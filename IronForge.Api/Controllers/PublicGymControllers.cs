using System.Globalization;
using System.Net;
using System.Text;
using IronForge.Api.Helpers;
using IronForge.Application.Entities;
using IronForge.Application.Execution;
using IronForge.Application.Gym.Billing;
using IronForge.Application.Gym.Members;
using IronForge.Application.Persistence;
using IronForge.Application.Gym.Billing.Payments;
using IronForge.Shared.Models.Gym;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace IronForge.Api.Controllers
{
    /// <summary>Joining a gym with its code, before having an account.</summary>
    [ApiController]
    [Route("api/join")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Anonymous)]
    public sealed class JoinController : ControllerBase
    {
        private readonly IGymJoinService _join;

        public JoinController(IGymJoinService join) => _join = join;

        [HttpPost("preview")]
        public async Task<IActionResult> Preview(GymJoinPreviewRequest request, CancellationToken cancellationToken) =>
            this.ToActionResult(await _join.PreviewAsync(request.JoinCode, cancellationToken));

        [HttpPost]
        public async Task<IActionResult> Join(GymJoinRequest request, CancellationToken cancellationToken) =>
            this.ToActionResult(await _join.JoinAsync(request, cancellationToken));
    }

    /// <summary>
    /// Online payment callbacks, all anonymous: the providers' webhooks, the
    /// page the payer's browser returns to, and the simulated test checkout.
    /// None of them trusts what it is sent: each asks the gym's provider
    /// (OnlinePaymentService) before an invoice is marked paid.
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    public sealed class PaymentsController : ControllerBase
    {
        private readonly IOnlinePaymentService _payments;
        private readonly ILogger<PaymentsController> _logger;

        public PaymentsController(IOnlinePaymentService payments, ILogger<PaymentsController> logger)
        {
            _payments = payments;
            _logger = logger;
        }

        /// <summary>
        /// A provider's notification for one gym: the URL the gym registers in
        /// its provider dashboard (Settings → Online payments shows it).
        /// </summary>
        [HttpPost("api/payments/webhooks/{provider}/{tenantId:int}")]
        [EnableRateLimiting(RateLimitPolicies.Webhook)]
        public async Task<IActionResult> Webhook(
            string provider,
            int tenantId,
            [FromServices] IExecutionContextAccessor executionContext,
            CancellationToken cancellationToken)
        {
            using var reader = new StreamReader(Request.Body, Encoding.UTF8);
            var body = await reader.ReadToEndAsync(cancellationToken);

            var headers = Request.Headers.ToDictionary(
                x => x.Key,
                x => x.Value.ToString(),
                StringComparer.OrdinalIgnoreCase);

            EnterTenant(executionContext, tenantId);

            var result = await _payments.HandleWebhookAsync(provider, tenantId, headers, body, cancellationToken);

            if (!result.Success)
            {
                _logger.LogWarning(
                    "{Provider} webhook for tenant {TenantId} not applied: {Reason}",
                    provider,
                    tenantId,
                    result.ErrorMessage);
            }

            // Provider unreachable (502): ask the provider to retry later.
            return result.Success
                ? Ok()
                : StatusCode(result.StatusCode == 502 ? 503 : result.StatusCode ?? 400);
        }

        /// <summary>
        /// Where the payer's browser lands after the provider's checkout. The
        /// provider's API is asked for the outcome, so payments are confirmed
        /// even where webhooks cannot reach the API (local development).
        /// </summary>
        [HttpGet("payments/return/{tenantId:int}/{invoiceId:int}")]
        [EnableRateLimiting(RateLimitPolicies.Anonymous)]
        public async Task<IActionResult> Return(
            int tenantId,
            int invoiceId,
            [FromServices] IExecutionContextAccessor executionContext,
            CancellationToken cancellationToken)
        {
            EnterTenant(executionContext, tenantId);

            var result = await _payments.ConfirmInvoiceAsync(tenantId, invoiceId, cancellationToken);

            if (result.Success && result.Data)
            {
                return Redirect("/payments/complete?status=success");
            }

            return result.Success
                ? Page(
                    "Payment processing",
                    "<p>We have not received the payment confirmation yet.</p>" +
                    "<p>If you completed the payment, it will show in the IronForge app in a moment: " +
                    "open your invoices and tap \"I've paid — check now\".</p>")
                : Redirect("/payments/complete?status=failed");
        }

        [HttpGet("payments/cancelled")]
        public IActionResult Cancelled() =>
            Page("Payment cancelled", "<p>No payment was made. You can close this page and return to the app.</p>");

        [HttpGet("payments/simulated")]
        [EnableRateLimiting(RateLimitPolicies.Anonymous)]
        public async Task<IActionResult> SimulatedCheckout(
            [FromQuery] int tenant,
            [FromQuery] int invoice,
            [FromQuery] string session,
            [FromServices] IExecutionContextAccessor executionContext,
            [FromServices] IRepository<Invoice> invoices,
            [FromServices] IOptions<PaymentOptions> options,
            CancellationToken cancellationToken)
        {
            if (!options.Value.AllowSimulated)
            {
                return NotFound();
            }

            EnterTenant(executionContext, tenant);

            var found = await FindSimulatedAsync(invoices, invoice, session, cancellationToken);

            if (found is null)
            {
                return Page("Payment link not valid", "<p>This payment link is invalid or has been replaced by a newer one.</p>");
            }

            if (!found.IsUnpaid)
            {
                return Page("Nothing to pay", $"<p>Invoice {Encode(found.Number)} is already {Encode(found.Status.ToString())}.</p>");
            }

            var amount = $"{found.Amount.ToString("N2", CultureInfo.InvariantCulture)} {found.Currency}";

            return Page(
                "Test payment",
                $"""
                <p class="muted">IronForge test checkout. No real payment is made.</p>
                <p><strong>{Encode(found.Number)}</strong><br>{Encode(found.Description)}</p>
                <p class="amount">{Encode(amount)}</p>
                <form method="post" action="/payments/simulated">
                  <input type="hidden" name="tenant" value="{tenant}">
                  <input type="hidden" name="invoice" value="{invoice}">
                  <input type="hidden" name="session" value="{Encode(session)}">
                  <button type="submit">Pay {Encode(amount)}</button>
                </form>
                <p><a href="/payments/cancelled">Cancel</a></p>
                """);
        }

        [HttpPost("payments/simulated")]
        [Consumes("application/x-www-form-urlencoded")]
        [EnableRateLimiting(RateLimitPolicies.Anonymous)]
        public async Task<IActionResult> ConfirmSimulatedCheckout(
            [FromForm] int tenant,
            [FromForm] int invoice,
            [FromForm] string session,
            [FromServices] IExecutionContextAccessor executionContext,
            [FromServices] IRepository<Invoice> invoices,
            [FromServices] IOptions<PaymentOptions> options,
            [FromServices] IPaymentConfirmationService confirmation,
            CancellationToken cancellationToken)
        {
            if (!options.Value.AllowSimulated)
            {
                return NotFound();
            }

            EnterTenant(executionContext, tenant);

            var found = await FindSimulatedAsync(invoices, invoice, session, cancellationToken);

            if (found is null)
            {
                return Redirect("/payments/complete?status=failed");
            }

            var result = await confirmation.ConfirmCheckoutAsync(
                PaymentProviderIds.Simulated,
                $"simevt_{session}",
                tenant,
                invoice,
                session,
                found.Amount,
                found.Currency,
                cancellationToken);

            return Redirect(result.Success
                ? "/payments/complete?status=success"
                : "/payments/complete?status=failed");
        }

        [HttpGet("payments/complete")]
        public IActionResult Complete([FromQuery] string? status) => status switch
        {
            "success" => Page("Payment received", "<p>Thank you! Your payment was successful.</p><p>You can close this page and return to the IronForge app.</p>"),
            "cancelled" => Page("Payment cancelled", "<p>No payment was made. You can close this page and return to the app.</p>"),
            _ => Page("Payment not completed", "<p>The payment could not be completed. Return to the app and try again.</p>")
        };

        private static Task<Invoice?> FindSimulatedAsync(
            IRepository<Invoice> invoices,
            int invoiceId,
            string session,
            CancellationToken cancellationToken) =>
            invoices.FirstOrDefaultAsync(
                x => x.Id == invoiceId &&
                     x.CheckoutSessionId == session &&
                     x.CheckoutProvider == PaymentProviderIds.Simulated,
                cancellationToken);

        private static void EnterTenant(IExecutionContextAccessor executionContext, int tenantId)
        {
            if (executionContext.Current is null)
            {
                executionContext.Set(new IronForge.Application.Execution.ExecutionContext
                {
                    TenantId = tenantId,
                    Source = ExecutionSource.Background
                });
            }
        }

        private static string Encode(string value) => WebUtility.HtmlEncode(value);

        private ContentResult Page(string title, string body) => new()
        {
            ContentType = "text/html; charset=utf-8",
            StatusCode = 200,
            Content =
                $$"""
                <!doctype html>
                <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
                <title>{{Encode(title)}} · IronForge</title>
                <style>
                  body{margin:0;font-family:Segoe UI,Arial,sans-serif;background:#f4f5f7;color:#1f2937;display:flex;min-height:100vh;align-items:center;justify-content:center;padding:16px}
                  main{background:#fff;border-radius:10px;padding:28px;max-width:420px;width:100%;box-shadow:0 4px 18px rgba(0,0,0,.08)}
                  h1{font-size:20px;margin:0 0 12px} .muted{color:#6b7280;font-size:13px} .amount{font-size:28px;font-weight:600}
                  button{background:#d9480f;color:#fff;border:0;border-radius:6px;padding:12px 18px;font-size:16px;width:100%;cursor:pointer}
                  a{color:#6b7280}
                </style></head>
                <body><main><h1>{{Encode(title)}}</h1>{{body}}</main></body></html>
                """
        };
    }
}

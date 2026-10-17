using System.Globalization;
using System.Net;
using System.Text;
using IronForge.Api.Helpers;
using IronForge.Application.Entities;
using IronForge.Application.Execution;
using IronForge.Application.Gym.Billing;
using IronForge.Application.Gym.Members;
using IronForge.Application.Persistence;
using IronForge.Infrastructure.Payments;
using IronForge.Shared.Models.Gym;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

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
    /// Payment callbacks: the Stripe webhook, the simulated checkout used
    /// without Stripe keys, and the page the payer lands on afterwards.
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    public sealed class PaymentsController : ControllerBase
    {
        private readonly IPaymentGateway _gateway;

        public PaymentsController(IPaymentGateway gateway) => _gateway = gateway;

        [HttpPost("api/payments/stripe/webhook")]
        [EnableRateLimiting(RateLimitPolicies.Webhook)]
        public async Task<IActionResult> StripeWebhook(
            [FromServices] StripeWebhookHandler handler,
            CancellationToken cancellationToken)
        {
            using var reader = new StreamReader(Request.Body, Encoding.UTF8);
            var payload = await reader.ReadToEndAsync(cancellationToken);

            var outcome = await handler.HandleAsync(
                payload,
                Request.Headers["Stripe-Signature"],
                cancellationToken);

            return outcome switch
            {
                WebhookOutcome.InvalidSignature => BadRequest(),
                WebhookOutcome.NotConfigured => NotFound(),
                _ => Ok()
            };
        }

        [HttpGet("payments/simulated")]
        [EnableRateLimiting(RateLimitPolicies.Anonymous)]
        public async Task<IActionResult> SimulatedCheckout(
            [FromQuery] int tenant,
            [FromQuery] int invoice,
            [FromQuery] string session,
            [FromServices] IExecutionContextAccessor executionContext,
            [FromServices] IRepository<Invoice> invoices,
            CancellationToken cancellationToken)
        {
            if (!_gateway.IsSimulated)
            {
                return NotFound();
            }

            EnterTenant(executionContext, tenant);

            var found = await invoices.FirstOrDefaultAsync(
                x => x.Id == invoice && x.CheckoutSessionId == session,
                cancellationToken);

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
                <p class="muted">IronForge simulated checkout. No real payment is made.</p>
                <p><strong>{Encode(found.Number)}</strong><br>{Encode(found.Description)}</p>
                <p class="amount">{Encode(amount)}</p>
                <form method="post" action="/payments/simulated">
                  <input type="hidden" name="tenant" value="{tenant}">
                  <input type="hidden" name="invoice" value="{invoice}">
                  <input type="hidden" name="session" value="{Encode(session)}">
                  <button type="submit">Pay {Encode(amount)}</button>
                </form>
                <p><a href="/payments/complete?status=cancelled">Cancel</a></p>
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
            [FromServices] IPaymentConfirmationService confirmation,
            CancellationToken cancellationToken)
        {
            if (!_gateway.IsSimulated)
            {
                return NotFound();
            }

            EnterTenant(executionContext, tenant);

            var found = await invoices.FirstOrDefaultAsync(
                x => x.Id == invoice && x.CheckoutSessionId == session,
                cancellationToken);

            if (found is null)
            {
                return Redirect("/payments/complete?status=failed");
            }

            var result = await confirmation.ConfirmCheckoutAsync(
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

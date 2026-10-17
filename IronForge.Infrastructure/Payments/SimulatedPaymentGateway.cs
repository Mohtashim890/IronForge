using IronForge.Application.Configurations;
using IronForge.Application.Gym.Billing;
using Microsoft.Extensions.Options;

namespace IronForge.Infrastructure.Payments;

/// <summary>
/// Stands in for Stripe when no keys are configured. The checkout URL opens
/// a page served by the API (PaymentsController) with a "Pay" button that
/// confirms the payment through the same path as a Stripe webhook. The
/// session id is a random value stored on the invoice, so the link cannot
/// be guessed for another invoice.
/// </summary>
public sealed class SimulatedPaymentGateway : IPaymentGateway
{
    private readonly AppUrlOptions _urls;

    public SimulatedPaymentGateway(IOptions<AppUrlOptions> urls)
    {
        _urls = urls.Value;
    }

    public string Provider => "simulated";

    public bool IsSimulated => true;

    public Task<CheckoutResult> CreateCheckoutAsync(
        CheckoutRequest request,
        CancellationToken cancellationToken = default)
    {
        var sessionId = $"sim_{Guid.NewGuid():N}";

        var url =
            $"{_urls.ApiBaseUrl.TrimEnd('/')}/payments/simulated" +
            $"?tenant={request.TenantId}&invoice={request.InvoiceId}&session={sessionId}";

        return Task.FromResult(new CheckoutResult(sessionId, url));
    }

    public Task<ConnectOnboardingLink> CreateConnectOnboardingLinkAsync(
        string? existingAccountId,
        string? email,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Stripe Connect is not available with simulated payments.");

    public Task<bool> IsChargesEnabledAsync(
        string accountId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}

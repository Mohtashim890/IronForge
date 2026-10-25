using IronForge.Application.Entities;
using IronForge.Application.Gym.Billing;

namespace IronForge.Infrastructure.Payments;

/// <summary>
/// No online payment provider is configured: members pay at the front desk
/// or by bank transfer, and staff record it. The member app hides "Pay now"
/// and the API refuses checkouts.
/// </summary>
public sealed class NoOnlinePaymentGateway : IPaymentGateway
{
    public string Provider => "none";

    public bool IsSimulated => false;

    public bool SupportsStripeConnect => false;

    public bool CanTakePayments(GymSettings settings) => false;

    public Task<CheckoutResult> CreateCheckoutAsync(
        CheckoutRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("No online payment provider is configured.");

    public Task<ConnectOnboardingLink> CreateConnectOnboardingLinkAsync(
        string? existingAccountId,
        string? email,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Stripe is not configured.");

    public Task<bool> IsChargesEnabledAsync(
        string accountId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}

using IronForge.Application.Configurations;
using IronForge.Application.Entities;
using IronForge.Application.Gym.Billing;
using IronForge.Infrastructure;
using IronForge.Infrastructure.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IronForge.Application.IntegrationTests.Gym;

/// <summary>Which online payment gateway runs, and when a gym may take payments through it.</summary>
public sealed class PaymentGatewayTests
{
    [Fact]
    public void WithoutAProvider_NoOnlinePayments_UnlessSimulatedIsAllowed()
    {
        Assert.IsType<NoOnlinePaymentGateway>(Resolve([]));

        Assert.IsType<SimulatedPaymentGateway>(Resolve(new() { ["Payments:AllowSimulated"] = "true" }));

        Assert.IsType<StripePaymentGateway>(Resolve(new()
        {
            ["Payments:AllowSimulated"] = "true",
            ["Stripe:SecretKey"] = "sk_test_123"
        }));
    }

    [Fact]
    public void NoProvider_TakesNoPayments()
    {
        var gateway = new NoOnlinePaymentGateway();

        Assert.False(gateway.CanTakePayments(new GymSettings()));
        Assert.False(gateway.SupportsStripeConnect);
        Assert.Equal("none", gateway.Provider);
    }

    [Fact]
    public void LiveStripe_NeedsTheGymsConnectedAccount_SoMoneyNeverLandsOnThePlatform()
    {
        var live = Stripe("sk_live_123");

        Assert.False(live.CanTakePayments(new GymSettings()));
        Assert.False(live.CanTakePayments(new GymSettings { StripeConnectedAccountId = "acct_1" }));
        Assert.True(live.CanTakePayments(new GymSettings { StripeConnectedAccountId = "acct_1", StripeChargesEnabled = true }));

        // Test keys (demos) may charge the platform's test account.
        Assert.True(Stripe("sk_test_123").CanTakePayments(new GymSettings()));
    }

    private static StripePaymentGateway Stripe(string key) =>
        new(Options.Create(new StripeOptions { SecretKey = key }), Options.Create(new AppUrlOptions()));

    private static IPaymentGateway Resolve(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddGymPlatform(configuration);

        return services.BuildServiceProvider().GetRequiredService<IPaymentGateway>();
    }
}

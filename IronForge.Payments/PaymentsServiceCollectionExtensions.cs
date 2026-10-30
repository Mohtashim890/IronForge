using IronForge.Application.Gym.Billing.Payments;
using IronForge.Payments.HitPay;
using IronForge.Payments.Safepay;
using IronForge.Payments.Simulated;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IronForge.Payments;

public static class PaymentsServiceCollectionExtensions
{
    private static readonly TimeSpan ProviderTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// The online payment providers and the services that use them. Gyms
    /// connect their own provider account; the keys are encrypted with
    /// Payments:EncryptionKey (JwtSettings:Key when empty).
    /// </summary>
    public static IServiceCollection AddIronForgePayments(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<PaymentOptions>()
            .Bind(configuration.GetSection(PaymentOptions.SectionName))
            .PostConfigure(options =>
            {
                if (string.IsNullOrWhiteSpace(options.EncryptionKey))
                {
                    options.EncryptionKey = configuration["JwtSettings:Key"] ?? "";
                }
            })
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Providers: typed HttpClients, so connections are pooled per provider.
        services.AddHttpClient<HitPayProvider>(http => http.Timeout = ProviderTimeout);
        services.AddHttpClient<SafepayProvider>(http => http.Timeout = ProviderTimeout);
        services.AddTransient<IPaymentProvider>(provider => provider.GetRequiredService<HitPayProvider>());
        services.AddTransient<IPaymentProvider>(provider => provider.GetRequiredService<SafepayProvider>());
        services.AddTransient<IPaymentProvider, SimulatedPaymentProvider>();

        services.AddScoped<IPaymentProviderCatalog, PaymentProviderCatalog>();
        services.AddSingleton<IPaymentCredentialProtector, PaymentCredentialProtector>();
        services.AddScoped<IPaymentConfirmationService, PaymentConfirmationService>();
        services.AddScoped<IOnlinePaymentService, OnlinePaymentService>();
        services.AddScoped<IGymPaymentAccountService, GymPaymentAccountService>();

        return services;
    }
}

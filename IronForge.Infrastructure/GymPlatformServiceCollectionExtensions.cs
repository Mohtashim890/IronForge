using IronForge.Application.Agents.Tools;
using IronForge.Application.Auth.Services;
using IronForge.Application.Configurations;
using IronForge.Application.Gym.AgentTools;
using IronForge.Application.Gym.Billing;
using IronForge.Application.Gym.CheckIns;
using IronForge.Application.Gym.Common;
using IronForge.Application.Gym.Fitness;
using IronForge.Application.Gym.Members;
using IronForge.Application.Gym.Notifications;
using IronForge.Application.Gym.Settings;
using IronForge.Infrastructure.Background;
using IronForge.Infrastructure.Email;
using IronForge.Infrastructure.Payments;
using IronForge.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IronForge.Infrastructure;

public static class GymPlatformServiceCollectionExtensions
{
    /// <summary>
    /// Gym domain services, their agent tools, payments and email. Used by
    /// every host that runs gym operations (API and MCP server).
    /// </summary>
    public static IServiceCollection AddGymPlatform(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);

        services
            .AddOptions<AppUrlOptions>()
            .Bind(configuration.GetSection(AppUrlOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Member pass secrets: a dedicated key, or the JWT signing key when none is set.
        services
            .AddOptions<CheckInOptions>()
            .Bind(configuration.GetSection(CheckInOptions.SectionName))
            .PostConfigure(options =>
            {
                if (string.IsNullOrWhiteSpace(options.PassSigningKey))
                {
                    options.PassSigningKey = configuration["JwtSettings:Key"] ?? "";
                }
            })
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.Configure<StripeOptions>(configuration.GetSection(StripeOptions.SectionName));
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));

        var stripe = configuration.GetSection(StripeOptions.SectionName).Get<StripeOptions>() ?? new();
        var email = configuration.GetSection(EmailOptions.SectionName).Get<EmailOptions>() ?? new();

        if (stripe.IsConfigured)
            services.AddSingleton<IPaymentGateway, StripePaymentGateway>();
        else
            services.AddSingleton<IPaymentGateway, SimulatedPaymentGateway>();

        if (email.IsConfigured)
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        else
            services.AddSingleton<IEmailSender, LoggingEmailSender>();

        // Domain services.
        services.AddScoped<IGymAccessService, GymAccessService>();
        services.AddScoped<IGymDirectory, GymDirectory>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IInvoiceNumberGenerator, InvoiceNumberGenerator>();
        services.AddScoped<IBillingEngine, BillingEngine>();
        services.AddScoped<IBillingService, BillingService>();
        services.AddScoped<IPaymentConfirmationService, PaymentConfirmationService>();
        services.AddScoped<IGymSettingsFactory, GymSettingsFactory>();
        services.AddScoped<IGymSettingsService, GymSettingsService>();
        services.AddScoped<IMembershipPlanService, MembershipPlanService>();
        services.AddScoped<IMemberService, MemberService>();
        services.AddScoped<ICheckInService, CheckInService>();
        services.AddSingleton<ICheckInKeys, CheckInKeys>();
        services.AddScoped<IMemberPassService, MemberPassService>();
        services.AddScoped<IMemberPhotoService, MemberPhotoService>();
        services.AddScoped<ICheckInDeviceService, CheckInDeviceService>();
        services.AddScoped<ICheckInDeviceDirectory, CheckInDeviceDirectory>();
        services.AddScoped<IKioskService, KioskService>();
        services.AddScoped<IGymDashboardService, GymDashboardService>();
        services.AddScoped<IGymJoinService, GymJoinService>();
        services.AddScoped<IRoutineService, RoutineService>();
        services.AddScoped<IProgressService, ProgressService>();
        services.AddScoped<IAnnouncementService, AnnouncementService>();
        services.AddScoped<IPasswordResetService, PasswordResetService>();
        services.AddScoped<StripeWebhookHandler>();

        // Agent tool catalogue: every gym tool declares its name, schema and default governance.
        foreach (var toolType in GymAgentToolCatalog.Types)
        {
            services.AddScoped(typeof(IAgentTool), toolType);
        }

        return services;
    }

    /// <summary>The billing job and the email outbox; registered by the API only.</summary>
    public static IServiceCollection AddGymBackgroundJobs(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<BillingJobOptions>(configuration.GetSection(BillingJobOptions.SectionName));
        services.AddHostedService<BillingBackgroundService>();
        services.AddHostedService<EmailOutboxProcessor>();

        return services;
    }
}

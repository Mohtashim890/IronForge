using IronForge.Shared.Configurations;
using IronForge.Shared.Services.Gym;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace IronForge.Shared.Services;

public static class IronForgeClientServiceCollectionExtensions
{
    public const string ApiClientName = "IronForgeApi";

    /// <summary>
    /// Registers every API client the front ends use. The host registers the
    /// named HttpClient <see cref="ApiClientName"/> (base address, primary
    /// handler), an <see cref="ITokenStore"/> and <see cref="ApiSettings"/>.
    ///
    /// IHttpClientFactory builds message handlers in its own DI scope, not the
    /// circuit's (web) or BlazorWebView's (MAUI). A JwtAuthorizationHandler
    /// added with AddHttpMessageHandler would get its own IAuthService, so
    /// token refreshes and forced logouts would not reach the UI. The handler
    /// is therefore created here, inside the UI scope, on top of the
    /// factory-managed primary handler.
    /// </summary>
    public static IServiceCollection AddIronForgeApiClients(
        this IServiceCollection services,
        ClientAppProfile profile)
    {
        services.AddSingleton(profile);

        services.AddScoped<IAuthService>(provider =>
            new AuthService(Anonymous(provider), provider.GetRequiredService<ITokenStore>()));

        services.AddScoped<IAgentWorkspaceService>(provider => new AgentWorkspaceService(Authorized(provider)));
        services.AddScoped<IAgentManagementService>(provider => new AgentManagementService(Authorized(provider)));
        services.AddScoped<IMcpClientManagementService>(provider => new McpClientManagementService(Authorized(provider)));
        services.AddScoped<IAgentMcpClientManagementService>(provider => new AgentMcpClientManagementService(Authorized(provider)));
        services.AddScoped<IAgentDelegationManagementService>(provider => new AgentDelegationManagementService(Authorized(provider)));
        services.AddScoped<IPlatformTenantService>(provider => new PlatformTenantService(Authorized(provider)));
        services.AddScoped<ITenantMembersService>(provider => new TenantMembersService(Authorized(provider)));
        services.AddScoped<IApprovalQueueClient>(provider => new ApprovalQueueClient(Authorized(provider)));

        services.AddScoped(provider => new GymApiClient(Authorized(provider)));
        services.AddScoped(provider => new MemberApiClient(Authorized(provider)));
        services.AddScoped(provider => new NotificationsApiClient(Authorized(provider)));
        services.AddScoped(provider => new PlatformApiClient(Authorized(provider)));
        services.AddScoped(provider => new AccountApiClient(Authorized(provider)));

        // Not signed in yet: plain clients without the JWT handler.
        services.AddScoped<IInvitationService>(provider => new InvitationService(Anonymous(provider)));
        services.AddScoped(provider => new PublicApiClient(Anonymous(provider)));

        // Check-in devices authenticate with their own key, never a user's JWT.
        services.AddScoped(provider => new KioskApiClient(Anonymous(provider)));

        // The member app replaces this with secure storage, so the pass works offline.
        services.TryAddScoped<IMemberPassStore, InMemoryMemberPassStore>();
        services.TryAddScoped<IPhoneUnlock, NoPhoneUnlock>();

        // Hosts may register their own (the mobile app opens the system browser).
        services.TryAddScoped<IExternalLinkOpener, NavigationLinkOpener>();

        services.AddScoped<JwtAuthenticationStateProvider>();
        services.AddScoped<AuthenticationStateProvider>(provider =>
            provider.GetRequiredService<JwtAuthenticationStateProvider>());

        return services;
    }

    private static HttpClient Anonymous(IServiceProvider provider) =>
        provider.GetRequiredService<IHttpClientFactory>().CreateClient(ApiClientName);

    private static HttpClient Authorized(IServiceProvider provider)
    {
        var settings = provider.GetRequiredService<IOptions<ApiSettings>>().Value;

        var handler = new JwtAuthorizationHandler(
            provider.GetRequiredService<ITokenStore>(),
            provider.GetRequiredService<IAuthService>())
        {
            InnerHandler = provider
                .GetRequiredService<IHttpMessageHandlerFactory>()
                .CreateHandler(ApiClientName)
        };

        // disposeHandler: false - the inner handler's lifetime is owned by the factory.
        return new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = new Uri(settings.BaseUrl)
        };
    }
}

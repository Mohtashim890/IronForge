using IronForge.Services;
using IronForge.Shared.Authorization;
using IronForge.Shared.Configurations;
using IronForge.Shared.Models;
using IronForge.Shared.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IronForge
{
    public static class MauiProgram
    {
        private const string ApiClientName = "IronForgeApi";

        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            builder.Services.AddMauiBlazorWebView();

            var apiSettings = new ApiSettings
            {
            #if ANDROID
                BaseUrl = "https://10.0.2.2:7226/"
            #else
                BaseUrl = "https://localhost:7226/"
            #endif
            };

            builder.Services.AddSingleton(apiSettings);

            // Tokens persist in the platform's secure storage (Keychain/Keystore/DPAPI).
            builder.Services.AddSingleton<ITokenStore, SecureTokenStore>();

            // One named client for the API. It only configures the primary
            // (socket-level) handler; the JWT handler is added per scope below.
            builder.Services.AddHttpClient(ApiClientName, (serviceProvider, client) =>
            {
                var settings =
                    serviceProvider.GetRequiredService<ApiSettings>();
                client.BaseAddress = new Uri(settings.BaseUrl);
            })
            .ConfigurePrimaryHttpMessageHandler(() =>
            {
                var handler = new HttpClientHandler();

#if DEBUG
                handler.ServerCertificateCustomValidationCallback =
                    (message, cert, chain, errors) =>
                    {
                        if (cert != null && cert.Issuer.Equals("CN=localhost"))
                            return true;

                        return errors == System.Net.Security.SslPolicyErrors.None;
                    };
#endif

                return handler;
            });

            builder.Services.AddScoped<IAuthService>(serviceProvider =>
            {
                var httpClient =
                    serviceProvider
                        .GetRequiredService<IHttpClientFactory>()
                        .CreateClient(ApiClientName);

                var tokenStore =
                    serviceProvider.GetRequiredService<ITokenStore>();

                return new AuthService(
                    httpClient,
                    tokenStore);
            });

            // IHttpClientFactory builds message handlers in its own DI scope, not the
            // BlazorWebView's. A JwtAuthorizationHandler added with AddHttpMessageHandler
            // would get its own IAuthService instance, so token refreshes and forced
            // logouts would not reach JwtAuthenticationStateProvider (the UI keeps a
            // stale login state) and the two instances would not share a refresh lock.
            // So the handler is created here, inside the BlazorWebView scope, on top
            // of the factory-managed primary handler.
            builder.Services.AddScoped<IProductService>(serviceProvider =>
            {
                var settings =
                    serviceProvider.GetRequiredService<ApiSettings>();

                var authorizationHandler =
                    new JwtAuthorizationHandler(
                        serviceProvider.GetRequiredService<ITokenStore>(),
                        serviceProvider.GetRequiredService<IAuthService>())
                    {
                        InnerHandler =
                            serviceProvider
                                .GetRequiredService<IHttpMessageHandlerFactory>()
                                .CreateHandler(ApiClientName)
                    };

                // disposeHandler: false - the inner handler's lifetime is owned by the factory.
                var httpClient =
                    new HttpClient(authorizationHandler, disposeHandler: false)
                    {
                        BaseAddress = new Uri(settings.BaseUrl)
                    };

                return new ProductService(httpClient);
            });

            builder.Services.AddScoped<IAgentWorkspaceService>(serviceProvider =>
            {
                var settings =
                    serviceProvider.GetRequiredService<ApiSettings>();

                var authorizationHandler =
                    new JwtAuthorizationHandler(
                        serviceProvider.GetRequiredService<ITokenStore>(),
                        serviceProvider.GetRequiredService<IAuthService>())
                    {
                        InnerHandler =
                            serviceProvider
                                .GetRequiredService<IHttpMessageHandlerFactory>()
                                .CreateHandler(ApiClientName)
                    };

                // disposeHandler: false - the inner handler's lifetime is owned by the factory.
                var httpClient =
                    new HttpClient(authorizationHandler, disposeHandler: false)
                    {
                        BaseAddress = new Uri(settings.BaseUrl)
                    };

                return new AgentWorkspaceService(httpClient);
            });

            builder.Services.AddScoped<IAgentManagementService>(serviceProvider =>
            {
                var settings =
                    serviceProvider.GetRequiredService<ApiSettings>();

                var authorizationHandler =
                    new JwtAuthorizationHandler(
                        serviceProvider.GetRequiredService<ITokenStore>(),
                        serviceProvider.GetRequiredService<IAuthService>())
                    {
                        InnerHandler =
                            serviceProvider
                                .GetRequiredService<IHttpMessageHandlerFactory>()
                                .CreateHandler(ApiClientName)
                    };

                // disposeHandler: false - the inner handler's lifetime is owned by the factory.
                var httpClient =
                    new HttpClient(authorizationHandler, disposeHandler: false)
                    {
                        BaseAddress = new Uri(settings.BaseUrl)
                    };

                return new AgentManagementService(httpClient);
            });

            builder.Services.AddScoped<IMcpClientManagementService>(serviceProvider =>
            {
                var settings =
                    serviceProvider
                        .GetRequiredService<IOptions<ApiSettings>>()
                        .Value;

                var authorizationHandler =
                    new JwtAuthorizationHandler(
                        serviceProvider.GetRequiredService<ITokenStore>(),
                        serviceProvider.GetRequiredService<IAuthService>())
                    {
                        InnerHandler =
                            serviceProvider
                                .GetRequiredService<IHttpMessageHandlerFactory>()
                                .CreateHandler(ApiClientName)
                    };

                var httpClient =
                    new HttpClient(
                        authorizationHandler,
                        disposeHandler: false)
                    {
                        BaseAddress = new Uri(settings.BaseUrl)
                    };

                return new McpClientManagementService(httpClient);
            });


            builder.Services.AddScoped<IAgentMcpClientManagementService>(serviceProvider =>
            {
                var settings =
                    serviceProvider
                        .GetRequiredService<IOptions<ApiSettings>>()
                        .Value;

                var authorizationHandler =
                    new JwtAuthorizationHandler(
                        serviceProvider.GetRequiredService<ITokenStore>(),
                        serviceProvider.GetRequiredService<IAuthService>())
                    {
                        InnerHandler =
                            serviceProvider
                                .GetRequiredService<IHttpMessageHandlerFactory>()
                                .CreateHandler(ApiClientName)
                    };

                var httpClient =
                    new HttpClient(
                        authorizationHandler,
                        disposeHandler: false)
                    {
                        BaseAddress = new Uri(settings.BaseUrl)
                    };

                return new AgentMcpClientManagementService(httpClient);
            });

            builder.Services.AddScoped<IAgentDelegationManagementService>(serviceProvider =>
            {
                var settings =
                    serviceProvider.GetRequiredService<ApiSettings>();

                var authorizationHandler =
                    new JwtAuthorizationHandler(
                        serviceProvider.GetRequiredService<ITokenStore>(),
                        serviceProvider.GetRequiredService<IAuthService>())
                    {
                        InnerHandler =
                            serviceProvider
                                .GetRequiredService<IHttpMessageHandlerFactory>()
                                .CreateHandler(ApiClientName)
                    };

                // disposeHandler: false - the inner handler's lifetime is owned by the factory.
                var httpClient =
                    new HttpClient(authorizationHandler, disposeHandler: false)
                    {
                        BaseAddress = new Uri(settings.BaseUrl)
                    };

                return new AgentDelegationManagementService(httpClient);
            });

            builder.Services.AddIronForgeMAUIAuthorization();
            builder.Services.AddCascadingAuthenticationState();
            builder.Services.AddScoped<AuthenticationStateProvider, JwtAuthenticationStateProvider>();

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
    		builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}

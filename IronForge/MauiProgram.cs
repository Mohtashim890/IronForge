using System.Text.Json;
using IronForge.Services;
using IronForge.Shared.Configurations;
using IronForge.Shared.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IronForge
{
    public static class MauiProgram
    {
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

            var apiBaseUrl = ResolveApiBaseUrl();

            builder.Services.Configure<ApiSettings>(options =>
            {
                options.BaseUrl = apiBaseUrl;
            });

            builder.Services.AddSingleton(serviceProvider =>
                serviceProvider
                    .GetRequiredService<IOptions<ApiSettings>>()
                    .Value);

            // Tokens persist in the platform's secure storage (Keychain/Keystore/DPAPI).
            builder.Services.AddSingleton<ITokenStore, SecureTokenStore>();

            // One named client for the API. It only configures the primary
            // (socket-level) handler; the JWT handler is added per scope by
            // AddIronForgeApiClients.
            builder.Services.AddHttpClient(
                IronForgeClientServiceCollectionExtensions.ApiClientName,
                (serviceProvider, client) =>
                {
                    var settings = serviceProvider.GetRequiredService<ApiSettings>();
                    client.BaseAddress = new Uri(settings.BaseUrl);
                })
            .ConfigurePrimaryHttpMessageHandler(() =>
            {
                var handler = new HttpClientHandler();

                // The ASP.NET Core development certificate (CN=localhost) is
                // trusted only for a local API: the emulator's host alias or
                // this machine. Every other host gets normal validation.
                handler.ServerCertificateCustomValidationCallback =
                    (message, cert, chain, errors) =>
                        errors == System.Net.Security.SslPolicyErrors.None ||
                        (cert != null &&
                         cert.Issuer.Equals("CN=localhost") &&
                         IsLocalHost(message.RequestUri?.Host));

                return handler;
            });

            // Payment checkout opens in the system browser (before the shared
            // registration, which only adds a default when none exists).
            builder.Services.AddScoped<IExternalLinkOpener, MauiLinkOpener>();

            // The member pass works offline from secure storage.
            builder.Services.AddSingleton<IMemberPassStore, SecureMemberPassStore>();

            // Fingerprint, face or screen lock before the pass shows (against handing the phone over).
            builder.Services.AddSingleton<IPhoneUnlock, MauiPhoneUnlock>();

            // The member app: gym members only.
            builder.Services.AddIronForgeApiClients(new ClientAppProfile
            {
                Audience = ClientAudience.Member,
                AppName = "Your gym in your pocket",
                HomeRoute = "/home"
            });

            builder.Services.AddIronForgeMAUIAuthorization();
            builder.Services.AddCascadingAuthenticationState();

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }

        /// <summary>
        /// Debug builds talk to the local API. Release builds read
        /// Resources/Raw/appsettings.json (ApiSettings:BaseUrl), so a Release
        /// build can also be pointed at a local API for testing. On the Android
        /// emulator, "localhost" means the emulator itself, so it is mapped to the
        /// host machine's alias 10.0.2.2. A physical device keeps "localhost" and
        /// reaches the host through "adb reverse tcp:7226 tcp:7226".
        /// </summary>
        private static string ResolveApiBaseUrl()
        {
#if DEBUG
            var url = "https://localhost:7226/";
#else
            var url = ReadConfiguredBaseUrl() ?? "https://api.mohtashim.dev/";
#endif

#if ANDROID
            if (DeviceInfo.Current.DeviceType == DeviceType.Virtual)
                url = url.Replace("://localhost", "://10.0.2.2").Replace("://127.0.0.1", "://10.0.2.2");
#endif

            return url.EndsWith('/') ? url : url + "/";
        }

        private static string? ReadConfiguredBaseUrl()
        {
            try
            {
                using var stream = FileSystem.OpenAppPackageFileAsync("appsettings.json").GetAwaiter().GetResult();
                using var document = JsonDocument.Parse(stream);

                return document.RootElement.TryGetProperty("ApiSettings", out var api) &&
                       api.TryGetProperty("BaseUrl", out var url) &&
                       url.GetString() is { Length: > 0 } value
                    ? value
                    : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool IsLocalHost(string? host) =>
            host is "localhost" or "127.0.0.1" or "10.0.2.2";
    }
}

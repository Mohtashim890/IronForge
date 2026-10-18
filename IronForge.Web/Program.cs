using IronForge.Shared.Configurations;
using IronForge.Shared.Services;
using IronForge.Web.Components;
using IronForge.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services
    .AddOptions<ApiSettings>()
    .Bind(builder.Configuration.GetSection(ApiSettings.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// One named client for the API; the JWT handler is added per circuit by
// AddIronForgeApiClients.
builder.Services.AddHttpClient(
    IronForgeClientServiceCollectionExtensions.ApiClientName,
    (serviceProvider, client) =>
    {
        var settings = serviceProvider.GetRequiredService<IOptions<ApiSettings>>().Value;
        client.BaseAddress = new Uri(settings.BaseUrl);
    });

// Token storage: browser localStorage, one store per circuit (scoped).
// ProtectedLocalStorage itself is registered by AddInteractiveServerComponents.
builder.Services.AddScoped<ITokenStore, WebTokenStore>();

// A check-in device (/kiosk) keeps its device key in the same protected storage.
builder.Services.AddScoped<IKioskKeyStore, WebKioskKeyStore>();

// The gym staff dashboard: Admins and Trainers.
builder.Services.AddIronForgeApiClients(new ClientAppProfile
{
    Audience = ClientAudience.Staff,
    AppName = "Gym Dashboard",
    HomeRoute = "/dashboard"
});

builder.Services.AddIronForgeWebAuthorization();
builder.Services.AddCascadingAuthenticationState();

// Tokens in localStorage are encrypted with Data Protection. Persisting the
// key ring keeps users signed in across restarts and deployments.
var keysPath = builder.Configuration["DataProtection:KeysPath"];

if (!string.IsNullOrWhiteSpace(keysPath))
{
    builder.Services
        .AddDataProtection()
        .SetApplicationName("IronForge.Web")
        .PersistKeysToFileSystem(new DirectoryInfo(keysPath));
}

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto;
    options.KnownProxies.Add(
      IPAddress.Parse(builder.Configuration["ReverseProxy:TrustedProxyIp"] ?? "172.30.0.10"));
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseForwardedHeaders();
app.UseHttpsRedirection();

app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();

// The JWT lives in the browser's localStorage, so it is never sent with the HTTP
// request that loads a page. Authorization happens inside the interactive circuit
// (AuthorizeRouteView + JwtAuthenticationStateProvider), so the page endpoints
// themselves must allow anonymous HTTP requests. Otherwise refreshing
// /dashboard would be rejected before Blazor starts.
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(
        typeof(IronForge.Shared.Pages.Login).Assembly,
        typeof(IronForge.UI.Admin.Pages.Dashboard).Assembly)
    .AllowAnonymous();

app.Run();

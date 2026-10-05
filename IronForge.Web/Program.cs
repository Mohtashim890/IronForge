using IronForge.Shared.Authorization;
using IronForge.Shared.Configurations;
using IronForge.Shared.Services;
using IronForge.Web.Components;
using IronForge.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services
    .AddOptions<ApiSettings>()
    .Bind(
        builder.Configuration.GetSection(
            ApiSettings.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

const string ApiClientName = "IronForgeApi";

builder.Services.AddHttpClient(ApiClientName,
    (serviceProvider, client) =>
    {
        var settings =
            serviceProvider
                .GetRequiredService<IOptions<ApiSettings>>()
                .Value;

        client.BaseAddress =
            new Uri(settings.BaseUrl);
    });

// Token storage: browser localStorage, one store per circuit (scoped).
// ProtectedLocalStorage itself is registered by AddInteractiveServerComponents.
builder.Services.AddScoped<ITokenStore, WebTokenStore>();

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
// circuit's. A JwtAuthorizationHandler added with AddHttpMessageHandler would
// get a different ITokenStore/IJSRuntime than the component calling it, and JS
// interop would fail. So the handler is created here, inside the circuit scope,
// on top of the factory-managed primary handler.
builder.Services.AddScoped<IProductService>(serviceProvider =>
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

    return new AgentWorkspaceService(httpClient);
});

builder.Services.AddScoped<IAgentManagementService>(serviceProvider =>
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


builder.Services.AddScoped<IAgentDelegationManagementService>(
    serviceProvider =>
    {
        var settings =
            serviceProvider
                .GetRequiredService<
                    IOptions<ApiSettings>>()
                .Value;

        var authorizationHandler =
            new JwtAuthorizationHandler(
                serviceProvider.GetRequiredService<ITokenStore>(),
                serviceProvider.GetRequiredService<IAuthService>())
            {
                InnerHandler =
                    serviceProvider
                        .GetRequiredService<
                            IHttpMessageHandlerFactory>()
                        .CreateHandler(ApiClientName)
            };

        var httpClient =
            new HttpClient(
                authorizationHandler,
                disposeHandler: false)
            {
                BaseAddress =
                    new Uri(settings.BaseUrl)
            };

        return new AgentDelegationManagementService(
            httpClient);
    });

builder.Services.AddIronForgeWebAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<JwtAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(
    serviceProvider =>
        serviceProvider.GetRequiredService<
            JwtAuthenticationStateProvider>());

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
    .AddAdditionalAssemblies(typeof(IronForge.Shared.Pages.Products).Assembly)
    .AllowAnonymous();

app.Run();

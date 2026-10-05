using IronForge.Application.Agents.Approvals;
using IronForge.Application.Agents.Authorization;
using IronForge.Application.Agents.Delegations;
using IronForge.Application.Auditing.Services;
using IronForge.Application.Auth.Services;
using IronForge.Application.Configurations;
using IronForge.Application.Entities;
using IronForge.Application.Execution;
using IronForge.Application.Observablity.Metrics;
using IronForge.Application.Observablity.Tracing;
using IronForge.Application.Persistence;
using IronForge.Application.Products.Services;
using IronForge.Application.Products.Tools.Products;
using IronForge.Application.Tenants.Services;
using IronForge.Infrastructure.Data;
using IronForge.Infrastructure.Persistence;
using IronForge.Mcp.Authorization;
using IronForge.Mcp.Identity;
using IronForge.Shared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Net;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpContextAccessor();

builder.Services
    .AddOptions<JwtSettings>()
    .Bind(
        builder.Configuration.GetSection(
            JwtSettings.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();


builder.Services
    .AddOptions<AgentCredentialSettings>()
    .Bind(
        builder.Configuration.GetSection(
            AgentCredentialSettings.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();


builder.Services
    .AddMcpServer()
    .WithHttpTransport(options =>
    {
        options.SessionMode =
            HttpServerSessionMode.Stateless;
    })
    .AddAuthorizationFilters()
    .WithRequestFilters(requestFilters =>
    {
        requestFilters.AddCallToolFilter(next => async (context, cancellationToken) =>
        {
            var authorization =
                context.Services
                    ?.GetRequiredService<IMcpAuthorizationService>();

            if (authorization is null)
            {
                return new CallToolResult
                {
                    IsError = true,
                    Content =
                    [
                        new TextContentBlock
                        {
                            Text = "MCP authorization service is unavailable."
                        }
                    ]
                };
            }

            var result =
                await authorization.AuthorizeToolAsync(
                    context.Params.Name,
                    cancellationToken);

            if (!result.Allowed)
            {
                return new CallToolResult
                {
                    IsError = true,
                    Content =
                    [
                        new TextContentBlock
                        {
                            Text = result.Reason
                        }
                    ]
                };
            }

            return await next(context, cancellationToken);
        });
    })
    .WithToolsFromAssembly();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<IronForgeDbContext>(
    options =>
        options.UseNpgsql(connectionString));

builder.Services.AddScoped(
    typeof(IRepository<>),
    typeof(EfRepository<>));

builder.Services
    .AddAuthentication(
        JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IServiceProvider>((options, serviceProvider) =>
    {
        var jwtSettings = serviceProvider
            .GetRequiredService<IOptions<JwtSettings>>()
            .Value;

        var agentCredentialSettings = serviceProvider
            .GetRequiredService<IOptions<AgentCredentialSettings>>()
            .Value;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            ValidIssuer = agentCredentialSettings.Issuer,
            ValidAudience = agentCredentialSettings.Audience,

            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSettings.Key))
        };
    });


builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddAuthorization();

builder.Services.AddScoped<ITenantService, TenantService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<ITenantAuthorizationService, TenantAuthorizationService>();
builder.Services.AddScoped<IUserAuthorizationService, UserAuthorizationService>();
builder.Services.AddScoped<IProductAuthorizationService, ProductAuthorizationService>();
builder.Services.AddScoped<IAgentAuthorizationService, AgentAuthorizationService>();
builder.Services.AddScoped<IEffectiveAgentAuthorizationService, EffectiveAgentAuthorizationService>();
builder.Services.AddScoped<IAgentDelegationService, AgentDelegationService>();
builder.Services.AddScoped<IAgentDelegationCredentialService, AgentDelegationCredentialService>();
builder.Services.AddScoped<IAgentMcpClientAuthorizationService, AgentMcpClientAuthorizationService>();
builder.Services.AddScoped<IMcpAuthorizationService, McpAuthorizationService>();
builder.Services.AddSingleton<IMcpToolPermissionRegistry, McpToolPermissionRegistry>();
builder.Services.AddScoped<IAgentPermissionRegistry, DatabaseAgentPermissionRegistry>();
builder.Services.AddScoped<IExecutionContextAccessor, ExecutionContextAccessor>();
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();

builder.Services.AddScoped<TelemetryContext>();
builder.Services.AddSingleton(IronForgeActivitySource.Source);
builder.Services.AddScoped<ITracingService, TracingService>();
builder.Services.AddSingleton(IronForgeMeter.Meter);
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IAuditQueryService, AuditQueryService>();

builder.Services.AddScoped<IAgentApprovalStore, DBAgentApprovalStore>();
builder.Services.AddScoped<IAgentDelegationStore, DBAgentDelegationStore>();
builder.Services.AddScoped<IAgentDelegationStore, DBAgentDelegationStore>();

builder.Services.AddScoped<IGetProductsTool, GetProductsTool>();
builder.Services.AddScoped<IGetProductTool, GetProductTool>();
builder.Services.AddScoped<ICreateProductTool, CreateProductTool>();
builder.Services.AddScoped<IUpdateProductTool, UpdateProductTool>();
builder.Services.AddScoped<IDeleteProductTool, DeleteProductTool>();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto;
    options.KnownProxies.Add(
       IPAddress.Parse(builder.Configuration["ReverseProxy:TrustedProxyIp"] ?? "172.30.0.10"));
});
builder.Services
    .AddHealthChecks()
    .AddDbContextCheck<IronForgeDbContext>(
        name: "database",
        tags: ["ready"]);


var app = builder.Build();
app.MapHealthChecks(
    "/health/live",
    new HealthCheckOptions
    {
        Predicate = _ => false
    });

app.MapHealthChecks(
    "/health/ready",
    new HealthCheckOptions
    {
        Predicate = check =>
            check.Tags.Contains("ready")
    });

app.UseForwardedHeaders();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<McpIdentityMiddleware>();

app.MapMcp()
    .RequireAuthorization();

app.Run();
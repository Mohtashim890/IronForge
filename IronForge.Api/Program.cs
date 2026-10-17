using IronForge.Api.Helpers;
using IronForge.Application.Agents;
using IronForge.Application.Agents.Approvals;
using IronForge.Application.Agents.Authorization;
using IronForge.Application.Agents.Delegations;
using IronForge.Application.Agents.Governance;
using IronForge.Application.Agents.Governance.Policies;
using IronForge.Application.Agents.Memory.Context;
using IronForge.Application.Agents.Memory.ConversationSession;
using IronForge.Application.Agents.Memory.Summarization;
using IronForge.Application.Agents.Memory.Tools;
using IronForge.Application.Agents.ModelProfiles;
using IronForge.Application.Agents.Usage;
using IronForge.Application.Auditing.AgentTools;
using IronForge.Application.Agents.Runtime;
using IronForge.Application.Agents.Services;
using IronForge.Application.Agents.Tools;
using IronForge.Application.Auditing.Services;
using IronForge.Application.Auth.Services;
using IronForge.Application.Configurations;
using IronForge.Application.Entities;
using IronForge.Application.Execution;
using IronForge.Application.McpClients.Services;
using IronForge.Application.Observablity.Metrics;
using IronForge.Application.Observablity.Tracing;
using IronForge.Application.Persistence;
using IronForge.Application.Tenants.Services;
using IronForge.Application.Tenants.Subscriptions;
using IronForge.Application.Tenants.Invitations;
using IronForge.Infrastructure.AI;
using IronForge.Infrastructure;
using IronForge.Infrastructure.Data;
using IronForge.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddHttpContextAccessor();

builder.Services
    .AddOptions<OpenTelemetryOptions>()
    .Bind(
        builder.Configuration.GetSection(
            OpenTelemetryOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

var otlpSettings =
    builder.Configuration
        .GetSection(OpenTelemetryOptions.SectionName)
        .Get<OpenTelemetryOptions>()!;


var otlpEndpoint = otlpSettings.EndPoint.TrimEnd('/');
if (!Enum.TryParse<OtlpExportProtocol>(
        otlpSettings.Protocol,
        ignoreCase: true,
        out var otlpExportProtocol))
{
    throw new InvalidOperationException(
        $"Unsupported OpenTelemetry OTLP protocol: {otlpSettings.Protocol}");
}

builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource =>
    {
        resource.AddService(
            serviceName: IronForgeActivitySource.Name,
            serviceVersion: IronForgeActivitySource.Version);
    })
    .WithTracing(tracing =>
    {
        tracing.AddSource(IronForgeActivitySource.Name);
        tracing.AddOtlpExporter(options =>
        {
            options.Protocol = otlpExportProtocol;
            options.Endpoint =
                new Uri($"{otlpEndpoint}{otlpSettings.TracesUrlSuffix}");
            options.Headers = otlpSettings.Headers;
        });
    })
    .WithMetrics(metrics =>
    {
        metrics.AddMeter(IronForgeMeter.Name);
        metrics.AddOtlpExporter(options =>
        {
            options.Protocol = otlpExportProtocol;
            options.Endpoint =
                new Uri($"{otlpEndpoint}{otlpSettings.MetricsUrlSuffix}");
            options.Headers = otlpSettings.Headers;
        });
    });

builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;

    logging.AddOtlpExporter(options =>
    {
        options.Protocol = otlpExportProtocol;
        options.Endpoint =
            new Uri($"{otlpEndpoint}{otlpSettings.LogsUrlSuffix}");
        options.Headers = otlpSettings.Headers;
    });
});


var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<IronForgeDbContext>(
    options =>
        options.UseNpgsql(connectionString));

builder.Services.AddScoped(
    typeof(IRepository<>),
    typeof(EfRepository<>));

builder.Services.AddProblemDetails();

builder.Services
    .AddOptions<JwtSettings>()
    .Bind(
        builder.Configuration.GetSection(
            JwtSettings.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<AISettings>()
    .Bind(
        builder.Configuration.GetSection(
            AISettings.SectionName))
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
    .AddOptions<InvitationOptions>()
    .Bind(
        builder.Configuration.GetSection(
            InvitationOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<ConversationContextOptions>()
    .Bind(
        builder.Configuration.GetSection(
            ConversationContextOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddAuthentication(
        JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtSettings>>((options, jwtSettingsOptions) =>
    {
        var jwtSettings = jwtSettingsOptions.Value;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,

            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSettings.Key))
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddSingleton<IValidateOptions<ConversationContextOptions>,ConversationContextOptionsValidator>();
builder.Services.AddScoped<ITenantService, TenantService>();
builder.Services.AddScoped<ITenantEntitlementService, TenantEntitlementService>();
builder.Services.AddScoped<ITenantProvisioningService, TenantProvisioningService>();
builder.Services.AddScoped<IAgentTemplateRolloutService, AgentTemplateRolloutService>();
builder.Services.AddScoped<ITemplateAgentStore, TemplateAgentStore>();
builder.Services.AddScoped<ITenantMemberService, TenantMemberService>();
builder.Services.AddScoped<ITenantInvitationService, TenantInvitationService>();
builder.Services.AddScoped<ITenantInvitationStore, TenantInvitationStore>();
builder.Services.AddScoped<IUnitOfWork, EfUnitOfWork>();
builder.Services.AddScoped<IAgentManagementService,AgentManagementService>();
builder.Services.AddScoped<IMcpClientManagementService,McpClientManagementService>();
builder.Services.AddScoped<IAgentMcpClientManagementService,AgentMcpClientManagementService>();
builder.Services.AddScoped<IAgentDelegationService,AgentDelegationService>();


builder.Services.AddScoped<ITenantAuthorizationService, TenantAuthorizationService>();
builder.Services.AddScoped<IUserAuthorizationService, UserAuthorizationService>();
builder.Services.AddScoped<IAgentManagementAuthorizationService, AgentManagementAuthorizationService>();
builder.Services.AddScoped<IAgentMcpClientAuthorizationService,AgentMcpClientAuthorizationService>();
builder.Services.AddScoped<IAgentAuthorizationService, AgentAuthorizationService>();
builder.Services.AddScoped<IEffectiveAgentAuthorizationService, EffectiveAgentAuthorizationService>();
builder.Services.AddScoped<IAgentDelegationCredentialService, AgentDelegationCredentialService>();
// Derived from tool bindings; resolved lazily to avoid a cycle through the tools.
builder.Services.AddScoped<IAgentPermissionRegistry>(services =>
    new DatabaseAgentPermissionRegistry(
        () => services.GetRequiredService<IAgentDefinitionProvider>(),
        () => services.GetRequiredService<IAgentToolRegistry>()));
builder.Services.AddScoped<IExecutionContextAccessor, ExecutionContextAccessor>();
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();


builder.Services.AddScoped<ConversationMessageMapper>();
builder.Services.AddScoped<IConversationService, ConversationService>();
builder.Services.AddScoped<IConversationAccessService, ConversationAccessService>();
builder.Services.AddScoped<IConversationToolContentMapper, ConversationToolContentMapper>();
builder.Services.AddScoped<IAgentConversationSessionAccessor, AgentConversationSessionAccessor>();
builder.Services.AddScoped<IConversationToolExecutionService,ConversationToolExecutionService>();
builder.Services.AddScoped<IConversationTokenEstimator, ConversationTokenEstimator>();
builder.Services.AddScoped<IConversationContextService, ConversationContextService>();
builder.Services.AddScoped<IConversationContextCompactor, ConversationContextCompactor>();
builder.Services.AddScoped<IConversationSummaryService, ConversationSummaryService>();
builder.Services.AddScoped<IAgentConversationSummarizer, AgentConversationSummarizer>();
builder.Services.AddScoped<IConversationSummaryTranscriptBuilder, ConversationSummaryTranscriptBuilder>();
builder.Services.AddScoped<IConversationContextUnitBuilder, ConversationContextUnitBuilder>();
builder.Services.AddScoped<IConversationSummaryPolicy, ConversationSummaryPolicy>();
builder.Services.AddScoped<IConversationSummaryCoordinator, ConversationSummaryCoordinator>();
builder.Services.AddScoped<IApprovedToolExecutionService, ApprovedToolExecutionService>();
builder.Services.AddScoped<IApprovalExecutionAuthorizationService, ApprovalExecutionAuthorizationService>();
builder.Services.AddScoped<IApprovalQueueService, ApprovalQueueService>();

builder.Services.AddScoped<TelemetryContext>();
builder.Services.AddSingleton(IronForgeActivitySource.Source);
builder.Services.AddScoped<ITracingService, TracingService>();
builder.Services.AddSingleton(IronForgeMeter.Meter);
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IAuditQueryService, AuditQueryService>();

builder.Services.AddScoped<IAgentApprovalStore, DBAgentApprovalStore>();
builder.Services.AddScoped<IAgentDelegationStore, DBAgentDelegationStore>();
/* AI LLM Agents */
builder.Services.AddScoped<IAgentGovernanceService, AgentGovernanceService>();

// Agent tool catalogue: each tool declares its name, schema and default governance.
builder.Services.AddScoped<IAgentTool, SearchAuditEventsAgentTool>();
builder.Services.AddScoped<IAgentToolRegistry, AgentToolRegistry>();

// Gym domain: services, agent tools, payments, email and background jobs.
builder.Services.AddGymPlatform(builder.Configuration);
builder.Services.AddGymBackgroundJobs(builder.Configuration);
builder.Services.AddIronForgeRateLimiting();
builder.Services.AddScoped<IPlatformInsightsService, PlatformInsightsService>();
// Generic internal-agent runtime; agents are definitions, not classes.
builder.Services.AddScoped<IAgentDefinitionProvider, DatabaseAgentDefinitionProvider>();
builder.Services.AddScoped<IAgentRuntime, AgentRuntime>();

// Usage tracking and tenant quotas.
builder.Services
    .AddOptions<AgentQuotaOptions>()
    .Bind(builder.Configuration.GetSection(AgentQuotaOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddScoped<IAgentUsageStore, AgentUsageStore>();
builder.Services.AddScoped<IAgentUsageRecorder, AgentUsageRecorder>();
builder.Services.AddScoped<IAgentQuotaService, AgentQuotaService>();

// Policies
// User first: when both fail, the user's missing permission is the clearer reason.
builder.Services.AddScoped<IAgentPolicy, UserPermissionPolicy>();
builder.Services.AddScoped<IAgentPolicy, AgentApprovalPolicy>();
builder.Services.AddScoped<IAgentPolicy, MaximumAmountPolicy>();

// Model profiles: one shared OpenAI connection, one cached client per profile.
builder.Services.AddSingleton<IAgentChatClientFactory, OpenAIAgentChatClientFactory>();

// Non-agent LLM callers (the conversation summariser) use the standard
// profile's client.
builder.Services.AddSingleton<IChatClient>(services =>
    services
        .GetRequiredService<IAgentChatClientFactory>()
        .GetClient(IAgentChatClientFactory.StandardProfile)
        .Client);

var corsSettings =
    builder.Configuration
        .GetSection(CorsSettings.SectionName)
        .Get<CorsSettings>()
    ?? throw new InvalidOperationException(
        "CORS settings are not configured.");

builder.Services.AddCors(options =>
{
    options.AddPolicy("IronForgeCors", policy =>
    {
        policy
            .WithOrigins(corsSettings.AllowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

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


/*-----------------------------------------------------------------------------*/
                        /* Application Pipeline */
/*-----------------------------------------------------------------------------*/

var app = builder.Build();
app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseCors("IronForgeCors");

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

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<ExecutionContextMiddleware>();

app.MapControllers();

if (!app.Environment.IsDevelopment() &&
    app.Services.GetRequiredService<IronForge.Application.Gym.Billing.IPaymentGateway>().IsSimulated)
{
    // Fine for a demo deployment; real gyms need Stripe keys (Stripe__SecretKey).
    app.Logger.LogWarning("Card payments are SIMULATED: no Stripe keys are configured, so no money is collected.");
}

// Local demo: migrate and seed on startup (appsettings.Development.json Seed:Demo).
if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("Seed:Demo"))
{
    await using var scope = app.Services.CreateAsyncScope();

    var db = scope.ServiceProvider.GetRequiredService<IronForgeDbContext>();
    await db.Database.MigrateAsync();

    await new IronForge.Infrastructure.Data.Seeding.DemoDataSeeder(
            db,
            scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>(),
            app.Logger)
        .SeedAsync();
}

app.Run();

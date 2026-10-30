using IronForge.Application.Auditing.Models;
using IronForge.Application.Auditing.Services;
using IronForge.Application.Commons;
using IronForge.Application.Configurations;
using IronForge.Application.Entities;
using IronForge.Application.Gym.Common;
using IronForge.Application.Persistence;
using IronForge.Shared.Authorization;
using IronForge.Shared.Models.Gym;
using IronForge.Shared.Subscriptions;
using Microsoft.Extensions.Options;

namespace IronForge.Application.Gym.Billing.Payments;

/// <summary>
/// A gym admin connects the gym's own payment provider account (Settings →
/// Online payments). Keys are checked with the provider before they are
/// saved, stored encrypted, and never returned.
/// </summary>
public interface IGymPaymentAccountService
{
    Task<ServiceResult<PaymentAccountDto>> GetAsync(CancellationToken cancellationToken = default);

    Task<ServiceResult<PaymentAccountDto>> SaveAsync(
        SavePaymentAccountRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Checks the stored keys with the provider again.</summary>
    Task<ServiceResult<PaymentAccountDto>> TestAsync(CancellationToken cancellationToken = default);

    /// <summary>Disconnects the provider: members can no longer pay in the app.</summary>
    Task<ServiceResult<PaymentAccountDto>> RemoveAsync(CancellationToken cancellationToken = default);
}

public sealed class GymPaymentAccountService : IGymPaymentAccountService
{
    private readonly IGymAccessService _access;
    private readonly IRepository<GymPaymentAccount> _accounts;
    private readonly IRepository<GymSettings> _settings;
    private readonly IPaymentProviderCatalog _providers;
    private readonly IPaymentCredentialProtector _protector;
    private readonly IOnlinePaymentService _payments;
    private readonly IAuditService _audit;
    private readonly PaymentOptions _options;
    private readonly AppUrlOptions _urls;
    private readonly TimeProvider _clock;

    public GymPaymentAccountService(
        IGymAccessService access,
        IRepository<GymPaymentAccount> accounts,
        IRepository<GymSettings> settings,
        IPaymentProviderCatalog providers,
        IPaymentCredentialProtector protector,
        IOnlinePaymentService payments,
        IAuditService audit,
        IOptions<PaymentOptions> options,
        IOptions<AppUrlOptions> urls,
        TimeProvider clock)
    {
        _access = access;
        _accounts = accounts;
        _settings = settings;
        _providers = providers;
        _protector = protector;
        _payments = payments;
        _audit = audit;
        _options = options.Value;
        _urls = urls.Value;
        _clock = clock;
    }

    public async Task<ServiceResult<PaymentAccountDto>> GetAsync(CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeAsync(cancellationToken);

        if (authorization is not null)
        {
            return authorization;
        }

        return await MapAsync(cancellationToken);
    }

    public async Task<ServiceResult<PaymentAccountDto>> SaveAsync(
        SavePaymentAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeAsync(cancellationToken);

        if (authorization is not null)
        {
            return authorization;
        }

        var provider = _providers.Find(request.Provider);

        if (provider is null || !provider.Descriptor.Selectable)
        {
            return GymResults.Invalid<PaymentAccountDto>("Choose a payment provider.");
        }

        var settings = await LoadSettingsAsync(cancellationToken);

        if (settings is null)
        {
            return GymResults.NotFound<PaymentAccountDto>("Gym settings");
        }

        if (!OnlinePaymentService.Supports(provider, settings.Currency))
        {
            return GymResults.Invalid<PaymentAccountDto>(
                $"{provider.Descriptor.Name} charges in {string.Join(", ", provider.Descriptor.Currencies)}; this gym bills in {settings.Currency}.");
        }

        var account = await _accounts.FirstOrDefaultTrackedAsync(x => x.TenantId == _access.TenantId!.Value, cancellationToken);

        // Blank fields keep the stored keys, but only for the same provider.
        var sameProvider = account is not null &&
                           string.Equals(account.Provider, provider.Descriptor.Id, StringComparison.OrdinalIgnoreCase);

        var apiKey = Clean(request.ApiKey) ?? (sameProvider ? _protector.Unprotect(account!.ApiKeyEncrypted) : null);
        var secret = Clean(request.Secret) ?? (sameProvider ? _protector.Unprotect(account!.SecretEncrypted) : null);
        var webhookSecret = provider.Descriptor.WebhookSecretLabel is null
            ? null
            : Clean(request.WebhookSecret) ??
              (sameProvider && account!.WebhookSecretEncrypted is not null ? _protector.Unprotect(account.WebhookSecretEncrypted) : null);

        if (apiKey is null || secret is null)
        {
            return GymResults.Invalid<PaymentAccountDto>(
                $"Enter the {provider.Descriptor.ApiKeyLabel} and the {provider.Descriptor.SecretLabel}.");
        }

        var credentials = new PaymentAccountCredentials(apiKey, secret, webhookSecret, request.IsLive);
        var problem = await provider.VerifyCredentialsAsync(credentials, cancellationToken);

        if (problem is not null)
        {
            await RecordAsync("PaymentAccountRejected", AuditOutcome.Failed, provider, request.IsLive, problem, cancellationToken);

            return GymResults.Invalid<PaymentAccountDto>(
                $"{provider.Descriptor.Name} did not accept these keys ({(request.IsLive ? "live" : "sandbox")}): {problem}");
        }

        var now = _clock.GetUtcNow().UtcDateTime;

        if (account is null)
        {
            account = new GymPaymentAccount
            {
                TenantId = _access.TenantId!.Value,
                CreatedAtUtc = now
            };

            _accounts.Add(account);
        }

        account.Provider = provider.Descriptor.Id;
        account.IsLive = request.IsLive;
        account.ApiKeyEncrypted = _protector.Protect(apiKey);
        account.SecretEncrypted = _protector.Protect(secret);
        account.WebhookSecretEncrypted = webhookSecret is null ? null : _protector.Protect(webhookSecret);
        account.ApiKeyHint = Hint(apiKey);
        account.VerifiedAtUtc = now;
        account.UpdatedAtUtc = now;
        account.UpdatedByUserId = _access.UserId;

        await _accounts.SaveChangesAsync(cancellationToken);
        await RecordAsync("PaymentAccountConnected", AuditOutcome.Succeeded, provider, request.IsLive, null, cancellationToken);

        return await MapAsync(cancellationToken);
    }

    public async Task<ServiceResult<PaymentAccountDto>> TestAsync(CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeAsync(cancellationToken);

        if (authorization is not null)
        {
            return authorization;
        }

        var account = await _accounts.FirstOrDefaultTrackedAsync(x => x.TenantId == _access.TenantId!.Value, cancellationToken);
        var provider = _providers.Find(account?.Provider);

        if (account is null || provider is null)
        {
            return GymResults.NotFound<PaymentAccountDto>("Payment provider");
        }

        var problem = await provider.VerifyCredentialsAsync(
            new PaymentAccountCredentials(
                _protector.Unprotect(account.ApiKeyEncrypted),
                _protector.Unprotect(account.SecretEncrypted),
                account.WebhookSecretEncrypted is null ? null : _protector.Unprotect(account.WebhookSecretEncrypted),
                account.IsLive),
            cancellationToken);

        if (problem is not null)
        {
            return GymResults.Invalid<PaymentAccountDto>($"{provider.Descriptor.Name} no longer accepts the stored keys: {problem}");
        }

        account.VerifiedAtUtc = _clock.GetUtcNow().UtcDateTime;
        await _accounts.SaveChangesAsync(cancellationToken);

        return await MapAsync(cancellationToken);
    }

    public async Task<ServiceResult<PaymentAccountDto>> RemoveAsync(CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeAsync(cancellationToken);

        if (authorization is not null)
        {
            return authorization;
        }

        var account = await _accounts.FirstOrDefaultTrackedAsync(x => x.TenantId == _access.TenantId!.Value, cancellationToken);

        if (account is not null)
        {
            var provider = _providers.Find(account.Provider);

            _accounts.Remove(account);
            await _accounts.SaveChangesAsync(cancellationToken);

            if (provider is not null)
            {
                await RecordAsync("PaymentAccountDisconnected", AuditOutcome.Succeeded, provider, account.IsLive, null, cancellationToken);
            }
        }

        return await MapAsync(cancellationToken);
    }

    private async Task<ServiceResult<PaymentAccountDto>?> AuthorizeAsync(CancellationToken cancellationToken)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymSettingsManage, PlatformModules.GymBilling, cancellationToken);

        return authorization.Allowed ? null : GymResults.Forbidden<PaymentAccountDto>(authorization);
    }

    private Task<GymSettings?> LoadSettingsAsync(CancellationToken cancellationToken) =>
        _settings.FirstOrDefaultAsync(x => x.TenantId == _access.TenantId!.Value, cancellationToken);

    private async Task<ServiceResult<PaymentAccountDto>> MapAsync(CancellationToken cancellationToken)
    {
        var tenantId = _access.TenantId!.Value;
        var settings = await LoadSettingsAsync(cancellationToken);
        var account = await _accounts.FirstOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);
        var provider = _providers.Find(account?.Provider);
        var currency = settings?.Currency ?? "";

        return ServiceResult<PaymentAccountDto>.Ok(new PaymentAccountDto
        {
            Configured = account is not null,
            Provider = account?.Provider,
            ProviderName = provider?.Descriptor.Name,
            IsLive = account?.IsLive ?? false,
            ApiKeyHint = account?.ApiKeyHint,
            HasWebhookSecret = account?.WebhookSecretEncrypted is not null,
            VerifiedAtUtc = account?.VerifiedAtUtc,
            UpdatedAtUtc = account?.UpdatedAtUtc,
            WebhookUrl = provider is null ? null : WebhookUrl(provider.Descriptor.Id, tenantId),
            Currency = currency,
            OnlinePaymentsAvailable = settings is not null && await _payments.IsAvailableAsync(settings, cancellationToken),
            SimulatedFallback = account is null && _options.AllowSimulated,
            Providers = _providers.Selectable
                .Select(x => new PaymentProviderInfoDto
                {
                    Id = x.Descriptor.Id,
                    Name = x.Descriptor.Name,
                    Region = x.Descriptor.Region,
                    Currencies = x.Descriptor.Currencies.ToList(),
                    SupportsGymCurrency = OnlinePaymentService.Supports(x, currency),
                    ApiKeyLabel = x.Descriptor.ApiKeyLabel,
                    SecretLabel = x.Descriptor.SecretLabel,
                    WebhookSecretLabel = x.Descriptor.WebhookSecretLabel,
                    SandboxDashboardUrl = x.Descriptor.SandboxDashboardUrl,
                    LiveDashboardUrl = x.Descriptor.LiveDashboardUrl,
                    WebhookSetup = x.Descriptor.WebhookSetup
                })
                .ToList()
        });
    }

    /// <summary>Where the provider sends payment notifications for this gym.</summary>
    public string WebhookUrl(string providerId, int tenantId) =>
        $"{_urls.ApiBaseUrl.TrimEnd('/')}/api/payments/webhooks/{providerId}/{tenantId}";

    private Task RecordAsync(
        string action,
        AuditOutcome outcome,
        IPaymentProvider provider,
        bool live,
        string? problem,
        CancellationToken cancellationToken) =>
        _audit.RecordAsync(
            AuditCategory.Billing,
            action,
            outcome,
            resourceType: "GymPaymentAccount",
            resourceId: _access.TenantId!.Value.ToString(),
            reason: $"{provider.Descriptor.Name} ({(live ? "live" : "sandbox")}){(problem is null ? "" : ": " + problem)}",
            cancellationToken: cancellationToken);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Hint(string apiKey) => apiKey.Length <= 4 ? "••••" : "••••" + apiKey[^4..];
}

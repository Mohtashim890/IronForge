using IronForge.Application.Auditing.Models;
using IronForge.Application.Auditing.Services;
using IronForge.Application.Commons;
using IronForge.Application.Entities;
using IronForge.Application.Gym.Billing;
using IronForge.Application.Gym.Billing.Payments;
using IronForge.Application.Gym.Common;
using IronForge.Application.Persistence;
using IronForge.Shared.Authorization;
using IronForge.Shared.Models.Gym;
using IronForge.Shared.Subscriptions;

namespace IronForge.Application.Gym.Settings;

public interface IGymSettingsService
{
    Task<ServiceResult<GymSettingsDto>> GetAsync(CancellationToken cancellationToken = default);

    Task<ServiceResult<GymSettingsDto>> UpdateAsync(
        UpdateGymSettingsRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<GymSettingsDto>> RegenerateJoinCodeAsync(CancellationToken cancellationToken = default);

    /// <summary>Turns the member pass on or off, and whether the app asks for the phone unlock.</summary>
    Task<ServiceResult<GymSettingsDto>> UpdateCheckInMethodsAsync(
        UpdateCheckInMethodsRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Creates a gym's settings row; used when a tenant is provisioned.</summary>
public interface IGymSettingsFactory
{
    /// <summary>Adds (does not save) settings with a unique join code.</summary>
    Task<GymSettings> CreateAsync(
        int tenantId,
        string currency = "USD",
        string timeZoneId = "UTC",
        CancellationToken cancellationToken = default);
}

public sealed class GymSettingsFactory : IGymSettingsFactory
{
    private readonly IRepository<GymSettings> _settings;
    private readonly TimeProvider _clock;

    public GymSettingsFactory(IRepository<GymSettings> settings, TimeProvider clock)
    {
        _settings = settings;
        _clock = clock;
    }

    public async Task<GymSettings> CreateAsync(
        int tenantId,
        string currency = "USD",
        string timeZoneId = "UTC",
        CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;

        var settings = new GymSettings
        {
            TenantId = tenantId,
            Currency = currency,
            TimeZoneId = timeZoneId,
            JoinCode = await NewUniqueJoinCodeAsync(_settings, cancellationToken),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        _settings.Add(settings);

        return settings;
    }

    public static async Task<string> NewUniqueJoinCodeAsync(
        IRepository<GymSettings> settings,
        CancellationToken cancellationToken)
    {
        // 32^8 codes: a collision is practically impossible, but cheap to rule out.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var code = GymCalendar.NewJoinCode();

            if (!await settings.AnyAsync(x => x.JoinCode == code, cancellationToken))
            {
                return code;
            }
        }

        throw new InvalidOperationException("Could not generate a unique join code.");
    }
}

public sealed class GymSettingsService : IGymSettingsService
{
    private static readonly HashSet<string> KnownCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "USD", "EUR", "GBP", "PKR", "INR", "AED", "SAR", "CAD", "AUD", "NZD", "SGD", "MYR", "ZAR", "CHF", "SEK", "NOK", "DKK", "JPY"
    };

    private readonly IGymAccessService _access;
    private readonly IRepository<GymSettings> _settings;
    private readonly IRepository<Tenant> _tenants;
    private readonly IOnlinePaymentService _payments;
    private readonly PaymentOptions _paymentOptions;
    private readonly IAuditService _audit;
    private readonly TimeProvider _clock;

    public GymSettingsService(
        IGymAccessService access,
        IRepository<GymSettings> settings,
        IRepository<Tenant> tenants,
        IOnlinePaymentService payments,
        Microsoft.Extensions.Options.IOptions<PaymentOptions> paymentOptions,
        IAuditService audit,
        TimeProvider clock)
    {
        _access = access;
        _settings = settings;
        _tenants = tenants;
        _payments = payments;
        _paymentOptions = paymentOptions.Value;
        _audit = audit;
        _clock = clock;
    }

    public async Task<ServiceResult<GymSettingsDto>> GetAsync(CancellationToken cancellationToken = default)
    {
        // Every staff member sees the settings (join code for the front desk).
        var authorization = await _access.AuthorizeAsync(Permissions.GymMembersRead, cancellationToken);

        if (!authorization.Allowed)
        {
            return GymResults.Forbidden<GymSettingsDto>(authorization);
        }

        var settings = await _settings.FirstOrDefaultAsync(
            x => x.TenantId == _access.TenantId!.Value,
            cancellationToken);

        return settings is null
            ? GymResults.NotFound<GymSettingsDto>("Gym settings")
            : ServiceResult<GymSettingsDto>.Ok(await MapAsync(settings, cancellationToken));
    }

    public async Task<ServiceResult<GymSettingsDto>> UpdateAsync(
        UpdateGymSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        var (settings, failure) = await LoadForChangeAsync(cancellationToken);

        if (settings is null)
        {
            return failure!;
        }

        var currency = request.Currency.Trim().ToUpperInvariant();

        if (!KnownCurrencies.Contains(currency))
        {
            return GymResults.Invalid<GymSettingsDto>(
                $"Currency must be one of: {string.Join(", ", KnownCurrencies.Order())}.");
        }

        if (!GymCalendar.IsValidTimeZone(request.TimeZoneId))
        {
            return GymResults.Invalid<GymSettingsDto>($"Unknown time zone '{request.TimeZoneId}'.");
        }

        var afterDue = (request.ReminderDaysAfterDue ?? [])
            .Where(x => x is > 0 and <= 90)
            .Distinct()
            .Order()
            .Take(5)
            .ToList();

        settings.Address = Clean(request.Address);
        settings.Phone = Clean(request.Phone);
        settings.ContactEmail = Clean(request.ContactEmail);
        settings.Currency = currency;
        settings.TimeZoneId = request.TimeZoneId.Trim();
        settings.InvoiceLeadDays = request.InvoiceLeadDays;
        settings.ReminderDaysBeforeDue = request.ReminderDaysBeforeDue;
        settings.ReminderDaysAfterDue = afterDue;
        settings.GracePeriodDays = request.GracePeriodDays;
        settings.PaymentInstructions = Clean(request.PaymentInstructions);
        settings.UpdatedAtUtc = _clock.GetUtcNow().UtcDateTime;

        await _settings.SaveChangesAsync(cancellationToken);

        await _audit.RecordAsync(
            AuditCategory.Tenant,
            "GymSettingsUpdated",
            AuditOutcome.Succeeded,
            resourceType: "GymSettings",
            resourceId: settings.Id.ToString(),
            cancellationToken: cancellationToken);

        return ServiceResult<GymSettingsDto>.Ok(await MapAsync(settings, cancellationToken));
    }

    public async Task<ServiceResult<GymSettingsDto>> RegenerateJoinCodeAsync(CancellationToken cancellationToken = default)
    {
        var (settings, failure) = await LoadForChangeAsync(cancellationToken);

        if (settings is null)
        {
            return failure!;
        }

        settings.JoinCode = await GymSettingsFactory.NewUniqueJoinCodeAsync(_settings, cancellationToken);
        settings.UpdatedAtUtc = _clock.GetUtcNow().UtcDateTime;

        await _settings.SaveChangesAsync(cancellationToken);

        await _audit.RecordAsync(
            AuditCategory.Tenant,
            "GymJoinCodeRegenerated",
            AuditOutcome.Succeeded,
            resourceType: "GymSettings",
            resourceId: settings.Id.ToString(),
            cancellationToken: cancellationToken);

        return ServiceResult<GymSettingsDto>.Ok(await MapAsync(settings, cancellationToken));
    }

    public async Task<ServiceResult<GymSettingsDto>> UpdateCheckInMethodsAsync(
        UpdateCheckInMethodsRequest request,
        CancellationToken cancellationToken = default)
    {
        var (settings, failure) = await LoadForChangeAsync(cancellationToken);

        if (settings is null)
        {
            return failure!;
        }

        if (settings.MemberPassCheckInEnabled != request.MemberPassEnabled ||
            settings.RequirePhoneUnlock != request.RequirePhoneUnlock)
        {
            settings.MemberPassCheckInEnabled = request.MemberPassEnabled;
            settings.RequirePhoneUnlock = request.RequirePhoneUnlock;
            settings.UpdatedAtUtc = _clock.GetUtcNow().UtcDateTime;

            await _settings.SaveChangesAsync(cancellationToken);

            await _audit.RecordAsync(
                AuditCategory.Tenant,
                "CheckInMethodsChanged",
                AuditOutcome.Succeeded,
                resourceType: "GymSettings",
                resourceId: settings.Id.ToString(),
                reason: $"member pass {(request.MemberPassEnabled ? "on" : "off")}, phone unlock {(request.RequirePhoneUnlock ? "required" : "not required")}",
                cancellationToken: cancellationToken);
        }

        return ServiceResult<GymSettingsDto>.Ok(await MapAsync(settings, cancellationToken));
    }

    private async Task<(GymSettings? Settings, ServiceResult<GymSettingsDto>? Failure)> LoadForChangeAsync(
        CancellationToken cancellationToken)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymSettingsManage, cancellationToken);

        if (!authorization.Allowed)
        {
            return (null, GymResults.Forbidden<GymSettingsDto>(authorization));
        }

        var settings = await _settings.FirstOrDefaultTrackedAsync(
            x => x.TenantId == _access.TenantId!.Value,
            cancellationToken);

        return settings is null
            ? (null, GymResults.NotFound<GymSettingsDto>("Gym settings"))
            : (settings, null);
    }

    private async Task<GymSettingsDto> MapAsync(GymSettings settings, CancellationToken cancellationToken)
    {
        var tenant = await _tenants.FirstOrDefaultAsync(x => x.Id == settings.TenantId, cancellationToken);

        return new GymSettingsDto
        {
            TenantId = settings.TenantId,
            GymName = tenant?.Name ?? "",
            Address = settings.Address,
            Phone = settings.Phone,
            ContactEmail = settings.ContactEmail,
            Currency = settings.Currency,
            TimeZoneId = settings.TimeZoneId,
            JoinCode = settings.JoinCode,
            InvoiceLeadDays = settings.InvoiceLeadDays,
            ReminderDaysBeforeDue = settings.ReminderDaysBeforeDue,
            ReminderDaysAfterDue = settings.ReminderDaysAfterDue.ToList(),
            GracePeriodDays = settings.GracePeriodDays,
            PaymentsSimulated = _paymentOptions.AllowSimulated,
            OnlinePaymentsAvailable = await _payments.IsAvailableAsync(settings, cancellationToken),
            PaymentInstructions = settings.PaymentInstructions,
            MemberPassCheckInEnabled = settings.MemberPassCheckInEnabled,
            RequirePhoneUnlock = settings.RequirePhoneUnlock,
            AccessControlEnabled = tenant?.GetEnabledModules().Contains(PlatformModules.GymAccess) ?? false,
            CoachingEnabled = tenant?.GetEnabledModules().Contains(PlatformModules.GymCoaching) ?? false
        };
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

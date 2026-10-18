using System.Security.Cryptography;
using System.Text;
using IronForge.Application.Auditing.Models;
using IronForge.Application.Auditing.Services;
using IronForge.Application.Commons;
using IronForge.Application.Entities;
using IronForge.Application.Execution;
using IronForge.Application.Gym.Billing;
using IronForge.Application.Gym.Common;
using IronForge.Application.Persistence;
using IronForge.Shared.Authorization;
using IronForge.Shared.Models.Gym;

namespace IronForge.Application.Gym.CheckIns;

/// <summary>
/// Device lookups made before a tenant is known: the device presents only
/// its pairing code or its key. Implemented in Infrastructure, where the
/// tenant query filter is bypassed and every query is by hash.
/// </summary>
public interface ICheckInDeviceDirectory
{
    Task<CheckInDevice?> FindByPairingCodeHashTrackedAsync(
        string pairingCodeHash,
        CancellationToken cancellationToken = default);

    Task<CheckInDevice?> FindByKeyHashAsync(
        string keyHash,
        CancellationToken cancellationToken = default);

    /// <summary>Updates LastSeenAtUtc at most once a minute, without loading the device.</summary>
    Task TouchAsync(
        int deviceId,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Admins add, re-pair and remove the gym's check-in devices.</summary>
public interface ICheckInDeviceService
{
    Task<ServiceResult<List<CheckInDeviceDto>>> ListAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Adds a device and returns its one-time pairing code.</summary>
    Task<ServiceResult<CheckInDevicePairingDto>> CreateAsync(
        CreateCheckInDeviceRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>A new pairing code, e.g. to replace the tablet. Pairing replaces the old key.</summary>
    Task<ServiceResult<CheckInDevicePairingDto>> NewPairingCodeAsync(
        int deviceId,
        CancellationToken cancellationToken = default);

    /// <summary>The device's key stops working at once.</summary>
    Task<ServiceResult<CheckInDeviceDto>> RevokeAsync(
        int deviceId,
        CancellationToken cancellationToken = default);
}

/// <summary>The device side: pairing with a code, then authenticating every request with the key.</summary>
public interface IKioskService
{
    Task<ServiceResult<KioskPairingResultDto>> PairAsync(
        string pairingCode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the device key and establishes the request's execution
    /// context as that device, in its gym, holding only
    /// <see cref="Permissions.GymCheckInsDevice"/>.
    /// </summary>
    Task<ServiceResult<KioskInfoDto>> AuthenticateAsync(
        string? deviceKey,
        CancellationToken cancellationToken = default);
}

public static class CheckInDeviceSecrets
{
    public const string KeyPrefix = "ifk_";

    public static readonly TimeSpan PairingLifetime = TimeSpan.FromMinutes(15);

    public static string NewPairingCode()
    {
        var code = GymCalendar.NewJoinCode(8);
        return $"{code[..4]}-{code[4..]}";
    }

    public static string NewDeviceKey() =>
        KeyPrefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    /// <summary>Codes and keys are random and long enough that a plain SHA-256 is safe.</summary>
    public static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public static string HashPairingCode(string code) => Hash("pair:" + GymCalendar.NormalizeJoinCode(code));

    public static string HashKey(string key) => Hash("key:" + key.Trim());
}

public sealed class CheckInDeviceService : ICheckInDeviceService
{
    private readonly IGymAccessService _access;
    private readonly IRepository<CheckInDevice> _devices;
    private readonly IRepository<CheckIn> _checkIns;
    private readonly IBillingEngine _billing;
    private readonly IAuditService _audit;
    private readonly TimeProvider _clock;

    public CheckInDeviceService(
        IGymAccessService access,
        IRepository<CheckInDevice> devices,
        IRepository<CheckIn> checkIns,
        IBillingEngine billing,
        IAuditService audit,
        TimeProvider clock)
    {
        _access = access;
        _devices = devices;
        _checkIns = checkIns;
        _billing = billing;
        _audit = audit;
        _clock = clock;
    }

    public async Task<ServiceResult<List<CheckInDeviceDto>>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymSettingsManage, cancellationToken);

        if (!authorization.Allowed)
        {
            return GymResults.Forbidden<List<CheckInDeviceDto>>(authorization);
        }

        var gym = await _billing.GetGymAsync(_access.TenantId!.Value, cancellationToken);
        var todayUtc = GymCalendar.StartOfDayUtc(
            GymCalendar.Today(_clock, gym?.Settings.TimeZoneId),
            gym?.Settings.TimeZoneId);
        var now = _clock.GetUtcNow().UtcDateTime;

        var devices = await _devices.QueryAsync(
            q => q.OrderBy(x => x.RevokedAtUtc != null).ThenBy(x => x.Name),
            cancellationToken);

        var counts = (await _checkIns.QueryAsync(
                q => q
                    .Where(x => x.CheckInDeviceId != null && x.CheckedInAtUtc >= todayUtc)
                    .GroupBy(x => x.CheckInDeviceId!.Value)
                    .Select(x => new { DeviceId = x.Key, Count = x.Count() }),
                cancellationToken))
            .ToDictionary(x => x.DeviceId, x => x.Count);

        return ServiceResult<List<CheckInDeviceDto>>.Ok(
            devices.Select(x => Map(x, now, counts.GetValueOrDefault(x.Id))).ToList());
    }

    public async Task<ServiceResult<CheckInDevicePairingDto>> CreateAsync(
        CreateCheckInDeviceRequest request,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymSettingsManage, cancellationToken);

        if (!authorization.Allowed)
        {
            return GymResults.Forbidden<CheckInDevicePairingDto>(authorization);
        }

        var name = request.Name.Trim();

        if (name.Length == 0)
        {
            return GymResults.Invalid<CheckInDevicePairingDto>("Give the device a name, e.g. \"Front entrance\".");
        }

        var device = new CheckInDevice
        {
            TenantId = _access.TenantId!.Value,
            Name = name,
            CreatedByUserId = _access.UserId,
            CreatedAtUtc = _clock.GetUtcNow().UtcDateTime
        };

        var code = IssuePairingCode(device);

        _devices.Add(device);
        await _devices.SaveChangesAsync(cancellationToken);

        await RecordAsync("CheckInDeviceAdded", device, cancellationToken);

        return Pairing(device, code);
    }

    public async Task<ServiceResult<CheckInDevicePairingDto>> NewPairingCodeAsync(
        int deviceId,
        CancellationToken cancellationToken = default)
    {
        var (device, failure) = await LoadAsync<CheckInDevicePairingDto>(deviceId, cancellationToken);

        if (device is null)
        {
            return failure!;
        }

        if (device.RevokedAtUtc is not null)
        {
            return GymResults.Conflict<CheckInDevicePairingDto>("This device was removed. Add a new device instead.");
        }

        var code = IssuePairingCode(device);
        await _devices.SaveChangesAsync(cancellationToken);

        await RecordAsync("CheckInDevicePairingCodeIssued", device, cancellationToken);

        return Pairing(device, code);
    }

    public async Task<ServiceResult<CheckInDeviceDto>> RevokeAsync(
        int deviceId,
        CancellationToken cancellationToken = default)
    {
        var (device, failure) = await LoadAsync<CheckInDeviceDto>(deviceId, cancellationToken);

        if (device is null)
        {
            return failure!;
        }

        var now = _clock.GetUtcNow().UtcDateTime;

        device.RevokedAtUtc ??= now;
        device.KeyHash = null;
        device.PairingCodeHash = null;
        device.PairingExpiresAtUtc = null;

        await _devices.SaveChangesAsync(cancellationToken);

        await RecordAsync("CheckInDeviceRemoved", device, cancellationToken);

        return ServiceResult<CheckInDeviceDto>.Ok(Map(device, now, 0));
    }

    private string IssuePairingCode(CheckInDevice device)
    {
        var code = CheckInDeviceSecrets.NewPairingCode();

        device.PairingCodeHash = CheckInDeviceSecrets.HashPairingCode(code);
        device.PairingExpiresAtUtc = _clock.GetUtcNow().UtcDateTime + CheckInDeviceSecrets.PairingLifetime;

        return code;
    }

    private async Task<(CheckInDevice? Device, ServiceResult<T>? Failure)> LoadAsync<T>(
        int deviceId,
        CancellationToken cancellationToken)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymSettingsManage, cancellationToken);

        if (!authorization.Allowed)
        {
            return (null, GymResults.Forbidden<T>(authorization));
        }

        var device = await _devices.FirstOrDefaultTrackedAsync(x => x.Id == deviceId, cancellationToken);

        return device is null
            ? (null, GymResults.NotFound<T>("Check-in device"))
            : (device, null);
    }

    private ServiceResult<CheckInDevicePairingDto> Pairing(CheckInDevice device, string code) =>
        ServiceResult<CheckInDevicePairingDto>.Ok(new CheckInDevicePairingDto
        {
            Device = Map(device, _clock.GetUtcNow().UtcDateTime, 0),
            PairingCode = code,
            ExpiresAtUtc = device.PairingExpiresAtUtc!.Value
        });

    private static CheckInDeviceDto Map(CheckInDevice device, DateTime now, int checkInsToday) => new()
    {
        Id = device.Id,
        Name = device.Name,
        IsPaired = device.KeyHash is not null,
        IsRevoked = device.RevokedAtUtc is not null,
        PairingExpiresAtUtc = device.PairingCodeHash is not null && device.PairingExpiresAtUtc > now
            ? device.PairingExpiresAtUtc
            : null,
        CreatedAtUtc = device.CreatedAtUtc,
        PairedAtUtc = device.PairedAtUtc,
        LastSeenAtUtc = device.LastSeenAtUtc,
        CheckInsToday = checkInsToday
    };

    private Task RecordAsync(string action, CheckInDevice device, CancellationToken cancellationToken) =>
        _audit.RecordAsync(
            AuditCategory.Membership,
            action,
            AuditOutcome.Succeeded,
            resourceType: "CheckInDevice",
            resourceId: device.Id.ToString(),
            reason: device.Name,
            cancellationToken: cancellationToken);
}

public sealed class KioskService : IKioskService
{
    private readonly ICheckInDeviceDirectory _directory;
    private readonly IRepository<Tenant> _tenants;
    private readonly IRepository<GymSettings> _settings;
    private readonly IExecutionContextAccessor _executionContext;
    private readonly IAuditService _audit;
    private readonly TimeProvider _clock;

    public KioskService(
        ICheckInDeviceDirectory directory,
        IRepository<Tenant> tenants,
        IRepository<GymSettings> settings,
        IExecutionContextAccessor executionContext,
        IAuditService audit,
        TimeProvider clock)
    {
        _directory = directory;
        _tenants = tenants;
        _settings = settings;
        _executionContext = executionContext;
        _audit = audit;
        _clock = clock;
    }

    public async Task<ServiceResult<KioskPairingResultDto>> PairAsync(
        string pairingCode,
        CancellationToken cancellationToken = default)
    {
        if (_executionContext.Current is not null)
        {
            return ServiceResult<KioskPairingResultDto>.Fail(SignedInMessage, 400);
        }

        var now = _clock.GetUtcNow().UtcDateTime;

        var device = await _directory.FindByPairingCodeHashTrackedAsync(
            CheckInDeviceSecrets.HashPairingCode(pairingCode),
            cancellationToken);

        if (device is null || device.RevokedAtUtc is not null || !(device.PairingExpiresAtUtc > now))
        {
            return ServiceResult<KioskPairingResultDto>.Fail(
                "That pairing code is not valid or has expired. Ask an admin for a new code under Check-in devices.",
                400);
        }

        var tenant = await _tenants.FirstOrDefaultAsync(x => x.Id == device.TenantId, cancellationToken);

        if (tenant is null || !tenant.HasAccess(now))
        {
            return ServiceResult<KioskPairingResultDto>.Fail("This gym's subscription is not active.", 403);
        }

        var key = CheckInDeviceSecrets.NewDeviceKey();

        device.KeyHash = CheckInDeviceSecrets.HashKey(key);
        device.PairingCodeHash = null;
        device.PairingExpiresAtUtc = null;
        device.PairedAtUtc = now;
        device.LastSeenAtUtc = now;

        await _directory.SaveChangesAsync(cancellationToken);

        SetDeviceContext(device);

        await _audit.RecordAsync(
            AuditCategory.Membership,
            "CheckInDevicePaired",
            AuditOutcome.Succeeded,
            resourceType: "CheckInDevice",
            resourceId: device.Id.ToString(),
            reason: device.Name,
            cancellationToken: cancellationToken);

        return ServiceResult<KioskPairingResultDto>.Ok(new KioskPairingResultDto
        {
            DeviceKey = key,
            DeviceName = device.Name,
            GymName = tenant.Name
        });
    }

    public async Task<ServiceResult<KioskInfoDto>> AuthenticateAsync(
        string? deviceKey,
        CancellationToken cancellationToken = default)
    {
        if (_executionContext.Current is not null)
        {
            return ServiceResult<KioskInfoDto>.Fail(SignedInMessage, 400);
        }

        if (string.IsNullOrWhiteSpace(deviceKey) ||
            !deviceKey.Trim().StartsWith(CheckInDeviceSecrets.KeyPrefix, StringComparison.Ordinal))
        {
            return NotPaired();
        }

        var device = await _directory.FindByKeyHashAsync(
            CheckInDeviceSecrets.HashKey(deviceKey),
            cancellationToken);

        if (device is null || device.RevokedAtUtc is not null)
        {
            return NotPaired();
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var tenant = await _tenants.FirstOrDefaultAsync(x => x.Id == device.TenantId, cancellationToken);

        if (tenant is null || !tenant.HasAccess(now))
        {
            return ServiceResult<KioskInfoDto>.Fail("This gym's subscription is not active.", 403);
        }

        SetDeviceContext(device);

        await _directory.TouchAsync(device.Id, now, cancellationToken);

        // GymSettings has no tenant filter: always by TenantId.
        var settings = await _settings.FirstOrDefaultAsync(x => x.TenantId == device.TenantId, cancellationToken);

        return ServiceResult<KioskInfoDto>.Ok(new KioskInfoDto
        {
            DeviceName = device.Name,
            GymName = tenant.Name,
            MemberPassEnabled = settings?.MemberPassCheckInEnabled ?? false,
            GymQrEnabled = settings?.GymQrCheckInEnabled ?? false
        });
    }

    /// <summary>The execution context can be set once per request, so a device request carries no user token.</summary>
    private const string SignedInMessage = "Device requests must not carry a sign-in token.";

    private void SetDeviceContext(CheckInDevice device) =>
        _executionContext.Set(new IronForge.Application.Execution.ExecutionContext
        {
            TenantId = device.TenantId,
            Source = ExecutionSource.Device,
            Actor = new ExecutionActor
            {
                DeviceId = device.Id,
                Username = device.Name,
                UserPermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    Permissions.GymCheckInsDevice
                }
            }
        });

    private static ServiceResult<KioskInfoDto> NotPaired() =>
        ServiceResult<KioskInfoDto>.Fail(
            "This device is not paired with a gym, or it was removed. Pair it again with a new code.",
            401);
}

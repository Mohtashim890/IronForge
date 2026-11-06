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
/// Terminal lookups made before a tenant is known: the terminal presents only
/// its serial number. Implemented in Infrastructure, where the tenant query
/// filter is bypassed.
/// </summary>
public interface ICheckInDeviceDirectory
{
    /// <summary>A registered fingerprint terminal by its (normalized) serial number, in any gym.</summary>
    Task<CheckInDevice?> FindTerminalBySerialAsync(
        string serialNumber,
        CancellationToken cancellationToken = default);

    /// <summary>Updates LastSeenAtUtc at most once a minute, without loading the device.</summary>
    Task TouchAsync(
        int deviceId,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Admins list and remove the gym's fingerprint terminals (registered with
/// <see cref="IronForge.Application.Gym.Access.ITerminalAdminService"/>).
/// </summary>
public interface ICheckInDeviceService
{
    Task<ServiceResult<List<CheckInDeviceDto>>> ListAsync(
        CancellationToken cancellationToken = default);

    /// <summary>IronForge stops accepting the terminal at once.</summary>
    Task<ServiceResult<CheckInDeviceDto>> RevokeAsync(
        int deviceId,
        CancellationToken cancellationToken = default);
}

public sealed class CheckInDeviceService : ICheckInDeviceService
{
    private readonly IGymAccessService _access;
    private readonly IRepository<CheckInDevice> _devices;
    private readonly IRepository<CheckIn> _checkIns;
    private readonly IRepository<TerminalUserSync> _syncs;
    private readonly IBillingEngine _billing;
    private readonly IAuditService _audit;
    private readonly TimeProvider _clock;

    public CheckInDeviceService(
        IGymAccessService access,
        IRepository<CheckInDevice> devices,
        IRepository<CheckIn> checkIns,
        IRepository<TerminalUserSync> syncs,
        IBillingEngine billing,
        IAuditService audit,
        TimeProvider clock)
    {
        _access = access;
        _devices = devices;
        _checkIns = checkIns;
        _syncs = syncs;
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
            // Kiosk rows are history only (kiosks were removed).
            q => q
                .Where(x => x.Kind == CheckInDeviceKind.ZkTecoTerminal)
                .OrderBy(x => x.RevokedAtUtc != null)
                .ThenBy(x => x.Name),
            cancellationToken);

        var counts = (await _checkIns.QueryAsync(
                q => q
                    .Where(x => x.CheckInDeviceId != null && x.CheckedInAtUtc >= todayUtc)
                    .GroupBy(x => x.CheckInDeviceId!.Value)
                    .Select(x => new { DeviceId = x.Key, Count = x.Count() }),
                cancellationToken))
            .ToDictionary(x => x.DeviceId, x => x.Count);

        var syncs = (await _syncs.QueryAsync(
                q => q
                    .GroupBy(x => x.CheckInDeviceId)
                    .Select(x => new
                    {
                        DeviceId = x.Key,
                        Confirmed = x.Count(s => s.ConfirmedAtUtc != null),
                        Pending = x.Count(s => s.ConfirmedAtUtc == null)
                    }),
                cancellationToken))
            .ToDictionary(x => x.DeviceId);

        return ServiceResult<List<CheckInDeviceDto>>.Ok(devices.Select(x =>
        {
            var dto = Map(x, now, counts.GetValueOrDefault(x.Id));
            dto.UsersInSync = syncs.GetValueOrDefault(x.Id)?.Confirmed ?? 0;
            dto.UsersPending = syncs.GetValueOrDefault(x.Id)?.Pending ?? 0;
            return dto;
        }).ToList());
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

        await _devices.SaveChangesAsync(cancellationToken);

        await RecordAsync("CheckInDeviceRemoved", device, cancellationToken);

        return ServiceResult<CheckInDeviceDto>.Ok(Map(device, now, 0));
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
            ? (null, GymResults.NotFound<T>("Terminal"))
            : (device, null);
    }

    private static CheckInDeviceDto Map(CheckInDevice device, DateTime now, int checkInsToday) => new()
    {
        Id = device.Id,
        Name = device.Name,
        Kind = device.Kind,
        SerialNumber = device.SerialNumber,
        AllowedIp = device.AllowedIp,
        // A terminal counts as connected once it has been in touch.
        IsPaired = device.LastSeenAtUtc is not null,
        IsRevoked = device.RevokedAtUtc is not null,
        CreatedAtUtc = device.CreatedAtUtc,
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


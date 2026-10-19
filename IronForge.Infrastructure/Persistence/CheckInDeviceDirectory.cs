using IronForge.Application.Entities;
using IronForge.Application.Gym.CheckIns;
using IronForge.Infrastructure.Data;
using IronForge.Shared.Models.Gym;
using Microsoft.EntityFrameworkCore;

namespace IronForge.Infrastructure.Persistence;

/// <summary>
/// See <see cref="ICheckInDeviceDirectory"/>. The device is not yet known to
/// belong to a tenant, so the tenant query filter is bypassed; every lookup
/// is by the hash of a random secret, which identifies one device only.
/// </summary>
public sealed class CheckInDeviceDirectory : ICheckInDeviceDirectory
{
    private static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(1);

    private readonly IronForgeDbContext _dbContext;

    public CheckInDeviceDirectory(IronForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<CheckInDevice?> FindByPairingCodeHashTrackedAsync(
        string pairingCodeHash,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.CheckInDevices
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.PairingCodeHash == pairingCodeHash, cancellationToken);
    }

    public Task<CheckInDevice?> FindByKeyHashAsync(
        string keyHash,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.CheckInDevices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.KeyHash == keyHash, cancellationToken);
    }

    public Task<CheckInDevice?> FindTerminalBySerialAsync(
        string serialNumber,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.CheckInDevices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.SerialNumber == serialNumber && x.Kind == CheckInDeviceKind.ZkTecoTerminal,
                cancellationToken);
    }

    public Task TouchAsync(
        int deviceId,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        var staleBefore = nowUtc - TouchInterval;

        return _dbContext.CheckInDevices
            .IgnoreQueryFilters()
            .Where(x => x.Id == deviceId && (x.LastSeenAtUtc == null || x.LastSeenAtUtc < staleBefore))
            .ExecuteUpdateAsync(x => x.SetProperty(d => d.LastSeenAtUtc, nowUtc), cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}

using System.Globalization;
using IronForge.Application.Auditing.Models;
using IronForge.Application.Auditing.Services;
using IronForge.Application.Commons;
using IronForge.Application.Entities;
using IronForge.Application.Gym.CheckIns;
using IronForge.Application.Gym.Common;
using IronForge.Application.Persistence;
using IronForge.Shared.Authorization;
using IronForge.Shared.Models.Gym;
using IronForge.Shared.Subscriptions;

namespace IronForge.Application.Gym.Access;

/// <summary>
/// Staff side of fingerprint terminals (module <see cref="PlatformModules.GymAccess"/>):
/// registering terminals, and the terminal users: the people enrolled on the
/// terminals, which staff link to members. Linking is the migration of an
/// existing ZKTeco installation: fingerprints stay on the terminals, and
/// IronForge decides, through the sync, who of them may enter.
/// </summary>
public interface ITerminalAdminService
{
    Task<ServiceResult<CheckInDeviceDto>> RegisterAsync(
        RegisterTerminalRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Asks the terminal to upload its user list again.</summary>
    Task<ServiceResult<bool>> RequestUserListAsync(
        int deviceId,
        CancellationToken cancellationToken = default);

    /// <summary>Sends every linked user to the terminal again (e.g. after a factory reset).</summary>
    Task<ServiceResult<bool>> ResyncAsync(
        int deviceId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<List<TerminalUserDto>>> ListUsersAsync(
        CancellationToken cancellationToken = default);

    Task<ServiceResult<TerminalUserDto>> LinkAsync(
        int terminalUserId,
        LinkTerminalUserRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Links every unlinked user whose terminal name matches exactly one member.</summary>
    Task<ServiceResult<int>> LinkSuggestedAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Marks a terminal user as not a member (staff): IronForge never changes them.</summary>
    Task<ServiceResult<TerminalUserDto>> SetIgnoredAsync(
        int terminalUserId,
        bool ignored,
        CancellationToken cancellationToken = default);

    /// <summary>A new member gets the next free user number; they enrol a finger at the terminal.</summary>
    Task<ServiceResult<TerminalUserDto>> CreateForMemberAsync(
        CreateTerminalUserRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Users exported from ZKTeco's software as "PIN,Name" lines.</summary>
    Task<ServiceResult<TerminalImportResultDto>> ImportAsync(
        ImportTerminalUsersRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class TerminalAdminService : ITerminalAdminService
{
    private sealed record MemberMatch(int Id, string Name);

    private const int FirstPin = 1001;

    private readonly IGymAccessService _access;
    private readonly ICheckInDeviceDirectory _directory;
    private readonly IRepository<CheckInDevice> _devices;
    private readonly IRepository<TerminalUser> _users;
    private readonly IRepository<TerminalUserSync> _syncs;
    private readonly IRepository<TerminalCommand> _commands;
    private readonly IRepository<MemberProfile> _members;
    private readonly IAuditService _audit;
    private readonly TimeProvider _clock;

    public TerminalAdminService(
        IGymAccessService access,
        ICheckInDeviceDirectory directory,
        IRepository<CheckInDevice> devices,
        IRepository<TerminalUser> users,
        IRepository<TerminalUserSync> syncs,
        IRepository<TerminalCommand> commands,
        IRepository<MemberProfile> members,
        IAuditService audit,
        TimeProvider clock)
    {
        _access = access;
        _directory = directory;
        _devices = devices;
        _users = users;
        _syncs = syncs;
        _commands = commands;
        _members = members;
        _audit = audit;
        _clock = clock;
    }

    public async Task<ServiceResult<CheckInDeviceDto>> RegisterAsync(
        RegisterTerminalRequest request,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymSettingsManage, PlatformModules.GymAccess, cancellationToken);

        if (!authorization.Allowed)
        {
            return GymResults.Forbidden<CheckInDeviceDto>(authorization);
        }

        var serial = ZkTecoProtocol.NormalizeSerial(request.SerialNumber);
        var name = request.Name.Trim();

        if (serial.Length < 4 || name.Length == 0)
        {
            return GymResults.Invalid<CheckInDeviceDto>("Enter the terminal's name and its serial number (Menu → System info).");
        }

        var allowedIp = string.IsNullOrWhiteSpace(request.AllowedIp) ? null : request.AllowedIp.Trim();

        if (allowedIp is not null && !System.Net.IPAddress.TryParse(allowedIp, out _))
        {
            return GymResults.Invalid<CheckInDeviceDto>("The allowed IP address is not valid.");
        }

        // Serial numbers are unique on the platform; never reveal which gym has one.
        if (await _directory.FindTerminalBySerialAsync(serial, cancellationToken) is not null)
        {
            return GymResults.Conflict<CheckInDeviceDto>("A terminal with this serial number is already registered.");
        }

        var now = _clock.GetUtcNow().UtcDateTime;

        var device = new CheckInDevice
        {
            TenantId = _access.TenantId!.Value,
            Name = name,
            Kind = CheckInDeviceKind.ZkTecoTerminal,
            SerialNumber = serial,
            AllowedIp = allowedIp,
            CreatedByUserId = _access.UserId,
            CreatedAtUtc = now
        };

        _devices.Add(device);
        await _devices.SaveChangesAsync(cancellationToken);

        await _audit.RecordAsync(
            AuditCategory.Membership,
            "TerminalRegistered",
            AuditOutcome.Succeeded,
            resourceType: "CheckInDevice",
            resourceId: device.Id.ToString(CultureInfo.InvariantCulture),
            reason: $"{name} ({serial})",
            cancellationToken: cancellationToken);

        return ServiceResult<CheckInDeviceDto>.Ok(new CheckInDeviceDto
        {
            Id = device.Id,
            Name = device.Name,
            Kind = device.Kind,
            SerialNumber = device.SerialNumber,
            AllowedIp = device.AllowedIp,
            CreatedAtUtc = device.CreatedAtUtc
        });
    }

    public async Task<ServiceResult<bool>> RequestUserListAsync(
        int deviceId,
        CancellationToken cancellationToken = default)
    {
        var (device, failure) = await LoadTerminalAsync(deviceId, cancellationToken);

        if (device is null)
        {
            return failure!;
        }

        _commands.Add(new TerminalCommand
        {
            TenantId = device.TenantId,
            CheckInDeviceId = device.Id,
            Text = ZkTecoProtocol.QueryUsers,
            CreatedAtUtc = _clock.GetUtcNow().UtcDateTime
        });

        await _commands.SaveChangesAsync(cancellationToken);

        return ServiceResult<bool>.Ok(true);
    }

    public async Task<ServiceResult<bool>> ResyncAsync(
        int deviceId,
        CancellationToken cancellationToken = default)
    {
        var (device, failure) = await LoadTerminalAsync(deviceId, cancellationToken);

        if (device is null)
        {
            return failure!;
        }

        foreach (var sync in await _syncs.ListTrackedAsync(x => x.CheckInDeviceId == device.Id, cancellationToken))
        {
            _syncs.Remove(sync);
        }

        device.LastSyncedAtUtc = null;
        await _syncs.SaveChangesAsync(cancellationToken);

        return ServiceResult<bool>.Ok(true);
    }

    public async Task<ServiceResult<List<TerminalUserDto>>> ListUsersAsync(
        CancellationToken cancellationToken = default)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymMembersRead, PlatformModules.GymAccess, cancellationToken);

        if (!authorization.Allowed)
        {
            return GymResults.Forbidden<List<TerminalUserDto>>(authorization);
        }

        var users = await _users.QueryAsync(
            q => q
                .OrderBy(x => x.MemberProfileId != null)
                .ThenBy(x => x.Pin.Length)
                .ThenBy(x => x.Pin)
                .Select(x => new
                {
                    User = x,
                    MemberName = x.MemberProfile != null ? x.MemberProfile.FullName : null,
                    MemberStatus = x.MemberProfile != null ? (MemberStatus?)x.MemberProfile.Status : null
                }),
            cancellationToken);

        // The latest state sent to any terminal, per user number.
        var syncs = (await _syncs.QueryAsync(
                q => q.Select(x => new { x.Pin, x.Enabled, x.ValidUntil, x.QueuedAtUtc }),
                cancellationToken))
            .GroupBy(x => x.Pin)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(s => s.QueuedAtUtc).First());

        var matches = await NameMatchesAsync(cancellationToken);

        return ServiceResult<List<TerminalUserDto>>.Ok(users.Select(x =>
        {
            var suggestion = x.User.MemberProfileId is null && x.User.IgnoredAtUtc is null && x.User.TerminalName is not null
                ? matches.GetValueOrDefault(NormalizeName(x.User.TerminalName))
                : null;
            var sync = syncs.GetValueOrDefault(x.User.Pin);

            return new TerminalUserDto
            {
                Id = x.User.Id,
                Pin = x.User.Pin,
                TerminalName = x.User.TerminalName,
                HasFingerprint = x.User.HasFingerprint,
                MemberId = x.User.MemberProfileId,
                MemberName = x.MemberName,
                MemberStatus = x.MemberStatus,
                SuggestedMemberId = suggestion?.Id,
                SuggestedMemberName = suggestion?.Name,
                Ignored = x.User.IgnoredAtUtc is not null,
                AccessEnabled = sync?.Enabled,
                ValidUntil = sync?.Enabled == true ? sync.ValidUntil : null,
                LastEntryAtUtc = x.User.LastEntryAtUtc,
                FirstSeenAtUtc = x.User.FirstSeenAtUtc
            };
        }).ToList());
    }

    public async Task<ServiceResult<TerminalUserDto>> LinkAsync(
        int terminalUserId,
        LinkTerminalUserRequest request,
        CancellationToken cancellationToken = default)
    {
        var (user, failure) = await LoadUserAsync(terminalUserId, cancellationToken);

        if (user is null)
        {
            return failure!;
        }

        if (request.MemberId is { } memberId)
        {
            var member = await _members.FirstOrDefaultAsync(x => x.Id == memberId && x.AnonymizedAtUtc == null, cancellationToken);

            if (member is null)
            {
                return GymResults.NotFound<TerminalUserDto>("Member");
            }

            var other = await _users.FirstOrDefaultAsync(x => x.MemberProfileId == memberId && x.Id != user.Id, cancellationToken);

            if (other is not null)
            {
                return GymResults.Conflict<TerminalUserDto>(
                    $"{member.FullName} is already terminal user {other.Pin}. Unlink that first.");
            }

            user.MemberProfileId = memberId;
            user.LinkedAtUtc = _clock.GetUtcNow().UtcDateTime;
            user.IgnoredAtUtc = null;
        }
        else
        {
            user.MemberProfileId = null;
            user.LinkedAtUtc = null;
        }

        await SaveAndResyncAsync(cancellationToken);

        await _audit.RecordAsync(
            AuditCategory.Membership,
            request.MemberId is null ? "TerminalUserUnlinked" : "TerminalUserLinked",
            AuditOutcome.Succeeded,
            resourceType: "TerminalUser",
            resourceId: user.Id.ToString(CultureInfo.InvariantCulture),
            reason: $"terminal user {user.Pin}" + (request.MemberId is { } id ? $" → member {id}" : ""),
            cancellationToken: cancellationToken);

        return await SingleAsync(user.Id, cancellationToken);
    }

    public async Task<ServiceResult<int>> LinkSuggestedAsync(
        CancellationToken cancellationToken = default)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymMembersManage, PlatformModules.GymAccess, cancellationToken);

        if (!authorization.Allowed)
        {
            return GymResults.Forbidden<int>(authorization);
        }

        var matches = await NameMatchesAsync(cancellationToken);
        var linkedMembers = (await _users.QueryAsync(
                q => q.Where(x => x.MemberProfileId != null).Select(x => x.MemberProfileId!.Value),
                cancellationToken))
            .ToHashSet();

        var unlinked = await _users.QueryTrackedAsync(
            q => q.Where(x => x.MemberProfileId == null && x.IgnoredAtUtc == null && x.TerminalName != null),
            cancellationToken);

        var now = _clock.GetUtcNow().UtcDateTime;
        var linked = 0;

        foreach (var user in unlinked)
        {
            if (matches.GetValueOrDefault(NormalizeName(user.TerminalName!)) is { } member && linkedMembers.Add(member.Id))
            {
                user.MemberProfileId = member.Id;
                user.LinkedAtUtc = now;
                linked++;
            }
        }

        if (linked > 0)
        {
            await SaveAndResyncAsync(cancellationToken);

            await _audit.RecordAsync(
                AuditCategory.Membership,
                "TerminalUsersLinked",
                AuditOutcome.Succeeded,
                resourceType: "TerminalUser",
                reason: $"{linked} terminal users linked to members by matching names",
                cancellationToken: cancellationToken);
        }

        return ServiceResult<int>.Ok(linked);
    }

    public async Task<ServiceResult<TerminalUserDto>> SetIgnoredAsync(
        int terminalUserId,
        bool ignored,
        CancellationToken cancellationToken = default)
    {
        var (user, failure) = await LoadUserAsync(terminalUserId, cancellationToken);

        if (user is null)
        {
            return failure!;
        }

        if (ignored && user.MemberProfileId is not null)
        {
            return GymResults.Conflict<TerminalUserDto>("Unlink the member first.");
        }

        user.IgnoredAtUtc = ignored ? _clock.GetUtcNow().UtcDateTime : null;
        await _users.SaveChangesAsync(cancellationToken);

        return await SingleAsync(user.Id, cancellationToken);
    }

    public async Task<ServiceResult<TerminalUserDto>> CreateForMemberAsync(
        CreateTerminalUserRequest request,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymMembersManage, PlatformModules.GymAccess, cancellationToken);

        if (!authorization.Allowed)
        {
            return GymResults.Forbidden<TerminalUserDto>(authorization);
        }

        var member = await _members.FirstOrDefaultAsync(x => x.Id == request.MemberId && x.AnonymizedAtUtc == null, cancellationToken);

        if (member is null)
        {
            return GymResults.NotFound<TerminalUserDto>("Member");
        }

        if (await _users.FirstOrDefaultAsync(x => x.MemberProfileId == member.Id, cancellationToken) is { } existing)
        {
            return GymResults.Conflict<TerminalUserDto>($"{member.FullName} is already terminal user {existing.Pin}.");
        }

        var pins = await _users.QueryAsync(q => q.Select(x => x.Pin), cancellationToken);
        var next = Math.Max(FirstPin, pins.Select(x => long.TryParse(x, out var n) ? n + 1 : 0).DefaultIfEmpty(0).Max());
        var now = _clock.GetUtcNow().UtcDateTime;

        var user = new TerminalUser
        {
            TenantId = member.TenantId,
            Pin = next.ToString(CultureInfo.InvariantCulture),
            TerminalName = ZkTecoProtocol.TerminalName(member.FullName),
            MemberProfileId = member.Id,
            FirstSeenAtUtc = now,
            LinkedAtUtc = now
        };

        _users.Add(user);
        await SaveAndResyncAsync(cancellationToken);

        return await SingleAsync(user.Id, cancellationToken);
    }

    public async Task<ServiceResult<TerminalImportResultDto>> ImportAsync(
        ImportTerminalUsersRequest request,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymMembersManage, PlatformModules.GymAccess, cancellationToken);

        if (!authorization.Allowed)
        {
            return GymResults.Forbidden<TerminalImportResultDto>(authorization);
        }

        var result = new TerminalImportResultDto();
        var now = _clock.GetUtcNow().UtcDateTime;
        var tenantId = _access.TenantId!.Value;
        var rows = new Dictionary<string, string?>(StringComparer.Ordinal);
        var lineNumber = 0;

        foreach (var raw in request.Csv.Replace("\r", "").Split('\n'))
        {
            lineNumber++;
            var line = raw.Trim();

            if (line.Length == 0)
                continue;

            var parts = line.Split([',', ';', '\t'], 2);
            var pin = ZkTecoProtocol.Clean(parts[0].Trim().Trim('"'));

            if (pin is null)
            {
                // A header row ("PIN,Name" or "User ID,Name") is expected; anything else is reported.
                if (lineNumber > 1)
                    result.Errors.Add($"Line {lineNumber}: \"{Truncate(line, 40)}\" has no user number.");

                continue;
            }

            rows[pin] = parts.Length > 1 && parts[1].Trim().Trim('"') is { Length: > 0 } name ? Truncate(name, 100) : null;
        }

        var pins = rows.Keys.ToList();
        var existing = (await _users.QueryTrackedAsync(q => q.Where(x => pins.Contains(x.Pin)), cancellationToken))
            .ToDictionary(x => x.Pin, StringComparer.Ordinal);

        foreach (var (pin, name) in rows)
        {
            if (existing.TryGetValue(pin, out var user))
            {
                if (name is not null && user.TerminalName != name)
                {
                    user.TerminalName = name;
                    result.Updated++;
                }

                continue;
            }

            _users.Add(new TerminalUser { TenantId = tenantId, Pin = pin, TerminalName = name, FirstSeenAtUtc = now });
            result.Added++;
        }

        await _users.SaveChangesAsync(cancellationToken);

        await _audit.RecordAsync(
            AuditCategory.Membership,
            "TerminalUsersImported",
            AuditOutcome.Succeeded,
            resourceType: "TerminalUser",
            reason: $"{result.Added} added, {result.Updated} updated",
            cancellationToken: cancellationToken);

        return ServiceResult<TerminalImportResultDto>.Ok(result);
    }

    /// <summary>Saves, and makes every terminal compare its list at its next poll.</summary>
    private async Task SaveAndResyncAsync(CancellationToken cancellationToken)
    {
        foreach (var device in await _devices.ListTrackedAsync(
                     x => x.Kind == CheckInDeviceKind.ZkTecoTerminal && x.RevokedAtUtc == null,
                     cancellationToken))
        {
            device.LastSyncedAtUtc = null;
        }

        await _users.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Normalized full name → the one member with it (names shared by two members are left out).</summary>
    private async Task<Dictionary<string, MemberMatch>> NameMatchesAsync(CancellationToken cancellationToken)
    {
        var members = await _members.QueryAsync(
            q => q
                .Where(x => x.AnonymizedAtUtc == null && x.Status != MemberStatus.Cancelled)
                .Select(x => new { x.Id, x.FullName }),
            cancellationToken);

        return members
            .GroupBy(x => NormalizeName(x.FullName))
            .Where(x => x.Key.Length > 0 && x.Count() == 1)
            .ToDictionary(x => x.Key, x => new MemberMatch(x.Single().Id, x.Single().FullName));
    }

    /// <summary>"  Sara  KHAN." and "sara khan" match; terminals often cut names at 24 characters.</summary>
    public static string NormalizeName(string name)
    {
        var letters = new string(name.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray());
        var normalized = string.Join(' ', letters.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        return normalized.Length > ZkTecoProtocol.MaxNameLength ? normalized[..ZkTecoProtocol.MaxNameLength].TrimEnd() : normalized;
    }

    private async Task<(CheckInDevice? Device, ServiceResult<bool>? Failure)> LoadTerminalAsync(
        int deviceId,
        CancellationToken cancellationToken)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymSettingsManage, PlatformModules.GymAccess, cancellationToken);

        if (!authorization.Allowed)
        {
            return (null, GymResults.Forbidden<bool>(authorization));
        }

        var device = await _devices.FirstOrDefaultTrackedAsync(
            x => x.Id == deviceId && x.Kind == CheckInDeviceKind.ZkTecoTerminal && x.RevokedAtUtc == null,
            cancellationToken);

        return device is null
            ? (null, GymResults.NotFound<bool>("Terminal"))
            : (device, null);
    }

    private async Task<(TerminalUser? User, ServiceResult<TerminalUserDto>? Failure)> LoadUserAsync(
        int terminalUserId,
        CancellationToken cancellationToken)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymMembersManage, PlatformModules.GymAccess, cancellationToken);

        if (!authorization.Allowed)
        {
            return (null, GymResults.Forbidden<TerminalUserDto>(authorization));
        }

        var user = await _users.FirstOrDefaultTrackedAsync(x => x.Id == terminalUserId, cancellationToken);

        return user is null
            ? (null, GymResults.NotFound<TerminalUserDto>("Terminal user"))
            : (user, null);
    }

    private async Task<ServiceResult<TerminalUserDto>> SingleAsync(int id, CancellationToken cancellationToken)
    {
        var all = await ListUsersAsync(cancellationToken);

        return all.Success && all.Data!.FirstOrDefault(x => x.Id == id) is { } user
            ? ServiceResult<TerminalUserDto>.Ok(user)
            : GymResults.NotFound<TerminalUserDto>("Terminal user");
    }

    private static string Truncate(string value, int length) =>
        value.Length > length ? value[..length] : value;
}

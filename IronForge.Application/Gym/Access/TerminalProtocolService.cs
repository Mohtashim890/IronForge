using System.Globalization;
using IronForge.Application.Auditing.Models;
using IronForge.Application.Auditing.Services;
using IronForge.Application.Commons;
using IronForge.Application.Entities;
using IronForge.Application.Execution;
using IronForge.Application.Gym.Billing;
using IronForge.Application.Gym.CheckIns;
using IronForge.Application.Gym.Common;
using IronForge.Application.Persistence;
using IronForge.Shared.Authorization;
using IronForge.Shared.Models.Gym;
using IronForge.Shared.Subscriptions;

namespace IronForge.Application.Gym.Access;

/// <summary>
/// The terminal side of the ZKTeco push protocol (see <see cref="ZkTecoProtocol"/>).
/// Every request starts with <see cref="AuthenticateAsync"/>, which finds the
/// registered terminal by serial number and acts as that device in its gym.
/// </summary>
public interface ITerminalProtocolService
{
    Task<ServiceResult<bool>> AuthenticateAsync(
        string? serialNumber,
        string? remoteIp,
        CancellationToken cancellationToken = default);

    /// <summary>The terminal starts: its options. The first time, it is asked for its user list.</summary>
    Task<string> ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>The terminal polls: membership changes since the last poll become commands.</summary>
    Task<string> PollAsync(CancellationToken cancellationToken = default);

    /// <summary>ATTLOG (entries) or OPERLOG/USERINFO (users) upload; answers "OK: n".</summary>
    Task<string> UploadAsync(
        string? table,
        string body,
        CancellationToken cancellationToken = default);

    Task<string> ReportResultsAsync(
        string body,
        CancellationToken cancellationToken = default);
}

public sealed class TerminalProtocolService : ITerminalProtocolService
{
    /// <summary>How often a polling terminal's user list is compared with the memberships.</summary>
    public static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(1);

    /// <summary>A command sent but not confirmed is sent again after this.</summary>
    private static readonly TimeSpan ResendAfter = TimeSpan.FromMinutes(2);

    private const int MaxCommandsPerPoll = 50;

    /// <summary>Entries within this window of a recorded visit are the same visit.</summary>
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromMinutes(30);

    private readonly ICheckInDeviceDirectory _directory;
    private readonly IExecutionContextAccessor _executionContext;
    private readonly IGymAccessService _access;
    private readonly IRepository<Tenant> _tenants;
    private readonly IRepository<CheckInDevice> _devices;
    private readonly IRepository<TerminalUser> _users;
    private readonly IRepository<TerminalUserSync> _syncs;
    private readonly IRepository<TerminalCommand> _commands;
    private readonly IRepository<MemberProfile> _members;
    private readonly IRepository<Invoice> _invoices;
    private readonly IRepository<CheckIn> _checkIns;
    private readonly IBillingEngine _billing;
    private readonly IAuditService _audit;
    private readonly TimeProvider _clock;

    private CheckInDevice? _device;

    public TerminalProtocolService(
        ICheckInDeviceDirectory directory,
        IExecutionContextAccessor executionContext,
        IGymAccessService access,
        IRepository<Tenant> tenants,
        IRepository<CheckInDevice> devices,
        IRepository<TerminalUser> users,
        IRepository<TerminalUserSync> syncs,
        IRepository<TerminalCommand> commands,
        IRepository<MemberProfile> members,
        IRepository<Invoice> invoices,
        IRepository<CheckIn> checkIns,
        IBillingEngine billing,
        IAuditService audit,
        TimeProvider clock)
    {
        _directory = directory;
        _executionContext = executionContext;
        _access = access;
        _tenants = tenants;
        _devices = devices;
        _users = users;
        _syncs = syncs;
        _commands = commands;
        _members = members;
        _invoices = invoices;
        _checkIns = checkIns;
        _billing = billing;
        _audit = audit;
        _clock = clock;
    }

    public async Task<ServiceResult<bool>> AuthenticateAsync(
        string? serialNumber,
        string? remoteIp,
        CancellationToken cancellationToken = default)
    {
        if (_executionContext.Current is not null)
        {
            return ServiceResult<bool>.Fail("Terminal requests must not carry a sign-in token.", 400);
        }

        var serial = ZkTecoProtocol.NormalizeSerial(serialNumber);

        var device = serial.Length == 0
            ? null
            : await _directory.FindTerminalBySerialAsync(serial, cancellationToken);

        // A terminal proves nothing but its serial number, so an admin must have
        // registered it, and it can be held to the gym's fixed IP address.
        if (device is null || device.RevokedAtUtc is not null)
        {
            return ServiceResult<bool>.Fail("Unknown terminal. Register its serial number under Check-in devices.", 401);
        }

        if (!string.IsNullOrWhiteSpace(device.AllowedIp) &&
            !string.Equals(device.AllowedIp.Trim(), remoteIp, StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResult<bool>.Fail("This terminal is not allowed from this address.", 403);
        }

        var tenant = await _tenants.FirstOrDefaultAsync(x => x.Id == device.TenantId, cancellationToken);

        if (tenant is null || !tenant.HasAccess(_clock.GetUtcNow().UtcDateTime))
        {
            return ServiceResult<bool>.Fail("This gym's subscription is not active.", 403);
        }

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
                    Permissions.GymAccessTerminal
                }
            }
        });

        var authorization = await _access.AuthorizeAsync(
            Permissions.GymAccessTerminal,
            PlatformModules.GymAccess,
            cancellationToken);

        if (!authorization.Allowed)
        {
            return ServiceResult<bool>.Fail(authorization.Reason, 403);
        }

        _device = await _devices.FirstOrDefaultTrackedAsync(x => x.Id == device.Id, cancellationToken);
        await _directory.TouchAsync(device.Id, _clock.GetUtcNow().UtcDateTime, cancellationToken);

        return ServiceResult<bool>.Ok(true);
    }

    public async Task<string> ConnectAsync(CancellationToken cancellationToken = default)
    {
        var device = Device();

        // First contact: learn who is already enrolled (migrating an existing terminal).
        if (!await _commands.AnyAsync(x => x.CheckInDeviceId == device.Id, cancellationToken))
        {
            _commands.Add(NewCommand(device, ZkTecoProtocol.QueryUsers, pin: null));

            await _commands.SaveChangesAsync(cancellationToken);

            await _audit.RecordAsync(
                AuditCategory.Membership,
                "TerminalConnected",
                AuditOutcome.Succeeded,
                resourceType: "CheckInDevice",
                resourceId: device.Id.ToString(CultureInfo.InvariantCulture),
                reason: $"{device.Name} ({device.SerialNumber}) connected for the first time; its user list was requested",
                cancellationToken: cancellationToken);
        }

        return ZkTecoProtocol.Options(device.SerialNumber!);
    }

    public async Task<string> PollAsync(CancellationToken cancellationToken = default)
    {
        var device = Device();
        var now = _clock.GetUtcNow().UtcDateTime;

        if (device.LastSyncedAtUtc is null || now - device.LastSyncedAtUtc >= SyncInterval)
        {
            await SyncAsync(device, cancellationToken);
        }

        var resendBefore = now - ResendAfter;

        var pending = await _commands.QueryTrackedAsync(
            q => q
                .Where(x => x.CheckInDeviceId == device.Id &&
                            x.CompletedAtUtc == null &&
                            (x.SentAtUtc == null || x.SentAtUtc < resendBefore))
                .OrderBy(x => x.Id)
                .Take(MaxCommandsPerPoll),
            cancellationToken);

        foreach (var command in pending)
        {
            command.SentAtUtc = now;
        }

        await _commands.SaveChangesAsync(cancellationToken);

        return ZkTecoProtocol.Commands(pending.Select(x => (x.Id, x.Text)));
    }

    public async Task<string> UploadAsync(
        string? table,
        string body,
        CancellationToken cancellationToken = default)
    {
        var device = Device();

        var count = (table ?? "").ToUpperInvariant() switch
        {
            "ATTLOG" => await RecordEntriesAsync(device, ZkTecoProtocol.ParseEntries(body), cancellationToken),
            "OPERLOG" or "USERINFO" => await RecordUsersAsync(ZkTecoProtocol.ParseUsers(body), cancellationToken),
            // Photos, operation logs and other tables are not used.
            _ => 0
        };

        return $"OK: {count}";
    }

    public async Task<string> ReportResultsAsync(
        string body,
        CancellationToken cancellationToken = default)
    {
        var device = Device();
        var now = _clock.GetUtcNow().UtcDateTime;
        var results = ZkTecoProtocol.ParseResults(body);
        var ids = results.Select(x => x.Id).ToList();

        var commands = await _commands.QueryTrackedAsync(
            q => q.Where(x => x.CheckInDeviceId == device.Id && ids.Contains(x.Id)),
            cancellationToken);

        foreach (var command in commands)
        {
            var code = results.First(x => x.Id == command.Id).ReturnCode;

            command.CompletedAtUtc = now;
            command.ReturnCode = code;

            if (command.Pin is not { } pin)
                continue;

            var sync = await _syncs.FirstOrDefaultTrackedAsync(
                x => x.CheckInDeviceId == device.Id && x.Pin == pin,
                cancellationToken);

            if (sync is null)
                continue;

            if (code >= 0)
            {
                sync.ConfirmedAtUtc = now;
            }
            else
            {
                // The terminal refused it: forget what was sent so the next sync tries again.
                _syncs.Remove(sync);
            }
        }

        await _commands.SaveChangesAsync(cancellationToken);

        return "OK";
    }

    /// <summary>
    /// Compares what the terminal was last told with what the memberships say
    /// now, and queues an update for every user that changed. Only users
    /// linked to a member (or unlinked since) are touched: staff and other
    /// people enrolled on the terminal stay as they are.
    /// </summary>
    private async Task SyncAsync(CheckInDevice device, CancellationToken cancellationToken)
    {
        var gym = await _billing.GetGymAsync(device.TenantId, cancellationToken);
        var timeZone = gym?.Settings.TimeZoneId;
        var today = GymCalendar.Today(_clock, timeZone);
        var grace = gym?.Settings.GracePeriodDays ?? 14;
        var now = _clock.GetUtcNow().UtcDateTime;

        var linked = await _users.QueryAsync(
            q => q.Where(x => x.MemberProfileId != null && x.IgnoredAtUtc == null),
            cancellationToken);

        var memberIds = linked.Select(x => x.MemberProfileId!.Value).ToList();

        var members = (await _members.QueryAsync(
                q => q.Where(x => memberIds.Contains(x.Id)),
                cancellationToken))
            .ToDictionary(x => x.Id);

        var overdue = (await _invoices.QueryAsync(
                q => q
                    .Where(x => memberIds.Contains(x.MemberProfileId) &&
                                (x.Status == InvoiceStatus.Overdue ||
                                 (x.Status == InvoiceStatus.Open && x.DueDate < today)))
                    .Select(x => new { x.MemberProfileId, x.Number, x.Amount, x.Currency, x.DueDate }),
                cancellationToken))
            .GroupBy(x => x.MemberProfileId)
            .ToDictionary(
                x => x.Key,
                x => x.OrderBy(i => i.DueDate).Select(i => new OverdueInvoice(i.Number, i.Amount, i.Currency, i.DueDate)).First());

        var desired = new Dictionary<string, (string Name, TerminalAccess Access)>(StringComparer.Ordinal);

        foreach (var user in linked)
        {
            if (!members.TryGetValue(user.MemberProfileId!.Value, out var member))
                continue;

            desired[user.Pin] = (
                member.FullName,
                TerminalAccessRules.For(member, overdue.GetValueOrDefault(member.Id), today, grace));
        }

        var synced = (await _syncs.QueryTrackedAsync(
                q => q.Where(x => x.CheckInDeviceId == device.Id),
                cancellationToken))
            .ToDictionary(x => x.Pin, StringComparer.Ordinal);

        // Unlinked since the last sync: no membership vouches for them any more.
        foreach (var (pin, sync) in synced)
        {
            if (!desired.ContainsKey(pin) && sync.Enabled)
            {
                desired[pin] = (sync.Name, new TerminalAccess(false, today.AddDays(-1)));
            }
        }

        foreach (var (pin, (name, access)) in desired)
        {
            var terminalName = ZkTecoProtocol.TerminalName(name);

            if (synced.TryGetValue(pin, out var sync) &&
                sync.Enabled == access.Enabled &&
                (!access.Enabled || sync.ValidUntil == access.ValidUntil) &&
                sync.Name == terminalName)
            {
                continue;
            }

            if (sync is null)
            {
                sync = new TerminalUserSync { TenantId = device.TenantId, CheckInDeviceId = device.Id, Pin = pin };
                _syncs.Add(sync);
            }

            sync.Name = terminalName;
            sync.Enabled = access.Enabled;
            sync.ValidUntil = access.ValidUntil;
            sync.QueuedAtUtc = now;
            sync.ConfirmedAtUtc = null;

            // A newer update replaces one the terminal has not received yet.
            foreach (var stale in await _commands.QueryTrackedAsync(
                         q => q.Where(x => x.CheckInDeviceId == device.Id && x.Pin == pin && x.CompletedAtUtc == null),
                         cancellationToken))
            {
                stale.CompletedAtUtc = now;
                stale.ReturnCode = int.MinValue;
            }

            _commands.Add(NewCommand(
                device,
                ZkTecoProtocol.UpdateUser(pin, terminalName, access.Enabled, access.ValidUntil, today),
                pin));
        }

        device.LastSyncedAtUtc = now;

        await _commands.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Entries the terminal already allowed at the door become visits. When
    /// IronForge would have refused (the terminal had an older list), the
    /// visit is kept with a warning and the terminal is synced at once.
    /// </summary>
    private async Task<int> RecordEntriesAsync(
        CheckInDevice device,
        List<TerminalEntry> entries,
        CancellationToken cancellationToken)
    {
        if (entries.Count == 0)
            return 0;

        var gym = await _billing.GetGymAsync(device.TenantId, cancellationToken);
        var timeZone = gym?.Settings.TimeZoneId;
        var zone = GymCalendar.ResolveTimeZone(timeZone);
        var today = GymCalendar.Today(_clock, timeZone);
        var grace = gym?.Settings.GracePeriodDays ?? 14;
        var now = _clock.GetUtcNow().UtcDateTime;
        var accepted = 0;

        foreach (var entry in entries)
        {
            accepted++;

            var atUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(entry.LocalTime, DateTimeKind.Unspecified), zone);

            // A terminal clock far in the future cannot create visits ahead of time.
            if (atUtc > now.AddMinutes(10))
                continue;

            var user = await _users.FirstOrDefaultTrackedAsync(x => x.Pin == entry.Pin, cancellationToken);

            if (user is null)
            {
                // Enrolled on the terminal but not known yet: list it for staff to link.
                _users.Add(new TerminalUser { TenantId = device.TenantId, Pin = entry.Pin, FirstSeenAtUtc = now, LastEntryAtUtc = atUtc });
                await _users.SaveChangesAsync(cancellationToken);
                continue;
            }

            if (user.LastEntryAtUtc is null || user.LastEntryAtUtc < atUtc)
            {
                user.LastEntryAtUtc = atUtc;
            }

            if (user.MemberProfileId is not { } memberId || user.IgnoredAtUtc is not null)
            {
                await _users.SaveChangesAsync(cancellationToken);
                continue;
            }

            var member = await _members.FirstOrDefaultAsync(x => x.Id == memberId && x.AnonymizedAtUtc == null, cancellationToken);

            if (member is null)
            {
                await _users.SaveChangesAsync(cancellationToken);
                continue;
            }

            var from = atUtc - DuplicateWindow;
            var to = atUtc + DuplicateWindow;

            if (await _checkIns.AnyAsync(x => x.MemberProfileId == memberId && x.CheckedInAtUtc >= from && x.CheckedInAtUtc <= to, cancellationToken))
            {
                await _users.SaveChangesAsync(cancellationToken);
                continue;
            }

            var overdue = (await _invoices.QueryAsync(
                    q => q
                        .Where(x => x.MemberProfileId == memberId &&
                                    (x.Status == InvoiceStatus.Overdue ||
                                     (x.Status == InvoiceStatus.Open && x.DueDate < today)))
                        .OrderBy(x => x.DueDate)
                        .Take(1)
                        .Select(x => new OverdueInvoice(x.Number, x.Amount, x.Currency, x.DueDate)),
                    cancellationToken))
                .FirstOrDefault();

            var decision = CheckInPolicy.Evaluate(member, overdue, today, grace);

            string? warning = null;

            if (!decision.Allowed)
            {
                warning = Truncate($"Entered at {device.Name} although: {decision.Message} The terminal had not received the update yet.", 300);

                // Send the refusal now rather than at the next scheduled sync.
                device.LastSyncedAtUtc = null;

                await _audit.RecordAsync(
                    AuditCategory.Membership,
                    "TerminalEntryNotAllowed",
                    AuditOutcome.Denied,
                    resourceType: "MemberProfile",
                    resourceId: member.Id.ToString(CultureInfo.InvariantCulture),
                    reason: warning,
                    cancellationToken: cancellationToken);
            }

            _checkIns.Add(new CheckIn
            {
                TenantId = device.TenantId,
                MemberProfileId = member.Id,
                CheckedInAtUtc = atUtc,
                Method = CheckInMethod.Terminal,
                CheckInDeviceId = device.Id,
                Warning = warning
            });

            await _checkIns.SaveChangesAsync(cancellationToken);
        }

        return accepted;
    }

    /// <summary>
    /// The terminal's user list: new numbers are added for staff to link,
    /// names and "has a fingerprint" are refreshed. No template is stored.
    /// </summary>
    private async Task<int> RecordUsersAsync(
        List<TerminalUserRecord> records,
        CancellationToken cancellationToken)
    {
        if (records.Count == 0)
            return 0;

        var device = Device();
        var now = _clock.GetUtcNow().UtcDateTime;
        var pins = records.Select(x => x.Pin).ToList();

        var existing = (await _users.QueryTrackedAsync(
                q => q.Where(x => pins.Contains(x.Pin)),
                cancellationToken))
            .ToDictionary(x => x.Pin, StringComparer.Ordinal);

        foreach (var record in records)
        {
            if (!existing.TryGetValue(record.Pin, out var user))
            {
                user = new TerminalUser { TenantId = device.TenantId, Pin = record.Pin, FirstSeenAtUtc = now };
                _users.Add(user);
                existing[record.Pin] = user;
            }

            if (record.Name is not null)
            {
                user.TerminalName = Truncate(record.Name, 100);
            }

            user.HasFingerprint |= record.HasFingerprint;
        }

        await _users.SaveChangesAsync(cancellationToken);

        return records.Count;
    }

    private TerminalCommand NewCommand(CheckInDevice device, string text, string? pin) => new()
    {
        TenantId = device.TenantId,
        CheckInDeviceId = device.Id,
        Text = text,
        Pin = pin,
        CreatedAtUtc = _clock.GetUtcNow().UtcDateTime
    };

    private CheckInDevice Device() =>
        _device ?? throw new InvalidOperationException("The terminal is not authenticated.");

    private static string Truncate(string value, int length) =>
        value.Length > length ? value[..length] : value;
}

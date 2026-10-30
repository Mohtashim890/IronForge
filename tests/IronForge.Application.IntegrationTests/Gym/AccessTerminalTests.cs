using IronForge.Application.Entities;
using IronForge.Application.Gym.Common;
using IronForge.Application.IntegrationTests.Infrastructure;
using IronForge.Shared.Authorization;
using IronForge.Shared.Models.Gym;
using IronForge.Shared.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace IronForge.Application.IntegrationTests.Gym;

/// <summary>
/// ZKTeco fingerprint terminals against PostgreSQL: registering by serial
/// number, migrating the terminal's users (never their fingerprints), the
/// sync that lets the terminal decide offline, entries becoming visits, the
/// module gate, tenant isolation, and the one-phone rule of the member pass.
/// </summary>
public sealed class AccessTerminalTests
    : IntegrationTestBase,
      IClassFixture<PostgreSqlTestDatabase>
{
    private static readonly DateTimeOffset Start = new(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);

    private const string GymIp = "203.0.113.10";

    private readonly TestClock _clock = new(Start);

    public AccessTerminalTests(PostgreSqlTestDatabase database) : base(database)
    {
    }

    private DateOnly Today => DateOnly.FromDateTime(_clock.Now.UtcDateTime);

    // ---- Migration of an existing terminal ----------------------------------

    [Fact]
    public async Task ExistingTerminal_UploadsItsUsers_WithoutFingerprints_AndStaffLinkThem()
    {
        var gym = await SeedGymAsync();
        var sara = await SeedMemberAsync(gym, "Sara Khan", paidThrough: Today.AddDays(20));
        var serial = await RegisterAsync(gym, "CJDE212460012");

        // First contact: the terminal is asked for its user list.
        var options = await AsTerminal(serial, s => s.Terminal.ConnectAsync());
        Assert.StartsWith("GET OPTION FROM: CJDE212460012", options);

        var poll = await AsTerminal(serial, s => s.Terminal.PollAsync());
        Assert.Contains(":DATA QUERY USERINFO", poll);

        var uploaded = await AsTerminal(serial, s => s.Terminal.UploadAsync("OPERLOG",
            "USER PIN=1001\tName=SARA KHAN\tPri=0\tPasswd=\tCard=\tGrp=1\n" +
            "FP PIN=1001\tFID=6\tSize=1184\tValid=1\tTMP=TOP-SECRET-FINGERPRINT-TEMPLATE\n" +
            "USER PIN=1002\tName=Coach Bilal\tPri=14\n"));
        Assert.Equal("OK: 2", uploaded);

        var users = (await AsAdmin(gym, s => s.TerminalAdmin.ListUsersAsync())).Data!;
        var saraOnTerminal = users.Single(x => x.Pin == "1001");

        Assert.True(saraOnTerminal.HasFingerprint);
        Assert.Equal(sara, saraOnTerminal.SuggestedMemberId);
        Assert.Null(users.Single(x => x.Pin == "1002").SuggestedMemberId);

        // The template is never stored, in any table.
        await using (var db = Database.CreateDbContext())
        {
            Assert.False(await db.TerminalUsers.IgnoreQueryFilters().AnyAsync(x => x.TerminalName!.Contains("TOP-SECRET")));
            Assert.False(await db.TerminalCommands.IgnoreQueryFilters().AnyAsync(x => x.Text.Contains("TOP-SECRET")));
            Assert.False(await db.AuditEvents.IgnoreQueryFilters().AnyAsync(x => x.Reason!.Contains("TOP-SECRET")));
        }

        var linked = await AsAdmin(gym, s => s.TerminalAdmin.LinkSuggestedAsync());
        Assert.Equal(1, linked.Data);

        // The next poll sends Sara's access with an end date; the staff user is left alone.
        var sync = await AsTerminal(serial, s => s.Terminal.PollAsync());
        var expectedEnd = Today.AddDays(21 + 14).ToString("yyyy-MM-dd");

        Assert.Contains("DATA UPDATE USERINFO PIN=1001\tName=Sara Khan", sync);
        Assert.Contains($"EndDatetime={expectedEnd} 23:59:59", sync);
        Assert.DoesNotContain("PIN=1002", sync);
        Assert.DoesNotContain("DATA DELETE", sync);

        var commandId = long.Parse(sync.Split('\n').Single(x => x.Contains("PIN=1001")).Split(':')[1]);
        await AsTerminal(serial, s => s.Terminal.ReportResultsAsync($"ID={commandId}&Return=0&CMD=DATA"));

        var listed = (await AsAdmin(gym, s => s.CheckInDevices.ListAsync())).Data!.Single();
        Assert.Equal(1, listed.UsersInSync);
        Assert.Equal(0, listed.UsersPending);
    }

    [Fact]
    public async Task ExpiredMember_IsTurnedOffOnTheTerminal_AtTheNextSync()
    {
        var gym = await SeedGymAsync();
        var sara = await SeedMemberAsync(gym, "Sara Khan", paidThrough: Today.AddDays(5));
        var serial = await RegisterAsync(gym, "SN0001");
        await LinkAsync(gym, serial, "1001", sara);

        await using (var db = Database.CreateDbContext())
        {
            var member = await db.MemberProfiles.IgnoreQueryFilters().SingleAsync(x => x.Id == sara);
            member.Status = MemberStatus.Expired;
            await db.SaveChangesAsync();
        }

        // Nothing changes until the next sync (once a minute).
        Assert.Equal("OK", await AsTerminal(serial, s => s.Terminal.PollAsync()));

        _clock.Now = _clock.Now.AddMinutes(2);
        var update = await AsTerminal(serial, s => s.Terminal.PollAsync());

        Assert.Contains("PIN=1001", update);
        Assert.Contains($"EndDatetime={Today.AddDays(-1):yyyy-MM-dd} 23:59:59", update);
    }

    [Fact]
    public async Task UnlinkingAMember_TurnsTheirFingerOff()
    {
        var gym = await SeedGymAsync();
        var sara = await SeedMemberAsync(gym, "Sara Khan", paidThrough: Today.AddDays(5));
        var serial = await RegisterAsync(gym, "SN0002");
        var user = await LinkAsync(gym, serial, "1001", sara);

        await AsAdmin(gym, s => s.TerminalAdmin.LinkAsync(user, new LinkTerminalUserRequest { MemberId = null }));

        var update = await AsTerminal(serial, s => s.Terminal.PollAsync());

        Assert.Contains($"PIN=1001\tName=Sara Khan", update);
        Assert.Contains($"EndDatetime={Today.AddDays(-1):yyyy-MM-dd}", update);
    }

    // ---- Entries ---------------------------------------------------------------

    [Fact]
    public async Task TerminalEntries_BecomeVisits_OnceEach()
    {
        var gym = await SeedGymAsync();
        var sara = await SeedMemberAsync(gym, "Sara Khan", paidThrough: Today.AddDays(5));
        var serial = await RegisterAsync(gym, "SN0003");
        await LinkAsync(gym, serial, "1001", sara);

        var log = $"1001\t{_clock.Now.UtcDateTime:yyyy-MM-dd HH:mm:ss}\t0\t1\t0\t0\t0\n" +
                  $"9999\t{_clock.Now.UtcDateTime:yyyy-MM-dd HH:mm:ss}\t0\t1\t0\t0\t0\n" +
                  "garbage line\n";

        Assert.Equal("OK: 2", await AsTerminal(serial, s => s.Terminal.UploadAsync("ATTLOG", log)));

        // The terminal resends after a lost answer: still one visit.
        await AsTerminal(serial, s => s.Terminal.UploadAsync("ATTLOG", log));

        await using var db = Database.CreateDbContext();
        var visit = await db.CheckIns.IgnoreQueryFilters().SingleAsync();

        Assert.Equal(sara, visit.MemberProfileId);
        Assert.Equal(CheckInMethod.Terminal, visit.Method);
        Assert.Null(visit.Warning);

        // The unknown user 9999 is listed for staff to link.
        Assert.True(await db.TerminalUsers.IgnoreQueryFilters().AnyAsync(x => x.Pin == "9999" && x.MemberProfileId == null));
    }

    [Fact]
    public async Task EntryTheRulesWouldRefuse_IsKeptWithAWarning_AndTheTerminalIsSyncedAtOnce()
    {
        var gym = await SeedGymAsync();
        var sara = await SeedMemberAsync(gym, "Sara Khan", status: MemberStatus.Frozen);
        var serial = await RegisterAsync(gym, "SN0004");
        await LinkAsync(gym, serial, "1001", sara);

        // While offline the terminal still had Sara as allowed.
        await AsTerminal(serial, s => s.Terminal.UploadAsync("ATTLOG",
            $"1001\t{_clock.Now.UtcDateTime:yyyy-MM-dd HH:mm:ss}\t0\t1\t0\t0\t0\n"));

        await using var db = Database.CreateDbContext();
        var visit = await db.CheckIns.IgnoreQueryFilters().SingleAsync();
        var terminal = await db.CheckInDevices.IgnoreQueryFilters().SingleAsync();

        Assert.Contains("frozen", visit.Warning);
        Assert.Null(terminal.LastSyncedAtUtc);
        Assert.True(await db.AuditEvents.IgnoreQueryFilters().AnyAsync(x => x.Action == "TerminalEntryNotAllowed"));
    }

    // ---- Who may talk to the terminal endpoints ---------------------------------

    [Fact]
    public async Task UnknownRemovedOrWrongPlaceTerminals_AreRefused()
    {
        var gym = await SeedGymAsync();
        var serial = await RegisterAsync(gym, "SN0005", allowedIp: GymIp);

        Assert.Equal(401, (await AuthenticateAsync("NOT-REGISTERED", GymIp)).StatusCode);
        Assert.Equal(403, (await AuthenticateAsync(serial, "198.51.100.7")).StatusCode);
        Assert.True((await AuthenticateAsync(serial, GymIp)).Success);

        var device = (await AsAdmin(gym, s => s.CheckInDevices.ListAsync())).Data!.Single();
        await AsAdmin(gym, s => s.CheckInDevices.RevokeAsync(device.Id));

        Assert.Equal(401, (await AuthenticateAsync(serial, GymIp)).StatusCode);
    }

    [Fact]
    public async Task Terminal_CanDoNothingButItsProtocol()
    {
        var gym = await SeedGymAsync();
        var serial = await RegisterAsync(gym, "SN0006");

        var members = await AsTerminal(serial, s => s.GymMembers.ListAsync(new MemberQuery()));
        var scan = await AsTerminal(serial, s => s.CheckIns.ScanPassAsync("IF1.1.1.1.1.0123456789abcdef01234567"));

        Assert.Equal(403, members.StatusCode);
        Assert.Equal(403, scan.StatusCode);
    }

    [Fact]
    public async Task AccessControl_NeedsTheModule()
    {
        var professional = await SeedGymAsync(SubscriptionPlans.Professional.Id);

        var register = await AsAdmin(professional, s => s.TerminalAdmin.RegisterAsync(
            new RegisterTerminalRequest { Name = "Door", SerialNumber = "SN0007" }));
        Assert.Equal(403, register.StatusCode);

        // A gym that drops the module: its terminals stop being served.
        var enterprise = await SeedGymAsync();
        var serial = await RegisterAsync(enterprise, "SN0008");

        await using (var db = Database.CreateDbContext())
        {
            var tenant = await db.Tenants.SingleAsync(x => x.Id == enterprise.TenantId);
            tenant.Plan = SubscriptionPlans.Professional.Id;
            await db.SaveChangesAsync();
        }

        Assert.Equal(403, (await AuthenticateAsync(serial, GymIp)).StatusCode);
    }

    [Fact]
    public async Task SerialNumbers_AreUniqueOnThePlatform_AndTerminalUsersStayInTheirGym()
    {
        var gymA = await SeedGymAsync();
        var gymB = await SeedGymAsync();
        await RegisterAsync(gymA, "SN0009");

        var duplicate = await AsAdmin(gymB, s => s.TerminalAdmin.RegisterAsync(
            new RegisterTerminalRequest { Name = "Door", SerialNumber = "sn-0009" }));
        Assert.Equal(409, duplicate.StatusCode);

        await AsTerminal("SN0009", s => s.Terminal.UploadAsync("OPERLOG", "USER PIN=1001\tName=Someone\n"));

        Assert.Single((await AsAdmin(gymA, s => s.TerminalAdmin.ListUsersAsync())).Data!);
        Assert.Empty((await AsAdmin(gymB, s => s.TerminalAdmin.ListUsersAsync())).Data!);
    }

    [Fact]
    public async Task TheTerminalsOwnList_IsTheSource_AndNewMembersGetTheNextNumber()
    {
        var gym = await SeedGymAsync();
        var sara = await SeedMemberAsync(gym, "Sara Khan");
        var omar = await SeedMemberAsync(gym, "Omar Ali");
        var serial = await RegisterAsync(gym, "SN0010");

        await AsTerminal(serial, s => s.Terminal.UploadAsync("OPERLOG",
            "USER PIN=1001\tName=Sara Khan\nUSER PIN=1005\tName=Coach Bilal\n"));

        var created = await AsAdmin(gym, s => s.TerminalAdmin.CreateForMemberAsync(new CreateTerminalUserRequest { MemberId = omar }));
        Assert.Equal("1006", created.Data!.Pin);
        Assert.Equal(omar, created.Data.MemberId);

        var again = await AsAdmin(gym, s => s.TerminalAdmin.CreateForMemberAsync(new CreateTerminalUserRequest { MemberId = omar }));
        Assert.Equal(409, again.StatusCode);

        var users = (await AsAdmin(gym, s => s.TerminalAdmin.ListUsersAsync())).Data!;
        Assert.Equal(sara, users.Single(x => x.Pin == "1001").SuggestedMemberId);

        // The new number reaches the terminal at its next poll, so the member can enrol a finger there.
        var poll = await AsTerminal(serial, s => s.Terminal.PollAsync());
        Assert.Contains("DATA UPDATE USERINFO PIN=1006\tName=Omar Ali", poll);
    }

    [Fact]
    public async Task Trainer_CannotLinkTerminalUsers()
    {
        var gym = await SeedGymAsync();
        var trainer = await SeedUserAsync(gym.TenantId, "trainer@gym.test", TenantRoles.Trainer);

        var result = await AsUser(gym, trainer, TenantRoles.Trainer, s => s.TerminalAdmin.LinkSuggestedAsync());

        Assert.Equal(403, result.StatusCode);
    }

    // ---- One phone per pass (against handing over or sharing the account) ----

    [Fact]
    public async Task Pass_WorksOnOnePhone_AndMovingItResetsThePassAndTellsTheMember()
    {
        var gym = await SeedGymAsync();
        var sara = await SeedMemberAsync(gym, "Sara Khan");
        var userId = await UserOfAsync(sara);

        var first = await AsUser(gym, userId, TenantRoles.Member, s => s.MemberPasses.GetMyPassAsync("phone-a"));
        Assert.True(first.Success, first.ErrorMessage);
        Assert.True(first.Data!.RequirePhoneUnlock);

        var otherPhone = await AsUser(gym, userId, TenantRoles.Member, s => s.MemberPasses.GetMyPassAsync("phone-b"));
        Assert.Equal(423, otherPhone.StatusCode);

        var noPhone = await AsUser(gym, userId, TenantRoles.Member, s => s.MemberPasses.GetMyPassAsync(null));
        Assert.Equal(400, noPhone.StatusCode);

        var moved = await AsUser(gym, userId, TenantRoles.Member, s => s.MemberPasses.MoveMyPassAsync("phone-b"));
        Assert.Equal(first.Data.KeyVersion + 1, moved.Data!.KeyVersion);

        // Now the first phone is the "other" one, and its old codes are dead (new version).
        var back = await AsUser(gym, userId, TenantRoles.Member, s => s.MemberPasses.GetMyPassAsync("phone-a"));
        Assert.Equal(423, back.StatusCode);

        await using var db = Database.CreateDbContext();
        Assert.True(await db.EmailOutbox.AnyAsync(x => x.Subject.Contains("moved to another phone")));
        Assert.True(await db.AuditEvents.IgnoreQueryFilters().AnyAsync(x => x.Action == "PassMovedToAnotherPhone"));
    }

    [Fact]
    public async Task StaffReset_FreesThePassFromItsPhone()
    {
        var gym = await SeedGymAsync();
        var sara = await SeedMemberAsync(gym, "Sara Khan");
        var userId = await UserOfAsync(sara);

        await AsUser(gym, userId, TenantRoles.Member, s => s.MemberPasses.GetMyPassAsync("phone-a"));
        await AsAdmin(gym, s => s.MemberPasses.ResetPassAsync(sara));

        var newPhone = await AsUser(gym, userId, TenantRoles.Member, s => s.MemberPasses.GetMyPassAsync("phone-b"));
        Assert.True(newPhone.Success, newPhone.ErrorMessage);
    }

    // ---- Helpers ------------------------------------------------------------------

    private sealed record SeededGym(int TenantId, int PlanId, int AdminUserId);

    private async Task<SeededGym> SeedGymAsync(string? plan = null)
    {
        await using var db = Database.CreateDbContext();

        var tenant = new Tenant
        {
            Name = "Gym",
            Slug = $"gym-{Guid.NewGuid():N}",
            IsActive = true,
            CreatedAtUtc = Start.UtcDateTime,
            Plan = plan ?? SubscriptionPlans.Enterprise.Id
        };

        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var membershipPlan = new MembershipPlan
        {
            TenantId = tenant.Id,
            Name = "Monthly",
            Price = 50m,
            BillingInterval = BillingInterval.Monthly,
            CreatedAtUtc = Start.UtcDateTime
        };

        db.GymSettings.Add(new GymSettings
        {
            TenantId = tenant.Id,
            JoinCode = GymCalendar.NewJoinCode(),
            Currency = "USD",
            TimeZoneId = "UTC",
            GracePeriodDays = 14,
            CreatedAtUtc = Start.UtcDateTime,
            UpdatedAtUtc = Start.UtcDateTime
        });
        db.MembershipPlans.Add(membershipPlan);
        await db.SaveChangesAsync();

        var admin = await SeedUserAsync(tenant.Id, $"admin{tenant.Id}@gym.test", TenantRoles.Admin);

        return new SeededGym(tenant.Id, membershipPlan.Id, admin);
    }

    private async Task<int> SeedUserAsync(int tenantId, string email, string role)
    {
        await using var db = Database.CreateDbContext();

        var user = new User
        {
            Username = email.Replace("@", "_") + Guid.NewGuid().ToString("N")[..6],
            Email = email,
            Role = PlatformRoles.User,
            IsActive = true,
            CreatedAtUtc = Start.UtcDateTime,
            PasswordHash = "not-used"
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.TenantMemberships.Add(new TenantMembership
        {
            TenantId = tenantId,
            UserId = user.Id,
            Role = role,
            IsActive = true,
            CreatedAtUtc = Start.UtcDateTime
        });

        await db.SaveChangesAsync();

        return user.Id;
    }

    private async Task<int> SeedMemberAsync(
        SeededGym gym,
        string fullName,
        MemberStatus status = MemberStatus.Active,
        DateOnly? paidThrough = null)
    {
        var email = fullName.Replace(" ", ".").ToLowerInvariant() + "@example.com";
        var userId = await SeedUserAsync(gym.TenantId, email, TenantRoles.Member);

        await using var db = Database.CreateDbContext();

        var member = new MemberProfile
        {
            TenantId = gym.TenantId,
            UserId = userId,
            FullName = fullName,
            Email = email,
            MembershipPlanId = gym.PlanId,
            Status = status,
            JoinedAtUtc = Start.UtcDateTime,
            ApprovedAtUtc = Start.UtcDateTime,
            PaidThrough = paidThrough,
            FrozenUntil = status == MemberStatus.Frozen ? DateOnly.FromDateTime(Start.UtcDateTime).AddDays(10) : null
        };

        db.MemberProfiles.Add(member);
        await db.SaveChangesAsync();

        return member.Id;
    }

    private async Task<int> UserOfAsync(int memberId)
    {
        await using var db = Database.CreateDbContext();
        return (await db.MemberProfiles.IgnoreQueryFilters().SingleAsync(x => x.Id == memberId)).UserId!.Value;
    }

    private async Task<string> RegisterAsync(SeededGym gym, string serial, string? allowedIp = null)
    {
        var result = await AsAdmin(gym, s => s.TerminalAdmin.RegisterAsync(
            new RegisterTerminalRequest { Name = "Front door", SerialNumber = serial, AllowedIp = allowedIp }));

        Assert.True(result.Success, result.ErrorMessage);
        return result.Data!.SerialNumber!;
    }

    /// <summary>Puts a user on the terminal and links them to the member; the first sync is consumed.</summary>
    private async Task<int> LinkAsync(SeededGym gym, string serial, string pin, int memberId)
    {
        await AsTerminal(serial, s => s.Terminal.UploadAsync("OPERLOG", $"USER PIN={pin}\tName=Someone\n"));

        var user = (await AsAdmin(gym, s => s.TerminalAdmin.ListUsersAsync())).Data!.Single(x => x.Pin == pin);
        var linked = await AsAdmin(gym, s => s.TerminalAdmin.LinkAsync(user.Id, new LinkTerminalUserRequest { MemberId = memberId }));
        Assert.True(linked.Success, linked.ErrorMessage);

        await AsTerminal(serial, s => s.Terminal.PollAsync());

        return user.Id;
    }

    private async Task<IronForge.Application.Commons.ServiceResult<bool>> AuthenticateAsync(string serial, string ip)
    {
        var accessor = new TestExecutionContextAccessor();
        await using var db = Database.CreateDbContext(accessor);

        return await new TestServiceFactory(db, accessor) { Clock = _clock }.Terminal.AuthenticateAsync(serial, ip);
    }

    /// <summary>A request from the terminal: it authenticates with its serial number first.</summary>
    private async Task<T> AsTerminal<T>(string serial, Func<TestServiceFactory, Task<T>> action)
    {
        var accessor = new TestExecutionContextAccessor();
        await using var db = Database.CreateDbContext(accessor);
        var services = new TestServiceFactory(db, accessor) { Clock = _clock };

        var authenticated = await services.Terminal.AuthenticateAsync(serial, GymIp);
        Assert.True(authenticated.Success, authenticated.ErrorMessage);

        return await action(services);
    }

    private Task<T> AsAdmin<T>(SeededGym gym, Func<TestServiceFactory, Task<T>> action) =>
        AsUser(gym, gym.AdminUserId, TenantRoles.Admin, action);

    private async Task<T> AsUser<T>(SeededGym gym, int userId, string role, Func<TestServiceFactory, Task<T>> action)
    {
        var context = TestServiceFactory.AsRole(gym.TenantId, userId, role);
        await using var db = Database.CreateDbContext(context);

        return await action(new TestServiceFactory(db, context) { Clock = _clock });
    }
}

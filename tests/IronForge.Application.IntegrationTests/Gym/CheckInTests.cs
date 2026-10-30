using IronForge.Application.Entities;
using IronForge.Application.Gym.Common;
using IronForge.Application.IntegrationTests.Infrastructure;
using IronForge.Shared.Authorization;
using IronForge.Shared.CheckIns;
using IronForge.Shared.Models.Gym;
using IronForge.Shared.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace IronForge.Application.IntegrationTests.Gym;

/// <summary>
/// Check-in against PostgreSQL: the rotating member pass, the grace-period
/// rule, check-in devices (pairing, device-only authority, revocation),
/// member photos and tenant isolation of all of them.
/// </summary>
public sealed class CheckInTests
    : IntegrationTestBase,
      IClassFixture<PostgreSqlTestDatabase>
{
    private static readonly DateTimeOffset Start = new(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);

    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];

    private readonly TestClock _clock = new(Start);

    public CheckInTests(PostgreSqlTestDatabase database) : base(database)
    {
    }

    private DateOnly Today => DateOnly.FromDateTime(_clock.Now.UtcDateTime);

    /// <summary>The member's own phone (app installation id).</summary>
    private const string Phone = "phone-of-the-member";

    // ---- Member pass -------------------------------------------------------------

    [Fact]
    public async Task ScannedPass_ChecksTheMemberIn_AndTheSameCodeCannotBeUsedTwice()
    {
        var gym = await SeedGymAsync();
        var ali = await SeedMemberAsync(gym, "ali@example.com");

        var pass = await AsMember(gym, ali, s => s.MemberPasses.GetMyPassAsync(Phone));
        Assert.True(pass.Success, pass.ErrorMessage);

        var code = Code(pass.Data!);

        var first = await AsAdmin(gym, s => s.CheckIns.ScanPassAsync(code));
        Assert.True(first.Success, first.ErrorMessage);
        Assert.Equal(CheckInOutcome.Allowed, first.Data!.Outcome);
        Assert.Equal(CheckInMethod.PassScan, first.Data.CheckIn!.Method);
        Assert.Equal(ali.MemberId, first.Data.MemberId);

        // A screenshot of the same code, shown again.
        var copy = await AsAdmin(gym, s => s.CheckIns.ScanPassAsync(code));
        Assert.Equal(CheckInOutcome.Denied, copy.Data!.Outcome);
        Assert.Equal(CheckInDenialReason.ReusedPass, copy.Data.DenialReason);

        // The next live code is fine, but the visit is not counted twice.
        _clock.Now = _clock.Now.AddSeconds(30);
        var again = await AsAdmin(gym, s => s.CheckIns.ScanPassAsync(Code(pass.Data!)));
        Assert.Equal(CheckInOutcome.AlreadyCheckedIn, again.Data!.Outcome);

        await using var db = Database.CreateDbContext();
        Assert.Equal(1, await db.CheckIns.IgnoreQueryFilters().CountAsync(x => x.MemberProfileId == ali.MemberId));
        Assert.Equal(1, await db.CheckInDenials.IgnoreQueryFilters().CountAsync(x =>
            x.MemberProfileId == ali.MemberId && x.Reason == CheckInDenialReason.ReusedPass));
        Assert.True(await db.AuditEvents.IgnoreQueryFilters().AnyAsync(x => x.Action == "PassReuseRejected"));
    }

    [Fact]
    public async Task OldOrForgedOrResetPasses_AreRefused()
    {
        var gym = await SeedGymAsync();
        var ali = await SeedMemberAsync(gym, "ali@example.com");
        var pass = (await AsMember(gym, ali, s => s.MemberPasses.GetMyPassAsync(Phone))).Data!;

        // A code from five minutes ago (an old screenshot).
        var old = Code(pass, _clock.Now.AddMinutes(-5));
        var expired = await AsAdmin(gym, s => s.CheckIns.ScanPassAsync(old));
        Assert.Equal(CheckInDenialReason.ExpiredPass, expired.Data!.DenialReason);

        // A code signed with the wrong secret.
        var forged = MemberPassCode.Create(new byte[32], gym.TenantId, ali.MemberId, pass.KeyVersion,
            MemberPassCode.StepAt(_clock.Now));
        var invalid = await AsAdmin(gym, s => s.CheckIns.ScanPassAsync(forged));
        Assert.Equal(CheckInDenialReason.InvalidPass, invalid.Data!.DenialReason);

        var garbage = await AsAdmin(gym, s => s.CheckIns.ScanPassAsync("hello"));
        Assert.Equal(CheckInDenialReason.InvalidPass, garbage.Data!.DenialReason);
        Assert.Null(garbage.Data.MemberId);

        // Reset (lost phone): the old pass stops working, the new one works.
        var current = Code(pass);
        var reset = await AsMember(gym, ali, s => s.MemberPasses.ResetMyPassAsync(Phone));
        Assert.Equal(pass.KeyVersion + 1, reset.Data!.KeyVersion);

        var revoked = await AsAdmin(gym, s => s.CheckIns.ScanPassAsync(current));
        Assert.Equal(CheckInDenialReason.RevokedPass, revoked.Data!.DenialReason);

        var fresh = await AsAdmin(gym, s => s.CheckIns.ScanPassAsync(Code(reset.Data)));
        Assert.Equal(CheckInOutcome.Allowed, fresh.Data!.Outcome);
    }

    [Fact]
    public async Task PassOfAnotherGym_IsUnknown_WithoutRevealingTheMember()
    {
        var gymA = await SeedGymAsync();
        var gymB = await SeedGymAsync();
        var bea = await SeedMemberAsync(gymB, "bea@example.com");

        var pass = (await AsMember(gymB, bea, s => s.MemberPasses.GetMyPassAsync(Phone))).Data!;

        var result = await AsAdmin(gymA, s => s.CheckIns.ScanPassAsync(Code(pass)));

        Assert.Equal(CheckInDenialReason.UnknownMember, result.Data!.DenialReason);
        Assert.Null(result.Data.MemberId);
        Assert.Null(result.Data.MemberName);

        // The refusal is gym A's record and carries no member of gym B.
        var denialsA = await AsAdmin(gymA, s => s.CheckIns.ListDenialsAsync(10));
        var denialsB = await AsAdmin(gymB, s => s.CheckIns.ListDenialsAsync(10));
        Assert.Single(denialsA.Data!);
        Assert.Null(denialsA.Data![0].MemberId);
        Assert.Empty(denialsB.Data!);
    }

    [Fact]
    public async Task PendingMember_HasNoPassYet()
    {
        var gym = await SeedGymAsync();
        var pending = await SeedMemberAsync(gym, "new@example.com", MemberStatus.Pending, approved: false);

        var pass = await AsMember(gym, pending, s => s.MemberPasses.GetMyPassAsync(Phone));

        Assert.Equal(409, pass.StatusCode);
    }

    // ---- Who may enter ----------------------------------------------------------

    [Fact]
    public async Task OverdueInvoice_AllowsEntryWithAWarning_DuringTheGracePeriodOnly()
    {
        var gym = await SeedGymAsync();
        var ali = await SeedMemberAsync(gym, "ali@example.com");
        await SeedInvoiceAsync(gym, ali.MemberId, dueDate: Today.AddDays(-3), InvoiceStatus.Overdue);

        var inGrace = await AsAdmin(gym, s => s.CheckIns.CheckInAsync(ali.MemberId));

        Assert.Equal(CheckInOutcome.Allowed, inGrace.Data!.Outcome);
        Assert.Contains("overdue", inGrace.Data.Warning);
        Assert.Equal(CheckInMethod.FrontDesk, inGrace.Data.CheckIn!.Method);

        // 15 days after the due date: the 14-day grace period is over.
        _clock.Now = _clock.Now.AddDays(12);
        var afterGrace = await AsAdmin(gym, s => s.CheckIns.CheckInAsync(ali.MemberId));

        Assert.Equal(CheckInOutcome.Denied, afterGrace.Data!.Outcome);
        Assert.Equal(CheckInDenialReason.GracePeriodEnded, afterGrace.Data.DenialReason);
    }

    [Fact]
    public async Task FrozenExpiredAndPendingMembers_AreRefused_AndTheRefusalIsRecorded()
    {
        var gym = await SeedGymAsync();
        var frozen = await SeedMemberAsync(gym, "frozen@example.com", MemberStatus.Frozen);
        var expired = await SeedMemberAsync(gym, "expired@example.com", MemberStatus.Expired);

        var a = await AsAdmin(gym, s => s.CheckIns.CheckInAsync(frozen.MemberId));
        var b = await AsAdmin(gym, s => s.CheckIns.CheckInAsync(expired.MemberId));

        Assert.Equal(CheckInDenialReason.Frozen, a.Data!.DenialReason);
        Assert.Equal(CheckInDenialReason.Expired, b.Data!.DenialReason);

        var denials = await AsAdmin(gym, s => s.CheckIns.ListDenialsAsync(10));
        Assert.Equal(2, denials.Data!.Count);

        var attendance = await AsAdmin(gym, s => s.CheckIns.GetAttendanceAsync(7));
        Assert.Equal(0, attendance.Data!.TotalCheckIns);
    }

    [Fact]
    public async Task Member_CannotScanPasses()
    {
        var gym = await SeedGymAsync();
        var ali = await SeedMemberAsync(gym, "ali@example.com");
        var pass = (await AsMember(gym, ali, s => s.MemberPasses.GetMyPassAsync(Phone))).Data!;

        var scan = await AsMember(gym, ali, s => s.CheckIns.ScanPassAsync(Code(pass)));

        Assert.Equal(403, scan.StatusCode);
    }

    // ---- Check-in devices -------------------------------------------------------

    [Fact]
    public async Task PairedDevice_ChecksMembersIn_AndCanDoNothingElse()
    {
        var gym = await SeedGymAsync();
        var ali = await SeedMemberAsync(gym, "ali@example.com");
        var pass = (await AsMember(gym, ali, s => s.MemberPasses.GetMyPassAsync(Phone))).Data!;

        var created = await AsAdmin(gym, s => s.CheckInDevices.CreateAsync(new CreateCheckInDeviceRequest { Name = "Front entrance" }));
        Assert.True(created.Success, created.ErrorMessage);

        var paired = await AsDevice(s => s.Kiosk.PairAsync(created.Data!.PairingCode.ToLowerInvariant()));
        Assert.True(paired.Success, paired.ErrorMessage);
        Assert.StartsWith("ifk_", paired.Data!.DeviceKey);

        // The pairing code works once.
        var reused = await AsDevice(s => s.Kiosk.PairAsync(created.Data.PairingCode));
        Assert.Equal(400, reused.StatusCode);

        var key = paired.Data.DeviceKey;

        var scan = await AsDevice(async s =>
        {
            var device = await s.Kiosk.AuthenticateAsync(key);
            Assert.True(device.Success, device.ErrorMessage);

            return await s.CheckIns.ScanPassAsync(Code(pass));
        });

        Assert.Equal(CheckInOutcome.Allowed, scan.Data!.Outcome);
        Assert.Equal(CheckInMethod.Kiosk, scan.Data.CheckIn!.Method);
        Assert.True(scan.Data.CheckIn.SelfCheckIn);

        // The device key grants nothing beyond scanning.
        var members = await AsDevice(async s =>
        {
            await s.Kiosk.AuthenticateAsync(key);
            return await s.GymMembers.ListAsync(new MemberQuery());
        });
        var manual = await AsDevice(async s =>
        {
            await s.Kiosk.AuthenticateAsync(key);
            return await s.CheckIns.CheckInAsync(ali.MemberId);
        });

        Assert.Equal(403, members.StatusCode);
        Assert.Equal(403, manual.StatusCode);

        var devices = await AsAdmin(gym, s => s.CheckInDevices.ListAsync());
        Assert.True(devices.Data!.Single().IsPaired);
        Assert.Equal(1, devices.Data!.Single().CheckInsToday);

        await using var db = Database.CreateDbContext();
        var checkIn = await db.CheckIns.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(created.Data.Device.Id, checkIn.CheckInDeviceId);
        Assert.Null(checkIn.RecordedByUserId);
    }

    [Fact]
    public async Task RemovedDevice_IsRejected_AndExpiredPairingCodesDoNotWork()
    {
        var gym = await SeedGymAsync();

        var created = await AsAdmin(gym, s => s.CheckInDevices.CreateAsync(new CreateCheckInDeviceRequest { Name = "Side door" }));
        var key = (await AsDevice(s => s.Kiosk.PairAsync(created.Data!.PairingCode))).Data!.DeviceKey;

        var revoked = await AsAdmin(gym, s => s.CheckInDevices.RevokeAsync(created.Data!.Device.Id));
        Assert.True(revoked.Data!.IsRevoked);

        var rejected = await AsDevice(s => s.Kiosk.AuthenticateAsync(key));
        Assert.Equal(401, rejected.StatusCode);

        var other = await AsAdmin(gym, s => s.CheckInDevices.CreateAsync(new CreateCheckInDeviceRequest { Name = "Back door" }));
        _clock.Now = _clock.Now.AddMinutes(16);

        var late = await AsDevice(s => s.Kiosk.PairAsync(other.Data!.PairingCode));
        Assert.Equal(400, late.StatusCode);
    }

    [Fact]
    public async Task DeviceOfOneGym_CannotCheckInMembersOfAnother_NorSeeItsDevices()
    {
        var gymA = await SeedGymAsync();
        var gymB = await SeedGymAsync();
        var bea = await SeedMemberAsync(gymB, "bea@example.com");
        var pass = (await AsMember(gymB, bea, s => s.MemberPasses.GetMyPassAsync(Phone))).Data!;

        var created = await AsAdmin(gymA, s => s.CheckInDevices.CreateAsync(new CreateCheckInDeviceRequest { Name = "A entrance" }));
        var key = (await AsDevice(s => s.Kiosk.PairAsync(created.Data!.PairingCode))).Data!.DeviceKey;

        var scan = await AsDevice(async s =>
        {
            await s.Kiosk.AuthenticateAsync(key);
            return await s.CheckIns.ScanPassAsync(Code(pass));
        });

        Assert.Equal(CheckInDenialReason.UnknownMember, scan.Data!.DenialReason);

        var devicesB = await AsAdmin(gymB, s => s.CheckInDevices.ListAsync());
        Assert.Empty(devicesB.Data!);

        var revokeFromB = await AsAdmin(gymB, s => s.CheckInDevices.RevokeAsync(created.Data!.Device.Id));
        Assert.Equal(404, revokeFromB.StatusCode);
    }

    [Fact]
    public async Task DeviceOfASuspendedGym_IsRejected()
    {
        var gym = await SeedGymAsync();
        var created = await AsAdmin(gym, s => s.CheckInDevices.CreateAsync(new CreateCheckInDeviceRequest { Name = "Front" }));
        var key = (await AsDevice(s => s.Kiosk.PairAsync(created.Data!.PairingCode))).Data!.DeviceKey;

        await using (var db = Database.CreateDbContext())
        {
            var tenant = await db.Tenants.SingleAsync(x => x.Id == gym.TenantId);
            tenant.IsActive = false;
            await db.SaveChangesAsync();
        }

        var result = await AsDevice(s => s.Kiosk.AuthenticateAsync(key));

        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Trainer_CannotManageDevices()
    {
        var gym = await SeedGymAsync();
        var trainer = await SeedUserAsync(gym, "trainer@gym.test", TenantRoles.Trainer);

        var result = await AsUser(gym, trainer, TenantRoles.Trainer,
            s => s.CheckInDevices.CreateAsync(new CreateCheckInDeviceRequest { Name = "Front" }));

        Assert.Equal(403, result.StatusCode);
    }

    // ---- Photos -----------------------------------------------------------------

    [Fact]
    public async Task MemberPhoto_ShowsAtCheckIn_AndAStaffPhotoCannotBeReplacedByTheMember()
    {
        var gym = await SeedGymAsync();
        var ali = await SeedMemberAsync(gym, "ali@example.com");
        var upload = new UploadMemberPhotoRequest { Data = Convert.ToBase64String(JpegBytes) };

        var own = await AsMember(gym, ali, s => s.MemberPhotos.UploadMineAsync(upload));
        Assert.True(own.Success, own.ErrorMessage);
        Assert.False(own.Data!.UploadedByStaff);
        Assert.StartsWith("data:image/jpeg;base64,", own.Data.DataUrl);

        var staff = await AsAdmin(gym, s => s.MemberPhotos.UploadAsync(ali.MemberId, upload));
        Assert.True(staff.Data!.UploadedByStaff);

        var replace = await AsMember(gym, ali, s => s.MemberPhotos.UploadMineAsync(upload));
        Assert.Equal(409, replace.StatusCode);

        var checkIn = await AsAdmin(gym, s => s.CheckIns.CheckInAsync(ali.MemberId));
        Assert.StartsWith("data:image/jpeg", checkIn.Data!.PhotoDataUrl);

        var detail = await AsAdmin(gym, s => s.GymMembers.GetAsync(ali.MemberId));
        Assert.True(detail.Data!.Summary.HasPhoto);
        Assert.NotNull(detail.Data.Photo);
    }

    [Fact]
    public async Task Photo_MustBeAnImage_AndStaysInsideItsGym()
    {
        var gymA = await SeedGymAsync();
        var gymB = await SeedGymAsync();
        var ali = await SeedMemberAsync(gymA, "ali@example.com");

        var notImage = await AsAdmin(gymA, s => s.MemberPhotos.UploadAsync(ali.MemberId,
            new UploadMemberPhotoRequest { Data = Convert.ToBase64String("<svg onload=x>"u8.ToArray()) }));
        Assert.Equal(400, notImage.StatusCode);

        await AsAdmin(gymA, s => s.MemberPhotos.UploadAsync(ali.MemberId,
            new UploadMemberPhotoRequest { Data = Convert.ToBase64String(JpegBytes) }));

        var fromB = await AsAdmin(gymB, s => s.MemberPhotos.GetAsync(ali.MemberId));
        Assert.Equal(404, fromB.StatusCode);
    }

    [Fact]
    public async Task Anonymize_DeletesThePhoto_AndInvalidatesThePass()
    {
        var gym = await SeedGymAsync();
        var ali = await SeedMemberAsync(gym, "ali@example.com");
        var pass = (await AsMember(gym, ali, s => s.MemberPasses.GetMyPassAsync(Phone))).Data!;

        await AsAdmin(gym, s => s.MemberPhotos.UploadAsync(ali.MemberId,
            new UploadMemberPhotoRequest { Data = Convert.ToBase64String(JpegBytes) }));

        var anonymized = await AsAdmin(gym, s => s.GymMembers.AnonymizeAsync(ali.MemberId));
        Assert.True(anonymized.Success, anonymized.ErrorMessage);

        var scan = await AsAdmin(gym, s => s.CheckIns.ScanPassAsync(Code(pass)));
        Assert.Equal(CheckInDenialReason.UnknownMember, scan.Data!.DenialReason);

        await using var db = Database.CreateDbContext();
        Assert.False(await db.MemberPhotos.IgnoreQueryFilters().AnyAsync());
    }

    // ---- The member-pass switch -------------------------------------------------

    [Fact]
    public async Task TurningOffTheMemberPass_StopsPassesEverywhere_ButNotTheFrontDesk()
    {
        var gym = await SeedGymAsync();
        var ali = await SeedMemberAsync(gym, "ali@example.com");
        var pass = (await AsMember(gym, ali, s => s.MemberPasses.GetMyPassAsync(Phone))).Data!;
        var (_, key) = await PairDeviceAsync(gym, "Entrance");

        await SetMethodsAsync(gym, memberPass: false);

        var getPass = await AsMember(gym, ali, s => s.MemberPasses.GetMyPassAsync(Phone));
        var staffScan = await AsAdmin(gym, s => s.CheckIns.ScanPassAsync(Code(pass)));
        var kioskScan = await AsDevice(async s =>
        {
            await s.Kiosk.AuthenticateAsync(key);
            return await s.CheckIns.ScanPassAsync(Code(pass));
        });

        Assert.Equal(409, getPass.StatusCode);
        Assert.Equal(409, staffScan.StatusCode);
        Assert.Equal(409, kioskScan.StatusCode);

        var frontDesk = await AsAdmin(gym, s => s.CheckIns.CheckInAsync(ali.MemberId));
        Assert.Equal(CheckInOutcome.Allowed, frontDesk.Data!.Outcome);

        var info = await AsDevice(s => s.Kiosk.AuthenticateAsync(key));
        Assert.False(info.Data!.MemberPassEnabled);
    }

    [Fact]
    public async Task OnlyAdmins_ChooseTheCheckInMethods()
    {
        var gym = await SeedGymAsync();
        var trainer = await SeedUserAsync(gym, "trainer@gym.test", TenantRoles.Trainer);

        var byTrainer = await AsUser(gym, trainer, TenantRoles.Trainer, s => s.GymSettings.UpdateCheckInMethodsAsync(
            new UpdateCheckInMethodsRequest { MemberPassEnabled = false, RequirePhoneUnlock = false }));
        Assert.Equal(403, byTrainer.StatusCode);

        var byAdmin = await SetMethodsAsync(gym, memberPass: false, phoneUnlock: false);
        Assert.False(byAdmin.MemberPassCheckInEnabled);
        Assert.False(byAdmin.RequirePhoneUnlock);

        await using var db = Database.CreateDbContext();
        Assert.True(await db.AuditEvents.IgnoreQueryFilters().AnyAsync(x => x.Action == "CheckInMethodsChanged"));
    }

    // ---- Helpers ------------------------------------------------------------------

    private async Task<GymSettingsDto> SetMethodsAsync(SeededGym gym, bool memberPass, bool phoneUnlock = true)
    {
        var result = await AsAdmin(gym, s => s.GymSettings.UpdateCheckInMethodsAsync(
            new UpdateCheckInMethodsRequest { MemberPassEnabled = memberPass, RequirePhoneUnlock = phoneUnlock }));

        Assert.True(result.Success, result.ErrorMessage);
        return result.Data!;
    }

    private async Task<(int DeviceId, string Key)> PairDeviceAsync(SeededGym gym, string name)
    {
        var created = await AsAdmin(gym, s => s.CheckInDevices.CreateAsync(new CreateCheckInDeviceRequest { Name = name }));
        var paired = await AsDevice(s => s.Kiosk.PairAsync(created.Data!.PairingCode));

        Assert.True(paired.Success, paired.ErrorMessage);
        return (created.Data!.Device.Id, paired.Data!.DeviceKey);
    }

    private string Code(MemberPassDto pass, DateTimeOffset? at = null) =>
        MemberPassCode.Create(
            Convert.FromBase64String(pass.Secret),
            pass.TenantId,
            pass.MemberId,
            pass.KeyVersion,
            MemberPassCode.StepAt(at ?? _clock.Now));

    private sealed record SeededGym(int TenantId, int PlanId, int AdminUserId);

    private sealed record SeededMember(int MemberId, int UserId);

    private async Task<SeededGym> SeedGymAsync()
    {
        await using var db = Database.CreateDbContext();

        var tenant = new Tenant
        {
            Name = "Gym",
            Slug = $"gym-{Guid.NewGuid():N}",
            IsActive = true,
            CreatedAtUtc = Start.UtcDateTime,
            Plan = SubscriptionPlans.Professional.Id
        };

        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var plan = new MembershipPlan
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
        db.MembershipPlans.Add(plan);
        await db.SaveChangesAsync();

        var adminUserId = await SeedUserAsync(tenant.Id, $"admin{tenant.Id}", $"admin{tenant.Id}@gym.test", TenantRoles.Admin, db);

        return new SeededGym(tenant.Id, plan.Id, adminUserId);
    }

    private async Task<int> SeedUserAsync(SeededGym gym, string email, string role)
    {
        await using var db = Database.CreateDbContext();

        return await SeedUserAsync(gym.TenantId, email.Split('@')[0] + gym.TenantId, email, role, db);
    }

    private static async Task<int> SeedUserAsync(
        int tenantId,
        string username,
        string email,
        string role,
        IronForge.Infrastructure.Data.IronForgeDbContext db)
    {
        var user = new User
        {
            Username = username,
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

    private async Task<SeededMember> SeedMemberAsync(
        SeededGym gym,
        string email,
        MemberStatus status = MemberStatus.Active,
        bool approved = true)
    {
        await using var db = Database.CreateDbContext();

        var userId = await SeedUserAsync(gym.TenantId, email.Split('@')[0] + gym.TenantId, email, TenantRoles.Member, db);

        var member = new MemberProfile
        {
            TenantId = gym.TenantId,
            UserId = userId,
            FullName = email.Split('@')[0],
            Email = email,
            MembershipPlanId = gym.PlanId,
            Status = status,
            JoinedAtUtc = Start.UtcDateTime,
            ApprovedAtUtc = approved ? Start.UtcDateTime : null,
            FrozenUntil = status == MemberStatus.Frozen ? DateOnly.FromDateTime(Start.UtcDateTime).AddDays(10) : null
        };

        db.MemberProfiles.Add(member);
        await db.SaveChangesAsync();

        return new SeededMember(member.Id, userId);
    }

    private async Task SeedInvoiceAsync(SeededGym gym, int memberId, DateOnly dueDate, InvoiceStatus status)
    {
        await using var db = Database.CreateDbContext();

        db.Invoices.Add(new Invoice
        {
            TenantId = gym.TenantId,
            MemberProfileId = memberId,
            MembershipPlanId = gym.PlanId,
            Number = $"INV-{Random.Shared.Next(100000, 999999)}",
            Description = "Monthly",
            PeriodStart = dueDate,
            PeriodEnd = dueDate.AddMonths(1).AddDays(-1),
            Amount = 50m,
            Currency = "USD",
            DueDate = dueDate,
            Status = status,
            IssuedAtUtc = Start.UtcDateTime
        });

        await db.SaveChangesAsync();
    }

    private Task<T> AsAdmin<T>(SeededGym gym, Func<TestServiceFactory, Task<T>> action) =>
        AsUser(gym, gym.AdminUserId, TenantRoles.Admin, action);

    private Task<T> AsMember<T>(SeededGym gym, SeededMember member, Func<TestServiceFactory, Task<T>> action) =>
        AsUser(gym, member.UserId, TenantRoles.Member, action);

    private async Task<T> AsUser<T>(SeededGym gym, int userId, string role, Func<TestServiceFactory, Task<T>> action)
    {
        var context = TestServiceFactory.AsRole(gym.TenantId, userId, role);

        await using var db = Database.CreateDbContext(context);

        return await action(new TestServiceFactory(db, context) { Clock = _clock });
    }

    /// <summary>An anonymous request: the device establishes its own context with its key.</summary>
    private async Task<T> AsDevice<T>(Func<TestServiceFactory, Task<T>> action)
    {
        var accessor = new TestExecutionContextAccessor();

        await using var db = Database.CreateDbContext(accessor);

        return await action(new TestServiceFactory(db, accessor) { Clock = _clock });
    }
}

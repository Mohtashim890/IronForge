using IronForge.Application.Agents.Memory.ConversationSession;
using IronForge.Application.Agents.Runtime;
using IronForge.Application.Auditing.Models;
using IronForge.Application.Entities;
using IronForge.Application.Gym.CheckIns;
using IronForge.Application.Gym.Common;
using IronForge.Shared.Authorization;
using AgentIds = IronForge.Shared.Models.Agents.AgentIds;
using IronForge.Shared.Models.Gym;
using IronForge.Shared.Subscriptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IronForge.Infrastructure.Data.Seeding;

/// <summary>
/// Realistic demo data for the whole platform: an operator, four gyms in
/// different subscription states, staff, members with months of billing,
/// attendance and training history, notifications, agents and their usage.
///
/// Deterministic (fixed random seed, dates relative to "now") and idempotent:
/// it does nothing when the demo operator already exists. Every account uses
/// <see cref="DemoPassword"/>; never run it against a production database.
/// </summary>
public sealed class DemoDataSeeder
{
    public const string DemoPassword = "Password123!";
    public const string OperatorEmail = "operator@ironforge.dev";

    private readonly IronForgeDbContext _db;
    private readonly ILogger _logger;
    private readonly Random _random = new(42);

    /// <summary>Check-in methods and refusals; separate so the rest of the demo data is unchanged.</summary>
    private readonly Random _checkInRandom = new(7);
    private readonly DateTime _now;
    private readonly string _passwordHash;
    private readonly Dictionary<string, Exercise> _exercises = new(StringComparer.Ordinal);

    public DemoDataSeeder(IronForgeDbContext db, IPasswordHasher<User> passwordHasher, ILogger logger, DateTime? nowUtc = null)
    {
        _db = db;
        _logger = logger;
        _now = DateTime.SpecifyKind(nowUtc ?? DateTime.UtcNow, DateTimeKind.Utc);

        // One hash for every demo account keeps seeding fast; they share a password anyway.
        _passwordHash = passwordHasher.HashPassword(new User(), DemoPassword);
    }

    /// <summary>Removes all data (keeps the schema and migration history).</summary>
    public static async Task ResetAsync(IronForgeDbContext db, CancellationToken cancellationToken = default)
    {
        var tables = await db.Database
            .SqlQueryRaw<string>(
                """
                SELECT tablename AS "Value"
                FROM pg_catalog.pg_tables
                WHERE schemaname = 'public' AND tablename <> '__EFMigrationsHistory'
                """)
            .ToListAsync(cancellationToken);

        if (tables.Count == 0)
        {
            return;
        }

        var list = string.Join(", ", tables.Select(x => $"\"{x.Replace("\"", "\"\"")}\""));

#pragma warning disable EF1002 // Table names come from the catalogue, not from input.
        await db.Database.ExecuteSqlRawAsync($"TRUNCATE TABLE {list} RESTART IDENTITY CASCADE;", cancellationToken);
#pragma warning restore EF1002
    }

    public async Task<bool> SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await _db.Users.AnyAsync(x => x.Email == OperatorEmail, cancellationToken))
        {
            _logger.LogInformation("Demo data already present; nothing to do.");
            return false;
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        SeedExerciseLibrary();
        AddUser("operator", OperatorEmail, PlatformRoles.PlatformAdmin);
        await _db.SaveChangesAsync(cancellationToken);

        foreach (var spec in GymSpecs())
        {
            await SeedGymAsync(spec, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Demo data seeded: {Gyms} gyms, {Members} members, {Invoices} invoices, {CheckIns} check-ins.",
            await _db.Tenants.CountAsync(cancellationToken),
            await _db.MemberProfiles.IgnoreQueryFilters().CountAsync(cancellationToken),
            await _db.Invoices.IgnoreQueryFilters().CountAsync(cancellationToken),
            await _db.CheckIns.IgnoreQueryFilters().CountAsync(cancellationToken));

        return true;
    }

    // ---- The gyms ---------------------------------------------------------------

    private sealed record PlanSpec(string Name, string Description, decimal Price, BillingInterval Interval, int Weight);

    private sealed record GymSpec(
        string Name,
        string Slug,
        string Plan,
        SubscriptionStatus Status,
        int? TrialDaysLeft,
        string Currency,
        string TimeZoneId,
        string Address,
        string JoinCode,
        int Members,
        PlanSpec[] Plans);

    private static GymSpec[] GymSpecs() =>
    [
        new("Iron Paradise Gym", "ironparadise", SubscriptionPlans.Professional.Id, SubscriptionStatus.Active, null,
            "PKR", "Asia/Karachi", "12 Main Boulevard, Gulberg III, Lahore", "PARADISE", 35,
            [
                new("Monthly", "Full gym access, billed monthly.", 6000m, BillingInterval.Monthly, 60),
                new("Quarterly", "Three months for the price of 2.75.", 16500m, BillingInterval.Quarterly, 25),
                new("Annual", "Best value: a full year of training.", 60000m, BillingInterval.Yearly, 15)
            ]),
        new("FlexZone Fitness", "flexzone", SubscriptionPlans.Starter.Id, SubscriptionStatus.Trial, 5,
            "USD", "America/New_York", "220 Atlantic Ave, Brooklyn, NY", "FLEXZONE", 15,
            [
                new("Basic", "Gym floor access, off-peak hours.", 29m, BillingInterval.Monthly, 50),
                new("Unlimited", "Any time, all classes.", 49m, BillingInterval.Monthly, 40),
                new("Annual Unlimited", "Unlimited for a year.", 499m, BillingInterval.Yearly, 10)
            ]),
        new("Titan Strength Club", "titanstrength", SubscriptionPlans.Enterprise.Id, SubscriptionStatus.PastDue, null,
            "GBP", "Europe/London", "4 Foundry Lane, Manchester", "TITAN777", 25,
            [
                new("Strength Monthly", "Powerlifting floor and platforms.", 45m, BillingInterval.Monthly, 55),
                new("Strength Quarterly", "Three months on the platforms.", 125m, BillingInterval.Quarterly, 30),
                new("Strength Annual", "A year of heavy lifting.", 450m, BillingInterval.Yearly, 15)
            ]),
        new("Old Barn Gym", "oldbarn", SubscriptionPlans.Starter.Id, SubscriptionStatus.Canceled, null,
            "USD", "America/Chicago", "88 County Road 9, Springfield, IL", "OLDBARN1", 5,
            [
                new("Monthly", "Classic gym membership.", 25m, BillingInterval.Monthly, 100)
            ])
    ];

    private async Task SeedGymAsync(GymSpec spec, CancellationToken cancellationToken)
    {
        var tenant = new Tenant
        {
            Name = spec.Name,
            Slug = spec.Slug,
            IsActive = true,
            CreatedAtUtc = _now.AddDays(-400),
            Plan = spec.Plan,
            SubscriptionStatus = spec.Status,
            TrialEndsAtUtc = spec.TrialDaysLeft is { } days ? _now.AddDays(days) : null,
            // The showcase gym has fingerprint terminals as an add-on (Enterprise includes them).
            AddOnModules = spec.Slug == "ironparadise" ? [PlatformModules.GymAccess] : []
        };

        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync(cancellationToken);

        var settings = new GymSettings
        {
            TenantId = tenant.Id,
            Address = spec.Address,
            Phone = "+1 555 0100",
            ContactEmail = $"hello@{spec.Slug}.demo",
            Currency = spec.Currency,
            TimeZoneId = spec.TimeZoneId,
            JoinCode = spec.JoinCode,
            // The showcase gym offers both self check-in methods; the others only the pass.
            GymQrCheckInEnabled = spec.Slug == "ironparadise",
            CreatedAtUtc = tenant.CreatedAtUtc,
            UpdatedAtUtc = tenant.CreatedAtUtc
        };

        _db.GymSettings.Add(settings);

        var plans = spec.Plans
            .Select(x => new MembershipPlan
            {
                TenantId = tenant.Id,
                Name = x.Name,
                Description = x.Description,
                Price = x.Price,
                BillingInterval = x.Interval,
                IsActive = true,
                CreatedAtUtc = tenant.CreatedAtUtc
            })
            .ToList();

        _db.MembershipPlans.AddRange(plans);

        // Staff.
        var admin = AddUser($"admin.{spec.Slug}", $"admin@{spec.Slug}.demo", PlatformRoles.User);
        var trainer1 = AddUser($"coach.{spec.Slug}", $"trainer1@{spec.Slug}.demo", PlatformRoles.User);
        var trainer2 = AddUser($"frontdesk.{spec.Slug}", $"trainer2@{spec.Slug}.demo", PlatformRoles.User);
        await _db.SaveChangesAsync(cancellationToken);

        AddMembership(tenant.Id, admin.Id, TenantRoles.Admin);
        AddMembership(tenant.Id, trainer1.Id, TenantRoles.Trainer);
        AddMembership(tenant.Id, trainer2.Id, TenantRoles.Trainer);

        // Agents the plan covers, like provisioning installs them.
        var enabled = tenant.GetEnabledModules();
        var profiles = tenant.GetAllowedModelProfiles();
        var agents = new List<string>();

        foreach (var template in AgentTemplates.Default.Where(t => t.Modules.All(enabled.Contains)))
        {
            var agent = template.CreateAgent(tenant.Id, tenant.CreatedAtUtc);
            agent.ModelProfile = template.ChooseModelProfile(profiles);
            _db.Agents.Add(agent);
            agents.Add(agent.AgentId);
        }

        await _db.SaveChangesAsync(cancellationToken);

        // An entrance kiosk paired two months ago. Its key was never kept, so
        // to use the kiosk an admin issues a new pairing code (Check-in devices).
        var kiosk = new CheckInDevice
        {
            TenantId = tenant.Id,
            Name = "Front entrance kiosk",
            KeyHash = CheckInDeviceSecrets.HashKey(CheckInDeviceSecrets.NewDeviceKey()),
            CreatedByUserId = admin.Id,
            CreatedAtUtc = _now.AddDays(-60),
            PairedAtUtc = _now.AddDays(-60),
            LastSeenAtUtc = _now.AddMinutes(-5)
        };

        _db.CheckInDevices.Add(kiosk);

        // A fingerprint terminal registered but not connected yet: open the
        // terminal simulator, power it on and load the "existing" fingerprints.
        if (tenant.GetEnabledModules().Contains(PlatformModules.GymAccess) && spec.Slug == "ironparadise")
        {
            _db.CheckInDevices.Add(new CheckInDevice
            {
                TenantId = tenant.Id,
                Name = "Main door (simulated ZKTeco)",
                Kind = CheckInDeviceKind.ZkTecoTerminal,
                SerialNumber = "SIMPARADISE01",
                CreatedByUserId = admin.Id,
                CreatedAtUtc = _now.AddDays(-1)
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        var coaching = enabled.Contains(PlatformModules.GymCoaching);
        var context = new GymContext(tenant, settings, plans, coaching, trainer1.Id, kiosk.Id);
        var invoiceCounter = 0;
        var members = new List<(MemberProfile Profile, double Activity)>();

        for (var i = 0; i < spec.Members; i++)
        {
            var isDemoMember = spec.Slug == "ironparadise" && i == 0;
            var member = SeedMember(context, i, spec.Members, isDemoMember, ref invoiceCounter);
            members.Add(member);
        }

        await _db.SaveChangesAsync(cancellationToken);

        settings.LastInvoiceNumber = invoiceCounter;

        foreach (var (profile, activity) in members)
        {
            SeedCheckIns(context, profile, activity);
            SeedCheckInDenials(context, profile);
        }

        await _db.SaveChangesAsync(cancellationToken);

        if (coaching)
        {
            foreach (var (profile, activity) in members.Where(x => x.Profile.UserId != null))
            {
                await SeedTrainingAsync(context, profile, activity, isDemo: profile.Email.StartsWith("member@"), cancellationToken);
            }
        }

        SeedAnnouncements(context, admin, members.Select(x => x.Profile).ToList());
        SeedAgentActivity(context, admin, members.Select(x => x.Profile).ToList(), agents);
        SeedAudit(context, admin, members.Select(x => x.Profile).ToList());

        await _db.SaveChangesAsync(cancellationToken);
    }

    private sealed record GymContext(
        Tenant Tenant,
        GymSettings Settings,
        List<MembershipPlan> Plans,
        bool Coaching,
        int TrainerUserId,
        int KioskDeviceId)
    {
        public int TenantId => Tenant.Id;
    }

    // ---- Members, billing and attendance -------------------------------------------

    private (MemberProfile Profile, double Activity) SeedMember(
        GymContext gym,
        int index,
        int count,
        bool isDemo,
        ref int invoiceCounter)
    {
        var today = GymCalendar.ToLocalDate(_now, gym.Settings.TimeZoneId);
        var name = isDemo ? "Ayesha Malik" : Name(gym.TenantId, index);
        var email = isDemo
            ? "member@ironparadise.demo"
            : $"{name.ToLowerInvariant().Replace(' ', '.')}@{gym.Tenant.Slug}.demo";

        // Status mix: mostly active, a few of every other state.
        var status = isDemo
            ? MemberStatus.Active
            : (index * 100 / Math.Max(1, count)) switch
            {
                < 66 => MemberStatus.Active,
                < 74 => MemberStatus.Pending,
                < 80 => MemberStatus.Frozen,
                < 92 => MemberStatus.Expired,
                _ => MemberStatus.Cancelled
            };

        // The canceled gym's members all left with it.
        if (gym.Tenant.SubscriptionStatus == SubscriptionStatus.Canceled && !isDemo)
        {
            status = index % 2 == 0 ? MemberStatus.Cancelled : MemberStatus.Expired;
        }

        var plan = isDemo ? gym.Plans[0] : PickPlan(gym.Plans);
        var hasApp = isDemo || status == MemberStatus.Pending || _random.NextDouble() < 0.8;

        int? userId = null;

        if (hasApp)
        {
            var user = AddUser(email.Split('@')[0] + "." + gym.Tenant.Slug[..3], email, PlatformRoles.User);
            _db.SaveChanges();
            userId = user.Id;
            // Join requests (Pending) get gym access only once staff approve them.
            AddMembership(gym.TenantId, user.Id, TenantRoles.Member, isActive: status != MemberStatus.Pending);
        }

        var joinedDaysAgo = status == MemberStatus.Pending
            ? _random.Next(0, 4)
            : isDemo ? 200 : _random.Next(25, 360);

        var joined = today.AddDays(-joinedDaysAgo);

        var profile = new MemberProfile
        {
            TenantId = gym.TenantId,
            UserId = userId,
            FullName = name,
            Email = email,
            Phone = $"+1 555 {_random.Next(1000, 9999)}",
            DateOfBirth = today.AddYears(-_random.Next(18, 55)).AddDays(-_random.Next(0, 365)),
            Gender = _random.NextDouble() < 0.5 ? "Female" : "Male",
            HeightCm = _random.Next(155, 195),
            EmergencyContact = $"{Name(gym.TenantId + 7, index)} +1 555 {_random.Next(1000, 9999)}",
            MembershipPlanId = plan.Id,
            Status = status,
            JoinedAtUtc = StartOfDayUtc(joined, gym).AddHours(10),
            ApprovedAtUtc = status == MemberStatus.Pending ? null : StartOfDayUtc(joined, gym).AddHours(11)
        };

        if (isDemo)
        {
            profile.Gender = "Female";
            profile.HeightCm = 165;
            profile.DateOfBirth = new DateOnly(1995, 4, 12);
        }

        _db.MemberProfiles.Add(profile);
        _db.SaveChanges();

        if (status != MemberStatus.Pending)
        {
            SeedInvoices(gym, profile, plan, joined, today, isDemo, ref invoiceCounter);
        }

        var activity = isDemo ? 0.85 : status switch
        {
            MemberStatus.Active => 0.15 + _random.NextDouble() * 0.45,
            MemberStatus.Frozen => 0.2,
            MemberStatus.Expired => 0.1,
            MemberStatus.Cancelled => 0.1,
            _ => 0
        };

        return (profile, activity);
    }

    private void SeedInvoices(
        GymContext gym,
        MemberProfile profile,
        MembershipPlan plan,
        DateOnly joined,
        DateOnly today,
        bool isDemo,
        ref int invoiceCounter)
    {
        // Billing stops: expired members stopped paying 16-40 days ago,
        // cancelled members left 10-90 days ago.
        var stop = profile.Status switch
        {
            MemberStatus.Expired => today.AddDays(-_random.Next(16, 40)),
            MemberStatus.Cancelled => today.AddDays(-_random.Next(10, 90)),
            _ => today
        };

        // Some active members have a recent unpaid invoice (overdue within the grace period).
        var lateButActive = !isDemo && profile.Status == MemberStatus.Active && _random.NextDouble() < 0.2;

        var periodStart = joined;
        var periods = new List<(DateOnly Start, DateOnly End)>();

        while (periodStart <= stop)
        {
            var end = GymCalendar.PeriodEnd(periodStart, plan.BillingInterval);
            periods.Add((periodStart, end));
            periodStart = end.AddDays(1);
        }

        // The demo member's next invoice is already issued and due in 3 days.
        if (isDemo)
        {
            var end = GymCalendar.PeriodEnd(periodStart, plan.BillingInterval);

            if (periodStart.DayNumber - today.DayNumber > 3)
            {
                periodStart = today.AddDays(3);
                end = GymCalendar.PeriodEnd(periodStart, plan.BillingInterval);
                var last = periods[^1];
                periods[^1] = (last.Start, periodStart.AddDays(-1));
            }

            periods.Add((periodStart, end));
            periodStart = end.AddDays(1);
        }

        DateOnly? paidThrough = null;

        for (var p = 0; p < periods.Count; p++)
        {
            var (start, end) = periods[p];
            var isLast = p == periods.Count - 1;
            var due = start;

            var unpaid =
                (isLast && profile.Status == MemberStatus.Expired) ||
                (isLast && lateButActive) ||
                due > today;

            if (isLast && lateButActive && due <= today)
            {
                // Overdue for 1-10 days, still within grace.
                due = today.AddDays(-_random.Next(1, 11));
            }

            invoiceCounter++;

            var invoice = new Invoice
            {
                TenantId = gym.TenantId,
                Number = GymCalendar.FormatInvoiceNumber(invoiceCounter),
                MemberProfileId = profile.Id,
                MembershipPlanId = plan.Id,
                Description = $"{plan.Name} membership, {start:d MMM yyyy} – {end:d MMM yyyy}",
                PeriodStart = start,
                PeriodEnd = end,
                Amount = plan.Price,
                Currency = gym.Settings.Currency,
                IssuedAtUtc = StartOfDayUtc(start.AddDays(-gym.Settings.InvoiceLeadDays), gym).AddHours(3),
                DueDate = due,
                Status = unpaid
                    ? (due < today ? InvoiceStatus.Overdue : InvoiceStatus.Open)
                    : InvoiceStatus.Paid
            };

            if (!unpaid)
            {
                var paidOn = due.AddDays(_random.Next(-2, 4));
                if (paidOn > today) paidOn = today;

                invoice.PaidAtUtc = StartOfDayUtc(paidOn, gym).AddHours(_random.Next(8, 21));
                paidThrough = end;

                var card = _random.NextDouble() < 0.6;

                _db.Payments.Add(new Payment
                {
                    TenantId = gym.TenantId,
                    Invoice = invoice,
                    MemberProfileId = profile.Id,
                    Amount = invoice.Amount,
                    Currency = invoice.Currency,
                    Method = card ? PaymentMethod.Card : PaymentMethod.Cash,
                    Provider = card ? "simulated" : "manual",
                    ProviderReference = card ? $"sim_{Guid.NewGuid():N}" : null,
                    PaidAtUtc = invoice.PaidAtUtc.Value
                });
            }
            else if (invoice.Status == InvoiceStatus.Overdue && profile.UserId is { } userId)
            {
                _db.InvoiceReminders.Add(new InvoiceReminder
                {
                    TenantId = gym.TenantId,
                    Invoice = invoice,
                    Kind = "after:1",
                    SentAtUtc = StartOfDayUtc(due.AddDays(1), gym).AddHours(9)
                });

                AddNotification(gym, userId, NotificationType.PaymentReminder,
                    $"Payment overdue: {invoice.Number}",
                    $"Your invoice {invoice.Number} for {invoice.Amount:N2} {invoice.Currency} was due on {due:d MMM}. Pay it in the app or at the front desk.",
                    "/invoices", StartOfDayUtc(due.AddDays(1), gym).AddHours(9));
            }
            else if (invoice.Status == InvoiceStatus.Open && profile.UserId is { } openUserId)
            {
                AddNotification(gym, openUserId, NotificationType.Payment,
                    $"New invoice {invoice.Number}",
                    $"Your invoice for {invoice.Description} is ready. Amount: {invoice.Amount:N2} {invoice.Currency}, due {due:d MMM}.",
                    "/invoices", invoice.IssuedAtUtc);
            }

            _db.Invoices.Add(invoice);
        }

        profile.PaidThrough = paidThrough;
        profile.NextBillingDate = profile.Status is MemberStatus.Cancelled ? null : periodStart;

        if (profile.Status == MemberStatus.Cancelled)
        {
            profile.CancelledAtUtc = StartOfDayUtc(stop, gym).AddHours(12);
        }

        if (profile.Status == MemberStatus.Frozen)
        {
            var freezeDays = _random.Next(7, 30);
            profile.FrozenUntil = today.AddDays(freezeDays);
            profile.NextBillingDate = periodStart.AddDays(freezeDays);
            profile.PaidThrough = paidThrough?.AddDays(freezeDays);
        }
    }

    private void SeedCheckIns(GymContext gym, MemberProfile profile, double activity)
    {
        if (activity <= 0)
        {
            return;
        }

        var today = GymCalendar.ToLocalDate(_now, gym.Settings.TimeZoneId);
        var joined = GymCalendar.ToLocalDate(profile.JoinedAtUtc, gym.Settings.TimeZoneId);

        var lastDay = profile.Status switch
        {
            MemberStatus.Expired or MemberStatus.Cancelled => today.AddDays(-25),
            MemberStatus.Frozen => today.AddDays(-10),
            _ => today
        };

        // Morning people and evening people.
        var preferredHour = _random.NextDouble() < 0.4 ? 7 : 18;

        for (var day = today.AddDays(-90); day <= lastDay; day = day.AddDays(1))
        {
            if (day < joined || _random.NextDouble() > activity * 0.6)
                continue;

            // Today only has check-ins that already happened.
            var hour = Math.Clamp(preferredHour + _random.Next(-2, 3), 6, 21);
            var at = StartOfDayUtc(day, gym).AddHours(hour).AddMinutes(_random.Next(0, 60));

            if (at > _now)
                continue;

            // Staff at the desk (by name or by scanning the pass) or the kiosk.
            var byStaff = _random.NextDouble() < 0.7;

            _db.CheckIns.Add(new CheckIn
            {
                TenantId = gym.TenantId,
                MemberProfileId = profile.Id,
                CheckedInAtUtc = at,
                RecordedByUserId = byStaff ? gym.TrainerUserId : null,
                // Self check-ins: the kiosk scanned the pass, or the member scanned the entrance code.
                Method = !byStaff
                    ? gym.Settings.GymQrCheckInEnabled && _checkInRandom.NextDouble() < 0.4 ? CheckInMethod.GymQr : CheckInMethod.Kiosk
                    : _checkInRandom.NextDouble() < 0.5 ? CheckInMethod.PassScan : CheckInMethod.FrontDesk,
                CheckInDeviceId = byStaff ? null : gym.KioskDeviceId
            });
        }
    }

    /// <summary>
    /// Refused entries for the "Refused attempts" list: members who are
    /// frozen or expired trying the kiosk, and the odd screenshot of a pass.
    /// </summary>
    private void SeedCheckInDenials(GymContext gym, MemberProfile profile)
    {
        var reason = profile.Status switch
        {
            MemberStatus.Frozen => CheckInDenialReason.Frozen,
            MemberStatus.Expired => CheckInDenialReason.Expired,
            MemberStatus.Active when _checkInRandom.NextDouble() < 0.1 => CheckInDenialReason.ReusedPass,
            _ => (CheckInDenialReason?)null
        };

        if (reason is null || _checkInRandom.NextDouble() < 0.4)
        {
            return;
        }

        var today = GymCalendar.ToLocalDate(_now, gym.Settings.TimeZoneId);
        var at = StartOfDayUtc(today.AddDays(-_checkInRandom.Next(0, 6)), gym)
            .AddHours(_checkInRandom.Next(7, 20))
            .AddMinutes(_checkInRandom.Next(0, 60));

        if (at > _now)
        {
            at = _now.AddMinutes(-_checkInRandom.Next(10, 120));
        }

        var message = reason == CheckInDenialReason.ReusedPass
            ? "This code was already used. Copies of a pass don't work: show the live pass."
            : CheckInPolicy.Evaluate(profile, null, today, gym.Settings.GracePeriodDays).Message;

        _db.CheckInDenials.Add(new CheckInDenial
        {
            TenantId = gym.TenantId,
            MemberProfileId = profile.Id,
            Method = CheckInMethod.Kiosk,
            CheckInDeviceId = gym.KioskDeviceId,
            Reason = reason.Value,
            Message = message,
            AttemptedAtUtc = at
        });
    }

    // ---- Training ------------------------------------------------------------------------

    private static readonly (string Name, string Goal, (string Day, string[] Exercises)[] Days)[] RoutineTemplates =
    [
        ("Beginner Full Body", "Build a base of strength and consistency",
        [
            ("Full Body A", ["Back Squat", "Bench Press", "Barbell Row", "Plank"]),
            ("Full Body B", ["Romanian Deadlift", "Overhead Press", "Lat Pulldown", "Walking Lunge"]),
            ("Full Body C", ["Leg Press", "Incline Dumbbell Press", "Seated Cable Row", "Hanging Leg Raise"])
        ]),
        ("Push Pull Legs", "Muscle growth with higher weekly volume",
        [
            ("Push", ["Bench Press", "Overhead Press", "Incline Dumbbell Press", "Lateral Raise", "Triceps Pushdown"]),
            ("Pull", ["Deadlift", "Pull-Up", "Barbell Row", "Face Pull", "Barbell Curl"]),
            ("Legs", ["Back Squat", "Romanian Deadlift", "Leg Press", "Leg Curl", "Standing Calf Raise"])
        ]),
        ("Fat Loss Circuit", "Burn calories and improve conditioning",
        [
            ("Circuit A", ["Goblet Squat", "Push-Up", "Kettlebell Swing", "Rowing Machine"]),
            ("Circuit B", ["Walking Lunge", "Dumbbell Row", "Burpee", "Treadmill Run"])
        ]),
        ("Upper / Lower Strength", "Get stronger on the main lifts",
        [
            ("Upper", ["Bench Press", "Barbell Row", "Overhead Press", "Pull-Up"]),
            ("Lower", ["Back Squat", "Deadlift", "Walking Lunge", "Plank"])
        ])
    ];

    private async Task SeedTrainingAsync(
        GymContext gym,
        MemberProfile profile,
        double activity,
        bool isDemo,
        CancellationToken cancellationToken)
    {
        if (profile.Status == MemberStatus.Pending || (!isDemo && _random.NextDouble() > 0.65))
        {
            return;
        }

        var today = GymCalendar.ToLocalDate(_now, gym.Settings.TimeZoneId);
        var template = isDemo ? RoutineTemplates[2] : RoutineTemplates[_random.Next(RoutineTemplates.Length)];
        var byTrainer = isDemo || _random.NextDouble() < 0.6;
        var created = isDemo ? _now.AddDays(-88) : _now.AddDays(-_random.Next(40, 90));

        var routine = new WorkoutRoutine
        {
            TenantId = gym.TenantId,
            MemberProfileId = profile.Id,
            Name = template.Name,
            Goal = template.Goal,
            Notes = byTrainer ? "Warm up for 10 minutes first. Add weight when all sets feel easy." : "Designed with the AI coach.",
            Source = byTrainer ? RoutineSource.Trainer : RoutineSource.Ai,
            AssignedByUserId = byTrainer ? gym.TrainerUserId : null,
            IsActive = true,
            CreatedAtUtc = created,
            UpdatedAtUtc = created,
            Days = template.Days
                .Select((day, d) => new RoutineDay
                {
                    TenantId = gym.TenantId,
                    DayNumber = d + 1,
                    Name = day.Day,
                    Exercises = day.Exercises
                        .Select((exercise, e) => new RoutineExercise
                        {
                            TenantId = gym.TenantId,
                            Exercise = _exercises[exercise],
                            Order = e + 1,
                            Sets = 3,
                            Reps = _exercises[exercise].MuscleGroup == "Cardio" ? "10 min" : "8-12",
                            RestSeconds = 90
                        })
                        .ToList()
                })
                .ToList()
        };

        _db.WorkoutRoutines.Add(routine);
        await _db.SaveChangesAsync(cancellationToken);

        // Workouts on most check-in days, following the routine in rotation.
        var checkIns = _db.CheckIns.Local
            .Where(x => x.MemberProfileId == profile.Id && x.CheckedInAtUtc >= created)
            .OrderBy(x => x.CheckedInAtUtc)
            .ToList();

        var strength = 0.6 + _random.NextDouble() * 0.8;
        var dayIndex = 0;
        var workoutCount = 0;

        foreach (var checkIn in checkIns)
        {
            if (!isDemo && _random.NextDouble() > 0.7)
                continue;

            var day = routine.Days[dayIndex++ % routine.Days.Count];
            var performedOn = GymCalendar.ToLocalDate(checkIn.CheckedInAtUtc, gym.Settings.TimeZoneId);
            var progress = 1 + workoutCount++ * 0.01; // slow, steady progression

            var workout = new WorkoutLog
            {
                TenantId = gym.TenantId,
                MemberProfileId = profile.Id,
                WorkoutRoutineId = routine.Id,
                RoutineDayId = day.Id,
                PerformedOn = performedOn,
                DurationMinutes = _random.Next(40, 80),
                CreatedAtUtc = checkIn.CheckedInAtUtc.AddMinutes(70)
            };

            foreach (var exercise in day.Exercises)
            {
                var library = _exercises.Values.First(x => x.Id == exercise.ExerciseId);
                var baseWeight = BaseWeight(library) * strength * progress;

                for (var set = 1; set <= exercise.Sets; set++)
                {
                    workout.Sets.Add(new WorkoutSet
                    {
                        TenantId = gym.TenantId,
                        ExerciseId = exercise.ExerciseId,
                        SetNumber = set,
                        Reps = library.MuscleGroup == "Cardio" ? 1 : _random.Next(8, 13),
                        WeightKg = baseWeight <= 0 ? null : Math.Round((decimal)baseWeight / 2.5m) * 2.5m
                    });
                }
            }

            _db.WorkoutLogs.Add(workout);
        }

        // Weekly measurements trending toward the goal.
        var losing = isDemo || template.Name == "Fat Loss Circuit" || _random.NextDouble() < 0.4;
        var startWeight = isDemo ? 78.0 : (profile.Gender == "Female" ? 58 : 72) + _random.Next(0, 30);

        if (isDemo || _random.NextDouble() < 0.6)
        {
            for (var week = 12; week >= 0; week--)
            {
                var change = (12 - week) * (losing ? -0.45 : 0.15) + (_random.NextDouble() - 0.5) * 0.6;

                _db.BodyMetrics.Add(new BodyMetric
                {
                    TenantId = gym.TenantId,
                    MemberProfileId = profile.Id,
                    RecordedOn = today.AddDays(-7 * week),
                    WeightKg = Math.Round((decimal)(startWeight + change), 1),
                    BodyFatPercent = week % 4 == 0
                        ? Math.Round((decimal)((profile.Gender == "Female" ? 30 : 22) + (losing ? -0.15 : -0.05) * (12 - week)), 1)
                        : null
                });
            }
        }

        _db.FitnessGoals.Add(new FitnessGoal
        {
            TenantId = gym.TenantId,
            MemberProfileId = profile.Id,
            Type = losing ? GoalType.LoseWeight : GoalType.BuildMuscle,
            Description = losing
                ? $"Reach {Math.Round(startWeight - 8)} kg"
                : "Add 10 kg to my squat",
            TargetValue = losing ? Math.Round((decimal)startWeight - 8) : 10,
            TargetUnit = "kg",
            TargetDate = today.AddDays(90),
            Status = GoalStatus.Active,
            CreatedAtUtc = created
        });

        if (isDemo)
        {
            _db.FitnessGoals.Add(new FitnessGoal
            {
                TenantId = gym.TenantId,
                MemberProfileId = profile.Id,
                Type = GoalType.Endurance,
                Description = "Train at least 3 times a week",
                Status = GoalStatus.Active,
                CreatedAtUtc = created
            });

            AddNotification(gym, profile.UserId!.Value, NotificationType.Routine,
                $"New routine: {routine.Name}",
                "Your trainer assigned you a new routine. Open it in the app to start training.",
                $"/routines/{routine.Id}", created);
        }
    }

    private static double BaseWeight(Exercise exercise) => exercise.Name switch
    {
        "Deadlift" => 90,
        "Back Squat" or "Leg Press" => 70,
        "Romanian Deadlift" => 55,
        "Bench Press" => 50,
        "Barbell Row" => 45,
        "Overhead Press" => 32,
        "Lat Pulldown" or "Seated Cable Row" => 40,
        "Incline Dumbbell Press" or "Dumbbell Row" or "Goblet Squat" => 18,
        "Kettlebell Swing" => 16,
        "Barbell Curl" or "Triceps Pushdown" or "Leg Curl" => 22,
        "Lateral Raise" or "Face Pull" => 8,
        "Standing Calf Raise" => 50,
        "Walking Lunge" => 12,
        _ => 0
    };

    // ---- Announcements, agents and audit -----------------------------------------------------

    private void SeedAnnouncements(GymContext gym, User admin, List<MemberProfile> members)
    {
        var withApp = members.Where(x => x.UserId != null && x.Status != MemberStatus.Cancelled).ToList();

        var announcements = new[]
        {
            (Days: 21, Title: "New squat racks have arrived!", Body: "We installed four new squat racks on the strength floor. Enjoy, and please re-rack your weights."),
            (Days: 6, Title: "Holiday opening hours", Body: "Next Monday we open from 9:00 to 17:00. Regular hours resume on Tuesday.")
        };

        foreach (var (days, title, body) in announcements)
        {
            var sentAt = _now.AddDays(-days);

            _db.Announcements.Add(new Announcement
            {
                TenantId = gym.TenantId,
                Title = title,
                Body = body,
                Audience = AnnouncementAudience.All,
                SentByUserId = admin.Id,
                SentByName = admin.Username,
                SentAtUtc = sentAt,
                RecipientCount = withApp.Count
            });

            foreach (var member in withApp)
            {
                AddNotification(gym, member.UserId!.Value, NotificationType.Announcement, title, body, "/notifications", sentAt,
                    read: _random.NextDouble() < 0.6);
            }
        }

        foreach (var pending in members.Where(x => x.Status == MemberStatus.Pending))
        {
            AddNotification(gym, admin.Id, NotificationType.Membership, "New join request",
                $"{pending.FullName} ({pending.Email}) asked to join. Review it on the Join requests page.",
                "/join-requests", pending.JoinedAtUtc);
        }
    }

    private void SeedAgentActivity(GymContext gym, User admin, List<MemberProfile> members, List<string> agents)
    {
        if (gym.Tenant.SubscriptionStatus == SubscriptionStatus.Canceled)
        {
            return;
        }

        // Token usage for the platform's usage charts.
        var memberUsers = members.Where(x => x.UserId != null).Select(x => x.UserId!.Value).ToList();

        for (var day = 90; day >= 0; day--)
        {
            foreach (var agentId in agents)
            {
                var runs = agentId == AgentIds.FitnessCoach ? _random.Next(0, 6) : _random.Next(0, 3);

                for (var r = 0; r < runs; r++)
                {
                    var input = _random.Next(800, 4000);
                    var output = _random.Next(150, 900);

                    _db.AgentUsageRecords.Add(new AgentUsageRecord
                    {
                        TenantId = gym.TenantId,
                        UserId = agentId == AgentIds.FitnessCoach && memberUsers.Count > 0
                            ? memberUsers[_random.Next(memberUsers.Count)]
                            : admin.Id,
                        AgentId = agentId,
                        Operation = "chat",
                        ModelProfile = agentId == AgentIds.InsightsAgent ? "economy" : "standard",
                        Model = "demo-model",
                        InputTokens = input,
                        OutputTokens = output,
                        TotalTokens = input + output,
                        CreatedAtUtc = _now.AddDays(-day).AddHours(-_random.Next(0, 12))
                    });
                }
            }
        }

        // A short admin conversation.
        if (agents.Contains(AgentIds.GymAdmin))
        {
            AddConversation(gym, admin.Id, AgentIds.GymAdmin, "Overdue members this week", _now.AddDays(-2),
            [
                (ConversationMessageRole.User, "Who has an overdue invoice right now?"),
                (ConversationMessageRole.Assistant,
                    $"{members.Count(x => x.Status == MemberStatus.Expired)} members have expired because of overdue invoices, " +
                    "and a few active members are a few days late. Would you like me to send them a payment reminder? Each reminder needs your approval.")
            ]);
        }

        // The demo member's coaching chat.
        var demo = members.FirstOrDefault(x => x.Email == "member@ironparadise.demo");

        if (demo?.UserId is { } demoUser && agents.Contains(AgentIds.FitnessCoach))
        {
            AddConversation(gym, demoUser, AgentIds.FitnessCoach, "Getting to 70 kg", _now.AddDays(-1),
            [
                (ConversationMessageRole.User, "How am I doing with my weight loss?"),
                (ConversationMessageRole.Assistant,
                    "Great progress! You've gone from about 78 kg to around 72.6 kg over the last 12 weeks — roughly 0.45 kg a week, " +
                    "which is a healthy pace. You trained 3-4 times most weeks. Keep the Fat Loss Circuit going and add a little weight " +
                    "to your goblet squats when 12 reps feel easy."),
                (ConversationMessageRole.User, "Should I add more cardio?"),
                (ConversationMessageRole.Assistant,
                    "One extra 20-30 minute easy cardio session a week would help, as long as you recover well. " +
                    "If you feel any pain or unusual fatigue, scale back and talk to your trainer.")
            ]);
        }
    }

    private void SeedAudit(GymContext gym, User admin, List<MemberProfile> members)
    {
        foreach (var member in members.Take(12))
        {
            _db.AuditEvents.Add(new AuditEvent
            {
                TenantId = gym.TenantId,
                OccurredAtUtc = member.JoinedAtUtc,
                Category = AuditCategory.Membership,
                Action = member.Status == MemberStatus.Pending ? "MemberJoinRequested" : "MemberApproved",
                Outcome = AuditOutcome.Succeeded,
                ActorType = member.Status == MemberStatus.Pending ? AuditActorType.User : AuditActorType.User,
                UserId = member.Status == MemberStatus.Pending ? member.UserId : admin.Id,
                ResourceType = "MemberProfile",
                ResourceId = member.Id.ToString(),
                Reason = member.FullName
            });
        }

        for (var day = 14; day >= 0; day--)
        {
            _db.AuditEvents.Add(new AuditEvent
            {
                TenantId = gym.TenantId,
                OccurredAtUtc = _now.AddDays(-day).AddHours(-_random.Next(1, 10)),
                Category = AuditCategory.Authentication,
                Action = "AuthenticationSucceeded",
                Outcome = AuditOutcome.Succeeded,
                ActorType = AuditActorType.User,
                UserId = admin.Id,
                ResourceType = "User",
                ResourceId = admin.Id.ToString()
            });
        }
    }

    // ---- Building blocks -------------------------------------------------------------------

    private void AddConversation(
        GymContext gym,
        int userId,
        string agentId,
        string title,
        DateTime at,
        (ConversationMessageRole Role, string Text)[] messages)
    {
        var session = new AgentSession
        {
            Id = Guid.NewGuid(),
            TenantId = gym.TenantId,
            UserId = userId,
            AgentId = agentId,
            Title = title,
            Status = AgentSessionStatus.Active,
            CreatedAtUtc = at,
            UpdatedAtUtc = at.AddMinutes(messages.Length),
            LastMessageAtUtc = at.AddMinutes(messages.Length)
        };

        _db.AgentSessions.Add(session);

        for (var i = 0; i < messages.Length; i++)
        {
            _db.ConversationMessages.Add(new ConversationMessage
            {
                SessionId = session.Id,
                TenantId = gym.TenantId,
                Role = messages[i].Role,
                Content = messages[i].Text,
                CreatedAtUtc = at.AddMinutes(i)
            });
        }
    }

    private void AddNotification(
        GymContext gym,
        int userId,
        NotificationType type,
        string title,
        string body,
        string? link,
        DateTime at,
        bool read = false)
    {
        _db.Notifications.Add(new Notification
        {
            TenantId = gym.TenantId,
            UserId = userId,
            Type = type,
            Title = title,
            Body = body,
            Link = link,
            CreatedAtUtc = at,
            ReadAtUtc = read ? at.AddHours(2) : null
        });
    }

    private User AddUser(string username, string email, string platformRole)
    {
        var user = new User
        {
            Username = username,
            Email = email,
            Role = platformRole,
            IsActive = true,
            PasswordHash = _passwordHash,
            CreatedAtUtc = _now.AddDays(-400)
        };

        _db.Users.Add(user);

        return user;
    }

    private void AddMembership(int tenantId, int userId, string role, bool isActive = true)
    {
        _db.TenantMemberships.Add(new TenantMembership
        {
            TenantId = tenantId,
            UserId = userId,
            Role = role,
            IsActive = isActive,
            CreatedAtUtc = _now.AddDays(-400)
        });
    }

    private MembershipPlan PickPlan(List<MembershipPlan> plans)
    {
        // Monthly plans are the most common.
        var roll = _random.Next(100);

        return plans.Count switch
        {
            1 => plans[0],
            _ => roll < 60 ? plans[0] : roll < 85 || plans.Count == 2 ? plans[1] : plans[2]
        };
    }

    private static DateTime StartOfDayUtc(DateOnly day, GymContext gym) =>
        GymCalendar.StartOfDayUtc(day, gym.Settings.TimeZoneId);

    private static readonly string[] FirstNames =
    [
        "Ali", "Fatima", "Omar", "Zara", "Hamza", "Sana", "Bilal", "Hira", "Usman", "Mehwish",
        "James", "Emily", "Michael", "Olivia", "Daniel", "Sophia", "David", "Mia", "Lucas", "Grace",
        "Noah", "Ava", "Ethan", "Chloe", "Liam", "Isla", "Ryan", "Amelia", "Adam", "Hannah",
        "Carlos", "Lucia", "Mateo", "Elena", "Kenji", "Yuki", "Priya", "Arjun", "Leila", "Samir"
    ];

    private static readonly string[] LastNames =
    [
        "Khan", "Ahmed", "Raza", "Hussain", "Iqbal", "Siddiqui", "Smith", "Johnson", "Brown", "Taylor",
        "Wilson", "Davies", "Evans", "Thomas", "Garcia", "Martinez", "Lopez", "Tanaka", "Sato", "Patel",
        "Sharma", "Haddad", "Nasser", "Murphy", "Walker", "Wright", "Green", "Hall", "Clarke", "Lewis"
    ];

    private static string Name(int seed, int index)
    {
        var first = FirstNames[(index * 7 + seed * 3) % FirstNames.Length];
        var last = LastNames[(index * 11 + seed * 5) % LastNames.Length];

        return $"{first} {last}";
    }

    private void SeedExerciseLibrary()
    {
        (string Name, string Group, string Equipment, string Instructions)[] library =
        [
            ("Back Squat", "Legs", "Barbell", "Bar on upper back, feet shoulder-width, sit down and back until thighs are parallel, drive up."),
            ("Front Squat", "Legs", "Barbell", "Bar on front of shoulders, elbows high, squat down keeping the torso upright."),
            ("Goblet Squat", "Legs", "Dumbbell", "Hold a dumbbell at the chest and squat between your knees."),
            ("Leg Press", "Legs", "Machine", "Feet hip-width on the platform; lower until knees reach 90 degrees, press back."),
            ("Walking Lunge", "Legs", "Dumbbells", "Step forward into a lunge, back knee near the floor, alternate legs."),
            ("Bulgarian Split Squat", "Legs", "Dumbbells", "Rear foot on a bench, lower straight down on the front leg."),
            ("Leg Curl", "Legs", "Machine", "Curl the pad toward your glutes, control the way back."),
            ("Leg Extension", "Legs", "Machine", "Extend the knees fully, pause, lower slowly."),
            ("Standing Calf Raise", "Legs", "Machine", "Rise onto the balls of your feet, pause, lower fully."),
            ("Deadlift", "Back", "Barbell", "Hinge at the hips, flat back, push the floor away and stand tall."),
            ("Romanian Deadlift", "Glutes", "Barbell", "Soft knees, push hips back until you feel the hamstrings, return."),
            ("Hip Thrust", "Glutes", "Barbell", "Upper back on a bench, drive hips up until the body is straight."),
            ("Bench Press", "Chest", "Barbell", "Lower the bar to mid-chest with control, press up over the shoulders."),
            ("Incline Dumbbell Press", "Chest", "Dumbbells", "On a 30-degree incline, press the dumbbells up and together."),
            ("Push-Up", "Chest", "Bodyweight", "Hands under shoulders, body straight, lower the chest to the floor."),
            ("Cable Fly", "Chest", "Cable", "Slight bend in the elbows, bring the handles together in an arc."),
            ("Dips", "Chest", "Bodyweight", "Lean slightly forward, lower until shoulders are below elbows, press up."),
            ("Pull-Up", "Back", "Bodyweight", "Hang with an overhand grip, pull your chin over the bar."),
            ("Lat Pulldown", "Back", "Machine", "Pull the bar to the upper chest, squeeze the shoulder blades."),
            ("Barbell Row", "Back", "Barbell", "Hinge forward, pull the bar to the lower ribs, lower with control."),
            ("Dumbbell Row", "Back", "Dumbbell", "One hand on a bench, row the dumbbell to the hip."),
            ("Seated Cable Row", "Back", "Cable", "Sit tall, pull the handle to the stomach, squeeze the back."),
            ("Face Pull", "Shoulders", "Cable", "Pull the rope toward your face, elbows high, rotate the hands out."),
            ("Overhead Press", "Shoulders", "Barbell", "Press the bar from the shoulders to overhead, ribs down."),
            ("Dumbbell Shoulder Press", "Shoulders", "Dumbbells", "Seated, press the dumbbells overhead without arching."),
            ("Lateral Raise", "Shoulders", "Dumbbells", "Raise the dumbbells to the side to shoulder height."),
            ("Barbell Curl", "Arms", "Barbell", "Elbows at your sides, curl the bar up, lower slowly."),
            ("Hammer Curl", "Arms", "Dumbbells", "Neutral grip curls, elbows still."),
            ("Triceps Pushdown", "Arms", "Cable", "Elbows at your sides, push the bar down until arms are straight."),
            ("Skull Crusher", "Arms", "EZ bar", "Lower the bar toward your forehead, extend the elbows."),
            ("Plank", "Core", "Bodyweight", "Forearms down, body straight from head to heels; hold."),
            ("Hanging Leg Raise", "Core", "Bodyweight", "Hang from a bar, raise the legs without swinging."),
            ("Cable Crunch", "Core", "Cable", "Kneel, crunch the rope toward your knees using the abs."),
            ("Russian Twist", "Core", "Plate", "Lean back slightly, rotate the weight side to side."),
            ("Kettlebell Swing", "Glutes", "Kettlebell", "Hinge and snap the hips to swing the bell to chest height."),
            ("Burpee", "Cardio", "Bodyweight", "Squat, kick back to a plank, push-up, jump up."),
            ("Treadmill Run", "Cardio", "Treadmill", "Steady pace you can hold a conversation at."),
            ("Rowing Machine", "Cardio", "Rower", "Legs, then back, then arms; reverse on the way in."),
            ("Stationary Bike", "Cardio", "Bike", "Moderate resistance, steady cadence."),
            ("Jump Rope", "Cardio", "Rope", "Light bounces on the balls of your feet.")
        ];

        foreach (var (name, group, equipment, instructions) in library)
        {
            var exercise = new Exercise
            {
                TenantId = null,
                Name = name,
                MuscleGroup = group,
                Equipment = equipment,
                Instructions = instructions
            };

            _db.Exercises.Add(exercise);
            _exercises[name] = exercise;
        }
    }
}

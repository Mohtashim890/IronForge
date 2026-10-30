using IronForge.Application.Entities;
using IronForge.Application.Gym.Billing.Payments;
using IronForge.Application.Gym.Common;
using IronForge.Application.IntegrationTests.Infrastructure;
using IronForge.Infrastructure;
using IronForge.Shared.Authorization;
using IronForge.Shared.Models.Gym;
using IronForge.Shared.Subscriptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IronForge.Application.IntegrationTests.Gym;

/// <summary>
/// Gyms connect their own payment provider; members pay invoices through it.
/// An invoice is marked paid only on what the provider's API reports.
/// </summary>
public sealed class OnlinePaymentTests
    : IntegrationTestBase,
      IClassFixture<PostgreSqlTestDatabase>
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly TestClock _clock = new(Start);

    /// <summary>One provider for the whole test, like the provider's own records.</summary>
    private readonly TestPaymentProvider _provider = new();

    private readonly PaymentOptions _options = new()
    {
        AllowSimulated = false,
        EncryptionKey = "integration-test-payment-key-long-enough"
    };

    public OnlinePaymentTests(PostgreSqlTestDatabase database) : base(database)
    {
    }

    // ---- The admin connects the gym's account -------------------------------------

    [Fact]
    public async Task Admin_ConnectsProvider_KeysAreCheckedEncryptedAndNeverReturned()
    {
        var gym = await SeedGymAsync();

        var saved = await AsAdmin(gym, s => s.PaymentAccounts.SaveAsync(Keys()));

        Assert.True(saved.Success, saved.ErrorMessage);
        Assert.True(saved.Data!.Configured);
        Assert.Equal(TestPaymentProvider.Id, saved.Data.Provider);
        Assert.Equal("••••1234", saved.Data.ApiKeyHint);
        Assert.True(saved.Data.HasWebhookSecret);
        Assert.True(saved.Data.OnlinePaymentsAvailable);
        Assert.Equal($"https://api.test/api/payments/webhooks/{TestPaymentProvider.Id}/{gym.TenantId}", saved.Data.WebhookUrl);

        // Checked with the provider before saving.
        Assert.Contains(_provider.CredentialsUsed, x => x.ApiKey == "key_live_1234" && x.Secret == "secret_abcd");

        await using var db = Database.CreateDbContext();
        var row = await db.GymPaymentAccounts.IgnoreQueryFilters().SingleAsync(x => x.TenantId == gym.TenantId);

        Assert.StartsWith("v1.", row.ApiKeyEncrypted);
        Assert.DoesNotContain("key_live_1234", row.ApiKeyEncrypted);
        Assert.DoesNotContain("secret_abcd", row.SecretEncrypted);
        Assert.DoesNotContain("whsec", row.WebhookSecretEncrypted);
    }

    [Fact]
    public async Task KeysTheProviderRefuses_AreNotSaved()
    {
        var gym = await SeedGymAsync();
        _provider.CredentialsProblem = "the secret key was not accepted.";

        var saved = await AsAdmin(gym, s => s.PaymentAccounts.SaveAsync(Keys()));

        Assert.Equal(400, saved.StatusCode);
        Assert.Contains("the secret key was not accepted.", saved.ErrorMessage);

        var account = await AsAdmin(gym, s => s.PaymentAccounts.GetAsync());
        Assert.False(account.Data!.Configured);
    }

    [Fact]
    public async Task UpdatingWithBlankKeys_KeepsTheStoredOnes()
    {
        var gym = await SeedGymAsync();
        await AsAdmin(gym, s => s.PaymentAccounts.SaveAsync(Keys()));

        var updated = await AsAdmin(gym, s => s.PaymentAccounts.SaveAsync(new SavePaymentAccountRequest
        {
            Provider = TestPaymentProvider.Id,
            IsLive = true
        }));

        Assert.True(updated.Success, updated.ErrorMessage);
        Assert.True(updated.Data!.IsLive);

        var last = _provider.CredentialsUsed[^1];
        Assert.Equal(("key_live_1234", "secret_abcd", "whsec_1", true), (last.ApiKey, last.Secret, last.WebhookSecret, last.Live));
    }

    [Fact]
    public async Task ProviderMustChargeInTheGymsCurrency()
    {
        var gym = await SeedGymAsync(currency: "MYR");

        var saved = await AsAdmin(gym, s => s.PaymentAccounts.SaveAsync(Keys()));

        Assert.Equal(400, saved.StatusCode);
        Assert.Contains("MYR", saved.ErrorMessage);
    }

    [Fact]
    public async Task OnlyAdmins_ConfigurePayments()
    {
        var gym = await SeedGymAsync();
        var trainer = await SeedUserAsync(gym, "trainer@example.com", TenantRoles.Trainer);

        var asTrainer = await Run(TestServiceFactory.AsRole(gym.TenantId, trainer, TenantRoles.Trainer), s => s.PaymentAccounts.SaveAsync(Keys()));

        Assert.Equal(403, asTrainer.StatusCode);
    }

    [Fact]
    public async Task PaymentAccounts_AreIsolatedPerGym()
    {
        var gymA = await SeedGymAsync();
        var gymB = await SeedGymAsync();

        await AsAdmin(gymA, s => s.PaymentAccounts.SaveAsync(Keys()));

        var seenByB = await AsAdmin(gymB, s => s.PaymentAccounts.GetAsync());
        Assert.False(seenByB.Data!.Configured);

        await using var db = Database.CreateDbContext(TestServiceFactory.Context(gymB.TenantId));
        Assert.Empty(await db.GymPaymentAccounts.ToListAsync());
    }

    // ---- Members pay ---------------------------------------------------------------

    [Fact]
    public async Task Member_PaysThroughTheGymsProvider_ConfirmedOnlyByTheProvider()
    {
        var gym = await SeedGymAsync();
        var member = await SeedMemberAsync(gym, "ali@example.com");
        var invoiceId = await IssueInvoiceAsync(gym, member.MemberId);
        await AsAdmin(gym, s => s.PaymentAccounts.SaveAsync(Keys()));

        var checkout = await AsMember(gym, member, s => s.Billing.StartMyCheckoutAsync(invoiceId));

        Assert.True(checkout.Success, checkout.ErrorMessage);
        Assert.Equal(TestPaymentProvider.Id, checkout.Data!.Provider);

        var request = Assert.Single(_provider.Checkouts);
        Assert.Equal($"{gym.TenantId}:{invoiceId}", request.Reference);
        Assert.Equal($"https://api.test/payments/return/{gym.TenantId}/{invoiceId}", request.ReturnUrl);
        Assert.Equal(50m, request.Amount);
        Assert.Equal("ali@example.com", request.CustomerEmail);

        var session = (await LoadInvoiceAsync(invoiceId)).CheckoutSessionId!;

        // "I've paid" before the provider has the money: nothing changes.
        var early = await AsMember(gym, member, s => s.Billing.CheckMyCheckoutAsync(invoiceId));
        Assert.False(early.Data!.Paid);
        Assert.Equal(InvoiceStatus.Open, (await LoadInvoiceAsync(invoiceId)).Status);

        _provider.MarkPaid(session);

        var paid = await AsMember(gym, member, s => s.Billing.CheckMyCheckoutAsync(invoiceId));
        Assert.True(paid.Data!.Paid);

        // The return page (or a webhook) after that applies nothing twice.
        var again = await AsSystem(gym, s => s.OnlinePayments.ConfirmInvoiceAsync(gym.TenantId, invoiceId));
        Assert.True(again.Data);

        await using var db = Database.CreateDbContext();
        var payment = await db.Payments.IgnoreQueryFilters().SingleAsync(x => x.InvoiceId == invoiceId);

        Assert.Equal(PaymentMethod.Online, payment.Method);
        Assert.Equal(TestPaymentProvider.Id, payment.Provider);
        Assert.Equal($"pay_{session}", payment.ProviderReference);
        Assert.Equal(InvoiceStatus.Paid, (await LoadInvoiceAsync(invoiceId)).Status);
    }

    [Fact]
    public async Task Webhook_NeedsAValidSignature_ThenAppliesThePaymentOnce()
    {
        var gym = await SeedGymAsync();
        var member = await SeedMemberAsync(gym, "ali@example.com");
        var invoiceId = await IssueInvoiceAsync(gym, member.MemberId);
        await AsAdmin(gym, s => s.PaymentAccounts.SaveAsync(Keys()));
        await AsMember(gym, member, s => s.Billing.StartMyCheckoutAsync(invoiceId));

        var session = (await LoadInvoiceAsync(invoiceId)).CheckoutSessionId!;
        _provider.MarkPaid(session);

        var forged = await Webhook(gym, session, signature: "wrong");
        Assert.Equal(401, forged.StatusCode);
        Assert.Equal(InvoiceStatus.Open, (await LoadInvoiceAsync(invoiceId)).Status);

        var first = await Webhook(gym, session, signature: "whsec_1");
        var replay = await Webhook(gym, session, signature: "whsec_1");

        Assert.True(first.Data);
        Assert.True(replay.Success);

        await using var db = Database.CreateDbContext();
        Assert.Equal(1, await db.Payments.IgnoreQueryFilters().CountAsync(x => x.InvoiceId == invoiceId));
        Assert.True(await db.AuditEvents.IgnoreQueryFilters().AnyAsync(x => x.Action == "PaymentWebhookRejected" && x.TenantId == gym.TenantId));
    }

    [Fact]
    public async Task ProviderPayment_ForAnotherInvoice_OrAnotherAmount_IsNotApplied()
    {
        var gym = await SeedGymAsync();
        var member = await SeedMemberAsync(gym, "ali@example.com");
        var first = await IssueInvoiceAsync(gym, member.MemberId);
        var second = await IssueInvoiceAsync(gym, member.MemberId);
        await AsAdmin(gym, s => s.PaymentAccounts.SaveAsync(Keys()));
        await AsMember(gym, member, s => s.Billing.StartMyCheckoutAsync(first));

        var session = (await LoadInvoiceAsync(first)).CheckoutSessionId!;

        // The provider's record points at the second invoice: refused for the first.
        _provider.Statuses[session] = _provider.Statuses[session] with { State = CheckoutState.Paid, Reference = $"{gym.TenantId}:{second}" };
        var wrongInvoice = await AsSystem(gym, s => s.OnlinePayments.ConfirmInvoiceAsync(gym.TenantId, first));
        Assert.Equal(400, wrongInvoice.StatusCode);

        // Paid, but less than the invoice: refused.
        _provider.Statuses[session] = _provider.Statuses[session] with { Reference = $"{gym.TenantId}:{first}", Amount = 5m };
        var wrongAmount = await AsSystem(gym, s => s.OnlinePayments.ConfirmInvoiceAsync(gym.TenantId, first));
        Assert.Equal(400, wrongAmount.StatusCode);

        Assert.Equal(InvoiceStatus.Open, (await LoadInvoiceAsync(first)).Status);
        Assert.Equal(InvoiceStatus.Open, (await LoadInvoiceAsync(second)).Status);
    }

    [Fact]
    public async Task WithoutAProvider_TheSimulatedCheckoutIsUsedOnlyWhereAllowed()
    {
        var gym = await SeedGymAsync();
        var member = await SeedMemberAsync(gym, "ali@example.com");
        var invoiceId = await IssueInvoiceAsync(gym, member.MemberId);

        var refused = await AsMember(gym, member, s => s.Billing.StartMyCheckoutAsync(invoiceId));
        Assert.Equal(409, refused.StatusCode);

        _options.AllowSimulated = true;

        var simulated = await AsMember(gym, member, s => s.Billing.StartMyCheckoutAsync(invoiceId));
        Assert.True(simulated.Data!.Simulated);
        Assert.Equal(PaymentProviderIds.Simulated, (await LoadInvoiceAsync(invoiceId)).CheckoutProvider);

        // Once the gym connects its own provider, that is used.
        await AsAdmin(gym, s => s.PaymentAccounts.SaveAsync(Keys()));
        var real = await AsMember(gym, member, s => s.Billing.StartMyCheckoutAsync(invoiceId));
        Assert.Equal(TestPaymentProvider.Id, real.Data!.Provider);
    }

    [Fact]
    public async Task Disconnecting_StopsOnlinePayments()
    {
        var gym = await SeedGymAsync();
        var member = await SeedMemberAsync(gym, "ali@example.com");
        await AsAdmin(gym, s => s.PaymentAccounts.SaveAsync(Keys()));

        Assert.True((await AsMember(gym, member, s => s.GymMembers.GetMyMembershipAsync())).Data!.OnlinePaymentAvailable);

        var removed = await AsAdmin(gym, s => s.PaymentAccounts.RemoveAsync());
        Assert.False(removed.Data!.Configured);

        Assert.False((await AsMember(gym, member, s => s.GymMembers.GetMyMembershipAsync())).Data!.OnlinePaymentAvailable);
    }

    [Fact]
    public void Platform_RegistersHitPaySafepayAndTheSimulatedCheckout()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:Key"] = "a-jwt-key-that-is-long-enough-for-tests"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGymPlatform(configuration);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var catalog = scope.ServiceProvider.GetRequiredService<IPaymentProviderCatalog>();

        Assert.Equal(
            [PaymentProviderIds.HitPay, PaymentProviderIds.Safepay],
            catalog.Selectable.Select(x => x.Descriptor.Id));
        Assert.NotNull(catalog.Find(PaymentProviderIds.Simulated));
    }

    // ---- Helpers ---------------------------------------------------------------------

    private static SavePaymentAccountRequest Keys() => new()
    {
        Provider = TestPaymentProvider.Id,
        IsLive = false,
        ApiKey = "key_live_1234",
        Secret = "secret_abcd",
        WebhookSecret = "whsec_1"
    };

    private Task<IronForge.Application.Commons.ServiceResult<bool>> Webhook(SeededGym gym, string body, string signature) =>
        AsSystem(gym, s => s.OnlinePayments.HandleWebhookAsync(
            TestPaymentProvider.Id,
            gym.TenantId,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["X-Test-Signature"] = signature },
            body));

    private sealed record SeededGym(int TenantId, int PlanId, int AdminUserId);

    private sealed record SeededMember(int MemberId, int UserId);

    private async Task<SeededGym> SeedGymAsync(string currency = "USD")
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
            Currency = currency,
            TimeZoneId = "UTC",
            CreatedAtUtc = Start.UtcDateTime,
            UpdatedAtUtc = Start.UtcDateTime
        });
        db.MembershipPlans.Add(plan);
        await db.SaveChangesAsync();

        var admin = await AddUserAsync(db, tenant.Id, $"admin{tenant.Id}@gym.test", TenantRoles.Admin);

        return new SeededGym(tenant.Id, plan.Id, admin);
    }

    private async Task<int> SeedUserAsync(SeededGym gym, string email, string role)
    {
        await using var db = Database.CreateDbContext();

        return await AddUserAsync(db, gym.TenantId, email, role);
    }

    private static async Task<int> AddUserAsync(IronForge.Infrastructure.Data.IronForgeDbContext db, int tenantId, string email, string role)
    {
        var user = new User
        {
            Username = $"{email.Split('@')[0]}-{Guid.NewGuid():N}"[..30],
            Email = $"{Guid.NewGuid():N}-{email}",
            Role = PlatformRoles.User,
            IsActive = true,
            CreatedAtUtc = Start.UtcDateTime
        };

        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, "Password123!");
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

    private async Task<SeededMember> SeedMemberAsync(SeededGym gym, string email)
    {
        await using var db = Database.CreateDbContext();

        var userId = await AddUserAsync(db, gym.TenantId, email, TenantRoles.Member);

        var member = new MemberProfile
        {
            TenantId = gym.TenantId,
            UserId = userId,
            FullName = email.Split('@')[0],
            Email = email,
            MembershipPlanId = gym.PlanId,
            Status = MemberStatus.Active,
            JoinedAtUtc = Start.UtcDateTime,
            ApprovedAtUtc = Start.UtcDateTime
        };

        db.MemberProfiles.Add(member);
        await db.SaveChangesAsync();

        return new SeededMember(member.Id, userId);
    }

    private Task<int> IssueInvoiceAsync(SeededGym gym, int memberId) =>
        AsSystem(gym, async s =>
        {
            var engine = s.BillingEngine;
            var info = (await engine.GetGymAsync(gym.TenantId))!;
            var member = await s.Db.MemberProfiles.SingleAsync(x => x.Id == memberId);
            var plan = await s.Db.MembershipPlans.SingleAsync(x => x.Id == gym.PlanId);

            var invoice = await engine.IssueNextInvoiceAsync(info, member, plan);
            await s.Db.SaveChangesAsync();

            return invoice.Id;
        });

    private async Task<Invoice> LoadInvoiceAsync(int invoiceId)
    {
        await using var db = Database.CreateDbContext();

        return await db.Invoices.IgnoreQueryFilters().SingleAsync(x => x.Id == invoiceId);
    }

    private Task<T> AsAdmin<T>(SeededGym gym, Func<TestServiceFactory, Task<T>> action) =>
        Run(TestServiceFactory.AsRole(gym.TenantId, gym.AdminUserId, TenantRoles.Admin), action);

    private Task<T> AsMember<T>(SeededGym gym, SeededMember member, Func<TestServiceFactory, Task<T>> action) =>
        Run(TestServiceFactory.AsRole(gym.TenantId, member.UserId, TenantRoles.Member), action);

    /// <summary>Webhooks and return pages: a tenant, no user.</summary>
    private Task<T> AsSystem<T>(SeededGym gym, Func<TestServiceFactory, Task<T>> action) =>
        Run(TestServiceFactory.Context(gym.TenantId), action);

    private async Task<T> Run<T>(IronForge.Application.Execution.ExecutionContext context, Func<TestServiceFactory, Task<T>> action)
    {
        await using var db = Database.CreateDbContext(context);

        var services = new TestServiceFactory(db, context)
        {
            Clock = _clock,
            PaymentProvider = _provider,
            PaymentOptions = _options
        };

        return await action(services);
    }
}

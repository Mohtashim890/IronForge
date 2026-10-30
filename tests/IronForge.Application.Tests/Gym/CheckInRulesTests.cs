using IronForge.Application.Auth.Models;
using IronForge.Application.Entities;
using IronForge.Application.Gym.CheckIns;
using IronForge.Application.Gym.Members;
using IronForge.Shared.Authorization;
using IronForge.Shared.CheckIns;
using IronForge.Shared.Models.Gym;
using Microsoft.Extensions.Options;

namespace IronForge.Application.Tests.Gym;

public class CheckInRulesTests
{
    private static readonly DateOnly Today = new(2026, 3, 20);

    // ---- Pass codes --------------------------------------------------------------

    [Fact]
    public void PassCode_RoundTrips_AndVerifiesWithTheSameSecretOnly()
    {
        var secret = Keys().MemberPassSecret(7, 42, 1);
        var code = MemberPassCode.Create(secret, 7, 42, 1, 123456);

        Assert.True(MemberPassCode.TryParse(code, out var pass));
        Assert.Equal((7, 42, 1, 123456L), (pass!.TenantId, pass.MemberId, pass.KeyVersion, pass.Step));
        Assert.True(pass.HasValidSignature(secret));
        Assert.False(pass.HasValidSignature(Keys().MemberPassSecret(7, 42, 2)));
        Assert.False(pass.HasValidSignature(Keys("another-master-key-that-is-long-enough").MemberPassSecret(7, 42, 1)));
    }

    [Fact]
    public void PassCode_ChangingAnyPart_BreaksTheSignature()
    {
        var secret = Keys().MemberPassSecret(7, 42, 1);
        var code = MemberPassCode.Create(secret, 7, 42, 1, 123456);

        // Another member's id, or a later step, with the original signature.
        foreach (var tampered in new[] { code.Replace(".42.", ".43."), code.Replace(".123456.", ".123457.") })
        {
            Assert.True(MemberPassCode.TryParse(tampered, out var pass));
            Assert.False(pass!.HasValidSignature(secret));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("IF1.7.42.1.123456")]
    [InlineData("IF2.7.42.1.123456.0123456789abcdef01234567")]
    [InlineData("IF1.7.-42.1.123456.0123456789abcdef01234567")]
    [InlineData("IF1.7.42.1.123456.short")]
    public void PassCode_RejectsMalformedText(string code)
    {
        Assert.False(MemberPassCode.TryParse(code, out _));
    }

    [Fact]
    public void PassCode_StepChangesEvery30Seconds()
    {
        var start = new DateTimeOffset(2026, 3, 20, 9, 0, 0, TimeSpan.Zero);

        Assert.Equal(MemberPassCode.StepAt(start), MemberPassCode.StepAt(start.AddSeconds(29)));
        Assert.Equal(MemberPassCode.StepAt(start) + 1, MemberPassCode.StepAt(start.AddSeconds(30)));
    }

    [Fact]
    public void PassSecrets_DifferPerGymMemberAndVersion()
    {
        var keys = Keys();

        var secrets = new[]
        {
            keys.MemberPassSecret(1, 1, 1),
            keys.MemberPassSecret(2, 1, 1),
            keys.MemberPassSecret(1, 2, 1),
            keys.MemberPassSecret(1, 1, 2)
        };

        Assert.Equal(4, secrets.Select(Convert.ToBase64String).Distinct().Count());
    }

    // ---- Policy ------------------------------------------------------------------

    [Theory]
    [InlineData(MemberStatus.Pending, CheckInDenialReason.Pending)]
    [InlineData(MemberStatus.Frozen, CheckInDenialReason.Frozen)]
    [InlineData(MemberStatus.Expired, CheckInDenialReason.Expired)]
    [InlineData(MemberStatus.Cancelled, CheckInDenialReason.Cancelled)]
    public void OnlyActiveMembers_GetIn(MemberStatus status, CheckInDenialReason reason)
    {
        var decision = CheckInPolicy.Evaluate(Member(status), overdue: null, Today, gracePeriodDays: 14);

        Assert.False(decision.Allowed);
        Assert.Equal(reason, decision.Reason);
    }

    [Fact]
    public void ActiveMember_WithNothingOverdue_GetsInWithoutWarning()
    {
        var notDueYet = new OverdueInvoice("INV-000001", 50m, "USD", Today);

        var decision = CheckInPolicy.Evaluate(Member(MemberStatus.Active), notDueYet, Today, 14);

        Assert.True(decision.Allowed);
        Assert.Null(decision.Warning);
        Assert.Equal("Welcome, Sara!", decision.Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(14)]
    public void OverdueInvoice_WithinTheGracePeriod_GetsInWithAWarning(int daysOverdue)
    {
        var overdue = new OverdueInvoice("INV-000001", 50m, "USD", Today.AddDays(-daysOverdue));

        var decision = CheckInPolicy.Evaluate(Member(MemberStatus.Active), overdue, Today, 14);

        Assert.True(decision.Allowed);
        Assert.Contains("INV-000001", decision.Warning);
        Assert.Contains("USD 50.00", decision.Warning);
    }

    [Fact]
    public void OverdueInvoice_PastTheGracePeriod_IsRefused()
    {
        var overdue = new OverdueInvoice("INV-000001", 50m, "USD", Today.AddDays(-15));

        var decision = CheckInPolicy.Evaluate(Member(MemberStatus.Active), overdue, Today, 14);

        Assert.False(decision.Allowed);
        Assert.Equal(CheckInDenialReason.GracePeriodEnded, decision.Reason);
    }

    // ---- Photos and roles ----------------------------------------------------

    [Fact]
    public void Photos_AreRecognisedByTheirBytes_NotByTheirName()
    {
        Assert.Equal("image/jpeg", MemberPhotoService.DetectContentType([0xFF, 0xD8, 0xFF, 0xE0]));
        Assert.Equal("image/png", MemberPhotoService.DetectContentType([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]));
        Assert.Equal("image/webp", MemberPhotoService.DetectContentType("RIFF\0\0\0\0WEBPVP8 "u8));
        Assert.Null(MemberPhotoService.DetectContentType("<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8));
        Assert.Null(MemberPhotoService.DetectContentType([]));
    }

    [Theory]
    [InlineData(TenantRoles.Admin)]
    [InlineData(TenantRoles.Trainer)]
    [InlineData(TenantRoles.Member)]
    public void NoRole_HoldsTheDevicePermission(string role)
    {
        Assert.DoesNotContain(Permissions.GymCheckInsDevice, RolePermissions.GetPermissions(role));
    }

    private static CheckInKeys Keys(string key = "unit-test-member-pass-key-that-is-long") =>
        new(Options.Create(new CheckInOptions { PassSigningKey = key }));

    private static MemberProfile Member(MemberStatus status) => new()
    {
        Id = 1,
        TenantId = 1,
        FullName = "Sara Khan",
        Status = status,
        FrozenUntil = status == MemberStatus.Frozen ? Today.AddDays(5) : null
    };
}

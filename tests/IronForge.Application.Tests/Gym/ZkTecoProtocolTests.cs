using IronForge.Application.Entities;
using IronForge.Application.Gym.Access;
using IronForge.Application.Gym.CheckIns;
using IronForge.Shared.Models.Gym;

namespace IronForge.Application.Tests.Gym;

public class ZkTecoProtocolTests
{
    private static readonly DateOnly Today = new(2026, 3, 20);

    [Fact]
    public void Entries_AreReadFromAttLog_SkippingBadLines()
    {
        var entries = ZkTecoProtocol.ParseEntries(
            "1043\t2026-03-20 08:31:02\t0\t1\t0\t0\t0\r\n" +
            "\n" +
            "abc\t2026-03-20 08:31:02\t0\t1\n" +
            "1044\tnot a time\t0\t1\n" +
            "1045\t2026-03-20 19:00:00\t0\t15\t0\n");

        Assert.Equal(2, entries.Count);
        Assert.Equal(new TerminalEntry("1043", new DateTime(2026, 3, 20, 8, 31, 2), ZkTecoProtocol.VerifyFingerprint), entries[0]);
        Assert.Equal("1045", entries[1].Pin);
    }

    [Fact]
    public void Users_AreReadFromOperLog_AndFingerprintTemplatesAreDropped()
    {
        var users = ZkTecoProtocol.ParseUsers(
            "FP PIN=1043\tFID=6\tSize=1184\tValid=1\tTMP=SECRET-TEMPLATE\n" +
            "USER PIN=1043\tName=Sara Khan\tPri=0\tPasswd=\tCard=\tGrp=1\n" +
            "USER PIN=1044\tName=\tPri=14\n" +
            "BIODATA Pin=1045\tNo=0\tIndex=0\tValid=1\tType=1\tTmp=SECRET\n" +
            "OPLOG 4\t0\t2026-03-20 08:00:00\t0\t0\t0\t0\n" +
            "PIN=1046\tName=Bare UserInfo Line\n");

        Assert.Equal(4, users.Count);
        Assert.Equal(new TerminalUserRecord("1043", "Sara Khan", true), users.Single(x => x.Pin == "1043"));
        Assert.Equal(new TerminalUserRecord("1044", null, false), users.Single(x => x.Pin == "1044"));
        Assert.True(users.Single(x => x.Pin == "1045").HasFingerprint);
        Assert.Equal("Bare UserInfo Line", users.Single(x => x.Pin == "1046").Name);

        // Nothing of the template survives parsing.
        Assert.DoesNotContain(users, x => (x.Name ?? "").Contains("SECRET"));
    }

    [Fact]
    public void CommandResults_AreRead()
    {
        var results = ZkTecoProtocol.ParseResults("ID=12&Return=0&CMD=DATA\nID=13&Return=-1002&CMD=DATA\nnonsense\n");

        Assert.Equal([new TerminalCommandResult(12, 0), new TerminalCommandResult(13, -1002)], results);
    }

    [Fact]
    public void UpdateUser_SetsAnEndDate_AndNeverDeletes()
    {
        var allowed = ZkTecoProtocol.UpdateUser("1043", "Sara\tKhan=X", enabled: true, new DateOnly(2026, 4, 30), Today);
        var refused = ZkTecoProtocol.UpdateUser("1043", "Sara Khan", enabled: false, new DateOnly(2026, 4, 30), Today);

        Assert.StartsWith("DATA UPDATE USERINFO PIN=1043\tName=SaraKhanX\t", allowed);
        Assert.Contains("EndDatetime=2026-04-30 23:59:59", allowed);
        Assert.Contains("EndDatetime=2026-03-19 23:59:59", refused);
        Assert.DoesNotContain("DELETE", allowed + refused);
    }

    [Fact]
    public void Commands_AreNumbered_OrOk()
    {
        Assert.Equal("OK", ZkTecoProtocol.Commands([]));
        Assert.Equal("C:7:DATA QUERY USERINFO\n", ZkTecoProtocol.Commands([(7L, ZkTecoProtocol.QueryUsers)]));
    }

    [Theory]
    [InlineData(" cjde-2124 60012 ", "CJDE212460012")]
    [InlineData("", "")]
    public void SerialNumbers_AreNormalized(string input, string expected)
    {
        Assert.Equal(expected, ZkTecoProtocol.NormalizeSerial(input));
    }

    [Theory]
    [InlineData("  Sara  KHAN.", "sara khan")]
    [InlineData("Muhammad Abdullah Khan Niazi Jr", "muhammad abdullah khan n")]
    public void Names_AreMatchedLoosely(string name, string normalized)
    {
        Assert.Equal(normalized, TerminalAdminService.NormalizeName(name));
    }

    // ---- What the terminal enforces offline ------------------------------------

    [Fact]
    public void PaidMember_IsValidUntilTheGracePeriodAfterTheNextPayment()
    {
        var access = TerminalAccessRules.For(Member(MemberStatus.Active, paidThrough: Today.AddDays(10)), null, Today, 14);

        Assert.True(access.Enabled);
        Assert.Equal(Today.AddDays(10 + 1 + 14), access.ValidUntil);
    }

    [Fact]
    public void OverdueMember_IsValidUntilTheEndOfTheGracePeriod()
    {
        var overdue = new OverdueInvoice("INV-1", 50m, "USD", Today.AddDays(-4));

        var access = TerminalAccessRules.For(Member(MemberStatus.Active), overdue, Today, 14);

        Assert.True(access.Enabled);
        Assert.Equal(Today.AddDays(10), access.ValidUntil);
    }

    [Theory]
    [InlineData(MemberStatus.Frozen)]
    [InlineData(MemberStatus.Expired)]
    [InlineData(MemberStatus.Cancelled)]
    [InlineData(MemberStatus.Pending)]
    public void RefusedMembers_EndedYesterday(MemberStatus status)
    {
        var access = TerminalAccessRules.For(Member(status), null, Today, 14);

        Assert.False(access.Enabled);
        Assert.Equal(Today.AddDays(-1), access.ValidUntil);
    }

    [Fact]
    public void DeletedMember_IsRefused()
    {
        var member = Member(MemberStatus.Active);
        member.AnonymizedAtUtc = DateTime.UtcNow;

        Assert.False(TerminalAccessRules.For(member, null, Today, 14).Enabled);
    }

    private static MemberProfile Member(MemberStatus status, DateOnly? paidThrough = null) => new()
    {
        FullName = "Sara Khan",
        Status = status,
        PaidThrough = paidThrough
    };
}

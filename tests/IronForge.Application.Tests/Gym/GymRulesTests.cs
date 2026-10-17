using IronForge.Application.Auth.Models;
using IronForge.Application.Gym.Common;
using IronForge.Shared.Authorization;
using IronForge.Shared.Models.Gym;

namespace IronForge.Application.Tests.Gym;

public class GymRulesTests
{
    [Theory]
    [InlineData("2026-01-15", BillingInterval.Monthly, "2026-02-14")]
    [InlineData("2026-01-31", BillingInterval.Monthly, "2026-02-27")]
    [InlineData("2026-01-01", BillingInterval.Quarterly, "2026-03-31")]
    [InlineData("2026-03-10", BillingInterval.Yearly, "2027-03-09")]
    public void PeriodEnd_IsTheDayBeforeTheNextPeriod(string start, BillingInterval interval, string expected)
    {
        Assert.Equal(
            DateOnly.Parse(expected),
            GymCalendar.PeriodEnd(DateOnly.Parse(start), interval));
    }

    [Fact]
    public void ConsecutivePeriods_NeverOverlapOrLeaveGaps()
    {
        var start = new DateOnly(2026, 1, 31);

        for (var i = 0; i < 24; i++)
        {
            var end = GymCalendar.PeriodEnd(start, BillingInterval.Monthly);

            Assert.True(end >= start);
            start = end.AddDays(1);
        }
    }

    [Theory]
    [InlineData("2026-10-12", "2026-10-12")] // Monday
    [InlineData("2026-10-18", "2026-10-12")] // Sunday
    [InlineData("2026-10-14", "2026-10-12")]
    public void WeekStart_IsMonday(string day, string monday)
    {
        Assert.Equal(DateOnly.Parse(monday), GymCalendar.WeekStart(DateOnly.Parse(day)));
    }

    [Fact]
    public void WeeklyStreak_CountsConsecutiveWeeks_AndForgivesTheCurrentEmptyWeek()
    {
        var today = new DateOnly(2026, 10, 14); // Wednesday

        // Last week and the two before it; nothing yet this week.
        var days = new[]
        {
            new DateOnly(2026, 10, 6),
            new DateOnly(2026, 9, 30),
            new DateOnly(2026, 9, 22),
            new DateOnly(2026, 9, 1) // a gap before this one
        };

        Assert.Equal(3, GymCalendar.WeeklyStreak(days, today));
        Assert.Equal(4, GymCalendar.WeeklyStreak(days.Append(today), today));
        Assert.Equal(0, GymCalendar.WeeklyStreak([new DateOnly(2026, 9, 1)], today));
    }

    [Fact]
    public void JoinCodes_AreUnambiguousAndNormalized()
    {
        for (var i = 0; i < 200; i++)
        {
            var code = GymCalendar.NewJoinCode();

            Assert.Equal(8, code.Length);
            Assert.DoesNotContain('0', code);
            Assert.DoesNotContain('O', code);
            Assert.DoesNotContain('1', code);
            Assert.DoesNotContain('I', code);
        }

        Assert.Equal("ABCD2345", GymCalendar.NormalizeJoinCode(" abcd-2345 "));
    }

    [Fact]
    public void ToLocalDate_UsesTheGymTimeZone()
    {
        var utc = new DateTime(2026, 10, 16, 21, 30, 0, DateTimeKind.Utc);

        Assert.Equal(new DateOnly(2026, 10, 16), GymCalendar.ToLocalDate(utc, "UTC"));
        Assert.Equal(new DateOnly(2026, 10, 17), GymCalendar.ToLocalDate(utc, "Asia/Karachi"));
        Assert.Equal(new DateOnly(2026, 10, 16), GymCalendar.ToLocalDate(utc, "not/a-zone"));
    }

    [Fact]
    public void Members_OnlyHoldSelfServicePermissions()
    {
        var permissions = RolePermissions.GetPermissions(TenantRoles.Member);

        Assert.NotEmpty(permissions);
        Assert.All(permissions, permission => Assert.StartsWith("gym.self.", permission));
    }

    [Fact]
    public void Trainers_CannotTouchBillingSettingsOrStaff()
    {
        var permissions = RolePermissions.GetPermissions(TenantRoles.Trainer);

        Assert.Contains(Permissions.GymMembersRead, permissions);
        Assert.Contains(Permissions.GymCheckInsManage, permissions);
        Assert.Contains(Permissions.GymRoutinesManage, permissions);
        Assert.DoesNotContain(Permissions.GymBillingRead, permissions);
        Assert.DoesNotContain(Permissions.GymBillingManage, permissions);
        Assert.DoesNotContain(Permissions.GymSettingsManage, permissions);
        Assert.DoesNotContain(Permissions.MembersManage, permissions);
        Assert.DoesNotContain(Permissions.ApprovalsDecide, permissions);
    }

    [Fact]
    public void Admins_HaveEveryStaffPermission_ButNoSelfService()
    {
        var permissions = RolePermissions.GetPermissions(TenantRoles.Admin);

        Assert.Contains(Permissions.GymBillingManage, permissions);
        Assert.Contains(Permissions.GymNotificationsSend, permissions);
        Assert.Contains(Permissions.ApprovalsDecide, permissions);
        Assert.DoesNotContain(permissions, permission => permission.StartsWith("gym.self."));
    }

    [Fact]
    public void UnknownRoles_GetNothing()
    {
        Assert.Empty(RolePermissions.GetPermissions("Viewer"));
        Assert.Empty(RolePermissions.GetPermissions("User"));
    }
}

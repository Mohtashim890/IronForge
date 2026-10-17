namespace IronForge.Shared.Models.Gym
{
    public sealed class GymDashboardDto
    {
        public string GymName { get; set; } = "";

        public string Currency { get; set; } = "USD";

        public int ActiveMembers { get; set; }

        public int PendingMembers { get; set; }

        public int FrozenMembers { get; set; }

        public int ExpiredMembers { get; set; }

        public int NewMembersThisMonth { get; set; }

        public decimal RevenueThisMonth { get; set; }

        public decimal RevenueLastMonth { get; set; }

        public decimal OverdueAmount { get; set; }

        public int OverdueInvoices { get; set; }

        public int CheckInsToday { get; set; }

        public int CheckInsLast7Days { get; set; }

        /// <summary>Members whose paid period ends in the next 7 days.</summary>
        public int ExpiringSoon { get; set; }

        public List<RevenuePointDto> RevenueByMonth { get; set; } = [];

        public List<DailyCountDto> CheckInsByDay { get; set; } = [];

        public List<PlanBreakdownDto> MembersByPlan { get; set; } = [];
    }

    public sealed class DailyCountDto
    {
        public DateOnly Day { get; set; }

        public int Count { get; set; }
    }

    public sealed class PlanBreakdownDto
    {
        public string PlanName { get; set; } = "";

        public int Members { get; set; }
    }

    public sealed class AttendanceSummaryDto
    {
        public DateOnly From { get; set; }

        public DateOnly To { get; set; }

        public int TotalCheckIns { get; set; }

        public int UniqueMembers { get; set; }

        public string? BusiestDayOfWeek { get; set; }

        public int? BusiestHour { get; set; }

        public List<DailyCountDto> ByDay { get; set; } = [];

        /// <summary>Active members with no check-in in the period.</summary>
        public List<MemberSummaryDto> InactiveMembers { get; set; } = [];
    }
}

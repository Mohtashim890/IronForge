namespace IronForge.Application.Gym.AgentTools;

/// <summary>Every gym agent tool, for DI registration and tests.</summary>
public static class GymAgentToolCatalog
{
    public static readonly IReadOnlyList<Type> Types =
    [
        // Fitness Coach (member self-service).
        typeof(GetMyProfileAndGoalsAgentTool),
        typeof(GetMyRoutinesAgentTool),
        typeof(GetExerciseLibraryAgentTool),
        typeof(GetMyWorkoutHistoryAgentTool),
        typeof(GetMyProgressAgentTool),
        typeof(LogWorkoutAgentTool),
        typeof(RecordBodyMetricAgentTool),
        typeof(SetFitnessGoalAgentTool),
        typeof(CreateRoutineForMeAgentTool),
        typeof(GetMyMembershipAndInvoicesAgentTool),

        // Gym Admin (staff).
        typeof(SearchMembersAgentTool),
        typeof(GetMemberDetailsAgentTool),
        typeof(GetDashboardSummaryAgentTool),
        typeof(GetAttendanceSummaryAgentTool),
        typeof(ListExpiringMembershipsAgentTool),
        typeof(FreezeMembershipAgentTool),
        typeof(ListOverdueInvoicesAgentTool),
        typeof(GetRevenueSummaryAgentTool),
        typeof(SendPaymentReminderAgentTool),
        typeof(RecordCashPaymentAgentTool),
        typeof(SendAnnouncementAgentTool)
    ];
}

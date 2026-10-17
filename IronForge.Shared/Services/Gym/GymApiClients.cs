using IronForge.Shared.Models;
using IronForge.Shared.Models.Gym;
using IronForge.Shared.Models.Tenants;

namespace IronForge.Shared.Services.Gym;

/// <summary>Staff operations of the selected gym (api/gym/*), used by the admin web app.</summary>
public sealed class GymApiClient : ApiClientBase
{
    public GymApiClient(HttpClient http) : base(http)
    {
    }

    // Dashboard and settings.
    public Task<ServiceResult<GymDashboardDto>> GetDashboardAsync(CancellationToken ct = default) =>
        GetAsync<GymDashboardDto>("api/gym/dashboard", "Unable to load the dashboard.", ct);

    public Task<ServiceResult<List<MemberSummaryDto>>> GetExpiringAsync(int days = 7, CancellationToken ct = default) =>
        GetAsync<List<MemberSummaryDto>>(Query("api/gym/expiring", ("days", days)), "Unable to load expiring memberships.", ct);

    public Task<ServiceResult<GymSettingsDto>> GetSettingsAsync(CancellationToken ct = default) =>
        GetAsync<GymSettingsDto>("api/gym/settings", "Unable to load gym settings.", ct);

    public Task<ServiceResult<GymSettingsDto>> UpdateSettingsAsync(UpdateGymSettingsRequest request, CancellationToken ct = default) =>
        PutAsync<GymSettingsDto>("api/gym/settings", request, "Unable to save gym settings.", ct);

    public Task<ServiceResult<GymSettingsDto>> RegenerateJoinCodeAsync(CancellationToken ct = default) =>
        PostAsync<GymSettingsDto>("api/gym/settings/join-code", null, "Unable to create a new join code.", ct);

    public Task<ServiceResult<StripeConnectLinkDto>> ConnectStripeAsync(CancellationToken ct = default) =>
        PostAsync<StripeConnectLinkDto>("api/gym/settings/stripe/connect", null, "Unable to start Stripe onboarding.", ct);

    public Task<ServiceResult<GymSettingsDto>> RefreshStripeAsync(CancellationToken ct = default) =>
        PostAsync<GymSettingsDto>("api/gym/settings/stripe/refresh", null, "Unable to check the Stripe account.", ct);

    // Plans.
    public Task<ServiceResult<List<MembershipPlanDto>>> GetPlansAsync(bool includeArchived = false, CancellationToken ct = default) =>
        GetAsync<List<MembershipPlanDto>>(Query("api/gym/plans", ("includeArchived", includeArchived)), "Unable to load plans.", ct);

    public Task<ServiceResult<MembershipPlanDto>> CreatePlanAsync(SaveMembershipPlanRequest request, CancellationToken ct = default) =>
        PostAsync<MembershipPlanDto>("api/gym/plans", request, "Unable to create the plan.", ct);

    public Task<ServiceResult<MembershipPlanDto>> UpdatePlanAsync(int planId, SaveMembershipPlanRequest request, CancellationToken ct = default) =>
        PutAsync<MembershipPlanDto>($"api/gym/plans/{planId}", request, "Unable to save the plan.", ct);

    // Members.
    public Task<ServiceResult<PagedResult<MemberSummaryDto>>> GetMembersAsync(MemberQuery query, CancellationToken ct = default) =>
        GetAsync<PagedResult<MemberSummaryDto>>(
            Query("api/gym/members",
                ("search", query.Search),
                ("status", query.Status),
                ("membershipPlanId", query.MembershipPlanId),
                ("overdueOnly", query.OverdueOnly ? true : null),
                ("page", query.Page),
                ("pageSize", query.PageSize)),
            "Unable to load members.",
            ct);

    public Task<ServiceResult<MemberDetailDto>> GetMemberAsync(int memberId, CancellationToken ct = default) =>
        GetAsync<MemberDetailDto>($"api/gym/members/{memberId}", "Unable to load the member.", ct);

    public Task<ServiceResult<MemberSummaryDto>> CreateMemberAsync(CreateMemberRequest request, CancellationToken ct = default) =>
        PostAsync<MemberSummaryDto>("api/gym/members", request, "Unable to add the member.", ct);

    public Task<ServiceResult<MemberSummaryDto>> UpdateMemberAsync(int memberId, UpdateMemberRequest request, CancellationToken ct = default) =>
        PutAsync<MemberSummaryDto>($"api/gym/members/{memberId}", request, "Unable to save the member.", ct);

    public Task<ServiceResult<MemberSummaryDto>> ApproveMemberAsync(int memberId, int? planId = null, CancellationToken ct = default) =>
        PostAsync<MemberSummaryDto>($"api/gym/members/{memberId}/approve", new ApproveMemberRequest { MembershipPlanId = planId }, "Unable to approve the member.", ct);

    public Task<ServiceResult<MemberSummaryDto>> RejectMemberAsync(int memberId, CancellationToken ct = default) =>
        PostAsync<MemberSummaryDto>($"api/gym/members/{memberId}/reject", null, "Unable to decline the request.", ct);

    public Task<ServiceResult<MemberSummaryDto>> FreezeMemberAsync(int memberId, FreezeMemberRequest request, CancellationToken ct = default) =>
        PostAsync<MemberSummaryDto>($"api/gym/members/{memberId}/freeze", request, "Unable to freeze the membership.", ct);

    public Task<ServiceResult<MemberSummaryDto>> UnfreezeMemberAsync(int memberId, CancellationToken ct = default) =>
        PostAsync<MemberSummaryDto>($"api/gym/members/{memberId}/unfreeze", null, "Unable to unfreeze the membership.", ct);

    public Task<ServiceResult<MemberSummaryDto>> CancelMemberAsync(int memberId, CancellationToken ct = default) =>
        PostAsync<MemberSummaryDto>($"api/gym/members/{memberId}/cancel", null, "Unable to cancel the membership.", ct);

    public Task<ServiceResult<MemberSummaryDto>> ReactivateMemberAsync(int memberId, CancellationToken ct = default) =>
        PostAsync<MemberSummaryDto>($"api/gym/members/{memberId}/reactivate", null, "Unable to reactivate the membership.", ct);

    public Task<ServiceResult<MemberSummaryDto>> ChangePlanAsync(int memberId, int planId, CancellationToken ct = default) =>
        PutAsync<MemberSummaryDto>($"api/gym/members/{memberId}/plan", new ChangeMemberPlanRequest { MembershipPlanId = planId }, "Unable to change the plan.", ct);

    public Task<ServiceResult<bool>> AnonymizeMemberAsync(int memberId, CancellationToken ct = default) =>
        DeleteAsync($"api/gym/members/{memberId}", "Unable to delete the member's data.", ct);

    public Task<ServiceResult<ProgressSummaryDto>> GetMemberProgressAsync(int memberId, CancellationToken ct = default) =>
        GetAsync<ProgressSummaryDto>($"api/gym/members/{memberId}/progress", "Unable to load progress.", ct);

    public Task<ServiceResult<RoutineDto>> AssignRoutineAsync(int memberId, SaveRoutineRequest request, CancellationToken ct = default) =>
        PostAsync<RoutineDto>($"api/gym/members/{memberId}/routines", request, "Unable to assign the routine.", ct);

    public Task<ServiceResult<RoutineDto>> GetRoutineAsync(int routineId, CancellationToken ct = default) =>
        GetAsync<RoutineDto>($"api/gym/routines/{routineId}", "Unable to load the routine.", ct);

    public Task<ServiceResult<RoutineDto>> SetRoutineActiveAsync(int routineId, bool active, CancellationToken ct = default) =>
        PutAsync<RoutineDto>(Query($"api/gym/routines/{routineId}/active", ("value", active)), null, "Unable to update the routine.", ct);

    public Task<ServiceResult<List<ExerciseDto>>> GetExercisesAsync(string? search = null, string? muscleGroup = null, CancellationToken ct = default) =>
        GetAsync<List<ExerciseDto>>(Query("api/gym/exercises", ("search", search), ("muscleGroup", muscleGroup)), "Unable to load exercises.", ct);

    public Task<ServiceResult<ExerciseDto>> CreateExerciseAsync(SaveExerciseRequest request, CancellationToken ct = default) =>
        PostAsync<ExerciseDto>("api/gym/exercises", request, "Unable to add the exercise.", ct);

    // Check-ins.
    public Task<ServiceResult<List<CheckInDto>>> GetCheckInsAsync(int take = 50, CancellationToken ct = default) =>
        GetAsync<List<CheckInDto>>(Query("api/gym/checkins", ("take", take)), "Unable to load check-ins.", ct);

    public Task<ServiceResult<CheckInDto>> CheckInAsync(int memberId, CancellationToken ct = default) =>
        PostAsync<CheckInDto>("api/gym/checkins", new CreateCheckInRequest { MemberId = memberId }, "Unable to check in.", ct);

    public Task<ServiceResult<AttendanceSummaryDto>> GetAttendanceAsync(int days = 30, CancellationToken ct = default) =>
        GetAsync<AttendanceSummaryDto>(Query("api/gym/checkins/attendance", ("days", days)), "Unable to load attendance.", ct);

    // Billing.
    public Task<ServiceResult<PagedResult<InvoiceDto>>> GetInvoicesAsync(InvoiceQuery query, CancellationToken ct = default) =>
        GetAsync<PagedResult<InvoiceDto>>(
            Query("api/gym/invoices",
                ("status", query.Status),
                ("memberId", query.MemberId),
                ("dueFrom", query.DueFrom),
                ("dueTo", query.DueTo),
                ("search", query.Search),
                ("page", query.Page),
                ("pageSize", query.PageSize)),
            "Unable to load invoices.",
            ct);

    public Task<ServiceResult<PaymentDto>> RecordPaymentAsync(int invoiceId, RecordPaymentRequest request, CancellationToken ct = default) =>
        PostAsync<PaymentDto>($"api/gym/invoices/{invoiceId}/payments", request, "Unable to record the payment.", ct);

    public Task<ServiceResult<InvoiceDto>> VoidInvoiceAsync(int invoiceId, CancellationToken ct = default) =>
        PostAsync<InvoiceDto>($"api/gym/invoices/{invoiceId}/void", null, "Unable to void the invoice.", ct);

    public Task<ServiceResult<SendReminderResultDto>> SendReminderAsync(int invoiceId, CancellationToken ct = default) =>
        PostAsync<SendReminderResultDto>($"api/gym/invoices/{invoiceId}/reminders", null, "Unable to send the reminder.", ct);

    public Task<ServiceResult<PagedResult<PaymentDto>>> GetPaymentsAsync(int page = 1, int pageSize = 25, CancellationToken ct = default) =>
        GetAsync<PagedResult<PaymentDto>>(Query("api/gym/payments", ("page", page), ("pageSize", pageSize)), "Unable to load payments.", ct);

    // Announcements.
    public Task<ServiceResult<List<AnnouncementDto>>> GetAnnouncementsAsync(CancellationToken ct = default) =>
        GetAsync<List<AnnouncementDto>>("api/gym/announcements", "Unable to load announcements.", ct);

    public Task<ServiceResult<AnnouncementDto>> SendAnnouncementAsync(CreateAnnouncementRequest request, CancellationToken ct = default) =>
        PostAsync<AnnouncementDto>("api/gym/announcements", request, "Unable to send the announcement.", ct);

    // Activity log.
    public Task<ServiceResult<AuditPageDto>> GetAuditAsync(int page = 1, int pageSize = 50, string? category = null, CancellationToken ct = default) =>
        GetAsync<AuditPageDto>(Query("api/audit", ("page", page), ("pageSize", pageSize), ("category", category)), "Unable to load activity.", ct);
}

/// <summary>The signed-in user's notifications in the selected gym (staff and members).</summary>
public sealed class NotificationsApiClient : ApiClientBase
{
    public NotificationsApiClient(HttpClient http) : base(http)
    {
    }

    public Task<ServiceResult<List<NotificationDto>>> GetAsync(bool unreadOnly = false, int take = 50, CancellationToken ct = default) =>
        GetAsync<List<NotificationDto>>(Query("api/notifications", ("unreadOnly", unreadOnly ? true : null), ("take", take)), "Unable to load notifications.", ct);

    public Task<ServiceResult<UnreadCountDto>> GetUnreadCountAsync(CancellationToken ct = default) =>
        GetAsync<UnreadCountDto>("api/notifications/unread-count", "Unable to load notifications.", ct);

    public Task<ServiceResult<bool>> MarkReadAsync(long id, CancellationToken ct = default) =>
        PostAsync<bool>($"api/notifications/{id}/read", null, "Unable to update the notification.", ct);

    public Task<ServiceResult<int>> MarkAllReadAsync(CancellationToken ct = default) =>
        PostAsync<int>("api/notifications/read-all", null, "Unable to update notifications.", ct);
}

/// <summary>Self-service for the signed-in gym member (api/me/*), used by the mobile app.</summary>
public sealed class MemberApiClient : ApiClientBase
{
    public MemberApiClient(HttpClient http) : base(http)
    {
    }

    public Task<ServiceResult<MyMembershipDto>> GetMembershipAsync(CancellationToken ct = default) =>
        GetAsync<MyMembershipDto>("api/me/membership", "Unable to load your membership.", ct);

    public Task<ServiceResult<MyMembershipDto>> UpdateProfileAsync(UpdateMyProfileRequest request, CancellationToken ct = default) =>
        PutAsync<MyMembershipDto>("api/me/profile", request, "Unable to save your profile.", ct);

    public Task<ServiceResult<List<MembershipPlanDto>>> GetPlansAsync(CancellationToken ct = default) =>
        GetAsync<List<MembershipPlanDto>>("api/me/plans", "Unable to load plans.", ct);

    public Task<ServiceResult<List<InvoiceDto>>> GetInvoicesAsync(CancellationToken ct = default) =>
        GetAsync<List<InvoiceDto>>("api/me/invoices", "Unable to load invoices.", ct);

    public Task<ServiceResult<CheckoutSessionDto>> StartCheckoutAsync(int invoiceId, CancellationToken ct = default) =>
        PostAsync<CheckoutSessionDto>($"api/me/invoices/{invoiceId}/checkout", null, "Unable to start the payment.", ct);

    public Task<ServiceResult<List<PaymentDto>>> GetPaymentsAsync(CancellationToken ct = default) =>
        GetAsync<List<PaymentDto>>("api/me/payments", "Unable to load payments.", ct);

    public Task<ServiceResult<List<CheckInDto>>> GetCheckInsAsync(CancellationToken ct = default) =>
        GetAsync<List<CheckInDto>>("api/me/checkins", "Unable to load check-ins.", ct);

    public Task<ServiceResult<List<ExerciseDto>>> GetExercisesAsync(string? search = null, string? muscleGroup = null, CancellationToken ct = default) =>
        GetAsync<List<ExerciseDto>>(Query("api/me/exercises", ("search", search), ("muscleGroup", muscleGroup)), "Unable to load exercises.", ct);

    public Task<ServiceResult<List<RoutineDto>>> GetRoutinesAsync(bool activeOnly = false, CancellationToken ct = default) =>
        GetAsync<List<RoutineDto>>(Query("api/me/routines", ("activeOnly", activeOnly ? true : null)), "Unable to load routines.", ct);

    public Task<ServiceResult<RoutineDto>> GetRoutineAsync(int routineId, CancellationToken ct = default) =>
        GetAsync<RoutineDto>($"api/me/routines/{routineId}", "Unable to load the routine.", ct);

    public Task<ServiceResult<RoutineDto>> SetRoutineActiveAsync(int routineId, bool active, CancellationToken ct = default) =>
        PutAsync<RoutineDto>(Query($"api/me/routines/{routineId}/active", ("value", active)), null, "Unable to update the routine.", ct);

    public Task<ServiceResult<List<WorkoutLogDto>>> GetWorkoutsAsync(int days = 30, CancellationToken ct = default) =>
        GetAsync<List<WorkoutLogDto>>(Query("api/me/workouts", ("days", days)), "Unable to load workouts.", ct);

    public Task<ServiceResult<WorkoutLogDto>> LogWorkoutAsync(LogWorkoutRequest request, CancellationToken ct = default) =>
        PostAsync<WorkoutLogDto>("api/me/workouts", request, "Unable to save the workout.", ct);

    public Task<ServiceResult<bool>> DeleteWorkoutAsync(int workoutId, CancellationToken ct = default) =>
        DeleteAsync($"api/me/workouts/{workoutId}", "Unable to delete the workout.", ct);

    public Task<ServiceResult<List<BodyMetricDto>>> GetMetricsAsync(CancellationToken ct = default) =>
        GetAsync<List<BodyMetricDto>>("api/me/metrics", "Unable to load measurements.", ct);

    public Task<ServiceResult<BodyMetricDto>> RecordMetricAsync(RecordBodyMetricRequest request, CancellationToken ct = default) =>
        PostAsync<BodyMetricDto>("api/me/metrics", request, "Unable to save the measurement.", ct);

    public Task<ServiceResult<List<FitnessGoalDto>>> GetGoalsAsync(CancellationToken ct = default) =>
        GetAsync<List<FitnessGoalDto>>("api/me/goals", "Unable to load goals.", ct);

    public Task<ServiceResult<FitnessGoalDto>> CreateGoalAsync(SaveFitnessGoalRequest request, CancellationToken ct = default) =>
        PostAsync<FitnessGoalDto>("api/me/goals", request, "Unable to save the goal.", ct);

    public Task<ServiceResult<FitnessGoalDto>> UpdateGoalStatusAsync(int goalId, GoalStatus status, CancellationToken ct = default) =>
        PutAsync<FitnessGoalDto>($"api/me/goals/{goalId}/status", new UpdateGoalStatusRequest { Status = status }, "Unable to update the goal.", ct);

    public Task<ServiceResult<ProgressSummaryDto>> GetProgressAsync(CancellationToken ct = default) =>
        GetAsync<ProgressSummaryDto>("api/me/progress", "Unable to load progress.", ct);
}

/// <summary>Calls made before signing in: joining a gym and resetting a password.</summary>
public sealed class PublicApiClient : ApiClientBase
{
    public PublicApiClient(HttpClient http) : base(http)
    {
    }

    public Task<ServiceResult<GymJoinPreviewDto>> PreviewGymAsync(string joinCode, CancellationToken ct = default) =>
        PostAsync<GymJoinPreviewDto>("api/join/preview", new GymJoinPreviewRequest { JoinCode = joinCode }, "Unable to find that gym.", ct);

    public Task<ServiceResult<GymJoinResultDto>> JoinAsync(GymJoinRequest request, CancellationToken ct = default) =>
        PostAsync<GymJoinResultDto>("api/join", request, "Unable to join the gym.", ct);

    public Task<ServiceResult<bool>> ForgotPasswordAsync(string email, CancellationToken ct = default) =>
        PostNoContentAsync("api/auth/forgot-password", new ForgotPasswordRequest { Email = email }, "Unable to send the reset email.", ct);

    public Task<ServiceResult<bool>> ResetPasswordAsync(string token, string newPassword, CancellationToken ct = default) =>
        PostNoContentAsync("api/auth/reset-password", new ResetPasswordRequest { Token = token, NewPassword = newPassword }, "Unable to reset the password.", ct);
}

/// <summary>Platform operator endpoints beyond tenant onboarding (api/platform/*).</summary>
public sealed class PlatformApiClient : ApiClientBase
{
    public PlatformApiClient(HttpClient http) : base(http)
    {
    }

    public Task<ServiceResult<PlatformOverviewDto>> GetOverviewAsync(CancellationToken ct = default) =>
        GetAsync<PlatformOverviewDto>("api/platform/overview", "Unable to load the overview.", ct);

    public Task<ServiceResult<PlatformTenantDetailDto>> GetTenantAsync(int tenantId, CancellationToken ct = default) =>
        GetAsync<PlatformTenantDetailDto>($"api/platform/tenants/{tenantId}", "Unable to load the tenant.", ct);

    public Task<ServiceResult<PlatformCatalogDto>> GetCatalogAsync(CancellationToken ct = default) =>
        GetAsync<PlatformCatalogDto>("api/platform/catalog", "Unable to load the catalogue.", ct);

    public Task<ServiceResult<List<PlatformAuditEventDto>>> GetAuditAsync(PlatformAuditQuery query, CancellationToken ct = default) =>
        GetAsync<List<PlatformAuditEventDto>>(
            Query("api/platform/audit",
                ("tenantId", query.TenantId),
                ("category", query.Category),
                ("outcome", query.Outcome),
                ("days", query.Days),
                ("take", query.Take)),
            "Unable to load the audit log.",
            ct);
}

/// <summary>A page of the gym's audit log (api/audit).</summary>
public sealed class AuditPageDto
{
    public List<AuditItemDto> Items { get; set; } = [];

    public int TotalCount { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }
}

public sealed class AuditItemDto
{
    public long Id { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public string Category { get; set; } = "";

    public string Action { get; set; } = "";

    public string Outcome { get; set; } = "";

    public string ActorType { get; set; } = "";

    public int? UserId { get; set; }

    public string? AgentId { get; set; }

    public string? ResourceType { get; set; }

    public string? ResourceId { get; set; }

    public string? Reason { get; set; }
}

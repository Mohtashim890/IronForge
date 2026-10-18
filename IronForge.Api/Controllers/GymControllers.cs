using IronForge.Api.Helpers;
using IronForge.Application.Gym.Billing;
using IronForge.Application.Gym.CheckIns;
using IronForge.Application.Gym.Fitness;
using IronForge.Application.Gym.Members;
using IronForge.Application.Gym.Notifications;
using IronForge.Application.Gym.Settings;
using IronForge.Shared.Models.Gym;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronForge.Api.Controllers
{
    // Staff endpoints of the selected gym. Authorization (permission, gym,
    // subscription module) is enforced by the services; controllers only map.

    [ApiController]
    [Route("api/gym")]
    [Authorize]
    public sealed class GymController : ControllerBase
    {
        private readonly IGymSettingsService _settings;
        private readonly IGymDashboardService _dashboard;

        public GymController(IGymSettingsService settings, IGymDashboardService dashboard)
        {
            _settings = settings;
            _dashboard = dashboard;
        }

        [HttpGet("dashboard")]
        public async Task<IActionResult> GetDashboard(CancellationToken cancellationToken) =>
            this.ToActionResult(await _dashboard.GetAsync(cancellationToken));

        [HttpGet("expiring")]
        public async Task<IActionResult> GetExpiring([FromQuery] int days = 7, CancellationToken cancellationToken = default) =>
            this.ToActionResult(await _dashboard.ListExpiringAsync(days, cancellationToken));

        [HttpGet("settings")]
        public async Task<IActionResult> GetSettings(CancellationToken cancellationToken) =>
            this.ToActionResult(await _settings.GetAsync(cancellationToken));

        [HttpPut("settings")]
        public async Task<IActionResult> UpdateSettings(UpdateGymSettingsRequest request, CancellationToken cancellationToken) =>
            this.ToActionResult(await _settings.UpdateAsync(request, cancellationToken));

        [HttpPut("settings/check-in-methods")]
        public async Task<IActionResult> UpdateCheckInMethods(UpdateCheckInMethodsRequest request, CancellationToken cancellationToken) =>
            this.ToActionResult(await _settings.UpdateCheckInMethodsAsync(request, cancellationToken));

        [HttpPost("settings/join-code")]
        public async Task<IActionResult> RegenerateJoinCode(CancellationToken cancellationToken) =>
            this.ToActionResult(await _settings.RegenerateJoinCodeAsync(cancellationToken));

        [HttpPost("settings/stripe/connect")]
        public async Task<IActionResult> ConnectStripe(CancellationToken cancellationToken) =>
            this.ToActionResult(await _settings.StartStripeConnectAsync(cancellationToken));

        [HttpPost("settings/stripe/refresh")]
        public async Task<IActionResult> RefreshStripe(CancellationToken cancellationToken) =>
            this.ToActionResult(await _settings.RefreshStripeStatusAsync(cancellationToken));
    }

    [ApiController]
    [Route("api/gym/plans")]
    [Authorize]
    public sealed class MembershipPlansController : ControllerBase
    {
        private readonly IMembershipPlanService _plans;

        public MembershipPlansController(IMembershipPlanService plans) => _plans = plans;

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] bool includeArchived = false, CancellationToken cancellationToken = default) =>
            this.ToActionResult(await _plans.ListAsync(includeArchived, cancellationToken));

        [HttpPost]
        public async Task<IActionResult> Create(SaveMembershipPlanRequest request, CancellationToken cancellationToken) =>
            this.ToActionResult(await _plans.CreateAsync(request, cancellationToken));

        [HttpPut("{planId:int}")]
        public async Task<IActionResult> Update(int planId, SaveMembershipPlanRequest request, CancellationToken cancellationToken) =>
            this.ToActionResult(await _plans.UpdateAsync(planId, request, cancellationToken));
    }

    [ApiController]
    [Route("api/gym/members")]
    [Authorize]
    public sealed class MembersController : ControllerBase
    {
        private readonly IMemberService _members;
        private readonly IRoutineService _routines;
        private readonly IProgressService _progress;
        private readonly IMemberPhotoService _photos;
        private readonly IMemberPassService _passes;

        public MembersController(
            IMemberService members,
            IRoutineService routines,
            IProgressService progress,
            IMemberPhotoService photos,
            IMemberPassService passes)
        {
            _members = members;
            _routines = routines;
            _progress = progress;
            _photos = photos;
            _passes = passes;
        }

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] MemberQuery query, CancellationToken cancellationToken) =>
            this.ToActionResult(await _members.ListAsync(query, cancellationToken));

        [HttpGet("{memberId:int}")]
        public async Task<IActionResult> Get(int memberId, CancellationToken cancellationToken) =>
            this.ToActionResult(await _members.GetAsync(memberId, cancellationToken));

        [HttpPost]
        public async Task<IActionResult> Create(CreateMemberRequest request, CancellationToken cancellationToken) =>
            this.ToActionResult(await _members.CreateAsync(request, cancellationToken));

        [HttpPut("{memberId:int}")]
        public async Task<IActionResult> Update(int memberId, UpdateMemberRequest request, CancellationToken cancellationToken) =>
            this.ToActionResult(await _members.UpdateAsync(memberId, request, cancellationToken));

        [HttpPost("{memberId:int}/approve")]
        public async Task<IActionResult> Approve(int memberId, ApproveMemberRequest request, CancellationToken cancellationToken) =>
            this.ToActionResult(await _members.ApproveAsync(memberId, request, cancellationToken));

        [HttpPost("{memberId:int}/reject")]
        public async Task<IActionResult> Reject(int memberId, CancellationToken cancellationToken) =>
            this.ToActionResult(await _members.RejectAsync(memberId, cancellationToken));

        [HttpPost("{memberId:int}/freeze")]
        public async Task<IActionResult> Freeze(int memberId, FreezeMemberRequest request, CancellationToken cancellationToken) =>
            this.ToActionResult(await _members.FreezeAsync(memberId, request, cancellationToken));

        [HttpPost("{memberId:int}/unfreeze")]
        public async Task<IActionResult> Unfreeze(int memberId, CancellationToken cancellationToken) =>
            this.ToActionResult(await _members.UnfreezeAsync(memberId, cancellationToken));

        [HttpPost("{memberId:int}/cancel")]
        public async Task<IActionResult> Cancel(int memberId, CancellationToken cancellationToken) =>
            this.ToActionResult(await _members.CancelAsync(memberId, cancellationToken));

        [HttpPost("{memberId:int}/reactivate")]
        public async Task<IActionResult> Reactivate(int memberId, CancellationToken cancellationToken) =>
            this.ToActionResult(await _members.ReactivateAsync(memberId, cancellationToken));

        [HttpPost("{memberId:int}/app-invitation")]
        public async Task<IActionResult> SendAppInvitation(int memberId, CancellationToken cancellationToken) =>
            this.ToActionResult(await _members.SendAppInvitationAsync(memberId, cancellationToken));

        [HttpPut("{memberId:int}/plan")]
        public async Task<IActionResult> ChangePlan(int memberId, ChangeMemberPlanRequest request, CancellationToken cancellationToken) =>
            this.ToActionResult(await _members.ChangePlanAsync(memberId, request, cancellationToken));

        /// <summary>Erases the member's personal data (right to be forgotten).</summary>
        [HttpDelete("{memberId:int}")]
        public async Task<IActionResult> Anonymize(int memberId, CancellationToken cancellationToken)
        {
            var result = await _members.AnonymizeAsync(memberId, cancellationToken);

            return result.Success ? NoContent() : this.ToErrorResult(result);
        }

        [HttpGet("{memberId:int}/routines")]
        public async Task<IActionResult> ListRoutines(int memberId, CancellationToken cancellationToken) =>
            this.ToActionResult(await _routines.ListMemberRoutinesAsync(memberId, cancellationToken));

        [HttpPost("{memberId:int}/routines")]
        public async Task<IActionResult> AssignRoutine(int memberId, SaveRoutineRequest request, CancellationToken cancellationToken) =>
            this.ToActionResult(await _routines.AssignRoutineAsync(memberId, request, cancellationToken));

        [HttpGet("{memberId:int}/progress")]
        public async Task<IActionResult> GetProgress(int memberId, CancellationToken cancellationToken) =>
            this.ToActionResult(await _progress.GetMemberProgressAsync(memberId, cancellationToken));

        /// <summary>204 when the member has no photo.</summary>
        [HttpGet("{memberId:int}/photo")]
        public async Task<IActionResult> GetPhoto(int memberId, CancellationToken cancellationToken)
        {
            var result = await _photos.GetAsync(memberId, cancellationToken);

            return result is { Success: true, Data: null } ? NoContent() : this.ToActionResult(result);
        }

        [HttpPut("{memberId:int}/photo")]
        public async Task<IActionResult> UploadPhoto(int memberId, UploadMemberPhotoRequest request, CancellationToken cancellationToken) =>
            this.ToActionResult(await _photos.UploadAsync(memberId, request, cancellationToken));

        [HttpDelete("{memberId:int}/photo")]
        public async Task<IActionResult> DeletePhoto(int memberId, CancellationToken cancellationToken)
        {
            var result = await _photos.DeleteAsync(memberId, cancellationToken);

            return result.Success ? NoContent() : this.ToErrorResult(result);
        }

        /// <summary>The member's current pass stops working; their app fetches the new one.</summary>
        [HttpPost("{memberId:int}/pass/reset")]
        public async Task<IActionResult> ResetPass(int memberId, CancellationToken cancellationToken)
        {
            var result = await _passes.ResetPassAsync(memberId, cancellationToken);

            return result.Success ? NoContent() : this.ToErrorResult(result);
        }
    }

    [ApiController]
    [Route("api/gym")]
    [Authorize]
    public sealed class GymRoutinesController : ControllerBase
    {
        private readonly IRoutineService _routines;

        public GymRoutinesController(IRoutineService routines) => _routines = routines;

        [HttpGet("exercises")]
        public async Task<IActionResult> ListExercises(
            [FromQuery] string? search,
            [FromQuery] string? muscleGroup,
            CancellationToken cancellationToken) =>
            this.ToActionResult(await _routines.ListExercisesAsync(search, muscleGroup, cancellationToken));

        [HttpPost("exercises")]
        public async Task<IActionResult> CreateExercise(SaveExerciseRequest request, CancellationToken cancellationToken) =>
            this.ToActionResult(await _routines.CreateExerciseAsync(request, cancellationToken));

        [HttpGet("routines/{routineId:int}")]
        public async Task<IActionResult> GetRoutine(int routineId, CancellationToken cancellationToken) =>
            this.ToActionResult(await _routines.GetRoutineAsync(routineId, cancellationToken));

        [HttpPut("routines/{routineId:int}/active")]
        public async Task<IActionResult> SetRoutineActive(int routineId, [FromQuery] bool value, CancellationToken cancellationToken) =>
            this.ToActionResult(await _routines.SetRoutineActiveAsync(routineId, value, cancellationToken));
    }

    [ApiController]
    [Route("api/gym/checkins")]
    [Authorize]
    public sealed class CheckInsController : ControllerBase
    {
        private readonly ICheckInService _checkIns;

        public CheckInsController(ICheckInService checkIns) => _checkIns = checkIns;

        [HttpGet]
        public async Task<IActionResult> ListRecent([FromQuery] int take = 50, CancellationToken cancellationToken = default) =>
            this.ToActionResult(await _checkIns.ListRecentAsync(take, cancellationToken));

        [HttpPost]
        public async Task<IActionResult> CheckIn(CreateCheckInRequest request, CancellationToken cancellationToken) =>
            this.ToActionResult(await _checkIns.CheckInAsync(request.MemberId, cancellationToken));

        /// <summary>Staff scanned a member's pass at the front desk.</summary>
        [HttpPost("scan")]
        public async Task<IActionResult> Scan(ScanPassRequest request, CancellationToken cancellationToken) =>
            this.ToActionResult(await _checkIns.ScanPassAsync(request.Code, cancellationToken));

        [HttpGet("denials")]
        public async Task<IActionResult> ListDenials([FromQuery] int take = 20, CancellationToken cancellationToken = default) =>
            this.ToActionResult(await _checkIns.ListDenialsAsync(take, cancellationToken));

        [HttpGet("attendance")]
        public async Task<IActionResult> Attendance([FromQuery] int days = 30, CancellationToken cancellationToken = default) =>
            this.ToActionResult(await _checkIns.GetAttendanceAsync(days, cancellationToken));
    }

    [ApiController]
    [Route("api/gym/checkin-devices")]
    [Authorize]
    public sealed class CheckInDevicesController : ControllerBase
    {
        private readonly ICheckInDeviceService _devices;

        public CheckInDevicesController(ICheckInDeviceService devices) => _devices = devices;

        [HttpGet]
        public async Task<IActionResult> List(CancellationToken cancellationToken) =>
            this.ToActionResult(await _devices.ListAsync(cancellationToken));

        [HttpPost]
        public async Task<IActionResult> Create(CreateCheckInDeviceRequest request, CancellationToken cancellationToken) =>
            this.ToActionResult(await _devices.CreateAsync(request, cancellationToken));

        [HttpPost("{deviceId:int}/pairing-code")]
        public async Task<IActionResult> NewPairingCode(int deviceId, CancellationToken cancellationToken) =>
            this.ToActionResult(await _devices.NewPairingCodeAsync(deviceId, cancellationToken));

        [HttpPost("{deviceId:int}/revoke")]
        public async Task<IActionResult> Revoke(int deviceId, CancellationToken cancellationToken) =>
            this.ToActionResult(await _devices.RevokeAsync(deviceId, cancellationToken));
    }

    [ApiController]
    [Route("api/gym")]
    [Authorize]
    public sealed class InvoicesController : ControllerBase
    {
        private readonly IBillingService _billing;

        public InvoicesController(IBillingService billing) => _billing = billing;

        [HttpGet("invoices")]
        public async Task<IActionResult> List([FromQuery] InvoiceQuery query, CancellationToken cancellationToken) =>
            this.ToActionResult(await _billing.ListInvoicesAsync(query, cancellationToken));

        [HttpGet("invoices/{invoiceId:int}")]
        public async Task<IActionResult> Get(int invoiceId, CancellationToken cancellationToken) =>
            this.ToActionResult(await _billing.GetInvoiceAsync(invoiceId, cancellationToken));

        [HttpPost("invoices/{invoiceId:int}/payments")]
        public async Task<IActionResult> RecordPayment(int invoiceId, RecordPaymentRequest request, CancellationToken cancellationToken) =>
            this.ToActionResult(await _billing.RecordPaymentAsync(invoiceId, request, cancellationToken));

        [HttpPost("invoices/{invoiceId:int}/void")]
        public async Task<IActionResult> Void(int invoiceId, CancellationToken cancellationToken) =>
            this.ToActionResult(await _billing.VoidInvoiceAsync(invoiceId, cancellationToken));

        [HttpPost("invoices/{invoiceId:int}/reminders")]
        public async Task<IActionResult> SendReminder(int invoiceId, CancellationToken cancellationToken) =>
            this.ToActionResult(await _billing.SendReminderAsync(invoiceId, cancellationToken));

        [HttpGet("payments")]
        public async Task<IActionResult> ListPayments(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 25,
            CancellationToken cancellationToken = default) =>
            this.ToActionResult(await _billing.ListPaymentsAsync(page, pageSize, cancellationToken));

        [HttpGet("revenue")]
        public async Task<IActionResult> Revenue([FromQuery] int months = 6, CancellationToken cancellationToken = default) =>
            this.ToActionResult(await _billing.GetRevenueByMonthAsync(months, cancellationToken));
    }

    [ApiController]
    [Route("api/gym/announcements")]
    [Authorize]
    public sealed class AnnouncementsController : ControllerBase
    {
        private readonly IAnnouncementService _announcements;

        public AnnouncementsController(IAnnouncementService announcements) => _announcements = announcements;

        [HttpGet]
        public async Task<IActionResult> List(CancellationToken cancellationToken) =>
            this.ToActionResult(await _announcements.ListAsync(cancellationToken));

        [HttpPost]
        public async Task<IActionResult> Send(CreateAnnouncementRequest request, CancellationToken cancellationToken) =>
            this.ToActionResult(await _announcements.SendAsync(request, cancellationToken));
    }

    /// <summary>The signed-in user's in-app notifications in the selected gym.</summary>
    [ApiController]
    [Route("api/notifications")]
    [Authorize]
    public sealed class NotificationsController : ControllerBase
    {
        private readonly INotificationService _notifications;

        public NotificationsController(INotificationService notifications) => _notifications = notifications;

        [HttpGet]
        public async Task<IActionResult> List(
            [FromQuery] bool unreadOnly = false,
            [FromQuery] int take = 50,
            CancellationToken cancellationToken = default) =>
            this.ToActionResult(await _notifications.ListMineAsync(unreadOnly, take, cancellationToken));

        [HttpGet("unread-count")]
        public async Task<IActionResult> UnreadCount(CancellationToken cancellationToken) =>
            this.ToActionResult(await _notifications.GetUnreadCountAsync(cancellationToken));

        [HttpPost("{notificationId:long}/read")]
        public async Task<IActionResult> MarkRead(long notificationId, CancellationToken cancellationToken) =>
            this.ToActionResult(await _notifications.MarkReadAsync(notificationId, cancellationToken));

        [HttpPost("read-all")]
        public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken) =>
            this.ToActionResult(await _notifications.MarkAllReadAsync(cancellationToken));
    }
}

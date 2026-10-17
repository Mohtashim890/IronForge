using IronForge.Api.Helpers;
using IronForge.Application.Gym.Billing;
using IronForge.Application.Gym.Fitness;
using IronForge.Application.Gym.Members;
using IronForge.Shared.Models.Gym;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronForge.Api.Controllers
{
    /// <summary>
    /// Self-service for the signed-in gym member (mobile app). Every endpoint
    /// works on the caller's own profile; no member id is ever accepted.
    /// </summary>
    [ApiController]
    [Route("api/me")]
    [Authorize]
    public sealed class MeController : ControllerBase
    {
        private readonly IMemberService _members;
        private readonly IMembershipPlanService _plans;
        private readonly IBillingService _billing;
        private readonly ICheckInService _checkIns;
        private readonly IRoutineService _routines;
        private readonly IProgressService _progress;

        public MeController(
            IMemberService members,
            IMembershipPlanService plans,
            IBillingService billing,
            ICheckInService checkIns,
            IRoutineService routines,
            IProgressService progress)
        {
            _members = members;
            _plans = plans;
            _billing = billing;
            _checkIns = checkIns;
            _routines = routines;
            _progress = progress;
        }

        [HttpGet("membership")]
        public async Task<IActionResult> GetMembership(CancellationToken cancellationToken) =>
            this.ToActionResult(await _members.GetMyMembershipAsync(cancellationToken));

        [HttpPut("profile")]
        public async Task<IActionResult> UpdateProfile(UpdateMyProfileRequest request, CancellationToken cancellationToken) =>
            this.ToActionResult(await _members.UpdateMyProfileAsync(request, cancellationToken));

        [HttpGet("plans")]
        public async Task<IActionResult> ListPlans(CancellationToken cancellationToken) =>
            this.ToActionResult(await _plans.ListAsync(includeArchived: false, cancellationToken));

        [HttpGet("invoices")]
        public async Task<IActionResult> ListInvoices(CancellationToken cancellationToken) =>
            this.ToActionResult(await _billing.ListMyInvoicesAsync(cancellationToken));

        [HttpPost("invoices/{invoiceId:int}/checkout")]
        public async Task<IActionResult> Checkout(int invoiceId, CancellationToken cancellationToken) =>
            this.ToActionResult(await _billing.StartMyCheckoutAsync(invoiceId, cancellationToken));

        [HttpGet("payments")]
        public async Task<IActionResult> ListPayments(CancellationToken cancellationToken) =>
            this.ToActionResult(await _billing.ListMyPaymentsAsync(cancellationToken));

        [HttpGet("checkins")]
        public async Task<IActionResult> ListCheckIns(CancellationToken cancellationToken) =>
            this.ToActionResult(await _checkIns.ListMineAsync(cancellationToken));

        [HttpGet("exercises")]
        public async Task<IActionResult> ListExercises(
            [FromQuery] string? search,
            [FromQuery] string? muscleGroup,
            CancellationToken cancellationToken) =>
            this.ToActionResult(await _routines.ListExercisesAsync(search, muscleGroup, cancellationToken));

        [HttpGet("routines")]
        public async Task<IActionResult> ListRoutines([FromQuery] bool activeOnly = false, CancellationToken cancellationToken = default) =>
            this.ToActionResult(await _routines.ListMyRoutinesAsync(activeOnly, cancellationToken));

        [HttpGet("routines/{routineId:int}")]
        public async Task<IActionResult> GetRoutine(int routineId, CancellationToken cancellationToken) =>
            this.ToActionResult(await _routines.GetMyRoutineAsync(routineId, cancellationToken));

        [HttpPost("routines")]
        public async Task<IActionResult> CreateRoutine(SaveRoutineRequest request, CancellationToken cancellationToken) =>
            this.ToActionResult(await _routines.CreateMyRoutineAsync(request, RoutineSource.Self, cancellationToken));

        [HttpPut("routines/{routineId:int}/active")]
        public async Task<IActionResult> SetRoutineActive(int routineId, [FromQuery] bool value, CancellationToken cancellationToken) =>
            this.ToActionResult(await _routines.SetMyRoutineActiveAsync(routineId, value, cancellationToken));

        [HttpGet("workouts")]
        public async Task<IActionResult> ListWorkouts([FromQuery] int days = 30, CancellationToken cancellationToken = default) =>
            this.ToActionResult(await _progress.ListMyWorkoutsAsync(days, cancellationToken));

        [HttpPost("workouts")]
        public async Task<IActionResult> LogWorkout(LogWorkoutRequest request, CancellationToken cancellationToken) =>
            this.ToActionResult(await _progress.LogMyWorkoutAsync(request, cancellationToken));

        [HttpDelete("workouts/{workoutId:int}")]
        public async Task<IActionResult> DeleteWorkout(int workoutId, CancellationToken cancellationToken)
        {
            var result = await _progress.DeleteMyWorkoutAsync(workoutId, cancellationToken);

            return result.Success ? NoContent() : this.ToErrorResult(result);
        }

        [HttpGet("metrics")]
        public async Task<IActionResult> ListMetrics(CancellationToken cancellationToken) =>
            this.ToActionResult(await _progress.ListMyMetricsAsync(cancellationToken));

        [HttpPost("metrics")]
        public async Task<IActionResult> RecordMetric(RecordBodyMetricRequest request, CancellationToken cancellationToken) =>
            this.ToActionResult(await _progress.RecordMyMetricAsync(request, cancellationToken));

        [HttpGet("goals")]
        public async Task<IActionResult> ListGoals(CancellationToken cancellationToken) =>
            this.ToActionResult(await _progress.ListMyGoalsAsync(cancellationToken));

        [HttpPost("goals")]
        public async Task<IActionResult> CreateGoal(SaveFitnessGoalRequest request, CancellationToken cancellationToken) =>
            this.ToActionResult(await _progress.CreateMyGoalAsync(request, cancellationToken));

        [HttpPut("goals/{goalId:int}/status")]
        public async Task<IActionResult> UpdateGoalStatus(int goalId, UpdateGoalStatusRequest request, CancellationToken cancellationToken) =>
            this.ToActionResult(await _progress.UpdateMyGoalStatusAsync(goalId, request.Status, cancellationToken));

        [HttpGet("progress")]
        public async Task<IActionResult> GetProgress(CancellationToken cancellationToken) =>
            this.ToActionResult(await _progress.GetMyProgressAsync(cancellationToken));
    }
}

using System.ComponentModel;
using IronForge.Application.Agents.Governance;
using IronForge.Application.Agents.Tools;
using IronForge.Application.Gym.Billing;
using IronForge.Application.Gym.Fitness;
using IronForge.Application.Gym.Members;
using IronForge.Shared.Authorization;
using IronForge.Shared.Models.Gym;
using IronForge.Shared.Subscriptions;

namespace IronForge.Application.Gym.AgentTools;

// Tools of the Fitness Coach. Every one acts on the signed-in member's own
// data: the services resolve the member from the execution context and never
// accept a member id, so the coach cannot reach another member.

public sealed class GetMyProfileAndGoalsAgentTool : AgentTool
{
    private readonly IMemberService _members;
    private readonly IProgressService _progress;

    public GetMyProfileAndGoalsAgentTool(IMemberService members, IProgressService progress)
    {
        _members = members;
        _progress = progress;
    }

    public override AgentToolDescriptor Descriptor { get; } = new()
    {
        Name = "get_my_profile_and_goals",
        Description =
            "Gets the member's profile (name, age details, height, membership status, plan, " +
            "workouts this week, weekly streak) and their fitness goals.",
        RequiredPermission = Permissions.GymSelfRead,
        Module = PlatformModules.GymCoaching
    };

    protected override Delegate Handler => ExecuteAsync;

    private async Task<object> ExecuteAsync()
    {
        var membership = await _members.GetMyMembershipAsync();

        if (!membership.Success)
            return GymToolResult.From(membership);

        var goals = await _progress.ListMyGoalsAsync();
        var m = membership.Data!;

        return GymToolResult.Ok(new
        {
            m.FullName,
            m.DateOfBirth,
            m.Gender,
            m.HeightCm,
            m.Status,
            Plan = m.Plan?.Name,
            m.WorkoutsThisWeek,
            m.WeeklyStreak,
            m.CheckInsLast30Days,
            Goals = goals.Data ?? []
        });
    }
}

public sealed class GetMyRoutinesAgentTool : AgentTool
{
    private readonly IRoutineService _routines;

    public GetMyRoutinesAgentTool(IRoutineService routines) => _routines = routines;

    public override AgentToolDescriptor Descriptor { get; } = new()
    {
        Name = "get_my_routines",
        Description =
            "Lists the member's workout routines with their days and exercises " +
            "(sets, reps, target weight). Includes routines from trainers and from the coach.",
        RequiredPermission = Permissions.GymSelfRead,
        Module = PlatformModules.GymCoaching
    };

    protected override Delegate Handler => ExecuteAsync;

    private async Task<object> ExecuteAsync(
        [Description("True to return only active routines. Default true.")]
        bool activeOnly = true) =>
        GymToolResult.From(await _routines.ListMyRoutinesAsync(activeOnly));
}

public sealed class GetExerciseLibraryAgentTool : AgentTool
{
    private readonly IRoutineService _routines;

    public GetExerciseLibraryAgentTool(IRoutineService routines) => _routines = routines;

    public override AgentToolDescriptor Descriptor { get; } = new()
    {
        Name = "get_exercise_library",
        Description =
            "Searches the gym's exercise library. Use it to find exercise ids before " +
            "logging a workout or creating a routine.",
        RequiredPermission = Permissions.GymSelfRead,
        Module = PlatformModules.GymCoaching
    };

    protected override Delegate Handler => ExecuteAsync;

    private async Task<object> ExecuteAsync(
        [Description("Optional part of the exercise name, e.g. 'squat'.")]
        string? search = null,
        [Description("Optional muscle group: Chest, Back, Legs, Shoulders, Arms, Core, Glutes or Cardio.")]
        string? muscleGroup = null)
    {
        var result = await _routines.ListExercisesAsync(search, muscleGroup);

        return result.Success
            ? GymToolResult.Ok(result.Data!.Select(x => new { x.Id, x.Name, x.MuscleGroup, x.Equipment }))
            : GymToolResult.From(result);
    }
}

public sealed class GetMyWorkoutHistoryAgentTool : AgentTool
{
    private readonly IProgressService _progress;

    public GetMyWorkoutHistoryAgentTool(IProgressService progress) => _progress = progress;

    public override AgentToolDescriptor Descriptor { get; } = new()
    {
        Name = "get_my_workout_history",
        Description = "Lists the member's logged workouts (newest first) with every set: exercise, reps and weight.",
        RequiredPermission = Permissions.GymSelfRead,
        Module = PlatformModules.GymCoaching
    };

    protected override Delegate Handler => ExecuteAsync;

    private async Task<object> ExecuteAsync(
        [Description("How many days back, 1 to 365. Default 30.")]
        int days = 30) =>
        GymToolResult.From(await _progress.ListMyWorkoutsAsync(days));
}

public sealed class GetMyProgressAgentTool : AgentTool
{
    private readonly IProgressService _progress;

    public GetMyProgressAgentTool(IProgressService progress) => _progress = progress;

    public override AgentToolDescriptor Descriptor { get; } = new()
    {
        Name = "get_my_progress",
        Description =
            "Summarises the member's progress: weight and body-fat trend, workouts in the last " +
            "30 days, weekly streak, training volume per week for 12 weeks, and active goals.",
        RequiredPermission = Permissions.GymSelfRead,
        Module = PlatformModules.GymCoaching
    };

    protected override Delegate Handler => ExecuteAsync;

    private async Task<object> ExecuteAsync()
    {
        var result = await _progress.GetMyProgressAsync();

        if (!result.Success)
            return GymToolResult.From(result);

        var p = result.Data!;

        // The latest measurements are enough for coaching; keep the payload small.
        return GymToolResult.Ok(new
        {
            p.CurrentWeightKg,
            p.StartWeightKg,
            p.CurrentBodyFatPercent,
            p.WorkoutsLast30Days,
            p.WorkoutsThisWeek,
            p.WeeklyStreak,
            p.VolumeLast30DaysKg,
            p.LastWorkoutOn,
            RecentMeasurements = p.Metrics.Take(10),
            p.WeeklyVolume,
            p.ActiveGoals
        });
    }
}

public sealed class LogWorkoutAgentTool : AgentTool
{
    private readonly IProgressService _progress;

    public LogWorkoutAgentTool(IProgressService progress) => _progress = progress;

    public override AgentToolDescriptor Descriptor { get; } = new()
    {
        Name = "log_workout",
        Description =
            "Logs a workout the member completed. Each set needs an exercise id from the " +
            "library or a routine, the reps done and optionally the weight in kg.",
        RequiredPermission = Permissions.GymSelfWorkoutsWrite,
        Module = PlatformModules.GymCoaching
    };

    protected override Delegate Handler => ExecuteAsync;

    private async Task<object> ExecuteAsync(
        [Description("The sets performed, in order.")]
        List<LogWorkoutSetRequest> sets,
        [Description("Date of the workout (yyyy-MM-dd); today when omitted.")]
        DateOnly? performedOn = null,
        [Description("Optional routine day id when the workout followed a routine day.")]
        int? routineDayId = null,
        [Description("Optional duration in minutes.")]
        int? durationMinutes = null,
        [Description("Optional notes, e.g. how it felt.")]
        string? notes = null) =>
        GymToolResult.From(await _progress.LogMyWorkoutAsync(new LogWorkoutRequest
        {
            Sets = sets ?? [],
            PerformedOn = performedOn,
            RoutineDayId = routineDayId,
            DurationMinutes = durationMinutes,
            Notes = notes
        }));
}

public sealed class RecordBodyMetricAgentTool : AgentTool
{
    private readonly IProgressService _progress;

    public RecordBodyMetricAgentTool(IProgressService progress) => _progress = progress;

    public override AgentToolDescriptor Descriptor { get; } = new()
    {
        Name = "record_body_metric",
        Description = "Records the member's body weight and/or body-fat percentage for today.",
        RequiredPermission = Permissions.GymSelfWorkoutsWrite,
        Module = PlatformModules.GymCoaching
    };

    protected override Delegate Handler => ExecuteAsync;

    private async Task<object> ExecuteAsync(
        [Description("Body weight in kg.")]
        decimal? weightKg = null,
        [Description("Body-fat percentage.")]
        decimal? bodyFatPercent = null,
        [Description("Optional note.")]
        string? notes = null) =>
        GymToolResult.From(await _progress.RecordMyMetricAsync(new RecordBodyMetricRequest
        {
            WeightKg = weightKg,
            BodyFatPercent = bodyFatPercent,
            Notes = notes
        }));
}

public sealed class SetFitnessGoalAgentTool : AgentTool
{
    private readonly IProgressService _progress;

    public SetFitnessGoalAgentTool(IProgressService progress) => _progress = progress;

    public override AgentToolDescriptor Descriptor { get; } = new()
    {
        Name = "set_fitness_goal",
        Description = "Adds a fitness goal for the member, e.g. 'Reach 75 kg by June'. Confirm the goal with the member first.",
        RequiredPermission = Permissions.GymSelfWorkoutsWrite,
        Module = PlatformModules.GymCoaching
    };

    protected override Delegate Handler => ExecuteAsync;

    private async Task<object> ExecuteAsync(
        [Description("LoseWeight, BuildMuscle, Endurance, Strength or General.")]
        string type,
        [Description("The goal in the member's words.")]
        string description,
        [Description("Optional numeric target, e.g. 75.")]
        decimal? targetValue = null,
        [Description("Unit of the target, e.g. kg, km, reps.")]
        string? targetUnit = null,
        [Description("Optional target date (yyyy-MM-dd).")]
        DateOnly? targetDate = null) =>
        GymToolResult.From(await _progress.CreateMyGoalAsync(new SaveFitnessGoalRequest
        {
            Type = Enum.TryParse<GoalType>(type, ignoreCase: true, out var parsed) ? parsed : GoalType.General,
            Description = description,
            TargetValue = targetValue,
            TargetUnit = targetUnit,
            TargetDate = targetDate
        }));
}

public sealed class CreateRoutineForMeAgentTool : AgentTool
{
    private readonly IRoutineService _routines;

    public CreateRoutineForMeAgentTool(IRoutineService routines) => _routines = routines;

    public override AgentToolDescriptor Descriptor { get; } = new()
    {
        Name = "create_routine_for_me",
        Description =
            "Saves a workout routine you designed for the member. Use only exercise ids from " +
            "get_exercise_library. Describe the routine and get the member's agreement before saving it.",
        RequiredPermission = Permissions.GymSelfWorkoutsWrite,
        Module = PlatformModules.GymCoaching
    };

    protected override Delegate Handler => ExecuteAsync;

    private async Task<object> ExecuteAsync(
        [Description("Routine name, e.g. 'Beginner full body'.")]
        string name,
        [Description("1 to 7 training days, each with 1 to 15 exercises.")]
        List<SaveRoutineDayRequest> days,
        [Description("What the routine is for.")]
        string? goal = null,
        [Description("Optional guidance for the member.")]
        string? notes = null) =>
        GymToolResult.From(await _routines.CreateMyRoutineAsync(
            new SaveRoutineRequest { Name = name, Goal = goal, Notes = notes, Days = days ?? [] },
            RoutineSource.Ai));
}

public sealed class GetMyMembershipAndInvoicesAgentTool : AgentTool
{
    private readonly IMemberService _members;
    private readonly IBillingService _billing;

    public GetMyMembershipAndInvoicesAgentTool(IMemberService members, IBillingService billing)
    {
        _members = members;
        _billing = billing;
    }

    public override AgentToolDescriptor Descriptor { get; } = new()
    {
        Name = "get_my_membership_and_invoices",
        Description =
            "Gets the member's plan, paid-through date, next billing date and recent invoices. " +
            "Read-only: payments are made in the app's Invoices page.",
        RequiredPermission = Permissions.GymSelfRead,
        Module = PlatformModules.GymBilling
    };

    protected override Delegate Handler => ExecuteAsync;

    private async Task<object> ExecuteAsync()
    {
        var membership = await _members.GetMyMembershipAsync();

        if (!membership.Success)
            return GymToolResult.From(membership);

        var invoices = await _billing.ListMyInvoicesAsync();
        var m = membership.Data!;

        return GymToolResult.Ok(new
        {
            m.Status,
            Plan = m.Plan is null ? null : new { m.Plan.Name, m.Plan.Price, m.Plan.Currency, m.Plan.BillingInterval },
            m.PaidThrough,
            m.NextBillingDate,
            m.FrozenUntil,
            m.OutstandingAmount,
            m.Currency,
            Invoices = (invoices.Data ?? []).Take(6).Select(x => new
            {
                x.Number,
                x.Description,
                x.Amount,
                x.Currency,
                x.DueDate,
                x.Status,
                x.DaysOverdue
            })
        });
    }
}

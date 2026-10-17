using IronForge.Application.Commons;
using IronForge.Application.Entities;
using IronForge.Application.Gym.Billing;
using IronForge.Application.Gym.Common;
using IronForge.Application.Persistence;
using IronForge.Shared.Authorization;
using IronForge.Shared.Models.Gym;
using IronForge.Shared.Subscriptions;

namespace IronForge.Application.Gym.Fitness;

/// <summary>Workout logging, body metrics, goals and progress, for the signed-in member.</summary>
public interface IProgressService
{
    Task<ServiceResult<WorkoutLogDto>> LogMyWorkoutAsync(
        LogWorkoutRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<List<WorkoutLogDto>>> ListMyWorkoutsAsync(
        int days,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<bool>> DeleteMyWorkoutAsync(
        int workoutId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<BodyMetricDto>> RecordMyMetricAsync(
        RecordBodyMetricRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<List<BodyMetricDto>>> ListMyMetricsAsync(
        CancellationToken cancellationToken = default);

    Task<ServiceResult<List<FitnessGoalDto>>> ListMyGoalsAsync(
        CancellationToken cancellationToken = default);

    Task<ServiceResult<FitnessGoalDto>> CreateMyGoalAsync(
        SaveFitnessGoalRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<FitnessGoalDto>> UpdateMyGoalStatusAsync(
        int goalId,
        GoalStatus status,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<ProgressSummaryDto>> GetMyProgressAsync(
        CancellationToken cancellationToken = default);

    /// <summary>A trainer's view of a member's progress.</summary>
    Task<ServiceResult<ProgressSummaryDto>> GetMemberProgressAsync(
        int memberId,
        CancellationToken cancellationToken = default);
}

public sealed class ProgressService : IProgressService
{
    private const int MaximumActiveGoals = 5;

    private readonly IGymAccessService _access;
    private readonly IRepository<WorkoutLog> _workouts;
    private readonly IRepository<WorkoutRoutine> _routines;
    private readonly IRepository<RoutineDay> _routineDays;
    private readonly IRepository<Exercise> _exercises;
    private readonly IRepository<BodyMetric> _metrics;
    private readonly IRepository<FitnessGoal> _goals;
    private readonly IRepository<MemberProfile> _members;
    private readonly IBillingEngine _gyms;
    private readonly TimeProvider _clock;

    public ProgressService(
        IGymAccessService access,
        IRepository<WorkoutLog> workouts,
        IRepository<WorkoutRoutine> routines,
        IRepository<RoutineDay> routineDays,
        IRepository<Exercise> exercises,
        IRepository<BodyMetric> metrics,
        IRepository<FitnessGoal> goals,
        IRepository<MemberProfile> members,
        IBillingEngine gyms,
        TimeProvider clock)
    {
        _access = access;
        _workouts = workouts;
        _routines = routines;
        _routineDays = routineDays;
        _exercises = exercises;
        _metrics = metrics;
        _goals = goals;
        _members = members;
        _gyms = gyms;
        _clock = clock;
    }

    public async Task<ServiceResult<WorkoutLogDto>> LogMyWorkoutAsync(
        LogWorkoutRequest request,
        CancellationToken cancellationToken = default)
    {
        var current = await CurrentMemberAsync(Permissions.GymSelfWorkoutsWrite, cancellationToken);

        if (!current.Success)
        {
            return GymResults.From<WorkoutLogDto, MemberProfile>(current);
        }

        var member = current.Data!;
        var today = await TodayAsync(member.TenantId, cancellationToken);
        var performedOn = request.PerformedOn ?? today;

        if (performedOn > today || performedOn < today.AddDays(-60))
        {
            return GymResults.Invalid<WorkoutLogDto>("Workouts can be logged for today or the last 60 days.");
        }

        if (request.Sets.Count is 0 or > 100)
        {
            return GymResults.Invalid<WorkoutLogDto>("Log between 1 and 100 sets.");
        }

        var exerciseIds = request.Sets.Select(x => x.ExerciseId).Distinct().ToList();

        var known = (await _exercises.QueryAsync(
                q => q.Where(x => exerciseIds.Contains(x.Id)).Select(x => x.Id),
                cancellationToken))
            .ToHashSet();

        var unknown = exerciseIds.Where(id => !known.Contains(id)).ToList();

        if (unknown.Count > 0)
        {
            return GymResults.Invalid<WorkoutLogDto>(
                $"Unknown exercise id(s): {string.Join(", ", unknown)}. Use ids from the exercise library.");
        }

        int? routineId = null;

        if (request.RoutineDayId is { } dayId)
        {
            var day = await _routineDays.FirstOrDefaultAsync(x => x.Id == dayId, cancellationToken);

            var ownsRoutine = day is not null && await _routines.AnyAsync(
                x => x.Id == day.WorkoutRoutineId && x.MemberProfileId == member.Id,
                cancellationToken);

            if (!ownsRoutine)
            {
                return GymResults.Invalid<WorkoutLogDto>("That routine day is not one of your routines.");
            }

            routineId = day!.WorkoutRoutineId;
        }

        // Sets are numbered per exercise in the order they were done.
        var setNumbers = new Dictionary<int, int>();

        var workout = new WorkoutLog
        {
            TenantId = member.TenantId,
            MemberProfileId = member.Id,
            WorkoutRoutineId = routineId,
            RoutineDayId = request.RoutineDayId,
            PerformedOn = performedOn,
            DurationMinutes = request.DurationMinutes,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            CreatedAtUtc = _clock.GetUtcNow().UtcDateTime,
            Sets = request.Sets
                .Select(set =>
                {
                    var number = setNumbers.GetValueOrDefault(set.ExerciseId) + 1;
                    setNumbers[set.ExerciseId] = number;

                    return new WorkoutSet
                    {
                        TenantId = member.TenantId,
                        ExerciseId = set.ExerciseId,
                        SetNumber = number,
                        Reps = set.Reps,
                        WeightKg = set.WeightKg
                    };
                })
                .ToList()
        };

        _workouts.Add(workout);

        await _workouts.SaveChangesAsync(cancellationToken);

        var dto = (await LoadWorkoutsAsync(q => q.Where(x => x.Id == workout.Id), cancellationToken)).Single();

        return ServiceResult<WorkoutLogDto>.Ok(dto);
    }

    public async Task<ServiceResult<List<WorkoutLogDto>>> ListMyWorkoutsAsync(
        int days,
        CancellationToken cancellationToken = default)
    {
        var current = await CurrentMemberAsync(Permissions.GymSelfRead, cancellationToken);

        if (!current.Success)
        {
            return GymResults.From<List<WorkoutLogDto>, MemberProfile>(current);
        }

        var member = current.Data!;
        var since = (await TodayAsync(member.TenantId, cancellationToken)).AddDays(-Math.Clamp(days, 1, 365));

        var workouts = await LoadWorkoutsAsync(
            q => q.Where(x => x.MemberProfileId == member.Id && x.PerformedOn >= since),
            cancellationToken);

        return ServiceResult<List<WorkoutLogDto>>.Ok(workouts);
    }

    public async Task<ServiceResult<bool>> DeleteMyWorkoutAsync(
        int workoutId,
        CancellationToken cancellationToken = default)
    {
        var current = await CurrentMemberAsync(Permissions.GymSelfWorkoutsWrite, cancellationToken);

        if (!current.Success)
        {
            return GymResults.From<bool, MemberProfile>(current);
        }

        var memberId = current.Data!.Id;

        var workout = await _workouts.FirstOrDefaultTrackedAsync(
            x => x.Id == workoutId && x.MemberProfileId == memberId,
            cancellationToken);

        if (workout is null)
        {
            return GymResults.NotFound<bool>("Workout");
        }

        _workouts.Remove(workout);

        await _workouts.SaveChangesAsync(cancellationToken);

        return ServiceResult<bool>.Ok(true);
    }

    public async Task<ServiceResult<BodyMetricDto>> RecordMyMetricAsync(
        RecordBodyMetricRequest request,
        CancellationToken cancellationToken = default)
    {
        var current = await CurrentMemberAsync(Permissions.GymSelfWorkoutsWrite, cancellationToken);

        if (!current.Success)
        {
            return GymResults.From<BodyMetricDto, MemberProfile>(current);
        }

        if (request.WeightKg is null && request.BodyFatPercent is null)
        {
            return GymResults.Invalid<BodyMetricDto>("Enter a weight, a body-fat percentage, or both.");
        }

        var member = current.Data!;
        var today = await TodayAsync(member.TenantId, cancellationToken);
        var recordedOn = request.RecordedOn ?? today;

        if (recordedOn > today || recordedOn < today.AddDays(-365))
        {
            return GymResults.Invalid<BodyMetricDto>("Measurements can be recorded for today or the last year.");
        }

        // One measurement per day: a second entry on the same day replaces the first.
        var metric = await _metrics.FirstOrDefaultTrackedAsync(
            x => x.MemberProfileId == member.Id && x.RecordedOn == recordedOn,
            cancellationToken);

        if (metric is null)
        {
            metric = new BodyMetric
            {
                TenantId = member.TenantId,
                MemberProfileId = member.Id,
                RecordedOn = recordedOn
            };

            _metrics.Add(metric);
        }

        metric.WeightKg = request.WeightKg ?? metric.WeightKg;
        metric.BodyFatPercent = request.BodyFatPercent ?? metric.BodyFatPercent;
        metric.Notes = string.IsNullOrWhiteSpace(request.Notes) ? metric.Notes : request.Notes.Trim();

        await _metrics.SaveChangesAsync(cancellationToken);

        return ServiceResult<BodyMetricDto>.Ok(MapMetric(metric));
    }

    public async Task<ServiceResult<List<BodyMetricDto>>> ListMyMetricsAsync(
        CancellationToken cancellationToken = default)
    {
        var current = await CurrentMemberAsync(Permissions.GymSelfRead, cancellationToken);

        if (!current.Success)
        {
            return GymResults.From<List<BodyMetricDto>, MemberProfile>(current);
        }

        return ServiceResult<List<BodyMetricDto>>.Ok(
            await LoadMetricsAsync(current.Data!.Id, cancellationToken));
    }

    public async Task<ServiceResult<List<FitnessGoalDto>>> ListMyGoalsAsync(
        CancellationToken cancellationToken = default)
    {
        var current = await CurrentMemberAsync(Permissions.GymSelfRead, cancellationToken);

        if (!current.Success)
        {
            return GymResults.From<List<FitnessGoalDto>, MemberProfile>(current);
        }

        return ServiceResult<List<FitnessGoalDto>>.Ok(
            await LoadGoalsAsync(current.Data!.Id, activeOnly: false, cancellationToken));
    }

    public async Task<ServiceResult<FitnessGoalDto>> CreateMyGoalAsync(
        SaveFitnessGoalRequest request,
        CancellationToken cancellationToken = default)
    {
        var current = await CurrentMemberAsync(Permissions.GymSelfWorkoutsWrite, cancellationToken);

        if (!current.Success)
        {
            return GymResults.From<FitnessGoalDto, MemberProfile>(current);
        }

        var member = current.Data!;

        if (await _goals.CountAsync(
                x => x.MemberProfileId == member.Id && x.Status == GoalStatus.Active,
                cancellationToken) >= MaximumActiveGoals)
        {
            return GymResults.Conflict<FitnessGoalDto>(
                $"You already have {MaximumActiveGoals} active goals. Complete or drop one first.");
        }

        var today = await TodayAsync(member.TenantId, cancellationToken);

        if (request.TargetDate is { } date && (date <= today || date > today.AddYears(3)))
        {
            return GymResults.Invalid<FitnessGoalDto>("The target date must be in the next three years.");
        }

        var goal = new FitnessGoal
        {
            TenantId = member.TenantId,
            MemberProfileId = member.Id,
            Type = request.Type,
            Description = request.Description.Trim(),
            TargetValue = request.TargetValue,
            TargetUnit = string.IsNullOrWhiteSpace(request.TargetUnit) ? null : request.TargetUnit.Trim(),
            TargetDate = request.TargetDate,
            Status = GoalStatus.Active,
            CreatedAtUtc = _clock.GetUtcNow().UtcDateTime
        };

        _goals.Add(goal);

        await _goals.SaveChangesAsync(cancellationToken);

        return ServiceResult<FitnessGoalDto>.Ok(MapGoal(goal));
    }

    public async Task<ServiceResult<FitnessGoalDto>> UpdateMyGoalStatusAsync(
        int goalId,
        GoalStatus status,
        CancellationToken cancellationToken = default)
    {
        var current = await CurrentMemberAsync(Permissions.GymSelfWorkoutsWrite, cancellationToken);

        if (!current.Success)
        {
            return GymResults.From<FitnessGoalDto, MemberProfile>(current);
        }

        var memberId = current.Data!.Id;

        var goal = await _goals.FirstOrDefaultTrackedAsync(
            x => x.Id == goalId && x.MemberProfileId == memberId,
            cancellationToken);

        if (goal is null)
        {
            return GymResults.NotFound<FitnessGoalDto>("Goal");
        }

        goal.Status = status;
        goal.CompletedAtUtc = status == GoalStatus.Active ? null : _clock.GetUtcNow().UtcDateTime;

        await _goals.SaveChangesAsync(cancellationToken);

        return ServiceResult<FitnessGoalDto>.Ok(MapGoal(goal));
    }

    public async Task<ServiceResult<ProgressSummaryDto>> GetMyProgressAsync(
        CancellationToken cancellationToken = default)
    {
        var current = await CurrentMemberAsync(Permissions.GymSelfRead, cancellationToken);

        if (!current.Success)
        {
            return GymResults.From<ProgressSummaryDto, MemberProfile>(current);
        }

        return ServiceResult<ProgressSummaryDto>.Ok(
            await BuildProgressAsync(current.Data!, cancellationToken));
    }

    public async Task<ServiceResult<ProgressSummaryDto>> GetMemberProgressAsync(
        int memberId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _access.AuthorizeAsync(
            Permissions.GymMembersRead,
            PlatformModules.GymCoaching,
            cancellationToken);

        if (!authorization.Allowed)
        {
            return GymResults.Forbidden<ProgressSummaryDto>(authorization);
        }

        var member = await _members.FirstOrDefaultAsync(
            x => x.Id == memberId && x.AnonymizedAtUtc == null,
            cancellationToken);

        return member is null
            ? GymResults.NotFound<ProgressSummaryDto>("Member")
            : ServiceResult<ProgressSummaryDto>.Ok(await BuildProgressAsync(member, cancellationToken));
    }

    private async Task<ProgressSummaryDto> BuildProgressAsync(
        MemberProfile member,
        CancellationToken cancellationToken)
    {
        var today = await TodayAsync(member.TenantId, cancellationToken);
        var weekStart = GymCalendar.WeekStart(today);
        var since30 = today.AddDays(-29);
        var since12Weeks = weekStart.AddDays(-7 * 11);

        var workouts = await _workouts.QueryAsync(
            q => q
                .Where(x => x.MemberProfileId == member.Id && x.PerformedOn >= today.AddDays(-400))
                .Select(x => new
                {
                    x.PerformedOn,
                    Volume = x.Sets.Sum(s => s.Reps * (s.WeightKg ?? 0))
                }),
            cancellationToken);

        var metrics = await LoadMetricsAsync(member.Id, cancellationToken);
        var weights = metrics.Where(x => x.WeightKg.HasValue).OrderBy(x => x.RecordedOn).ToList();

        var weekly = workouts
            .Where(x => x.PerformedOn >= since12Weeks)
            .GroupBy(x => GymCalendar.WeekStart(x.PerformedOn))
            .ToDictionary(x => x.Key, x => (Count: x.Count(), Volume: x.Sum(w => w.Volume)));

        return new ProgressSummaryDto
        {
            CurrentWeightKg = weights.LastOrDefault()?.WeightKg,
            StartWeightKg = weights.FirstOrDefault()?.WeightKg,
            CurrentBodyFatPercent = metrics
                .Where(x => x.BodyFatPercent.HasValue)
                .OrderBy(x => x.RecordedOn)
                .LastOrDefault()?.BodyFatPercent,
            WorkoutsLast30Days = workouts.Count(x => x.PerformedOn >= since30),
            WorkoutsThisWeek = workouts.Count(x => x.PerformedOn >= weekStart),
            WeeklyStreak = GymCalendar.WeeklyStreak(workouts.Select(x => x.PerformedOn), today),
            VolumeLast30DaysKg = workouts.Where(x => x.PerformedOn >= since30).Sum(x => x.Volume),
            LastWorkoutOn = workouts.Count == 0 ? null : workouts.Max(x => x.PerformedOn),
            Metrics = metrics,
            WeeklyVolume = Enumerable.Range(0, 12)
                .Select(i => since12Weeks.AddDays(7 * i))
                .Select(week => new WeeklyVolumeDto
                {
                    WeekStart = week,
                    Workouts = weekly.TryGetValue(week, out var w) ? w.Count : 0,
                    VolumeKg = weekly.TryGetValue(week, out var v) ? v.Volume : 0
                })
                .ToList(),
            ActiveGoals = await LoadGoalsAsync(member.Id, activeOnly: true, cancellationToken)
        };
    }

    private Task<ServiceResult<MemberProfile>> CurrentMemberAsync(
        string permission,
        CancellationToken cancellationToken) =>
        _access.GetCurrentMemberAsync(
            permission,
            cancellationToken: cancellationToken,
            module: PlatformModules.GymCoaching);

    private async Task<DateOnly> TodayAsync(int tenantId, CancellationToken cancellationToken)
    {
        var gym = await _gyms.GetGymAsync(tenantId, cancellationToken);

        return GymCalendar.Today(_clock, gym?.Settings.TimeZoneId);
    }

    private Task<List<WorkoutLogDto>> LoadWorkoutsAsync(
        Func<IQueryable<WorkoutLog>, IQueryable<WorkoutLog>> filter,
        CancellationToken cancellationToken) =>
        _workouts.QueryAsync(
            q => filter(q)
                .OrderByDescending(x => x.PerformedOn)
                .ThenByDescending(x => x.Id)
                .Take(100)
                .Select(x => new WorkoutLogDto
                {
                    Id = x.Id,
                    PerformedOn = x.PerformedOn,
                    RoutineId = x.WorkoutRoutineId,
                    DurationMinutes = x.DurationMinutes,
                    Notes = x.Notes,
                    TotalVolumeKg = x.Sets.Sum(s => s.Reps * (s.WeightKg ?? 0)),
                    Sets = x.Sets
                        .OrderBy(s => s.Id)
                        .Select(s => new WorkoutSetDto
                        {
                            ExerciseId = s.ExerciseId,
                            ExerciseName = s.Exercise!.Name,
                            SetNumber = s.SetNumber,
                            Reps = s.Reps,
                            WeightKg = s.WeightKg
                        })
                        .ToList()
                }),
            cancellationToken);

    private Task<List<BodyMetricDto>> LoadMetricsAsync(int memberId, CancellationToken cancellationToken) =>
        _metrics.QueryAsync(
            q => q
                .Where(x => x.MemberProfileId == memberId)
                .OrderByDescending(x => x.RecordedOn)
                .Take(120)
                .Select(x => new BodyMetricDto
                {
                    Id = x.Id,
                    RecordedOn = x.RecordedOn,
                    WeightKg = x.WeightKg,
                    BodyFatPercent = x.BodyFatPercent,
                    Notes = x.Notes
                }),
            cancellationToken);

    private Task<List<FitnessGoalDto>> LoadGoalsAsync(int memberId, bool activeOnly, CancellationToken cancellationToken) =>
        _goals.QueryAsync(
            q => q
                .Where(x => x.MemberProfileId == memberId)
                .Where(x => !activeOnly || x.Status == GoalStatus.Active)
                .OrderBy(x => x.Status)
                .ThenByDescending(x => x.CreatedAtUtc)
                .Select(x => new FitnessGoalDto
                {
                    Id = x.Id,
                    Type = x.Type,
                    Description = x.Description,
                    TargetValue = x.TargetValue,
                    TargetUnit = x.TargetUnit,
                    TargetDate = x.TargetDate,
                    Status = x.Status,
                    CreatedAtUtc = x.CreatedAtUtc
                }),
            cancellationToken);

    private static BodyMetricDto MapMetric(BodyMetric x) => new()
    {
        Id = x.Id,
        RecordedOn = x.RecordedOn,
        WeightKg = x.WeightKg,
        BodyFatPercent = x.BodyFatPercent,
        Notes = x.Notes
    };

    private static FitnessGoalDto MapGoal(FitnessGoal x) => new()
    {
        Id = x.Id,
        Type = x.Type,
        Description = x.Description,
        TargetValue = x.TargetValue,
        TargetUnit = x.TargetUnit,
        TargetDate = x.TargetDate,
        Status = x.Status,
        CreatedAtUtc = x.CreatedAtUtc
    };
}

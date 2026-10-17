using IronForge.Application.Auditing.Models;
using IronForge.Application.Auditing.Services;
using IronForge.Application.Commons;
using IronForge.Application.Entities;
using IronForge.Application.Gym.Billing;
using IronForge.Application.Gym.Common;
using IronForge.Application.Gym.Notifications;
using IronForge.Application.Persistence;
using IronForge.Shared.Authorization;
using IronForge.Shared.Models.Gym;
using IronForge.Shared.Subscriptions;

namespace IronForge.Application.Gym.Fitness;

public interface IRoutineService
{
    Task<ServiceResult<List<ExerciseDto>>> ListExercisesAsync(
        string? search,
        string? muscleGroup,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a gym-specific exercise to the library.</summary>
    Task<ServiceResult<ExerciseDto>> CreateExerciseAsync(
        SaveExerciseRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<List<RoutineSummaryDto>>> ListMemberRoutinesAsync(
        int memberId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<RoutineDto>> GetRoutineAsync(
        int routineId,
        CancellationToken cancellationToken = default);

    /// <summary>A trainer assigns a routine to a member, who is notified.</summary>
    Task<ServiceResult<RoutineDto>> AssignRoutineAsync(
        int memberId,
        SaveRoutineRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<RoutineDto>> SetRoutineActiveAsync(
        int routineId,
        bool isActive,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<List<RoutineDto>>> ListMyRoutinesAsync(
        bool activeOnly,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<RoutineDto>> GetMyRoutineAsync(
        int routineId,
        CancellationToken cancellationToken = default);

    /// <summary>A member's own routine, written by them or by the AI coach.</summary>
    Task<ServiceResult<RoutineDto>> CreateMyRoutineAsync(
        SaveRoutineRequest request,
        RoutineSource source,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<RoutineDto>> SetMyRoutineActiveAsync(
        int routineId,
        bool isActive,
        CancellationToken cancellationToken = default);
}

public sealed class RoutineService : IRoutineService
{
    private readonly IGymAccessService _access;
    private readonly IRepository<Exercise> _exercises;
    private readonly IRepository<WorkoutRoutine> _routines;
    private readonly IRepository<MemberProfile> _members;
    private readonly IRepository<User> _users;
    private readonly IBillingEngine _gyms;
    private readonly INotificationService _notifications;
    private readonly IAuditService _audit;
    private readonly TimeProvider _clock;

    public RoutineService(
        IGymAccessService access,
        IRepository<Exercise> exercises,
        IRepository<WorkoutRoutine> routines,
        IRepository<MemberProfile> members,
        IRepository<User> users,
        IBillingEngine gyms,
        INotificationService notifications,
        IAuditService audit,
        TimeProvider clock)
    {
        _access = access;
        _exercises = exercises;
        _routines = routines;
        _members = members;
        _users = users;
        _gyms = gyms;
        _notifications = notifications;
        _audit = audit;
        _clock = clock;
    }

    public async Task<ServiceResult<List<ExerciseDto>>> ListExercisesAsync(
        string? search,
        string? muscleGroup,
        CancellationToken cancellationToken = default)
    {
        var staff = await _access.AuthorizeAsync(Permissions.GymMembersRead, PlatformModules.GymCoaching, cancellationToken);
        var authorization = staff.Allowed
            ? staff
            : await _access.AuthorizeAsync(Permissions.GymSelfRead, PlatformModules.GymCoaching, cancellationToken);

        if (!authorization.Allowed)
        {
            return GymResults.Forbidden<List<ExerciseDto>>(authorization);
        }

        var term = search?.Trim().ToLower();
        var group = muscleGroup?.Trim().ToLower();

        var items = await _exercises.QueryAsync(
            q => q
                .Where(x => string.IsNullOrEmpty(term) || x.Name.ToLower().Contains(term))
                .Where(x => string.IsNullOrEmpty(group) || x.MuscleGroup.ToLower() == group)
                .OrderBy(x => x.MuscleGroup)
                .ThenBy(x => x.Name)
                .Take(200)
                .Select(x => new ExerciseDto
                {
                    Id = x.Id,
                    Name = x.Name,
                    MuscleGroup = x.MuscleGroup,
                    Equipment = x.Equipment,
                    Instructions = x.Instructions,
                    IsCustom = x.TenantId != null
                }),
            cancellationToken);

        return ServiceResult<List<ExerciseDto>>.Ok(items);
    }

    public async Task<ServiceResult<ExerciseDto>> CreateExerciseAsync(
        SaveExerciseRequest request,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymRoutinesManage, PlatformModules.GymCoaching, cancellationToken);

        if (!authorization.Allowed)
        {
            return GymResults.Forbidden<ExerciseDto>(authorization);
        }

        var name = request.Name.Trim();

        if (await _exercises.AnyAsync(x => x.Name.ToLower() == name.ToLower(), cancellationToken))
        {
            return GymResults.Conflict<ExerciseDto>($"The library already has an exercise named '{name}'.");
        }

        var exercise = new Exercise
        {
            TenantId = _access.TenantId!.Value,
            Name = name,
            MuscleGroup = request.MuscleGroup.Trim(),
            Equipment = string.IsNullOrWhiteSpace(request.Equipment) ? null : request.Equipment.Trim(),
            Instructions = string.IsNullOrWhiteSpace(request.Instructions) ? null : request.Instructions.Trim()
        };

        _exercises.Add(exercise);

        await _exercises.SaveChangesAsync(cancellationToken);

        return ServiceResult<ExerciseDto>.Ok(new ExerciseDto
        {
            Id = exercise.Id,
            Name = exercise.Name,
            MuscleGroup = exercise.MuscleGroup,
            Equipment = exercise.Equipment,
            Instructions = exercise.Instructions,
            IsCustom = true
        });
    }

    public async Task<ServiceResult<List<RoutineSummaryDto>>> ListMemberRoutinesAsync(
        int memberId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymMembersRead, PlatformModules.GymCoaching, cancellationToken);

        if (!authorization.Allowed)
        {
            return GymResults.Forbidden<List<RoutineSummaryDto>>(authorization);
        }

        var items = await _routines.QueryAsync(
            q => q
                .Where(x => x.MemberProfileId == memberId)
                .OrderByDescending(x => x.IsActive)
                .ThenByDescending(x => x.CreatedAtUtc)
                .Select(x => new RoutineSummaryDto
                {
                    Id = x.Id,
                    Name = x.Name,
                    Goal = x.Goal,
                    Source = x.Source,
                    IsActive = x.IsActive,
                    DayCount = x.Days.Count,
                    CreatedAtUtc = x.CreatedAtUtc
                }),
            cancellationToken);

        return ServiceResult<List<RoutineSummaryDto>>.Ok(items);
    }

    public async Task<ServiceResult<RoutineDto>> GetRoutineAsync(
        int routineId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymMembersRead, PlatformModules.GymCoaching, cancellationToken);

        if (!authorization.Allowed)
        {
            return GymResults.Forbidden<RoutineDto>(authorization);
        }

        var routine = await LoadRoutinesAsync(q => q.Where(x => x.Id == routineId), cancellationToken);

        return routine.Count == 0
            ? GymResults.NotFound<RoutineDto>("Routine")
            : ServiceResult<RoutineDto>.Ok(routine[0]);
    }

    public async Task<ServiceResult<RoutineDto>> AssignRoutineAsync(
        int memberId,
        SaveRoutineRequest request,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymRoutinesManage, PlatformModules.GymCoaching, cancellationToken);

        if (!authorization.Allowed)
        {
            return GymResults.Forbidden<RoutineDto>(authorization);
        }

        var member = await _members.FirstOrDefaultAsync(
            x => x.Id == memberId && x.AnonymizedAtUtc == null,
            cancellationToken);

        if (member is null)
        {
            return GymResults.NotFound<RoutineDto>("Member");
        }

        var created = await CreateAsync(member, request, RoutineSource.Trainer, _access.UserId, cancellationToken);

        if (!created.Success)
        {
            return created;
        }

        var gym = await _gyms.GetGymAsync(member.TenantId, cancellationToken);

        _notifications.Queue(
            member.TenantId,
            [BillingEngine.Recipient(member)],
            new NotificationMessage
            {
                Type = NotificationType.Routine,
                Title = $"New routine: {created.Data!.Name}",
                Body =
                    $"Your trainer {created.Data.AssignedBy} assigned you a new routine with " +
                    $"{created.Data.Days.Count} day(s). Open it in the app to start training.",
                Link = $"/routines/{created.Data.Id}",
                GymName = gym?.Name
            });

        await _routines.SaveChangesAsync(cancellationToken);

        await _audit.RecordAsync(
            AuditCategory.Coaching,
            "RoutineAssigned",
            AuditOutcome.Succeeded,
            resourceType: "WorkoutRoutine",
            resourceId: created.Data.Id.ToString(),
            reason: $"'{created.Data.Name}' for {member.FullName}.",
            cancellationToken: cancellationToken);

        return created;
    }

    public async Task<ServiceResult<RoutineDto>> SetRoutineActiveAsync(
        int routineId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymRoutinesManage, PlatformModules.GymCoaching, cancellationToken);

        if (!authorization.Allowed)
        {
            return GymResults.Forbidden<RoutineDto>(authorization);
        }

        var routine = await _routines.FirstOrDefaultTrackedAsync(x => x.Id == routineId, cancellationToken);

        if (routine is null)
        {
            return GymResults.NotFound<RoutineDto>("Routine");
        }

        routine.IsActive = isActive;
        routine.UpdatedAtUtc = _clock.GetUtcNow().UtcDateTime;

        await _routines.SaveChangesAsync(cancellationToken);

        return (await LoadRoutinesAsync(q => q.Where(x => x.Id == routineId), cancellationToken)) is [var dto]
            ? ServiceResult<RoutineDto>.Ok(dto)
            : GymResults.NotFound<RoutineDto>("Routine");
    }

    public async Task<ServiceResult<List<RoutineDto>>> ListMyRoutinesAsync(
        bool activeOnly,
        CancellationToken cancellationToken = default)
    {
        var member = await _access.GetCurrentMemberAsync(
            Permissions.GymSelfRead,
            cancellationToken: cancellationToken,
            module: PlatformModules.GymCoaching);

        if (!member.Success)
        {
            return GymResults.From<List<RoutineDto>, MemberProfile>(member);
        }

        var memberId = member.Data!.Id;

        var routines = await LoadRoutinesAsync(
            q => q
                .Where(x => x.MemberProfileId == memberId)
                .Where(x => !activeOnly || x.IsActive),
            cancellationToken);

        return ServiceResult<List<RoutineDto>>.Ok(routines);
    }

    public async Task<ServiceResult<RoutineDto>> GetMyRoutineAsync(
        int routineId,
        CancellationToken cancellationToken = default)
    {
        var member = await _access.GetCurrentMemberAsync(
            Permissions.GymSelfRead,
            cancellationToken: cancellationToken,
            module: PlatformModules.GymCoaching);

        if (!member.Success)
        {
            return GymResults.From<RoutineDto, MemberProfile>(member);
        }

        var memberId = member.Data!.Id;

        var routines = await LoadRoutinesAsync(
            q => q.Where(x => x.Id == routineId && x.MemberProfileId == memberId),
            cancellationToken);

        return routines.Count == 0
            ? GymResults.NotFound<RoutineDto>("Routine")
            : ServiceResult<RoutineDto>.Ok(routines[0]);
    }

    public async Task<ServiceResult<RoutineDto>> CreateMyRoutineAsync(
        SaveRoutineRequest request,
        RoutineSource source,
        CancellationToken cancellationToken = default)
    {
        var member = await _access.GetCurrentMemberAsync(
            Permissions.GymSelfWorkoutsWrite,
            cancellationToken: cancellationToken,
            module: PlatformModules.GymCoaching);

        if (!member.Success)
        {
            return GymResults.From<RoutineDto, MemberProfile>(member);
        }

        var created = await CreateAsync(
            member.Data!,
            request,
            source == RoutineSource.Trainer ? RoutineSource.Self : source,
            assignedByUserId: null,
            cancellationToken);

        if (created.Success)
        {
            await _audit.RecordAsync(
                AuditCategory.Coaching,
                source == RoutineSource.Ai ? "RoutineCreatedByCoach" : "RoutineCreated",
                AuditOutcome.Succeeded,
                resourceType: "WorkoutRoutine",
                resourceId: created.Data!.Id.ToString(),
                reason: created.Data.Name,
                cancellationToken: cancellationToken);
        }

        return created;
    }

    public async Task<ServiceResult<RoutineDto>> SetMyRoutineActiveAsync(
        int routineId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var member = await _access.GetCurrentMemberAsync(
            Permissions.GymSelfWorkoutsWrite,
            cancellationToken: cancellationToken,
            module: PlatformModules.GymCoaching);

        if (!member.Success)
        {
            return GymResults.From<RoutineDto, MemberProfile>(member);
        }

        var memberId = member.Data!.Id;

        var routine = await _routines.FirstOrDefaultTrackedAsync(
            x => x.Id == routineId && x.MemberProfileId == memberId,
            cancellationToken);

        if (routine is null)
        {
            return GymResults.NotFound<RoutineDto>("Routine");
        }

        routine.IsActive = isActive;
        routine.UpdatedAtUtc = _clock.GetUtcNow().UtcDateTime;

        await _routines.SaveChangesAsync(cancellationToken);

        return await GetMyRoutineAsync(routineId, cancellationToken);
    }

    private async Task<ServiceResult<RoutineDto>> CreateAsync(
        MemberProfile member,
        SaveRoutineRequest request,
        RoutineSource source,
        int? assignedByUserId,
        CancellationToken cancellationToken)
    {
        if (request.Days.Count is 0 or > 7)
        {
            return GymResults.Invalid<RoutineDto>("A routine needs between 1 and 7 days.");
        }

        if (request.Days.Any(d => d.Exercises.Count is 0 or > 15))
        {
            return GymResults.Invalid<RoutineDto>("Each day needs between 1 and 15 exercises.");
        }

        var exerciseIds = request.Days
            .SelectMany(d => d.Exercises)
            .Select(e => e.ExerciseId)
            .Distinct()
            .ToList();

        // The library query filter limits this to shared and this gym's exercises.
        var known = (await _exercises.QueryAsync(
                q => q.Where(x => exerciseIds.Contains(x.Id)).Select(x => x.Id),
                cancellationToken))
            .ToHashSet();

        var unknown = exerciseIds.Where(id => !known.Contains(id)).ToList();

        if (unknown.Count > 0)
        {
            return GymResults.Invalid<RoutineDto>(
                $"Unknown exercise id(s): {string.Join(", ", unknown)}. Use ids from the exercise library.");
        }

        var now = _clock.GetUtcNow().UtcDateTime;

        var routine = new WorkoutRoutine
        {
            TenantId = member.TenantId,
            MemberProfileId = member.Id,
            Name = request.Name.Trim(),
            Goal = string.IsNullOrWhiteSpace(request.Goal) ? null : request.Goal.Trim(),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            Source = source,
            AssignedByUserId = assignedByUserId,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Days = request.Days
                .Select((day, dayIndex) => new RoutineDay
                {
                    TenantId = member.TenantId,
                    DayNumber = dayIndex + 1,
                    Name = day.Name.Trim(),
                    Exercises = day.Exercises
                        .Select((exercise, order) => new RoutineExercise
                        {
                            TenantId = member.TenantId,
                            ExerciseId = exercise.ExerciseId,
                            Order = order + 1,
                            Sets = Math.Clamp(exercise.Sets, 1, 10),
                            Reps = exercise.Reps.Trim(),
                            TargetWeightKg = exercise.TargetWeightKg,
                            RestSeconds = exercise.RestSeconds,
                            Notes = string.IsNullOrWhiteSpace(exercise.Notes) ? null : exercise.Notes.Trim()
                        })
                        .ToList()
                })
                .ToList()
        };

        _routines.Add(routine);

        await _routines.SaveChangesAsync(cancellationToken);

        var dto = (await LoadRoutinesAsync(q => q.Where(x => x.Id == routine.Id), cancellationToken)).Single();

        return ServiceResult<RoutineDto>.Ok(dto);
    }

    private async Task<List<RoutineDto>> LoadRoutinesAsync(
        Func<IQueryable<WorkoutRoutine>, IQueryable<WorkoutRoutine>> filter,
        CancellationToken cancellationToken)
    {
        var routines = await _routines.QueryAsync(
            q => filter(q)
                .OrderByDescending(x => x.IsActive)
                .ThenByDescending(x => x.CreatedAtUtc)
                .Select(x => new RoutineDto
                {
                    Id = x.Id,
                    MemberId = x.MemberProfileId,
                    Name = x.Name,
                    Goal = x.Goal,
                    Notes = x.Notes,
                    Source = x.Source,
                    IsActive = x.IsActive,
                    CreatedAtUtc = x.CreatedAtUtc,
                    AssignedBy = x.AssignedByUserId == null ? null : x.AssignedByUserId.ToString(),
                    Days = x.Days
                        .OrderBy(d => d.DayNumber)
                        .Select(d => new RoutineDayDto
                        {
                            Id = d.Id,
                            DayNumber = d.DayNumber,
                            Name = d.Name,
                            Exercises = d.Exercises
                                .OrderBy(e => e.Order)
                                .Select(e => new RoutineExerciseDto
                                {
                                    Id = e.Id,
                                    ExerciseId = e.ExerciseId,
                                    ExerciseName = e.Exercise!.Name,
                                    MuscleGroup = e.Exercise.MuscleGroup,
                                    Order = e.Order,
                                    Sets = e.Sets,
                                    Reps = e.Reps,
                                    TargetWeightKg = e.TargetWeightKg,
                                    RestSeconds = e.RestSeconds,
                                    Notes = e.Notes
                                })
                                .ToList()
                        })
                        .ToList()
                }),
            cancellationToken);

        // Show the trainer's name rather than their user id.
        var trainerIds = routines
            .Where(x => x.AssignedBy is not null)
            .Select(x => int.Parse(x.AssignedBy!))
            .Distinct()
            .ToList();

        if (trainerIds.Count > 0)
        {
            var names = (await _users.ListAsync(x => trainerIds.Contains(x.Id), cancellationToken))
                .ToDictionary(x => x.Id.ToString(), x => x.Username);

            foreach (var routine in routines.Where(x => x.AssignedBy is not null))
            {
                routine.AssignedBy = names.GetValueOrDefault(routine.AssignedBy!, "a trainer");
            }
        }

        return routines;
    }
}

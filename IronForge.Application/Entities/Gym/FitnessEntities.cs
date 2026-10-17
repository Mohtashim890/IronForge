using IronForge.Shared.Models.Gym;

namespace IronForge.Application.Entities
{
    /// <summary>
    /// An exercise in the library. TenantId null is the platform's shared
    /// library, visible to every gym; otherwise a gym's own exercise.
    /// </summary>
    public class Exercise
    {
        public int Id { get; set; }

        public int? TenantId { get; set; }

        public string Name { get; set; } = "";

        public string MuscleGroup { get; set; } = "";

        public string? Equipment { get; set; }

        public string? Instructions { get; set; }
    }

    public class WorkoutRoutine
    {
        public int Id { get; set; }

        public int TenantId { get; set; }

        public int MemberProfileId { get; set; }

        public string Name { get; set; } = "";

        public string? Goal { get; set; }

        public string? Notes { get; set; }

        public RoutineSource Source { get; set; }

        public int? AssignedByUserId { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAtUtc { get; set; }

        public DateTime UpdatedAtUtc { get; set; }

        public List<RoutineDay> Days { get; set; } = [];
    }

    public class RoutineDay
    {
        public int Id { get; set; }

        public int TenantId { get; set; }

        public int WorkoutRoutineId { get; set; }

        public int DayNumber { get; set; }

        public string Name { get; set; } = "";

        public List<RoutineExercise> Exercises { get; set; } = [];
    }

    public class RoutineExercise
    {
        public int Id { get; set; }

        public int TenantId { get; set; }

        public int RoutineDayId { get; set; }

        public int ExerciseId { get; set; }

        public Exercise? Exercise { get; set; }

        public int Order { get; set; }

        public int Sets { get; set; }

        public string Reps { get; set; } = "";

        public decimal? TargetWeightKg { get; set; }

        public int? RestSeconds { get; set; }

        public string? Notes { get; set; }
    }

    public class WorkoutLog
    {
        public int Id { get; set; }

        public int TenantId { get; set; }

        public int MemberProfileId { get; set; }

        public int? WorkoutRoutineId { get; set; }

        public int? RoutineDayId { get; set; }

        public DateOnly PerformedOn { get; set; }

        public int? DurationMinutes { get; set; }

        public string? Notes { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public List<WorkoutSet> Sets { get; set; } = [];
    }

    public class WorkoutSet
    {
        public int Id { get; set; }

        public int TenantId { get; set; }

        public int WorkoutLogId { get; set; }

        public int ExerciseId { get; set; }

        public Exercise? Exercise { get; set; }

        public int SetNumber { get; set; }

        public int Reps { get; set; }

        public decimal? WeightKg { get; set; }
    }

    public class BodyMetric
    {
        public int Id { get; set; }

        public int TenantId { get; set; }

        public int MemberProfileId { get; set; }

        public DateOnly RecordedOn { get; set; }

        public decimal? WeightKg { get; set; }

        public decimal? BodyFatPercent { get; set; }

        public string? Notes { get; set; }
    }

    public class FitnessGoal
    {
        public int Id { get; set; }

        public int TenantId { get; set; }

        public int MemberProfileId { get; set; }

        public GoalType Type { get; set; }

        public string Description { get; set; } = "";

        public decimal? TargetValue { get; set; }

        public string? TargetUnit { get; set; }

        public DateOnly? TargetDate { get; set; }

        public GoalStatus Status { get; set; } = GoalStatus.Active;

        public DateTime CreatedAtUtc { get; set; }

        public DateTime? CompletedAtUtc { get; set; }
    }
}

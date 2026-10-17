using System.ComponentModel.DataAnnotations;

namespace IronForge.Shared.Models.Gym
{
    public sealed class ExerciseDto
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";

        public string MuscleGroup { get; set; } = "";

        public string? Equipment { get; set; }

        public string? Instructions { get; set; }

        /// <summary>False for the platform's shared library.</summary>
        public bool IsCustom { get; set; }
    }

    public sealed class SaveExerciseRequest
    {
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = "";

        [Required]
        [MaxLength(50)]
        public string MuscleGroup { get; set; } = "";

        [MaxLength(100)]
        public string? Equipment { get; set; }

        [MaxLength(2000)]
        public string? Instructions { get; set; }
    }

    public sealed class RoutineSummaryDto
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";

        public string? Goal { get; set; }

        public RoutineSource Source { get; set; }

        public bool IsActive { get; set; }

        public int DayCount { get; set; }

        public string? AssignedBy { get; set; }

        public DateTime CreatedAtUtc { get; set; }
    }

    public sealed class RoutineDto
    {
        public int Id { get; set; }

        public int MemberId { get; set; }

        public string Name { get; set; } = "";

        public string? Goal { get; set; }

        public string? Notes { get; set; }

        public RoutineSource Source { get; set; }

        public bool IsActive { get; set; }

        public string? AssignedBy { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public List<RoutineDayDto> Days { get; set; } = [];
    }

    public sealed class RoutineDayDto
    {
        public int Id { get; set; }

        public int DayNumber { get; set; }

        public string Name { get; set; } = "";

        public List<RoutineExerciseDto> Exercises { get; set; } = [];
    }

    public sealed class RoutineExerciseDto
    {
        public int Id { get; set; }

        public int ExerciseId { get; set; }

        public string ExerciseName { get; set; } = "";

        public string MuscleGroup { get; set; } = "";

        public int Order { get; set; }

        public int Sets { get; set; }

        /// <summary>Target repetitions, e.g. "8-12".</summary>
        public string Reps { get; set; } = "";

        public decimal? TargetWeightKg { get; set; }

        public int? RestSeconds { get; set; }

        public string? Notes { get; set; }
    }

    public sealed class SaveRoutineRequest
    {
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = "";

        [MaxLength(200)]
        public string? Goal { get; set; }

        [MaxLength(2000)]
        public string? Notes { get; set; }

        [MinLength(1)]
        [MaxLength(7)]
        public List<SaveRoutineDayRequest> Days { get; set; } = [];
    }

    public sealed class SaveRoutineDayRequest
    {
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = "";

        [MinLength(1)]
        [MaxLength(15)]
        public List<SaveRoutineExerciseRequest> Exercises { get; set; } = [];
    }

    public sealed class SaveRoutineExerciseRequest
    {
        public int ExerciseId { get; set; }

        [Range(1, 10)]
        public int Sets { get; set; } = 3;

        [Required]
        [MaxLength(20)]
        public string Reps { get; set; } = "10";

        [Range(typeof(decimal), "0", "1000")]
        public decimal? TargetWeightKg { get; set; }

        [Range(0, 600)]
        public int? RestSeconds { get; set; }

        [MaxLength(300)]
        public string? Notes { get; set; }
    }

    public sealed class WorkoutLogDto
    {
        public int Id { get; set; }

        public DateOnly PerformedOn { get; set; }

        public int? RoutineId { get; set; }

        public string? RoutineName { get; set; }

        public string? DayName { get; set; }

        public int? DurationMinutes { get; set; }

        public string? Notes { get; set; }

        public decimal TotalVolumeKg { get; set; }

        public List<WorkoutSetDto> Sets { get; set; } = [];
    }

    public sealed class WorkoutSetDto
    {
        public int ExerciseId { get; set; }

        public string ExerciseName { get; set; } = "";

        public int SetNumber { get; set; }

        public int Reps { get; set; }

        public decimal? WeightKg { get; set; }
    }

    public sealed class LogWorkoutRequest
    {
        public DateOnly? PerformedOn { get; set; }

        public int? RoutineDayId { get; set; }

        [Range(1, 600)]
        public int? DurationMinutes { get; set; }

        [MaxLength(1000)]
        public string? Notes { get; set; }

        [MinLength(1)]
        [MaxLength(100)]
        public List<LogWorkoutSetRequest> Sets { get; set; } = [];
    }

    public sealed class LogWorkoutSetRequest
    {
        public int ExerciseId { get; set; }

        [Range(1, 1000)]
        public int Reps { get; set; }

        [Range(typeof(decimal), "0", "1000")]
        public decimal? WeightKg { get; set; }
    }

    public sealed class BodyMetricDto
    {
        public int Id { get; set; }

        public DateOnly RecordedOn { get; set; }

        public decimal? WeightKg { get; set; }

        public decimal? BodyFatPercent { get; set; }

        public string? Notes { get; set; }
    }

    public sealed class RecordBodyMetricRequest
    {
        public DateOnly? RecordedOn { get; set; }

        [Range(typeof(decimal), "20", "400")]
        public decimal? WeightKg { get; set; }

        [Range(typeof(decimal), "2", "70")]
        public decimal? BodyFatPercent { get; set; }

        [MaxLength(500)]
        public string? Notes { get; set; }
    }

    public sealed class FitnessGoalDto
    {
        public int Id { get; set; }

        public GoalType Type { get; set; }

        public string Description { get; set; } = "";

        public decimal? TargetValue { get; set; }

        public string? TargetUnit { get; set; }

        public DateOnly? TargetDate { get; set; }

        public GoalStatus Status { get; set; }

        public DateTime CreatedAtUtc { get; set; }
    }

    public sealed class SaveFitnessGoalRequest
    {
        public GoalType Type { get; set; }

        [Required]
        [MaxLength(300)]
        public string Description { get; set; } = "";

        [Range(typeof(decimal), "0", "10000")]
        public decimal? TargetValue { get; set; }

        [MaxLength(20)]
        public string? TargetUnit { get; set; }

        public DateOnly? TargetDate { get; set; }
    }

    public sealed class UpdateGoalStatusRequest
    {
        public GoalStatus Status { get; set; }
    }

    public sealed class ProgressSummaryDto
    {
        public decimal? CurrentWeightKg { get; set; }

        public decimal? StartWeightKg { get; set; }

        public decimal? CurrentBodyFatPercent { get; set; }

        public int WorkoutsLast30Days { get; set; }

        public int WorkoutsThisWeek { get; set; }

        public int WeeklyStreak { get; set; }

        public decimal VolumeLast30DaysKg { get; set; }

        public DateOnly? LastWorkoutOn { get; set; }

        public List<BodyMetricDto> Metrics { get; set; } = [];

        public List<WeeklyVolumeDto> WeeklyVolume { get; set; } = [];

        public List<FitnessGoalDto> ActiveGoals { get; set; } = [];
    }

    public sealed class WeeklyVolumeDto
    {
        /// <summary>Monday of the week.</summary>
        public DateOnly WeekStart { get; set; }

        public int Workouts { get; set; }

        public decimal VolumeKg { get; set; }
    }
}

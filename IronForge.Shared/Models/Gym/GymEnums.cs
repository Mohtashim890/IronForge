namespace IronForge.Shared.Models.Gym
{
    public enum BillingInterval
    {
        Monthly = 0,
        Quarterly = 1,
        Yearly = 2
    }

    /// <summary>Lifecycle of a gym membership.</summary>
    public enum MemberStatus
    {
        /// <summary>Joined with the gym's code, or invited; waits for staff approval.</summary>
        Pending = 0,

        Active = 1,

        /// <summary>Paused by staff: no invoices until the freeze ends.</summary>
        Frozen = 2,

        /// <summary>An invoice stayed unpaid past the grace period. Paying reactivates.</summary>
        Expired = 3,

        Cancelled = 4
    }

    public enum InvoiceStatus
    {
        Open = 0,
        Paid = 1,
        Overdue = 2,
        Void = 3
    }

    public enum PaymentMethod
    {
        Card = 0,
        Cash = 1,
        BankTransfer = 2
    }

    /// <summary>Who created a workout routine.</summary>
    public enum RoutineSource
    {
        Trainer = 0,
        Ai = 1,
        Self = 2
    }

    public enum GoalType
    {
        LoseWeight = 0,
        BuildMuscle = 1,
        Endurance = 2,
        Strength = 3,
        General = 4
    }

    public enum GoalStatus
    {
        Active = 0,
        Achieved = 1,
        Abandoned = 2
    }

    public enum NotificationType
    {
        System = 0,
        PaymentReminder = 1,
        Payment = 2,
        Membership = 3,
        Announcement = 4,
        Routine = 5
    }

    public enum AnnouncementAudience
    {
        All = 0,
        Active = 1,
        Overdue = 2,
        Plan = 3
    }
}

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
        /// <summary>Old card payments (Stripe); new online payments use <see cref="Online"/>.</summary>
        Card = 0,
        Cash = 1,
        BankTransfer = 2,

        /// <summary>Paid in the app through the gym's payment provider (card, wallet or online banking).</summary>
        Online = 3
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

    /// <summary>How a member was identified at check-in.</summary>
    public enum CheckInMethod
    {
        /// <summary>Staff looked the member up by name at the front desk.</summary>
        FrontDesk = 0,

        /// <summary>Staff scanned the member's pass from the app.</summary>
        PassScan = 1,

        /// <summary>An unattended check-in device scanned the member's pass.</summary>
        Kiosk = 2,

        // 3 was GymQr (the member scanned a code shown by the gym); removed, old rows were migrated to Kiosk.

        /// <summary>A fingerprint terminal recognised the member and let them in.</summary>
        Terminal = 4
    }

    public enum CheckInDeviceKind
    {
        /// <summary>A tablet or PC running /kiosk, paired with a code.</summary>
        Kiosk = 0,

        /// <summary>A ZKTeco fingerprint terminal speaking the push (ADMS) protocol, registered by serial number.</summary>
        ZkTecoTerminal = 1
    }

    public enum CheckInOutcome
    {
        Allowed = 0,

        /// <summary>Already checked in within the last 30 minutes; no new visit is recorded.</summary>
        AlreadyCheckedIn = 1,

        Denied = 2
    }

    /// <summary>Why a check-in was refused.</summary>
    public enum CheckInDenialReason
    {
        Pending = 0,
        Frozen = 1,
        Expired = 2,
        Cancelled = 3,

        /// <summary>Active, but an invoice is overdue past the gym's grace period.</summary>
        GracePeriodEnded = 4,

        /// <summary>Not an IronForge pass, or the signature does not match.</summary>
        InvalidPass = 5,

        /// <summary>The code's 30-second window is over (an old screenshot).</summary>
        ExpiredPass = 6,

        /// <summary>The same code was already scanned (a copied code).</summary>
        ReusedPass = 7,

        /// <summary>The member reset their pass; codes from the old one no longer work.</summary>
        RevokedPass = 8,

        /// <summary>The pass belongs to another gym or to a deleted member.</summary>
        UnknownMember = 9

        // 10 and 11 were the gym-QR refusals; old rows were migrated to InvalidPass and ExpiredPass.
    }
}

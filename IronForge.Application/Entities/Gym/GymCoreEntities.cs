using IronForge.Shared.Models.Gym;

namespace IronForge.Application.Entities
{
    /// <summary>
    /// Gym-specific settings of a tenant (1:1). Like <see cref="Tenant"/> it
    /// has no tenant query filter: it is looked up by join code before a
    /// tenant is selected and by the billing job for every tenant, so callers
    /// always filter by TenantId explicitly.
    /// </summary>
    public class GymSettings
    {
        public int Id { get; set; }

        public int TenantId { get; set; }

        public string? Address { get; set; }

        public string? Phone { get; set; }

        public string? ContactEmail { get; set; }

        /// <summary>ISO 4217 code; every price and invoice of the gym uses it.</summary>
        public string Currency { get; set; } = "USD";

        public string TimeZoneId { get; set; } = "UTC";

        /// <summary>Unique across the platform; upper-case letters and digits.</summary>
        public string JoinCode { get; set; } = "";

        public int InvoiceLeadDays { get; set; } = 7;

        public int ReminderDaysBeforeDue { get; set; } = 3;

        public List<int> ReminderDaysAfterDue { get; set; } = [1, 7];

        public int GracePeriodDays { get; set; } = 14;

        /// <summary>Stripe Connect (Standard) account that receives member payments.</summary>
        public string? StripeConnectedAccountId { get; set; }

        public bool StripeChargesEnabled { get; set; }

        /// <summary>
        /// How members pay without online payments, e.g. the gym's bank account
        /// or "cash at the front desk". Shown in the member app with unpaid invoices.
        /// </summary>
        public string? PaymentInstructions { get; set; }

        /// <summary>Members may check in by showing their rotating pass (staff or kiosk scans it).</summary>
        public bool MemberPassCheckInEnabled { get; set; } = true;

        /// <summary>Members may check in by scanning the code a check-in device shows.</summary>
        public bool GymQrCheckInEnabled { get; set; }

        /// <summary>
        /// The app asks for the phone's fingerprint, face or screen lock before
        /// it shows the pass or scans the entrance code (against handing the
        /// phone to someone else).
        /// </summary>
        public bool RequirePhoneUnlock { get; set; } = true;

        /// <summary>
        /// Last invoice number issued. Only incremented atomically in the
        /// database (IInvoiceNumberGenerator), never through the entity.
        /// </summary>
        public int LastInvoiceNumber { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime UpdatedAtUtc { get; set; }
    }

    /// <summary>A gym's own membership offer (not the platform subscription plan).</summary>
    public class MembershipPlan
    {
        public int Id { get; set; }

        public int TenantId { get; set; }

        public string Name { get; set; } = "";

        public string? Description { get; set; }

        public decimal Price { get; set; }

        public BillingInterval BillingInterval { get; set; }

        /// <summary>Archived plans keep existing members but cannot be chosen.</summary>
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAtUtc { get; set; }
    }

    /// <summary>
    /// A gym member. Staff can add members who have no app account yet
    /// (<see cref="UserId"/> is null); the profile is linked when the member
    /// accepts the invitation, matched by email.
    /// </summary>
    public class MemberProfile
    {
        public int Id { get; set; }

        public int TenantId { get; set; }

        public int? UserId { get; set; }

        public string FullName { get; set; } = "";

        /// <summary>Lower-case; unique per gym.</summary>
        public string Email { get; set; } = "";

        public string? Phone { get; set; }

        public DateOnly? DateOfBirth { get; set; }

        public string? Gender { get; set; }

        public decimal? HeightCm { get; set; }

        public string? EmergencyContact { get; set; }

        public string? Notes { get; set; }

        public int? MembershipPlanId { get; set; }

        public MembershipPlan? MembershipPlan { get; set; }

        public MemberStatus Status { get; set; } = MemberStatus.Pending;

        public DateTime JoinedAtUtc { get; set; }

        /// <summary>
        /// When staff approved the membership (or added the member). A
        /// member's app access to the gym requires approval: until then their
        /// TenantMembership stays inactive.
        /// </summary>
        public DateTime? ApprovedAtUtc { get; set; }

        public bool IsApproved => ApprovedAtUtc != null;

        /// <summary>A join request staff declined (never approved).</summary>
        public bool IsDeclinedRequest => Status == MemberStatus.Cancelled && ApprovedAtUtc == null;

        /// <summary>Last day covered by a paid invoice.</summary>
        public DateOnly? PaidThrough { get; set; }

        /// <summary>Start of the next billing period not yet invoiced.</summary>
        public DateOnly? NextBillingDate { get; set; }

        public DateOnly? FrozenUntil { get; set; }

        public DateTime? CancelledAtUtc { get; set; }

        /// <summary>Set when personal data was erased on request; invoices are kept.</summary>
        public DateTime? AnonymizedAtUtc { get; set; }

        /// <summary>
        /// Version of the member's rotating pass. Resetting the pass (a lost
        /// phone) increments it, so codes from the old secret are refused.
        /// </summary>
        public int PassKeyVersion { get; set; } = 1;

        /// <summary>Last pass time step accepted; an older or equal step is a copied code.</summary>
        public long? LastPassStep { get; set; }

        /// <summary>When the current photo was set; null without a photo.</summary>
        public DateTime? PhotoUpdatedAtUtc { get; set; }

        /// <summary>
        /// SHA-256 of the app installation the pass lives on. A pass works on
        /// one phone at a time; moving it is allowed but resets the pass and
        /// tells the member, so a shared account is noticed.
        /// </summary>
        public string? PassPhoneIdHash { get; set; }

        public DateTime? PassPhoneBoundAtUtc { get; set; }

        public List<Invoice> Invoices { get; set; } = [];

        public List<CheckIn> CheckIns { get; set; } = [];
    }

    public class CheckIn
    {
        public long Id { get; set; }

        public int TenantId { get; set; }

        public int MemberProfileId { get; set; }

        public MemberProfile? MemberProfile { get; set; }

        public DateTime CheckedInAtUtc { get; set; }

        /// <summary>Staff member at the front desk; null for a self check-in.</summary>
        public int? RecordedByUserId { get; set; }

        public CheckInMethod Method { get; set; }

        /// <summary>The kiosk that scanned the pass.</summary>
        public int? CheckInDeviceId { get; set; }

        public CheckInDevice? CheckInDevice { get; set; }

        /// <summary>
        /// Set when the person got in although the rules would have refused
        /// them (a terminal deciding offline with an older user list).
        /// </summary>
        public string? Warning { get; set; }
    }

    /// <summary>
    /// A refused check-in. Kept apart from <see cref="CheckIn"/> so attendance
    /// counts only real visits, and so staff can spot shared or copied passes.
    /// </summary>
    public class CheckInDenial
    {
        public long Id { get; set; }

        public int TenantId { get; set; }

        /// <summary>Null when the pass did not match a member of this gym.</summary>
        public int? MemberProfileId { get; set; }

        public MemberProfile? MemberProfile { get; set; }

        public CheckInMethod Method { get; set; }

        public int? CheckInDeviceId { get; set; }

        public CheckInDevice? CheckInDevice { get; set; }

        public int? RecordedByUserId { get; set; }

        public CheckInDenialReason Reason { get; set; }

        public string Message { get; set; } = "";

        public DateTime AttemptedAtUtc { get; set; }
    }

    /// <summary>
    /// An unattended check-in device (a tablet or PC at the entrance with a
    /// camera or a USB scanner). An admin creates it and gets a short pairing
    /// code; the device exchanges the code for a long secret key that it sends
    /// with every scan. Only hashes of the code and the key are stored.
    /// </summary>
    public class CheckInDevice
    {
        public int Id { get; set; }

        public int TenantId { get; set; }

        public string Name { get; set; } = "";

        public CheckInDeviceKind Kind { get; set; }

        /// <summary>
        /// A ZKTeco terminal identifies itself by serial number (upper-case,
        /// unique on the platform). Kiosks have none.
        /// </summary>
        public string? SerialNumber { get; set; }

        /// <summary>Accept the terminal only from this IP address (the gym's fixed line), when set.</summary>
        public string? AllowedIp { get; set; }

        /// <summary>When the terminal's user list was last compared with the memberships.</summary>
        public DateTime? LastSyncedAtUtc { get; set; }

        /// <summary>SHA-256 of the device key; null until paired.</summary>
        public string? KeyHash { get; set; }

        /// <summary>SHA-256 of the pending pairing code.</summary>
        public string? PairingCodeHash { get; set; }

        public DateTime? PairingExpiresAtUtc { get; set; }

        public int? CreatedByUserId { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime? PairedAtUtc { get; set; }

        public DateTime? LastSeenAtUtc { get; set; }

        public DateTime? RevokedAtUtc { get; set; }
    }

    /// <summary>
    /// A member's photo, shown at check-in so staff can tell whether the
    /// person at the desk owns the pass. Stored in the database, apart from
    /// the profile so member queries never load the image.
    /// </summary>
    public class MemberPhoto
    {
        public int Id { get; set; }

        public int TenantId { get; set; }

        public int MemberProfileId { get; set; }

        /// <summary>image/jpeg, image/png or image/webp, detected from the bytes.</summary>
        public string ContentType { get; set; } = "";

        public byte[] Data { get; set; } = [];

        /// <summary>Staff took or approved it; the member cannot replace it from the app.</summary>
        public bool UploadedByStaff { get; set; }

        public int? UploadedByUserId { get; set; }

        public DateTime UploadedAtUtc { get; set; }
    }

    public class PasswordResetToken
    {
        public Guid Id { get; set; }

        public int UserId { get; set; }

        /// <summary>SHA-256 of the emailed token; the token itself is never stored.</summary>
        public string TokenHash { get; set; } = "";

        public DateTime CreatedAtUtc { get; set; }

        public DateTime ExpiresAtUtc { get; set; }

        public DateTime? UsedAtUtc { get; set; }
    }

    /// <summary>
    /// A person enrolled on the gym's fingerprint terminals, by terminal user
    /// number (PIN). One numbering per gym, as gyms keep the same PIN on every
    /// terminal. The fingerprint template never leaves the terminal: IronForge
    /// records only that one exists.
    /// </summary>
    public class TerminalUser
    {
        public int Id { get; set; }

        public int TenantId { get; set; }

        public string Pin { get; set; } = "";

        /// <summary>The name stored on the terminal (from its user list or an import).</summary>
        public string? TerminalName { get; set; }

        public bool HasFingerprint { get; set; }

        /// <summary>The member this terminal user is; null until staff link them.</summary>
        public int? MemberProfileId { get; set; }

        public MemberProfile? MemberProfile { get; set; }

        /// <summary>Not a member (e.g. staff): IronForge never changes this user on the terminals.</summary>
        public DateTime? IgnoredAtUtc { get; set; }

        public DateTime FirstSeenAtUtc { get; set; }

        public DateTime? LinkedAtUtc { get; set; }

        public DateTime? LastEntryAtUtc { get; set; }
    }

    /// <summary>What a terminal was last told about one user, to send only changes.</summary>
    public class TerminalUserSync
    {
        public int Id { get; set; }

        public int TenantId { get; set; }

        public int CheckInDeviceId { get; set; }

        public string Pin { get; set; } = "";

        public string Name { get; set; } = "";

        public bool Enabled { get; set; }

        public DateOnly ValidUntil { get; set; }

        /// <summary>When the command went into the queue.</summary>
        public DateTime QueuedAtUtc { get; set; }

        /// <summary>When the terminal reported it applied it.</summary>
        public DateTime? ConfirmedAtUtc { get; set; }
    }

    /// <summary>A command waiting for a terminal, delivered when it polls (ZKTeco getrequest).</summary>
    public class TerminalCommand
    {
        public long Id { get; set; }

        public int TenantId { get; set; }

        public int CheckInDeviceId { get; set; }

        /// <summary>The protocol text after "C:{Id}:", e.g. "DATA UPDATE USERINFO PIN=1043...".</summary>
        public string Text { get; set; } = "";

        /// <summary>The user it concerns, so a failure can be retried.</summary>
        public string? Pin { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime? SentAtUtc { get; set; }

        public DateTime? CompletedAtUtc { get; set; }

        /// <summary>The terminal's answer: 0 is success, negative is an error.</summary>
        public int? ReturnCode { get; set; }
    }
}

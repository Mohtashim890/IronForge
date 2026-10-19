using System.ComponentModel.DataAnnotations;

namespace IronForge.Shared.Models.Gym
{
    /// <summary>
    /// The answer to a check-in attempt, for every method. A refused entry
    /// is a normal outcome (<see cref="CheckInOutcome.Denied"/>), not an
    /// error, so the front desk and the kiosk can show who was refused and why.
    /// </summary>
    public sealed class CheckInResultDto
    {
        public CheckInOutcome Outcome { get; set; }

        public bool IsAllowed => Outcome != CheckInOutcome.Denied;

        /// <summary>One line for the screen, e.g. "Welcome, Sara!".</summary>
        public string Message { get; set; } = "";

        /// <summary>Entry is allowed but staff should act, e.g. an overdue invoice in the grace period.</summary>
        public string? Warning { get; set; }

        public CheckInDenialReason? DenialReason { get; set; }

        /// <summary>Null when the pass could not be matched to a member of this gym.</summary>
        public int? MemberId { get; set; }

        public string? MemberName { get; set; }

        public MemberStatus? Status { get; set; }

        public string? PlanName { get; set; }

        /// <summary>A data: URL, so staff can compare the face with the person at the desk.</summary>
        public string? PhotoDataUrl { get; set; }

        public CheckInDto? CheckIn { get; set; }
    }

    public sealed class ScanPassRequest
    {
        /// <summary>The text of the member's QR code.</summary>
        [Required]
        [MaxLength(200)]
        public string Code { get; set; } = "";
    }

    /// <summary>A refused check-in, kept apart from visits so attendance stays accurate.</summary>
    public sealed class CheckInDenialDto
    {
        public long Id { get; set; }

        public int? MemberId { get; set; }

        public string? MemberName { get; set; }

        public CheckInMethod Method { get; set; }

        public string? DeviceName { get; set; }

        public CheckInDenialReason Reason { get; set; }

        public string Message { get; set; } = "";

        public DateTime AttemptedAtUtc { get; set; }
    }

    /// <summary>
    /// What the member app needs to draw the rotating pass offline: the
    /// codes are computed on the phone from <see cref="Secret"/> and the clock.
    /// </summary>
    public sealed class MemberPassDto
    {
        public int TenantId { get; set; }

        public int MemberId { get; set; }

        /// <summary>Changes when the pass is reset; codes of older versions are refused.</summary>
        public int KeyVersion { get; set; }

        /// <summary>Base64 HMAC key for this member and version.</summary>
        public string Secret { get; set; } = "";

        public int StepSeconds { get; set; } = 30;

        public string FullName { get; set; } = "";

        public string GymName { get; set; } = "";

        public MemberStatus Status { get; set; }

        public string? PhotoDataUrl { get; set; }

        /// <summary>Server time when issued, so the app can correct a wrong phone clock.</summary>
        public DateTime IssuedAtUtc { get; set; }

        /// <summary>Ask for the phone's fingerprint, face or screen lock before showing the pass.</summary>
        public bool RequirePhoneUnlock { get; set; }
    }

    public sealed class MemberPhotoDto
    {
        public string DataUrl { get; set; } = "";

        /// <summary>Set by staff: the member can no longer replace it from the app.</summary>
        public bool UploadedByStaff { get; set; }

        public DateTime UploadedAtUtc { get; set; }
    }

    public sealed class UploadMemberPhotoRequest
    {
        /// <summary>Base64 JPEG, PNG or WebP; at most 1 MB once decoded.</summary>
        [Required]
        [MaxLength(1_400_000)]
        public string Data { get; set; } = "";
    }

    // ---- Check-in devices (kiosks) -------------------------------------------

    public sealed class CheckInDeviceDto
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";

        public CheckInDeviceKind Kind { get; set; }

        /// <summary>A terminal's serial number (it identifies itself with it).</summary>
        public string? SerialNumber { get; set; }

        /// <summary>A terminal only accepted from this IP address, when set.</summary>
        public string? AllowedIp { get; set; }

        /// <summary>Terminal users whose latest state the terminal has confirmed / still has to receive.</summary>
        public int UsersInSync { get; set; }

        public int UsersPending { get; set; }

        public bool IsPaired { get; set; }

        public bool IsRevoked { get; set; }

        /// <summary>A pairing code is waiting to be entered on the device.</summary>
        public DateTime? PairingExpiresAtUtc { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime? PairedAtUtc { get; set; }

        public DateTime? LastSeenAtUtc { get; set; }

        public int CheckInsToday { get; set; }
    }

    public sealed class CreateCheckInDeviceRequest
    {
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = "";
    }

    /// <summary>Shown once to the admin, who types the code on the kiosk.</summary>
    public sealed class CheckInDevicePairingDto
    {
        public CheckInDeviceDto Device { get; set; } = new();

        public string PairingCode { get; set; } = "";

        public DateTime ExpiresAtUtc { get; set; }
    }

    public sealed class PairKioskRequest
    {
        [Required]
        [MaxLength(20)]
        public string PairingCode { get; set; } = "";
    }

    /// <summary>The kiosk keeps <see cref="DeviceKey"/> and sends it with every scan.</summary>
    public sealed class KioskPairingResultDto
    {
        public string DeviceKey { get; set; } = "";

        public string DeviceName { get; set; } = "";

        public string GymName { get; set; } = "";
    }

    public sealed class KioskInfoDto
    {
        public string DeviceName { get; set; } = "";

        public string GymName { get; set; } = "";

        /// <summary>The kiosk scans members' passes.</summary>
        public bool MemberPassEnabled { get; set; }

        /// <summary>The kiosk shows the gym's entrance code for members to scan.</summary>
        public bool GymQrEnabled { get; set; }
    }

    /// <summary>The entrance code a check-in device shows right now.</summary>
    public sealed class GymQrDto
    {
        public string Code { get; set; } = "";

        public int StepSeconds { get; set; }

        /// <summary>When the screen should fetch the next code.</summary>
        public DateTime ExpiresAtUtc { get; set; }
    }

    public sealed class GymQrCheckInRequest
    {
        /// <summary>The text of the code the member scanned at the gym.</summary>
        [Required]
        [MaxLength(200)]
        public string Code { get; set; } = "";
    }
}

using System.ComponentModel.DataAnnotations;

namespace IronForge.Shared.Models.Gym
{
    public sealed class RegisterTerminalRequest
    {
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = "";

        /// <summary>As shown on the terminal (Menu, System info), e.g. CJDE212460012.</summary>
        [Required]
        [MaxLength(40)]
        public string SerialNumber { get; set; } = "";

        /// <summary>Optional: accept the terminal only from the gym's fixed IP address.</summary>
        [MaxLength(45)]
        public string? AllowedIp { get; set; }
    }

    /// <summary>
    /// A person enrolled on the gym's fingerprint terminals, identified by the
    /// terminal user number (PIN). The fingerprint stays on the terminal:
    /// IronForge stores only that one exists.
    /// </summary>
    public sealed class TerminalUserDto
    {
        public int Id { get; set; }

        public string Pin { get; set; } = "";

        /// <summary>The name stored on the terminal.</summary>
        public string? TerminalName { get; set; }

        public bool HasFingerprint { get; set; }

        public int? MemberId { get; set; }

        public string? MemberName { get; set; }

        public MemberStatus? MemberStatus { get; set; }

        /// <summary>For an unlinked user: the member whose name matches exactly.</summary>
        public int? SuggestedMemberId { get; set; }

        public string? SuggestedMemberName { get; set; }

        /// <summary>Not a member (e.g. staff): kept on the terminal and never changed by IronForge.</summary>
        public bool Ignored { get; set; }

        /// <summary>What IronForge last sent to the terminals for this user.</summary>
        public bool? AccessEnabled { get; set; }

        public DateOnly? ValidUntil { get; set; }

        public DateTime? LastEntryAtUtc { get; set; }

        public DateTime FirstSeenAtUtc { get; set; }
    }

    public sealed class LinkTerminalUserRequest
    {
        /// <summary>The member to link; null unlinks.</summary>
        public int? MemberId { get; set; }
    }

    /// <summary>Gives a member a terminal user number; they then enrol a finger at the terminal.</summary>
    public sealed class CreateTerminalUserRequest
    {
        [Required]
        public int MemberId { get; set; }
    }
}

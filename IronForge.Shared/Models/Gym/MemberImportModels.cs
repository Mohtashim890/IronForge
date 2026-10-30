using System.ComponentModel.DataAnnotations;

namespace IronForge.Shared.Models.Gym
{
    /// <summary>
    /// Bulk onboarding of members from a CSV file (see <see cref="MemberImportFormat"/>).
    /// Members only: staff are invited one by one on the Staff page.
    /// </summary>
    public sealed class MemberImportRequest
    {
        /// <summary>The CSV text, header row first.</summary>
        [Required]
        [MaxLength(MemberImportFormat.MaxCharacters)]
        public string Csv { get; set; } = "";

        /// <summary>Email each new member a link to set up the IronForge app (members with an account are linked instead).</summary>
        public bool SendInvitations { get; set; }
    }

    public enum MemberImportRowStatus
    {
        /// <summary>Valid; imported (or would be, in a preview).</summary>
        Ready = 0,

        /// <summary>Already a member of this gym (same email); left as is.</summary>
        Skipped = 1,

        /// <summary>Something is wrong with the row; it is not imported.</summary>
        Error = 2
    }

    public sealed class MemberImportRowDto
    {
        /// <summary>Line in the file (the header is line 1).</summary>
        public int Line { get; set; }

        public string FullName { get; set; } = "";

        public string Email { get; set; } = "";

        public string Plan { get; set; } = "";

        public DateOnly? PaidThrough { get; set; }

        /// <summary>First day IronForge will bill (the day after PaidThrough, or today).</summary>
        public DateOnly? FirstBillingDate { get; set; }

        public MemberImportRowStatus Status { get; set; }

        public string? Message { get; set; }

        /// <summary>After an import: the new member.</summary>
        public int? MemberId { get; set; }
    }

    public sealed class MemberImportResultDto
    {
        /// <summary>False for a preview: nothing was saved.</summary>
        public bool Imported { get; set; }

        /// <summary>A problem with the whole file (no header, unknown column, too many rows…).</summary>
        public string? FileError { get; set; }

        public int ReadyCount { get; set; }

        public int SkippedCount { get; set; }

        public int ErrorCount { get; set; }

        public int InvitationsSent { get; set; }

        /// <summary>Imported members linked to an IronForge account that already existed.</summary>
        public int LinkedToAccounts { get; set; }

        public List<MemberImportRowDto> Rows { get; set; } = [];
    }

    /// <summary>The import file format: one place for the server, the page and the template.</summary>
    public static class MemberImportFormat
    {
        public const int MaxRows = 2000;

        public const int MaxCharacters = 2_000_000;

        public const string DateFormat = "yyyy-MM-dd";

        public sealed record Column(string Name, bool Required, string Format, string Example);

        public static readonly IReadOnlyList<Column> Columns =
        [
            new("FullName", true, "The member's name, up to 150 characters.", "Sara Khan"),
            new("Email", true, "Unique in the file and in the gym. Used to sign in to the app.", "sara.khan@example.com"),
            new("Plan", true, "The name of an active membership plan, exactly as under Membership plans (any capitalisation).", "Monthly"),
            new("Phone", false, "Up to 50 characters.", "+92 300 1234567"),
            new("PaidThrough", false, "yyyy-MM-dd. Last day the member already paid for in your old system. IronForge bills from the next day; empty or past: from today.", "2026-11-30"),
            new("MemberSince", false, "yyyy-MM-dd. When they first joined, for the record. Defaults to the import date.", "2023-04-01"),
            new("DateOfBirth", false, "yyyy-MM-dd.", "1995-08-17"),
            new("Gender", false, "Female, Male or Other.", "Female"),
            new("EmergencyContact", false, "Name and phone, up to 200 characters.", "Ali Khan +92 300 7654321"),
            new("Notes", false, "Staff-only notes, up to 2000 characters.", "Prefers mornings")
        ];

        /// <summary>A file with the header and one example row.</summary>
        public static string Template() =>
            string.Join(",", Columns.Select(x => x.Name)) + "\r\n" +
            string.Join(",", Columns.Select(x => x.Example.Contains(',') ? $"\"{x.Example}\"" : x.Example)) + "\r\n";
    }
}

using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using IronForge.Application.Auditing.Models;
using IronForge.Application.Auditing.Services;
using IronForge.Application.Commons;
using IronForge.Application.Entities;
using IronForge.Application.Gym.Billing;
using IronForge.Application.Gym.Common;
using IronForge.Application.Persistence;
using IronForge.Application.Tenants.Invitations;
using IronForge.Shared.Authorization;
using IronForge.Shared.Models.Gym;

namespace IronForge.Application.Gym.Members;

/// <summary>
/// Bulk onboarding of members from a CSV file (<see cref="MemberImportFormat"/>),
/// e.g. when a gym moves to IronForge from another system.
/// <list type="number">
/// <item><see cref="PreviewAsync"/> checks every row and saves nothing;</item>
/// <item><see cref="ImportAsync"/> checks again and creates the valid rows in one transaction.</item>
/// </list>
/// Every row becomes an approved, Active member on its plan. Staff are never
/// imported: an email that belongs to a staff member is refused. The import
/// issues no invoices itself: the billing job bills from the first billing
/// date (the day after PaidThrough, or today), so nobody is billed for the
/// time before the import.
/// </summary>
public interface IMemberImportService
{
    Task<ServiceResult<MemberImportResultDto>> PreviewAsync(
        MemberImportRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<MemberImportResultDto>> ImportAsync(
        MemberImportRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class MemberImportService : IMemberImportService
{
    private static readonly EmailAddressAttribute EmailCheck = new();

    private readonly IGymAccessService _access;
    private readonly IRepository<MemberProfile> _members;
    private readonly IRepository<MembershipPlan> _plans;
    private readonly IRepository<User> _users;
    private readonly IRepository<TenantMembership> _memberships;
    private readonly IBillingEngine _billing;
    private readonly ITenantInvitationService _invitations;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditService _audit;
    private readonly TimeProvider _clock;

    public MemberImportService(
        IGymAccessService access,
        IRepository<MemberProfile> members,
        IRepository<MembershipPlan> plans,
        IRepository<User> users,
        IRepository<TenantMembership> memberships,
        IBillingEngine billing,
        ITenantInvitationService invitations,
        IUnitOfWork unitOfWork,
        IAuditService audit,
        TimeProvider clock)
    {
        _access = access;
        _members = members;
        _plans = plans;
        _users = users;
        _memberships = memberships;
        _billing = billing;
        _invitations = invitations;
        _unitOfWork = unitOfWork;
        _audit = audit;
        _clock = clock;
    }

    public Task<ServiceResult<MemberImportResultDto>> PreviewAsync(
        MemberImportRequest request,
        CancellationToken cancellationToken = default) =>
        RunAsync(request, save: false, cancellationToken);

    public Task<ServiceResult<MemberImportResultDto>> ImportAsync(
        MemberImportRequest request,
        CancellationToken cancellationToken = default) =>
        RunAsync(request, save: true, cancellationToken);

    /// <summary>One row checked: the values to create the member with.</summary>
    private sealed class Candidate
    {
        public required MemberImportRowDto Row { get; init; }

        public MembershipPlan? Plan { get; set; }

        public string? Phone { get; set; }

        public DateOnly? MemberSince { get; set; }

        public DateOnly? DateOfBirth { get; set; }

        public string? Gender { get; set; }

        public string? EmergencyContact { get; set; }

        public string? Notes { get; set; }

        public int? ExistingUserId { get; set; }
    }

    private async Task<ServiceResult<MemberImportResultDto>> RunAsync(
        MemberImportRequest request,
        bool save,
        CancellationToken cancellationToken)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymMembersManage, cancellationToken);

        if (!authorization.Allowed)
        {
            return GymResults.Forbidden<MemberImportResultDto>(authorization);
        }

        var tenantId = _access.TenantId!.Value;
        var gym = await _billing.GetGymAsync(tenantId, cancellationToken);

        if (gym is null)
        {
            return GymResults.NotFound<MemberImportResultDto>("Gym settings");
        }

        var plans = await _plans.QueryAsync(q => q.Where(x => x.IsActive), cancellationToken);

        if (plans.Count == 0)
        {
            return GymResults.Invalid<MemberImportResultDto>(
                "Create a membership plan first (Membership plans). Every imported member needs a plan.");
        }

        var parsed = MemberImportCsv.Parse(request.Csv);

        if (parsed.Error is not null)
        {
            return ServiceResult<MemberImportResultDto>.Ok(new MemberImportResultDto { FileError = parsed.Error });
        }

        var today = GymCalendar.Today(_clock, gym.Settings.TimeZoneId);
        var candidates = Validate(parsed.Rows, plans, today);

        await CheckAgainstGymAsync(tenantId, candidates, cancellationToken);

        var result = new MemberImportResultDto
        {
            Rows = candidates.Select(x => x.Row).ToList()
        };

        var ready = candidates.Where(x => x.Row.Status == MemberImportRowStatus.Ready).ToList();

        if (save && ready.Count > 0)
        {
            await CreateAsync(tenantId, ready, today, cancellationToken);

            result.Imported = true;
            result.LinkedToAccounts = ready.Count(x => x.ExistingUserId is not null);

            if (request.SendInvitations)
            {
                result.InvitationsSent = await InviteAsync(tenantId, ready.Where(x => x.ExistingUserId is null), cancellationToken);
            }

            await _audit.RecordAsync(
                AuditCategory.Membership,
                "MembersImported",
                AuditOutcome.Succeeded,
                resourceType: "MemberProfile",
                reason: $"{ready.Count} members imported from a file ({result.LinkedToAccounts} linked to existing accounts, " +
                        $"{result.InvitationsSent} invited); {candidates.Count(x => x.Row.Status == MemberImportRowStatus.Skipped)} already members, " +
                        $"{candidates.Count(x => x.Row.Status == MemberImportRowStatus.Error)} rows with errors",
                cancellationToken: cancellationToken);
        }

        result.ReadyCount = ready.Count;
        result.SkippedCount = candidates.Count(x => x.Row.Status == MemberImportRowStatus.Skipped);
        result.ErrorCount = candidates.Count(x => x.Row.Status == MemberImportRowStatus.Error);

        return ServiceResult<MemberImportResultDto>.Ok(result);
    }

    /// <summary>Checks each row on its own and against the rest of the file.</summary>
    private static List<Candidate> Validate(
        List<MemberImportCsv.Row> rows,
        List<MembershipPlan> plans,
        DateOnly today)
    {
        var candidates = new List<Candidate>();
        var emailsInFile = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            var dto = new MemberImportRowDto
            {
                Line = row.Line,
                FullName = row.Get("FullName"),
                Email = GymCalendar.NormalizeEmail(row.Get("Email")),
                Plan = row.Get("Plan")
            };

            var candidate = new Candidate { Row = dto };
            candidates.Add(candidate);

            var errors = new List<string>();

            if (dto.FullName.Length == 0)
                errors.Add("FullName is empty");
            else if (dto.FullName.Length > 150)
                errors.Add("FullName is longer than 150 characters");

            if (dto.Email.Length == 0)
                errors.Add("Email is empty");
            else if (dto.Email.Length > 256 || !EmailCheck.IsValid(dto.Email) || !dto.Email.Contains('.'))
                errors.Add($"\"{dto.Email}\" is not an email address");
            else if (emailsInFile.TryGetValue(dto.Email, out var firstLine))
                errors.Add($"the email is already on line {firstLine}");
            else
                emailsInFile[dto.Email] = row.Line;

            candidate.Plan = plans.FirstOrDefault(x => string.Equals(x.Name.Trim(), dto.Plan, StringComparison.OrdinalIgnoreCase));

            if (dto.Plan.Length == 0)
                errors.Add("Plan is empty");
            else if (candidate.Plan is null)
                errors.Add($"there is no active plan \"{dto.Plan}\" (plans: {string.Join(", ", plans.Select(x => x.Name))})");

            dto.PaidThrough = Date(row, "PaidThrough", errors);
            candidate.MemberSince = Date(row, "MemberSince", errors);
            candidate.DateOfBirth = Date(row, "DateOfBirth", errors);

            if (dto.PaidThrough > today.AddYears(2))
                errors.Add("PaidThrough is more than two years ahead");

            if (candidate.MemberSince > today)
                errors.Add("MemberSince is in the future");

            if (candidate.DateOfBirth is { } birth && (birth > today || birth < today.AddYears(-120)))
                errors.Add("DateOfBirth is not a real birth date");

            candidate.Phone = Text(row, "Phone", 50, errors);
            candidate.EmergencyContact = Text(row, "EmergencyContact", 200, errors);
            candidate.Notes = Text(row, "Notes", 2000, errors);

            var gender = Text(row, "Gender", 20, errors);
            candidate.Gender = gender is null
                ? null
                : new[] { "Female", "Male", "Other" }.FirstOrDefault(x => string.Equals(x, gender, StringComparison.OrdinalIgnoreCase));

            if (gender is not null && candidate.Gender is null)
                errors.Add("Gender must be Female, Male or Other");

            // Bill from the day after the paid period; never for the time before the import.
            dto.FirstBillingDate = dto.PaidThrough is { } paid && paid >= today ? paid.AddDays(1) : today;

            if (errors.Count > 0)
            {
                dto.Status = MemberImportRowStatus.Error;
                dto.Message = Sentence(errors);
            }
        }

        return candidates;
    }

    /// <summary>Already members, staff emails, and existing IronForge accounts.</summary>
    private async Task CheckAgainstGymAsync(int tenantId, List<Candidate> candidates, CancellationToken cancellationToken)
    {
        var emails = candidates
            .Where(x => x.Row.Status == MemberImportRowStatus.Ready && x.Row.Email.Length > 0)
            .Select(x => x.Row.Email)
            .ToList();

        if (emails.Count == 0)
            return;

        var existingMembers = (await _members.QueryAsync(
                q => q.Where(x => emails.Contains(x.Email)).Select(x => x.Email),
                cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        var users = (await _users.QueryAsync(
                q => q.Where(x => emails.Contains(x.Email.ToLower())).Select(x => new { x.Id, Email = x.Email.ToLower() }),
                cancellationToken))
            .GroupBy(x => x.Email)
            .ToDictionary(x => x.Key, x => x.First().Id, StringComparer.Ordinal);

        var userIds = users.Values.ToList();

        var staffUserIds = (await _memberships.QueryAsync(
                q => q
                    .Where(x => x.TenantId == tenantId && userIds.Contains(x.UserId) && x.Role != TenantRoles.Member)
                    .Select(x => x.UserId),
                cancellationToken))
            .ToHashSet();

        var usersWithProfile = (await _members.QueryAsync(
                q => q.Where(x => x.UserId != null && userIds.Contains(x.UserId.Value)).Select(x => x.UserId!.Value),
                cancellationToken))
            .ToHashSet();

        foreach (var candidate in candidates.Where(x => x.Row.Status == MemberImportRowStatus.Ready))
        {
            var email = candidate.Row.Email;

            if (existingMembers.Contains(email))
            {
                candidate.Row.Status = MemberImportRowStatus.Skipped;
                candidate.Row.Message = "Already a member of this gym; left as is.";
                continue;
            }

            if (!users.TryGetValue(email, out var userId))
                continue;

            if (staffUserIds.Contains(userId))
            {
                candidate.Row.Status = MemberImportRowStatus.Error;
                candidate.Row.Message = "This email belongs to a staff member of this gym. Only members are imported; staff are managed on the Staff page.";
            }
            else if (usersWithProfile.Contains(userId))
            {
                candidate.Row.Status = MemberImportRowStatus.Error;
                candidate.Row.Message = "This person's IronForge account is already a member here under another email.";
            }
            else
            {
                candidate.ExistingUserId = userId;
                candidate.Row.Message = "Has an IronForge account: it will be linked, no invitation needed.";
            }
        }
    }

    private async Task CreateAsync(int tenantId, List<Candidate> ready, DateOnly today, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var linkedUsers = ready.Where(x => x.ExistingUserId is not null).Select(x => x.ExistingUserId!.Value).ToList();

        await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var created = new List<(Candidate Candidate, MemberProfile Member)>();

            foreach (var candidate in ready)
            {
                var row = candidate.Row;

                var member = new MemberProfile
                {
                    TenantId = tenantId,
                    UserId = candidate.ExistingUserId,
                    FullName = row.FullName,
                    Email = row.Email,
                    Phone = candidate.Phone,
                    DateOfBirth = candidate.DateOfBirth,
                    Gender = candidate.Gender,
                    EmergencyContact = candidate.EmergencyContact,
                    Notes = candidate.Notes,
                    MembershipPlanId = candidate.Plan!.Id,
                    Status = MemberStatus.Active,
                    JoinedAtUtc = candidate.MemberSince is { } since ? since.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) : now,
                    ApprovedAtUtc = now,
                    PaidThrough = row.PaidThrough is { } paid && paid >= today ? paid : null,
                    NextBillingDate = row.FirstBillingDate
                };

                _members.Add(member);
                created.Add((candidate, member));
            }

            // Existing accounts get (or get back) their Member role in this gym.
            var memberships = await _memberships.QueryTrackedAsync(
                q => q.Where(x => x.TenantId == tenantId && linkedUsers.Contains(x.UserId)),
                cancellationToken);

            foreach (var userId in linkedUsers)
            {
                if (memberships.FirstOrDefault(x => x.UserId == userId) is { } membership)
                {
                    membership.IsActive = true;
                }
                else
                {
                    _memberships.Add(new TenantMembership
                    {
                        TenantId = tenantId,
                        UserId = userId,
                        Role = TenantRoles.Member,
                        IsActive = true,
                        CreatedAtUtc = now
                    });
                }
            }

            await _members.SaveChangesAsync(cancellationToken);

            foreach (var (candidate, member) in created)
            {
                candidate.Row.MemberId = member.Id;
            }

            return true;
        }, cancellationToken);
    }

    private async Task<int> InviteAsync(int tenantId, IEnumerable<Candidate> candidates, CancellationToken cancellationToken)
    {
        var sent = 0;

        foreach (var candidate in candidates)
        {
            // Accepting the invitation creates the account and links this profile by email.
            var invitation = await _invitations.IssueAsync(tenantId, candidate.Row.Email, TenantRoles.Member, cancellationToken);

            if (invitation.Success)
            {
                sent++;
            }
            else
            {
                candidate.Row.Message = $"Imported, but the invitation failed: {invitation.ErrorMessage}";
            }
        }

        return sent;
    }

    private static DateOnly? Date(MemberImportCsv.Row row, string column, List<string> errors)
    {
        var value = row.Get(column);

        if (value.Length == 0)
            return null;

        if (DateOnly.TryParseExact(value, MemberImportFormat.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date;

        errors.Add($"{column} \"{value}\" is not a date like 2026-11-30");
        return null;
    }

    private static string? Text(MemberImportCsv.Row row, string column, int maxLength, List<string> errors)
    {
        var value = row.Get(column);

        if (value.Length > maxLength)
        {
            errors.Add($"{column} is longer than {maxLength} characters");
            return null;
        }

        return value.Length == 0 ? null : value;
    }

    private static string Sentence(List<string> errors)
    {
        var text = string.Join("; ", errors);
        return char.ToUpperInvariant(text[0]) + text[1..] + ".";
    }
}

/// <summary>
/// Reads the import CSV (RFC 4180: quoted fields, doubled quotes, line
/// breaks inside quotes). Accepts a UTF-8 byte order mark, and semicolons
/// when the header uses them (spreadsheets in some regions save that way).
/// Header names are matched ignoring case, spaces and underscores.
/// </summary>
public static class MemberImportCsv
{
    public sealed class Row
    {
        private readonly Dictionary<string, string> _values;

        public Row(int line, Dictionary<string, string> values)
        {
            Line = line;
            _values = values;
        }

        public int Line { get; }

        public string Get(string column) =>
            _values.TryGetValue(column, out var value) ? value.Trim() : "";
    }

    public sealed record Result(List<Row> Rows, string? Error);

    public static Result Parse(string? csv)
    {
        var text = (csv ?? "").TrimStart('﻿');

        if (string.IsNullOrWhiteSpace(text))
            return new Result([], "The file is empty.");

        if (text.Length > MemberImportFormat.MaxCharacters)
            return new Result([], "The file is larger than 2 MB. Split it into smaller files.");

        var firstLine = text.Split('\n', 2)[0];
        var delimiter = firstLine.Count(c => c == ';') > firstLine.Count(c => c == ',') ? ';' : ',';

        var records = Records(text, delimiter);

        if (records.Count == 0)
            return new Result([], "The file is empty.");

        var (headerLine, header) = records[0];
        var columns = new List<string?>();
        var known = MemberImportFormat.Columns.ToDictionary(x => Key(x.Name), x => x.Name, StringComparer.Ordinal);

        foreach (var name in header)
        {
            if (name.Trim().Length == 0)
            {
                columns.Add(null);
                continue;
            }

            if (!known.TryGetValue(Key(name), out var column))
                return new Result([], $"Unknown column \"{name.Trim()}\" in the header (line {headerLine}). Use the columns of the template.");

            if (columns.Contains(column))
                return new Result([], $"The column {column} appears twice in the header.");

            columns.Add(column);
        }

        var missing = MemberImportFormat.Columns.Where(x => x.Required && !columns.Contains(x.Name)).Select(x => x.Name).ToList();

        if (missing.Count > 0)
            return new Result([], $"The header must have the columns {string.Join(", ", missing)}. The first line of the file is the header.");

        var rows = new List<Row>();

        foreach (var (line, fields) in records.Skip(1))
        {
            // Spreadsheets often add empty rows at the end.
            if (fields.All(x => x.Trim().Length == 0))
                continue;

            if (rows.Count == MemberImportFormat.MaxRows)
                return new Result([], $"The file has more than {MemberImportFormat.MaxRows} members. Split it into smaller files.");

            var values = new Dictionary<string, string>(StringComparer.Ordinal);

            for (var i = 0; i < fields.Count && i < columns.Count; i++)
            {
                if (columns[i] is { } column)
                    values[column] = fields[i];
            }

            rows.Add(new Row(line, values));
        }

        return rows.Count == 0
            ? new Result([], "The file has a header but no members.")
            : new Result(rows, null);
    }

    private static string Key(string name) =>
        new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    /// <summary>Records with the line each starts on.</summary>
    private static List<(int Line, List<string> Fields)> Records(string text, char delimiter)
    {
        var records = new List<(int, List<string>)>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var line = 1;
        var recordLine = 1;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    if (c == '\n')
                        line++;

                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"' when field.Length == 0:
                    quoted = true;
                    break;

                case '\r':
                    break;

                case '\n':
                    fields.Add(field.ToString());
                    field.Clear();
                    records.Add((recordLine, fields));
                    fields = [];
                    line++;
                    recordLine = line;
                    break;

                default:
                    if (c == delimiter)
                    {
                        fields.Add(field.ToString());
                        field.Clear();
                    }
                    else
                    {
                        field.Append(c);
                    }

                    break;
            }
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            records.Add((recordLine, fields));
        }

        return records;
    }
}

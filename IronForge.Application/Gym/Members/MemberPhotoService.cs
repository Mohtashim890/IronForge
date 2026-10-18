using IronForge.Application.Auditing.Models;
using IronForge.Application.Auditing.Services;
using IronForge.Application.Commons;
using IronForge.Application.Entities;
using IronForge.Application.Gym.Common;
using IronForge.Application.Persistence;
using IronForge.Shared.Authorization;
using IronForge.Shared.Models.Gym;

namespace IronForge.Application.Gym.Members;

/// <summary>
/// Member photos for identity checks at the front desk and the kiosk. Staff
/// can always set or remove a photo; a member can set their own from the app
/// until staff set one (a staff photo is the verified one).
/// </summary>
public interface IMemberPhotoService
{
    Task<ServiceResult<MemberPhotoDto?>> GetAsync(
        int memberId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<MemberPhotoDto>> UploadAsync(
        int memberId,
        UploadMemberPhotoRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<bool>> DeleteAsync(
        int memberId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<MemberPhotoDto?>> GetMineAsync(
        CancellationToken cancellationToken = default);

    Task<ServiceResult<MemberPhotoDto>> UploadMineAsync(
        UploadMemberPhotoRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class MemberPhotoService : IMemberPhotoService
{
    public const int MaxBytes = 1024 * 1024;

    private readonly IGymAccessService _access;
    private readonly IRepository<MemberProfile> _members;
    private readonly IRepository<MemberPhoto> _photos;
    private readonly IAuditService _audit;
    private readonly TimeProvider _clock;

    public MemberPhotoService(
        IGymAccessService access,
        IRepository<MemberProfile> members,
        IRepository<MemberPhoto> photos,
        IAuditService audit,
        TimeProvider clock)
    {
        _access = access;
        _members = members;
        _photos = photos;
        _audit = audit;
        _clock = clock;
    }

    public async Task<ServiceResult<MemberPhotoDto?>> GetAsync(
        int memberId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymMembersRead, cancellationToken);

        if (!authorization.Allowed)
        {
            return GymResults.Forbidden<MemberPhotoDto?>(authorization);
        }

        if (!await _members.AnyAsync(x => x.Id == memberId && x.AnonymizedAtUtc == null, cancellationToken))
        {
            return GymResults.NotFound<MemberPhotoDto?>("Member");
        }

        return ServiceResult<MemberPhotoDto?>.Ok(await LoadAsync(_photos, memberId, cancellationToken));
    }

    public async Task<ServiceResult<MemberPhotoDto>> UploadAsync(
        int memberId,
        UploadMemberPhotoRequest request,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymMembersManage, cancellationToken);

        if (!authorization.Allowed)
        {
            return GymResults.Forbidden<MemberPhotoDto>(authorization);
        }

        var member = await _members.FirstOrDefaultTrackedAsync(
            x => x.Id == memberId && x.AnonymizedAtUtc == null,
            cancellationToken);

        if (member is null)
        {
            return GymResults.NotFound<MemberPhotoDto>("Member");
        }

        var result = await SaveAsync(member, request, byStaff: true, cancellationToken);

        if (result.Success)
        {
            await RecordAsync("MemberPhotoSet", member, cancellationToken);
        }

        return result;
    }

    public async Task<ServiceResult<bool>> DeleteAsync(
        int memberId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _access.AuthorizeAsync(Permissions.GymMembersManage, cancellationToken);

        if (!authorization.Allowed)
        {
            return GymResults.Forbidden<bool>(authorization);
        }

        var member = await _members.FirstOrDefaultTrackedAsync(
            x => x.Id == memberId && x.AnonymizedAtUtc == null,
            cancellationToken);

        if (member is null)
        {
            return GymResults.NotFound<bool>("Member");
        }

        var photo = await _photos.FirstOrDefaultTrackedAsync(x => x.MemberProfileId == memberId, cancellationToken);

        if (photo is not null)
        {
            _photos.Remove(photo);
            member.PhotoUpdatedAtUtc = null;
            await _photos.SaveChangesAsync(cancellationToken);
            await RecordAsync("MemberPhotoRemoved", member, cancellationToken);
        }

        return ServiceResult<bool>.Ok(true);
    }

    public async Task<ServiceResult<MemberPhotoDto?>> GetMineAsync(
        CancellationToken cancellationToken = default)
    {
        var member = await _access.GetCurrentMemberAsync(Permissions.GymSelfRead, cancellationToken: cancellationToken);

        if (!member.Success)
        {
            return GymResults.From<MemberPhotoDto?, MemberProfile>(member);
        }

        return ServiceResult<MemberPhotoDto?>.Ok(await LoadAsync(_photos, member.Data!.Id, cancellationToken));
    }

    public async Task<ServiceResult<MemberPhotoDto>> UploadMineAsync(
        UploadMemberPhotoRequest request,
        CancellationToken cancellationToken = default)
    {
        var current = await _access.GetCurrentMemberAsync(
            Permissions.GymSelfRead,
            tracked: true,
            cancellationToken: cancellationToken);

        if (!current.Success)
        {
            return GymResults.From<MemberPhotoDto, MemberProfile>(current);
        }

        var member = current.Data!;

        if (await _photos.AnyAsync(x => x.MemberProfileId == member.Id && x.UploadedByStaff, cancellationToken))
        {
            return GymResults.Conflict<MemberPhotoDto>(
                "Your gym has set your photo. Ask the front desk if it needs changing.");
        }

        return await SaveAsync(member, request, byStaff: false, cancellationToken);
    }

    /// <summary>The member's photo as a data: URL, or null. No authorization: callers have checked.</summary>
    public static async Task<string?> LoadDataUrlAsync(
        IRepository<MemberPhoto> photos,
        int memberId,
        CancellationToken cancellationToken)
    {
        return (await LoadAsync(photos, memberId, cancellationToken))?.DataUrl;
    }

    /// <summary>Recognises JPEG, PNG and WebP by their first bytes; anything else is refused.</summary>
    public static string? DetectContentType(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return "image/jpeg";

        if (data.Length >= 8 && data[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            return "image/png";

        if (data.Length >= 12 &&
            data[..4].SequenceEqual("RIFF"u8) &&
            data[8..12].SequenceEqual("WEBP"u8))
            return "image/webp";

        return null;
    }

    private async Task<ServiceResult<MemberPhotoDto>> SaveAsync(
        MemberProfile member,
        UploadMemberPhotoRequest request,
        bool byStaff,
        CancellationToken cancellationToken)
    {
        byte[] data;

        try
        {
            data = Convert.FromBase64String(StripDataUrlPrefix(request.Data));
        }
        catch (FormatException)
        {
            return GymResults.Invalid<MemberPhotoDto>("The photo could not be read.");
        }

        if (data.Length == 0 || data.Length > MaxBytes)
        {
            return GymResults.Invalid<MemberPhotoDto>("The photo must be smaller than 1 MB.");
        }

        var contentType = DetectContentType(data);

        if (contentType is null)
        {
            return GymResults.Invalid<MemberPhotoDto>("Use a JPEG, PNG or WebP photo.");
        }

        var now = _clock.GetUtcNow().UtcDateTime;

        var photo = await _photos.FirstOrDefaultTrackedAsync(x => x.MemberProfileId == member.Id, cancellationToken);

        if (photo is null)
        {
            photo = new MemberPhoto { TenantId = member.TenantId, MemberProfileId = member.Id };
            _photos.Add(photo);
        }

        photo.ContentType = contentType;
        photo.Data = data;
        photo.UploadedByStaff = byStaff;
        photo.UploadedByUserId = _access.UserId;
        photo.UploadedAtUtc = now;
        member.PhotoUpdatedAtUtc = now;

        await _photos.SaveChangesAsync(cancellationToken);

        return ServiceResult<MemberPhotoDto>.Ok(Map(photo));
    }

    private static async Task<MemberPhotoDto?> LoadAsync(
        IRepository<MemberPhoto> photos,
        int memberId,
        CancellationToken cancellationToken)
    {
        var photo = (await photos.QueryAsync(
            q => q.Where(x => x.MemberProfileId == memberId),
            cancellationToken)).SingleOrDefault();

        return photo is null ? null : Map(photo);
    }

    private static MemberPhotoDto Map(MemberPhoto photo) => new()
    {
        DataUrl = $"data:{photo.ContentType};base64,{Convert.ToBase64String(photo.Data)}",
        UploadedByStaff = photo.UploadedByStaff,
        UploadedAtUtc = photo.UploadedAtUtc
    };

    private static string StripDataUrlPrefix(string value)
    {
        var comma = value.IndexOf(',');

        return value.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma > 0
            ? value[(comma + 1)..]
            : value;
    }

    private Task RecordAsync(string action, MemberProfile member, CancellationToken cancellationToken) =>
        _audit.RecordAsync(
            AuditCategory.Membership,
            action,
            AuditOutcome.Succeeded,
            resourceType: "MemberProfile",
            resourceId: member.Id.ToString(),
            reason: member.FullName,
            cancellationToken: cancellationToken);
}

using IronForge.Shared.Models.Gym;

namespace IronForge.Shared.Services;

/// <summary>
/// Keeps the member's last downloaded pass so the app can show it without a
/// connection (gym basements often have none). The mobile app stores it in
/// the platform's secure storage; the default keeps it in memory only.
/// </summary>
public interface IMemberPassStore
{
    Task<MemberPassDto?> GetAsync(int tenantId);

    Task SaveAsync(MemberPassDto pass);

    Task ClearAsync();
}

public sealed class InMemoryMemberPassStore : IMemberPassStore
{
    private MemberPassDto? _pass;

    public Task<MemberPassDto?> GetAsync(int tenantId) =>
        Task.FromResult(_pass?.TenantId == tenantId ? _pass : null);

    public Task SaveAsync(MemberPassDto pass)
    {
        _pass = pass;
        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        _pass = null;
        return Task.CompletedTask;
    }
}

/// <summary>Where a check-in device (kiosk) keeps its device key between restarts.</summary>
public interface IKioskKeyStore
{
    Task<string?> GetAsync();

    Task SaveAsync(string deviceKey);

    Task ClearAsync();
}

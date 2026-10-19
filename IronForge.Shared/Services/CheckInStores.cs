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

    /// <summary>
    /// A random id of this app installation, kept across sign-outs. The pass
    /// works on one phone at a time; this is how the server tells phones apart.
    /// </summary>
    Task<string> GetPhoneIdAsync();
}

public sealed class InMemoryMemberPassStore : IMemberPassStore
{
    private MemberPassDto? _pass;
    private readonly string _phoneId = Guid.NewGuid().ToString("N");

    public Task<string> GetPhoneIdAsync() => Task.FromResult(_phoneId);

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

public enum PhoneUnlockResult
{
    Unlocked,

    /// <summary>The member cancelled or the check failed.</summary>
    Failed,

    /// <summary>The phone has no screen lock, fingerprint or face set up.</summary>
    NotSetUp,

    /// <summary>The platform cannot check (very old OS, or not a phone).</summary>
    NotSupported
}

/// <summary>
/// Asks the phone's owner to prove it is them (fingerprint, face, or the
/// screen lock PIN/pattern) before the pass shows. The phone does the check;
/// IronForge never sees any biometric data.
/// </summary>
public interface IPhoneUnlock
{
    Task<PhoneUnlockResult> UnlockAsync(string reason);
}

/// <summary>For hosts without a phone lock (e.g. a browser): nothing to check.</summary>
public sealed class NoPhoneUnlock : IPhoneUnlock
{
    public Task<PhoneUnlockResult> UnlockAsync(string reason) => Task.FromResult(PhoneUnlockResult.NotSupported);
}

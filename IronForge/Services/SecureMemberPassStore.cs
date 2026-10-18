using System.Text.Json;
using IronForge.Shared.Models.Gym;
using IronForge.Shared.Services;

namespace IronForge.Services
{
    /// <summary>
    /// The member's pass in the platform's secure storage (Keychain, Keystore,
    /// DPAPI), so the rotating QR code works without a connection. Removed on
    /// sign-out together with the tokens (<see cref="SecureTokenStore"/>).
    /// </summary>
    public sealed class SecureMemberPassStore : IMemberPassStore
    {
        public const string StorageKey = "ironforge_member_pass";

        public async Task<MemberPassDto?> GetAsync(int tenantId)
        {
            try
            {
                var json = await SecureStorage.Default.GetAsync(StorageKey);
                var pass = json is null ? null : JsonSerializer.Deserialize<MemberPassDto>(json);

                return pass?.TenantId == tenantId ? pass : null;
            }
            catch (Exception)
            {
                SecureStorage.Default.Remove(StorageKey);
                return null;
            }
        }

        public Task SaveAsync(MemberPassDto pass) =>
            SecureStorage.Default.SetAsync(StorageKey, JsonSerializer.Serialize(pass));

        public Task ClearAsync()
        {
            SecureStorage.Default.Remove(StorageKey);
            return Task.CompletedTask;
        }
    }
}

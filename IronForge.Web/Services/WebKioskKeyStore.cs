using System.Security.Cryptography;
using IronForge.Shared.Services;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.JSInterop;

namespace IronForge.Web.Services
{
    /// <summary>
    /// The check-in device key in the kiosk browser's localStorage, encrypted
    /// with Data Protection like the staff tokens (<see cref="WebTokenStore"/>).
    /// </summary>
    public sealed class WebKioskKeyStore : IKioskKeyStore
    {
        private const string Key = "ironforge_kiosk_device_key";

        private readonly ProtectedLocalStorage _storage;

        public WebKioskKeyStore(ProtectedLocalStorage storage)
        {
            _storage = storage;
        }

        public async Task<string?> GetAsync()
        {
            try
            {
                var result = await _storage.GetAsync<string>(Key);
                return result.Success ? result.Value : null;
            }
            catch (CryptographicException)
            {
                // Written with other Data Protection keys: the device pairs again.
                await ClearAsync();
                return null;
            }
            catch (Exception ex) when (ex is InvalidOperationException or JSDisconnectedException)
            {
                return null;
            }
        }

        public async Task SaveAsync(string deviceKey) =>
            await _storage.SetAsync(Key, deviceKey);

        public async Task ClearAsync()
        {
            try
            {
                await _storage.DeleteAsync(Key);
            }
            catch (Exception ex) when (ex is InvalidOperationException or JSDisconnectedException)
            {
            }
        }
    }
}

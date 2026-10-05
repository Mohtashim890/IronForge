using System.Security.Cryptography;
using IronForge.Shared.Services;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.JSInterop;

namespace IronForge.Web.Services
{
    /// <summary>
    /// Stores JWT tokens in the browser's localStorage (encrypted with ASP.NET Core
    /// Data Protection via <see cref="ProtectedLocalStorage"/>).
    ///
    /// Registered as scoped, so one instance exists per circuit (per browser tab).
    /// Tokens are cached in memory after the first read, so JS interop only happens
    /// once per circuit for access-token reads, plus once per write.
    ///
    /// JS interop is unavailable while a component is statically rendered/prerendered.
    /// In that case the store behaves as if no tokens exist (instead of throwing) and
    /// tries again on the next call, once the interactive circuit is connected.
    /// </summary>
    public sealed class WebTokenStore : ITokenStore
    {
        private const string AccessTokenKey =
            "ironforge_access_token";

        private const string RefreshTokenKey =
            "ironforge_refresh_token";

        private readonly ProtectedLocalStorage _storage;
        private readonly ILogger<WebTokenStore> _logger;
        private readonly SemaphoreSlim _loadLock = new(1, 1);

        private bool _loaded;
        private string? _accessToken;
        private string? _refreshToken;

        public WebTokenStore(
            ProtectedLocalStorage storage,
            ILogger<WebTokenStore> logger)
        {
            _storage = storage;
            _logger = logger;
        }

        public async Task<string?> GetAccessTokenAsync()
        {
            await EnsureLoadedAsync();
            return _accessToken;
        }

        public async Task<string?> GetRefreshTokenAsync()
        {
            // Refresh tokens may be rotated by another tab (another circuit) sharing
            // the same localStorage, so always re-read before using one.
            await EnsureLoadedAsync(forceReload: true);
            return _refreshToken;
        }

        public async Task SetAccessTokenAsync(string token)
        {
            await EnsureLoadedAsync();
            _accessToken = token;
            await TryWriteAsync(AccessTokenKey, token);
        }

        public async Task SetRefreshTokenAsync(string token)
        {
            await EnsureLoadedAsync();
            _refreshToken = token;
            await TryWriteAsync(RefreshTokenKey, token);
        }

        public async Task RemoveTokensAsync()
        {
            _accessToken = null;
            _refreshToken = null;
            _loaded = true;

            await TryDeleteAsync(AccessTokenKey);
            await TryDeleteAsync(RefreshTokenKey);
        }

        private async Task EnsureLoadedAsync(bool forceReload = false)
        {
            if (_loaded && !forceReload)
                return;

            await _loadLock.WaitAsync();

            try
            {
                if (_loaded && !forceReload)
                    return;

                _accessToken = await ReadAsync(AccessTokenKey);
                _refreshToken = await ReadAsync(RefreshTokenKey);
                _loaded = true;
            }
            catch (InvalidOperationException)
            {
                // Statically rendered / prerendering: JS interop is not available yet.
                // Leave _loaded = false so the next call (from the interactive
                // circuit) reads the real values from localStorage.
            }
            catch (JSDisconnectedException)
            {
                // The circuit has gone away; nothing to read.
            }
            catch (TaskCanceledException)
            {
                // The circuit is shutting down; nothing to read.
            }
            finally
            {
                _loadLock.Release();
            }
        }

        private async Task<string?> ReadAsync(string key)
        {
            try
            {
                var result = await _storage.GetAsync<string>(key);

                return result.Success
                    ? result.Value
                    : null;
            }
            catch (CryptographicException ex)
            {
                // The value was written with a different Data Protection key
                // (e.g. keys were reset) or was tampered with. Discard it.
                _logger.LogWarning(
                    ex,
                    "Discarding unreadable token '{Key}' from browser storage.",
                    key);

                await _storage.DeleteAsync(key);

                return null;
            }
        }

        private async Task TryWriteAsync(string key, string value)
        {
            try
            {
                await _storage.SetAsync(key, value);
            }
            catch (Exception ex) when (IsJsUnavailable(ex))
            {
                // The in-memory value is still updated, so the current circuit keeps
                // working. It just won't survive a page reload.
                _logger.LogWarning(
                    "Could not persist token '{Key}' to browser storage: JS interop is unavailable.",
                    key);
            }
        }

        private async Task TryDeleteAsync(string key)
        {
            try
            {
                await _storage.DeleteAsync(key);
            }
            catch (Exception ex) when (IsJsUnavailable(ex))
            {
                _logger.LogWarning(
                    "Could not remove token '{Key}' from browser storage: JS interop is unavailable.",
                    key);
            }
        }

        private static bool IsJsUnavailable(Exception ex) =>
            ex is InvalidOperationException
                or JSDisconnectedException
                or TaskCanceledException;
    }
}

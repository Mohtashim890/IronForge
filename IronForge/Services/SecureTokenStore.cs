
using IronForge.Shared.Services;
using System;
using System.Collections.Generic;
using System.Text;

namespace IronForge.Services
{
    public class SecureTokenStore : ITokenStore
    {
        private const string AccessTokenKey = "ironforge_access_token";

        private const string RefreshTokenKey = "ironforge_refresh_token";

        public async Task SetAccessTokenAsync(
    string token)
        {
            await SecureStorage.Default.SetAsync(
                AccessTokenKey,
                token);
        }

        public async Task<string?> GetAccessTokenAsync()
        {
            try
            {
                return await SecureStorage.Default.GetAsync(
                    AccessTokenKey);
            }
            catch
            {
                SecureStorage.Default.Remove(
                    AccessTokenKey);

                return null;
            }
        }
        public async Task SetRefreshTokenAsync(
    string token)
        {
            await SecureStorage.Default.SetAsync(
                RefreshTokenKey,
                token);
        }

        public async Task<string?> GetRefreshTokenAsync()
        {
            try
            {
                return await SecureStorage.Default.GetAsync(
                    RefreshTokenKey);
            }
            catch
            {
                SecureStorage.Default.Remove(
                    RefreshTokenKey);

                return null;
            }
        }
        public Task RemoveTokensAsync()
        {
            SecureStorage.Default.Remove(
                AccessTokenKey);

            SecureStorage.Default.Remove(
                RefreshTokenKey);

            return Task.CompletedTask;
        }
    }
}

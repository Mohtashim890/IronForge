using System;
using System.Collections.Generic;
using System.Text;

namespace IronForge.Shared.Services
{
    public class InMemoryTokenStore : ITokenStore
    {
        private string? _accessToken;
        private string? _refreshToken;


        public Task SetAccessTokenAsync(string token)
        {
            _accessToken = token;
            return Task.CompletedTask;
        }

        public Task<string?> GetAccessTokenAsync()
        {
            return Task.FromResult(_accessToken);
        }

        public Task SetRefreshTokenAsync(string token)
        {
            _refreshToken = token;
            return Task.CompletedTask;
        }

        public Task<string?> GetRefreshTokenAsync()
        {
            return Task.FromResult(_refreshToken);
        }

        public Task RemoveTokensAsync()
        {
            _accessToken = null;
            _refreshToken = null;
            return Task.CompletedTask;
        }
    }
}

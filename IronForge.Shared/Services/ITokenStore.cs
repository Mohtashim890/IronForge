using System;
using System.Collections.Generic;
using System.Text;

namespace IronForge.Shared.Services
{
    public interface ITokenStore
    {
        Task SetAccessTokenAsync(string token);

        Task<string?> GetAccessTokenAsync();

        Task SetRefreshTokenAsync(string token);

        Task<string?> GetRefreshTokenAsync();

        Task RemoveTokensAsync();
    }
}

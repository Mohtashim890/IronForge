using Microsoft.AspNetCore.Components.Authorization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace IronForge.Shared.Services
{
    public class JwtAuthenticationStateProvider : AuthenticationStateProvider
    {
        private readonly ITokenStore _tokenStore;
        private readonly IAuthService _authService;

        public JwtAuthenticationStateProvider(
            ITokenStore tokenStore,
            IAuthService authService)
        {
            _tokenStore = tokenStore;
            _authService = authService;

            _authService.AuthenticationStateChanged +=
                OnAuthenticationStateChanged;
        }

        public override async Task<AuthenticationState>GetAuthenticationStateAsync()
        {
            var token =
                await _tokenStore.GetAccessTokenAsync();

            if (string.IsNullOrWhiteSpace(token))
            {
                return Anonymous();
            }

            try
            {
                var handler =
                    new JwtSecurityTokenHandler();

                var jwt =
                    handler.ReadJwtToken(token);

                var identity =
                    new ClaimsIdentity(
                        jwt.Claims,
                        authenticationType: "jwt",
                        nameType: ClaimTypes.Name,
                        roleType: ClaimTypes.Role);

                var user =
                    new ClaimsPrincipal(identity);

                return new AuthenticationState(user);
            }
            catch
            {
                return Anonymous();
            }
        }

        private void OnAuthenticationStateChanged()
        {
            NotifyAuthenticationStateChanged(
                GetAuthenticationStateAsync());
        }

        private static AuthenticationState Anonymous()
        {
            var identity =
                new ClaimsIdentity();

            var user =
                new ClaimsPrincipal(identity);

            return new AuthenticationState(user);
        }
    }
}
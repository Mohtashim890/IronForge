using IronForge.Shared.Models;
using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace IronForge.Shared.Services
{
    public class AuthService : IAuthService
    {
        public event Action? AuthenticationStateChanged;
        private readonly HttpClient _httpClient;
        private readonly ITokenStore _tokenStore;
        private readonly SemaphoreSlim _refreshLock = new(1, 1);

        public AuthService(
            HttpClient httpClient,
            ITokenStore tokenStore)
        {
            _httpClient = httpClient;
            _tokenStore = tokenStore;
        }
        public async Task<ActiveTenant?> GetActiveTenantAsync()
        {
            var accessToken =
                await _tokenStore.GetAccessTokenAsync();

            if (string.IsNullOrWhiteSpace(accessToken))
                return null;

            try
            {
                var handler =
                    new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();

                var jwt =
                    handler.ReadJwtToken(accessToken);

                var tenantClaim =
                    jwt.Claims.FirstOrDefault(
                        x => x.Type == "tenant_id");

                if (tenantClaim is null ||
                    !int.TryParse(
                        tenantClaim.Value,
                        out var tenantId))
                {
                    return null;
                }

                var nameClaim =
                    jwt.Claims.FirstOrDefault(
                        x => x.Type == System.Security.Claims.ClaimTypes.Name);

                var roleClaim =
                    jwt.Claims.FirstOrDefault(
                        x => x.Type == System.Security.Claims.ClaimTypes.Role);

                return new ActiveTenant
                {
                    TenantId = tenantId,
                    TenantName = "",
                    Role = roleClaim?.Value ?? ""
                };
            }
            catch
            {
                return null;
            }
        }

        public async Task<ServiceResult<TenantSelectionResponse>>SelectTenantAsync(int tenantId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(
                        await _tokenStore.GetRefreshTokenAsync()))
                {
                    return ServiceResult<TenantSelectionResponse>.Fail(
                        "No refresh token is available.",
                        401);
                }

                // The refresh token is read inside the factory because a retry
                // after a token refresh must send the new (rotated) refresh token.
                using var response =
                    await SendWithAccessTokenAsync(async () =>
                        new HttpRequestMessage(
                            HttpMethod.Post,
                            "api/tenants/select")
                        {
                            Content = JsonContent.Create(new
                            {
                                TenantId = tenantId,
                                RefreshToken =
                                    await _tokenStore.GetRefreshTokenAsync()
                            })
                        });

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode ==
                        System.Net.HttpStatusCode.Forbidden)
                    {
                        return ServiceResult<TenantSelectionResponse>.Fail(
                            "You do not have access to this tenant.",
                            403);
                    }

                    if (response.StatusCode ==
                        System.Net.HttpStatusCode.Unauthorized)
                    {
                        return ServiceResult<TenantSelectionResponse>.Fail(
                            "Your authentication session is no longer valid.",
                            401);
                    }

                    return ServiceResult<TenantSelectionResponse>.Fail(
                        "Unable to select the tenant.",
                        (int)response.StatusCode);
                }

                var result =
                    await response.Content
                        .ReadFromJsonAsync<TenantSelectionResponse>();

                if (result is null ||
                    string.IsNullOrWhiteSpace(result.AccessToken) ||
                    string.IsNullOrWhiteSpace(result.RefreshToken))
                {
                    return ServiceResult<TenantSelectionResponse>.Fail(
                        "The server returned an invalid tenant selection response.",
                        500);
                }

                // Replace both credentials.
                await _tokenStore.SetAccessTokenAsync(
                    result.AccessToken);

                await _tokenStore.SetRefreshTokenAsync(
                    result.RefreshToken);

                AuthenticationStateChanged?.Invoke();

                return ServiceResult<TenantSelectionResponse>.Ok(
                    result);
            }
            catch (HttpRequestException)
            {
                return ServiceResult<TenantSelectionResponse>.Fail(
                    "Unable to connect to the API.");
            }
            catch (TaskCanceledException)
            {
                return ServiceResult<TenantSelectionResponse>.Fail(
                    "The tenant selection request timed out.");
            }
            catch (Exception)
            {
                return ServiceResult<TenantSelectionResponse>.Fail(
                    "An unexpected error occurred.");
            }
        }
        public async Task<ServiceResult<List<TenantMembership>>>GetMyTenantsAsync()
        {
            try
            {
                using var response =
                    await SendWithAccessTokenAsync(() =>
                        Task.FromResult(
                            new HttpRequestMessage(
                                HttpMethod.Get,
                                "api/tenants")));

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode ==
                        System.Net.HttpStatusCode.Unauthorized)
                    {
                        return ServiceResult<List<TenantMembership>>.Fail(
                            "Your authentication session is no longer valid.",
                            401);
                    }

                    return ServiceResult<List<TenantMembership>>.Fail(
                        "Unable to load your tenants.",
                        (int)response.StatusCode);
                }

                var tenants =
                    await response.Content
                        .ReadFromJsonAsync<List<TenantMembership>>();

                if (tenants is null)
                {
                    return ServiceResult<List<TenantMembership>>.Fail(
                        "The server returned an invalid tenant response.",
                        (int)response.StatusCode);
                }

                return ServiceResult<List<TenantMembership>>.Ok(
                    tenants);
            }
            catch (HttpRequestException)
            {
                return ServiceResult<List<TenantMembership>>.Fail(
                    "Unable to connect to the API.");
            }
            catch (TaskCanceledException)
            {
                return ServiceResult<List<TenantMembership>>.Fail(
                    "The tenant request timed out.");
            }
            catch (Exception)
            {
                return ServiceResult<List<TenantMembership>>.Fail(
                    "An unexpected error occurred.");
            }
        }

        public async Task<ServiceResult<LoginResponse>> LoginAsync(
            string username,
            string password)
        {
            try
            {
                var request = new
                {
                    username,
                    password
                };

                var response = await _httpClient.PostAsJsonAsync(
                    "api/auth/login",
                    request);

                if (!response.IsSuccessStatusCode)
                {
                    return ServiceResult<LoginResponse>.Fail(
                        "Invalid username or password.",
                        (int)response.StatusCode);
                }

                var result =
                    await response.Content
                        .ReadFromJsonAsync<LoginResponse>();

                if (result is null ||
                    string.IsNullOrWhiteSpace(result.AccessToken))
                {
                    return ServiceResult<LoginResponse>.Fail(
                        "The server returned an invalid login response.",
                        (int)response.StatusCode);
                }

                await _tokenStore.SetAccessTokenAsync(
                    result.AccessToken);

                await _tokenStore.SetRefreshTokenAsync(
                    result.RefreshToken);
                AuthenticationStateChanged?.Invoke();
                return ServiceResult<LoginResponse>.Ok(result);
            }
            catch (HttpRequestException)
            {
                return ServiceResult<LoginResponse>.Fail(
                    "Unable to connect to the API.");
            }
            catch (TaskCanceledException)
            {
                return ServiceResult<LoginResponse>.Fail(
                    "The login request timed out.");
            }
            catch (Exception)
            {
                return ServiceResult<LoginResponse>.Fail(
                    "An unexpected error occurred.");
            }
        }
        public async Task<ServiceResult<LoginResponse>>RefreshTokenAsync(string? failedAccessToken = null)
        {
            await _refreshLock.WaitAsync();

            try
            {
                // Check whether another request already refreshed
                // the access token while we were waiting for the lock.

                var currentAccessToken =
                    await _tokenStore.GetAccessTokenAsync();

                if (!string.IsNullOrWhiteSpace(failedAccessToken) &&
                    !string.IsNullOrWhiteSpace(currentAccessToken) &&
                    currentAccessToken != failedAccessToken)
                {
                    return ServiceResult<LoginResponse>.Ok(
                        new LoginResponse
                        {
                            AccessToken = currentAccessToken,
                            RefreshToken =
                                await _tokenStore.GetRefreshTokenAsync()
                                ?? ""
                        });
                }

                // Nobody has refreshed the token yet.
                // We actually need to use the refresh token.

                var refreshToken =
                    await _tokenStore.GetRefreshTokenAsync();

                if (string.IsNullOrWhiteSpace(refreshToken))
                {
                    return ServiceResult<LoginResponse>.Fail(
                        "No refresh token is available.",
                        401);
                }

                try
                {
                    var response =
                        await _httpClient.PostAsJsonAsync(
                            "api/auth/refresh",
                            new
                            {
                                RefreshToken = refreshToken
                            });

                    if (!response.IsSuccessStatusCode)
                    {
                        return ServiceResult<LoginResponse>.Fail(
                       "Failed to get new refresh token.",
                       (int)response.StatusCode);
                    }

                    var result =
                        await response.Content
                            .ReadFromJsonAsync<LoginResponse>();

                    if (result == null ||
                        string.IsNullOrWhiteSpace(result.AccessToken) ||
                        string.IsNullOrWhiteSpace(result.RefreshToken))
                    {
                        return ServiceResult<LoginResponse>.Fail(
                            "Invalid token response from server.",
                            500);
                    }

                    await _tokenStore.SetAccessTokenAsync(
                        result.AccessToken);

                    await _tokenStore.SetRefreshTokenAsync(
                        result.RefreshToken);

                    AuthenticationStateChanged?.Invoke();

                    return ServiceResult<LoginResponse>.Ok(
                        result);
                }
                catch (HttpRequestException)
                {
                    return ServiceResult<LoginResponse>.Fail(
                        "Unable to connect to the authentication server.");
                }
                catch (TaskCanceledException)
                {
                    return ServiceResult<LoginResponse>.Fail(
                        "The refresh request timed out.");
                }
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        public async Task LogoutAsync()
        {
            await _tokenStore.RemoveTokensAsync();
            AuthenticationStateChanged?.Invoke();
        }

        public async Task<string?> GetAccessTokenAsync()
        {
            return await _tokenStore.GetAccessTokenAsync();
        }

        public async Task<bool> IsAuthenticatedAsync()
        {
            var token = await _tokenStore.GetAccessTokenAsync();
            return !string.IsNullOrWhiteSpace(token);
        }

        /// <summary>
        /// Sends a request with the current access token. On 401 it refreshes the
        /// tokens once and retries. If the API rejects the refresh token, the user
        /// is signed out (which raises AuthenticationStateChanged and sends the UI
        /// back to the login page).
        /// </summary>
        private async Task<HttpResponseMessage> SendWithAccessTokenAsync(
            Func<Task<HttpRequestMessage>> createRequest)
        {
            var accessToken =
                await _tokenStore.GetAccessTokenAsync();

            using (var request = await createRequest())
            {
                AttachAccessToken(request, accessToken);

                var response =
                    await _httpClient.SendAsync(request);

                if (response.StatusCode !=
                    System.Net.HttpStatusCode.Unauthorized)
                {
                    return response;
                }

                response.Dispose();
            }

            var refreshResult =
                await RefreshTokenAsync(accessToken);

            if (!refreshResult.Success)
            {
                // StatusCode is null for connectivity problems; only sign out
                // when the API actually rejected the refresh.
                if (refreshResult.StatusCode is not null)
                {
                    await LogoutAsync();
                }

                return new HttpResponseMessage(
                    System.Net.HttpStatusCode.Unauthorized);
            }

            using var retryRequest = await createRequest();

            AttachAccessToken(
                retryRequest,
                await _tokenStore.GetAccessTokenAsync());

            return await _httpClient.SendAsync(retryRequest);
        }

        private static void AttachAccessToken(
            HttpRequestMessage request,
            string? accessToken)
        {
            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                request.Headers.Authorization =
                    new AuthenticationHeaderValue(
                        "Bearer",
                        accessToken);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace IronForge.Shared.Services
{
    public class JwtAuthorizationHandler : DelegatingHandler
    {
        private readonly ITokenStore _tokenStore;
        private readonly IAuthService _authService;

        public JwtAuthorizationHandler(
            ITokenStore tokenStore,
            IAuthService authService)
        {
            _tokenStore = tokenStore;
            _authService = authService;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
        {
            var retryRequest =
                await CloneRequestAsync(request);

            var accessToken =
                await _tokenStore.GetAccessTokenAsync();

            AttachAccessToken(request, accessToken);

            var response =
                await base.SendAsync(
                    request,
                    cancellationToken);

            if (response.StatusCode !=
                HttpStatusCode.Unauthorized)
            {
                return response;
            }

            response.Dispose();

            var refreshResult =
                await _authService.RefreshTokenAsync(
                    accessToken);

            if (!refreshResult.Success)
            {
                // StatusCode is null for connectivity problems; only sign out
                // when the API actually rejected the refresh.
                if (refreshResult.StatusCode is not null)
                {
                    await _authService.LogoutAsync();
                }

                return new HttpResponseMessage(
                    HttpStatusCode.Unauthorized);
            }

            AttachAccessToken(
                retryRequest,
                await _tokenStore.GetAccessTokenAsync());

            return await base.SendAsync(
                retryRequest,
                cancellationToken);
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

        private static async Task<HttpRequestMessage>CloneRequestAsync(
                HttpRequestMessage request)
        {
            var clone =
                new HttpRequestMessage(
                    request.Method,
                    request.RequestUri);

            foreach (var header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(
                    header.Key,
                    header.Value);
            }

            if (request.Content != null)
            {
                var content =
                    await request.Content.ReadAsByteArrayAsync();

                clone.Content =
                    new ByteArrayContent(content);

                foreach (var header in request.Content.Headers)
                {
                    clone.Content.Headers.TryAddWithoutValidation(
                        header.Key,
                        header.Value);
                }
            }

            return clone;
        }
    }
}

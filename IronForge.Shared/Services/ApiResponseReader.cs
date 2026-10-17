using System.Net;
using System.Net.Http.Json;
using IronForge.Shared.Models;

namespace IronForge.Shared.Services;

/// <summary>Turns API responses into <see cref="ServiceResult{T}"/>.</summary>
internal static class ApiResponseReader
{
    public static async Task<ServiceResult<T>> ReadAsync<T>(
        HttpResponseMessage response,
        string fallbackMessage,
        CancellationToken cancellationToken = default)
    {
        if (response.IsSuccessStatusCode)
        {
            var data = await response.Content.ReadFromJsonAsync<T>(
                cancellationToken: cancellationToken);

            return data is null
                ? ServiceResult<T>.Fail(
                    "The server returned an empty response.",
                    (int)response.StatusCode)
                : ServiceResult<T>.Ok(data);
        }

        return await FailAsync<T>(response, fallbackMessage);
    }

    /// <summary>For endpoints that return 204 No Content.</summary>
    public static async Task<ServiceResult<bool>> ReadNoContentAsync(
        HttpResponseMessage response,
        string fallbackMessage)
    {
        return response.IsSuccessStatusCode
            ? ServiceResult<bool>.Ok(true)
            : await FailAsync<bool>(response, fallbackMessage);
    }

    private static async Task<ServiceResult<T>> FailAsync<T>(
        HttpResponseMessage response,
        string fallbackMessage)
    {
        var text = (await response.Content.ReadAsStringAsync()).Trim().Trim('"');

        // The API returns plain-text messages; ignore HTML and problem+json bodies.
        var message =
            !string.IsNullOrWhiteSpace(text) &&
            !text.StartsWith('<') &&
            !text.StartsWith('{')
                ? text
                : response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => "Authentication is required.",
                    HttpStatusCode.Forbidden => "You are not authorized to perform this operation.",
                    HttpStatusCode.NotFound => "The item was not found.",
                    _ => fallbackMessage
                };

        return ServiceResult<T>.Fail(message, (int)response.StatusCode);
    }
}

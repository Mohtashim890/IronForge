using System.Net;
using System.Net.Http.Json;
using IronForge.Shared.Models;

namespace IronForge.Shared.Services;

/// <summary>Turns API responses into <see cref="ServiceResult{T}"/>.</summary>
internal static partial class ApiResponseReader
{
    /// <summary>A property name in a default validation message, e.g. "MuscleGroup".</summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"\b[A-Z][a-z]+(?:[A-Z][a-z]+)+\b")]
    private static partial System.Text.RegularExpressions.Regex FieldName();

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
        return ServiceResult<T>.Fail(
            await ErrorMessageAsync(response, fallbackMessage),
            (int)response.StatusCode);
    }

    /// <summary>
    /// The user-facing message of a failed response: the API's plain-text
    /// message, the "message" of its JSON error body, or the field messages
    /// of a validation problem. Other HTML and problem+json bodies fall back
    /// to a message per status code.
    /// </summary>
    public static async Task<string> ErrorMessageAsync(
        HttpResponseMessage response,
        string fallbackMessage)
    {
        var text = (await response.Content.ReadAsStringAsync()).Trim();

        if (text.StartsWith('{'))
        {
            try
            {
                using var json = System.Text.Json.JsonDocument.Parse(text);

                if (json.RootElement.TryGetProperty("message", out var property) &&
                    property.GetString() is { Length: > 0 } fromJson &&
                    response.StatusCode != HttpStatusCode.InternalServerError)
                {
                    return fromJson;
                }

                // Model validation (ValidationProblemDetails): show the field messages.
                if (json.RootElement.TryGetProperty("errors", out var errors) &&
                    errors.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    var messages = errors.EnumerateObject()
                        .SelectMany(x => x.Value.ValueKind == System.Text.Json.JsonValueKind.Array
                            ? x.Value.EnumerateArray().Select(m => m.GetString())
                            : [])
                        .Where(m => !string.IsNullOrWhiteSpace(m))
                        .Select(m => FieldName().Replace(m!, x => x.Value[..1] + string.Concat(x.Value[1..].Select(c => char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : c.ToString()))))
                        .Distinct()
                        .ToList();

                    if (messages.Count > 0)
                    {
                        return string.Join(" ", messages);
                    }
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // Not our error body: fall through.
            }
        }

        text = text.Trim('"');

        return !string.IsNullOrWhiteSpace(text) &&
            !text.StartsWith('<') &&
            !text.StartsWith('{')
                ? text
                : response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => "Authentication is required.",
                    HttpStatusCode.Forbidden => "You are not authorized to perform this operation.",
                    HttpStatusCode.NotFound => "The item was not found.",
                    HttpStatusCode.TooManyRequests => "Too many requests. Please wait a minute and try again.",
                    _ => fallbackMessage
                };
    }
}

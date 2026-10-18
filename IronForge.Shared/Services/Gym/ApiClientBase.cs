using System.Globalization;
using System.Net.Http.Json;
using IronForge.Shared.Models;

namespace IronForge.Shared.Services.Gym;

/// <summary>Common request helpers; every call returns a <see cref="ServiceResult{T}"/>.</summary>
public abstract class ApiClientBase
{
    protected ApiClientBase(HttpClient http)
    {
        Http = http;
    }

    protected HttpClient Http { get; }

    protected async Task<ServiceResult<T>> GetAsync<T>(string url, string fallback, CancellationToken ct)
    {
        try
        {
            using var response = await Http.GetAsync(url, ct);
            return await ApiResponseReader.ReadAsync<T>(response, fallback, ct);
        }
        catch (HttpRequestException)
        {
            return Offline<T>();
        }
    }

    /// <summary>For endpoints that answer 204 No Content when there is nothing (Data is null).</summary>
    protected async Task<ServiceResult<T?>> GetOptionalAsync<T>(string url, string fallback, CancellationToken ct)
        where T : class
    {
        try
        {
            using var response = await Http.GetAsync(url, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                return ServiceResult<T?>.Ok(null);
            }

            var result = await ApiResponseReader.ReadAsync<T>(response, fallback, ct);

            return result.Success
                ? ServiceResult<T?>.Ok(result.Data)
                : ServiceResult<T?>.Fail(result.ErrorMessage, result.StatusCode);
        }
        catch (HttpRequestException)
        {
            return Offline<T?>();
        }
    }

    protected async Task<ServiceResult<T>> PostAsync<T>(string url, object? body, string fallback, CancellationToken ct)
    {
        try
        {
            using var response = body is null
                ? await Http.PostAsync(url, content: null, ct)
                : await Http.PostAsJsonAsync(url, body, ct);
            return await ApiResponseReader.ReadAsync<T>(response, fallback, ct);
        }
        catch (HttpRequestException)
        {
            return Offline<T>();
        }
    }

    protected async Task<ServiceResult<bool>> PostNoContentAsync(string url, object? body, string fallback, CancellationToken ct)
    {
        try
        {
            using var response = body is null
                ? await Http.PostAsync(url, content: null, ct)
                : await Http.PostAsJsonAsync(url, body, ct);
            return await ApiResponseReader.ReadNoContentAsync(response, fallback);
        }
        catch (HttpRequestException)
        {
            return Offline<bool>();
        }
    }

    protected async Task<ServiceResult<T>> PutAsync<T>(string url, object? body, string fallback, CancellationToken ct)
    {
        try
        {
            using var response = await Http.PutAsJsonAsync(url, body, ct);
            return await ApiResponseReader.ReadAsync<T>(response, fallback, ct);
        }
        catch (HttpRequestException)
        {
            return Offline<T>();
        }
    }

    protected async Task<ServiceResult<bool>> DeleteAsync(string url, string fallback, CancellationToken ct)
    {
        try
        {
            using var response = await Http.DeleteAsync(url, ct);
            return await ApiResponseReader.ReadNoContentAsync(response, fallback);
        }
        catch (HttpRequestException)
        {
            return Offline<bool>();
        }
    }

    /// <summary>Builds a query string from non-null values.</summary>
    protected static string Query(string path, params (string Name, object? Value)[] values)
    {
        var parts = values
            .Where(x => x.Value is not null && !(x.Value is string text && string.IsNullOrWhiteSpace(text)))
            .Select(x => $"{x.Name}={Uri.EscapeDataString(Format(x.Value!))}")
            .ToList();

        return parts.Count == 0 ? path : $"{path}?{string.Join("&", parts)}";
    }

    private static string Format(object value) => value switch
    {
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        bool flag => flag ? "true" : "false",
        Enum e => Convert.ToInt32(e, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };

    private static ServiceResult<T> Offline<T>() =>
        ServiceResult<T>.Fail("Cannot reach the IronForge server. Check your connection and try again.", 503);
}

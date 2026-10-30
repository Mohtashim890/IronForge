using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IronForge.Application.Gym.Billing.Payments;

namespace IronForge.Payments;

/// <summary>HTTP and signature helpers shared by the providers.</summary>
internal static class ProviderHttp
{
    /// <summary>
    /// Sends the request and returns the parsed JSON body of a success. A
    /// network failure or an error status becomes a PaymentProviderException
    /// with the provider's message when it gives one.
    /// </summary>
    public static async Task<JsonDocument> SendAsync(
        HttpClient http,
        HttpRequestMessage request,
        string providerName,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;

        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new PaymentProviderException($"{providerName} could not be reached.", exception);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new PaymentProviderException(
                    ErrorMessage(body) ?? $"{providerName} answered {(int)response.StatusCode}.",
                    statusCode: (int)response.StatusCode);
            }

            try
            {
                return JsonDocument.Parse(body);
            }
            catch (JsonException exception)
            {
                throw new PaymentProviderException($"{providerName} sent an unreadable answer.", exception);
            }
        }
    }

    /// <summary>The message in a provider's error body ("message", or Safepay's status.errors).</summary>
    public static string? ErrorMessage(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (root.TryGetProperty("status", out var status) &&
                status.ValueKind == JsonValueKind.Object &&
                status.TryGetProperty("errors", out var errors) &&
                errors.ValueKind == JsonValueKind.Array &&
                errors.GetArrayLength() > 0)
            {
                return errors[0].GetString();
            }

            if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
            {
                return message.GetString();
            }

            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
            {
                return error.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    public static string? Header(IReadOnlyDictionary<string, string> headers, string name) =>
        headers.FirstOrDefault(x => string.Equals(x.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

    /// <summary>Compares a received hex signature with the expected HMAC in constant time.</summary>
    public static bool HexEquals(byte[] expected, string? receivedHex)
    {
        if (string.IsNullOrWhiteSpace(receivedHex))
        {
            return false;
        }

        byte[] received;

        try
        {
            received = Convert.FromHexString(receivedHex.Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expected, received);
    }

    public static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

    public static string? String(JsonElement element, params string[] path)
    {
        var current = element;

        foreach (var name in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out current))
            {
                return null;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number => current.GetRawText(),
            _ => null
        };
    }
}

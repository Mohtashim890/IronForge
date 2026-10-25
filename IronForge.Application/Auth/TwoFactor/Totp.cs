using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace IronForge.Application.Auth.TwoFactor;

/// <summary>
/// Time-based one-time passwords (RFC 6238: HMAC-SHA1, 30-second steps,
/// 6 digits), the codes Google Authenticator, Microsoft Authenticator and
/// similar apps show.
/// </summary>
public static class Totp
{
    public const int StepSeconds = 30;

    public const int Digits = 6;

    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(20);

    public static long StepAt(DateTimeOffset now) => now.ToUnixTimeSeconds() / StepSeconds;

    public static string Code(byte[] secret, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);

        var hash = HMACSHA1.HashData(secret, counter);
        var offset = hash[^1] & 0x0F;

        var binary =
            ((hash[offset] & 0x7F) << 24) |
            (hash[offset + 1] << 16) |
            (hash[offset + 2] << 8) |
            hash[offset + 3];

        return (binary % 1_000_000).ToString("D6");
    }

    /// <summary>
    /// The step of the matching code: the current step or one either side
    /// (phone clock drift). Steps at or before <paramref name="lastUsedStep"/>
    /// are refused, so a code cannot be used twice.
    /// </summary>
    public static long? Match(byte[] secret, string? code, DateTimeOffset now, long? lastUsedStep)
    {
        var digits = Normalize(code);

        if (digits.Length != Digits || !digits.All(char.IsAsciiDigit))
        {
            return null;
        }

        var current = StepAt(now);

        for (var step = current - 1; step <= current + 1; step++)
        {
            if (lastUsedStep is { } last && step <= last)
            {
                continue;
            }

            if (CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(Code(secret, step)),
                    Encoding.ASCII.GetBytes(digits)))
            {
                return step;
            }
        }

        return null;
    }

    /// <summary>Drops spaces and dashes people type or paste.</summary>
    public static string Normalize(string? code) =>
        new((code ?? "").Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());

    /// <summary>The link an authenticator app reads from the QR code.</summary>
    public static string OtpAuthUri(string issuer, string account, byte[] secret) =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}" +
        $"?secret={ToBase32(secret)}&issuer={Uri.EscapeDataString(issuer)}&digits={Digits}&period={StepSeconds}";

    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>RFC 4648 base32 without padding, as authenticator apps expect.</summary>
    public static string ToBase32(byte[] data)
    {
        var result = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;

        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;

            while (bits >= 5)
            {
                result.Append(Base32Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            result.Append(Base32Alphabet[(buffer << (5 - bits)) & 31]);
        }

        return result.ToString();
    }

    public static byte[] FromBase32(string text)
    {
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;

        foreach (var c in text.ToUpperInvariant().Where(c => c != '=' && !char.IsWhiteSpace(c)))
        {
            var value = Base32Alphabet.IndexOf(c);

            if (value < 0)
            {
                throw new FormatException($"'{c}' is not a base32 character.");
            }

            buffer = (buffer << 5) | value;
            bits += 5;

            if (bits >= 8)
            {
                bytes.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }

        return bytes.ToArray();
    }
}

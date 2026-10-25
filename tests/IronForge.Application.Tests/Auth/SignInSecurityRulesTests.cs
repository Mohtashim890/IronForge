using System.Security.Cryptography;
using System.Text;
using IronForge.Application.Auth.Services;
using IronForge.Application.Auth.TwoFactor;
using Microsoft.Extensions.Options;

namespace IronForge.Application.Tests.Auth;

public class SignInSecurityRulesTests
{
    // RFC 6238 appendix B: SHA-1 secret "12345678901234567890"; the last 6 of the 8-digit codes.
    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    public void Totp_MatchesTheRfcTestVectors(long unixSeconds, string expected)
    {
        var secret = Encoding.ASCII.GetBytes("12345678901234567890");

        Assert.Equal(expected, Totp.Code(secret, unixSeconds / Totp.StepSeconds));
    }

    [Fact]
    public void Totp_AcceptsOneStepOfClockDrift_AndNoCodeTwice()
    {
        var secret = Totp.NewSecret();
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var step = Totp.StepAt(now);

        Assert.Equal(step - 1, Totp.Match(secret, Totp.Code(secret, step - 1), now, null));
        Assert.Equal(step + 1, Totp.Match(secret, Totp.Code(secret, step + 1), now, null));
        Assert.Null(Totp.Match(secret, Totp.Code(secret, step - 2), now, null));

        // Typed with a space; but a code at or before the last used step is refused.
        Assert.Equal(step, Totp.Match(secret, Totp.Code(secret, step).Insert(3, " "), now, null));
        Assert.Null(Totp.Match(secret, Totp.Code(secret, step), now, lastUsedStep: step));

        Assert.Null(Totp.Match(secret, "12345", now, null));
        Assert.Null(Totp.Match(secret, "abcdef", now, null));
    }

    [Fact]
    public void Base32_RoundTrips_AndMatchesRfc4648()
    {
        Assert.Equal("MZXW6YTBOI", Totp.ToBase32(Encoding.ASCII.GetBytes("foobar")));

        var secret = Totp.NewSecret();

        Assert.Equal(secret, Totp.FromBase32(Totp.ToBase32(secret)));
        Assert.Contains("secret=" + Totp.ToBase32(secret), Totp.OtpAuthUri("IronForge", "a@b.c", secret));
    }

    [Theory]
    [InlineData(4, 0)]
    [InlineData(5, 1)]
    [InlineData(6, 2)]
    [InlineData(7, 4)]
    [InlineData(9, 16)]
    [InlineData(10, 30)]
    [InlineData(50, 30)]
    public void Lockout_StartsAtTheFifthFailure_AndGrowsToHalfAnHour(int failures, int minutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(minutes), SignInGuard.LockoutFor(failures));
    }

    [Fact]
    public void SecretProtector_RoundTrips_AndRefusesTamperingOrAnotherKey()
    {
        var protector = Protector("first-two-factor-encryption-key-0123456789");
        var secret = Totp.NewSecret();

        var stored = protector.Protect(secret);

        Assert.StartsWith("v1.", stored);
        Assert.DoesNotContain(Totp.ToBase32(secret), stored);
        Assert.Equal(secret, protector.Unprotect(stored));

        var bytes = Convert.FromBase64String(stored[3..]);
        bytes[^1] ^= 1;

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect("v1." + Convert.ToBase64String(bytes)));
        Assert.ThrowsAny<CryptographicException>(() =>
            Protector("other-two-factor-encryption-key-0123456789").Unprotect(stored));
    }

    [Fact]
    public void RecoveryCodes_AreHashedIgnoringCaseAndDashes()
    {
        Assert.Equal(
            TwoFactorService.HashRecoveryCode("abcde-fghjk"),
            TwoFactorService.HashRecoveryCode("ABCDE FGHJK"));
    }

    private static TwoFactorSecretProtector Protector(string key) =>
        new(Options.Create(new TwoFactorOptions { EncryptionKey = key }));
}

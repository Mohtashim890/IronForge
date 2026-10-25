using IronForge.Application.Auth.DTOs;

namespace IronForge.Application.Auth.Models;

/// <summary>
/// Outcome of a sign-in step: tokens (or a two-factor challenge), wrong
/// credentials, or a refusal with a reason and status code.
/// </summary>
public sealed class SignInOutcome
{
    public LoginResponse? Response { get; private init; }

    /// <summary>Why the sign-in was refused (shown to the user); null for wrong credentials.</summary>
    public string? DenialReason { get; private init; }

    /// <summary>403 for valid credentials without access, 429 for a locked account, 401 for a wrong code.</summary>
    public int StatusCode { get; private init; } = 401;

    public bool Succeeded => Response is not null;

    public static readonly SignInOutcome InvalidCredentials = new();

    public static SignInOutcome Denied(string reason) => new() { DenialReason = reason, StatusCode = 403 };

    public static SignInOutcome LockedOut(string reason) => new() { DenialReason = reason, StatusCode = 429 };

    public static SignInOutcome InvalidCode() =>
        new() { DenialReason = "That code is not valid. Enter the current code from your authenticator app, or a recovery code.", StatusCode = 401 };

    public static SignInOutcome Success(LoginResponse response) => new() { Response = response, StatusCode = 200 };
}

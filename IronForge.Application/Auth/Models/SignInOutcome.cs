using IronForge.Application.Auth.DTOs;

namespace IronForge.Application.Auth.Models;

/// <summary>Outcome of a sign-in: tokens, wrong credentials, or valid credentials without access yet.</summary>
public sealed class SignInOutcome
{
    public LoginResponse? Response { get; private init; }

    /// <summary>Why valid credentials were refused (e.g. a join request awaiting approval).</summary>
    public string? DenialReason { get; private init; }

    public bool Succeeded => Response is not null;

    public static readonly SignInOutcome InvalidCredentials = new();

    public static SignInOutcome Denied(string reason) => new() { DenialReason = reason };

    public static SignInOutcome Success(LoginResponse response) => new() { Response = response };
}

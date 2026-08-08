using FantasyTools.Api.Models;

namespace FantasyTools.Api.Services;

public enum LoginOutcome
{
    Success,
    InvalidCredentials,
    EmailNotVerified
}

public interface IAuthService
{
    /// <summary>
    /// Creates the account and sends the verification email. No session is issued -- the caller must
    /// verify first. Throws <see cref="ArgumentException"/> on invalid input or a duplicate email.
    /// </summary>
    Task Register(RegisterRequestModel request);

    Task<(LoginOutcome Outcome, AuthResponseModel Response)> Login(LoginRequestModel request);

    /// <summary>
    /// Marks the account verified. Returns false for an unknown account, a wrong token, or an expired
    /// one -- all three answer identically, so this cannot be used to probe which addresses exist.
    /// Idempotent: following the same link twice succeeds twice.
    /// </summary>
    Task<bool> Verify(string email, string token);

    /// <summary>Re-sends the verification email, subject to a per-account throttle. Never reveals whether the account exists.</summary>
    Task ResendVerification(string email);

    /// <summary>Current state of the signed-in account, read from the document rather than the token.</summary>
    Task<UserResponseModel> GetByEmail(string email);
}

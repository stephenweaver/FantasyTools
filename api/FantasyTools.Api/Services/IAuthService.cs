using FantasyTools.Api.Models;

namespace FantasyTools.Api.Services;

public enum LoginOutcome
{
    Success,
    InvalidCredentials,
    EmailNotVerified
}

public enum RegisterOutcome
{
    Created,

    /// <summary>The address had an unverified account and the right password, so the link was sent again.</summary>
    Reissued,

    /// <summary>Verified already, or the password did not match. Answered the same way for both.</summary>
    AlreadyExists
}

public interface IAuthService
{
    /// <summary>
    /// Creates the account and sends the verification email. No session is issued -- the caller must
    /// verify first. Throws <see cref="ArgumentException"/> on invalid input or a duplicate email, and
    /// <see cref="EmailDeliveryException"/> when the account was written but the mail could not be sent.
    /// </summary>
    /// <remarks>
    /// Registering an address that already has an *unverified* account is not a duplicate: given the
    /// right password it re-sends that account's link. See <c>AuthService.Reissue</c>.
    /// </remarks>
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

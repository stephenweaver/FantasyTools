namespace FantasyTools.Api.Documents;

/// <summary>
/// A user account. <see cref="Id"/> is the normalized email so the account can be looked up
/// with a single <c>IFileService.Retrieve</c> -- there is no query engine behind the document store.
/// </summary>
public class UserDocument : BaseDocument
{
    #region Overrides

    public override string Id
    { get => Normalize(Email); set { } }

    public override string Pk
    { get => "users"; set { } }

    #endregion Overrides

    public string UserId { get; set; }
    public string Email { get; set; }
    public string Name { get; set; }
    public string PasswordHash { get; set; }
    public DateTime CreatedAt { get; set; }

    public bool EmailVerified { get; set; }

    /// <summary>
    /// SHA-256 of the token that went out in the email. The token itself is a bearer credential
    /// sitting in an inbox, so only the hash is ever persisted.
    /// </summary>
    public string VerificationTokenHash { get; set; }

    public DateTime? VerificationTokenExpiresAt { get; set; }

    /// <summary>When the last verification email went out. Backs the resend throttle.</summary>
    public DateTime? VerificationSentAt { get; set; }

    public static string Normalize(string email) => email?.Trim().ToLowerInvariant();
}

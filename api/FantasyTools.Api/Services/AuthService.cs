using FantasyTools.Api.Documents;
using FantasyTools.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace FantasyTools.Api.Services;

public class AuthService(
    IFileService fileService,
    IPasswordHasher<UserDocument> passwordHasher,
    IEmailService emailService,
    ILogger<AuthService> logger
    ) : IAuthService
{
    public const string Audience = "fantasytools";
    public const string Issuer = "fantasytools";

    private static readonly TimeSpan TokenLifetime = TimeSpan.FromDays(7);
    private static readonly TimeSpan VerificationLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan ResendInterval = TimeSpan.FromSeconds(60);

    /// <summary>
    /// One gate per account, held across every read-modify-write of a user document.
    /// The document store is plain files with no locking or optimistic concurrency: two overlapping
    /// requests for the same account can collide on the write, and a read that lands mid-write gets
    /// truncated JSON, which <c>FileService.Retrieve</c> swallows and returns as null. That surfaces as
    /// a perfectly valid verification link being reported as invalid. Not hypothetical -- mail clients
    /// prefetch links and users double-click them.
    /// </summary>
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new();

    public async Task Register(RegisterRequestModel request)
    {
        var email = UserDocument.Normalize(request?.Email);

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            throw new ArgumentException("A valid email is required.");
        }

        if (string.IsNullOrEmpty(request.Password) || request.Password.Length < 8)
        {
            throw new ArgumentException("Password must be at least 8 characters.");
        }

        // Locked so two simultaneous signups for the same address cannot both pass the existence check.
        var outcome = await WithUserLock(email, async () =>
        {
            var existing = await fileService.Retrieve(new UserDocument { Email = email });

            if (existing != null)
            {
                return await Reissue(existing, request.Password);
            }

            var user = new UserDocument
            {
                UserId = Guid.NewGuid().ToString(),
                Email = email,
                Name = string.IsNullOrWhiteSpace(request.Name) ? email : request.Name.Trim(),
                CreatedAt = DateTime.UtcNow,
                At = DateTime.UtcNow
            };

            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

            await IssueVerification(user);

            return RegisterOutcome.Created;
        });

        if (outcome == RegisterOutcome.AlreadyExists)
        {
            throw new ArgumentException("An account with that email already exists.");
        }
    }

    /// <summary>
    /// Handles registering an address that already has an account. An unverified one is nearly always a
    /// signup whose email never arrived -- the send can fail after the document is written, and the
    /// document store has no transaction to roll that back -- so this re-sends for the same account
    /// instead of leaving the person at a dead end.
    /// </summary>
    /// <remarks>
    /// Two constraints make this safe to do:
    ///
    /// The password must already match. Otherwise anyone could trigger verification mail at an address
    /// with a pending signup.
    ///
    /// The stored hash is never replaced. Letting a second registration set the password would mean a
    /// stranger could re-register someone else's pending address, and the moment the real owner clicked
    /// the link sitting in their own inbox, the account would be verified against the stranger's
    /// password. The link is not the credential -- the hash on the document is.
    /// </remarks>
    private async Task<RegisterOutcome> Reissue(UserDocument existing, string password)
    {
        if (existing.EmailVerified || existing.PasswordHash == null)
        {
            return RegisterOutcome.AlreadyExists;
        }

        if (passwordHasher.VerifyHashedPassword(existing, existing.PasswordHash, password) == PasswordVerificationResult.Failed)
        {
            return RegisterOutcome.AlreadyExists;
        }

        // Same throttle the resend endpoint uses, so this cannot become an unmetered way to send mail
        // at an address. The previous link is still live, so answering as if it had sent is honest.
        if (existing.VerificationSentAt.HasValue
            && DateTime.UtcNow - existing.VerificationSentAt.Value < ResendInterval)
        {
            logger.LogInformation("Throttled a re-registration resend for {Email}", existing.Email);
            return RegisterOutcome.Reissued;
        }

        await IssueVerification(existing);

        logger.LogInformation("Re-sent verification for the unverified account {Email}", existing.Email);

        return RegisterOutcome.Reissued;
    }

    public async Task<(LoginOutcome, AuthResponseModel)> Login(LoginRequestModel request)
    {
        var email = UserDocument.Normalize(request?.Email);

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(request.Password))
        {
            return (LoginOutcome.InvalidCredentials, null);
        }

        var user = await fileService.Retrieve(new UserDocument { Email = email });

        if (user?.PasswordHash == null)
        {
            return (LoginOutcome.InvalidCredentials, null);
        }

        var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);

        if (result == PasswordVerificationResult.Failed)
        {
            return (LoginOutcome.InvalidCredentials, null);
        }

        // Checked only after the password, so this cannot be used to probe which addresses are registered.
        if (!user.EmailVerified)
        {
            return (LoginOutcome.EmailNotVerified, null);
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
            await fileService.Upsert(user);
        }

        return (LoginOutcome.Success, Respond(user));
    }

    public async Task<bool> Verify(string email, string token)
    {
        var normalized = UserDocument.Normalize(email);

        if (string.IsNullOrWhiteSpace(normalized) || string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        return await WithUserLock(normalized, async () =>
        {
            var user = await fileService.Retrieve(new UserDocument { Email = normalized });

            // Every failure below returns the same false. Because the token is unguessable, an attacker
            // probing a known address gets the identical answer whether or not the account exists.
            if (user?.VerificationTokenHash == null)
            {
                return false;
            }

            if (user.VerificationTokenExpiresAt < DateTime.UtcNow)
            {
                return false;
            }

            if (!FixedTimeEquals(user.VerificationTokenHash, Hash(token)))
            {
                return false;
            }

            if (user.EmailVerified)
            {
                // Already done -- a double-clicked link should not look like a broken one.
                return true;
            }

            user.EmailVerified = true;
            user.At = DateTime.UtcNow;

            // The hash is deliberately left in place until it expires, so following the link twice
            // succeeds twice. It grants nothing once the account is verified.
            await fileService.Upsert(user);

            logger.LogInformation("Verified email for {Email}", user.Email);

            return true;
        });
    }

    public async Task ResendVerification(string email)
    {
        var normalized = UserDocument.Normalize(email);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        await WithUserLock(normalized, async () =>
        {
            var user = await fileService.Retrieve(new UserDocument { Email = normalized });

            if (user == null || user.EmailVerified)
            {
                return false;
            }

            if (user.VerificationSentAt.HasValue
                && DateTime.UtcNow - user.VerificationSentAt.Value < ResendInterval)
            {
                logger.LogInformation("Throttled a resend for {Email}", user.Email);
                return false;
            }

            await IssueVerification(user);

            return true;
        });
    }

    public async Task<UserResponseModel> GetByEmail(string email)
    {
        var user = await fileService.Retrieve(new UserDocument { Email = UserDocument.Normalize(email) });

        return user == null ? null : new UserResponseModel
        {
            UserId = user.UserId,
            Email = user.Email,
            Name = user.Name,
            EmailVerified = user.EmailVerified
        };
    }

    /// <summary>The signing key, also used by the JwtBearer handler in <c>Startup</c>.</summary>
    public static SymmetricSecurityKey GetSigningKey()
    {
        var secret = EnvironmentHelper.GetVar("JWT_SECRET");

        if (string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) < 32)
        {
            throw new InvalidOperationException("JWT_SECRET must be set and at least 32 bytes long.");
        }

        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
    }

    /// <summary>Mints a fresh token, persists only its hash, and emails the link.</summary>
    /// <remarks>
    /// The document has to be written before the send, or a link followed from a fast inbox could beat
    /// its own token to storage. When the send then fails, that write is rolled back by hand -- there is
    /// no transaction to do it, and the caller is always holding this account's lock.
    ///
    /// Rolling back matters twice over. Leaving the new timestamp would arm the resend throttle on a
    /// message that never left, so the retry that is supposed to recover would answer "check your inbox"
    /// with nothing behind it. Leaving the new hash would invalidate a working link from an earlier,
    /// successful send in favour of one nobody ever received.
    /// </remarks>
    private async Task IssueVerification(UserDocument user)
    {
        var token = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

        var previousHash = user.VerificationTokenHash;
        var previousExpiry = user.VerificationTokenExpiresAt;
        var previousSentAt = user.VerificationSentAt;

        user.VerificationTokenHash = Hash(token);
        user.VerificationTokenExpiresAt = DateTime.UtcNow.Add(VerificationLifetime);
        user.VerificationSentAt = DateTime.UtcNow;
        user.At = DateTime.UtcNow;

        await fileService.Upsert(user);

        var appUrl = (EnvironmentHelper.GetVar("APP_URL") ?? "http://localhost:5173").TrimEnd('/');
        var url = $"{appUrl}/verify?email={Uri.EscapeDataString(user.Email)}&token={Uri.EscapeDataString(token)}";

        try
        {
            await emailService.SendVerification(user, url);
        }
        catch
        {
            user.VerificationTokenHash = previousHash;
            user.VerificationTokenExpiresAt = previousExpiry;
            user.VerificationSentAt = previousSentAt;
            user.At = DateTime.UtcNow;

            await fileService.Upsert(user);

            throw;
        }
    }

    /// <summary>Serializes read-modify-write on a single account. See <see cref="UserLocks"/>.</summary>
    private static async Task<T> WithUserLock<T>(string email, Func<Task<T>> action)
    {
        var gate = UserLocks.GetOrAdd(email, _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync();

        try
        {
            return await action();
        }
        finally
        {
            gate.Release();
        }
    }

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a ?? string.Empty),
            Encoding.UTF8.GetBytes(b ?? string.Empty));

    private static AuthResponseModel Respond(UserDocument user) => new()
    {
        Token = CreateToken(user),
        User = new UserResponseModel
        {
            UserId = user.UserId,
            Email = user.Email,
            Name = user.Name,
            EmailVerified = user.EmailVerified
        }
    };

    private static string CreateToken(UserDocument user)
    {
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, user.UserId),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Name, user.Name)
            ],
            expires: DateTime.UtcNow.Add(TokenLifetime),
            signingCredentials: new SigningCredentials(GetSigningKey(), SecurityAlgorithms.HmacSha256)
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

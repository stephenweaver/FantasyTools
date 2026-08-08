using FantasyTools.Api.HttpClients;
using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;

namespace FantasyTools.Api.Services;

public interface ITurnstileService
{
    Task<bool> Verify(string token, string remoteIp);
}

public class TurnstileService(ITurnstileHttpClient client, ILogger<TurnstileService> logger) : ITurnstileService
{
    public async Task<bool> Verify(string token, string remoteIp)
    {
        if (!IsEnabled)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            var result = await client.Post<SiteVerifyResponse>("turnstile/v0/siteverify", new
            {
                secret = GetSecretKey(),
                response = token,
                remoteip = remoteIp
            });

            if (result?.Success != true)
            {
                logger.LogWarning("Turnstile rejected a token: {Errors}",
                    string.Join(", ", result?.ErrorCodes ?? ["no response"]));
            }

            return result?.Success == true;
        }
        catch (Exception ex)
        {
            // Fail closed: if Cloudflare is unreachable we do not silently wave traffic through.
            logger.LogError(ex, "Turnstile verification call failed.");
            return false;
        }
    }

    /// <summary>
    /// Captcha is on unless TURNSTILE_ENABLED is explicitly "false". Turnstile deliberately refuses to
    /// auto-solve for an automated browser, so the e2e suite switches it off rather than depending on
    /// Cloudflare being reachable. Requiring the literal string means a missing or misspelled variable
    /// leaves the captcha ON.
    /// </summary>
    public static bool IsEnabled =>
        !string.Equals(EnvironmentHelper.GetVar("TURNSTILE_ENABLED"), "false", StringComparison.OrdinalIgnoreCase);

    public static string GetSiteKey() => IsEnabled ? EnvironmentHelper.GetVar("TURNSTILE_SITE_KEY") : null;

    public static string GetSecretKey()
    {
        var secret = EnvironmentHelper.GetVar("TURNSTILE_SECRET_KEY");

        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException(
                "TURNSTILE_SECRET_KEY must be set. Set TURNSTILE_ENABLED=false to run without a captcha.");
        }

        return secret;
    }

    private class SiteVerifyResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("error-codes")]
        public List<string> ErrorCodes { get; set; }
    }
}

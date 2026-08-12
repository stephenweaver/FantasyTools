using FantasyTools.Api.Documents;
using FantasyTools.Api.HttpClients;
using Microsoft.Extensions.Logging;
using System.Text;

namespace FantasyTools.Api.Services;

public interface IEmailService
{
    Task SendVerification(UserDocument user, string verificationUrl);
}

/// <summary>
/// The message did not get out. Distinct from the transport's own exception type so callers can answer
/// "mail is down" without knowing or caring which provider is configured.
/// </summary>
public class EmailDeliveryException(string message, Exception inner) : Exception(message, inner);

/// <summary>
/// Sends through Resend and/or writes the rendered message to a local outbox folder.
/// </summary>
/// <remarks>
/// MAIL_TRANSPORT picks the behaviour: <c>resend</c>, <c>outbox</c>, or <c>both</c>. It defaults to
/// resend when RESEND_API_KEY is set and outbox otherwise, so a fresh clone with no mail credentials
/// still completes the verification loop. Locally it is pinned to outbox so test runs do not push mail
/// at throwaway addresses through the real account.
///
/// The Common package still registers its MailerSend client -- that is shared with StockScreener and is
/// simply unused here. Nothing in this repo resolves IMailerSendService.
/// </remarks>
public class EmailService(
    IResendHttpClient resendHttpClient,
    ILogger<EmailService> logger
    ) : IEmailService
{
    public async Task SendVerification(UserDocument user, string verificationUrl)
    {
        var subject = "Verify your email";

        var text =
            $"Hi {user.Name},\r\n\r\n" +
            $"Confirm your email address to finish setting up your FantasyTools account:\r\n\r\n" +
            $"{verificationUrl}\r\n\r\n" +
            "This link expires in 24 hours. If you did not sign up, you can ignore this email.";

        var html =
            $"<p>Hi {System.Net.WebUtility.HtmlEncode(user.Name)},</p>" +
            "<p>Confirm your email address to finish setting up your FantasyTools account:</p>" +
            $"<p><a href=\"{verificationUrl}\">Verify my email</a></p>" +
            $"<p>Or paste this into your browser:<br/>{verificationUrl}</p>" +
            "<p>This link expires in 24 hours. If you did not sign up, you can ignore this email.</p>";

        var transport = GetTransport();

        if (transport is "outbox" or "both")
        {
            await WriteToOutbox(user, subject, text);
        }

        if (transport is "resend" or "both")
        {
            await SendViaResend(user, subject, html, text);
        }
    }

    private async Task SendViaResend(UserDocument user, string subject, string html, string text)
    {
        string messageId;

        try
        {
            messageId = await resendHttpClient.SendEmail(GetFrom(), user.Email, subject, html, text);
        }
        catch (Exception ex)
        {
            // Rethrown as one type so AuthController can answer 503 without referencing Resend. The
            // provider's own wording is kept as the inner exception -- it is usually the whole answer.
            throw new EmailDeliveryException($"Could not send the verification email to {user.Email}.", ex);
        }

        logger.LogInformation("Sent verification email to {Email} (message {MessageId})", user.Email, messageId);
    }

    /// <summary>
    /// Resend takes a single from string. The address must be on a domain verified in the Resend
    /// dashboard -- an unverified one is a 403 at send time, not a configuration error caught earlier.
    /// </summary>
    private static string GetFrom()
    {
        var email = EnvironmentHelper.GetVar("MAIL_FROM_EMAIL");
        var name = EnvironmentHelper.GetVar("MAIL_FROM_NAME");

        return string.IsNullOrWhiteSpace(name) ? email : $"{name} <{email}>";
    }

    private async Task WriteToOutbox(UserDocument user, string subject, string text)
    {
        var folder = GetOutboxFolder();

        Directory.CreateDirectory(folder);

        // One file per address, overwritten each send, so a resend is easy to read back.
        var path = Path.Combine(folder, $"{UserDocument.Normalize(user.Email)}.txt");

        var contents = new StringBuilder()
            .AppendLine($"To: {user.Email}")
            .AppendLine($"Subject: {subject}")
            .AppendLine()
            .AppendLine(text)
            .ToString();

        await File.WriteAllTextAsync(path, contents);

        logger.LogInformation("Wrote verification email for {Email} to {Path}", user.Email, path);
    }

    private static string GetOutboxFolder() =>
        EnvironmentHelper.GetVar("MAIL_OUTBOX_FOLDER") ?? @"C:\FantasyTools\Outbox";

    private static string GetTransport()
    {
        var configured = EnvironmentHelper.GetVar("MAIL_TRANSPORT")?.Trim().ToLowerInvariant();

        if (configured is "outbox" or "resend" or "both")
        {
            return configured;
        }

        // Anything unrecognised -- including a leftover "mailersend" -- falls through to the default
        // rather than silently sending nothing.
        return string.IsNullOrWhiteSpace(EnvironmentHelper.GetVar("RESEND_API_KEY"))
            ? "outbox"
            : "resend";
    }
}

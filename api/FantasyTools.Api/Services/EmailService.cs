using FantasyTools.Api.Documents;
using Microsoft.Extensions.Logging;
using StephenWeaver.Common.Model.MailerSend;
using System.Text;

namespace FantasyTools.Api.Services;

public interface IEmailService
{
    Task SendVerification(UserDocument user, string verificationUrl);
}

/// <summary>
/// Sends through MailerSend (already registered by RegisterStephenWeaverCommon) and/or writes the
/// rendered message to a local outbox folder.
/// </summary>
/// <remarks>
/// MAIL_TRANSPORT picks the behaviour: <c>mailersend</c>, <c>outbox</c>, or <c>both</c>. It defaults to
/// mailersend when MAILERSEND_API_KEY is set and outbox otherwise, so a fresh clone with no mail
/// credentials still completes the verification loop. Locally it is pinned to outbox so test runs do not
/// push mail at throwaway addresses through the real account.
/// </remarks>
public class EmailService(
    IMailerSendService mailerSendService,
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

        if (transport is "mailersend" or "both")
        {
            await SendViaMailerSend(user, subject, html, text);
        }
    }

    private async Task SendViaMailerSend(UserDocument user, string subject, string html, string text)
    {
        var request = new SendEmailRequestModel
        {
            From = new From
            {
                Email = EnvironmentHelper.GetVar("MAIL_FROM_EMAIL"),
                Name = EnvironmentHelper.GetVar("MAIL_FROM_NAME") ?? "FantasyTools"
            },
            To = [new To { Email = user.Email, Name = user.Name }],
            Subject = subject,
            Html = html,
            Text = text
        };

        var response = await mailerSendService.SendEmail(request);

        logger.LogInformation("Sent verification email to {Email} (message {MessageId})",
            user.Email, response?.MessageId);
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

        if (configured is "outbox" or "mailersend" or "both")
        {
            return configured;
        }

        return string.IsNullOrWhiteSpace(EnvironmentHelper.GetVar("MAILERSEND_API_KEY"))
            ? "outbox"
            : "mailersend";
    }
}

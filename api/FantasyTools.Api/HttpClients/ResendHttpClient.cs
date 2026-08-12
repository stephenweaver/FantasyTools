using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FantasyTools.Api.HttpClients;

public interface IResendHttpClient : IHttpClientBase
{
    /// <summary>Sends one message and returns Resend's id for it.</summary>
    Task<string> SendEmail(string from, string to, string subject, string html, string text);
}

/// <summary>
/// Resend's send endpoint. Unlike <see cref="TurnstileHttpClient"/> this does not use the base class's
/// Post: Resend reports a rejected message as a 4xx whose body carries the reason, and
/// EnsureSuccessStatusCode discards that body. A verification email that never sends locks a user out
/// of their own account, so the reason is worth keeping.
/// </summary>
public class ResendHttpClient : HttpClientBase, IResendHttpClient
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public ResendHttpClient(HttpClient httpClient)
        : base(httpClient)
    {
        HttpClient.BaseAddress = new Uri("https://api.resend.com");
    }

    public async Task<string> SendEmail(string from, string to, string subject, string html, string text)
    {
        var payload = JsonSerializer.Serialize(new SendRequest
        {
            From = from,
            To = [to],
            Subject = subject,
            Html = html,
            Text = text
        }, Options);

        var request = new HttpRequestMessage(HttpMethod.Post, "emails")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GetApiKey());

        var response = await HttpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Resend rejected the message ({(int)response.StatusCode}): {body}");
        }

        return JsonSerializer.Deserialize<SendResponse>(body)?.Id;
    }

    public static string GetApiKey()
    {
        var key = EnvironmentHelper.GetVar("RESEND_API_KEY");

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                "RESEND_API_KEY must be set to send mail. Set MAIL_TRANSPORT=outbox to write messages to a folder instead.");
        }

        return key;
    }

    /// <summary>
    /// Resend takes <c>from</c> as one string, not a name/email pair: "Fantasy Tools &lt;noreply@…&gt;".
    /// A bare address is valid too, which is what an unset MAIL_FROM_NAME falls back to.
    /// </summary>
    private class SendRequest
    {
        public string From { get; set; }
        public List<string> To { get; set; }
        public string Subject { get; set; }
        public string Html { get; set; }
        public string Text { get; set; }
    }

    private class SendResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }
    }
}

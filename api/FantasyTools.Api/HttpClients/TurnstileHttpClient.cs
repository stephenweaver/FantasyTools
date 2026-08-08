namespace FantasyTools.Api.HttpClients;

public interface ITurnstileHttpClient : IHttpClientBase
{
}

/// <summary>
/// Cloudflare's siteverify endpoint. Reuses <see cref="HttpClientBase"/> from StephenWeaver.Common,
/// which posts JSON -- siteverify accepts that and answers 200 even when the token is rejected,
/// so the base class's EnsureSuccessStatusCode does not get in the way.
/// </summary>
public class TurnstileHttpClient : HttpClientBase, ITurnstileHttpClient
{
    public TurnstileHttpClient(HttpClient httpClient)
        : base(httpClient)
    {
        HttpClient.BaseAddress = new Uri("https://challenges.cloudflare.com");
    }
}

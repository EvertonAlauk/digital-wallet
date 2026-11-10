using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace DigitalWallet.BffTests.Infrastructure;

/// <summary>
/// Stands in for Keycloak's token and logout endpoints. Records every request, so tests can check exactly which
/// credentials and grant the BFF sent. No network is used.
/// </summary>
public sealed class FakeKeycloak : HttpMessageHandler
{
    private readonly List<RecordedRequest> _requests = new();

    public Func<RecordedRequest, HttpResponseMessage> Respond { get; set; } = _ => Json(HttpStatusCode.OK, Tokens());

    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_requests)
            {
                return _requests.ToList();
            }
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var form = request.Content is null
            ? new Dictionary<string, string>()
            : ParseForm(await request.Content.ReadAsStringAsync(cancellationToken));

        var recorded = new RecordedRequest(request.RequestUri!.AbsolutePath, form);
        lock (_requests)
        {
            _requests.Add(recorded);
        }

        return Respond(recorded);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, object body) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
    };

    public static object Tokens(string access = "access-token-1", string refresh = "refresh-token-1") => new
    {
        access_token = access,
        refresh_token = refresh,
        expires_in = 300,
        token_type = "Bearer",
    };

    public static object Error(string code) => new { error = code };

    private static Dictionary<string, string> ParseForm(string body)
    {
        var values = new Dictionary<string, string>();
        foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            values[Uri.UnescapeDataString(parts[0])] = parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : string.Empty;
        }

        return values;
    }
}

public sealed record RecordedRequest(string Path, IReadOnlyDictionary<string, string> Form);

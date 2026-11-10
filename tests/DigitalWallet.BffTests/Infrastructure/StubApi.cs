using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Yarp.ReverseProxy.Forwarder;

namespace DigitalWallet.BffTests.Infrastructure;

/// <summary>
/// Stands in for DigitalWallet.Api behind the proxy. It records what the proxy sent, so tests can check the Bearer
/// token, the removed cookie and the rewritten path. No network is used.
/// </summary>
public sealed class StubApi : HttpMessageHandler
{
    private readonly ConcurrentQueue<RecordedApiRequest> _requests = new();

    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => Json(HttpStatusCode.OK, new { ok = true });

    public IReadOnlyCollection<RecordedApiRequest> Requests => _requests.ToArray();

    public void Reset()
    {
        _requests.Clear();
        Respond = _ => Json(HttpStatusCode.OK, new { ok = true });
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _requests.Enqueue(new RecordedApiRequest(
            request.Method.Method,
            request.RequestUri!.PathAndQuery,
            request.Headers.Authorization?.ToString(),
            request.Headers.Contains("Cookie")));

        return Task.FromResult(Respond(request));
    }

    public static HttpResponseMessage Json(HttpStatusCode status, object body) => new(status)
    {
        Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
    };
}

/// <summary>What the stub API received from the proxy.</summary>
public sealed record RecordedApiRequest(string Method, string PathAndQuery, string? Authorization, bool HasCookie);

/// <summary>Makes the proxy send its requests to the stub instead of a real HTTP connection.</summary>
public sealed class StubApiForwarderFactory(HttpMessageHandler handler) : IForwarderHttpClientFactory
{
    public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(handler, disposeHandler: false);
}

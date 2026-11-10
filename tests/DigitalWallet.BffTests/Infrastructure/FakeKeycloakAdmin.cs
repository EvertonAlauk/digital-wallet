using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace DigitalWallet.BffTests.Infrastructure;

/// <summary>
/// Stands in for Keycloak's token endpoint and Admin REST API. Records each request with its bearer token and body.
/// Tests change <see cref="Respond"/> to simulate a failure at one step. No network is used.
/// </summary>
public sealed class FakeKeycloakAdmin : HttpMessageHandler
{
    public const string AdminToken = "admin-token-for-tests";
    public const string NewUserId = "7f0c1a2e-0000-4000-8000-00000000abcd";
    public const string UserRoleId = "role-user-id";

    private readonly List<RecordedAdminRequest> _requests = [];

    public Func<RecordedAdminRequest, HttpResponseMessage> Respond { get; set; } = Default;

    public IReadOnlyList<RecordedAdminRequest> Requests
    {
        get
        {
            lock (_requests)
            {
                return _requests.ToList();
            }
        }
    }

    public void Reset()
    {
        lock (_requests)
        {
            _requests.Clear();
        }

        Respond = Default;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var recorded = new RecordedAdminRequest(
            request.Method.Method,
            request.RequestUri!.AbsolutePath,
            request.Headers.Authorization?.ToString(),
            body);

        lock (_requests)
        {
            _requests.Add(recorded);
        }

        return Respond(recorded);
    }

    /// <summary>The successful path: the token is issued, the user is created, the role exists and is granted.</summary>
    public static HttpResponseMessage Default(RecordedAdminRequest request)
    {
        if (request.Method == "POST" && request.Path.EndsWith("/protocol/openid-connect/token", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.OK, new { access_token = AdminToken, token_type = "Bearer", expires_in = 60 });
        }

        if (request.Method == "POST" && request.Path.EndsWith("/admin/realms/digital-wallet/users", StringComparison.Ordinal))
        {
            var created = new HttpResponseMessage(HttpStatusCode.Created);
            created.Headers.Location = new Uri($"http://keycloak.test/admin/realms/digital-wallet/users/{NewUserId}");
            return created;
        }

        if (request.Method == "GET" && request.Path.EndsWith("/admin/realms/digital-wallet/roles/user", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.OK, new { id = UserRoleId, name = "user" });
        }

        if (request.Method == "POST" && request.Path.EndsWith("/role-mappings/realm", StringComparison.Ordinal))
        {
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        if (request.Method == "DELETE")
        {
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, object body) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
    };
}

public sealed record RecordedAdminRequest(string Method, string Path, string? Authorization, string Body);

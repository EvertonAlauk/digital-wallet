using System.Net;
using System.Text.Json;
using DigitalWallet.Bff.Auth;
using DigitalWallet.Bff.Identity;
using DigitalWallet.Bff.Keycloak;
using DigitalWallet.BffTests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace DigitalWallet.BffTests;

public class KeycloakUserRegistrationServiceTests
{
    private readonly FakeKeycloakAdmin _keycloak = new();
    private readonly KeycloakUserRegistrationService _service;

    private static readonly NewUserAccount Account = new("alice@example.com", "a-strong-password", "Alice", "Customer");

    public KeycloakUserRegistrationServiceTests()
    {
        var options = Options.Create(new KeycloakOptions
        {
            TokenEndpoint = "http://keycloak.test/realms/digital-wallet/protocol/openid-connect/token",
            LogoutEndpoint = "http://keycloak.test/realms/digital-wallet/protocol/openid-connect/logout",
            ClientId = "digital-wallet-bff",
            ClientSecret = "bff-secret",
            RegistrarClientId = "digital-wallet-registrar",
            RegistrarClientSecret = "registrar-secret",
            AdminBaseUrl = "http://keycloak.test/admin/realms/digital-wallet",
        });
        _service = new KeycloakUserRegistrationService(new HttpClient(_keycloak), options);
    }

    [Fact]
    public async Task Register_CreatesTheUserAndGrantsTheUserRole()
    {
        var result = await _service.RegisterAsync(Account, CancellationToken.None);

        result.Outcome.Should().Be(RegistrationOutcome.Created);
        _keycloak.Requests.Select(request => $"{request.Method} {request.Path}").Should().Equal(
            "POST /realms/digital-wallet/protocol/openid-connect/token",
            "POST /admin/realms/digital-wallet/users",
            "GET /admin/realms/digital-wallet/roles/user",
            $"POST /admin/realms/digital-wallet/users/{FakeKeycloakAdmin.NewUserId}/role-mappings/realm");
    }

    [Fact]
    public async Task Register_AuthenticatesWithTheRegistrarClientCredentials()
    {
        await _service.RegisterAsync(Account, CancellationToken.None);

        var token = _keycloak.Requests.First();
        token.Body.Should().Contain("grant_type=client_credentials");
        token.Body.Should().Contain("client_id=digital-wallet-registrar");
        token.Body.Should().NotContain("bff-secret", "the BFF's own client is not the registrar");
    }

    [Fact]
    public async Task Register_SendsTheAccountWithTheEmailAsUsernameAndAPasswordCredential()
    {
        await _service.RegisterAsync(Account, CancellationToken.None);

        var create = _keycloak.Requests.Single(request => request.Path.EndsWith("/users", StringComparison.Ordinal));
        create.Authorization.Should().Be($"Bearer {FakeKeycloakAdmin.AdminToken}");
        using var json = JsonDocument.Parse(create.Body);
        var root = json.RootElement;
        root.GetProperty("username").GetString().Should().Be("alice@example.com");
        root.GetProperty("email").GetString().Should().Be("alice@example.com");
        root.GetProperty("enabled").GetBoolean().Should().BeTrue();
        root.GetProperty("emailVerified").GetBoolean().Should().BeFalse("there is no mail server to verify it");
        var credential = root.GetProperty("credentials")[0];
        credential.GetProperty("type").GetString().Should().Be("password");
        credential.GetProperty("value").GetString().Should().Be("a-strong-password");
        credential.GetProperty("temporary").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Register_GrantsTheRoleWithTheIdFromTheLocationHeader()
    {
        await _service.RegisterAsync(Account, CancellationToken.None);

        var mapping = _keycloak.Requests.Last();
        mapping.Path.Should().Contain(FakeKeycloakAdmin.NewUserId);
        mapping.Body.Should().Contain(FakeKeycloakAdmin.UserRoleId);
    }

    [Fact]
    public async Task Register_EmailAlreadyUsed_ReturnsAlreadyExistsAndGrantsNothing()
    {
        _keycloak.Respond = request => request.Path.EndsWith("/users", StringComparison.Ordinal) && request.Method == "POST"
            ? new HttpResponseMessage(HttpStatusCode.Conflict)
            : FakeKeycloakAdmin.Default(request);

        var result = await _service.RegisterAsync(Account, CancellationToken.None);

        result.Outcome.Should().Be(RegistrationOutcome.AlreadyExists);
        _keycloak.Requests.Should().NotContain(request => request.Path.Contains("role-mappings"));
    }

    [Fact]
    public async Task Register_PasswordRefusedByThePolicy_ReturnsRejected()
    {
        _keycloak.Respond = request => request.Path.EndsWith("/users", StringComparison.Ordinal) && request.Method == "POST"
            ? FakeKeycloakAdmin.Json(HttpStatusCode.BadRequest, new { errorMessage = "Password policy not met" })
            : FakeKeycloakAdmin.Default(request);

        var result = await _service.RegisterAsync(Account, CancellationToken.None);

        result.Outcome.Should().Be(RegistrationOutcome.Rejected);
    }

    [Fact]
    public async Task Register_RegistrarTokenRefused_ReturnsMisconfigured_AndCreatesNothing()
    {
        _keycloak.Respond = request => request.Path.EndsWith("/protocol/openid-connect/token", StringComparison.Ordinal)
            ? FakeKeycloakAdmin.Json(HttpStatusCode.Unauthorized, new { error = "unauthorized_client" })
            : FakeKeycloakAdmin.Default(request);

        var result = await _service.RegisterAsync(Account, CancellationToken.None);

        result.Outcome.Should().Be(RegistrationOutcome.Misconfigured);
        _keycloak.Requests.Should().HaveCount(1, "only the token request is made");
    }

    [Fact]
    public async Task Register_RegistrarLacksPermission_ReturnsMisconfigured()
    {
        _keycloak.Respond = request => request.Path.EndsWith("/users", StringComparison.Ordinal) && request.Method == "POST"
            ? new HttpResponseMessage(HttpStatusCode.Forbidden)
            : FakeKeycloakAdmin.Default(request);

        var result = await _service.RegisterAsync(Account, CancellationToken.None);

        result.Outcome.Should().Be(RegistrationOutcome.Misconfigured);
    }

    [Fact]
    public async Task Register_RoleCannotBeGranted_RemovesTheNewUser_AndReturnsUnavailable()
    {
        _keycloak.Respond = request => request.Method == "POST" && request.Path.EndsWith("/role-mappings/realm", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : FakeKeycloakAdmin.Default(request);

        var result = await _service.RegisterAsync(Account, CancellationToken.None);

        result.Outcome.Should().Be(RegistrationOutcome.Unavailable);
        _keycloak.Requests.Should().ContainSingle(request => request.Method == "DELETE")
            .Which.Path.Should().EndWith(FakeKeycloakAdmin.NewUserId);
    }

    [Fact]
    public async Task Register_KeycloakUnreachable_ReturnsUnavailable()
    {
        _keycloak.Respond = _ => throw new HttpRequestException("connection refused");

        var result = await _service.RegisterAsync(Account, CancellationToken.None);

        result.Outcome.Should().Be(RegistrationOutcome.Unavailable);
    }

    [Fact]
    public async Task Register_KeycloakTimesOut_ReturnsUnavailable()
    {
        _keycloak.Respond = _ => throw new TaskCanceledException("timeout");

        var result = await _service.RegisterAsync(Account, CancellationToken.None);

        result.Outcome.Should().Be(RegistrationOutcome.Unavailable);
    }

    [Fact]
    public void NewUserAccount_DoesNotPrintThePassword()
    {
        Account.ToString().Should().NotContain("a-strong-password");
    }
}

public class RegistrationRulesTests
{
    [Fact]
    public void ValidForm_ProducesTheAccountWithLowerCaseEmail()
    {
        var (account, errors) = RegistrationRules.Check("  Alice@Example.COM ", "a-strong-password", " Alice ", "Customer");

        errors.Should().BeEmpty();
        account!.Email.Should().Be("alice@example.com");
        account.FirstName.Should().Be("Alice");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("two@@example.com")]
    public void InvalidEmail_IsReported(string email)
    {
        var (account, errors) = RegistrationRules.Check(email, "a-strong-password", "Alice", "Customer");

        account.Should().BeNull();
        errors.Should().ContainKey("email");
    }

    [Fact]
    public void ShortPassword_IsReported()
    {
        var (_, errors) = RegistrationRules.Check("alice@example.com", "short", "Alice", "Customer");

        errors.Should().ContainKey("password");
    }

    [Fact]
    public void MissingNames_AreReported()
    {
        var (_, errors) = RegistrationRules.Check("alice@example.com", "a-strong-password", " ", null);

        errors.Should().ContainKeys("firstName", "lastName");
    }
}

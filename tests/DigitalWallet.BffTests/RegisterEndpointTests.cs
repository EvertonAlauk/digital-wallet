using System.Net;
using System.Net.Http.Json;
using System.Text;
using DigitalWallet.Bff.Identity;
using DigitalWallet.BffTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DigitalWallet.BffTests;

public class RegisterEndpointTests : IClassFixture<BffFactory>
{
    private readonly BffFactory factory;
    private readonly HttpClient _http;

    public RegisterEndpointTests(BffFactory factory)
    {
        this.factory = factory;
        this.factory.Registration.Reset();
        _http = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
        });
    }

    [Fact]
    public async Task Register_NewAccount_Returns201_AndDoesNotSignIn()
    {
        var response = await SendAsync(Form("Alice@Example.com", "a-strong-password"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        SessionCookie.SetCookieOf(response).Should().BeNull("registering does not sign the user in");
    }

    [Fact]
    public async Task Register_StoresTheEmailInLowerCase_AsTheUsername()
    {
        await SendAsync(Form("Alice@Example.com", "a-strong-password"));

        factory.Registration.Accounts.Should().ContainSingle()
            .Which.Email.Should().Be("alice@example.com");
    }

    [Fact]
    public async Task Register_ResponseNeverContainsThePassword()
    {
        var response = await SendAsync(Form("bob@example.com", "a-strong-password"));

        (await response.Content.ReadAsStringAsync()).Should().NotContain("a-strong-password");
    }

    [Fact]
    public async Task Register_EmailAlreadyUsed_Returns409WithProblemDetails()
    {
        factory.Registration.Outcome = _ => RegistrationOutcome.AlreadyExists;

        var response = await SendAsync(Form("taken@example.com", "a-strong-password"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Register_PasswordRefused_Returns400()
    {
        factory.Registration.Outcome = _ => RegistrationOutcome.Rejected;

        var response = await SendAsync(Form("carol@example.com", "a-strong-password"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Register_IdentityProviderDown_Returns503()
    {
        factory.Registration.Outcome = _ => RegistrationOutcome.Unavailable;

        var response = await SendAsync(Form("dave@example.com", "a-strong-password"));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Register_RegistrarMisconfigured_Returns500()
    {
        factory.Registration.Outcome = _ => RegistrationOutcome.Misconfigured;

        var response = await SendAsync(Form("erin@example.com", "a-strong-password"));

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task Register_InvalidForm_Returns400WithFieldErrors_AndCallsNothing()
    {
        var response = await SendAsync(Form("not-an-email", "short"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("email").And.Contain("password");
        factory.Registration.Accounts.Should().BeEmpty();
    }

    [Fact]
    public async Task Register_WithoutTheCsrfHeader_Returns403_AndCallsNothing()
    {
        var response = await SendAsync(Form("frank@example.com", "a-strong-password"), csrf: false);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        factory.Registration.Accounts.Should().BeEmpty();
    }

    [Fact]
    public async Task Register_WithANonJsonBody_Returns415()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/bff/register")
        {
            Content = new StringContent("email=x", Encoding.UTF8, "application/x-www-form-urlencoded"),
        };
        request.Headers.Add("X-CSRF", "1");

        var response = await _http.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
    }

    private static object Form(string email, string password) => new
    {
        email,
        password,
        firstName = "Test",
        lastName = "User",
    };

    private async Task<HttpResponseMessage> SendAsync(object body, bool csrf = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/bff/register")
        {
            Content = JsonContent.Create(body),
        };
        if (csrf)
        {
            request.Headers.Add("X-CSRF", "1");
        }

        return await _http.SendAsync(request);
    }
}

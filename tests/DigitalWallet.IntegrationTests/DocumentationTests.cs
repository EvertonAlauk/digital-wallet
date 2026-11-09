using System.Net;
using DigitalWallet.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DigitalWallet.IntegrationTests;

public class DocumentationTests(WalletApiFactory factory) : IClassFixture<WalletApiFactory>
{
    private readonly HttpClient _http = factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
    });

    [Fact]
    public async Task SwaggerUi_IsServedAsHtml()
    {
        var response = await _http.GetAsync("/swagger/index.html");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
    }

    [Fact]
    public async Task OpenApiDocument_DescribesTheAccountEndpoints()
    {
        var response = await _http.GetAsync("/openapi/v1.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var document = await response.Content.ReadAsStringAsync();
        document.Should().Contain("/accounts").And.Contain("/accounts/{id}/statement");
    }
}

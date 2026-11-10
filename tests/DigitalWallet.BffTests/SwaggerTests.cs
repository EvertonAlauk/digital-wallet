using System.Net;
using DigitalWallet.BffTests.Infrastructure;
using FluentAssertions;

namespace DigitalWallet.BffTests;

public class SwaggerTests
{
    [Fact]
    public async Task InDevelopment_OpenApiDocument_DescribesTheBffEndpoints()
    {
        await using var factory = new DevelopmentBffFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");
        var document = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        document.Should().Contain("/bff/login").And.Contain("/bff/session").And.Contain("/bff/logout");
    }

    [Fact]
    public async Task InDevelopment_SwaggerUi_IsAvailable()
    {
        await using var factory = new DevelopmentBffFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/index.html");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task InProduction_OpenApiDocument_IsNotPublished()
    {
        await using var factory = new ProductionBffFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task InProduction_SwaggerUi_IsNotPublished()
    {
        await using var factory = new ProductionBffFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/index.html");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private sealed class DevelopmentBffFactory : BffFactory
    {
        protected override string EnvironmentName => "Development";
    }

    private sealed class ProductionBffFactory : BffFactory
    {
        protected override string EnvironmentName => "Production";
    }
}

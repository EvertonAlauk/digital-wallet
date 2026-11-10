using System.Net;
using DigitalWallet.BffTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DigitalWallet.BffTests;

/// <summary>A frontend folder with a marker page, created once for the test run.</summary>
public static class FrontendFolder
{
    public const string Marker = "frontend-build-marker";

    public static readonly string Path = Create();

    private static string Create()
    {
        var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bff-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(System.IO.Path.Combine(folder, "index.html"), $"<!doctype html><title>Wallet</title><p>{Marker}</p>");
        return folder;
    }
}

public sealed class FrontendBffFactory : BffFactory
{
    protected override string? UiStaticFilesPath => FrontendFolder.Path;
}

public class StaticUiTests(FrontendBffFactory factory) : IClassFixture<FrontendBffFactory>
{
    private readonly HttpClient _http = factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
    });

    [Fact]
    public async Task Root_ServesTheFrontendIndexPage()
    {
        var response = await _http.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain(FrontendFolder.Marker);
    }

    [Fact]
    public async Task ClientSideRoute_FallsBackToTheIndexPage()
    {
        var response = await _http.GetAsync("/accounts/some-page");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain(FrontendFolder.Marker);
    }

    [Fact]
    public async Task BffRoutes_AreNotReplacedByTheFrontend()
    {
        var response = await _http.GetAsync("/bff/session");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).Should().NotContain(FrontendFolder.Marker);
    }
}

public sealed class FrontendDisabledFactory : BffFactory;

public class StaticUiDisabledTests(FrontendDisabledFactory factory) : IClassFixture<FrontendDisabledFactory>
{
    [Fact]
    public async Task ByDefault_TheBffDoesNotServeAFrontend()
    {
        var response = await factory.CreateClient().GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

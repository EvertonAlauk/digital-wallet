using System.Net;
using DigitalWallet.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace DigitalWallet.IntegrationTests;

/// <summary>Uses a factory with a limit of three requests per minute, so the limit is reached quickly.</summary>
public class RateLimitApiTests(LowRateLimitFactory factory) : IClassFixture<LowRateLimitFactory>
{
    [Fact]
    public async Task SameUser_BeyondTheLimit_Returns429()
    {
        var client = new ApiClient(factory);
        var unknownAccount = Guid.NewGuid();

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 4; attempt++)
        {
            statuses.Add((await client.GetAccountAsync(unknownAccount)).StatusCode);
        }

        statuses.Take(3).Should().OnlyContain(status => status == HttpStatusCode.NotFound);
        statuses.Last().Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task AnotherUser_HasTheirOwnBudget()
    {
        var limited = new ApiClient(factory);
        var fresh = new ApiClient(factory);
        for (var attempt = 0; attempt < 4; attempt++)
        {
            await limited.GetAccountAsync(Guid.NewGuid());
        }

        var response = await fresh.GetAccountAsync(Guid.NewGuid());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "the second user is not affected by the first one's limit");
    }
}

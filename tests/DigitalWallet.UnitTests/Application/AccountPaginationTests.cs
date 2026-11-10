using DigitalWallet.Application.Common;
using DigitalWallet.Domain.Enums;
using FluentAssertions;

namespace DigitalWallet.UnitTests.Application;

public class AccountPaginationTests
{
    private static async Task<WalletHarness> HarnessWithAccountsAsync(Guid owner, int count)
    {
        var harness = await WalletHarness.CreateAsync();
        for (var index = 0; index < count; index++)
        {
            await harness.CreateAccountAsync(owner, $"account {index}");
            harness.Clock.Advance(TimeSpan.FromMinutes(1));
        }

        return harness;
    }

    [Fact]
    public async Task Page_ReturnsTheRequestedSliceAndTheTotal()
    {
        var owner = Guid.NewGuid();
        var harness = await HarnessWithAccountsAsync(owner, 5);

        var page = await harness.ListPageAsync(owner, limit: 2, offset: 2);

        page.Items.Should().HaveCount(2);
        page.TotalCount.Should().Be(5);
        page.Limit.Should().Be(2);
        page.Offset.Should().Be(2);
    }

    [Fact]
    public async Task Pages_TogetherCoverEveryAccountExactlyOnce()
    {
        var owner = Guid.NewGuid();
        var harness = await HarnessWithAccountsAsync(owner, 5);

        var first = await harness.ListPageAsync(owner, limit: 2, offset: 0);
        var second = await harness.ListPageAsync(owner, limit: 2, offset: 2);
        var third = await harness.ListPageAsync(owner, limit: 2, offset: 4);

        var ids = first.Items.Concat(second.Items).Concat(third.Items).Select(account => account.Id).ToList();
        ids.Should().HaveCount(5).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task OffsetBeyondTheEnd_ReturnsAnEmptyPageWithTheTotal()
    {
        var owner = Guid.NewGuid();
        var harness = await HarnessWithAccountsAsync(owner, 2);

        var page = await harness.ListPageAsync(owner, limit: 10, offset: 50);

        page.Items.Should().BeEmpty();
        page.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task TotalCount_RespectsTheStatusFilter()
    {
        var owner = Guid.NewGuid();
        var harness = await HarnessWithAccountsAsync(owner, 3);
        var inactive = (await harness.ListAccountsAsync(owner)).First();
        await harness.SetActiveAsync(inactive.Id, active: false);

        var activePage = await harness.ListPageAsync(owner, status: AccountStatus.Active);
        var inactivePage = await harness.ListPageAsync(owner, status: AccountStatus.Blocked);

        activePage.TotalCount.Should().Be(2);
        inactivePage.TotalCount.Should().Be(1);
        inactivePage.Items.Should().ContainSingle().Which.Id.Should().Be(inactive.Id);
    }

    [Fact]
    public async Task DefaultPageSize_IsFive()
    {
        var owner = Guid.NewGuid();
        var harness = await HarnessWithAccountsAsync(owner, 12);

        var page = await harness.ListPageAsync(owner);

        page.Limit.Should().Be(5);
        page.Items.Should().HaveCount(5);
        page.TotalCount.Should().Be(12);
    }

    [Fact]
    public async Task NegativeOffset_IsRejected()
    {
        var harness = await WalletHarness.CreateAsync();

        Func<Task> act = () => harness.ListPageAsync(Guid.NewGuid(), offset: -1);

        var exception = await act.Should().ThrowAsync<RequestValidationException>();
        exception.Which.Errors.Should().ContainKey("offset");
    }
}

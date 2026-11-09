using DigitalWallet.Application.Common;
using DigitalWallet.Application.Statements;
using DigitalWallet.Domain.Enums;
using FluentAssertions;

namespace DigitalWallet.UnitTests.Application;

public class StatementUseCaseTests
{
    [Fact]
    public async Task Statement_ReturnsMostRecentEntriesFirst()
    {
        var harness = await WalletHarness.CreateAsync();
        var accountId = await harness.CreateFundedAccountAsync(0m);
        await harness.DepositAsync(accountId, 10m);
        await harness.DepositAsync(accountId, 20m);
        await harness.DepositAsync(accountId, 30m);

        var page = await harness.StatementAsync(accountId);

        page.Items.Select(item => item.Amount).Should().Equal(30m, 20m, 10m);
        page.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task Statement_FollowingCursor_ReturnsEveryEntryExactlyOnce()
    {
        var harness = await WalletHarness.CreateAsync();
        var accountId = await harness.CreateFundedAccountAsync(0m);
        for (var amount = 1; amount <= 5; amount++)
        {
            await harness.DepositAsync(accountId, amount);
        }

        var collected = new List<decimal>();
        string? cursor = null;
        var pages = 0;

        do
        {
            var page = await harness.StatementAsync(accountId, cursor, limit: 2);
            collected.AddRange(page.Items.Select(item => item.Amount));
            cursor = page.NextCursor;
            pages++;
        }
        while (cursor is not null);

        pages.Should().Be(3);
        collected.Should().Equal(5m, 4m, 3m, 2m, 1m);
    }

    [Fact]
    public async Task Statement_IncludesWithdrawalsAsNegativeAmounts()
    {
        var harness = await WalletHarness.CreateAsync();
        var accountId = await harness.CreateFundedAccountAsync(100m);
        await harness.WithdrawAsync(accountId, 30m);

        var page = await harness.StatementAsync(accountId);

        page.Items.Select(item => item.Type).Should().Equal(
            TransactionType.Withdrawal.ToString(),
            TransactionType.Deposit.ToString());
        page.Items.Select(item => item.Amount).Should().Equal(-30m, 100m);
    }

    [Fact]
    public async Task Statement_ContainsOnlyEntriesOfTheRequestedAccount()
    {
        var harness = await WalletHarness.CreateAsync();
        var first = await harness.CreateFundedAccountAsync(10m);
        var second = await harness.CreateFundedAccountAsync(99m);

        var page = await harness.StatementAsync(first);

        page.Items.Should().ContainSingle();
        page.Items[0].Amount.Should().Be(10m);
        second.Should().NotBe(first);
    }

    [Fact]
    public async Task Statement_EmptyAccount_ReturnsNoItemsAndNoCursor()
    {
        var harness = await WalletHarness.CreateAsync();
        var accountId = await harness.CreateFundedAccountAsync(0m);

        var page = await harness.StatementAsync(accountId);

        page.Items.Should().BeEmpty();
        page.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task Statement_UnknownAccount_ThrowsNotFound()
    {
        var harness = await WalletHarness.CreateAsync();

        Func<Task> act = () => harness.StatementAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<AccountNotFoundException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Statement_LimitOutOfRange_ThrowsValidation(int limit)
    {
        var harness = await WalletHarness.CreateAsync();
        var accountId = await harness.CreateFundedAccountAsync(1m);

        Func<Task> act = () => harness.StatementAsync(accountId, limit: limit);

        var exception = await act.Should().ThrowAsync<RequestValidationException>();
        exception.Which.Errors.Should().ContainKey("limit");
    }

    [Theory]
    [InlineData("not a cursor!")]
    [InlineData("AAAA")]
    public async Task Statement_InvalidCursor_ThrowsValidation(string cursor)
    {
        var harness = await WalletHarness.CreateAsync();
        var accountId = await harness.CreateFundedAccountAsync(1m);

        Func<Task> act = () => harness.StatementAsync(accountId, cursor);

        var exception = await act.Should().ThrowAsync<RequestValidationException>();
        exception.Which.Errors.Should().ContainKey("cursor");
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(42L)]
    [InlineData(long.MaxValue)]
    public void StatementCursor_RoundTrips(long sequence)
    {
        var cursor = StatementCursor.Encode(sequence);

        StatementCursor.Decode(cursor).Should().Be(sequence);
    }

    [Fact]
    public void StatementCursor_IsUrlSafe()
    {
        var cursor = StatementCursor.Encode(long.MaxValue);

        cursor.Should().NotContain("+").And.NotContain("/").And.NotContain("=");
    }
}

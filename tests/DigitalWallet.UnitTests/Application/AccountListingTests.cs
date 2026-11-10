using DigitalWallet.Application.Accounts;
using DigitalWallet.Application.Common;
using DigitalWallet.Domain.Exceptions;
using FluentAssertions;

namespace DigitalWallet.UnitTests.Application;

public class AccountListingTests
{
    [Fact]
    public async Task CreateAccount_WithAName_StoresTheName()
    {
        var harness = await WalletHarness.CreateAsync();

        var account = await harness.CreateAccountAsync(Guid.NewGuid(), name: "Groceries");

        account.Name.Should().Be("Groceries");
        (await harness.GetAccountAsync(account.Id)).Name.Should().Be("Groceries");
    }

    [Fact]
    public async Task CreateAccount_WithAnInvalidName_ThrowsAndPersistsNothing()
    {
        var harness = await WalletHarness.CreateAsync();
        var owner = Guid.NewGuid();

        Func<Task> act = () => harness.CreateAccountAsync(owner, name: new string('x', 61));

        await act.Should().ThrowAsync<InvalidAccountNameException>();
        (await harness.ListAccountsAsync(owner)).Should().BeEmpty();
    }

    [Fact]
    public async Task ListAccounts_ReturnsOnlyTheOwnersAccounts()
    {
        var harness = await WalletHarness.CreateAsync();
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var aliceAccount = await harness.CreateAccountAsync(alice, name: "Mine");
        await harness.CreateAccountAsync(bob, name: "Bob's");

        var accounts = await harness.ListAccountsAsync(alice);

        accounts.Should().ContainSingle().Which.Id.Should().Be(aliceAccount.Id);
    }

    [Fact]
    public async Task ListAccounts_ReturnsNewestFirst()
    {
        var harness = await WalletHarness.CreateAsync();
        var owner = Guid.NewGuid();
        var first = await harness.CreateAccountAsync(owner, name: "First");
        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        var second = await harness.CreateAccountAsync(owner, name: "Second");

        var accounts = await harness.ListAccountsAsync(owner);

        accounts.Select(account => account.Id).Should().Equal(second.Id, first.Id);
    }

    [Fact]
    public async Task ListAccounts_RespectsTheLimit()
    {
        var harness = await WalletHarness.CreateAsync();
        var owner = Guid.NewGuid();
        for (var index = 0; index < 3; index++)
        {
            await harness.CreateAccountAsync(owner);
        }

        var accounts = await harness.ListAccountsAsync(owner, limit: 2);

        accounts.Should().HaveCount(2);
    }

    [Fact]
    public async Task ListAccounts_WithoutAnyAccount_ReturnsAnEmptyList()
    {
        var harness = await WalletHarness.CreateAsync();

        var accounts = await harness.ListAccountsAsync(Guid.NewGuid());

        accounts.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(ListAccountsUseCase.MaxLimit + 1)]
    public async Task ListAccounts_LimitOutOfRange_IsRejected(int limit)
    {
        var harness = await WalletHarness.CreateAsync();

        Func<Task> act = () => harness.ListAccountsAsync(Guid.NewGuid(), limit);

        var exception = await act.Should().ThrowAsync<RequestValidationException>();
        exception.Which.Errors.Should().ContainKey("limit");
    }

    [Fact]
    public async Task ListAccounts_DoesNotIncludeTheSettlementAccount()
    {
        var harness = await WalletHarness.CreateAsync();

        var accounts = await harness.ListAccountsAsync(Guid.Empty);

        accounts.Should().BeEmpty();
    }
}

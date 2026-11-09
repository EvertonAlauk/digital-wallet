using DigitalWallet.Application.Accounts;
using DigitalWallet.Application.Common;
using DigitalWallet.Application.Settlement;
using DigitalWallet.Domain.Enums;
using DigitalWallet.Domain.ValueObjects;
using FluentAssertions;

namespace DigitalWallet.UnitTests.Application;

public class AccountUseCaseTests
{
    [Fact]
    public async Task CreateAccount_PersistsActiveAccountWithZeroBalance()
    {
        var harness = await WalletHarness.CreateAsync();
        var ownerId = Guid.NewGuid();

        var account = await harness.CreateAccountAsync(ownerId);

        account.OwnerId.Should().Be(ownerId);
        account.Status.Should().Be(AccountStatus.Active.ToString());
        account.Balance.Should().Be(0m);
        account.Currency.Should().Be(Money.DefaultCurrency);
        account.CreatedAt.Should().Be(WalletHarness.Now);
        harness.Database.GetAccount(account.Id).Should().NotBeNull();
    }

    [Fact]
    public async Task CreateAccount_EmptyOwner_ThrowsValidationAndPersistsNothing()
    {
        var harness = await WalletHarness.CreateAsync();

        Func<Task> act = () => harness.CreateAccountAsync(Guid.Empty);

        var exception = await act.Should().ThrowAsync<RequestValidationException>();
        exception.Which.Errors.Should().ContainKey("ownerId");
    }

    [Fact]
    public async Task GetAccount_Unknown_ThrowsNotFound()
    {
        var harness = await WalletHarness.CreateAsync();
        var unknownId = Guid.NewGuid();

        Func<Task> act = () => harness.GetAccountAsync(unknownId);

        var exception = await act.Should().ThrowAsync<AccountNotFoundException>();
        exception.Which.AccountId.Should().Be(unknownId);
    }

    [Fact]
    public async Task GetAccount_ReturnsCurrentBalance()
    {
        var harness = await WalletHarness.CreateAsync();
        var accountId = await harness.CreateFundedAccountAsync(75m);

        var account = await harness.GetAccountAsync(accountId);

        account.Balance.Should().Be(75m);
    }

    [Fact]
    public async Task EnsureSettlementAccount_CreatesSettlementAccountOnce()
    {
        var harness = await WalletHarness.CreateAsync();
        var unitOfWork = harness.Database.CreateUnitOfWork();

        await new EnsureSettlementAccountUseCase(unitOfWork.Accounts, unitOfWork, harness.Clock)
            .ExecuteAsync(CancellationToken.None);

        var settlement = harness.Database.GetAccount(SettlementAccount.Id);
        settlement.Should().NotBeNull();
        settlement!.Kind.Should().Be(AccountKind.Settlement);
    }
}

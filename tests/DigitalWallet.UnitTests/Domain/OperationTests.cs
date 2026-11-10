using DigitalWallet.Domain.Entities;
using DigitalWallet.Domain.Enums;
using DigitalWallet.Domain.Exceptions;
using DigitalWallet.Domain.ValueObjects;
using FluentAssertions;

namespace DigitalWallet.UnitTests.Domain;

public class OperationTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static Operation Accepted() =>
        Operation.Accept(Guid.NewGuid(), OperationKind.Deposit, Guid.NewGuid(), null, 10m, Now);

    [Fact]
    public void Accept_StartsAsAccepted()
    {
        var operation = Accepted();

        operation.Status.Should().Be(OperationStatus.Accepted);
        operation.TransactionId.Should().BeNull();
        operation.CompletedAt.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Accept_NonPositiveAmount_Throws(decimal amount)
    {
        Action act = () => Operation.Accept(Guid.NewGuid(), OperationKind.Withdrawal, Guid.NewGuid(), null, amount, Now);

        act.Should().Throw<InvalidAmountException>();
    }

    [Fact]
    public void Complete_RecordsTheTransaction()
    {
        var operation = Accepted();
        var transactionId = Guid.NewGuid();

        operation.Complete(transactionId, Now);

        operation.Status.Should().Be(OperationStatus.Completed);
        operation.TransactionId.Should().Be(transactionId);
        operation.CompletedAt.Should().Be(Now);
    }

    [Fact]
    public void Reject_RecordsTheReason()
    {
        var operation = Accepted();

        operation.Reject("Insufficient funds.", Now);

        operation.Status.Should().Be(OperationStatus.Rejected);
        operation.FailureReason.Should().Be("Insufficient funds.");
    }

    [Fact]
    public void FinalStates_CannotChangeAgain()
    {
        var completed = Accepted();
        completed.Complete(Guid.NewGuid(), Now);
        var rejected = Accepted();
        rejected.Reject("Blocked.", Now);

        Action completeAgain = () => completed.Complete(Guid.NewGuid(), Now);
        Action rejectAfterCompletion = () => completed.Reject("Late.", Now);
        Action completeAfterRejection = () => rejected.Complete(Guid.NewGuid(), Now);

        completeAgain.Should().Throw<InvalidTransactionException>();
        rejectAfterCompletion.Should().Throw<InvalidTransactionException>();
        completeAfterRejection.Should().Throw<InvalidTransactionException>();
    }

    [Fact]
    public void EnsureCanApply_DoesNotChangeTheBalance()
    {
        var account = Account.Open(Guid.NewGuid(), Guid.NewGuid(), Now);
        var entry = new LedgerEntry(account.Id, Money.Brl(-10m), Guid.NewGuid());

        Action act = () => account.EnsureCanApply(entry);

        act.Should().Throw<InsufficientFundsException>();
        account.Balance.Should().Be(Money.Brl(0m));
        account.Version.Should().Be(0);
    }
}

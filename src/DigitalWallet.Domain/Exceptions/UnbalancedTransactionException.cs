namespace DigitalWallet.Domain.Exceptions;

public sealed class UnbalancedTransactionException(decimal netSum)
    : DomainException($"The transaction's ledger entries do not sum to zero (sum: {netSum}).")
{
    public decimal NetSum { get; } = netSum;
}

namespace DigitalWallet.Application.Settlement;

/// <summary>
/// Well-known settlement account (BRL). Deposits debit it and withdrawals credit it, so the wallet's money
/// always balances against an account outside the customer balances. It may have a negative balance.
/// </summary>
public static class SettlementAccount
{
    public static readonly Guid Id = new("00000000-0000-0000-0000-000000000001");
}

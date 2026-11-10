using DigitalWallet.Application.Operations;

namespace DigitalWallet.Application.Messaging;

/// <summary>Every message type in the pipeline. Each one gets its own queue, retry queue and dead-letter topic.</summary>
public static class MessageTypes
{
    public static readonly string[] All =
    [
        nameof(DepositRequested),
        nameof(DepositCreated),
        nameof(WithdrawalRequested),
        nameof(WithdrawalCreated),
        nameof(TransferRequested),
        nameof(TransferCreated),
    ];
}

using DigitalWallet.Application.Operations;
using DigitalWallet.Domain.Events;

namespace DigitalWallet.Application.Messaging;

/// <summary>Every message type the system publishes. Each one gets its own queue, retry queue and dead-letter topic.</summary>
public static class MessageTypes
{
    public static readonly string[] All =
    [
        nameof(DepositRequested),
        nameof(DepositCreated),
        nameof(DepositRejected),
        nameof(WithdrawalRequested),
        nameof(WithdrawalCreated),
        nameof(WithdrawalRejected),
        nameof(TransferRequested),
        nameof(TransferCreated),
        nameof(TransferRejected),
        nameof(TransferCompleted),
    ];
}

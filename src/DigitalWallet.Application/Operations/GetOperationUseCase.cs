using DigitalWallet.Application.Common;

namespace DigitalWallet.Application.Operations;

public sealed class GetOperationUseCase(IOperationRepository operations)
{
    public async Task<OperationView> ExecuteAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var operation = await operations.GetByIdAsync(operationId, cancellationToken)
            ?? throw new OperationNotFoundException(operationId);

        return OperationView.From(operation);
    }
}

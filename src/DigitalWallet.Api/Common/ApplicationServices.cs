using DigitalWallet.Application.Accounts;
using DigitalWallet.Application.Idempotency;
using DigitalWallet.Application.Operations;
using DigitalWallet.Application.Operations.Handlers;
using DigitalWallet.Application.Outbox;
using DigitalWallet.Application.Settlement;
using DigitalWallet.Application.Statements;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalWallet.Api.Common;

internal static class ApplicationServices
{
    public static IServiceCollection AddApplicationUseCases(this IServiceCollection services)
    {
        services.AddScoped<CreateAccountUseCase>();
        services.AddScoped<GetAccountUseCase>();
        services.AddScoped<ListAccountsUseCase>();
        services.AddScoped<GetStatementUseCase>();
        services.AddScoped<EnsureSettlementAccountUseCase>();
        services.AddScoped<IdempotencyService>();
        services.AddScoped<OutboxRelay>();

        services.AddScoped<AcceptDepositUseCase>();
        services.AddScoped<AcceptWithdrawalUseCase>();
        services.AddScoped<AcceptTransferUseCase>();
        services.AddScoped<GetOperationUseCase>();

        // Pipeline consumers: one handler per message type.
        services.AddScoped<IMessageHandler, DepositRequestedHandler>();
        services.AddScoped<IMessageHandler, WithdrawalRequestedHandler>();
        services.AddScoped<IMessageHandler, TransferRequestedHandler>();
        services.AddScoped<IMessageHandler, DepositCreatedHandler>();
        services.AddScoped<IMessageHandler, WithdrawalCreatedHandler>();
        services.AddScoped<IMessageHandler, TransferCreatedHandler>();

        return services;
    }
}

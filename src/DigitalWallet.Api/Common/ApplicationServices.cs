using DigitalWallet.Application.Accounts;
using DigitalWallet.Application.Idempotency;
using DigitalWallet.Application.Movements;
using DigitalWallet.Application.Settlement;
using DigitalWallet.Application.Statements;
using DigitalWallet.Application.Transfers;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalWallet.Api.Common;

internal static class ApplicationServices
{
    public static IServiceCollection AddApplicationUseCases(this IServiceCollection services)
    {
        services.AddScoped<CreateAccountUseCase>();
        services.AddScoped<GetAccountUseCase>();
        services.AddScoped<DepositUseCase>();
        services.AddScoped<WithdrawalUseCase>();
        services.AddScoped<GetStatementUseCase>();
        services.AddScoped<EnsureSettlementAccountUseCase>();
        services.AddScoped<TransferUseCase>();
        services.AddScoped<IdempotencyService>();

        return services;
    }
}

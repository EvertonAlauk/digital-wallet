using DigitalWallet.Application.Idempotency;
using DigitalWallet.Application.Ports;
using DigitalWallet.Infrastructure.Clock;
using DigitalWallet.Infrastructure.Persistence;
using DigitalWallet.Infrastructure.Persistence.Idempotency;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql.EntityFrameworkCore.PostgreSQL;

namespace DigitalWallet.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<WalletDbContext>(options => options.UseNpgsql(connectionString));

        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ILedgerReader, LedgerReader>();
        services.AddScoped<IIdempotencyRepository, IdempotencyRepository>();
        services.AddSingleton<IClock, SystemClock>();

        return services;
    }
}

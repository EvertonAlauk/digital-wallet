using DigitalWallet.Application.Idempotency;
using DigitalWallet.Application.Notifications;
using DigitalWallet.Application.Operations;
using DigitalWallet.Application.Outbox;
using DigitalWallet.Application.Ports;
using DigitalWallet.Infrastructure.Clock;
using DigitalWallet.Infrastructure.Messaging;
using DigitalWallet.Infrastructure.Messaging.RabbitMq;
using DigitalWallet.Infrastructure.Persistence.Operations;
using DigitalWallet.Infrastructure.Persistence;
using DigitalWallet.Infrastructure.Persistence.Idempotency;
using DigitalWallet.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql.EntityFrameworkCore.PostgreSQL;

namespace DigitalWallet.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddRabbitMq(this IServiceCollection services, RabbitMqOptions options)
    {
        services.AddSingleton(options);
        services.AddSingleton<RabbitMqConnection>();
        services.AddSingleton<RabbitMqPublishChannel>();
        services.AddSingleton<IMessagePublisher, RabbitMqPublisher>();

        return services;
    }

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<WalletDbContext>(options => options.UseNpgsql(connectionString));

        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ILedgerReader, LedgerReader>();
        services.AddScoped<IIdempotencyRepository, IdempotencyRepository>();
        services.AddScoped<IOutbox, OutboxWriter>();
        services.AddScoped<IOutboxStore, OutboxStore>();
        services.AddScoped<IProcessedMessageStore, ProcessedMessageStore>();
        services.AddScoped<IOperationRepository, OperationRepository>();
        services.AddScoped<INotificationSender, LogNotificationSender>();
        services.AddSingleton<IClock, SystemClock>();

        return services;
    }
}

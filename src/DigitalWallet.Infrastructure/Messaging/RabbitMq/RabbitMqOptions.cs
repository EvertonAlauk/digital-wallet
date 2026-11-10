namespace DigitalWallet.Infrastructure.Messaging.RabbitMq;

public sealed record RabbitMqOptions(
    string Host,
    int Port,
    string UserName,
    string Password,
    string VirtualHost = "/");

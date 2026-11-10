using System.Text;
using DigitalWallet.Application.Messaging;
using RabbitMQ.Client;

namespace DigitalWallet.Infrastructure.Messaging.RabbitMq;

/// <summary>Reads the attempt counter from a delivered message. Brokers and clients may encode numbers differently.</summary>
public static class RabbitMqHeaders
{
    public static int ReadAttempt(IReadOnlyBasicProperties properties)
    {
        if (properties.Headers is null || !properties.Headers.TryGetValue(RetryPolicy.AttemptHeader, out var value))
        {
            return 0;
        }

        return value switch
        {
            int number => number,
            long number => (int)number,
            byte[] bytes when int.TryParse(Encoding.UTF8.GetString(bytes), out var parsed) => parsed,
            string text when int.TryParse(text, out var parsed) => parsed,
            _ => 0,
        };
    }

    public static BasicProperties WithAttempt(IReadOnlyBasicProperties source, int attempt) => new()
    {
        Persistent = true,
        MessageId = source.MessageId,
        Type = source.Type,
        Headers = new Dictionary<string, object?> { [RetryPolicy.AttemptHeader] = attempt },
    };
}

using System.Text;

namespace DigitalWallet.Application.Messaging;

/// <summary>
/// Queue naming. For a message type the main queue is its kebab-case name ("DepositCreated" -> "deposit-created").
/// Its retry queue ("-dlq") waits and sends the message back, and its dead-letter topic ("-dlt") keeps messages that
/// failed every retry, for inspection and manual replay.
/// </summary>
public static class QueueNames
{
    public static string For(string messageType)
    {
        var name = new StringBuilder(messageType.Length + 4);

        for (var index = 0; index < messageType.Length; index++)
        {
            var character = messageType[index];

            if (char.IsUpper(character))
            {
                if (index > 0)
                {
                    name.Append('-');
                }

                name.Append(char.ToLowerInvariant(character));
            }
            else
            {
                name.Append(character);
            }
        }

        return name.ToString();
    }

    public static string RetryFor(string queue) => queue + "-dlq";

    public static string DeadLetterFor(string queue) => queue + "-dlt";
}

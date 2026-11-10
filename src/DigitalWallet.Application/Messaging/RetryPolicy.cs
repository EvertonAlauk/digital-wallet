namespace DigitalWallet.Application.Messaging;

/// <summary>
/// Retry rule for a failed delivery. The attempt number travels in the message header: 0 on the first delivery,
/// incremented on every retry. A message is retried while the attempt is below <see cref="MaxRetries"/>; after that
/// it goes to the dead-letter topic. So a message is delivered at most <c>MaxRetries + 1</c> times.
/// </summary>
public static class RetryPolicy
{
    public const int MaxRetries = 5;

    public const string AttemptHeader = "x-attempt";

    public static bool ShouldRetry(int attempt) => attempt < MaxRetries;
}

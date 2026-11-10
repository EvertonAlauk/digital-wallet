using DigitalWallet.Application.Messaging;
using FluentAssertions;

namespace DigitalWallet.UnitTests.Messaging;

public class MessagingRulesTests
{
    [Theory]
    [InlineData("DepositCreated", "deposit-created")]
    [InlineData("TransferRequested", "transfer-requested")]
    [InlineData("TransferCreated", "transfer-created")]
    public void QueueName_IsTheKebabCaseOfTheMessageType(string messageType, string expected)
    {
        QueueNames.For(messageType).Should().Be(expected);
    }

    [Fact]
    public void RetryAndDeadLetterNames_AreDerivedFromTheMainQueue()
    {
        QueueNames.RetryFor("deposit-created").Should().Be("deposit-created-dlq");
        QueueNames.DeadLetterFor("deposit-created").Should().Be("deposit-created-dlt");
    }

    [Fact]
    public void EveryMessageType_HasADistinctQueue()
    {
        var queues = MessageTypes.All.Select(QueueNames.For).ToList();

        queues.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void RetryPolicy_AllowsFiveRetriesThenDeadLetters()
    {
        // The first delivery has attempt 0. Attempts 0 to 4 fail into the retry queue (five retries),
        // and attempt 5, the sixth delivery, fails into the dead-letter topic.
        for (var attempt = 0; attempt < RetryPolicy.MaxRetries; attempt++)
        {
            RetryPolicy.ShouldRetry(attempt).Should().BeTrue($"attempt {attempt} may be retried");
        }

        RetryPolicy.ShouldRetry(RetryPolicy.MaxRetries).Should().BeFalse("the retries are exhausted");
    }
}

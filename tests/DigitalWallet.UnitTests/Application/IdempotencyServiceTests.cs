using DigitalWallet.Application.Idempotency;
using DigitalWallet.Testing.Fakes;
using DigitalWallet.Testing.InMemory;
using FluentAssertions;

namespace DigitalWallet.UnitTests.Application;

public class IdempotencyServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly InMemoryDatabase _database = new();
    private readonly FakeClock _clock = new(Start);
    private readonly IdempotencyService _service;

    public IdempotencyServiceTests()
    {
        _service = new IdempotencyService(new InMemoryIdempotencyRepository(_database), _clock);
    }

    private static StoredResponse Created(string body) =>
        new(201, "/accounts/1", "application/json", System.Text.Encoding.UTF8.GetBytes(body));

    [Fact]
    public async Task NewKey_IsAcquired()
    {
        var decision = await _service.AcquireAsync("key-1", "hash-a", CancellationToken.None);

        decision.Kind.Should().Be(IdempotencyDecisionKind.Acquired);
    }

    [Fact]
    public async Task CompletedKey_WithSamePayload_ReplaysTheStoredResponse()
    {
        await _service.AcquireAsync("key-1", "hash-a", CancellationToken.None);
        var original = Created("{\"transactionId\":\"t1\"}");
        await _service.CompleteAsync("key-1", original, CancellationToken.None);

        var decision = await _service.AcquireAsync("key-1", "hash-a", CancellationToken.None);

        decision.Kind.Should().Be(IdempotencyDecisionKind.Replay);
        decision.Response!.StatusCode.Should().Be(201);
        decision.Response.Location.Should().Be("/accounts/1");
        decision.Response.Body.Should().Equal(original.Body);
    }

    [Fact]
    public async Task KeyReusedWithDifferentPayload_IsAPayloadMismatch()
    {
        await _service.AcquireAsync("key-1", "hash-a", CancellationToken.None);
        await _service.CompleteAsync("key-1", Created("{}"), CancellationToken.None);

        var decision = await _service.AcquireAsync("key-1", "hash-b", CancellationToken.None);

        decision.Kind.Should().Be(IdempotencyDecisionKind.PayloadMismatch);
    }

    [Fact]
    public async Task KeyStillProcessing_WithSamePayload_IsInProgress()
    {
        await _service.AcquireAsync("key-1", "hash-a", CancellationToken.None);

        var decision = await _service.AcquireAsync("key-1", "hash-a", CancellationToken.None);

        decision.Kind.Should().Be(IdempotencyDecisionKind.InProgress);
    }

    [Fact]
    public async Task KeyStillProcessing_WithDifferentPayload_IsAPayloadMismatch()
    {
        // The payload conflict is reported whatever the state of the first request.
        await _service.AcquireAsync("key-1", "hash-a", CancellationToken.None);

        var decision = await _service.AcquireAsync("key-1", "hash-b", CancellationToken.None);

        decision.Kind.Should().Be(IdempotencyDecisionKind.PayloadMismatch);
    }

    [Fact]
    public async Task ReleasedKey_CanBeProcessedAgain()
    {
        await _service.AcquireAsync("key-1", "hash-a", CancellationToken.None);
        await _service.ReleaseAsync("key-1", CancellationToken.None);

        var decision = await _service.AcquireAsync("key-1", "hash-a", CancellationToken.None);

        decision.Kind.Should().Be(IdempotencyDecisionKind.Acquired);
    }

    [Fact]
    public async Task KeyExpiresAfter24Hours_AndIsTreatedAsNew()
    {
        await _service.AcquireAsync("key-1", "hash-a", CancellationToken.None);
        await _service.CompleteAsync("key-1", Created("{}"), CancellationToken.None);

        _clock.Advance(IdempotencyService.Retention);

        // Exactly 24 hours later the key is expired, so even a different payload is accepted as new.
        var decision = await _service.AcquireAsync("key-1", "hash-b", CancellationToken.None);

        decision.Kind.Should().Be(IdempotencyDecisionKind.Acquired);
    }

    [Fact]
    public async Task KeyJustBeforeExpiry_IsStillActive()
    {
        await _service.AcquireAsync("key-1", "hash-a", CancellationToken.None);
        await _service.CompleteAsync("key-1", Created("{}"), CancellationToken.None);

        _clock.Advance(IdempotencyService.Retention - TimeSpan.FromSeconds(1));

        var decision = await _service.AcquireAsync("key-1", "hash-a", CancellationToken.None);

        decision.Kind.Should().Be(IdempotencyDecisionKind.Replay);
    }

    [Fact]
    public async Task ConcurrentAcquires_OfTheSameKey_OnlyOneIsAcquired()
    {
        var attempts = Enumerable.Range(0, 50).Select(_ => Task.Run(() =>
            _service.AcquireAsync("shared-key", "hash-a", CancellationToken.None)));

        var decisions = await Task.WhenAll(attempts).WaitAsync(TimeSpan.FromSeconds(30));

        decisions.Count(decision => decision.Kind == IdempotencyDecisionKind.Acquired).Should().Be(1);
        decisions.Count(decision => decision.Kind == IdempotencyDecisionKind.InProgress).Should().Be(49);
    }
}

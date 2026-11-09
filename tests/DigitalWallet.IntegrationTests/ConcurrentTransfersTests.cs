using System.Net;
using DigitalWallet.Api.Features.Accounts;
using DigitalWallet.Api.Features.Movements;
using DigitalWallet.Application.Settlement;
using DigitalWallet.IntegrationTests.Infrastructure;
using FluentAssertions;
using System.Net.Http.Json;

namespace DigitalWallet.IntegrationTests;

/// <summary>
/// 1,000 transfers fired at once among a small group of accounts, so most of them contend for the same rows.
/// Checks the three invariants the wallet must keep under concurrency.
/// </summary>
public class ConcurrentTransfersTests(WalletApiFactory factory) : IClassFixture<WalletApiFactory>
{
    private const int AccountCount = 5;
    private const int TransferCount = 1000;
    private const decimal InitialBalance = 1000m;

    // Long enough for a loaded run, short enough that a deadlock fails the test instead of hanging it.
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(120);

    [Fact]
    public async Task ThousandConcurrentTransfers_KeepTheInvariants()
    {
        var api = new ApiClient(factory);
        var accounts = new List<Guid>();
        for (var index = 0; index < AccountCount; index++)
        {
            var created = (await (await api.PostAccountAsync(Guid.NewGuid())).Content.ReadFromJsonAsync<AccountResponse>())!;
            await api.PostDepositAsync(created.Id, InitialBalance);
            accounts.Add(created.Id);
        }

        var random = new Random(20260101);
        var plans = Enumerable.Range(0, TransferCount).Select(_ =>
        {
            var source = random.Next(AccountCount);
            var destination = random.Next(AccountCount - 1);
            if (destination >= source)
            {
                destination++;
            }

            return (From: accounts[source], To: accounts[destination], Amount: random.Next(1, 201));
        }).ToList();

        var responses = await Task.WhenAll(plans.Select(plan =>
            api.PostTransferAsync(plan.From, plan.To, plan.Amount))).WaitAsync(Guard);

        try
        {
            // Every transfer either commits (201) or is rejected for insufficient funds (422). None may fail
            // any other way: a 409 would mean a lock was bypassed, and a 500 would mean an unexpected fault.
            responses.Select(response => response.StatusCode)
                .Should().OnlyContain(status => status == HttpStatusCode.Created || status == (HttpStatusCode)422);

            responses.Count(response => response.StatusCode == HttpStatusCode.Created)
                .Should().BeGreaterThan(0, "some transfers must succeed for the test to mean anything");
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        // (1) No balance is negative. Customer accounts cannot go below zero (the settlement account may).
        foreach (var account in accounts)
        {
            factory.Database.GetAccount(account)!.Balance.Amount.Should().BeGreaterThanOrEqualTo(0m);
        }

        // (2) The total money is unchanged: customers hold exactly what was deposited.
        var customerTotal = accounts.Sum(account => factory.Database.GetAccount(account)!.Balance.Amount);
        customerTotal.Should().Be(AccountCount * InitialBalance);

        // (3) Each cached balance equals the sum of that account's ledger entries.
        var allAccounts = accounts.Append(SettlementAccount.Id);
        foreach (var account in allAccounts)
        {
            var cached = factory.Database.GetAccount(account)!.Balance.Amount;
            var fromLedger = factory.Database.ReadAccountEntries(account, beforeSequence: null, limit: int.MaxValue)
                .Sum(entry => entry.Amount.Amount);
            cached.Should().Be(fromLedger, $"account {account} must match its ledger");
        }

        // Double entry: across all accounts, including settlement, the ledger nets to zero.
        allAccounts.Sum(account => factory.Database.GetAccount(account)!.Balance.Amount).Should().Be(0m);
    }
}

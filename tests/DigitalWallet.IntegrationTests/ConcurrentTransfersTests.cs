using System.Net;
using System.Net.Http.Json;
using DigitalWallet.Api.Features.Accounts;
using DigitalWallet.Api.Features.Operations;
using DigitalWallet.Application.Settlement;
using DigitalWallet.Domain.Enums;
using DigitalWallet.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace DigitalWallet.IntegrationTests;

/// <summary>
/// 1,000 transfers accepted at once among a small group of accounts, so most of them contend for the same balances.
/// The pipeline then processes them. Checks the invariants the wallet must keep under concurrency.
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
            var created = (await (await api.PostAccountAsync()).Content.ReadFromJsonAsync<AccountResponse>())!;
            await api.FundAsync(created.Id, InitialBalance);
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

        var operationIds = new List<Guid>();
        try
        {
            // Every transfer is accepted (202). Insufficient funds shows up later as a rejected operation, never as a
            // server error or a conflict at acceptance.
            responses.Select(response => response.StatusCode).Should().OnlyContain(status => status == HttpStatusCode.Accepted);

            foreach (var response in responses)
            {
                operationIds.Add((await response.Content.ReadFromJsonAsync<OperationAcceptedResponse>())!.OperationId);
            }
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        await factory.DrainAsync();

        var outcomes = operationIds.Select(id => factory.Database.GetOperation(id)!.Status).ToList();
        outcomes.Should().OnlyContain(status => status == OperationStatus.Completed || status == OperationStatus.Rejected);
        outcomes.Count(status => status == OperationStatus.Completed).Should().BeGreaterThan(0,
            "some transfers must complete for the test to mean anything");

        // (1) No customer balance is negative. Only the settlement account may be.
        foreach (var account in accounts)
        {
            factory.Database.GetAccount(account)!.Balance.Amount.Should().BeGreaterThanOrEqualTo(0m);
        }

        // (2) The total money is unchanged: customers hold exactly what was deposited.
        var customerTotal = accounts.Sum(account => factory.Database.GetAccount(account)!.Balance.Amount);
        customerTotal.Should().Be(AccountCount * InitialBalance);

        // (3) Each cached balance equals the sum of that account's ledger entries.
        var allAccounts = accounts.Append(SettlementAccount.Id).ToList();
        foreach (var account in allAccounts)
        {
            var cached = factory.Database.GetAccount(account)!.Balance.Amount;
            var fromLedger = factory.Database.ReadAccountEntries(account, beforeSequence: null, limit: int.MaxValue)
                .Sum(entry => entry.Amount.Amount);
            cached.Should().Be(fromLedger, $"account {account} must match its ledger");
        }

        // Double entry: across all accounts, including settlement, the balances net to zero.
        allAccounts.Sum(account => factory.Database.GetAccount(account)!.Balance.Amount).Should().Be(0m);
    }
}

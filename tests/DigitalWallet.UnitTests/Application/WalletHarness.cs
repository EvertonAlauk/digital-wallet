using DigitalWallet.Application.Accounts;
using DigitalWallet.Application.Movements;
using DigitalWallet.Application.Settlement;
using DigitalWallet.Application.Statements;
using DigitalWallet.Testing.Fakes;
using DigitalWallet.Testing.InMemory;

namespace DigitalWallet.UnitTests.Application;

/// <summary>
/// Builds the use cases over the in-memory fakes. Each call creates its own unit of work, the same way
/// a request does in the API.
/// </summary>
internal sealed class WalletHarness
{
    public static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private WalletHarness()
    {
        Database = new InMemoryDatabase();
        Clock = new FakeClock(Now);
    }

    public InMemoryDatabase Database { get; }

    public FakeClock Clock { get; }

    /// <summary>Creates a harness with the settlement account already initialized, as the API does at startup.</summary>
    public static async Task<WalletHarness> CreateAsync()
    {
        var harness = new WalletHarness();
        var unitOfWork = harness.Database.CreateUnitOfWork();
        var ensure = new EnsureSettlementAccountUseCase(unitOfWork.Accounts, unitOfWork, harness.Clock);
        await ensure.ExecuteAsync(CancellationToken.None);
        return harness;
    }

    public Task<AccountView> CreateAccountAsync(Guid ownerId)
    {
        var unitOfWork = Database.CreateUnitOfWork();
        var useCase = new CreateAccountUseCase(unitOfWork.Accounts, unitOfWork, Clock);
        return useCase.ExecuteAsync(new CreateAccountCommand(ownerId), CancellationToken.None);
    }

    public Task<AccountView> GetAccountAsync(Guid accountId)
    {
        var unitOfWork = Database.CreateUnitOfWork();
        return new GetAccountUseCase(unitOfWork.Accounts).ExecuteAsync(accountId, CancellationToken.None);
    }

    public Task<MovementResult> DepositAsync(Guid accountId, decimal amount)
    {
        var unitOfWork = Database.CreateUnitOfWork();
        var useCase = new DepositUseCase(unitOfWork.Accounts, unitOfWork.Transactions, unitOfWork, Clock);
        return useCase.ExecuteAsync(new MovementCommand(accountId, amount), CancellationToken.None);
    }

    public Task<MovementResult> WithdrawAsync(Guid accountId, decimal amount)
    {
        var unitOfWork = Database.CreateUnitOfWork();
        var useCase = new WithdrawalUseCase(unitOfWork.Accounts, unitOfWork.Transactions, unitOfWork, Clock);
        return useCase.ExecuteAsync(new MovementCommand(accountId, amount), CancellationToken.None);
    }

    public Task<StatementPage> StatementAsync(Guid accountId, string? cursor = null, int? limit = null)
    {
        var reader = new InMemoryLedgerReader(Database);
        var unitOfWork = Database.CreateUnitOfWork();
        var useCase = new GetStatementUseCase(unitOfWork.Accounts, reader);
        return useCase.ExecuteAsync(new GetStatementQuery(accountId, cursor, limit), CancellationToken.None);
    }

    public async Task<Guid> CreateFundedAccountAsync(decimal amount)
    {
        var account = await CreateAccountAsync(Guid.NewGuid());
        if (amount > 0m)
        {
            await DepositAsync(account.Id, amount);
        }

        return account.Id;
    }

    public async Task BlockAccountAsync(Guid accountId)
    {
        var unitOfWork = Database.CreateUnitOfWork();
        var account = await unitOfWork.Accounts.GetByIdAsync(accountId, CancellationToken.None);
        account!.Block();
        await unitOfWork.CommitAsync(CancellationToken.None);
    }
}

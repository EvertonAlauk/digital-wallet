using DigitalWallet.Application.Accounts;
using DigitalWallet.Application.Operations;
using DigitalWallet.Application.Settlement;
using DigitalWallet.Application.Statements;
using DigitalWallet.Domain.Enums;
using DigitalWallet.Testing.Fakes;
using DigitalWallet.Testing.InMemory;

namespace DigitalWallet.UnitTests.Application;

/// <summary>
/// Builds the use cases over the in-memory fakes. Requests are accepted through the real use cases, and the
/// pipeline runs the real handlers. Methods that return an operation drain the pipeline first.
/// </summary>
internal sealed class WalletHarness
{
    public static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private WalletHarness()
    {
        Database = new InMemoryDatabase();
        Clock = new FakeClock(Now);
        Pipeline = new InMemoryPipeline(Database, Clock);
    }

    public InMemoryDatabase Database { get; }

    public FakeClock Clock { get; }

    public InMemoryPipeline Pipeline { get; }

    /// <summary>Creates a harness with the settlement account already initialized, as the API does at startup.</summary>
    public static async Task<WalletHarness> CreateAsync()
    {
        var harness = new WalletHarness();
        var unitOfWork = harness.Database.CreateUnitOfWork();
        var ensure = new EnsureSettlementAccountUseCase(unitOfWork.Accounts, unitOfWork, harness.Clock);
        await ensure.ExecuteAsync(CancellationToken.None);
        return harness;
    }

    public Task<AccountView> CreateAccountAsync(Guid ownerId, string? name = null)
    {
        var unitOfWork = Database.CreateUnitOfWork();
        var useCase = new CreateAccountUseCase(unitOfWork.Accounts, unitOfWork, Clock);
        return useCase.ExecuteAsync(new CreateAccountCommand(ownerId, name), CancellationToken.None);
    }

    public async Task<IReadOnlyList<AccountView>> ListAccountsAsync(Guid ownerId, int? limit = null, AccountStatus? status = null) =>
        (await ListPageAsync(ownerId, limit, status: status)).Items;

    public Task<AccountPage> ListPageAsync(Guid ownerId, int? limit = null, int? offset = null, AccountStatus? status = null)
    {
        var unitOfWork = Database.CreateUnitOfWork();
        var useCase = new ListAccountsUseCase(unitOfWork.Accounts);
        return useCase.ExecuteAsync(new ListAccountsQuery(ownerId, status, limit, offset), CancellationToken.None);
    }

    public Task<AccountView> SetActiveAsync(Guid accountId, bool active)
    {
        var unitOfWork = Database.CreateUnitOfWork();
        var useCase = new SetAccountActiveUseCase(unitOfWork.Accounts, unitOfWork);
        return useCase.ExecuteAsync(new SetAccountActiveCommand(accountId, active), CancellationToken.None);
    }

    public Task<AccountView> RenameAsync(Guid accountId, string? name)
    {
        var unitOfWork = Database.CreateUnitOfWork();
        var useCase = new RenameAccountUseCase(unitOfWork.Accounts, unitOfWork);
        return useCase.ExecuteAsync(new RenameAccountCommand(accountId, name), CancellationToken.None);
    }

    public Task<AccountSummary> SummaryAsync(Guid ownerId)
    {
        var unitOfWork = Database.CreateUnitOfWork();
        return new AccountSummaryUseCase(unitOfWork.Accounts).ExecuteAsync(ownerId, CancellationToken.None);
    }

    public Task<AccountView> GetAccountAsync(Guid accountId)
    {
        var unitOfWork = Database.CreateUnitOfWork();
        return new GetAccountUseCase(unitOfWork.Accounts).ExecuteAsync(accountId, CancellationToken.None);
    }

    /// <summary>Accepts a deposit without running the pipeline, so tests can inspect the accepted state.</summary>
    public async Task<OperationAccepted> AcceptDepositOnlyAsync(Guid accountId, decimal amount)
    {
        using var unitOfWork = Database.CreateUnitOfWork();
        var useCase = new AcceptDepositUseCase(unitOfWork.Accounts, unitOfWork.Operations, unitOfWork.Outbox, unitOfWork, Clock);
        return await useCase.ExecuteAsync(new AcceptDepositCommand(accountId, amount), CancellationToken.None);
    }

    public async Task<OperationAccepted> AcceptWithdrawalOnlyAsync(Guid accountId, decimal amount)
    {
        using var unitOfWork = Database.CreateUnitOfWork();
        var useCase = new AcceptWithdrawalUseCase(unitOfWork.Accounts, unitOfWork.Operations, unitOfWork.Outbox, unitOfWork, Clock);
        return await useCase.ExecuteAsync(new AcceptWithdrawalCommand(accountId, amount), CancellationToken.None);
    }

    public async Task<OperationAccepted> AcceptTransferOnlyAsync(Guid sourceAccountId, Guid destinationAccountId, decimal amount)
    {
        using var unitOfWork = Database.CreateUnitOfWork();
        var useCase = new AcceptTransferUseCase(unitOfWork.Accounts, unitOfWork.Operations, unitOfWork.Outbox, unitOfWork, Clock);
        return await useCase.ExecuteAsync(
            new AcceptTransferCommand(sourceAccountId, destinationAccountId, amount),
            CancellationToken.None);
    }

    public async Task<OperationView> DepositAsync(Guid accountId, decimal amount)
    {
        var accepted = await AcceptDepositOnlyAsync(accountId, amount);
        await Pipeline.DrainAsync();
        return await OperationAsync(accepted.OperationId);
    }

    public async Task<OperationView> WithdrawAsync(Guid accountId, decimal amount)
    {
        var accepted = await AcceptWithdrawalOnlyAsync(accountId, amount);
        await Pipeline.DrainAsync();
        return await OperationAsync(accepted.OperationId);
    }

    public async Task<OperationView> TransferAsync(Guid sourceAccountId, Guid destinationAccountId, decimal amount)
    {
        var accepted = await AcceptTransferOnlyAsync(sourceAccountId, destinationAccountId, amount);
        await Pipeline.DrainAsync();
        return await OperationAsync(accepted.OperationId);
    }

    public Task<OperationView> OperationAsync(Guid operationId)
    {
        using var unitOfWork = Database.CreateUnitOfWork();
        return new GetOperationUseCase(unitOfWork.Operations).ExecuteAsync(operationId, CancellationToken.None);
    }

    public Task<StatementPage> StatementAsync(Guid accountId, string? cursor = null, int? limit = null)
    {
        var reader = new InMemoryLedgerReader(Database);
        var unitOfWork = Database.CreateUnitOfWork();
        var useCase = new GetStatementUseCase(unitOfWork.Accounts, reader);
        return useCase.ExecuteAsync(new GetStatementQuery(accountId, cursor, limit), CancellationToken.None);
    }

    public async Task<Guid> CreateFundedAccountAsync(decimal amount, Guid? ownerId = null)
    {
        var account = await CreateAccountAsync(ownerId ?? Guid.NewGuid());
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

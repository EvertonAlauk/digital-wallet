using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql.EntityFrameworkCore.PostgreSQL;

namespace DigitalWallet.Infrastructure.Persistence;

/// <summary>
/// Used only by the EF Core tools (dotnet ef migrations). It lets migrations be generated without
/// starting the API or connecting to a running database.
/// </summary>
public sealed class WalletDbContextFactory : IDesignTimeDbContextFactory<WalletDbContext>
{
    public WalletDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<WalletDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=digital_wallet;Username=wallet;Password=wallet")
            .Options;

        return new WalletDbContext(options);
    }
}

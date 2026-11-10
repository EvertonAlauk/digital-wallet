using DigitalWallet.Domain.Entities;
using DigitalWallet.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DigitalWallet.Infrastructure.Persistence.Configurations;

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("accounts");

        builder.HasKey(account => account.Id);

        builder.Property(account => account.OwnerId).IsRequired();

        builder.Property(account => account.Name).HasMaxLength(Account.MaxNameLength);

        builder.Property(account => account.Kind)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(AccountKind.Customer);

        builder.Property(account => account.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        // Optimistic concurrency token: UPDATE ... WHERE version = <version read>.
        builder.Property(account => account.Version).IsConcurrencyToken();

        builder.Property(account => account.CreatedAt).IsRequired();

        builder.ComplexProperty(account => account.Balance, MoneyConfiguration.Configure);
    }
}

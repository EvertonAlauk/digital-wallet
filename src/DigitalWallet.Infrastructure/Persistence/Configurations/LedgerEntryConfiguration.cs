using DigitalWallet.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DigitalWallet.Infrastructure.Persistence.Configurations;

internal sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.ToTable("ledger_entries");

        // Persistence-only surrogate key. The domain model does not expose an identity for entries.
        builder.Property<long>("Id").ValueGeneratedOnAdd();
        builder.HasKey("Id");

        builder.Property(entry => entry.AccountId).IsRequired();
        builder.Property(entry => entry.TransactionId).IsRequired();

        builder.ComplexProperty(entry => entry.Amount, MoneyConfiguration.Configure);

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(entry => entry.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entry => entry.AccountId);
        builder.HasIndex(entry => entry.TransactionId);
    }
}

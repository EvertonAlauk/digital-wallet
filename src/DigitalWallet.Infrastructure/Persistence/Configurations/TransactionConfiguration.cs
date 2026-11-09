using DigitalWallet.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DigitalWallet.Infrastructure.Persistence.Configurations;

internal sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("transactions");

        builder.HasKey(transaction => transaction.Id);

        builder.Property(transaction => transaction.Type)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(transaction => transaction.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(transaction => transaction.CreatedAt).IsRequired();

        // Entries are exposed as a read-only view over a private list. Map the list by field name and
        // ignore the view, so EF does not try to map it a second time.
        builder.Ignore(transaction => transaction.Entries);

        builder.HasMany<LedgerEntry>("_entries")
            .WithOne()
            .HasForeignKey(entry => entry.TransactionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation("_entries").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

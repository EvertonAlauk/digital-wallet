using DigitalWallet.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DigitalWallet.Infrastructure.Persistence.Configurations;

internal sealed class OperationConfiguration : IEntityTypeConfiguration<Operation>
{
    public void Configure(EntityTypeBuilder<Operation> builder)
    {
        builder.ToTable("operations");

        builder.HasKey(operation => operation.Id);

        builder.Property(operation => operation.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(operation => operation.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(operation => operation.AccountId).IsRequired();
        builder.Property(operation => operation.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(operation => operation.FailureReason).HasMaxLength(512);
        builder.Property(operation => operation.CreatedAt).IsRequired();

        builder.HasIndex(operation => operation.AccountId);
    }
}

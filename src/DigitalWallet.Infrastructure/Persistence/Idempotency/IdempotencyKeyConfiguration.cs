using DigitalWallet.Infrastructure.Persistence.Idempotency;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DigitalWallet.Infrastructure.Persistence.Configurations;

internal sealed class IdempotencyKeyConfiguration : IEntityTypeConfiguration<IdempotencyKeyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyKeyRecord> builder)
    {
        builder.ToTable("idempotency_keys");

        // The primary key is what makes the key unique: concurrent inserts of the same key cannot both succeed.
        builder.HasKey(record => record.Key);
        builder.Property(record => record.Key).HasMaxLength(255);
        builder.Property(record => record.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(record => record.StartedAt).IsRequired();
        builder.Property(record => record.ExpiresAt).IsRequired();
        builder.Property(record => record.ResponseLocation).HasMaxLength(512);
        builder.Property(record => record.ResponseContentType).HasMaxLength(128);

        // Used to purge expired keys.
        builder.HasIndex(record => record.ExpiresAt);
    }
}

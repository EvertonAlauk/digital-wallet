using DigitalWallet.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DigitalWallet.Infrastructure.Persistence.Configurations;

internal sealed class ProcessedMessageConfiguration : IEntityTypeConfiguration<ProcessedMessageRecord>
{
    public void Configure(EntityTypeBuilder<ProcessedMessageRecord> builder)
    {
        builder.ToTable("processed_messages");

        // The primary key makes "already processed" atomic, the same way idempotency keys work.
        builder.HasKey(message => message.MessageId);
        builder.Property(message => message.ProcessedAt).IsRequired();
    }
}

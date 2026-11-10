using DigitalWallet.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DigitalWallet.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessageRecord>
{
    public void Configure(EntityTypeBuilder<OutboxMessageRecord> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(message => message.Id);
        builder.Property(message => message.Sequence).ValueGeneratedOnAdd();
        builder.HasIndex(message => message.Sequence).IsUnique();

        builder.Property(message => message.Type).HasMaxLength(128).IsRequired();
        builder.Property(message => message.Payload).IsRequired();
        builder.Property(message => message.OccurredAt).IsRequired();

        // Pending messages are the ones without PublishedAt; the relay reads them in Sequence order.
        builder.HasIndex(message => new { message.PublishedAt, message.Sequence });
    }
}

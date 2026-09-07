using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        // Primary key with UUID v7 for time-ordering
        builder.Property(e => e.Id).HasDefaultValueSql("uuidv7()");

        // Aggregate correlation fields
        builder.Property(e => e.AggregateType).HasMaxLength(200).IsRequired();
        builder.Property(e => e.EventType).HasMaxLength(200).IsRequired();

        // Payload stored as JSONB for PostgreSQL efficiency
        builder.Property(e => e.Payload).HasColumnType("jsonb");

        // Status (stored as integer)
        builder.Property(e => e.Status).IsRequired();

        // Error tracking
        builder.Property(e => e.LastError).HasMaxLength(2000);

        // Dead-letter: default 10 retries before quarantine
        builder.Property(e => e.MaxRetries).HasDefaultValue(10);

        // Primary index for background processor polling: pending items ordered by creation time
        builder.HasIndex(e => new { e.Status, e.NextRetryAt, e.CreatedAt })
            .HasDatabaseName("IX_OutboxMessages_WorkerPoll");

        // Index for aggregate correlation queries (find all messages for an entity)
        builder.HasIndex(e => new { e.AggregateType, e.AggregateId })
            .HasDatabaseName("IX_OutboxMessages_Aggregate");

        // Index for idempotency checks (prevent duplicate messages for the same event)
        builder.HasIndex(e => new { e.AggregateType, e.AggregateId, e.EventType, e.CreatedAt })
            .HasDatabaseName("IX_OutboxMessages_Dedup");
    }
}

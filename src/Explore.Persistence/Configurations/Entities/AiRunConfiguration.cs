using Explore.Domain.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class AiRunConfiguration : IEntityTypeConfiguration<AiRun>
{
    public void Configure(EntityTypeBuilder<AiRun> builder)
    {
        builder.Property(e => e.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(e => e.StatusId)
            .HasDefaultValue((int)AiRunStatus.Queued)
            .IsRequired();
        builder.Property(e => e.Provider).HasMaxLength(100).IsRequired();
        builder.Property(e => e.ModelId).HasMaxLength(200).IsRequired();
        builder.Property(e => e.FailureCode).HasMaxLength(100);
        builder.Property(e => e.FailureMessage).HasMaxLength(1000);

        builder.HasOne(e => e.StatusLookup)
            .WithMany()
            .HasForeignKey(e => e.StatusId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.StatusId, e.QueuedAt })
            .HasDatabaseName("ix_ai_runs_tenant_status_queued_at");

        builder.HasIndex(e => new { e.TenantId, e.ConversationId, e.QueuedAt })
            .HasDatabaseName("ix_ai_runs_tenant_conversation_queued_at");

    }
}

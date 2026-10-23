using Explore.Domain.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class AiToolExecutionConfiguration : IEntityTypeConfiguration<AiToolExecution>
{
    public void Configure(EntityTypeBuilder<AiToolExecution> builder)
    {
        builder.Property(e => e.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(e => e.ToolName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.FailureCode).HasMaxLength(100);
        builder.Property(e => e.FailureMessage).HasMaxLength(1000);
        builder.Property(e => e.PayloadDigest).HasMaxLength(64).IsRequired();
        builder.Property(e => e.ImageSourceDigest).HasMaxLength(64);
        builder.Property(e => e.ImageUploadOperationKey).HasMaxLength(100);
        builder.Property(e => e.ConcurrencyStamp).IsConcurrencyToken();

        builder.HasOne(e => e.ProposedAction)
            .WithMany()
            .HasForeignKey(e => e.ProposedActionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.ProposedActionId).IsUnique()
            .HasDatabaseName("ux_ai_tool_executions_action");

        builder.HasIndex(e => e.ReservedEventId).IsUnique()
            .HasDatabaseName("ux_ai_tool_executions_reserved_event");

        builder.HasOne<Explore.Domain.Event>().WithMany()
            .HasForeignKey(e => new { e.TenantId, e.PrimaryEventId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.ToolName, e.StartedAt })
            .HasDatabaseName("ix_ai_tool_executions_tenant_tool_started_at");
    }
}

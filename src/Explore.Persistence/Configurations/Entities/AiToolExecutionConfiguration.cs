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

        builder.HasOne(e => e.ProposedAction)
            .WithMany()
            .HasForeignKey(e => e.ProposedActionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.TenantId, e.ProposedActionId, e.StartedAt })
            .HasDatabaseName("ix_ai_tool_executions_tenant_action_started_at");

        builder.HasIndex(e => new { e.TenantId, e.ToolName, e.StartedAt })
            .HasDatabaseName("ix_ai_tool_executions_tenant_tool_started_at");
    }
}

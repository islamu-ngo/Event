using Explore.Domain.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class AiProposedActionConfiguration : IEntityTypeConfiguration<AiProposedAction>
{
    public void Configure(EntityTypeBuilder<AiProposedAction> builder)
    {
        builder.Property(e => e.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(e => e.KindId).IsRequired();
        builder.Property(e => e.StatusId)
            .HasDefaultValue((int)AiProposedActionStatus.Proposed)
            .IsRequired();
        builder.Property(e => e.PayloadJson).HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.FailureCode).HasMaxLength(100);
        builder.Property(e => e.FailureMessage).HasMaxLength(1000);

        builder.HasOne(e => e.Message)
            .WithMany()
            .HasForeignKey(e => e.MessageId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.ActingActor)
            .WithMany()
            .HasForeignKey(e => e.ActingActorId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.KindLookup)
            .WithMany()
            .HasForeignKey(e => e.KindId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.StatusLookup)
            .WithMany()
            .HasForeignKey(e => e.StatusId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.ConversationId, e.StatusId, e.CreatedAt })
            .HasDatabaseName("ix_ai_proposed_actions_tenant_conversation_status_created_at");

        builder.HasIndex(e => new { e.TenantId, e.StatusId, e.KindId, e.CreatedAt })
            .HasDatabaseName("ix_ai_proposed_actions_tenant_status_kind_created_at");

        builder.HasIndex(e => new { e.TenantId, e.ActingActorId, e.CreatedAt })
            .HasFilter("acting_actor_id IS NOT NULL")
            .HasDatabaseName("ix_ai_proposed_actions_tenant_acting_actor_created_at")
            .IsDescending(false, false, true);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_ai_proposed_actions_payload_object", "jsonb_typeof(payload_json) = 'object'");
        });
    }
}

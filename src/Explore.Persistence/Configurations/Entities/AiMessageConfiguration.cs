using Explore.Domain.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class AiMessageConfiguration : IEntityTypeConfiguration<AiMessage>
{
    public void Configure(EntityTypeBuilder<AiMessage> builder)
    {
        builder.Property(e => e.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(e => e.RoleId).IsRequired();
        builder.Property(e => e.Content).HasMaxLength(16000).IsRequired();
        builder.Property(e => e.ImageAttachmentsJson).HasColumnType("jsonb");

        builder.HasOne(e => e.RoleLookup)
            .WithMany()
            .HasForeignKey(e => e.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.ConversationId, e.Sequence })
            .IsUnique()
            .HasDatabaseName("ux_ai_messages_tenant_conversation_sequence");

        builder.HasIndex(e => new { e.TenantId, e.ConversationId, e.CreatedAt })
            .HasDatabaseName("ix_ai_messages_tenant_conversation_created_at");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_ai_messages_sequence_positive", "sequence > 0");
        });
    }
}

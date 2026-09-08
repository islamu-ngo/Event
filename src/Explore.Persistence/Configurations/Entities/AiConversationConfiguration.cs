using Explore.Domain.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class AiConversationConfiguration : IEntityTypeConfiguration<AiConversation>
{
    public void Configure(EntityTypeBuilder<AiConversation> builder)
    {
        builder.Property(e => e.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(e => e.StatusId)
            .HasDefaultValue((int)AiConversationStatus.Active)
            .IsRequired();
        builder.Property(e => e.Title).HasMaxLength(200);
        builder.Property(e => e.Provider).HasMaxLength(100);
        builder.Property(e => e.ModelId).HasMaxLength(200);
        builder.Property(e => e.BlockedReason).HasMaxLength(200);
        builder.Property(e => e.ConcurrencyStamp).IsConcurrencyToken();

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Actor)
            .WithMany()
            .HasForeignKey(e => e.ActorId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.StatusLookup)
            .WithMany()
            .HasForeignKey(e => e.StatusId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.Messages)
            .WithOne(e => e.Conversation)
            .HasForeignKey(e => e.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.Runs)
            .WithOne(e => e.Conversation)
            .HasForeignKey(e => e.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.References)
            .WithOne(e => e.Conversation)
            .HasForeignKey(e => e.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.ProposedActions)
            .WithOne(e => e.Conversation)
            .HasForeignKey(e => e.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.TenantId, e.UserId, e.StatusId, e.UpdatedAt })
            .HasDatabaseName("ix_ai_conversations_tenant_user_status_updated_at")
            .IsDescending(false, false, false, true);

        builder.HasIndex(e => new { e.TenantId, e.ActorId, e.UpdatedAt })
            .HasFilter("actor_id IS NOT NULL")
            .HasDatabaseName("ix_ai_conversations_tenant_actor_updated_at")
            .IsDescending(false, false, true);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_ai_conversations_last_message_sequence_nonnegative", "last_message_sequence >= 0");
        });
    }
}

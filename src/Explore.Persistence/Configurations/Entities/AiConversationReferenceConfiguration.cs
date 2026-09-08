using Explore.Domain.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class AiConversationReferenceConfiguration : IEntityTypeConfiguration<AiConversationReference>
{
    public void Configure(EntityTypeBuilder<AiConversationReference> builder)
    {
        builder.Property(e => e.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(e => e.KindId).IsRequired();
        builder.Property(e => e.DisplayName).HasMaxLength(500).IsRequired();
        builder.Property(e => e.Summary).HasMaxLength(2000);

        builder.HasOne(e => e.KindLookup)
            .WithMany()
            .HasForeignKey(e => e.KindId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.ConversationId, e.KindId, e.ReferenceId })
            .IsUnique()
            .HasDatabaseName("ux_ai_conversation_references_identity");
    }
}

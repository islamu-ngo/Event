using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Moderation;

public sealed class ModerationRecognitionEscalationConfiguration : IEntityTypeConfiguration<ModerationRecognitionEscalation>
{
    public void Configure(EntityTypeBuilder<ModerationRecognitionEscalation> builder)
    {
        builder.HasKey(row => row.Id);
        builder.HasQueryFilter("ModerationRecognition", row => false);
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.HasIndex(row => new { row.TargetId, row.SourceId, row.RecipientHumanId, row.Stage }).IsUnique();
        builder.HasIndex(row => new { row.RecipientHumanId, row.AcknowledgedAt });
        builder.HasIndex(row => row.RetainUntil);
    }
}

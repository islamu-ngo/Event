using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Moderation;

public sealed class ModerationRecognitionContinuityConfiguration : IEntityTypeConfiguration<ModerationRecognitionContinuityBinding>
{
    public void Configure(EntityTypeBuilder<ModerationRecognitionContinuityBinding> builder)
    {
        builder.HasKey(row => row.Id);
        builder.Property(row => row.CaptureMaterialReceipt).HasMaxLength(64).IsRequired();
        builder.HasQueryFilter("ModerationRecognitionContinuity", row => false);
        builder.HasIndex(row => new { row.FreshSubjectId, row.SanctionId }).IsUnique();
        builder.HasIndex(row => row.RetainUntil);
        builder.HasOne<ModerationRecognitionCaptureItem>().WithMany()
            .HasForeignKey(row => row.CaptureItemId).OnDelete(DeleteBehavior.Restrict);
        // No User navigation: erasure never cascades into reassignment of original authority.
    }
}

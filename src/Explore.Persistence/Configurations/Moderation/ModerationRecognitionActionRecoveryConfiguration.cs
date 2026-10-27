using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Moderation;

public sealed class ModerationRecognitionActionRecoveryConfiguration :
    IEntityTypeConfiguration<ModerationRecognitionActionRecovery>
{
    public void Configure(EntityTypeBuilder<ModerationRecognitionActionRecovery> builder)
    {
        builder.HasKey(row => row.Id);
        builder.Property(row => row.CaptureMaterialReceipt).HasMaxLength(64).IsRequired();
        builder.HasQueryFilter("ModerationRecognitionActionRecovery", row => false);
        builder.HasIndex(row => new { row.FreshSubjectId, row.VerifiedBindingId, row.CaptureItemId }).IsUnique();
        builder.HasIndex(row => new { row.ResolvedAt, row.DispositionBy });
        builder.HasOne<ModerationRecognitionCaptureItem>().WithMany()
            .HasForeignKey(row => row.CaptureItemId).OnDelete(DeleteBehavior.Restrict);
    }
}

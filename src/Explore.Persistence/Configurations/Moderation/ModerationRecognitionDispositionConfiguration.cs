using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Moderation;

public sealed class ModerationRecognitionDispositionConfiguration :
    IEntityTypeConfiguration<ModerationRecognitionDisposition>,
    IEntityTypeConfiguration<ModerationRecognitionDispositionRemedy>,
    IEntityTypeConfiguration<ModerationRecognitionDispositionAcceptance>
{
    public void Configure(EntityTypeBuilder<ModerationRecognitionDispositionAcceptance> builder)
    {
        builder.HasKey(row => new { row.DispositionId, row.Role });
        builder.HasQueryFilter("ModerationRecognition", row => false);
        builder.Property(row => row.RequestDigest).HasMaxLength(64).IsRequired();
        builder.HasOne<ModerationRecognitionDisposition>().WithMany().HasForeignKey(row => row.DispositionId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<ModerationRecognitionDisposition> builder)
    {
        builder.HasKey(row => row.Id);
        builder.HasQueryFilter("ModerationRecognition", row => false);
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.Property(row => row.OriginalMaterialReceipt).HasMaxLength(64);
        builder.Property(row => row.ObservedMaterialCommitment).HasMaxLength(64).IsRequired();
        builder.Property(row => row.CoverageReceipt).HasMaxLength(64).IsRequired();
        builder.Property(row => row.GroundsReceipt).HasMaxLength(64).IsRequired();
        builder.Property(row => row.LegalMandateReceipt).HasMaxLength(64).IsRequired();
        builder.HasIndex(row => new { row.IntentId, row.PendingRevision, row.SourceId }).IsUnique();
        builder.HasIndex(row => row.RetainUntil);
    }

    public void Configure(EntityTypeBuilder<ModerationRecognitionDispositionRemedy> builder)
    {
        builder.HasKey(row => row.Id);
        builder.HasQueryFilter("ModerationRecognition", row => false);
        builder.Property(row => row.ReceiptHash).HasMaxLength(32).IsRequired();
        builder.Property(row => row.OriginalMaterialReceipt).HasMaxLength(64);
        builder.Property(row => row.ObservedMaterialCommitment).HasMaxLength(64).IsRequired();
        builder.Property(row => row.CoverageReceipt).HasMaxLength(64).IsRequired();
        builder.Property(row => row.GroundsReceipt).HasMaxLength(64).IsRequired();
        builder.Property(row => row.LegalMandateReceipt).HasMaxLength(64).IsRequired();
        builder.HasIndex(row => row.ReceiptHash);
        builder.HasIndex(row => row.RetainUntil);
        // No FK to the shorter replay receipt: each approved record class has its own ceiling.
    }
}

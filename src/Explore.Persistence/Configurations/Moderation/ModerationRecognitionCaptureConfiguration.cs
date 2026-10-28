using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Moderation;

public sealed class ModerationRecognitionCaptureConfiguration :
    IEntityTypeConfiguration<ModerationRecognitionPending>, IEntityTypeConfiguration<ModerationRecognitionCaptureItem>
{
    public void Configure(EntityTypeBuilder<ModerationRecognitionPending> builder)
    {
        builder.HasKey(row => row.IntentId);
        builder.HasQueryFilter("ModerationRecognition", row => false);
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.Property(row => row.ReceiptHash).HasMaxLength(32).IsRequired();
        builder.Property(row => row.MaterialReceipt).HasMaxLength(64);
        builder.HasIndex(row => row.SubjectId).IsUnique();
        builder.HasIndex(row => row.ReceiptHash).IsUnique();
        builder.HasIndex(row => new { row.State, row.EscalateAt });
        builder.HasMany(row => row.Material).WithOne().HasForeignKey(row => row.IntentId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(row => row.Material).HasField("material").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(row => row.HasCompleteMaterial);
    }

    public void Configure(EntityTypeBuilder<ModerationRecognitionCaptureItem> builder)
    {
        builder.HasKey(row => row.Id);
        builder.HasQueryFilter("ModerationRecognition", row => false);
        builder.Property(row => row.KeyId).HasMaxLength(64);
        builder.Property(row => row.Digest).HasMaxLength(64);
        builder.Property(row => row.KeyVerificationTag).HasMaxLength(64);
        builder.Property(row => row.RuleCode).HasMaxLength(100);
        builder.HasIndex(row => new { row.IntentId, row.ProviderKind, row.Purpose, row.Digest, row.SanctionId }).IsUnique();
        builder.HasIndex(row => new { row.TrustDomainId, row.ProviderKind, row.Purpose, row.KeyId, row.Digest });
        builder.HasOne<ModerationRecognitionCaptureItem>().WithMany().HasForeignKey(row => row.ParentCaptureItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationRecognitionContinuityBinding>().WithMany().HasForeignKey(row => row.ContinuityBindingId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Ignore(row => row.Scope);
        ModerationLedgerConfiguration.ConfigureScope(builder, "scope", trustColumn: "trust_domain_id");
        builder.Ignore(row => row.Token);
    }
}

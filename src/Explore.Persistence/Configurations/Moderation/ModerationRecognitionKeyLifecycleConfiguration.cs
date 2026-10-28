using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Moderation;

public sealed class ModerationRecognitionKeyLifecycleConfiguration :
    IEntityTypeConfiguration<ModerationRecognitionKeyLifecycle>,
    IEntityTypeConfiguration<ModerationRecognitionKeyLifecycleKey>,
    IEntityTypeConfiguration<KeyObligation>, IEntityTypeConfiguration<KeyReplica>,
    IEntityTypeConfiguration<ModerationRecognitionKeyLifecyclePurge>
{
    public void Configure(EntityTypeBuilder<ModerationRecognitionKeyLifecycle> builder)
    {
        builder.HasKey(row => row.Id);
        builder.HasQueryFilter("ModerationRecognitionKeyLifecycle", row => false);
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.Property(row => row.AcknowledgementSequence).IsConcurrencyToken();
        builder.Property(row => row.InitialErasureHead).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        builder.Property(row => row.InitialErasureRetainedFloor).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        builder.HasMany(row => row.Keys).WithOne().HasForeignKey(row => row.TrustDomainId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(row => row.Keys).HasField("keys").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(row => row.Commitment);
    }

    public void Configure(EntityTypeBuilder<ModerationRecognitionKeyLifecycleKey> builder)
    {
        builder.HasKey(row => new { row.TrustDomainId, row.KeyId });
        builder.HasQueryFilter("ModerationRecognitionKeyLifecycle", row => false);
        builder.HasIndex(row => new { row.TrustDomainId, row.KeyGeneration }).IsUnique();
        builder.Property(row => row.KeyId).HasMaxLength(64);
        builder.Property(row => row.VerificationTag).HasMaxLength(64);
        builder.Property(row => row.KeyGeneration).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        builder.Property(row => row.VerificationTag).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        builder.Property(row => row.OriginalObligationsUntil).IsConcurrencyToken();
        builder.Property(row => row.RequiredErasureHead).IsConcurrencyToken();
    }

    public void Configure(EntityTypeBuilder<KeyObligation> builder)
    {
        builder.HasKey(row => row.Id);
        builder.HasQueryFilter("ModerationRecognitionKeyLifecycle", row => false);
        builder.HasIndex(row => new { row.TrustDomainId, row.KeyId, row.KeyGeneration });
        builder.HasIndex(row => row.IntentId);
        builder.Property(row => row.KeyId).HasMaxLength(64);
        builder.Property(row => row.VerificationTag).HasMaxLength(64);
        // No capture FK: an obligation outlives purpose-limited token removal.
        builder.Property(row => row.CaptureMaterialReceipt).HasMaxLength(64);
        builder.Property(row => row.MaterialReceipt).HasMaxLength(64);
        foreach (var property in builder.Metadata.GetProperties().Where(property =>
                     property.Name is not (nameof(KeyObligation.AuthoritySequence) or nameof(KeyObligation.MaterialPrunedAt)
                         or nameof(KeyObligation.ReceiptRetainUntil))))
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }

    public void Configure(EntityTypeBuilder<KeyReplica> builder)
    {
        builder.HasKey(row => row.Id);
        builder.HasQueryFilter("ModerationRecognitionKeyLifecycle", row => false);
        builder.HasIndex(row => new { row.TrustDomainId, row.RuntimeReplicaId, row.RingGeneration });
        builder.HasIndex(row => new { row.TrustDomainId, row.Sequence }).IsUnique();
        builder.Property(row => row.RingCommitment).HasMaxLength(64);
        builder.Property(row => row.BindingRevision).HasMaxLength(256);
        foreach (var property in builder.Metadata.GetProperties()) property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }

    public void Configure(EntityTypeBuilder<ModerationRecognitionKeyLifecyclePurge> builder)
    {
        builder.HasKey(row => row.IntentId);
        builder.HasQueryFilter("ModerationRecognitionKeyLifecycle", row => false);
        builder.HasIndex(row => row.AuthoritySequence).IsUnique();
        builder.Property(row => row.MaterialReceipt).HasMaxLength(64);
    }
}

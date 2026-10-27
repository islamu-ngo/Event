using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Moderation;

public sealed class ModerationConsumerMeasurementChallengeConfiguration
    : IEntityTypeConfiguration<ModerationConsumerMeasurementChallenge>
{
    public void Configure(EntityTypeBuilder<ModerationConsumerMeasurementChallenge> builder)
    {
        builder.ToTable("moderation_consumer_measurement_challenges", table =>
        {
            table.HasCheckConstraint("ck_moderation_measurement_challenge_source",
                "installation_revision = 1 AND installation_writer_generation > 0 AND writer_generation > installation_writer_generation AND revision = 1");
            table.HasCheckConstraint("ck_moderation_measurement_challenge_owner", "consumer_id <> '' AND mode_code = 1");
            table.HasCheckConstraint("ck_moderation_measurement_challenge_validity", "valid_until > prepared_at");
            table.HasCheckConstraint("ck_moderation_measurement_challenge_original",
                "(original_effect_id IS NULL AND original_snapshot_hash IS NULL AND original_effect_binding_hash IS NULL) OR (original_effect_id IS NOT NULL AND original_snapshot_hash IS NOT NULL AND original_effect_binding_hash IS NOT NULL)");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.ConsumerId).HasMaxLength(64).IsRequired();
        foreach (var name in new[] { nameof(ModerationConsumerMeasurementChallenge.RuntimeGeneration),
            nameof(ModerationConsumerMeasurementChallenge.InventoryFingerprint), nameof(ModerationConsumerMeasurementChallenge.BindingDigest),
            nameof(ModerationConsumerMeasurementChallenge.ConsumerContractDigest), nameof(ModerationConsumerMeasurementChallenge.Nonce) })
            builder.Property<string>(name).HasMaxLength(64).IsRequired();
        builder.HasIndex(row => row.Nonce).IsUnique();
        builder.Property(row => row.OriginalSnapshotHash).HasMaxLength(64);
        builder.Property(row => row.OriginalEffectBindingHash).HasMaxLength(64);
        builder.HasOne<ModerationEffectIntent>().WithMany().HasForeignKey(row => row.OriginalEffectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationHostBinding>().WithMany().HasForeignKey(row => row.HostBindingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(row => row.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationConsumerInstallation>().WithMany().HasForeignKey(row => row.InstallationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationPolicyPublication>().WithMany().HasForeignKey(row => row.DefinitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.PreparedByHumanId).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}

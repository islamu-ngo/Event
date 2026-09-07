using Explore.Domain;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class PrivacyErasureReplayCheckpointConfiguration
    : IEntityTypeConfiguration<PrivacyErasureReplayCheckpoint>
{
    public void Configure(EntityTypeBuilder<PrivacyErasureReplayCheckpoint> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_privacy_erasure_replay_checkpoints_sequence",
                "authority_sequence > 0");
            table.HasCheckConstraint(
                "ck_privacy_erasure_replay_checkpoints_chain",
                "(authority_sequence = 1 AND previous_checkpoint_id IS NULL) OR " +
                "(authority_sequence > 1 AND previous_checkpoint_id IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_privacy_erasure_checkpoints_uuid_v7",
                "substring(id::text, 15, 1) = '7' AND substring(id::text, 20, 1) IN ('8', '9', 'a', 'b') AND " +
                "substring(intent_id::text, 15, 1) = '7' AND substring(intent_id::text, 20, 1) IN ('8', '9', 'a', 'b')");
        });

        builder.Property(item => item.Id).HasValueGenerator<GuidVersion7ValueGenerator>();
        builder.HasIndex(item => item.AuthoritySequence)
            .HasDatabaseName("ux_privacy_erasure_checkpoints_sequence")
            .IsUnique();
        builder.HasIndex(item => item.IntentId)
            .HasDatabaseName("ux_privacy_erasure_checkpoints_intent")
            .IsUnique();
        builder.HasIndex(item => item.PreviousCheckpointId)
            .HasDatabaseName("ux_privacy_erasure_checkpoints_previous")
            .IsUnique();
        builder.Property(item => item.SubjectKind).HasConversion<short>();
        builder.Property(item => item.ReasonCode).HasConversion<short>();
        builder.HasOne<PrivacyErasureReplayCheckpoint>()
            .WithMany()
            .HasForeignKey(item => item.PreviousCheckpointId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

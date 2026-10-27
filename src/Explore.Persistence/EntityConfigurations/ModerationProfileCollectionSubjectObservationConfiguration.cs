using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Explore.Persistence.EntityConfigurations;

/// <summary>Private original subject metadata with one PII deletion path and no mutable renewal state.</summary>
public sealed class ModerationProfileCollectionSubjectObservationConfiguration
    : IEntityTypeConfiguration<ModerationProfileCollectionSubjectObservation>
{
    public void Configure(EntityTypeBuilder<ModerationProfileCollectionSubjectObservation> builder)
    {
        builder.ToTable("moderation_profile_collection_subject_observations", table =>
        {
            table.HasCheckConstraint("ck_profile_subject_observation_generations",
                "revision = 1 AND generation > 0 AND generation < 9223372036854775807 " +
                "AND observation_sequence = generation AND policy_revision > 0 " +
                "AND policy_revision < 9223372036854775807 AND procedure_source_revision = 1 " +
                "AND procedure_source_writer_generation > 0 AND procedure_adoption_revision = 1 " +
                "AND procedure_adoption_writer_generation > procedure_source_writer_generation " +
                "AND epoch_revision >= procedure_adoption_writer_generation AND epoch_revision < 9223372036854775807");
            table.HasCheckConstraint("ck_profile_subject_observation_scope",
                $"jurisdiction_kind IN ({(int)ModerationScopeKind.Instance}, {(int)ModerationScopeKind.Tenant}) " +
                "AND mode_code BETWEEN 1 AND 2");
            table.HasCheckConstraint("ck_profile_subject_observation_window",
                "observed_at > 0 AND valid_until < 3155378975999999999 AND valid_until > observed_at " +
                "AND procedure_adopted_at > 0 AND procedure_adoption_valid_until < 3155378975999999999 " +
                "AND procedure_adoption_valid_until > procedure_adopted_at " +
                "AND observed_at >= procedure_adopted_at AND valid_until <= procedure_adoption_valid_until");
            table.HasCheckConstraint("ck_profile_subject_observation_identity",
                "id <> subject_human_id AND id <> observation_stamp AND id <> procedure_source_id " +
                "AND id <> procedure_adoption_id AND observation_stamp <> subject_human_id");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        // A policy or procedure change must not reset the subject observation slot.
        builder.HasIndex(row => new
        { row.HostBindingId, row.TenantId, row.SubjectHumanId, row.ProfileId, row.ModeCode, row.ObservationSequence })
            .HasDatabaseName("ux_profile_subject_observation_slot").IsUnique();
        builder.HasIndex(row => row.ObservationStamp).IsUnique();
        builder.HasOne<UserPii>().WithMany().HasForeignKey(row => row.SubjectHumanId)
            .HasPrincipalKey(row => row.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(row => row.ProcedureSource).WithMany().HasForeignKey(row => row.ProcedureSourceId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(row => row.ProcedureSource).AutoInclude();
        builder.HasOne<ModerationProfileCollectionProcedureAdoption>().WithMany()
            .HasForeignKey(row => row.ProcedureAdoptionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationQualificationEpoch>().WithMany().HasForeignKey(row => row.EpochId)
            .OnDelete(DeleteBehavior.Restrict);
        // The optional original login row ID is a private lookup anchor, not a provider key.
        // No FK may block unlink/replacement or create a second cascading path from User.
        // A missing/replaced login makes the original unavailable at every native read.
        builder.Property(row => row.SubjectLoginId);
        builder.Property(row => row.OriginalProcedureDigest).HasMaxLength(64).IsRequired();
        builder.Property(row => row.OriginalPolicyDigest).HasMaxLength(64).IsRequired();
        builder.Property(row => row.OriginalSelectionDigest).HasMaxLength(64).IsRequired();
        var instant = new ValueConverter<DateTimeOffset, long>(
            value => ModerationProfileSubjectConsentSnapshot.EncodeInstant(value),
            value => ModerationProfileSubjectConsentSnapshot.DecodeInstant(value));
        builder.Property(row => row.ObservedAt).HasConversion(instant);
        builder.Property(row => row.ValidUntil).HasConversion(instant);
        builder.Property(row => row.ProcedureAdoptedAt).HasConversion(instant);
        builder.Property(row => row.ProcedureAdoptionValidUntil).HasConversion(instant);
        foreach (var property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}

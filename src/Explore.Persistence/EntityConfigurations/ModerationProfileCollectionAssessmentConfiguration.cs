using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Explore.Persistence.EntityConfigurations;

/// <summary>
/// Private PII-owned applicability proposal metadata only. Immutable procedure history holds
/// the original child-age coordinates; no native lawful determination or receipt is supplied.
/// </summary>
public sealed class ModerationProfileCollectionAssessmentSourceConfiguration
    : IEntityTypeConfiguration<ModerationProfileCollectionAssessmentSource>
{
    public void Configure(EntityTypeBuilder<ModerationProfileCollectionAssessmentSource> builder)
    {
        builder.ToTable("moderation_profile_collection_assessment_sources", table =>
        {
            table.HasCheckConstraint("ck_profile_assessment_source_generation",
                "revision = 1 AND generation > 0 AND generation < 9223372036854775807 " +
                "AND procedure_adoption_revision = 1 AND procedure_adoption_writer_generation > 0 " +
                "AND procedure_adoption_writer_generation < 9223372036854775807");
            table.HasCheckConstraint("ck_profile_assessment_source_required", "required BETWEEN 0 AND 7");
            table.HasCheckConstraint("ck_profile_assessment_source_independence",
                "recorded_by_human_id <> procedure_adopted_by_human_id " +
                "AND procedure_adopted_by_human_id <> owner_controller_human_id " +
                "AND owner_controller_human_id <> reviewer_controller_human_id");
            table.HasCheckConstraint("ck_profile_assessment_source_window",
                "recorded_at > 0 AND valid_until < 3155378975999999999 AND valid_until > recorded_at " +
                "AND procedure_adopted_at > 0 AND procedure_adoption_valid_until < 3155378975999999999 " +
                "AND procedure_adoption_valid_until > procedure_adopted_at " +
                "AND recorded_at >= procedure_adopted_at AND valid_until <= procedure_adoption_valid_until");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.HasIndex(row => new { row.SubjectHumanId, row.ProcedureSourceId, row.Generation }).IsUnique();

        // The four scalar originals are necessary for real relational keys. EF cannot populate
        // shadow coordinates from the factory-only binding or computed procedure ownership.
        builder.HasOne<UserPii>().WithMany().HasForeignKey(row => row.SubjectHumanId)
            .HasPrincipalKey(row => row.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(row => row.ProcedureSource).WithMany().HasForeignKey(row => row.ProcedureSourceId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(row => row.ProcedureSource).AutoInclude();
        builder.HasOne<ModerationProfileCollectionProcedureAdoption>().WithMany()
            .HasForeignKey(row => row.ProcedureAdoptionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.RecordedByHumanId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.ProcedureAdoptedByHumanId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.OwnerControllerHumanId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.ReviewerControllerHumanId)
            .OnDelete(DeleteBehavior.Restrict);

        // The unchanged consent binding codec has exactly the same Human/ProfileCompletion
        // semantics. Its subject-required assessment codec is deliberately not used.
        builder.Property(row => row.Binding).HasConversion(
            value => ModerationProfileSubjectConsentSnapshot.EncodeBinding(value),
            value => ModerationProfileSubjectConsentSnapshot.DecodeBinding(value))
            .HasMaxLength(4096).IsRequired();
        // All eight explicit complete sets are representable, including positive empty (0).
        // This finite scalar encoding is not a prerequisite or an inference from allowed fields.
        builder.Property(row => row.Required).HasConversion(
            value => EncodeRequired(value), value => DecodeRequired(value)).IsRequired()
            .Metadata.SetValueComparer(new ValueComparer<IReadOnlyList<ModerationCollectionPrerequisiteKind>>(
                (left, right) => left == right || left != null && right != null && left.SequenceEqual(right),
                value => EncodeRequired(value).GetHashCode(), value => value));

        var instant = new ValueConverter<DateTimeOffset, long>(
            value => ModerationProfileSubjectConsentSnapshot.EncodeInstant(value),
            value => ModerationProfileSubjectConsentSnapshot.DecodeInstant(value));
        builder.Property(row => row.RecordedAt).HasConversion(instant);
        builder.Property(row => row.ValidUntil).HasConversion(instant);
        builder.Property(row => row.ProcedureAdoptedAt).HasConversion(instant);
        builder.Property(row => row.ProcedureAdoptionValidUntil).HasConversion(instant);

        foreach (var property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }

    private static int EncodeRequired(IReadOnlyList<ModerationCollectionPrerequisiteKind> required)
    {
        if (required.Count > 3 || required.Any(kind => !Enum.IsDefined(kind))
            || required.Distinct().Count() != required.Count || !required.SequenceEqual(required.Order()))
            throw new ArgumentException("Assessment storage requires an explicit complete ordered prerequisite set.");
        var encoded = 0;
        foreach (var kind in required)
            encoded |= kind switch
            {
                ModerationCollectionPrerequisiteKind.AccountEligibility => 1,
                ModerationCollectionPrerequisiteKind.SubjectConsent => 2,
                ModerationCollectionPrerequisiteKind.GuardianConsent => 4,
                _ => throw new ArgumentException("Assessment storage contains an unknown prerequisite.")
            };
        return encoded;
    }

    private static IReadOnlyList<ModerationCollectionPrerequisiteKind> DecodeRequired(int encoded)
    {
        if (encoded is < 0 or > 7)
            throw new ArgumentException("Assessment storage contains an unknown prerequisite set.");
        var required = new List<ModerationCollectionPrerequisiteKind>(3);
        if ((encoded & 1) != 0) required.Add(ModerationCollectionPrerequisiteKind.AccountEligibility);
        if ((encoded & 2) != 0) required.Add(ModerationCollectionPrerequisiteKind.SubjectConsent);
        if ((encoded & 4) != 0) required.Add(ModerationCollectionPrerequisiteKind.GuardianConsent);
        return required.AsReadOnly();
    }
}

/// <summary>Finite exact-source acceptance with terminal revision-CAS withdrawal, deleted beneath PII.</summary>
public sealed class ModerationProfileCollectionAssessmentAdoptionConfiguration
    : IEntityTypeConfiguration<ModerationProfileCollectionAssessmentAdoption>
{
    public void Configure(EntityTypeBuilder<ModerationProfileCollectionAssessmentAdoption> builder)
    {
        builder.ToTable("moderation_profile_collection_assessment_adoptions", table =>
        {
            table.HasCheckConstraint("ck_profile_assessment_adoption_generation",
                "source_revision = 1 AND source_generation > 0 AND source_generation < 9223372036854775807 " +
                "AND generation > source_generation AND generation < 9223372036854775807");
            table.HasCheckConstraint("ck_profile_assessment_adoption_window",
                "adopted_at > 0 AND valid_until < 3155378975999999999 AND valid_until > adopted_at");
            table.HasCheckConstraint("ck_profile_assessment_adoption_terminal",
                "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR " +
                "(revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL " +
                "AND withdrawn_at >= adopted_at AND withdrawn_at < 3155378975999999999)");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.HasIndex(row => row.SourceId).IsUnique();
        // One deletion path only: UserPii -> proposal -> acceptance.
        builder.HasOne(row => row.Source).WithMany().HasForeignKey(row => row.SourceId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(row => row.Source).AutoInclude();
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.AdoptedByHumanId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.WithdrawnByHumanId)
            .OnDelete(DeleteBehavior.Restrict);

        var instant = new ValueConverter<DateTimeOffset, long>(
            value => ModerationProfileSubjectConsentSnapshot.EncodeInstant(value),
            value => ModerationProfileSubjectConsentSnapshot.DecodeInstant(value));
        builder.Property(row => row.AdoptedAt).HasConversion(instant);
        builder.Property(row => row.ValidUntil).HasConversion(instant);
        builder.Property(row => row.WithdrawnAt).HasConversion(instant);
        foreach (var property in builder.Metadata.GetProperties().Where(property =>
            property.Name is not (nameof(ModerationProfileCollectionAssessmentAdoption.Revision)
                or nameof(ModerationProfileCollectionAssessmentAdoption.WithdrawnAt)
                or nameof(ModerationProfileCollectionAssessmentAdoption.WithdrawnByHumanId))))
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}

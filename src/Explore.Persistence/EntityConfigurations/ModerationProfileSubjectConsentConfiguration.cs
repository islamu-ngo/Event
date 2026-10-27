using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Explore.Persistence.EntityConfigurations;

/// <summary>Private PII-owned consent metadata, not a native consent authority or collection receipt.</summary>
public sealed class ModerationProfileSubjectConsentConfiguration : IEntityTypeConfiguration<ModerationProfileSubjectConsent>
{
    public void Configure(EntityTypeBuilder<ModerationProfileSubjectConsent> builder)
    {
        builder.ToTable("moderation_profile_subject_consents", table =>
        {
            table.HasCheckConstraint("ck_profile_subject_consent_generation",
                "generation > 0 AND generation < 9223372036854775807 " +
                "AND procedure_adoption_revision = 1 AND procedure_adoption_writer_generation > 0 " +
                "AND procedure_adoption_writer_generation < 9223372036854775807");
            table.HasCheckConstraint("ck_profile_subject_consent_validity",
                "granted_at > 0 AND valid_until < 3155378975999999999 AND valid_until > granted_at " +
                "AND procedure_adopted_at > 0 AND procedure_adoption_valid_until < 3155378975999999999 " +
                "AND procedure_adoption_valid_until > procedure_adopted_at " +
                "AND granted_at >= procedure_adopted_at AND valid_until <= procedure_adoption_valid_until");
            table.HasCheckConstraint("ck_profile_subject_consent_terminal",
                "(revision = 1 AND withdrawn_at IS NULL) OR " +
                "(revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_at >= granted_at " +
                "AND withdrawn_at < 3155378975999999999)");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.Ignore(row => row.Purpose);

        // The immutable source row remains the original reference, not a latest-source lookup.
        builder.Property<Guid>("SourceId").IsRequired();
        builder.HasOne(row => row.Source).WithMany().HasForeignKey("SourceId")
            .IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(row => row.Source).AutoInclude();
        // PII's actual shared User key owns deletion. Do not attach this metadata directly
        // to the durable Human row or to privacy recognition history.
        builder.HasOne<UserPii>().WithMany().HasForeignKey(row => row.SubjectHumanId)
            .HasPrincipalKey(row => row.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(nameof(ModerationProfileSubjectConsent.SubjectHumanId), "SourceId",
            nameof(ModerationProfileSubjectConsent.Generation)).IsUnique();

        // These two bounded owner columns avoid EF value-entity identity and CLR constructor
        // materialization. Decoding restores every exact semantic coordinate through factories.
        builder.Property(row => row.Binding).HasConversion(
            value => ModerationProfileSubjectConsentSnapshot.EncodeBinding(value),
            value => ModerationProfileSubjectConsentSnapshot.DecodeBinding(value))
            .HasMaxLength(4096).IsRequired();
        builder.Property(row => row.Assessment).HasConversion(
            value => ModerationProfileSubjectConsentSnapshot.EncodeAssessment(value),
            value => ModerationProfileSubjectConsentSnapshot.DecodeAssessment(value))
            .HasMaxLength(8192).IsRequired()
            .Metadata.SetValueComparer(new ValueComparer<ModerationCollectionPrerequisiteAssessment>(
                (left, right) => left == right || left != null && right != null
                    && ModerationProfileSubjectConsentSnapshot.EncodeAssessment(left)
                        == ModerationProfileSubjectConsentSnapshot.EncodeAssessment(right),
                value => ModerationProfileSubjectConsentSnapshot.EncodeAssessment(value).GetHashCode(),
                value => value));

        var instant = new ValueConverter<DateTimeOffset, long>(
            value => ModerationProfileSubjectConsentSnapshot.EncodeInstant(value),
            value => ModerationProfileSubjectConsentSnapshot.DecodeInstant(value));
        builder.Property(row => row.GrantedAt).HasConversion(instant);
        builder.Property(row => row.ValidUntil).HasConversion(instant);
        builder.Property(row => row.ProcedureAdoptedAt).HasConversion(instant);
        builder.Property(row => row.ProcedureAdoptionValidUntil).HasConversion(instant);
        builder.Property(row => row.WithdrawnAt).HasConversion(instant);

        foreach (var property in builder.Metadata.GetProperties().Where(property =>
            property.Name is not (nameof(ModerationProfileSubjectConsent.Revision)
                or nameof(ModerationProfileSubjectConsent.WithdrawnAt))))
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}

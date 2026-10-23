using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Moderation;

public sealed class ModerationPolicyPublicationCandidateConfiguration
    : IEntityTypeConfiguration<ModerationPolicyPublicationCandidate>
{
    public void Configure(EntityTypeBuilder<ModerationPolicyPublicationCandidate> builder)
    {
        builder.ToTable("moderation_policy_publication_candidates", table =>
        {
            table.HasCheckConstraint("ck_moderation_publication_candidate_revision",
                "expected_revision >= 0 AND expected_revision < 9223372036854775807 AND revision = 1");
            table.HasCheckConstraint("ck_moderation_publication_candidate_history",
                "(expected_revision = 0 AND previous_publication_id IS NULL) OR " +
                "(expected_revision > 0 AND previous_publication_id IS NOT NULL)");
            table.HasCheckConstraint("ck_moderation_publication_candidate_source",
                "authority_revision > 0 AND authority_revision < 9223372036854775806 " +
                "AND writer_generation = authority_revision + 1");
            table.HasCheckConstraint("ck_moderation_publication_candidate_identity", "id <> definition_id");
            table.HasCheckConstraint("ck_moderation_publication_candidate_validity", "valid_until > prepared_at");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.RequestDigest).HasMaxLength(64).IsRequired();
        builder.Property(row => row.ProposedStateDigest).HasMaxLength(64).IsRequired();
        builder.Ignore(row => row.Jurisdiction);
        builder.HasIndex(row => row.DefinitionId).IsUnique();
        builder.HasIndex(row => row.OperationId).IsUnique();
        builder.HasOne(row => row.Definition).WithMany()
            .HasForeignKey(row => new { row.PolicyId, row.DefinitionId })
            .HasPrincipalKey(row => new { row.PolicyId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationPolicyPublication>().WithMany()
            .HasForeignKey(row => new { row.PolicyId, row.PreviousPublicationId })
            .HasPrincipalKey(row => new { row.PolicyId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationHostBinding>().WithMany()
            .HasForeignKey(row => row.HostBindingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany()
            .HasForeignKey(row => row.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(row => row.PreparedByHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationQualificationEpoch>().WithMany()
            .HasForeignKey(row => row.AuthoritySourceId).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}

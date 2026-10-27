using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Moderation;

public sealed class ModerationPreFindingConfiguration :
    IEntityTypeConfiguration<ModerationPreFindingCustody>,
    IEntityTypeConfiguration<ModerationPreFindingNotice>,
    IEntityTypeConfiguration<ModerationPreFindingReceipt>,
    IEntityTypeConfiguration<ModerationPreFindingErasureTombstone>
{
    public void Configure(EntityTypeBuilder<ModerationPreFindingCustody> builder)
    {
        builder.ToTable("moderation_pre_finding_custody");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.Ignore(row => row.Terms);
        builder.Property(row => row.Purpose).HasMaxLength(2048).IsRequired();
        builder.Property(row => row.PrivacyLegalMandate).HasMaxLength(2048).IsRequired();
        builder.HasAlternateKey(row => new { row.Id, row.HostBindingId, row.TrustDomainId, row.TenantId, row.CaseId });
        builder.HasOne<ModerationCase>().WithMany().HasForeignKey(row => row.CaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationCaseAttributionSource>().WithMany()
            .HasForeignKey(row => row.AttributionSourceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.ProposedByHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.PrivacyOwnerHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_pre_finding_custody_limits",
                "maximum_retention_ticks > 0 AND maximum_retention_ticks < 9223372036854775807 " +
                "AND maximum_text_bytes > 0 AND maximum_text_bytes <= 65536 AND valid_until > proposed_at");
            table.HasCheckConstraint("ck_pre_finding_custody_independence", "proposed_by_human_id <> privacy_owner_human_id");
            table.HasCheckConstraint("ck_pre_finding_custody_attribution",
                "attribution_generation > 0 AND evidence_revision > 0");
        });
    }

    public void Configure(EntityTypeBuilder<ModerationPreFindingNotice> builder)
    {
        builder.ToTable("moderation_pre_finding_notices");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.Ignore(row => row.ProtectedNotice);
        builder.Ignore(row => row.ProtectedResponse);
        builder.Property<byte[]>("protectedNotice").HasColumnName("protected_notice").HasMaxLength(131072).IsRequired();
        builder.Property<byte[]>("protectedResponse").HasColumnName("protected_response").HasMaxLength(131072).IsRequired();
        // One occurrence/evidence opportunity cannot be replaced to restart its clock.
        builder.HasIndex(row => row.CaseId).IsUnique();
        builder.HasIndex(row => new { row.TenantId, row.RetainUntil });
        builder.HasOne<ModerationPreFindingCustody>().WithMany()
            .HasForeignKey(row => new { row.CustodyId, row.HostBindingId, row.TrustDomainId, row.TenantId, row.CaseId })
            .HasPrincipalKey(row => new { row.Id, row.HostBindingId, row.TrustDomainId, row.TenantId, row.CaseId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationCase>().WithMany().HasForeignKey(row => row.CaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationCase>().WithMany().HasForeignKey(row => row.OccurrenceCaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.RecipientHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationReviewerAssignment>().WithMany()
            .HasForeignKey(row => row.ReviewerAssignmentId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_pre_finding_notice_finite", "retain_until > issued_at");
            table.HasCheckConstraint("ck_pre_finding_notice_link",
                "case_id <> occurrence_case_id AND case_revision > 0 AND evidence_revision > 0 " +
                "AND attribution_generation > 0 AND revision > 0");
            table.HasCheckConstraint("ck_pre_finding_notice_response",
                "(response_id IS NULL AND responded_at IS NULL) OR " +
                "(response_id IS NOT NULL AND responded_at >= acknowledged_at AND responded_at < retain_until)");
        });
    }

    public void Configure(EntityTypeBuilder<ModerationPreFindingReceipt> builder)
    {
        builder.ToTable("moderation_pre_finding_receipts");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.RequestDigest).HasMaxLength(64).IsRequired();
        builder.HasIndex(row => new { row.ResultId, row.ResultRevision }).IsUnique();
        builder.HasOne<ModerationCase>().WithMany().HasForeignKey(row => row.CaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.ActorHumanId).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<ModerationPreFindingErasureTombstone> builder)
    {
        builder.ToTable("moderation_pre_finding_erasure_tombstones");
        builder.HasKey(row => row.CaseId);
        builder.Property(row => row.CaseId).ValueGeneratedNever();
        builder.HasOne<ModerationCase>().WithMany().HasForeignKey(row => row.CaseId).OnDelete(DeleteBehavior.Restrict);
    }
}

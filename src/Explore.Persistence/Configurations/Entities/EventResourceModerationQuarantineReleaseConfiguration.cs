using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class EventResourceModerationQuarantineReleaseConfiguration :
    IEntityTypeConfiguration<EventResourceModerationQuarantineReview>,
    IEntityTypeConfiguration<EventResourceModerationQuarantineRelease>
{
    public void Configure(EntityTypeBuilder<EventResourceModerationQuarantineReview> builder)
    {
        builder.ToTable("event_resource_quarantine_reviews", table =>
        {
            table.HasCheckConstraint("ck_resource_safety_step", "step BETWEEN 1 AND 6");
            table.HasCheckConstraint("ck_resource_safety_quorum", "required_approvals BETWEEN 1 AND 2");
            table.HasCheckConstraint("ck_resource_safety_lifetime", "maximum_evidence_lifetime_ticks > 0");
        });
        builder.HasKey(row => row.Id);
        builder.HasAlternateKey(row => new { row.TenantId, row.Id });
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.NativeDigest).HasMaxLength(64).IsRequired();
        builder.Property(row => row.RequestDigest).HasMaxLength(64).IsRequired();
        builder.HasIndex(row => new { row.TenantId, row.QuarantineId, row.PurposeId });
        builder.HasOne<EventResourceModerationQuarantine>().WithMany()
            .HasForeignKey(row => new { row.TenantId, row.QuarantineId })
            .HasPrincipalKey(row => new { row.TenantId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }

    public void Configure(EntityTypeBuilder<EventResourceModerationQuarantineRelease> builder)
    {
        builder.ToTable("event_resource_quarantine_releases");
        builder.HasKey(row => row.Id);
        builder.HasAlternateKey(row => new { row.TenantId, row.Id });
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.RequestDigest).HasMaxLength(64).IsRequired();
        builder.HasIndex(row => row.QuarantineId).IsUnique();
        builder.HasOne<EventResourceModerationQuarantine>().WithMany()
            .HasForeignKey(row => new { row.TenantId, row.QuarantineId })
            .HasPrincipalKey(row => new { row.TenantId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EventResourceModerationQuarantineReview>().WithMany()
            .HasForeignKey(row => new { row.TenantId, row.ApprovalId })
            .HasPrincipalKey(row => new { row.TenantId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}

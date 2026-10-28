using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class EventResourceModerationReviewDutyConfiguration :
    IEntityTypeConfiguration<EventResourceModerationReviewDuty>,
    IEntityTypeConfiguration<EventResourceModerationReviewCompletion>,
    IEntityTypeConfiguration<EventResourceModerationReviewEscalation>
{
    public void Configure(EntityTypeBuilder<EventResourceModerationReviewDuty> builder)
    {
        builder.ToTable("event_resource_moderation_review_duties");
        builder.HasKey(row => row.Id);
        builder.HasAlternateKey(row => new { row.TenantId, row.Id });
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.HasIndex(row => new { row.TenantId, row.DueAt, row.Id });
        builder.HasOne<EventResourceModerationQuarantine>().WithMany()
            .HasForeignKey(row => new { row.TenantId, row.Id })
            .HasPrincipalKey(row => new { row.TenantId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }

    public void Configure(EntityTypeBuilder<EventResourceModerationReviewCompletion> builder)
    {
        builder.ToTable("event_resource_moderation_review_completions", table =>
            table.HasCheckConstraint("ck_resource_review_disposition", "disposition IN (1, 2)"));
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.HasIndex(row => new { row.TenantId, row.QuarantineId, row.CompletedAt });
        builder.HasIndex(row => new { row.TenantId, row.PurposeId }).IsUnique();
        builder.HasOne<EventResourceModerationQuarantine>().WithMany()
            .HasForeignKey(row => new { row.TenantId, row.QuarantineId })
            .HasPrincipalKey(row => new { row.TenantId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EventResourceModerationQuarantineReview>().WithMany()
            .HasForeignKey(row => new { row.TenantId, row.ApprovalId })
            .HasPrincipalKey(row => new { row.TenantId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EventResourceModerationQuarantineReview>().WithMany()
            .HasForeignKey(row => new { row.TenantId, row.AttestationId })
            .HasPrincipalKey(row => new { row.TenantId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }

    public void Configure(EntityTypeBuilder<EventResourceModerationReviewEscalation> builder)
    {
        builder.ToTable("event_resource_moderation_review_escalations", table =>
            table.HasCheckConstraint("ck_resource_review_escalation_recipients", "recipient_count BETWEEN 1 AND 32"));
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.HasOne<EventResourceModerationReviewDuty>().WithOne()
            .HasForeignKey<EventResourceModerationReviewEscalation>(row => new { row.TenantId, row.Id })
            .HasPrincipalKey<EventResourceModerationReviewDuty>(row => new { row.TenantId, row.Id })
            .OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}

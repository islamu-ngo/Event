using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations;

/// <summary>Primary-store admission evidence; identity identifiers may precede their separate-store records.</summary>
public sealed class VisitorEnrollmentOperationConfiguration : IEntityTypeConfiguration<VisitorEnrollmentOperation>
{
    public void Configure(EntityTypeBuilder<VisitorEnrollmentOperation> builder)
    {
        builder.ToTable("visitor_enrollment_operations", table =>
        {
            table.HasCheckConstraint("ck_visitor_enrollment_stage", "stage BETWEEN 1 AND 5");
            table.HasCheckConstraint("ck_visitor_enrollment_generation", "generation > 0");
            table.HasCheckConstraint("ck_visitor_enrollment_timeline",
                "expires_at > created_at AND (updated_at IS NULL OR updated_at >= created_at)");
            table.HasCheckConstraint("ck_visitor_enrollment_cleanup",
                "cleanup_completed_at IS NULL OR (stage = 5 AND updated_at IS NOT NULL "
                + "AND cleanup_completed_at >= created_at AND cleanup_completed_at <= updated_at)");
            table.HasCheckConstraint("ck_visitor_enrollment_evidence",
                "(stage = 1 AND credential_generation IS NULL AND tenant_user_id IS NULL) "
                + "OR (stage = 2 AND credential_generation IS NOT NULL AND credential_generation > 0 AND tenant_user_id IS NULL) "
                + "OR (stage IN (3, 4) AND credential_generation IS NOT NULL AND credential_generation > 0 AND tenant_user_id IS NOT NULL) "
                + "OR (stage = 5 AND tenant_user_id IS NULL AND (credential_generation IS NULL OR credential_generation > 0))");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Generation).IsConcurrencyToken().IsRequired();
        builder.Property(row => row.Stage).HasConversion<int>().IsRequired();
        builder.Property(row => row.IntentDigest).HasMaxLength(64).IsFixedLength().IsUnicode(false).IsRequired();
        builder.Property(row => row.CreatedAt).HasConversion(
            value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc)).IsRequired();
        builder.Property(row => row.ExpiresAt).HasConversion(
            value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc)).IsRequired();
        builder.Property(row => row.UpdatedAt).HasConversion(
            value => value, value => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null);
        builder.Property(row => row.CleanupCompletedAt).HasConversion(
            value => value, value => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(row => row.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(row => row.CredentialOperationId).IsUnique();
        builder.HasIndex(row => row.LocalSubjectId).IsUnique();
        builder.HasIndex(row => new { row.Stage, row.ExpiresAt, row.Id });
    }
}

using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Moderation;

public sealed class ModerationMeasurementIssuerEnrollmentConfiguration : IEntityTypeConfiguration<ModerationMeasurementIssuerEnrollment>
{
    public void Configure(EntityTypeBuilder<ModerationMeasurementIssuerEnrollment> builder)
    {
        builder.ToTable("moderation_measurement_issuer_enrollments", table =>
        {
            table.HasCheckConstraint("ck_moderation_measurement_issuer_enrollment_key", "key_id <> id");
            table.HasCheckConstraint("ck_moderation_measurement_issuer_enrollment_generation",
                "writer_generation > 0 AND writer_generation < 9223372036854775807");
            table.HasCheckConstraint("ck_moderation_measurement_issuer_enrollment_validity", "valid_until > approved_at");
            table.HasCheckConstraint("ck_moderation_measurement_issuer_enrollment_revocation",
                "(revision = 1 AND revoked_at IS NULL AND revoked_by_human_id IS NULL) OR " +
                "(revision = 2 AND revoked_at IS NOT NULL AND revoked_by_human_id IS NOT NULL AND revoked_at >= approved_at)");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.Property(row => row.Issuer).HasMaxLength(128).IsRequired();
        builder.Property(row => row.PublicKeySpkiBase64).HasMaxLength(1024).IsRequired();
        builder.Property(row => row.RunnerDigest).HasMaxLength(64).IsRequired();
        builder.Property(row => row.CatalogueDigest).HasMaxLength(64).IsRequired();
        builder.Property(row => row.TestBundleDigest).HasMaxLength(64).IsRequired();
        builder.HasIndex(row => new { row.HostBindingId, row.TenantId, row.KeyId }).IsUnique();
        builder.HasOne<ModerationHostBinding>().WithMany().HasForeignKey(row => row.HostBindingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(row => row.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.ApprovedByHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.RevokedByHumanId).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties().Where(property =>
            property.Name is not (nameof(ModerationMeasurementIssuerEnrollment.Revision)
                or nameof(ModerationMeasurementIssuerEnrollment.RevokedAt) or nameof(ModerationMeasurementIssuerEnrollment.RevokedByHumanId))))
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}

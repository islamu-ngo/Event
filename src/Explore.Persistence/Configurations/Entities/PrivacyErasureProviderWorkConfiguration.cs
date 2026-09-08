using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class PrivacyErasureProviderWorkConfiguration
    : IEntityTypeConfiguration<PrivacyErasureProviderWork>
{
    public void Configure(EntityTypeBuilder<PrivacyErasureProviderWork> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_privacy_erasure_provider_work_subject_kind", "subject_kind = 1");
            table.HasCheckConstraint("ck_privacy_erasure_provider_work_attempt_count", "attempt_count >= 0");
            table.HasCheckConstraint("ck_privacy_erasure_provider_work_lease_fence", "lease_fence >= 0");
            table.HasCheckConstraint("ck_privacy_erasure_provider_work_locator_kind", "locator_kind BETWEEN 1 AND 7");
            table.HasCheckConstraint("ck_privacy_erasure_provider_work_locator_version", "locator_protection_version >= 1");
            table.HasCheckConstraint("ck_privacy_erasure_provider_work_locator_expiry", "locator_expires_at_utc > created_at_utc");
            table.HasCheckConstraint(
                "ck_privacy_erasure_provider_work_locator_lifecycle",
                "(status = 5 AND protected_locator IS NULL) OR status = 6 OR (status NOT IN (5, 6) AND protected_locator IS NOT NULL)");
        });

        builder.HasKey(item => item.Id);
        builder.Property(item => item.SubjectKind).HasConversion<short>();
        builder.Property(item => item.ProviderKind).HasConversion<short>();
        builder.Property(item => item.Action).HasConversion<short>();
        builder.Property(item => item.LocatorKind).HasConversion<short>();
        builder.Property(item => item.Status).HasConversion<short>();
        builder.Property(item => item.ProtectedLocator).HasMaxLength(8192);
        builder.Property(item => item.LeaseOwner).HasMaxLength(100);
        builder.Property(item => item.LastFailureCode).HasMaxLength(100);
        builder.HasIndex(item => new
        {
            item.IntentId,
            item.ProviderKind,
            item.Action,
            item.TenantId,
            item.TargetId
        }).IsUnique();
        builder.HasIndex(item => new { item.Status, item.NextAttemptAtUtc, item.LeaseExpiresAtUtc });
        builder.HasOne<PrivacyErasureSaga>()
            .WithMany()
            .HasForeignKey(item => item.IntentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

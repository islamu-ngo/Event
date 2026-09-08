// ABOUTME: Maps the independent lifecycle ledger in both primary and external Identity models.
// ABOUTME: Enforces finite purpose, fixed expiration, and coherent consumption/mirror receipts.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Identity.Configurations;

public sealed class LocalIdentityLifecycleOperationConfiguration : IEntityTypeConfiguration<LocalIdentityLifecycleOperation>
{
    public void Configure(EntityTypeBuilder<LocalIdentityLifecycleOperation> builder)
    {
        builder.HasKey(operation => operation.Id);
        builder.Property(operation => operation.Id).ValueGeneratedNever();
        builder.Property(operation => operation.Generation).IsConcurrencyToken().ValueGeneratedNever();
        builder.Property(operation => operation.Purpose).HasConversion<int>();
        builder.Property(operation => operation.PendingAddress).HasMaxLength(256).IsRequired();
        builder.Property(operation => operation.SecurityStamp).HasMaxLength(256).IsRequired();
        builder.Property(operation => operation.ResultSecurityStamp).HasMaxLength(256);
        Utc(builder.Property(operation => operation.CreatedAt));
        Utc(builder.Property(operation => operation.ExpiresAt));
        NullableUtc(builder.Property(operation => operation.ConsumedAt));
        NullableUtc(builder.Property(operation => operation.SynchronizedAt));
        builder.Property(operation => operation.DeliveryState).HasConversion<int>();
        NullableUtc(builder.Property(operation => operation.DeliveryAdmittedAt));
        NullableUtc(builder.Property(operation => operation.DeliveryCompletedAt));
        builder.HasOne<LocalIdentityUser>().WithMany().HasForeignKey(operation => operation.LocalSubjectId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(operation => new { operation.LocalSubjectId, operation.Purpose, operation.ExpiresAt });
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_local_lifecycle_purpose", "purpose BETWEEN 1 AND 3");
            table.HasCheckConstraint("ck_local_lifecycle_delivery_state", "delivery_state BETWEEN 0 AND 3 AND delivery_attempt_count BETWEEN 0 AND 3");
            table.HasCheckConstraint("ck_local_lifecycle_delivery_attempt",
                "(delivery_attempt_count = 0 AND delivery_attempt_id IS NULL AND delivery_admitted_at IS NULL AND delivery_completed_at IS NULL AND delivery_state IN (0,3)) OR "
                + "(delivery_attempt_count > 0 AND delivery_attempt_id IS NOT NULL AND delivery_admitted_at IS NOT NULL "
                + "AND delivery_admitted_at >= created_at AND delivery_admitted_at < expires_at "
                + "AND (delivery_completed_at IS NULL OR delivery_completed_at >= delivery_admitted_at) "
                + "AND (delivery_state <> 1 OR delivery_completed_at IS NULL) AND (delivery_state <> 2 OR delivery_completed_at IS NOT NULL))");
            table.HasCheckConstraint("ck_local_lifecycle_expiry", "expires_at > created_at");
            table.HasCheckConstraint("ck_local_lifecycle_consumption",
                "(consumed_at IS NULL AND result_security_stamp IS NULL AND synchronized_at IS NULL) OR "
                + "(consumed_at IS NOT NULL AND result_security_stamp IS NOT NULL AND consumed_at >= created_at "
                + "AND consumed_at < expires_at AND (synchronized_at IS NULL OR synchronized_at >= consumed_at))");
        });
    }
    private static void Utc(PropertyBuilder<DateTime> property) => property.HasConversion(
        value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
    private static void NullableUtc(PropertyBuilder<DateTime?> property) => property.HasConversion(
        value => value, value => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null);
}

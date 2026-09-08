using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class StorageUsageCounterConfiguration : IEntityTypeConfiguration<StorageUsageCounter>
{
    public void Configure(EntityTypeBuilder<StorageUsageCounter> builder)
    {
        builder.Property(e => e.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(e => e.Provider).HasMaxLength(50).IsRequired();
        builder.Property(e => e.ConcurrencyStamp).IsConcurrencyToken();

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.Provider })
            .IsUnique()
            .HasDatabaseName("ux_storage_usage_counters_tenant_provider");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_storage_usage_counters_used_bytes_nonnegative", "used_bytes >= 0");
            t.HasCheckConstraint("ck_storage_usage_counters_reserved_bytes_nonnegative", "reserved_bytes >= 0");
            t.HasCheckConstraint("ck_storage_usage_counters_quarantined_bytes_nonnegative", "quarantined_bytes >= 0");
            t.HasCheckConstraint("ck_storage_usage_counters_object_count_nonnegative", "object_count >= 0");
            t.HasCheckConstraint("ck_storage_usage_counters_provider", "provider IN ('local', 's3_compatible', 'legacy_external')");
        });
    }
}

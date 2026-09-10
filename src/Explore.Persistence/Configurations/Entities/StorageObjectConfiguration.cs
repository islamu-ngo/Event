using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class StorageObjectConfiguration : IEntityTypeConfiguration<StorageObject>
{
    public void Configure(EntityTypeBuilder<StorageObject> builder)
    {
        builder.Property(e => e.Id).HasDefaultValueSql("uuidv7()");
        builder.HasAlternateKey(e => new { e.TenantId, e.Id })
            .HasName("ak_storage_objects_tenant_id_id");

        builder.Property(e => e.Uri).HasMaxLength(1000).IsRequired();
        builder.Property(e => e.ObjectKey).HasMaxLength(1024);
        builder.Property(e => e.Provider).HasMaxLength(50).IsRequired();
        builder.Property(e => e.FullName).HasMaxLength(500).IsRequired();
        builder.Property(e => e.SafeDisplayName).HasMaxLength(500).IsRequired();
        builder.Property(e => e.Extension).HasMaxLength(50).IsRequired();
        builder.Property(e => e.ContentType).HasMaxLength(255);
        builder.Property(e => e.Sha256Checksum).HasColumnName("sha256_checksum").HasMaxLength(64);
        builder.Property(e => e.Visibility).HasMaxLength(50).IsRequired();
        builder.Property(e => e.Purpose).HasMaxLength(100).IsRequired();
        builder.Property(e => e.LifecycleState).HasMaxLength(50).IsRequired();
        builder.Property(e => e.OwningResourceKind).HasMaxLength(100);
        builder.Property(e => e.RegistrationContentRetentionUntilUtc);
        builder.Property(e => e.QuarantineReason).HasMaxLength(500);
        builder.Property(e => e.ConcurrencyStamp).IsConcurrencyToken();

        builder.HasOne(e => e.FileType)
            .WithMany()
            .HasForeignKey(e => e.FileTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Actor)
            .WithMany()
            .HasForeignKey(e => e.ActorId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => new { e.TenantId, e.Provider, e.LifecycleState })
            .HasDatabaseName("ix_storage_objects_tenant_provider_lifecycle_state");

        builder.HasIndex(e => new { e.TenantId, e.Visibility, e.Purpose })
            .HasDatabaseName("ix_storage_objects_tenant_visibility_purpose");

        builder.HasIndex(e => new { e.Provider, e.ObjectKey })
            .IsUnique()
            .HasFilter("object_key IS NOT NULL")
            .HasDatabaseName("ux_storage_objects_provider_object_key");

        builder.HasIndex(e => new { e.TenantId, e.OwningResourceKind, e.OwningResourceId })
            .HasFilter("owning_resource_kind IS NOT NULL AND owning_resource_id IS NOT NULL")
            .HasDatabaseName("ix_storage_objects_tenant_owner");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_storage_objects_size_nonnegative", "size >= 0");
            t.HasCheckConstraint("ck_storage_objects_provider", "provider IN ('local', 's3_compatible', 'legacy_external')");
            t.HasCheckConstraint("ck_storage_objects_visibility", "visibility IN ('public_image', 'authenticated_tenant', 'private_owner')");
            t.HasCheckConstraint("ck_storage_objects_purpose", "purpose IN ('legacy_image', 'profile_image', 'event_image', 'attachment', 'document', 'system_asset')");
            t.HasCheckConstraint("ck_storage_objects_lifecycle_state", "lifecycle_state IN ('pending', 'active', 'quarantined', 'delete_requested', 'deleted')");
        });

        // NOTE: Business entity seed data moved to DatabaseSeeder for conditional (Development-only) seeding.
        // See Explore.Persistence/Seed/DatabaseSeeder.cs and SeedData.cs
    }
}

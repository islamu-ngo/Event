using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class StorageObjectDeletionTombstoneConfiguration : IEntityTypeConfiguration<StorageObjectDeletionTombstone>
{
    public void Configure(EntityTypeBuilder<StorageObjectDeletionTombstone> builder)
    {
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.Provider).HasMaxLength(50).IsRequired();
        builder.Property(item => item.ObjectKey).HasMaxLength(1024).IsRequired();
        builder.Property(item => item.ProviderObjectVersion).HasMaxLength(1024);
        builder.Property(item => item.State).HasConversion<short>();
        builder.Property(item => item.ConcurrencyStamp).IsConcurrencyToken().ValueGeneratedNever();
        builder.HasOne<StorageProviderBinding>().WithMany()
            .HasForeignKey(item => item.ProviderBindingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(item => new { item.State, item.NextAttemptAtUtc, item.Id });
        builder.HasIndex(item => new { item.State, item.LeaseExpiresAtUtc, item.Id });
        // No source-object or tenant FK: their removal cannot cascade retry authority.
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_storage_deletion_identity",
                "id <> '00000000-0000-0000-0000-000000000000' AND tenant_id <> '00000000-0000-0000-0000-000000000000' AND concurrency_stamp <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("ck_storage_deletion_provider",
                $"provider IN ('{StorageProviders.Local}', '{StorageProviders.S3Compatible}') AND object_key <> ''");
            table.HasCheckConstraint("ck_storage_deletion_state",
                "(state = 1 AND next_attempt_at_utc IS NULL AND lease_expires_at_utc IS NULL) OR " +
                "(state = 2 AND next_attempt_at_utc IS NOT NULL AND lease_expires_at_utc IS NULL) OR " +
                "(state = 3 AND next_attempt_at_utc IS NULL AND lease_expires_at_utc IS NOT NULL) OR " +
                "(state = 4 AND next_attempt_at_utc IS NULL AND lease_expires_at_utc IS NULL)");
        });
    }
}

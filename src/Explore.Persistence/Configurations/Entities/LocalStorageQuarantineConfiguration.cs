using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Explore.Persistence.Configurations.Entities;

public sealed class LocalStorageQuarantineConfiguration : IEntityTypeConfiguration<LocalStorageQuarantine>
{
    public void Configure(EntityTypeBuilder<LocalStorageQuarantine> builder)
    {
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.ObjectKey).HasMaxLength(1024).IsRequired();
        builder.Property(row => row.CanonicalPhysicalPath).HasMaxLength(4096).IsRequired();
        builder.Property(row => row.PhysicalPathHash).HasMaxLength(64).IsRequired();
        builder.Property(row => row.AdmissionReason).HasMaxLength(128).IsRequired();
        builder.Property(row => row.State).HasConversion<short>();
        builder.Property(row => row.Outcome).HasConversion<short>();
        builder.Property(row => row.ConcurrencyStamp).IsConcurrencyToken().ValueGeneratedNever();
        var utcTicks = new ValueConverter<DateTime, long>(
            value => value.Ticks, value => new DateTime(value, DateTimeKind.Utc));
        builder.Property(row => row.ObservedLastModifiedUtc).HasConversion(utcTicks).HasColumnName("observed_last_modified_utc_ticks");
        builder.Property(row => row.ModifiedBeforeUtc).HasConversion(utcTicks).HasColumnName("modified_before_utc_ticks");
        builder.Property(row => row.AdmittedAtUtc).HasConversion(utcTicks).HasColumnName("admitted_at_utc_ticks");
        builder.Property(row => row.NextAttemptAtUtc).HasConversion(utcTicks).HasColumnName("next_attempt_at_utc_ticks");
        builder.Property(row => row.LeaseExpiresAtUtc).HasConversion(utcTicks).HasColumnName("lease_expires_at_utc_ticks");
        builder.Property(row => row.SettledAtUtc).HasConversion(utcTicks).HasColumnName("settled_at_utc_ticks");
        foreach (var name in new[]
        {
            nameof(LocalStorageQuarantine.Id), nameof(LocalStorageQuarantine.ProviderBindingId),
            nameof(LocalStorageQuarantine.ObjectKey), nameof(LocalStorageQuarantine.CanonicalPhysicalPath),
            nameof(LocalStorageQuarantine.PhysicalPathHash), nameof(LocalStorageQuarantine.ObservedSize),
            nameof(LocalStorageQuarantine.ObservedLastModifiedUtc), nameof(LocalStorageQuarantine.ModifiedBeforeUtc),
            nameof(LocalStorageQuarantine.AdmissionReason), nameof(LocalStorageQuarantine.AdmittedAtUtc)
        })
            builder.Property(name).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        builder.HasOne<StorageProviderBinding>().WithMany()
            .HasForeignKey(row => row.ProviderBindingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(row => row.PhysicalPathHash).IsUnique();
        builder.HasIndex(row => new { row.State, row.NextAttemptAtUtc, row.Id });
        builder.HasIndex(row => new { row.State, row.LeaseExpiresAtUtc, row.Id });
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_local_quarantine_identity",
                "id <> '00000000-0000-0000-0000-000000000000' AND provider_binding_id <> '00000000-0000-0000-0000-000000000000' AND concurrency_stamp <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("ck_local_quarantine_evidence",
                "observed_size >= 0 AND observed_last_modified_utc_ticks > 0 AND observed_last_modified_utc_ticks <= modified_before_utc_ticks AND modified_before_utc_ticks <= admitted_at_utc_ticks AND object_key <> '' AND canonical_physical_path <> '' AND admission_reason <> ''");
            table.HasCheckConstraint("ck_local_quarantine_state",
                "(state = 1 AND next_attempt_at_utc_ticks IS NOT NULL AND lease_expires_at_utc_ticks IS NULL AND settled_at_utc_ticks IS NULL AND outcome IS NULL) OR " +
                "(state = 2 AND next_attempt_at_utc_ticks IS NULL AND lease_expires_at_utc_ticks IS NOT NULL AND settled_at_utc_ticks IS NULL AND outcome IS NULL) OR " +
                "(state = 3 AND next_attempt_at_utc_ticks IS NULL AND lease_expires_at_utc_ticks IS NULL AND settled_at_utc_ticks IS NOT NULL AND outcome IN (1, 2, 3)) OR " +
                "(state = 4 AND next_attempt_at_utc_ticks IS NULL AND lease_expires_at_utc_ticks IS NULL AND settled_at_utc_ticks IS NOT NULL AND outcome = 4)");
        });
    }
}

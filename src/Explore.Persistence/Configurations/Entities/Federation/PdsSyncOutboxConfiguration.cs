using Explore.Domain.Federation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities.Federation;

public sealed class PdsSyncOutboxConfiguration : IEntityTypeConfiguration<PdsSyncOutbox>
{
    public void Configure(EntityTypeBuilder<PdsSyncOutbox> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_pds_sync_outbox_operation", "operation BETWEEN 1 AND 3");
            table.HasCheckConstraint("ck_pds_sync_outbox_status", "status BETWEEN 1 AND 6");
            table.HasCheckConstraint("ck_pds_sync_outbox_retry_count", "retry_count >= 0 AND max_retries > 0");
            table.HasCheckConstraint("ck_pds_sync_outbox_lease_fence", "lease_fence >= 0");
            table.HasCheckConstraint("ck_pds_sync_outbox_payload_hash", "payload_hash ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint(
                "ck_pds_sync_outbox_payload_shape",
                "(operation = 3 AND payload IS NULL) OR (operation IN (1, 2) AND payload IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_pds_sync_outbox_lease_shape",
                "(status = 2 AND lease_owner IS NOT NULL AND btrim(lease_owner) <> '' AND lease_token IS NOT NULL AND lease_expires_at IS NOT NULL) OR " +
                "(status <> 2 AND lease_owner IS NULL AND lease_token IS NULL AND lease_expires_at IS NULL)");
            table.HasCheckConstraint(
                "ck_pds_sync_outbox_completion_shape",
                "status <> 3 OR (processed_at IS NOT NULL AND settled_uri IS NOT NULL AND settled_cid IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_pds_sync_outbox_supersession_shape",
                "(status = 6 AND superseded_by_id IS NOT NULL AND superseded_at IS NOT NULL) OR " +
                "(status <> 6 AND superseded_by_id IS NULL AND superseded_at IS NULL)");
        });

        builder.Property(value => value.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(value => value.Did).HasMaxLength(255).IsRequired();
        builder.Property(value => value.Collection).HasMaxLength(255).IsRequired();
        builder.Property(value => value.RecordKey).HasMaxLength(255).IsRequired();
        builder.Property(value => value.Payload).HasColumnType("jsonb");
        builder.Property(value => value.PayloadHash).HasMaxLength(64).IsRequired();
        builder.Property(value => value.IdempotencyKey).HasMaxLength(255).IsRequired();
        builder.Property(value => value.PdsHost).HasMaxLength(500).IsRequired();
        builder.Property(value => value.SourceEntityType).HasMaxLength(100).IsRequired();
        builder.Property(value => value.DependsOnCid).HasMaxLength(255);
        builder.Property(value => value.ExpectedCid).HasMaxLength(255);
        builder.Property(value => value.LastError).HasMaxLength(500);
        builder.Property(value => value.LeaseOwner).HasMaxLength(200);
        builder.Property(value => value.SettledUri).HasMaxLength(500);
        builder.Property(value => value.SettledCid).HasMaxLength(255);
        builder.Property(value => value.MaxRetries).HasDefaultValue(10);
        builder.Property(value => value.LeaseFence).IsConcurrencyToken();

        builder.HasOne(value => value.Tenant)
            .WithMany()
            .HasForeignKey(value => value.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.User)
            .WithMany()
            .HasForeignKey(value => value.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.TenantUser)
            .WithMany()
            .HasForeignKey(value => new { value.TenantId, value.UserId })
            .HasPrincipalKey(value => new { value.TenantId, value.UserId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.AtprotoRecord)
            .WithMany()
            .HasForeignKey(value => value.AtprotoRecordId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.DependsOnAtprotoRecord)
            .WithMany()
            .HasForeignKey(value => value.DependsOnAtprotoRecordId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.SupersededBy)
            .WithMany()
            .HasForeignKey(value => value.SupersededById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(value => new { value.TenantId, value.IdempotencyKey })
            .IsUnique();
        builder.HasIndex(value => new
        {
            value.TenantId,
            value.SourceEntityType,
            value.SourceEntityId,
            value.SourceVersion,
            value.Operation,
            value.PayloadHash
        })
            .IsUnique()
            .HasFilter("status IN (1, 2) AND superseded_at IS NULL");
        builder.HasIndex(value => new { value.Status, value.NextRetryAt, value.LeaseExpiresAt, value.CreatedAt });
        builder.HasIndex(value => new { value.TenantId, value.UserId, value.Status });
        builder.HasIndex(value => new { value.Did, value.Collection, value.RecordKey });
        builder.HasIndex(value => value.DependsOnAtprotoRecordId)
            .HasFilter("depends_on_atproto_record_id IS NOT NULL");
    }
}

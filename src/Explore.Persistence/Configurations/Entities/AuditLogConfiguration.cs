using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.Property(e => e.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(e => e.EntityType).HasMaxLength(200).IsRequired();
        builder.Property(e => e.EntityId).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Action).HasMaxLength(50).IsRequired();

        builder.Property(e => e.OldValues).HasColumnType("jsonb");
        builder.Property(e => e.NewValues).HasColumnType("jsonb");
        builder.Property(e => e.AffectedColumns).HasColumnType("jsonb");

        builder.Property(e => e.Timestamp)
            .HasDefaultValueSql("NOW()")
            .IsRequired();

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        // Query by entity (e.g., "show audit trail for Event X")
        builder.HasIndex(e => new { e.TenantId, e.EntityType, e.EntityId })
            .HasDatabaseName("ix_auditlogs_tenant_entity");

        // Query by actor (e.g., "what did user Y change?")
        builder.HasIndex(e => new { e.TenantId, e.ActorId, e.Timestamp })
            .HasDatabaseName("ix_auditlogs_tenant_actor_time")
            .IsDescending(false, false, true);

        // Time-range queries for compliance
        builder.HasIndex(e => new { e.TenantId, e.Timestamp })
            .HasDatabaseName("ix_auditlogs_tenant_time")
            .IsDescending(false, true);
    }
}

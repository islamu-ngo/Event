using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class EventResourceAuditEntryConfiguration : IEntityTypeConfiguration<EventResourceAuditEntry>
{
    public void Configure(EntityTypeBuilder<EventResourceAuditEntry> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_event_resource_audit_entries_action", "action BETWEEN 1 AND 10");
            table.HasCheckConstraint("ck_event_resource_audit_entries_outcome", "outcome BETWEEN 1 AND 3");
            table.HasCheckConstraint("ck_event_resource_audit_entries_reason", "reason BETWEEN 1 AND 5");
        });
        builder.Property(entry => entry.Id).ValueGeneratedNever();
        builder.Property(entry => entry.Action).HasConversion<int>();
        builder.Property(entry => entry.Outcome).HasConversion<int>();
        builder.Property(entry => entry.Reason).HasConversion<int>();
        builder.Property(entry => entry.Timestamp).IsRequired();

        builder.HasOne<EventResource>().WithMany()
            .HasForeignKey(entry => new { entry.TenantId, entry.EventResourceId })
            .HasPrincipalKey(resource => new { resource.TenantId, resource.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entry => new
        {
            entry.TenantId,
            entry.EventResourceId,
            entry.Timestamp,
            entry.Id
        });
        builder.HasIndex(entry => new { entry.TenantId, entry.Timestamp, entry.Id });
    }
}

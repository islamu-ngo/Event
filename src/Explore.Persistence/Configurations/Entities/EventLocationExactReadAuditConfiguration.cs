using Explore.Domain;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class EventLocationExactReadAuditConfiguration
    : IEntityTypeConfiguration<EventLocationExactReadAudit>
{
    public void Configure(EntityTypeBuilder<EventLocationExactReadAudit> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_event_location_exact_read_audits_purpose",
                "purpose BETWEEN 1 AND 4");
            table.HasCheckConstraint(
                "ck_event_location_exact_read_audits_trace",
                "correlation_id IS NOT NULL OR trace_id IS NOT NULL");
            table.HasCheckConstraint(
                "ck_event_location_exact_read_audits_uuid_v7",
                "substring(id::text, 15, 1) = '7' AND substring(id::text, 20, 1) IN ('8', '9', 'a', 'b')");
        });

        builder.Property(item => item.Id).HasValueGenerator<GuidVersion7ValueGenerator>();
        builder.Property(item => item.Purpose).HasConversion<int>();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(item => item.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EventLocation>()
            .WithMany()
            .HasForeignKey(item => new { item.TenantId, item.EventLocationId })
            .HasPrincipalKey(item => new { item.TenantId, item.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(item => new { item.TenantId, item.EventLocationId, item.OccurredAtUtc });
        builder.HasIndex(item => new { item.TenantId, item.RequesterUserId, item.OccurredAtUtc });
    }
}

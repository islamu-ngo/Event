using Explore.Domain;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class SupportAccessAuditEventConfiguration : IEntityTypeConfiguration<SupportAccessAuditEvent>
{
    public void Configure(EntityTypeBuilder<SupportAccessAuditEvent> builder)
    {
        builder.Property(e => e.Id).HasValueGenerator<GuidVersion7ValueGenerator>();
        builder.Property(e => e.OccurredAtUtc).IsRequired();
        builder.Property(e => e.RouteName).HasMaxLength(SupportAccessAuditEvent.MaxRouteNameLength);
        builder.Property(e => e.RequestName).HasMaxLength(SupportAccessAuditEvent.MaxRequestNameLength);
        builder.Property(e => e.ResourceKind).HasMaxLength(SupportAccessAuditEvent.MaxResourceKindLength);
        builder.Property(e => e.ResourceId).HasMaxLength(SupportAccessAuditEvent.MaxResourceIdLength);
        builder.Property(e => e.Action).HasMaxLength(SupportAccessAuditEvent.MaxActionLength);
        builder.Property(e => e.Outcome).HasMaxLength(SupportAccessAuditEvent.MaxOutcomeLength).IsRequired();
        builder.Property(e => e.CorrelationId).HasMaxLength(SupportAccessAuditEvent.MaxCorrelationIdLength);
        builder.Property(e => e.TraceId).HasMaxLength(SupportAccessAuditEvent.MaxTraceIdLength);
        builder.Property(e => e.SanitizedMetadataJson).HasColumnType("jsonb");

        builder.HasOne(e => e.SupportAccessSession)
            .WithMany()
            .HasForeignKey(e => e.SupportAccessSessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.EventType)
            .WithMany()
            .HasForeignKey(e => e.EventTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ActorUser)
            .WithMany()
            .HasForeignKey(e => e.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasOne(e => e.TargetTenant)
            .WithMany()
            .HasForeignKey(e => e.TargetTenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.TargetTenantUser)
            .WithMany()
            .HasForeignKey(e => e.TargetTenantUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasIndex(e => new { e.SupportAccessSessionId, e.OccurredAtUtc })
            .HasDatabaseName("ix_support_access_audit_events_session_occurred")
            .IsDescending(false, true);

        builder.HasIndex(e => new { e.TargetTenantId, e.OccurredAtUtc })
            .HasDatabaseName("ix_support_access_audit_events_tenant_occurred")
            .IsDescending(false, true);

        builder.HasIndex(e => new { e.ActorUserId, e.OccurredAtUtc })
            .HasDatabaseName("ix_support_access_audit_events_actor_occurred")
            .IsDescending(false, true);
    }
}

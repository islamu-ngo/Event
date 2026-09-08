using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class TicketTypeEntitlementConfiguration : IEntityTypeConfiguration<TicketTypeEntitlement>
{
    public void Configure(EntityTypeBuilder<TicketTypeEntitlement> builder)
    {
        builder.Property(entitlement => entitlement.Id).ValueGeneratedNever();
        builder.Property(entitlement => entitlement.ScopeId)
            .HasComputedColumnSql(
                "COALESCE(event_session_id, event_day_id, target_event_id)",
                stored: true);
        builder.HasIndex(entitlement => new
            {
                entitlement.TenantId,
                entitlement.TicketTypeId,
                entitlement.TargetEventId,
                entitlement.EntitlementScopeTypeId,
                entitlement.ScopeId
            })
            .IsUnique();
        builder.HasOne<EventTicketType>().WithMany(ticketType => ticketType.Entitlements)
            .HasForeignKey(entitlement => new { entitlement.TenantId, entitlement.TicketTypeId })
            .HasPrincipalKey(ticketType => new { ticketType.TenantId, ticketType.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Event>().WithMany().HasForeignKey(entitlement => new { entitlement.TenantId, entitlement.TargetEventId })
            .HasPrincipalKey(@event => new { @event.TenantId, @event.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EventDay>().WithMany().HasForeignKey(entitlement => new { entitlement.TenantId, entitlement.TargetEventId, entitlement.EventDayId })
            .HasPrincipalKey(day => new { day.TenantId, day.EventId, day.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EventSession>().WithMany().HasForeignKey(entitlement => new { entitlement.TenantId, entitlement.TargetEventId, entitlement.EventSessionId })
            .HasPrincipalKey(session => new { session.TenantId, session.EventId, session.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entitlement => entitlement.EntitlementScopeType).WithMany().HasForeignKey(entitlement => entitlement.EntitlementScopeTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entitlement => entitlement.EntitlementSelectionRule).WithMany().HasForeignKey(entitlement => entitlement.EntitlementSelectionRuleId).OnDelete(DeleteBehavior.Restrict);
    }
}

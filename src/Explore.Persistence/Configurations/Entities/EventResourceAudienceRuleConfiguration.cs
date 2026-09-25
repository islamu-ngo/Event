using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class EventResourceAudienceRuleConfiguration : IEntityTypeConfiguration<EventResourceAudienceRule>
{
    public void Configure(EntityTypeBuilder<EventResourceAudienceRule> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_event_resource_audience_rules_kind", "audience_kind_id BETWEEN 1 AND 9");
            table.HasCheckConstraint("ck_event_resource_audience_rules_owner_session", "(resource_event_session_id IS NULL AND resource_session_scope_id = event_id) OR (resource_event_session_id IS NOT NULL AND resource_session_scope_id = resource_event_session_id AND (event_session_id IS NULL OR event_session_id = resource_event_session_id))");
            table.HasCheckConstraint("ck_event_resource_audience_rules_ticket", "((event_ticket_catalog_version_id IS NULL AND event_ticket_type_id IS NULL) OR (event_ticket_catalog_version_id IS NOT NULL AND event_ticket_type_id IS NOT NULL AND audience_kind_id IN (4, 5)))");
            table.HasCheckConstraint("ck_event_resource_audience_rules_participant", "audience_kind_id = 3 OR (require_confirmed_order = 0 AND require_participant_approval = 0 AND require_participant_completion = 0)");
            table.HasCheckConstraint("ck_event_resource_audience_rules_target", "(audience_kind_id = 5 AND admission_target_type_id IS NOT NULL AND admission_target_type_id IN (1, 2, 3) AND admission_target_id IS NOT NULL AND admission_target_scope_id IS NOT NULL AND ((admission_target_type_id = 1 AND admission_target_scope_id = event_id) OR admission_target_type_id = 2 OR (admission_target_type_id = 3 AND event_session_id IS NOT NULL AND admission_target_scope_id = event_session_id))) OR (audience_kind_id <> 5 AND admission_target_type_id IS NULL AND admission_target_id IS NULL AND admission_target_scope_id IS NULL)");
            table.HasCheckConstraint("ck_event_resource_audience_rules_session", "(audience_kind_id IN (3, 4, 7) AND event_session_id IS NOT NULL) OR audience_kind_id = 5 OR (audience_kind_id IN (1, 2, 6, 8, 9) AND event_session_id IS NULL)");
        });

        builder.Property(rule => rule.Id).ValueGeneratedNever();
        builder.Property(rule => rule.RequireConfirmedOrder).HasConversion<int>();
        builder.Property(rule => rule.RequireParticipantApproval).HasConversion<int>();
        builder.Property(rule => rule.RequireParticipantCompletion).HasConversion<int>();

        builder.HasOne<EventSession>().WithMany()
            .HasForeignKey(rule => new { rule.TenantId, rule.EventId, rule.EventSessionId })
            .HasPrincipalKey(session => new { session.TenantId, session.EventId, session.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EventTicketCatalogVersion>().WithMany()
            .HasForeignKey(rule => new { rule.TenantId, rule.EventId, rule.EventTicketCatalogVersionId })
            .HasPrincipalKey(catalog => new { catalog.TenantId, catalog.EventId, catalog.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EventTicketType>().WithMany()
            .HasForeignKey(rule => new { rule.TenantId, rule.EventTicketCatalogVersionId, rule.EventTicketTypeId })
            .HasPrincipalKey(ticketType => new { ticketType.TenantId, ticketType.CatalogId, ticketType.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AdmissionTarget>().WithMany()
            .HasForeignKey(rule => new
            {
                rule.TenantId,
                rule.EventId,
                rule.AdmissionTargetTypeId,
                rule.AdmissionTargetId,
                rule.AdmissionTargetScopeId
            })
            .HasPrincipalKey(target => new
            {
                target.TenantId,
                target.EventId,
                target.AdmissionTargetTypeId,
                target.Id,
                target.ScopeId
            })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(rule => new
        {
            rule.TenantId,
            rule.EventResourceId,
            rule.AudienceKindId,
            rule.EventSessionId,
            rule.AdmissionTargetId
        });
    }
}

using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Extensions;

internal static class EventQueryExtensions
{
    internal static IQueryable<Event> IncludeStandardDetails(this IQueryable<Event> query)
    {
        return query
            .Include(e => e.EventType)
            .Include(e => e.EventProvenanceType)
            .Include(e => e.ParticipationConfiguration)
                .ThenInclude(configuration => configuration!.ParticipationHandlingMode)
            .Include(e => e.ParticipationConfiguration)
                .ThenInclude(configuration => configuration!.AdvanceRegistrationObligation)
            .Include(e => e.ParticipationConfiguration)
                .ThenInclude(configuration => configuration!.IdentityAccessMode)
            .Include(e => e.ParticipationConfiguration)
                .ThenInclude(configuration => configuration!.RequirementAttachments)
                .ThenInclude(attachment => attachment.RegistrationRequirement)
            .Include(e => e.ParticipationConfiguration)
                .ThenInclude(configuration => configuration!.RequirementAttachments)
                .ThenInclude(attachment => attachment.RegistrationFormVersion)
            .Include(e => e.PublicActions)
                .ThenInclude(action => action.EventPublicActionKind)
            .Include(e => e.PublicActions)
                .ThenInclude(action => action.HealthState)
            .Include(e => e.AudienceGender)
            .Include(e => e.AudienceAge)
            .Include(e => e.Actor)
                .ThenInclude(a => a.ActorType)
            .Include(e => e.Actor)
                .ThenInclude(a => a!.Pii)
            .Include(e => e.OrganizerActor)
            .Include(e => e.FeaturedImage)
            .Include(e => e.EventStatus)
            .Include(e => e.VisibilityType)
            .Include(e => e.EventFormat)
            .Include(e => e.Madhab)
            .Include(e => e.IslamicAspect)
                .ThenInclude(a => a!.Madhab)
            .Include(e => e.IslamicAspect)
                .ThenInclude(a => a!.PrimaryLanguage)
            .Include(e => e.TechAspect)
            .Include(e => e.CapacityPools)
                .ThenInclude(pool => pool.CapacityHoldPolicy)
            .Include(e => e.CapacityPools)
                .ThenInclude(pool => pool.CapacityOversellPolicy)
            .Include(e => e.TicketCatalogVersions.Where(catalog =>
                !catalog.IsDeleted && catalog.TicketCatalogStatusId == (int)TicketCatalogStatusEnum.Published))
                .ThenInclude(catalog => catalog.TicketTypes.Where(ticketType => !ticketType.IsDeleted))
            .Include(e => e.RegistrationPolicy);
    }
}

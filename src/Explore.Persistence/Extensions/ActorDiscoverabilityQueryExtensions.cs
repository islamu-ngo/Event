using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Persistence.Extensions;

internal static class ActorDiscoverabilityQueryExtensions
{
    internal static IQueryable<Actor> WhereLocallyDiscoverable(
        this IQueryable<Actor> query,
        ExploreDbContext dbContext,
        Guid tenantId)
    {
        IQueryable<Guid> publiclyEligibleActorIds = dbContext.Events
            .WherePubliclyEligible(dbContext)
            .Where(@event => @event.TenantId == tenantId && @event.AtprotoRecordId != null)
            .Select(@event => @event.ActorId);

        return query.Where(actor =>
            !actor.IsDeleted
            && !actor.IsSuspended
            && (actor.OrganizationId != null
                && dbContext.OrganizationTenants.Any(participation =>
                    participation.TenantId == tenantId
                    && participation.OrganizationId == actor.OrganizationId
                    && participation.ApprovalStatusId == (int)ApprovalStatusEnum.Approved
                    && participation.IsVisible
                    && !participation.IsSuspended
                    && !participation.IsDeleted)
                || actor.GroupId != null
                && dbContext.GroupTenants.Any(participation =>
                    participation.TenantId == tenantId
                    && participation.GroupId == actor.GroupId
                    && participation.ApprovalStatusId == (int)ApprovalStatusEnum.Approved
                    && participation.IsVisible
                    && !participation.IsSuspended
                    && !participation.IsDeleted)
                || publiclyEligibleActorIds.Contains(actor.Id)));
    }
}

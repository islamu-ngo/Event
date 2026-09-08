using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Persistence.Extensions;

internal static class PublicEventEligibilityQueryExtensions
{
    internal static IQueryable<Event> WherePubliclyEligible(
        this IQueryable<Event> query,
        ExploreDbContext dbContext) =>
        query.Where(@event =>
            !@event.IsDeleted
            && @event.EventStatusId == (int)EventStatusEnum.Published
            && @event.VisibilityTypeId == (int)VisibilityTypeEnum.Public
            && @event.Actor != null
            && !@event.Actor.IsDeleted
            && !@event.Actor.IsSuspended
            && ((@event.AtprotoRecordId == null
                || dbContext.AtprotoOutboundRecordOwnerships.Any(ownership =>
                    ownership.TenantId == @event.TenantId
                    && ownership.AtprotoRecordId == @event.AtprotoRecordId
                    && ownership.SourceEntityType == "Event"
                    && ownership.SourceEntityId == @event.Id)
                ? @event.Actor.ActorTypeId == (int)ActorTypeEnum.User
                    && @event.Actor.UserId != null
                    && dbContext.TenantUsers.Any(tenantUser =>
                        tenantUser.TenantId == @event.TenantId
                        && tenantUser.UserId == @event.Actor.UserId
                        && tenantUser.ActorId == @event.ActorId
                        && tenantUser.StatusId == (int)TenantUserStatusEnum.Active
                        && !tenantUser.IsDeleted)
                || @event.Actor.ActorTypeId == (int)ActorTypeEnum.Organization
                    && @event.Actor.OrganizationId != null
                    && dbContext.OrganizationTenants.Any(participation =>
                        participation.TenantId == @event.TenantId
                        && participation.OrganizationId == @event.Actor.OrganizationId
                        && participation.ApprovalStatusId == (int)ApprovalStatusEnum.Approved
                        && participation.IsVisible
                        && !participation.IsSuspended
                        && !participation.IsDeleted)
                || @event.Actor.ActorTypeId == (int)ActorTypeEnum.Group
                    && @event.Actor.GroupId != null
                    && dbContext.GroupTenants.Any(participation =>
                        participation.TenantId == @event.TenantId
                        && participation.GroupId == @event.Actor.GroupId
                        && participation.ApprovalStatusId == (int)ApprovalStatusEnum.Approved
                        && participation.IsVisible
                        && !participation.IsSuspended
                        && !participation.IsDeleted)
                : @event.AtprotoRecord != null
                    && @event.AtprotoRecord.TombstonedAt == null
                    && dbContext.AtprotoRecordTenantPresentations.Any(presentation =>
                        presentation.TenantId == @event.TenantId
                        && presentation.AtprotoRecordId == @event.AtprotoRecordId
                        && presentation.IsVisible
                        && presentation.SourceVersion == @event.AtprotoRecord.SourceVersion)
                    && dbContext.AtprotoIdentities.Any(identity =>
                        identity.ActorId == @event.ActorId
                        && identity.Did == @event.AtprotoRecord.Did
                        && identity.IsActive
                        && !identity.IsSuspended
                        && !identity.IsDeleted))));

    internal static IQueryable<EventSession> WherePubliclyEligible(
        this IQueryable<EventSession> query,
        ExploreDbContext dbContext)
    {
        var eligibleEventIds = dbContext.Events.WherePubliclyEligible(dbContext).Select(@event => @event.Id);

        return query
            .Where(session => eligibleEventIds.Contains(session.EventId))
            .Where(session =>
                session.EventSessionStatusId == (int)EventSessionStatusEnum.Published
                && session.StartTime != null
                && session.EndTime != null
                && (session.EventDayId == null || session.EventDay!.IsPublished));
    }

    internal static IQueryable<EventSessionGroup> WherePubliclyEligible(
        this IQueryable<EventSessionGroup> query,
        ExploreDbContext dbContext)
    {
        var eligibleEventIds = dbContext.Events.WherePubliclyEligible(dbContext).Select(@event => @event.Id);

        return query
            .Where(group => eligibleEventIds.Contains(group.EventId))
            .Where(group => group.IsPublished);
    }

    internal static IQueryable<EventSessionGroupSession> WherePubliclyEligible(
        this IQueryable<EventSessionGroupSession> query,
        ExploreDbContext dbContext)
    {
        var eligibleEventIds = dbContext.Events.WherePubliclyEligible(dbContext).Select(@event => @event.Id);

        return query
            .Where(assignment => eligibleEventIds.Contains(assignment.EventId))
            .Where(assignment =>
                assignment.EventSessionGroup.IsPublished
                && assignment.EventSession.EventSessionStatusId == (int)EventSessionStatusEnum.Published
                && assignment.EventSession.StartTime != null
                && assignment.EventSession.EndTime != null
                && (assignment.EventSession.EventDayId == null || assignment.EventSession.EventDay!.IsPublished));
    }

    internal static IQueryable<EventAgendaItem> WherePubliclyEligible(
        this IQueryable<EventAgendaItem> query,
        ExploreDbContext dbContext)
    {
        var eligibleEventIds = dbContext.Events.WherePubliclyEligible(dbContext).Select(@event => @event.Id);

        return query
            .Where(item => eligibleEventIds.Contains(item.EventId))
            .Where(item => item.EventDayId == null || item.EventDay!.IsPublished);
    }

    internal static IQueryable<EventSessionAgendaItem> WherePubliclyEligible(
        this IQueryable<EventSessionAgendaItem> query,
        ExploreDbContext dbContext)
    {
        var eligibleEventIds = dbContext.Events.WherePubliclyEligible(dbContext).Select(@event => @event.Id);

        return query
            .Where(item => eligibleEventIds.Contains(item.EventSession.EventId))
            .Where(item =>
                item.EventSession.EventSessionStatusId == (int)EventSessionStatusEnum.Published
                && item.EventSession.StartTime != null
                && item.EventSession.EndTime != null
                && (item.EventSession.EventDayId == null || item.EventSession.EventDay!.IsPublished));
    }
}

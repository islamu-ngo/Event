using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Constants;

namespace Explore.Application.Features.Events.Discovery;

public sealed class GetEventDiscoveryIdentityQueryHandler(
    IEventRepository events,
    IEventDiscoveryIdentityRepository identities,
    IEventAuthoritySnapshotService authority,
    ITenantUserRepository memberships,
    IAuthorizationProvider authorization,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    IUserContext userContext,
    ITenantLifecycleAccessService lifecycle,
    TimeProvider clock)
    : IQueryHandler<GetEventDiscoveryIdentityQuery, EventDiscoveryIdentityDto>
{
    public Task<EventDiscoveryIdentityDto> QueryAsync(
        GetEventDiscoveryIdentityQuery request, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteSerializableAsync(async token =>
        {
            Guid tenant = tenantContext.TenantId;
            if (tenant == Guid.Empty || request.EventId == Guid.Empty)
                throw new NotFoundException("Event discovery identity", request.EventId);

            bool publicTenant = await lifecycle.IsPublicAsync(tenant, token);
            bool isPublic = publicTenant
                && await events.IsPubliclyEligibleAsync(tenant, request.EventId, token);
            bool management = await AllowsAsync([request.EventId], AuthorizationActions.Events.ViewManagement, token);
            if (!isPublic && !management)
                throw new NotFoundException("Event discovery identity", request.EventId);

            var identity = await identities.FindAsync(
                tenant, EventDiscoverySourceKind.LocalEvent, request.EventId.ToString("D"), token);
            IReadOnlyList<EventDiscoveryIdentity> group = identity is null || identity.IsDeleted
                ? [] : await identities.GetGroupAsync(tenant, identity.Alias?.PrimaryIdentityId ?? identity.Id, token);
            var primary = identity?.Alias is { } alias
                ? group.SingleOrDefault(item => item.Id == alias.PrimaryIdentityId && !item.IsDeleted)
                : null;
            Guid? publicPrimaryId = primary is { SourceKind: EventDiscoverySourceKind.LocalEvent }
                && Guid.TryParseExact(primary.SourceKey, "D", out Guid primaryId)
                && publicTenant && await events.IsPubliclyEligibleAsync(tenant, primaryId, token)
                    ? primaryId : null;

            // Unavailable primaries are indistinguishable from a record without a public relationship.
            Guid? reviewTarget = management ? request.CandidateEventId ?? publicPrimaryId : null;
            if (reviewTarget == request.EventId || reviewTarget is { } candidate
                && (!publicTenant || !await events.IsPubliclyEligibleAsync(tenant, candidate, token)))
                reviewTarget = null;

            bool canReview = false;
            bool canReverse = false;
            if (management && reviewTarget is { } targetId)
            {
                var targetIdentity = await identities.FindAsync(
                    tenant, EventDiscoverySourceKind.LocalEvent, targetId.ToString("D"), token);
                var targetGroup = targetIdentity is null || targetIdentity.IsDeleted
                    ? [] : await identities.GetGroupAsync(
                        tenant, targetIdentity.Alias?.PrimaryIdentityId ?? targetIdentity.Id, token);
                var affected = group.Concat(targetGroup).DistinctBy(item => item.Id).ToArray();
                if (affected.All(item => !item.IsDeleted && item.SourceKind == EventDiscoverySourceKind.LocalEvent
                        && Guid.TryParseExact(item.SourceKey, "D", out _)))
                {
                    var ids = affected.Select(item => Guid.ParseExact(item.SourceKey, "D"))
                        .Concat([request.EventId, targetId]).Distinct().ToArray();
                    if (ids.Length <= IEventRepository.MaximumAuthorizationTargetBatchSize)
                    {
                        canReview = await AllowsAsync(ids, AuthorizationActions.Events.ReviewDiscoveryIdentity, token);
                        canReverse = publicPrimaryId == targetId
                            && await AllowsAsync(ids, AuthorizationActions.Events.ReverseDiscoveryIdentity, token);
                    }
                }
            }

            return new EventDiscoveryIdentityDto(
                request.EventId,
                management ? (await identities.GetRevisionAsync(tenant, token))?.IdentityEpoch ?? 0 : null,
                publicPrimaryId, reviewTarget,
                management && publicPrimaryId.HasValue ? identity?.Alias?.ReasonCode : null)
            {
                IsManagementView = !isPublic && management,
                CanViewCandidates = management,
                CanReview = canReview,
                CanReverse = canReverse
            };
        }, cancellationToken);

    private async Task<bool> AllowsAsync(Guid[] eventIds, string action, CancellationToken token)
    {
        Guid tenant = tenantContext.TenantId;
        if (!userContext.IsAuthenticated || userContext.UserId is not { } viewer
            || !await memberships.IsActiveTenantUserAsync(tenant, viewer, token))
            return false;
        var targets = await events.GetAuthorizationTargetsByIdsAsync(eventIds, token);
        if (targets.Count != eventIds.Length || targets.Any(target => target.TenantId != tenant || target.IsDeleted))
            return false;
        var snapshot = await authority.GetForUserAndEventsAsync(
            tenant, viewer, eventIds, clock.GetUtcNow().UtcDateTime, token);
        if (snapshot.TenantId != tenant || snapshot.UserId != viewer)
            return false;

        bool decision = EventDiscoveryIdentityAuthorizationFacts.IsDecisionAction(action);
        string permission = action == AuthorizationActions.Events.ReverseDiscoveryIdentity
            ? PermissionCodes.EventReverseDiscoveryIdentity : PermissionCodes.EventReviewDiscoveryIdentity;
        foreach (var target in targets)
        {
            snapshot.Events.TryGetValue(target.Id, out var grant);
            bool management = grant is not null && (grant.IsManager || grant.IsOwner
                || grant.PermissionCodes.Contains(PermissionCodes.EventUpdate));
            bool conflict = decision && (grant?.IsOwner == true || target.CreatedBy == viewer
                || target.SubmittedByUserId == viewer || target.Actor.UserId == viewer
                || target.OrganizerActor?.UserId == viewer);
            var facts = new EventDiscoveryIdentityAuthorizationFacts(
                tenant, target.Id, viewer, action, true, management,
                !decision || grant?.PermissionCodes.Contains(permission) == true, conflict);
            if (!facts.Allows(tenant, viewer, target.Id, action))
                return false;
            var result = await authorization.AuthorizeAsync(new AuthorizationRequest(
                ResourceKinds.Event, target.Id.ToString("D"), action,
                new AuthorizationScope(TenantId: tenant.ToString("D")), facts,
                new AuthorizationSubject(viewer), new AuthorizationTenant(tenant)), token);
            if (!result.IsAllowed)
                return false;
        }
        return true;
    }
}

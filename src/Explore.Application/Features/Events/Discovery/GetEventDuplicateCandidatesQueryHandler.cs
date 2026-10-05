using System.Collections.Immutable;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Specifications.Events;
using Explore.Domain.Constants;
using Explore.Domain.Enums;

namespace Explore.Application.Features.Events.Discovery;

public sealed class GetEventDuplicateCandidatesQueryHandler(
    IEventRepository events,
    IEventDiscoveryIdentityRepository identities,
    IEventAuthoritySnapshotService authority,
    ITenantUserRepository memberships,
    IAuthorizationProvider authorization,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    IUserContext userContext,
    TimeProvider clock)
    : IQueryHandler<GetEventDuplicateCandidatesQuery, EventDuplicateCandidatesDto>
{
    private const int CandidateLimit = 20;

    public async Task<EventDuplicateCandidatesDto> QueryAsync(
        GetEventDuplicateCandidatesQuery request, CancellationToken cancellationToken)
    {
        Guid tenant = tenantContext.TenantId;
        if (tenant == Guid.Empty || request.EventId == Guid.Empty
            || !userContext.IsAuthenticated || userContext.UserId is not { } viewer)
            throw new NotFoundException("Event discovery identity", request.EventId);

        return await unitOfWork.ExecuteSerializableAsync(async token =>
        {
            var targets = await events.GetAuthorizationTargetsByIdsAsync([request.EventId], token);
            var target = targets.SingleOrDefault();
            if (target is null || target.TenantId != tenant || target.IsDeleted)
                throw new NotFoundException("Event discovery identity", request.EventId);
            if (!await memberships.IsActiveTenantUserAsync(tenant, viewer, token))
                throw new AuthorizationException(ResourceKinds.Event, AuthorizationActions.Events.ViewManagement);
            var now = clock.GetUtcNow();
            var snapshot = await authority.GetForUserAndEventsAsync(tenant, viewer, [request.EventId], now.UtcDateTime, token);
            if (snapshot.TenantId != tenant || snapshot.UserId != viewer
                || !snapshot.Events.TryGetValue(request.EventId, out var grant)
                || !(grant.IsOwner || grant.IsManager || grant.PermissionCodes.Contains(PermissionCodes.EventUpdate)))
                throw new AuthorizationException(ResourceKinds.Event, AuthorizationActions.Events.ViewManagement);

            var facts = new EventDiscoveryIdentityAuthorizationFacts(tenant, target.Id, viewer,
                AuthorizationActions.Events.ViewManagement, true, true, true, false);
            var permission = await authorization.AuthorizeAsync(new AuthorizationRequest(
                ResourceKinds.Event, target.Id.ToString("D"), AuthorizationActions.Events.ViewManagement,
                new AuthorizationScope(TenantId: tenant.ToString("D")), facts,
                new AuthorizationSubject(viewer), new AuthorizationTenant(tenant)), token);
            if (!permission.IsAllowed)
            {
                if (permission.ReasonCode is AuthorizationDecisionReasonCodes.ProviderUnavailable
                    or AuthorizationDecisionReasonCodes.ProviderError)
                    throw new AuthorizationProviderUnavailableException(ResourceKinds.Event, AuthorizationActions.Events.ViewManagement);
                throw new AuthorizationException(ResourceKinds.Event, AuthorizationActions.Events.ViewManagement);
            }

            // All candidate rows are independently public before repository count/Take. Private venue
            // evidence, publisher allegations, and candidate totals never enter this bounded surface.
            string title = target.Title.Trim();
            string term = title[..Math.Min(title.Length, 80)];
            long revision = (await identities.GetRevisionAsync(tenant, token))?.IdentityEpoch ?? 0;
            if (term.Length < 3)
                return new EventDuplicateCandidatesDto(request.EventId, revision, [], false);
            var specification = new EventQuerySpecification()
                .And(EventFilter.PubliclyDiscoverable())
                .And(EventFilter.Status((int)EventStatusEnum.Published))
                .And(EventFilter.SearchTerm(term))
                .WithOccurrence(new(null, null, TemporalView.All, now))
                .SortBy(EventSort.Title);
            var (rows, _) = await events.GetEventsWithDetailsPaged(1, CandidateLimit + 2, specification, token);
            var visible = rows.Where(candidate => candidate.Id != request.EventId
                    && candidate.TenantId == tenant && !candidate.IsDeleted
                    && candidate.VisibilityTypeId == (int)VisibilityTypeEnum.Public
                    && candidate.EventStatusId == (int)EventStatusEnum.Published
                    && candidate.Title.Contains(term, StringComparison.OrdinalIgnoreCase)).ToArray();
            var candidates = visible.Take(CandidateLimit)
                .Select(candidate =>
                {
                    var session = candidate.Sessions.SingleOrDefault();
                    return new EventDuplicateCandidateDto(candidate.Id,
                        candidate.Title[..Math.Min(candidate.Title.Length, 200)], candidate.PublicCode,
                        "local-event", candidate.SourcePublisherName is { } publisher
                            ? publisher[..Math.Min(publisher.Length, 120)] : null,
                        session?.Id, session?.StartTime, session?.EndTime);
                }).ToImmutableArray();
            return new EventDuplicateCandidatesDto(request.EventId, revision, candidates,
                visible.Length > CandidateLimit || rows.Count == CandidateLimit + 2);
        }, cancellationToken);
    }
}

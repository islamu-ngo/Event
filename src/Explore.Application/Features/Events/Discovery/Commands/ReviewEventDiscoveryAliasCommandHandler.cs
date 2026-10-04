using System.Text.Json;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Responses;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Constants;

namespace Explore.Application.Features.Events.Discovery.Commands;

public sealed class ReviewEventDiscoveryAliasCommandHandler(
    IEventDiscoveryIdentityRepository identities,
    IEventRepository events,
    IEventAuthoritySnapshotService authority,
    ITenantUserRepository memberships,
    IAuthorizationProvider authorization,
    IAuditLogRepository audit,
    IOutboxRepository outbox,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    IUserContext userContext,
    TimeProvider clock)
    : ICommandHandler<ReviewEventDiscoveryAliasCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        ReviewEventDiscoveryAliasCommand request, CancellationToken cancellationToken)
    {
        var validation = await new ReviewEventDiscoveryAliasCommandValidator()
            .ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            return BaseCommandResponse.Validation<Guid>(validation.Errors.Select(error => error.ErrorMessage));
        if (!userContext.IsAuthenticated || userContext.UserId is not { } reviewer)
            return BaseCommandResponse.Authentication<Guid>();
        Guid tenant = tenantContext.TenantId;
        if (tenant == Guid.Empty)
            return BaseCommandResponse.Authorization<Guid>();

        Guid receiptId = Guid.CreateVersion7();
        Guid outboxId = Guid.CreateVersion7();
        DateTime occurredAt = clock.GetUtcNow().UtcDateTime;
        string action = request.Review.Decision == "reverse"
            ? AuthorizationActions.Events.ReverseDiscoveryIdentity
            : AuthorizationActions.Events.ReviewDiscoveryIdentity;
        string permission = request.Review.Decision == "reverse"
            ? PermissionCodes.EventReverseDiscoveryIdentity
            : PermissionCodes.EventReviewDiscoveryIdentity;

        try
        {
            return await unitOfWork.ExecuteSerializableAsync(async token =>
            {
                // The entire attempt is replayed by the owning UoW, including graph and authority reads.
                var member = await identities.FindAsync(tenant, EventDiscoverySourceKind.LocalEvent,
                    request.EventId.ToString("D"), token);
                var primary = await identities.FindAsync(tenant, EventDiscoverySourceKind.LocalEvent,
                    request.Review.PrimaryEventId.ToString("D"), token);
                var before = await ReadGroupsAsync(tenant, member, primary, token);
                if (before.Any(identity => identity.SourceKind != EventDiscoverySourceKind.LocalEvent
                        || !Guid.TryParseExact(identity.SourceKey, "D", out _)))
                    throw new Rejected(BaseCommandResponse.Authorization<Guid>());
                Guid[] authorityIds = before.Select(identity => Guid.ParseExact(identity.SourceKey, "D"))
                    .Concat(new[] { request.EventId, request.Review.PrimaryEventId }).Distinct().ToArray();
                if (authorityIds.Length > IEventRepository.MaximumAuthorizationTargetBatchSize)
                    throw new Rejected(BaseCommandResponse.Failure<Guid>("discovery_identity_bounded"));
                // Authority rows precede identities. Epoch comparison is deferred
                // until the graph, audit and outbox writes have all completed.
                bool active = await memberships.FenceActiveTenantUserAsync(tenant, reviewer, token);
                if (!active)
                    throw new Rejected(BaseCommandResponse.Authorization<Guid>());
                await authority.GetCommitBoundForUserAndEventsAsync(
                    tenant, reviewer, authorityIds, clock.GetUtcNow().UtcDateTime, token);
                await identities.AcquireFenceAsync(tenant,
                    before.Select(identity => identity.Id).Concat(
                        new[] { member?.Id ?? request.EventId, primary?.Id ?? request.Review.PrimaryEventId })
                        .Distinct().ToArray(), token);

                identities.ExpectRevisionAtCommit(tenant, request.Review.ExpectedRevision);

                member = await identities.FindAsync(tenant, EventDiscoverySourceKind.LocalEvent,
                    request.EventId.ToString("D"), token);
                primary = await identities.FindAsync(tenant, EventDiscoverySourceKind.LocalEvent,
                    request.Review.PrimaryEventId.ToString("D"), token);
                var graph = await ReadGroupsAsync(tenant, member, primary, token);
                if (graph.Any(identity => identity.SourceKind != EventDiscoverySourceKind.LocalEvent
                        || !Guid.TryParseExact(identity.SourceKey, "D", out _)))
                    throw new Rejected(BaseCommandResponse.Authorization<Guid>());
                Guid[] affectedIds = graph.Select(identity => Guid.ParseExact(identity.SourceKey, "D"))
                    .Concat(new[] { request.EventId, request.Review.PrimaryEventId }).Distinct().ToArray();
                if (affectedIds.Any(id => !authorityIds.Contains(id)))
                    throw new Rejected(BaseCommandResponse.Conflict(request.EventId, "discovery_revision_conflict"));

                active = await memberships.IsActiveTenantUserAsync(tenant, reviewer, token);
                var targets = await events.GetAuthorizationTargetsByIdsAsync(affectedIds, token);
                if (!active || targets.Count != affectedIds.Length
                    || targets.Any(target => target.TenantId != tenant || target.IsDeleted))
                    throw new Rejected(BaseCommandResponse.Authorization<Guid>());

                // Evaluate after all fence waits, not using the request's original time or cached claims.
                var snapshot = await authority.GetForUserAndEventsAsync(
                    tenant, reviewer, affectedIds, clock.GetUtcNow().UtcDateTime, token);
                if (snapshot.TenantId != tenant || snapshot.UserId != reviewer)
                    throw new Rejected(BaseCommandResponse.Authorization<Guid>());
                foreach (var target in targets)
                {
                    snapshot.Events.TryGetValue(target.Id, out var grant);
                    bool management = grant is not null && (grant.IsManager || grant.IsOwner
                        || grant.PermissionCodes.Contains(PermissionCodes.EventUpdate));
                    bool conflict = grant?.IsOwner == true || target.CreatedBy == reviewer
                        || target.SubmittedByUserId == reviewer || target.Actor.UserId == reviewer
                        || target.OrganizerActor?.UserId == reviewer;
                    var facts = new EventDiscoveryIdentityAuthorizationFacts(tenant, target.Id, reviewer, action,
                        active, management, grant?.PermissionCodes.Contains(permission) == true, conflict);
                    if (!facts.Allows(tenant, reviewer, target.Id, action))
                        throw new Rejected(BaseCommandResponse.Authorization<Guid>());

                    var decision = await authorization.AuthorizeAsync(new AuthorizationRequest(
                        ResourceKinds.Event, target.Id.ToString("D"), action,
                        new AuthorizationScope(TenantId: tenant.ToString("D")), facts,
                        new AuthorizationSubject(reviewer), new AuthorizationTenant(tenant)), token);
                    if (!decision.IsAllowed)
                        throw new Rejected(decision.ReasonCode is AuthorizationDecisionReasonCodes.ProviderUnavailable
                            or AuthorizationDecisionReasonCodes.ProviderError
                            ? BaseCommandResponse.Failure<Guid>("discovery_identity_unavailable")
                            : BaseCommandResponse.Authorization<Guid>());
                }

                // Provider calls can span a grant's validity boundary even while native rows are held.
                var finalAuthority = await authority.GetForUserAndEventsAsync(
                    tenant, reviewer, affectedIds, clock.GetUtcNow().UtcDateTime, token);
                if (finalAuthority.TenantId != tenant || finalAuthority.UserId != reviewer
                    || affectedIds.Any(id => !finalAuthority.Events.TryGetValue(id, out var grant)
                        || !grant.PermissionCodes.Contains(permission)
                        || !(grant.IsManager || grant.IsOwner || grant.PermissionCodes.Contains(PermissionCodes.EventUpdate))))
                    throw new Rejected(BaseCommandResponse.Authorization<Guid>());

                if (graph.Any(identity => !before.Any(previous => previous.Id == identity.Id)))
                    throw new Rejected(BaseCommandResponse.Conflict(request.EventId, "discovery_revision_conflict"));

                long decidedRevision = request.Review.ExpectedRevision;
                if (request.Review.Decision != "different-offering")
                {
                    if (request.Review.Decision == "reverse" && (member is null || primary is null))
                        throw new Rejected(BaseCommandResponse.Conflict(request.EventId, "discovery_relationship_changed"));
                    member ??= await identities.GetOrCreateAsync(tenant, EventDiscoverySourceKind.LocalEvent,
                        request.EventId.ToString("D"), token);
                    primary ??= await identities.GetOrCreateAsync(tenant, EventDiscoverySourceKind.LocalEvent,
                        request.Review.PrimaryEventId.ToString("D"), token);
                    var decided = request.Review.Decision == "reverse"
                        ? await identities.ReverseAsync(tenant, member.Id, primary.Id, request.Review.ExpectedRevision,
                            reviewer, request.Review.ReasonCode, occurredAt, token)
                        : await identities.ReviewAsync(tenant, member.Id, primary.Id, request.Review.ExpectedRevision,
                            reviewer, request.Review.ReasonCode, occurredAt, token);
                    decidedRevision = decided.IdentityEpoch;
                }

                var receipt = new EventDiscoveryIdentityCorrectionRequested(tenant, request.EventId,
                    request.Review.PrimaryEventId, decidedRevision, request.Review.Decision, request.Review.ReasonCode);
                string payload = JsonSerializer.Serialize(receipt);
                await audit.Create(new AuditLog
                {
                    Id = receiptId, TenantId = tenant, Tenant = null!,
                    EntityType = nameof(EventDiscoveryIdentity), EntityId = request.EventId.ToString("D"),
                    Action = action, ActorId = reviewer, Timestamp = occurredAt, NewValues = payload
                });
                await outbox.CreateRange([new OutboxMessage
                {
                    Id = outboxId, AggregateType = nameof(EventDiscoveryIdentity), AggregateId = request.EventId,
                    EventType = EventDiscoveryIdentityCorrectionRequested.EventType, Payload = payload,
                    Status = OutboxMessageStatus.Pending, CreatedAt = occurredAt, MaxRetries = 5
                }], token);
                return BaseCommandResponse.Success(request.EventId);
            }, cancellationToken);
        }
        catch (Rejected rejected)
        {
            return rejected.Response;
        }
        catch (ConcurrencyConflictException)
        {
            return BaseCommandResponse.Conflict(request.EventId, "discovery_revision_conflict");
        }
        catch (InvalidOperationException exception) when (exception.Message is
            "discovery_revision_conflict" or "discovery_relationship_changed" or "discovery_relationship_unchanged"
            or "discovery_source_suppressed" or "discovery_source_key_conflict"
            or "discovery_relationship_invalid" or "discovery_graph_invalid" or "discovery_identity_unavailable")
        {
            return BaseCommandResponse.Conflict(request.EventId, exception.Message);
        }
    }

    private async Task<IReadOnlyList<EventDiscoveryIdentity>> ReadGroupsAsync(
        Guid tenant, EventDiscoveryIdentity? member, EventDiscoveryIdentity? primary, CancellationToken token)
    {
        IReadOnlyList<EventDiscoveryIdentity> members = member is null
            ? [] : await identities.GetGroupAsync(tenant, member.Id, token);
        IReadOnlyList<EventDiscoveryIdentity> primaries = primary is null
            ? [] : await identities.GetGroupAsync(tenant, primary.Id, token);
        return members.Concat(primaries).DistinctBy(identity => identity.Id).ToArray();
    }

    private sealed class Rejected(BaseCommandResponse<Guid> response) : Exception
    {
        public BaseCommandResponse<Guid> Response { get; } = response;
    }
}

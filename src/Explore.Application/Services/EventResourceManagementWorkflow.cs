using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventResource;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventResources;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;

namespace Explore.Application.Services;

/// <summary>
/// Owns native draft transactions. A lease is provisional: every execution-strategy invocation
/// rechecks current authority inside its serializable transaction before changing state and audit.
/// </summary>
public sealed partial class EventResourceManagementWorkflow(
    IEventResourceRepository resources, IUnitOfWork unitOfWork,
    EventResourceStorageLifecycleService lifecycle, EventResourceAuthorityOrchestrator authority, ITenantContext tenant,
    ICurrentUserService user, IMachinePrincipalAccessor machine, TimeProvider clock)
{
    public async Task<BaseCommandResponse<Guid>> CreateAsync(Guid eventId, Guid resourceId,
        EventResourceDraftDto draft, CancellationToken cancellationToken)
    {
        if (resourceId == Guid.Empty || resourceId.Version != 7)
            return Invalid();
        var request = Request(eventId, "create");
        await using var authorized = await authority.AuthorizeAsync(request, EmptyPreparation, cancellationToken);
        if (authorized.Lease is not { } lease) return Failure(authorized.Outcome, resourceId, cancellationToken);
        var policy = lease.Snapshot.Facts.Access.GovernancePolicy;
        if (policy is null) return Failure(EventResourceAuthorityOutcome.Unavailable, resourceId, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        var audit = Audit(request, resourceId, EventResourceAuditAction.Create, now);
        try
        {
            return await unitOfWork.ExecuteSerializableAsync(async ct =>
            {
                var outcome = await authority.RecheckMutationAsync(lease, lease.Snapshot.Facts.ResourceVersion, ct);
                if (outcome != EventResourceAuthorityOutcome.Allowed) return Failure(outcome, resourceId, ct);
                // Tombstones also reserve replay identity. Replaying is conflict, not cached authority.
                if (await resources.GetReplayIdentityAsync(request.TenantId, resourceId, ct) is not null)
                    return BaseCommandResponse.Conflict(resourceId);
                if (await resources.CountActiveAsync(request.TenantId, eventId, ct) >= policy.MaxActiveResources)
                    return BaseCommandResponse.Failure<Guid>(EventResourceManagementFailureCodes.CapacityExceeded);
                if (!Governed(draft, policy)) return Invalid();
                var rules = BuildRules(draft, request.TenantId, eventId, resourceId);
                if (!await ValidLineageAsync(draft, rules, request.TenantId, eventId, resourceId, ct)) return Invalid();
                var resource = EventResource.CreateDraft(resourceId, request.TenantId, eventId, draft.EventSessionId,
                    draft.ToMetadata(), draft.DeliveryType, draft.Availability.ToDomain(), rules, request.SubjectUserId!.Value, now);
                outcome = await authority.RecheckMutationAsync(lease, lease.Snapshot.Facts.ResourceVersion, ct);
                if (outcome != EventResourceAuthorityOutcome.Allowed) return Failure(outcome, resourceId, ct);
                await resources.AddAsync(resource, ct);
                if (policy.AuditRetentionDays > 0) await resources.AddAuditEntryAsync(audit, ct);
                await resources.SaveChangesAsync(ct);
                return BaseCommandResponse.Success(resourceId);
            }, cancellationToken);
        }
        catch (ArgumentException) { return Invalid(); }
        catch (ConcurrencyConflictException) { return BaseCommandResponse.Conflict(resourceId); }
    }

    public Task<BaseCommandResponse<Guid>> UpdateAsync(Guid resourceId, Guid expectedVersion,
        EventResourceDraftDto draft, CancellationToken cancellationToken) =>
        MutateAsync(resourceId, expectedVersion, "update", EventResourceAuditAction.UpdateMetadata,
            draft, cancellationToken);

    public Task<BaseCommandResponse<Guid>> ChangeStateAsync(Guid resourceId, Guid expectedVersion,
        EventResourceManagementAction action, CancellationToken cancellationToken) => action switch
        {
            EventResourceManagementAction.Publish => MutateAsync(resourceId, expectedVersion, "publish", EventResourceAuditAction.Publish, null, cancellationToken),
            EventResourceManagementAction.Unpublish => MutateAsync(resourceId, expectedVersion, "unpublish", EventResourceAuditAction.Withdraw, null, cancellationToken),
            EventResourceManagementAction.Archive => MutateAsync(resourceId, expectedVersion, "archive", EventResourceAuditAction.Archive, null, cancellationToken),
            EventResourceManagementAction.Delete => MutateAsync(resourceId, expectedVersion, "delete", EventResourceAuditAction.Delete, null, cancellationToken),
            EventResourceManagementAction.Moderate => MutateAsync(resourceId, expectedVersion, "moderate", EventResourceAuditAction.Moderate, null, cancellationToken),
            _ => Task.FromResult(Invalid())
        };

    private async Task<BaseCommandResponse<Guid>> MutateAsync(Guid resourceId, Guid expectedVersion,
        string action, EventResourceAuditAction auditAction, EventResourceDraftDto? draft, CancellationToken cancellationToken)
    {
        var request = Request(resourceId, action);
        await using var authorized = await authority.AuthorizeAsync(request, EmptyPreparation, cancellationToken);
        if (authorized.Lease is not { } lease) return Failure(authorized.Outcome, resourceId, cancellationToken);
        var policy = lease.Snapshot.Facts.Access.GovernancePolicy;
        if (policy is null) return Failure(EventResourceAuthorityOutcome.Unavailable, resourceId, cancellationToken);
        var eventId = lease.Snapshot.Facts.Access.Parent.EventId;
        var now = clock.GetUtcNow().UtcDateTime;
        if (action == "publish" && lease.Snapshot.Facts.Policy?.PublicationStateId == (int)EventResourcePublicationStateEnum.Withdrawn)
            auditAction = EventResourceAuditAction.Republish;
        var audit = Audit(request, resourceId, auditAction, now);
        try
        {
            return await unitOfWork.ExecuteSerializableAsync(async ct =>
            {
                var outcome = await authority.RecheckMutationAsync(lease, expectedVersion, ct);
                if (outcome != EventResourceAuthorityOutcome.Allowed) return Failure(outcome, resourceId, ct);
                var resource = await resources.GetByIdForUpdateAsync(request.TenantId, eventId, resourceId, ct);
                if (resource is null) return BaseCommandResponse.NotFound<Guid>();
                if (resource.ConcurrencyStamp != expectedVersion || resource.IsDeleted
                    || resource.PublicationStateId == (int)EventResourcePublicationStateEnum.Archived && action != "delete")
                    return BaseCommandResponse.Conflict(resourceId);
                if (action == "publish" && resource.EventResourceDeliveryTypeId != (int)EventResourceDeliveryTypeEnum.StoredFile)
                    return BaseCommandResponse.Failure<Guid>(EventResourceManagementFailureCodes.PublicationUnavailable);
                EventResourceAudienceRule[]? rules = null;
                if (draft is not null)
                {
                    // Ownership and placeholder type are fixed at creation; no payload setter is used as a shortcut.
                    if (draft.EventSessionId != resource.EventSessionId || (int)draft.DeliveryType != resource.EventResourceDeliveryTypeId)
                        return Invalid();
                    if (!policy.EnabledAudiences.IsSupersetOf(draft.AudienceRules.Select(rule => rule.Kind))) return Invalid();
                    rules = BuildRules(draft, request.TenantId, eventId, resourceId);
                    if (!await ValidLineageAsync(draft, rules, request.TenantId, eventId, resourceId, ct)) return Invalid();
                }
                outcome = await authority.RecheckMutationAsync(lease, expectedVersion, ct);
                if (outcome != EventResourceAuthorityOutcome.Allowed) return Failure(outcome, resourceId, ct);
                if (action is "unpublish" or "moderate" && resource.PublicationStateId != (int)EventResourcePublicationStateEnum.Published
                    || action == "archive" && resource.PublicationStateId is not ((int)EventResourcePublicationStateEnum.Draft) and not ((int)EventResourcePublicationStateEnum.Withdrawn))
                    return BaseCommandResponse.Conflict(resourceId);
                // Domain rejection must roll back, including partially applied metadata/policy.
                switch (action)
                {
                    case "publish":
                        if (resource.PublicationStateId == (int)EventResourcePublicationStateEnum.Withdrawn)
                            resource.Republish(lease.Snapshot.Facts.Access.Parent, lease.Snapshot.Facts.Access.PayloadSafetySatisfied,
                                expectedVersion, request.SubjectUserId!.Value, now);
                        else
                            resource.Publish(lease.Snapshot.Facts.Access.Parent, lease.Snapshot.Facts.Access.PayloadSafetySatisfied,
                                expectedVersion, request.SubjectUserId!.Value, now);
                        break;
                    case "update":
                        resource.UpdateMetadata(draft!.ToMetadata(), expectedVersion, request.SubjectUserId!.Value, now);
                        resource.ReplacePolicy(draft.Availability.ToDomain(), rules!, expectedVersion, request.SubjectUserId.Value, now);
                        break;
                    case "unpublish":
                    case "moderate": resource.Withdraw(expectedVersion, request.SubjectUserId!.Value, now); break;
                    case "archive": resource.Archive(expectedVersion, request.SubjectUserId!.Value, now); break;
                    case "delete":
                        resource.Delete(expectedVersion, request.SubjectUserId!.Value, now);
                        await lifecycle.RetireAsync(request.TenantId, [resource.Id], [], now, ct);
                        break;
                    default: throw new InvalidOperationException("Unsupported resource mutation.");
                }
                resources.Update(resource);
                if (policy.AuditRetentionDays > 0) await resources.AddAuditEntryAsync(audit, ct);
                await resources.SaveChangesAsync(ct);
                return BaseCommandResponse.Success(resourceId);
            }, cancellationToken);
        }
        catch (ArgumentException) { return Invalid(); }
        catch (ConcurrencyConflictException) { return BaseCommandResponse.Conflict(resourceId); }
    }

    private async Task<bool> ValidLineageAsync(EventResourceDraftDto draft, EventResourceAudienceRule[] rules,
        Guid tenantId, Guid eventId, Guid resourceId, CancellationToken ct)
    {
        Guid[] sessions = rules.Where(rule => rule.EventSessionId.HasValue).Select(rule => rule.EventSessionId!.Value)
            .Concat(draft.EventSessionId.HasValue ? [draft.EventSessionId.Value] : []).Distinct().ToArray();
        if ((await resources.GetSessionsAsync(tenantId, eventId, sessions, 101, ct)).Count != sessions.Length) return false;
        Guid[] types = rules.Where(rule => rule.EventTicketTypeId.HasValue).Select(rule => rule.EventTicketTypeId!.Value).Distinct().ToArray();
        var ticketTypes = await resources.GetAudienceTicketTypesAsync(tenantId, eventId, types, ct);
        if (rules.Where(rule => rule.EventTicketTypeId.HasValue).Any(rule => !ticketTypes.Any(type =>
            type.Id == rule.EventTicketTypeId && type.CatalogId == rule.EventTicketCatalogVersionId))) return false;
        Guid[] targetIds = rules.Where(rule => rule.AdmissionTargetId.HasValue).Select(rule => rule.AdmissionTargetId!.Value).Distinct().ToArray();
        var targets = await resources.GetAdmissionTargetsAsync(tenantId, eventId, targetIds, 100, ct);
        if (rules.Where(rule => rule.AdmissionTargetId.HasValue).Any(rule => !targets.Any(target =>
            target.Id == rule.AdmissionTargetId && target.AdmissionTargetTypeId == rule.AdmissionTargetTypeId
            && target.ScopeId == rule.AdmissionTargetScopeId))) return false;
        if (draft.AccessibleAlternativeEventResourceId is { } alternativeId)
        {
            // Only the authorized event is searched. Never return the hidden reference's title or existence.
            if (alternativeId == resourceId) return false;
            var alternative = await resources.GetByIdAsync(tenantId, eventId, alternativeId, ct);
            if (alternative is null || alternative.PublicationStateId == (int)EventResourcePublicationStateEnum.Archived) return false;
        }
        return true;
    }

    private static EventResourceAudienceRule[] BuildRules(EventResourceDraftDto draft, Guid tenantId, Guid eventId, Guid resourceId) =>
        draft.AudienceRules.Select(rule => EventResourceAudienceRule.Create(tenantId, eventId, resourceId,
            rule.Kind, rule.EventSessionId, rule.TicketTypeId, rule.AdmissionTargetType, rule.AdmissionTargetId,
            rule.RequireConfirmedOrder, rule.RequireParticipantApproval, rule.RequireParticipantCompletion,
            rule.TicketCatalogVersionId, rule.AdmissionTargetScopeId)).ToArray();

    private static bool Governed(EventResourceDraftDto draft, EventResourceGovernancePolicy policy) =>
        policy.EnabledDeliveryTypes.Contains(draft.DeliveryType)
        && policy.EnabledAudiences.IsSupersetOf(draft.AudienceRules.Select(rule => rule.Kind));

    private EventResourceAuthorityRequest Request(Guid targetId, string action, bool collection = false) =>
        new(tenant.TenantId, targetId, user.IsAuthenticated ? user.UserId : null, machine.IsMachineCaller, action)
        { IsEventCollection = collection };

    private static EventResourceAuditEntry Audit(EventResourceAuthorityRequest request, Guid resourceId,
        EventResourceAuditAction action, DateTime now) => EventResourceAuditEntry.Create(request.TenantId, resourceId,
            request.SubjectUserId, action, EventResourceAuditOutcome.Succeeded,
            action == EventResourceAuditAction.Moderate ? EventResourceAuditReason.Moderation : EventResourceAuditReason.OrganizerMutation, now);

    private static Task<IEventResourcePrivatePreparation> EmptyPreparation(
        Explore.Application.Authorization.EventResourceAuthorizationFacts facts, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IEventResourcePrivatePreparation>(new ManagementPreparation<object>(facts.AttachmentGeneration, new object()));
    }

    private static BaseCommandResponse<Guid> Invalid() =>
        BaseCommandResponse.Validation<Guid>(["The resource draft or its scoped references are invalid."]);

    private static BaseCommandResponse<Guid> Failure(EventResourceAuthorityOutcome outcome, Guid id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return outcome switch
        {
            EventResourceAuthorityOutcome.NotFound => BaseCommandResponse.NotFound<Guid>(),
            EventResourceAuthorityOutcome.AuthenticationRequired => BaseCommandResponse.Authentication<Guid>(),
            EventResourceAuthorityOutcome.VersionConflict => BaseCommandResponse.Conflict(id),
            EventResourceAuthorityOutcome.Forbidden => BaseCommandResponse.Failure<Guid>(EventResourceManagementFailureCodes.Forbidden),
            _ => BaseCommandResponse.Failure<Guid>(EventResourceManagementFailureCodes.Unavailable)
        };
    }

    private sealed class ManagementPreparation<T>(string generation, T value) : IEventResourcePrivatePreparation
    {
        public string AttachmentGeneration { get; } = generation;
        public T Value { get; } = value;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

public enum EventResourceManagementAction { Publish, Unpublish, Archive, Delete, Moderate }

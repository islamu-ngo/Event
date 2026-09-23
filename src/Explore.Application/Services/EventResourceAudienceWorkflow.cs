using System.Collections.Immutable;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventResource;
using Explore.Application.Specifications.EventResources;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Services;

/// <summary>Complete bounded discovery through the shared fresh A/provider/B authority pipeline.</summary>
public sealed class EventResourceAudienceWorkflow(
    IEventResourceRepository resources, IUnitOfWork unitOfWork,
    EventResourceAuthorityOrchestrator authority, ITenantContext tenant,
    ICurrentUserService user, IMachinePrincipalAccessor machine, IEventResourceCursorProtector cursors)
{
    public async Task<EventResourceAudienceDetailResult> GetAsync(Guid resourceId, CancellationToken cancellationToken)
    {
        try { return await ReadDetailAsync(resourceId, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return new(EventResourceAudienceFailure.Unavailable); }
    }

    public async Task<EventResourceAudiencePageResult> ListAsync(Guid eventId, int pageSize,
        string? cursor, CancellationToken cancellationToken)
    {
        try { return await ReadPageAsync(eventId, pageSize, cursor, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return new(EventResourceAudienceFailure.Unavailable); }
    }

    private async Task<EventResourceAudienceDetailResult> ReadDetailAsync(Guid resourceId, CancellationToken cancellationToken)
    {
        var resource = await unitOfWork.ExecuteSerializableAsync(
            ct => resources.GetAuthorityResourceAsync(tenant.TenantId, resourceId, ct), cancellationToken);
        if (resource is null) return new(EventResourceAudienceFailure.NotFound);
        var rows = new List<EventResource> { resource };
        if (resource.AccessibleAlternativeEventResourceId is { } alternativeId && alternativeId != resource.Id)
        {
            var alternative = await unitOfWork.ExecuteSerializableAsync(
                ct => resources.GetAuthorityResourceAsync(tenant.TenantId, alternativeId, ct), cancellationToken);
            if (alternative?.EventId == resource.EventId) rows.Add(alternative);
        }
        var checks = rows.Select(BindVersion).ToArray();
        var decisions = await authority.AuthorizeAudienceAsync(checks, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (decisions[0].Outcome != EventResourceAuthorityOutcome.Allowed)
            return new(Failure(decisions[0].Outcome));
        var accepted = Accepted(rows, checks, decisions);
        var proofChecks = new List<EventResourceAuthorityRequest>();
        var dto = Project(resource, accepted, proofChecks);
        return new(EventResourceAudienceFailure.None, dto,
            new(Scope(resource.EventId), null, proofChecks));
    }

    private async Task<EventResourceAudiencePageResult> ReadPageAsync(Guid eventId, int pageSize,
        string? cursor, CancellationToken cancellationToken)
    {
        if (eventId == Guid.Empty || pageSize is < 1 or > 100)
            return new(EventResourceAudienceFailure.InvalidRequest);
        var scope = Scope(eventId);
        EventResourceCursorPosition? position = null;
        if (cursor is not null && (cursor.Length > 2048 || !cursors.TryUnprotect(cursor, scope, out position)))
            return new(EventResourceAudienceFailure.InvalidRequest);
        var parentRequest = Request(eventId) with { IsEventCollection = true };
        var parent = (await authority.AuthorizeAudienceAsync([parentRequest], cancellationToken))[0];
        cancellationToken.ThrowIfCancellationRequested();
        if (parent.Outcome is not (EventResourceAuthorityOutcome.Allowed or EventResourceAuthorityOutcome.NotFound))
            return new(Failure(parent.Outcome));
        EventResourceAuthorityRequest? parentBinding = parent.Outcome == EventResourceAuthorityOutcome.Allowed
            ? parentRequest with { ExpectedResourceVersion = parent.Version, ExpectedDisclosure = parent.Disclosure }
            : null;

        var rows = await unitOfWork.ExecuteSerializableAsync(async ct =>
        {
            if (await resources.CountActiveAsync(scope.TenantId, eventId, ct) > 500) return null;
            var specification = new EventResourceQuerySpecification().And(EventResourceFilter.Event(eventId))
                .And(EventResourceFilter.PublicationState((int)EventResourcePublicationStateEnum.Published));
            if (scope.SubjectUserId is null || scope.IsMachineCaller)
                specification = specification.And(EventResourceFilter.PublicDisclosure());
            return await resources.ListCandidatesAsync(scope.TenantId, 500, specification, ct);
        }, cancellationToken);
        if (rows is null) return new(parentBinding is null
            ? EventResourceAudienceFailure.NotFound : EventResourceAudienceFailure.Unavailable);
        var checks = rows.Select(BindVersion).ToArray();
        var decisions = await authority.AuthorizeAudienceAsync(checks, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        // Never return a partial public representation when provider availability is unknown.
        if (decisions.Any(decision => decision.Outcome is EventResourceAuthorityOutcome.Unavailable
                or EventResourceAuthorityOutcome.Expired)) return new(parentBinding is null
                    ? EventResourceAudienceFailure.NotFound : EventResourceAudienceFailure.Unavailable);
        var accepted = Accepted(rows, checks, decisions);
        // Some private-parent entitlements require resource-qualified session/ticket facts. An allowed
        // resource already passed the exact parent ceiling; use that fresh decision as the collection
        // witness rather than inventing a broader parent grant or stranding the eligible resource.
        if (parentBinding is null && accepted.Count == 0) return new(EventResourceAudienceFailure.NotFound);
        int start = 0;
        if (position is not null)
        {
            // Use persisted ordering, including each engine's UUID ordering. Reordering/deletion requires refresh.
            int anchor = -1;
            for (int index = 0; index < rows.Count; index++)
                if (rows[index].Id == position.ResourceId && rows[index].SortOrder == position.SortOrder) anchor = index;
            if (anchor < 0) return new(EventResourceAudienceFailure.InvalidRequest);
            start = anchor + 1;
        }
        var visible = rows.Skip(start).Where(row => accepted.ContainsKey(row.Id)).Take(pageSize + 1).ToArray();
        var proofChecks = new List<EventResourceAuthorityRequest>();
        if (parentBinding is null) proofChecks.Add(accepted.Values.First());
        var items = visible.Take(pageSize).Select(row => Project(row, accepted, proofChecks)).ToImmutableArray();
        string? next = null;
        if (visible.Length > pageSize)
        {
            proofChecks.Add(accepted[visible[pageSize].Id]);
            var last = visible[pageSize - 1];
            next = cursors.Protect(scope, new(last.SortOrder, last.Id));
        }
        var proof = new EventResourceAudienceDisclosureProof(scope, parentBinding, proofChecks);
        var final = await AuthorizeDisclosureAsync(proof, cancellationToken);
        return final == EventResourceAudienceFailure.None
            ? new(final, new(items, next), proof) : new(final);
    }

    public async Task<EventResourceAudienceFailure> AuthorizeDisclosureAsync(
        EventResourceAudienceDisclosureProof proof, CancellationToken cancellationToken)
    {
        if (proof.Scope != Scope(proof.Scope.EventId)) return EventResourceAudienceFailure.NotFound;
        if (proof.Parent is { } parent)
        {
            var result = (await authority.AuthorizeCapabilitiesAsync([parent], cancellationToken))[0];
            cancellationToken.ThrowIfCancellationRequested();
            if (result != EventResourceAuthorityOutcome.Allowed) return Failure(result);
        }
        var outcomes = await authority.AuthorizeCapabilitiesAsync(proof.Checks, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return Failure(outcomes.FirstOrDefault(outcome => outcome != EventResourceAuthorityOutcome.Allowed,
            EventResourceAuthorityOutcome.Allowed));
    }

    private EventResourceCursorScope Scope(Guid eventId) => new(tenant.TenantId, eventId,
        user.IsAuthenticated ? user.UserId : null, machine.IsMachineCaller);
    private EventResourceAuthorityRequest Request(Guid id) => new(tenant.TenantId, id,
        user.IsAuthenticated ? user.UserId : null, machine.IsMachineCaller, "view");
    private EventResourceAuthorityRequest BindVersion(EventResource row) => Request(row.Id) with
        { ExpectedResourceVersion = row.ConcurrencyStamp };

    private static Dictionary<Guid, EventResourceAuthorityRequest> Accepted(IReadOnlyList<EventResource> rows,
        EventResourceAuthorityRequest[] checks, IReadOnlyList<EventResourceAudienceDecision> decisions) =>
        rows.Select((row, index) => (row, index))
            .Where(pair => decisions[pair.index].Outcome == EventResourceAuthorityOutcome.Allowed)
            .ToDictionary(pair => pair.row.Id, pair => checks[pair.index] with
                { ExpectedDisclosure = decisions[pair.index].Disclosure });

    private static EventResourceAudienceDetailDto Project(EventResource row,
        IReadOnlyDictionary<Guid, EventResourceAuthorityRequest> accepted, List<EventResourceAuthorityRequest> proof)
    {
        var check = accepted[row.Id];
        proof.Add(check);
        var disclosure = check.ExpectedDisclosure!;
        bool privateMetadata = disclosure.DisclosePrivateMetadata;
        Guid? alternative = null;
        if (privateMetadata && row.AccessibleAlternativeEventResourceId is { } id
            && accepted.TryGetValue(id, out var alternativeCheck))
        {
            alternative = id;
            proof.Add(alternativeCheck);
        }
        return new(row.Id, row.EventId, privateMetadata ? row.Title : row.PublicTitle!,
            (EventResourceKindEnum)row.EventResourceKindId, !privateMetadata,
            disclosure.CanAccess ? "available" : "unavailable",
            row.AudienceRules.Any(rule => rule.AudienceKindId == (int)EventResourceAudienceKindEnum.Public)
                ? "public" : "eligibility-required",
            privateMetadata ? row.Description : null, privateMetadata ? row.LanguageCode : null,
            privateMetadata ? row.AccessibilityNote : null, alternative);
    }

    private static EventResourceAudienceFailure Failure(EventResourceAuthorityOutcome outcome) => outcome switch
    {
        EventResourceAuthorityOutcome.Allowed => EventResourceAudienceFailure.None,
        EventResourceAuthorityOutcome.AuthenticationRequired => EventResourceAudienceFailure.AuthenticationRequired,
        EventResourceAuthorityOutcome.Forbidden => EventResourceAudienceFailure.Forbidden,
        EventResourceAuthorityOutcome.NotFound => EventResourceAudienceFailure.NotFound,
        _ => EventResourceAudienceFailure.Unavailable
    };
}

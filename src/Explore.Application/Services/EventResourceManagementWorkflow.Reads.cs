using System.Collections.Immutable;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventResource;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Services;

public sealed partial class EventResourceManagementWorkflow
{
    public Task<EventResourceManagementReadResult<EventResourceManagementDto>> GetAsync(
        Guid resourceId, CancellationToken cancellationToken) => ReadAsync(Request(resourceId, "view-management"),
        async (facts, ct) =>
        {
            var resource = await resources.GetByIdAsync(facts.Access.TenantId, facts.Access.Parent.EventId, resourceId, ct);
            return resource is null ? null : Map(resource);
        }, cancellationToken);

    public async Task<EventResourceManagementReadResult<EventResourceManagementPageDto>> ListAsync(
        Guid eventId, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            throw new BadRequestException("Invalid management page bounds.");
        var parent = Request(eventId, "view-management", collection: true);
        var admission = (await authority.AuthorizeCapabilitiesAsync([parent], cancellationToken))[0];
        if (admission != EventResourceAuthorityOutcome.Allowed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return EventResourceManagementReadResult.Denied<EventResourceManagementPageDto>(admission);
        }
        var prepared = await unitOfWork.ExecuteSerializableAsync(async ct =>
        {
            var rows = await resources.ListManagementAsync(parent.TenantId, eventId, (page - 1) * pageSize, pageSize, ct);
            return new EventResourceManagementPageDto(rows.Select(Map).ToImmutableArray(), page, pageSize);
        }, cancellationToken);
        var denial = await AuthorizeDisclosureAsync(eventId, prepared.Items, cancellationToken);
        return denial == EventResourceAuthorityOutcome.Allowed
            ? EventResourceManagementReadResult.Success(prepared)
            : EventResourceManagementReadResult.Denied<EventResourceManagementPageDto>(denial);
    }

    public async Task<EventResourceAuthorityOutcome> AuthorizeDisclosureAsync(
        Guid? collectionEventId, ImmutableArray<EventResourceManagementDto> items, CancellationToken cancellationToken)
    {
        if (items.IsDefault || items.Length > 100 || collectionEventId == Guid.Empty
            || (collectionEventId is null && items.Length != 1)
            || items.Any(item => item.Id == Guid.Empty || item.Version == Guid.Empty
                || (collectionEventId is { } eventId && item.EventId != eventId)))
            throw new BadRequestException("Invalid management disclosure scope.");
        var checks = items.Select(item => Request(item.Id, "view-management") with
            { ExpectedResourceVersion = item.Version }).ToList();
        if (collectionEventId is { } parentId)
            checks.Insert(0, Request(parentId, "view-management", collection: true));
        var decisions = await authority.AuthorizeCapabilitiesAsync(checks, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return decisions.FirstOrDefault(outcome => outcome != EventResourceAuthorityOutcome.Allowed,
            EventResourceAuthorityOutcome.Allowed);
    }

    public async Task<EventResourceManagementReadResult<EventResourceAuditPageDto>> GetAuditAsync(
        Guid resourceId, int limit, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 200) throw new BadRequestException("Invalid audit page bounds.");
        int retention = 0;
        var result = await ReadAsync(Request(resourceId, "view-audit"), async (facts, ct) =>
        {
            retention = facts.Access.GovernancePolicy?.AuditRetentionDays ?? 0;
            if (retention == 0) return new EventResourceAuditPageDto([]);
            var rows = await resources.GetUnexpiredAuditEntriesAsync(facts.Access.TenantId, resourceId,
                clock.GetUtcNow().UtcDateTime.AddDays(-retention), limit, ct);
            return new EventResourceAuditPageDto(rows.Select(row => new EventResourceAuditDto(row.Id,
                row.Action, row.Outcome, row.Reason, row.Timestamp, row.ResponsibleManagerUserId)).ToImmutableArray());
        }, cancellationToken);
        if (result.Value is not { } value) return result;
        // Expiry is a read boundary, not merely a promise that the sweeper will eventually run.
        var cutoff = clock.GetUtcNow().UtcDateTime.AddDays(-retention);
        return EventResourceManagementReadResult.Success(new EventResourceAuditPageDto(
            value.Items.Where(row => retention > 0 && row.Timestamp > cutoff).ToImmutableArray()));
    }

    private async Task<EventResourceManagementReadResult<T>> ReadAsync<T>(EventResourceAuthorityRequest request,
        Func<EventResourceAuthorizationFacts, CancellationToken, Task<T?>> load, CancellationToken cancellationToken)
        where T : class
    {
        await using var authorized = await authority.AuthorizeAsync(request, async (facts, ct) =>
        {
            var value = await unitOfWork.ExecuteSerializableAsync(token => load(facts, token), ct);
            return new ManagementPreparation<T?>(facts.AttachmentGeneration, value);
        }, cancellationToken);
        if (authorized.Lease is not { } lease)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return EventResourceManagementReadResult.Denied<T>(authorized.Outcome);
        }
        var gate = await authority.CompleteHeadersAsync(lease, cancellationToken);
        if (gate.Outcome != EventResourceAuthorityOutcome.Allowed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return EventResourceManagementReadResult.Denied<T>(gate.Outcome);
        }
        await using var preparation = (ManagementPreparation<T?>)gate.Preparation!;
        return preparation.Value is { } value
            ? EventResourceManagementReadResult.Success(value)
            : EventResourceManagementReadResult.Denied<T>(EventResourceAuthorityOutcome.NotFound);
    }

    private static EventResourceManagementDto Map(EventResource resource) => new(resource.Id, resource.EventId,
        resource.ConcurrencyStamp, (EventResourcePublicationStateEnum)resource.PublicationStateId, new()
        {
            Title = resource.Title, PublicTitle = resource.PublicTitle, Description = resource.Description,
            SensitiveNotes = resource.SensitiveNotes, Kind = (EventResourceKindEnum)resource.EventResourceKindId,
            DisclosureMode = (EventResourceDisclosureModeEnum)resource.DisclosureModeId,
            DeliveryType = (EventResourceDeliveryTypeEnum)resource.EventResourceDeliveryTypeId,
            EventSessionId = resource.EventSessionId, LanguageCode = resource.LanguageCode,
            AccessibilityNote = resource.AccessibilityNote, SortOrder = resource.SortOrder,
            AccessibleAlternativeEventResourceId = resource.AccessibleAlternativeEventResourceId,
            Availability = new(resource.AvailabilityAbsoluteStartUtc, resource.AvailabilityAbsoluteEndUtc,
                (EventResourceAvailabilityAnchorEnum?)resource.AvailabilityStartAnchorId, resource.AvailabilityStartOffsetTicks,
                (EventResourceAvailabilityAnchorEnum?)resource.AvailabilityEndAnchorId, resource.AvailabilityEndOffsetTicks),
            AudienceRules = resource.AudienceRules.Select(rule => new EventResourceAudienceDto(
                (EventResourceAudienceKindEnum)rule.AudienceKindId, rule.EventSessionId, rule.EventTicketCatalogVersionId,
                rule.EventTicketTypeId, (AdmissionTargetTypeEnum?)rule.AdmissionTargetTypeId, rule.AdmissionTargetId,
                rule.AdmissionTargetScopeId, rule.RequireConfirmedOrder, rule.RequireParticipantApproval,
                rule.RequireParticipantCompletion)).ToImmutableArray()
        }, resource.CreatedAt, resource.UpdatedAt);
}

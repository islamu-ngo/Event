using System.Collections.Immutable;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventResource;
using Explore.Application.Exceptions;
using Explore.Domain.Enums;

namespace Explore.Application.Services;

public sealed partial class EventResourceManagementWorkflow
{
    public async Task<EventResourceManagementReadResult<EventResourceMetadataExportPageDto>> ExportAsync(
        Guid eventId, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (eventId == Guid.Empty || page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            throw new BadRequestException("Invalid metadata export bounds.");
        var parent = Request(eventId, "export", collection: true);
        var admission = (await authority.AuthorizeCapabilitiesAsync([parent], cancellationToken))[0];
        cancellationToken.ThrowIfCancellationRequested();
        if (admission != EventResourceAuthorityOutcome.Allowed)
            return EventResourceManagementReadResult.Denied<EventResourceMetadataExportPageDto>(admission);
        (EventResourceMetadataExportPageDto Page, EventResourceAuthorityRequest[] Checks) prepared;
        try
        {
            prepared = await unitOfWork.ExecuteSerializableAsync(async ct =>
            {
                var rows = await resources.ListManagementAsync(parent.TenantId, eventId, (page - 1) * pageSize, pageSize, ct);
                var items = rows.Select(row =>
                {
                    var semantics = Map(row).Draft;
                    return new EventResourceMetadataExportDto
                    {
                        Id = row.Id, EventSessionId = row.EventSessionId,
                        PublicationState = (EventResourcePublicationStateEnum)row.PublicationStateId,
                        Title = semantics.Title, PublicTitle = semantics.PublicTitle,
                        Description = semantics.Description, SensitiveNotes = semantics.SensitiveNotes,
                        Kind = semantics.Kind, DisclosureMode = semantics.DisclosureMode,
                        DeliveryType = semantics.DeliveryType, LanguageCode = semantics.LanguageCode,
                        AccessibilityNote = semantics.AccessibilityNote, SortOrder = semantics.SortOrder,
                        AccessibleAlternativeEventResourceId = semantics.AccessibleAlternativeEventResourceId,
                        Availability = semantics.Availability, AudienceRules = semantics.AudienceRules
                    };
                }).ToImmutableArray();
                EventResourceAuthorityRequest[] checks =
                [
                    parent,
                    .. rows.Select(row => Request(row.Id, "export") with { ExpectedResourceVersion = row.ConcurrencyStamp })
                ];
                return (Page: new EventResourceMetadataExportPageDto(eventId, page, pageSize, items), Checks: checks);
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            return EventResourceManagementReadResult.Denied<EventResourceMetadataExportPageDto>(
                EventResourceAuthorityOutcome.Unavailable);
        }
        var decisions = await authority.AuthorizeCapabilitiesAsync(prepared.Checks, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var failure = decisions.FirstOrDefault(outcome => outcome != EventResourceAuthorityOutcome.Allowed,
            EventResourceAuthorityOutcome.Allowed);
        return failure == EventResourceAuthorityOutcome.Allowed
            ? EventResourceManagementReadResult.Success(prepared.Page)
            : EventResourceManagementReadResult.Denied<EventResourceMetadataExportPageDto>(failure);
    }
}

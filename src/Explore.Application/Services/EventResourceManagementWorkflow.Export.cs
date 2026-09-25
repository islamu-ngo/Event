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
        (EventResourceMetadataExportPageDto Page, EventResourceAuthorityRequest[] ExportChecks,
            EventResourceAuthorityRequest[] DownloadChecks) prepared;
        try
        {
            prepared = await unitOfWork.ExecuteSerializableAsync(async ct =>
            {
                var rows = await resources.ListManagementAsync(parent.TenantId, eventId,
                    (page - 1) * pageSize, pageSize, ct);
                var storageIds = rows.Select(row => row.StorageObjectId).OfType<Guid>().Distinct().ToArray();
                var storage = (await resources.GetStorageObjectsAsync(parent.TenantId, storageIds, ct))
                    .ToDictionary(item => item.Id);
                var files = rows.ToDictionary(row => row.Id, row => EventResourceFileSafety.Describe(row,
                    storage.GetValueOrDefault(row.StorageObjectId ?? Guid.Empty)));
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
                        Availability = semantics.Availability, AudienceRules = semantics.AudienceRules,
                        File = files[row.Id]
                    };
                }).ToImmutableArray();
                var exportChecks = rows.Select(row => Request(row.Id, "export") with
                {
                    ExpectedResourceVersion = row.ConcurrencyStamp,
                    ExpectedAttachmentGeneration = files[row.Id]?.AttachmentGeneration
                }).ToArray();
                var downloadChecks = rows.Where(row => files[row.Id] is not null).Select(row =>
                    Request(row.Id, "download") with
                    {
                        ExpectedResourceVersion = row.ConcurrencyStamp,
                        ExpectedAttachmentGeneration = files[row.Id]!.AttachmentGeneration
                    }).ToArray();
                return (new EventResourceMetadataExportPageDto(eventId, page, pageSize, items),
                    exportChecks, downloadChecks);
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            return EventResourceManagementReadResult.Denied<EventResourceMetadataExportPageDto>(
                EventResourceAuthorityOutcome.Unavailable);
        }

        EventResourceAuthorityRequest[] checks = [parent, .. prepared.ExportChecks, .. prepared.DownloadChecks];
        var decisions = await authority.AuthorizeCapabilitiesAsync(checks, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        int exportDecisionCount = 1 + prepared.ExportChecks.Length;
        var failure = decisions.Take(exportDecisionCount).FirstOrDefault(
            outcome => outcome != EventResourceAuthorityOutcome.Allowed, EventResourceAuthorityOutcome.Allowed);
        if (failure != EventResourceAuthorityOutcome.Allowed)
            return EventResourceManagementReadResult.Denied<EventResourceMetadataExportPageDto>(failure);

        var downloadable = prepared.DownloadChecks.Select((check, index) => (check.ResourceId,
                Allowed: decisions[exportDecisionCount + index] == EventResourceAuthorityOutcome.Allowed))
            .Where(result => result.Allowed).Select(result => result.ResourceId).ToHashSet();
        var pageResult = prepared.Page with
        {
            Items = prepared.Page.Items.Select(item => item with
                { DownloadAuthorized = downloadable.Contains(item.Id) }).ToImmutableArray()
        };
        return EventResourceManagementReadResult.Success(pageResult);
    }
}

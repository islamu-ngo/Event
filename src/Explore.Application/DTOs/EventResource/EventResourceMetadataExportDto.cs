using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Explore.Domain.Enums;

namespace Explore.Application.DTOs.EventResource;

/// <summary>Portable semantics only; delivery storage, protection and audit state are not part of this contract.</summary>
public sealed record EventResourceMetadataExportDto
{
    public required Guid Id { get; init; }
    public Guid? EventSessionId { get; init; }
    public required EventResourcePublicationStateEnum PublicationState { get; init; }
    public required string Title { get; init; }
    public string? PublicTitle { get; init; }
    public string? Description { get; init; }
    public string? SensitiveNotes { get; init; }
    public required EventResourceKindEnum Kind { get; init; }
    public required EventResourceDisclosureModeEnum DisclosureMode { get; init; }
    public required EventResourceDeliveryTypeEnum DeliveryType { get; init; }
    public string? LanguageCode { get; init; }
    public string? AccessibilityNote { get; init; }
    public int SortOrder { get; init; }
    public Guid? AccessibleAlternativeEventResourceId { get; init; }
    public required EventResourceTimeIntentDto Availability { get; init; }
    public required ImmutableArray<EventResourceAudienceDto> AudienceRules { get; init; }
    public EventResourceFileMetadataDto? File { get; init; }
    public EventResourceDownloadReferenceDto? Download { get; init; }

    [JsonIgnore]
    public bool DownloadAuthorized { get; init; }

    public override string ToString() => nameof(EventResourceMetadataExportDto);
}

/// <summary>Application-controlled content route; never a provider or storage capability.</summary>
public sealed record EventResourceDownloadReferenceDto(string Href)
{
    public override string ToString() => nameof(EventResourceDownloadReferenceDto);
}

public sealed record EventResourceMetadataExportPageDto(
    Guid EventId, int Page, int PageSize, ImmutableArray<EventResourceMetadataExportDto> Items)
{
    public override string ToString() => nameof(EventResourceMetadataExportPageDto);
}

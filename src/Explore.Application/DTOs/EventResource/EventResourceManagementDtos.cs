using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Explore.Application.Hateoas;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;

namespace Explore.Application.DTOs.EventResource;

public sealed record EventResourceAudienceDto(
    EventResourceAudienceKindEnum Kind,
    Guid? EventSessionId = null,
    Guid? TicketCatalogVersionId = null,
    Guid? TicketTypeId = null,
    AdmissionTargetTypeEnum? AdmissionTargetType = null,
    Guid? AdmissionTargetId = null,
    Guid? AdmissionTargetScopeId = null,
    bool? RequireConfirmedOrder = null,
    bool RequireParticipantApproval = false,
    bool RequireParticipantCompletion = false);

public sealed record EventResourceTimeIntentDto(
    DateTimeOffset? AbsoluteStartUtc = null, DateTimeOffset? AbsoluteEndUtc = null,
    EventResourceAvailabilityAnchorEnum? StartAnchor = null, long? StartOffsetTicks = null,
    EventResourceAvailabilityAnchorEnum? EndAnchor = null, long? EndOffsetTicks = null)
{
    internal EventResourceAvailability ToDomain() => EventResourceAvailability.Create(
        AbsoluteStartUtc, AbsoluteEndUtc, StartAnchor,
        StartOffsetTicks.HasValue ? TimeSpan.FromTicks(StartOffsetTicks.Value) : null,
        EndAnchor, EndOffsetTicks.HasValue ? TimeSpan.FromTicks(EndOffsetTicks.Value) : null);
}

/// <summary>Semantic authoring only. Delivery placeholders cannot configure or publish content.</summary>
public sealed record EventResourceDraftDto
{
    public required string Title { get; init; }
    public string? PublicTitle { get; init; }
    public string? Description { get; init; }
    public string? SensitiveNotes { get; init; }
    public required EventResourceKindEnum Kind { get; init; }
    public required EventResourceDisclosureModeEnum DisclosureMode { get; init; }
    public required EventResourceDeliveryTypeEnum DeliveryType { get; init; }
    public Guid? EventSessionId { get; init; }
    public string? LanguageCode { get; init; }
    public string? AccessibilityNote { get; init; }
    public int SortOrder { get; init; }
    public Guid? AccessibleAlternativeEventResourceId { get; init; }
    public EventResourceTimeIntentDto Availability { get; init; } = new();
    public ImmutableArray<EventResourceAudienceDto> AudienceRules { get; init; } = [];
    public override string ToString() => nameof(EventResourceDraftDto);

    internal EventResourceMetadata ToMetadata() => new()
    {
        Title = Title, PublicTitle = PublicTitle, Description = Description, SensitiveNotes = SensitiveNotes,
        Kind = Kind, DisclosureMode = DisclosureMode, LanguageCode = LanguageCode,
        AccessibilityNote = AccessibilityNote, SortOrder = SortOrder,
        AccessibleAlternativeEventResourceId = AccessibleAlternativeEventResourceId
    };
}

public sealed record EventResourceManagementDto(Guid Id, Guid EventId, Guid Version,
    EventResourcePublicationStateEnum PublicationState, EventResourceDraftDto Draft,
    DateTime CreatedAt, DateTime? UpdatedAt, EventResourceFileMetadataDto? File = null)
{
    public override string ToString() => nameof(EventResourceManagementDto);
}

public sealed record EventResourceManagementPageDto(ImmutableArray<EventResourceManagementDto> Items,
    int Page, int PageSize)
{
    public override string ToString() => nameof(EventResourceManagementPageDto);
}

/// <summary>A bounded management page without totals or links derived from undisclosed rows.</summary>
public sealed record EventResourceManagementCollectionDto(int PageNumber, int PageSize,
    [property: JsonPropertyName("_links")] ImmutableDictionary<string, HalLink> Links,
    [property: JsonPropertyName("_embedded")] HalCollectionEmbedded<EventResourceManagementDto> Embedded);

public sealed record EventResourceAuditDto(Guid Id, EventResourceAuditAction Action,
    EventResourceAuditOutcome Outcome, EventResourceAuditReason Reason, DateTime Timestamp,
    Guid? ResponsibleManagerUserId);

public sealed record EventResourceAuditPageDto(ImmutableArray<EventResourceAuditDto> Items);

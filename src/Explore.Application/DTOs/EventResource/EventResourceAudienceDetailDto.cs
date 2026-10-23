using System.Text.Json.Serialization;
using Explore.Domain.Enums;

namespace Explore.Application.DTOs.EventResource;

/// <summary>Explicit audience projection. Public teasers never fall back to a private title.</summary>
public sealed record EventResourceAudienceDetailDto(
    Guid Id, Guid EventId, string? Title, EventResourceKindEnum? Kind,
    bool IsTeaser, string Availability, string? Requirements,
    string? Description = null, string? LanguageCode = null,
    string? AccessibilityNote = null, Guid? AccessibleAlternativeEventResourceId = null,
    EventResourceFileMetadataDto? File = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ExternalDestinationSafeOrigin = null)
{
    /// <summary>The exact operationally quarantined material, not a finding against a human.</summary>
    public Guid? MaterialVersionId { get; init; }

    /// <summary>Server-derived affordances bound to the native terminal disclosure proof, never input grants.</summary>
    [JsonIgnore]
    public bool DownloadAuthorized { get; internal init; }
    [JsonIgnore]
    public bool AccessAuthorized { get; internal init; }
    [JsonIgnore]
    public bool ManagementAuthorized { get; internal init; }

    public override string ToString() => nameof(EventResourceAudienceDetailDto);
}

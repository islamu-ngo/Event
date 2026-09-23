using Explore.Domain.Enums;

namespace Explore.Application.DTOs.EventResource;

/// <summary>Explicit audience projection. Public teasers never fall back to a private title.</summary>
public sealed record EventResourceAudienceDetailDto(
    Guid Id, Guid EventId, string Title, EventResourceKindEnum Kind,
    bool IsTeaser, string Availability, string Requirements,
    string? Description = null, string? LanguageCode = null,
    string? AccessibilityNote = null, Guid? AccessibleAlternativeEventResourceId = null)
{
    public override string ToString() => nameof(EventResourceAudienceDetailDto);
}

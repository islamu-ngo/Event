using System;

namespace Explore.Application.DTOs.Event;

public sealed record UpdateEventDraftRequestDto
{
    public Guid ExpectedConcurrencyStamp { get; init; }
    public Guid ExpectedParticipationConfigurationConcurrencyStamp { get; init; }

    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public string? Description { get; init; }
    public string? Content { get; init; }
    public string? Slug { get; init; }

    public int? EventTypeId { get; init; }
    public int? AudienceGenderId { get; init; }
    public int? AudienceAgeId { get; init; }

    public Guid? OrganizationId { get; init; }
    public Guid? GroupId { get; init; }

    public Guid? FeaturedImageId { get; init; }

    public required ConfigureEventParticipationDto ParticipationConfiguration { get; init; }

    public int VisibilityTypeId { get; init; } = 1;
    public int EventFormatId { get; init; } = 1;
    public int? MadhabId { get; init; }

    public string? Timezone { get; init; }
    public string? EventTimeZoneId { get; init; }

    public string? BackgroundColor { get; init; }
    public string? BackgroundEffect { get; init; }
    public Guid? BackgroundImageId { get; init; }

    public Guid? TemplateId { get; init; }
    public Guid? EventSeriesId { get; init; }
    public int? SeriesOrder { get; init; }
    public int? RegistrationPolicyId { get; init; }
}

using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Models.Events;

public sealed class EventDraftEditModel
{
    public Guid ExpectedConcurrencyStamp { get; set; }
    public required string Title { get; set; }
    public string? Subtitle { get; set; }
    public string? Description { get; set; }
    public string? Content { get; set; }
    public string? Slug { get; set; }
    public int? EventTypeId { get; set; }
    public int? AudienceGenderId { get; set; }
    public int? AudienceAgeId { get; set; }
    public Guid? OrganizationId { get; set; }
    public Guid? GroupId { get; set; }
    public Guid? FeaturedImageId { get; set; }
    public ParticipationConfiguration? ParticipationConfiguration { get; set; }
    public int? VisibilityTypeId { get; set; } = 1;
    public int? EventFormatId { get; set; } = 1;
    public int? MadhabId { get; set; }
    public string? Timezone { get; set; }
    public string? EventTimeZoneId { get; set; }
    public string? BackgroundColor { get; set; }
    public string? BackgroundEffect { get; set; }
    public Guid? BackgroundImageId { get; set; }
    public Guid? TemplateId { get; set; }
    public Guid? EventSeriesId { get; set; }
    public int? SeriesOrder { get; set; }
    public int? RegistrationPolicyId { get; set; }
}

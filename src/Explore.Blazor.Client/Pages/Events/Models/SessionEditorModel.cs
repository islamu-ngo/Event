using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Pages.Events.Models;

public class SessionEditorModel
{
    public Guid? Id { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public DateTime StartTime { get; set; } = DateTime.Today.AddHours(9);
    public DateTime EndTime { get; set; } = DateTime.Today.AddHours(17);
    public Guid? LocationId { get; set; }
    public int? MaxAudienceAttendees { get; set; }
    public int? RegistrationModeId { get; set; } = 1;
    public Guid? SessionTemplateId { get; set; }
    public IReadOnlyCollection<int> LanguageIds { get; set; } = new HashSet<int>();

    // Media
    public Guid? FeaturedImageId { get; set; }
    public string? FeaturedImagePreviewUrl { get; set; }
    public bool UseEventImage { get; set; } = true;
    public static SessionEditorModel FromDto(EventSessionListDto dto, string? eventImageUri = null)
    {
        // var hasOwnImage = dto.FeaturedImageId.HasValue;
        return new SessionEditorModel
        {
            Id = dto.Id,
            Title = dto.Title ?? dto.EventTitle,
            Description = null,
            StartTime = dto.StartTime?.LocalDateTime ?? DateTime.Now,
            EndTime = dto.EndTime?.LocalDateTime ?? DateTime.Now.AddHours(1),
            LocationId = dto.LocationId,
            MaxAudienceAttendees = dto.MaxAudienceAttendees,
            RegistrationModeId = dto.RegistrationModeId
            // FeaturedImageId = dto.FeaturedImageId,
            // FeaturedImagePreviewUrl = dto.FeaturedImageUri ?? eventImageUri,
            // UseEventImage = !hasOwnImage
        };
    }

}

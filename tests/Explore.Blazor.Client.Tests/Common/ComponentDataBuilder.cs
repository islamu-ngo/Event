using Bogus;

namespace Explore.Blazor.Client.Tests.Common;

/// <summary>
/// Bogus-based fake data generators for Blazor client DTOs.
/// Follows the DataBuilder pattern established in Event.Application.UnitTests.
/// </summary>
/// <remarks>
/// <para>
/// All property names and types are verified against the actual NSwag-generated DTOs
/// in Explore.Blazor.Client.Clients.EventApiTagClients.g.cs
/// </para>
/// <para>
/// Key type mappings (verified from API client):
/// - FirstSessionDate, LastSessionDate: DateTimeOffset? (not DateOnly)
/// - CreateOrganizationDto.Postcode: int (not string)
/// - OrganizationListDto.Postcode: string
/// - Property names use FullName suffix (e.g., EventTypeFullName, not EventTypeName)
/// </para>
/// <para>
/// Usage:
/// <code>
/// var events = ComponentDataBuilder.EventListDto.Generate(5);
/// var singleEvent = ComponentDataBuilder.EventDto.Generate();
/// </code>
/// </para>
/// </remarks>
public static class ComponentDataBuilder
{
    #region Event DTOs

    /// <summary>
    /// Generates fake EventListDto for list displays.
    /// Property names match EventApiTagClients.g.cs
    /// </summary>
    public static Faker<EventListDto> EventListDto => new Faker<EventListDto>()
        .RuleFor(e => e.Id, f => Guid.NewGuid())
        .RuleFor(e => e.Title, f => f.Lorem.Sentence(3, 5))
        .RuleFor(e => e.Subtitle, f => f.Lorem.Sentence(5, 10))
        .RuleFor(e => e.Description, f => f.Lorem.Paragraph())
        .RuleFor(e => e.Slug, f => f.Lorem.Slug(3))
        // DateTimeOffset? for FirstSessionDate (verified from API)
        .RuleFor(e => e.FirstSessionDate, f => f.Date.FutureOffset(1))
        .RuleFor(e => e.LastSessionDate, (f, e) => e.FirstSessionDate?.AddDays(f.Random.Int(0, 7)))
        .RuleFor(e => e.TotalViews, f => f.Random.Int(0, 10000))
        // Event Type
        .RuleFor(e => e.EventTypeId, f => f.Random.Int(1, 5))
        .RuleFor(e => e.EventTypeFullName, f => f.PickRandom("Conference", "Workshop", "Webinar", "Seminar", "Lecture"))
        // Audience Gender
        .RuleFor(e => e.AudienceGenderId, f => f.Random.Int(1, 4))
        .RuleFor(e => e.AudienceGenderFullName, f => f.PickRandom("Mixed", "Men Only", "Women Only", "Family"))
        // Audience Age
        .RuleFor(e => e.AudienceAgeId, f => f.Random.Int(1, 5))
        .RuleFor(e => e.AudienceAgeFullName, f => f.PickRandom("All Ages", "Adults", "Youth", "Children", "Seniors"))
        .RuleFor(e => e.AudienceAgeMinAge, f => f.Random.Int(0, 12))
        .RuleFor(e => e.AudienceAgeMaxAge, f => f.Random.Int(18, 99))
        // Actor
        .RuleFor(e => e.ActorId, f => Guid.NewGuid())
        .RuleFor(e => e.ActorDisplayName, f => f.Company.CompanyName())
        .RuleFor(e => e.ActorTypeId, f => f.Random.Int(1, 3))
        .RuleFor(e => e.ActorTypeFullName, f => f.PickRandom("Organization", "User", "System"))
        // Event Status
        .RuleFor(e => e.EventStatusId, f => f.Random.Int(1, 4))
        .RuleFor(e => e.EventStatusFullName, f => f.PickRandom("Draft", "Published", "Cancelled", "Completed"))
        // Event Format
        .RuleFor(e => e.EventFormatId, f => f.Random.Int(1, 3))
        .RuleFor(e => e.EventFormatFullName, f => f.PickRandom("In-Person", "Online", "Hybrid"))
        // Visibility
        .RuleFor(e => e.VisibilityTypeId, f => f.Random.Int(1, 3))
        .RuleFor(e => e.VisibilityTypeFullName, f => f.PickRandom("Public", "Private", "Unlisted"))
        // Featured Image
        .RuleFor(e => e.FeaturedImageId, f => Guid.NewGuid())
        .RuleFor(e => e.FeaturedImageUri, f => f.Internet.Url())
        .RuleFor(e => e.ParticipationConfiguration, _ => new EventParticipationConfigurationDto
        {
            ParticipationHandlingModeId = 1,
            AdvanceRegistrationObligationId = 1
        })
        // Session info
        .RuleFor(e => e.SessionCount, f => f.Random.Int(1, 10))
        // Madhab (optional)
        .RuleFor(e => e.MadhabId, f => f.Random.Bool() ? f.Random.Int(1, 4) : null)
        .RuleFor(e => e.MadhabFullName, f => f.PickRandom("Hanafi", "Maliki", "Shafi'i", "Hanbali", null))
        // Tenant
        .RuleFor(e => e.TenantId, f => Guid.Parse("018e4e5c-7f00-7000-8000-000000000001"))
        // Timezone
        .RuleFor(e => e.Timezone, f => f.PickRandom("UTC", "Europe/London", "America/New_York", "Asia/Dubai"));

    /// <summary>
    /// Generates fake EventDto for detail views.
    /// Property names match EventApiTagClients.g.cs
    /// </summary>
    public static Faker<EventDto> EventDto => new Faker<EventDto>()
        .RuleFor(e => e.Id, f => Guid.NewGuid())
        .RuleFor(e => e.Title, f => f.Lorem.Sentence(3, 5))
        .RuleFor(e => e.Subtitle, f => f.Lorem.Sentence(5, 10))
        .RuleFor(e => e.Description, f => f.Lorem.Paragraphs(2))
        .RuleFor(e => e.Slug, f => f.Lorem.Slug(3))
        .RuleFor(e => e.TotalViews, f => f.Random.Int(0, 10000))
        .RuleFor(e => e.ParticipationConfiguration, _ => new ParticipationConfiguration
        {
            ParticipationHandlingModeId = 1,
            AdvanceRegistrationObligationId = 1
        })
        // DateTimeOffset? for dates (verified from API)
        .RuleFor(e => e.FirstSessionDate, f => f.Date.FutureOffset(1))
        .RuleFor(e => e.LastSessionDate, (f, e) => e.FirstSessionDate?.AddDays(f.Random.Int(0, 7)))
        // Event Type
        .RuleFor(e => e.EventTypeId, f => f.Random.Int(1, 5))
        .RuleFor(e => e.EventTypeFullName, f => f.PickRandom("Conference", "Workshop", "Webinar"))
        .RuleFor(e => e.EventTypeMasterCode, f => f.Random.AlphaNumeric(5).ToUpper())
        // Audience Gender
        .RuleFor(e => e.AudienceGenderId, f => f.Random.Int(1, 4))
        .RuleFor(e => e.AudienceGenderFullName, f => f.PickRandom("Mixed", "Men Only", "Women Only"))
        .RuleFor(e => e.AudienceGenderMasterCode, f => f.Random.AlphaNumeric(5).ToUpper())
        // Audience Age
        .RuleFor(e => e.AudienceAgeId, f => f.Random.Int(1, 5))
        .RuleFor(e => e.AudienceAgeFullName, f => f.PickRandom("All Ages", "Adults", "Youth"))
        .RuleFor(e => e.AudienceAgeMasterCode, f => f.Random.AlphaNumeric(5).ToUpper())
        .RuleFor(e => e.AudienceAgeMinAge, f => f.Random.Int(0, 12))
        .RuleFor(e => e.AudienceAgeMaxAge, f => f.Random.Int(18, 99))
        // Actor
        .RuleFor(e => e.ActorId, f => Guid.NewGuid())
        .RuleFor(e => e.ActorDisplayName, f => f.Company.CompanyName());

    /// <summary>
    /// Generates fake CreateEventDraftRequestDto for form testing.
    /// </summary>
    public static Faker<CreateEventDraftRequestDto> CreateEventDraftDto => new Faker<CreateEventDraftRequestDto>()
        .RuleFor(e => e.Title, f => f.Lorem.Sentence(3, 5))
        .RuleFor(e => e.Subtitle, f => f.Lorem.Sentence(5, 10))
        .RuleFor(e => e.Description, f => f.Lorem.Paragraph())
        .RuleFor(e => e.EventTypeId, f => f.Random.Int(1, 5))
        .RuleFor(e => e.AudienceGenderId, f => f.Random.Int(1, 4))
        .RuleFor(e => e.AudienceAgeId, f => f.Random.Int(1, 5))
        .RuleFor(e => e.VisibilityTypeId, f => f.Random.Int(1, 3))
        .RuleFor(e => e.EventFormatId, f => f.Random.Int(1, 3))
        .RuleFor(e => e.ParticipationConfiguration, _ => new ConfigureEventParticipationDto
        {
            ParticipationHandlingModeId = 1,
            AdvanceRegistrationObligationId = 1
        });

    public static Faker<CreateEventDraftRequestDto> CreateEventRequest => CreateEventDraftDto;

    #endregion

    #region Organization DTOs

    /// <summary>
    /// Generates fake OrganizationListDto for list displays.
    /// Property names match EventApiTagClients.g.cs
    /// Postcode is string in OrganizationListDto (verified)
    /// </summary>
    public static Faker<OrganizationListDto> OrganizationListDto => new Faker<OrganizationListDto>()
        .RuleFor(o => o.Id, f => Guid.NewGuid())
        .RuleFor(o => o.FullName, f => f.Company.CompanyName())
        .RuleFor(o => o.Email, f => f.Internet.Email())
        .RuleFor(o => o.Country, f => f.Address.Country())
        .RuleFor(o => o.City, f => f.Address.City())
        .RuleFor(o => o.Address, f => f.Address.StreetAddress())
        // Postcode is string in OrganizationListDto (verified from API line 25806)
        .RuleFor(o => o.Postcode, f => f.Address.ZipCode())
        .RuleFor(o => o.WebsiteUrl, f => f.Internet.Url())
        .RuleFor(o => o.ApprovalStatusId, f => f.Random.Int(1, 4))
        .RuleFor(o => o.ApprovalStatusFullName, f => f.PickRandom("Pending", "Approved", "Rejected", "Suspended"));

    /// <summary>
    /// Generates fake OrganizationDto for detail views.
    /// Property names match EventApiTagClients.g.cs
    /// Postcode is string in OrganizationDto (verified)
    /// </summary>
    public static Faker<OrganizationDto> OrganizationDto => new Faker<OrganizationDto>()
        .RuleFor(o => o.Id, f => Guid.NewGuid())
        .RuleFor(o => o.FullName, f => f.Company.CompanyName())
        .RuleFor(o => o.Email, f => f.Internet.Email())
        .RuleFor(o => o.Country, f => f.Address.Country())
        .RuleFor(o => o.City, f => f.Address.City())
        .RuleFor(o => o.Address, f => f.Address.StreetAddress())
        // Postcode is string in OrganizationDto (verified from API line 25715)
        .RuleFor(o => o.Postcode, f => f.Address.ZipCode())
        .RuleFor(o => o.WebsiteUrl, f => f.Internet.Url())
        .RuleFor(o => o.ApprovalStatusId, f => f.Random.Int(1, 4))
        .RuleFor(o => o.ApprovalStatusFullName, f => f.PickRandom("Pending", "Approved", "Rejected", "Suspended"));

    /// <summary>
    /// Generates fake CreateOrganizationDto for form testing.
    /// Property names match EventApiTagClients.g.cs
    /// IMPORTANT: Postcode is int in CreateOrganizationDto (verified from API line 24063)
    /// </summary>
    public static Faker<CreateOrganizationDto> CreateOrganizationDto => new Faker<CreateOrganizationDto>()
        .RuleFor(o => o.FullName, f => f.Company.CompanyName())
        .RuleFor(o => o.Email, f => f.Internet.Email())
        .RuleFor(o => o.Country, f => f.Address.Country())
        .RuleFor(o => o.City, f => f.Address.City())
        .RuleFor(o => o.Address, f => f.Address.StreetAddress())
        // Postcode is int in CreateOrganizationDto (verified from API line 24063)
        .RuleFor(o => o.Postcode, f => f.Random.Int(10000, 99999))
        .RuleFor(o => o.WebsiteUrl, f => f.Internet.Url());

    #endregion

    #region Lookup Table DTOs

    /// <summary>
    /// Generates fake CategoryListDto for category dropdowns.
    /// </summary>
    public static Faker<CategoryListDto> CategoryListDto => new Faker<CategoryListDto>()
        .RuleFor(c => c.Id, f => Guid.NewGuid())
        .RuleFor(c => c.FullName, f => f.Commerce.Categories(1)[0])
        .RuleFor(c => c.MasterCode, f => f.Random.AlphaNumeric(10).ToUpper());

    /// <summary>
    /// Generates fake TagListDto for tag selections.
    /// </summary>
    public static Faker<TagListDto> TagListDto => new Faker<TagListDto>()
        .RuleFor(t => t.Id, f => Guid.NewGuid())
        .RuleFor(t => t.FullName, f => f.Lorem.Word())
        .RuleFor(t => t.MasterCode, f => f.Random.AlphaNumeric(10).ToUpper());

    /// <summary>
    /// Generates fake LocationListDto for location dropdowns.
    /// Property names match EventApiTagClients.g.cs
    /// NOTE: LocationListDto does NOT have Postcode, Latitude, or Longitude properties
    /// </summary>
    public static Faker<LocationListDto> LocationListDto => new Faker<LocationListDto>()
        .RuleFor(l => l.Id, f => Guid.NewGuid())
        .RuleFor(l => l.FullName, f => f.Company.CompanyName())
        .RuleFor(l => l.Address, f => f.Address.StreetAddress())
        .RuleFor(l => l.City, f => f.Address.City())
        .RuleFor(l => l.Country, f => f.Address.Country())
        .RuleFor(l => l.Timezone, f => f.PickRandom("UTC", "Europe/London", "America/New_York"));

    /// <summary>
    /// Generates fake AudienceAgeListDto for age dropdown.
    /// </summary>
    public static Faker<AudienceAgeListDto> AudienceAgeListDto => new Faker<AudienceAgeListDto>()
        .RuleFor(a => a.Id, f => f.IndexFaker + 1)
        .RuleFor(a => a.FullName, f => f.PickRandom("Children", "Youth", "Adults", "Seniors", "All Ages"))
        .RuleFor(a => a.MasterCode, f => f.Random.AlphaNumeric(5).ToUpper());

    /// <summary>
    /// Generates fake AudienceGenderListDto for gender dropdown.
    /// </summary>
    public static Faker<AudienceGenderListDto> AudienceGenderListDto => new Faker<AudienceGenderListDto>()
        .RuleFor(a => a.Id, f => f.IndexFaker + 1)
        .RuleFor(a => a.FullName, f => f.PickRandom("Mixed", "Men Only", "Women Only", "Family"))
        .RuleFor(a => a.MasterCode, f => f.Random.AlphaNumeric(5).ToUpper());

    /// <summary>
    /// Generates fake EventTypeListDto for event type dropdown.
    /// </summary>
    public static Faker<EventTypeListDto> EventTypeListDto => new Faker<EventTypeListDto>()
        .RuleFor(e => e.Id, f => f.IndexFaker + 1)
        .RuleFor(e => e.FullName, f => f.PickRandom("Conference", "Workshop", "Webinar", "Seminar", "Lecture"))
        .RuleFor(e => e.MasterCode, f => f.Random.AlphaNumeric(5).ToUpper());

    /// <summary>
    /// Generates fake EventFormatListDto for format dropdown.
    /// </summary>
    public static Faker<EventFormatListDto> EventFormatListDto => new Faker<EventFormatListDto>()
        .RuleFor(e => e.Id, f => f.IndexFaker + 1)
        .RuleFor(e => e.FullName, f => f.PickRandom("In-Person", "Online", "Hybrid"))
        .RuleFor(e => e.MasterCode, f => f.Random.AlphaNumeric(5).ToUpper());

    /// <summary>
    /// Generates fake EventStatusListDto for status dropdown.
    /// </summary>
    public static Faker<EventStatusListDto> EventStatusListDto => new Faker<EventStatusListDto>()
        .RuleFor(e => e.Id, f => f.IndexFaker + 1)
        .RuleFor(e => e.FullName, f => f.PickRandom("Draft", "Published", "Cancelled", "Completed"))
        .RuleFor(e => e.MasterCode, f => f.Random.AlphaNumeric(5).ToUpper());

    /// <summary>
    /// Generates fake LanguageListDto for language dropdown.
    /// </summary>
    public static Faker<LanguageListDto> LanguageListDto => new Faker<LanguageListDto>()
        .RuleFor(l => l.Id, f => f.IndexFaker + 1)
        .RuleFor(l => l.FullName, f => f.PickRandom("English", "Arabic", "French", "German", "Turkish"))
        .RuleFor(l => l.MasterCode, f => f.PickRandom("EN", "AR", "FR", "DE", "TR"));

    /// <summary>
    /// Generates fake MadhabListDto for madhab dropdown.
    /// </summary>
    public static Faker<MadhabListDto> MadhabListDto => new Faker<MadhabListDto>()
        .RuleFor(m => m.Id, f => f.IndexFaker + 1)
        .RuleFor(m => m.FullName, f => f.PickRandom("Hanafi", "Maliki", "Shafi'i", "Hanbali"))
        .RuleFor(m => m.MasterCode, f => f.Random.AlphaNumeric(5).ToUpper());

    #endregion

    #region User & Session DTOs

    /// <summary>
    /// Generates fake UserDto for user displays.
    /// </summary>
    public static Faker<UserDto> UserDto => new Faker<UserDto>()
        .RuleFor(u => u.Id, f => Guid.NewGuid())
        .RuleFor(u => u.Email, f => f.Internet.Email())
        .RuleFor(u => u.FirstName, f => f.Name.FirstName())
        .RuleFor(u => u.LastName, f => f.Name.LastName())
        .RuleFor(u => u.EmailVerified, f => f.Random.Bool());

    /// <summary>
    /// Generates fake EventSessionListDto for session lists.
    /// Property names match EventApiTagClients.g.cs
    /// NOTE: EventSessionListDto does NOT have a Description property
    /// </summary>
    public static Faker<EventSessionListDto> EventSessionListDto => new Faker<EventSessionListDto>()
        .RuleFor(s => s.Id, f => Guid.NewGuid())
        .RuleFor(s => s.EventId, f => Guid.NewGuid())
        .RuleFor(s => s.EventTitle, f => f.Lorem.Sentence(2, 4))
        .RuleFor(s => s.Title, f => f.Lorem.Sentence(2, 4))
        .RuleFor(s => s.Slug, f => f.Lorem.Slug(2))
        .RuleFor(s => s.StartTime, f => f.Date.FutureOffset(1))
        .RuleFor(s => s.EndTime, (f, s) => s.StartTime?.AddHours(f.Random.Int(1, 4)))
        .RuleFor(s => s.LocationId, f => f.Random.Bool() ? Guid.NewGuid() : null)
        .RuleFor(s => s.LocationFullName, f => f.Company.CompanyName())
        .RuleFor(s => s.LocationCity, f => f.Address.City())
        .RuleFor(s => s.MaxAudienceAttendees, f => f.Random.Int(10, 500))
        .RuleFor(s => s.CurrentAudienceAttendees, f => f.Random.Int(0, 100));

    #endregion

    #region Response DTOs

    /// <summary>
    /// Generates a successful BaseCommandResponseOfGuid.
    /// </summary>
    /// <param name="id">Optional specific ID to return</param>
    public static BaseCommandResponseOfGuid SuccessResponse(Guid? id = null) => new()
    {
        Success = true,
        Id = id ?? Guid.NewGuid(),
        Message = "Operation completed successfully."
    };

    /// <summary>
    /// Generates a failed BaseCommandResponseOfGuid with errors.
    /// </summary>
    /// <param name="errors">Error messages to include</param>
    public static BaseCommandResponseOfGuid FailureResponse(params string[] errors) => new()
    {
        Success = false,
        Message = "Operation failed.",
        Errors = errors.ToList()
    };

    /// <summary>
    /// Generates a validation failure response.
    /// </summary>
    /// <param name="fieldName">Field that failed validation</param>
    /// <param name="message">Validation error message</param>
    public static BaseCommandResponseOfGuid ValidationFailureResponse(string fieldName, string message) => new()
    {
        Success = false,
        Message = "Validation failed.",
        Errors = new List<string> { $"{fieldName}: {message}" }
    };

    #endregion
}

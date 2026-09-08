using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json.Serialization;
using Explore.Application.DTOs.Category;
using Explore.Application.DTOs.EventAspects;
using Explore.Application.DTOs.Tag;

namespace Explore.Application.DTOs.Event;

public sealed record EventDto
{
    public Guid Id { get; init; }
    public Guid ConcurrencyStamp { get; init; }
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public string? Description { get; init; }
    public string? Content { get; init; }
    public string? Slug { get; init; }
    public string? PublicCode { get; init; }

    // Event Type
    public int? EventTypeId { get; init; }
    public string? EventTypeFullName { get; init; }
    public string? EventTypeMasterCode { get; init; }

    // Audience
    public int? AudienceGenderId { get; init; }
    public string? AudienceGenderFullName { get; init; }
    public string? AudienceGenderMasterCode { get; init; }
    public int? AudienceAgeId { get; init; }
    public string? AudienceAgeFullName { get; init; }
    public string? AudienceAgeMasterCode { get; init; }
    public int? AudienceAgeMinAge { get; init; }
    public int? AudienceAgeMaxAge { get; init; }

    // Actor (Owner - User or Organization)
    public Guid ActorId { get; init; }
    public required string ActorDisplayName { get; init; }
    public string? ActorHandle { get; init; }
    public string? ActorDid { get; init; }
    public int ActorTypeId { get; init; }
    public required string ActorTypeFullName { get; init; }
    public Guid? ActorUserId { get; init; }
    public Guid? ActorOrganizationId { get; init; }
    public Guid? ActorGroupId { get; init; }
    public Guid? ActorProfilePictureId { get; init; }
    public string? ActorProfilePictureUri { get; set; }

    public int ProvenanceTypeId { get; init; }
    public string? ProvenanceTypeCode { get; init; }
    public string? ProvenanceTypeName { get; init; }
    public Guid? SubmittedByUserId { get; init; }
    public Guid? OrganizerActorId { get; init; }
    [JsonIgnore]
    public Guid? OrganizerActorUserId { get; init; }
    [JsonIgnore]
    public Guid? OrganizerActorOrganizationId { get; init; }
    [JsonIgnore]
    public Guid? OrganizerActorGroupId { get; init; }
    [JsonIgnore]
    public bool IsPubliclyEligible { get; set; }
    [JsonIgnore]
    public bool IsManagementView { get; set; }
    [JsonIgnore]
    public bool IsReportingIntakeEnabled { get; init; }
    public string? SourcePublisherName { get; init; }
    private IReadOnlyList<EventPublicActionDto>? _publicActions = ImmutableArray<EventPublicActionDto>.Empty;

    public IReadOnlyList<EventPublicActionDto> PublicActions
    {
        get => _publicActions!;
        init => _publicActions = value?.ToImmutableArray();
    }

    public EventTicketPriceSummaryDto? TicketPriceSummary { get; init; }

    // Featured Image
    public Guid FeaturedImageId { get; init; }
    public string? FeaturedImageUri { get; set; }

    public Explore.Application.DTOs.PublicExperience.VisitorAccessCapabilityDto? VisitorAccess { get; init; }
    public EventParticipationConfigurationDto? ParticipationConfiguration { get; set; }
    public int? RegistrationPolicyId { get; init; }
    public string? RegistrationPolicyFullName { get; init; }
    public string? RegistrationPolicyMasterCode { get; init; }

    // Status & Visibility
    public int EventStatusId { get; init; }
    public required string EventStatusFullName { get; init; }
    public required string EventStatusMasterCode { get; init; }
    public bool IsUnmoderationEligible { get; set; }
    public int VisibilityTypeId { get; init; }
    public required string VisibilityTypeFullName { get; init; }
    public required string VisibilityTypeMasterCode { get; init; }

    // Format
    public int EventFormatId { get; init; }
    public required string EventFormatFullName { get; init; }
    public required string EventFormatMasterCode { get; init; }

    // Islamic Context
    public int? MadhabId { get; init; }
    public string? MadhabFullName { get; init; }
    public string? MadhabMasterCode { get; init; }

    // Session Info
    public int? SessionCount { get; init; }
    public DateOnly? FirstSessionDate { get; init; }
    public DateOnly? LastSessionDate { get; init; }
    public string? Timezone { get; init; }

    // Metadata
    public int TotalViews { get; init; }

    // ATProto Federation
    public Guid? AtprotoRecordId { get; init; }
    public string? AtprotoRecordUri { get; init; }
    public string? AtprotoRecordCid { get; init; }

    // ===== Aspects =====
    // List of active aspect types for this event (e.g., ["Islamic", "Tech"])
    private IReadOnlyList<string>? _availableAspects = ImmutableArray<string>.Empty;

    public IReadOnlyList<string> AvailableAspects
    {
        get => _availableAspects!;
        init => _availableAspects = value?.ToImmutableArray();
    }

    // Islamic Aspect (only populated if event has Islamic characteristics)
    public EventAspects.EventIslamicAspectDto? IslamicAspect { get; init; }

    // Tech Aspect (only populated if event has Tech characteristics)
    public EventAspects.EventTechAspectDto? TechAspect { get; init; }

    // Appearance
    public string? BackgroundColor { get; init; }
    public string? BackgroundEffect { get; init; }
    public Guid? BackgroundImageId { get; init; }
    public string? BackgroundImageUri { get; init; }

    // Tags & Categories (populated via junction tables)
    private IReadOnlyList<TagListDto>? _tags = ImmutableArray<TagListDto>.Empty;
    private IReadOnlyList<CategoryListDto>? _categories = ImmutableArray<CategoryListDto>.Empty;

    public IReadOnlyList<TagListDto> Tags { get => _tags!; init => _tags = value?.ToImmutableArray(); }
    public IReadOnlyList<CategoryListDto> Categories { get => _categories!; init => _categories = value?.ToImmutableArray(); }

    // Tenant
    public Guid TenantId { get; init; }

    internal EventDto CreateRequestCopy() => (EventDto)MemberwiseClone();
}

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Explore.Application.DTOs.Event;

public sealed record EventListDto
{
    public Guid Id { get; init; }
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public string? Description { get; init; }
    public string? Slug { get; init; }
    public string? PublicCode { get; init; }

    // Event Type
    public int EventTypeId { get; init; }
    public required string EventTypeFullName { get; init; }

    // Audience
    public int AudienceGenderId { get; init; }
    public required string AudienceGenderFullName { get; init; }
    public int AudienceAgeId { get; init; }
    public required string AudienceAgeFullName { get; init; }
    public int? AudienceAgeMinAge { get; init; }
    public int? AudienceAgeMaxAge { get; init; }

    // Actor (Owner - User or Organization)
    public Guid ActorId { get; init; }
    public required string ActorDisplayName { get; init; }
    public int ActorTypeId { get; init; }
    public required string ActorTypeFullName { get; init; }
    public Guid? ActorUserId { get; init; }
    public Guid? ActorOrganizationId { get; init; }
    public Guid? ActorGroupId { get; init; }
    public Guid? ActorProfilePictureId { get; init; }
    public string? ActorProfilePictureUri { get; set; }

    public string? ProvenanceTypeCode { get; init; }

    public EventTicketPriceSummaryDto? TicketPriceSummary { get; init; }

    // Featured Image
    public Guid FeaturedImageId { get; init; }
    public string? FeaturedImageUri { get; set; }

    public EventParticipationConfigurationDto? ParticipationConfiguration { get; init; }
    public int? RegistrationPolicyId { get; init; }
    public string? RegistrationPolicyFullName { get; init; }

    // Status & Visibility
    public int EventStatusId { get; init; }
    public required string EventStatusFullName { get; init; }
    public int VisibilityTypeId { get; init; }
    public required string VisibilityTypeFullName { get; init; }

    // Format
    public int EventFormatId { get; init; }
    public required string EventFormatFullName { get; init; }

    // Islamic Context
    public int? MadhabId { get; init; }
    public string? MadhabFullName { get; init; }

    // Session Info
    public int? SessionCount { get; init; }
    public DateOnly? FirstSessionDate { get; init; }
    public DateOnly? LastSessionDate { get; init; }
    public DateTimeOffset? FirstSessionStartUtc { get; init; }
    public string? Timezone { get; init; }

    // Series
    public string? EventSeriesTitle { get; init; }

    // Temporal
    public bool IsPast { get; init; }

    // Metadata
    public int TotalViews { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }
    public Guid? AtprotoRecordId { get; init; }
    public string? AtprotoDeliveryStatus { get; set; }
    public string? AtprotoDeliveryFailureCode { get; set; }

    [JsonIgnore]
    public bool IsManagementView { get; set; }
    [JsonIgnore]
    public bool IsReportingIntakeEnabled { get; init; }

    // Tenant
    public Guid TenantId { get; init; }
}

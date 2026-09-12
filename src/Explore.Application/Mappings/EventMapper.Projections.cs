using Explore.Application.DTOs.Event;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;
using Riok.Mapperly.Abstractions;

namespace Explore.Application.Mappings;

public static partial class EventMapper
{
    public static EventDto? ToDetail(Event? source)
    {
        if (source is null)
            return null;

        return MapEventDetail(source) with
        {
            FeaturedImageId = source.FeaturedImageId ?? Guid.Empty,
            ActorTypeId = source.Actor?.ActorTypeId ?? 0,
            TicketPriceSummary = EventTicketPriceSummaryMapper.Map(source),
            PublicActions = PublicActions(source),
            AvailableAspects = AvailableAspects(source)
        };
    }

    public static EventListDto ToListItem(Event source) => MapEventList(source) with
    {
        FeaturedImageId = source.FeaturedImageId ?? Guid.Empty,
        EventTypeId = source.EventTypeId ?? 0,
        AudienceGenderId = source.AudienceGenderId ?? 0,
        AudienceAgeId = source.AudienceAgeId ?? 0,
        ActorTypeId = source.Actor?.ActorTypeId ?? 0,
        IsPast = source.LastSessionEndUtc is not null && source.LastSessionEndUtc <= DateTimeOffset.UtcNow,
        TicketPriceSummary = EventTicketPriceSummaryMapper.Map(source)
    };

    // Domain graphs, provenance internals, lifecycle/audit and scheduling caches are not response graphs.
    // The wrapper owns the filtered actions, aspect names and ticket summary. Services own enrichment/flags.
    [MapperIgnoreSource(nameof(Event.SubmittedByUser))]
    [MapperIgnoreSource(nameof(Event.Tenant))]
    [MapperIgnoreSource(nameof(Event.Sessions))]
    [MapperIgnoreSource(nameof(Event.SessionGroups))]
    [MapperIgnoreSource(nameof(Event.AgendaItems))]
    [MapperIgnoreSource(nameof(Event.Days))]
    [MapperIgnoreSource(nameof(Event.ModerationRecords))]
    [MapperIgnoreSource(nameof(Event.OrganizerClaims))]
    [MapperIgnoreSource(nameof(Event.PublicActions))]
    [MapperIgnoreSource(nameof(Event.TicketCatalogVersions))]
    [MapperIgnoreSource(nameof(Event.CapacityPools))]
    [MapperIgnoreSource(nameof(Event.SourceTemplateId))]
    [MapperIgnoreSource(nameof(Event.SourceTemplateKey))]
    [MapperIgnoreSource(nameof(Event.SourceTemplateVersion))]
    [MapperIgnoreSource(nameof(Event.InstantiatedFromTemplateAt))]
    [MapperIgnoreSource(nameof(Event.LastSyncedFromTemplateAt))]
    [MapperIgnoreSource(nameof(Event.FirstSessionStartUtc))]
    [MapperIgnoreSource(nameof(Event.LastSessionStartUtc))]
    [MapperIgnoreSource(nameof(Event.LastSessionEndUtc))]
    [MapperIgnoreSource(nameof(Event.EventTimeZoneId))]
    [MapperIgnoreSource(nameof(Event.ProvenanceSource))]
    [MapperIgnoreSource(nameof(Event.ProvenanceExternalId))]
    [MapperIgnoreSource(nameof(Event.EventSeriesId))]
    [MapperIgnoreSource(nameof(Event.EventSeries))]
    [MapperIgnoreSource(nameof(Event.SeriesOrder))]
    [MapperIgnoreSource(nameof(Event.CreatedAt))]
    [MapperIgnoreSource(nameof(Event.CreatedBy))]
    [MapperIgnoreSource(nameof(Event.UpdatedAt))]
    [MapperIgnoreSource(nameof(Event.UpdatedBy))]
    [MapperIgnoreSource(nameof(Event.IsDeleted))]
    [MapperIgnoreSource(nameof(Event.DeletedAt))]
    [MapperIgnoreSource(nameof(Event.DeletedBy))]
    [MapperIgnoreTarget(nameof(EventDto.IsPubliclyEligible))]
    [MapperIgnoreTarget(nameof(EventDto.IsManagementView))]
    [MapperIgnoreTarget(nameof(EventDto.IsReportingIntakeEnabled))]
    [MapperIgnoreTarget(nameof(EventDto.IsUnmoderationEligible))]
    [MapperIgnoreTarget(nameof(EventDto.VisitorAccess))]
    [MapperIgnoreTarget(nameof(EventDto.Tags))]
    [MapperIgnoreTarget(nameof(EventDto.Categories))]
    [MapperIgnoreTarget(nameof(EventDto.ActorProfilePictureId))]
    [MapperIgnoreTarget(nameof(EventDto.TicketPriceSummary))]
    [MapperIgnoreTarget(nameof(EventDto.PublicActions))]
    [MapperIgnoreTarget(nameof(EventDto.AvailableAspects))]
    [MapperIgnoreSource(nameof(Event.FeaturedImageId))]
    [MapperIgnoreTarget(nameof(EventDto.FeaturedImageId))]
    [MapperIgnoreTarget(nameof(EventDto.ActorTypeId))]
    [MapProperty(nameof(Event.EventProvenanceTypeId), nameof(EventDto.ProvenanceTypeId))]
    [MapProperty(nameof(Event.EventProvenanceType), nameof(EventDto.ProvenanceTypeCode), Use = nameof(ProvenanceCode))]
    [MapProperty(nameof(Event.EventProvenanceType), nameof(EventDto.ProvenanceTypeName), Use = nameof(ProvenanceName))]
    [MapProperty(nameof(Event.EventType), nameof(EventDto.EventTypeFullName), Use = nameof(EventTypeName))]
    [MapProperty(nameof(Event.EventType), nameof(EventDto.EventTypeMasterCode), Use = nameof(EventTypeCode))]
    [MapProperty(nameof(Event.AudienceGender), nameof(EventDto.AudienceGenderFullName), Use = nameof(GenderName))]
    [MapProperty(nameof(Event.AudienceGender), nameof(EventDto.AudienceGenderMasterCode), Use = nameof(GenderCode))]
    [MapProperty(nameof(Event.AudienceAge), nameof(EventDto.AudienceAgeFullName), Use = nameof(AgeName))]
    [MapProperty(nameof(Event.AudienceAge), nameof(EventDto.AudienceAgeMasterCode), Use = nameof(AgeCode))]
    [MapProperty(nameof(Event.AudienceAge), nameof(EventDto.AudienceAgeMinAge), Use = nameof(MinimumAge))]
    [MapProperty(nameof(Event.AudienceAge), nameof(EventDto.AudienceAgeMaxAge), Use = nameof(MaximumAge))]
    [MapProperty(nameof(Event.Actor), nameof(EventDto.ActorDisplayName), Use = nameof(EventActorName))]
    [MapProperty(nameof(Event.Actor), nameof(EventDto.ActorHandle), Use = nameof(EventActorHandle))]
    [MapProperty(nameof(Event.Actor), nameof(EventDto.ActorDid), Use = nameof(EventActorDid))]
    [MapProperty(nameof(Event.Actor), nameof(EventDto.ActorTypeFullName), Use = nameof(EventActorTypeName))]
    [MapProperty(nameof(Event.Actor), nameof(EventDto.ActorUserId), Use = nameof(EventActorUserId))]
    [MapProperty(nameof(Event.Actor), nameof(EventDto.ActorOrganizationId), Use = nameof(EventActorOrganizationId))]
    [MapProperty(nameof(Event.Actor), nameof(EventDto.ActorGroupId), Use = nameof(EventActorGroupId))]
    [MapProperty(nameof(Event.Actor), nameof(EventDto.ActorProfilePictureUri), Use = nameof(EventActorPicture))]
    [MapProperty(nameof(Event.OrganizerActor), nameof(EventDto.OrganizerActorUserId), Use = nameof(EventActorUserId))]
    [MapProperty(nameof(Event.OrganizerActor), nameof(EventDto.OrganizerActorOrganizationId), Use = nameof(EventActorOrganizationId))]
    [MapProperty(nameof(Event.OrganizerActor), nameof(EventDto.OrganizerActorGroupId), Use = nameof(EventActorGroupId))]
    [MapProperty(nameof(Event.FeaturedImage), nameof(EventDto.FeaturedImageUri), Use = nameof(EventImageUri))]
    [MapProperty(nameof(Event.BackgroundImage), nameof(EventDto.BackgroundImageUri), Use = nameof(EventImageUri))]
    [MapProperty(nameof(Event.EventStatus), nameof(EventDto.EventStatusFullName), Use = nameof(EventStatusName))]
    [MapProperty(nameof(Event.EventStatus), nameof(EventDto.EventStatusMasterCode), Use = nameof(EventStatusCode))]
    [MapProperty(nameof(Event.VisibilityType), nameof(EventDto.VisibilityTypeFullName), Use = nameof(VisibilityName))]
    [MapProperty(nameof(Event.VisibilityType), nameof(EventDto.VisibilityTypeMasterCode), Use = nameof(VisibilityCode))]
    [MapProperty(nameof(Event.EventFormat), nameof(EventDto.EventFormatFullName), Use = nameof(FormatName))]
    [MapProperty(nameof(Event.EventFormat), nameof(EventDto.EventFormatMasterCode), Use = nameof(FormatCode))]
    [MapProperty(nameof(Event.Madhab), nameof(EventDto.MadhabFullName), Use = nameof(EventMadhabName))]
    [MapProperty(nameof(Event.Madhab), nameof(EventDto.MadhabMasterCode), Use = nameof(EventMadhabCode))]
    [MapProperty(nameof(Event.AtprotoRecord), nameof(EventDto.AtprotoRecordUri), Use = nameof(EventRecordUri))]
    [MapProperty(nameof(Event.AtprotoRecord), nameof(EventDto.AtprotoRecordCid), Use = nameof(EventRecordCid))]
    [MapProperty(nameof(Event.RegistrationPolicy), nameof(EventDto.RegistrationPolicyFullName), Use = nameof(PolicyName))]
    [MapProperty(nameof(Event.RegistrationPolicy), nameof(EventDto.RegistrationPolicyMasterCode), Use = nameof(PolicyCode))]
    [MapProperty(nameof(Event.ParticipationConfiguration), nameof(EventDto.ParticipationConfiguration), Use = nameof(Participation))]
    [MapProperty(nameof(Event.IslamicAspect), nameof(EventDto.IslamicAspect), Use = nameof(MapIslamicAspect))]
    [MapProperty(nameof(Event.TechAspect), nameof(EventDto.TechAspect), Use = nameof(MapTechAspect))]
    private static partial EventDto MapEventDetail(Event source);

    // List is a scalar summary. It never traverses series children or aspect/session/action graphs.
    [MapperIgnoreSource(nameof(Event.ConcurrencyStamp))]
    [MapperIgnoreSource(nameof(Event.Content))]
    [MapperIgnoreSource(nameof(Event.EventProvenanceTypeId))]
    [MapperIgnoreSource(nameof(Event.SubmittedByUserId))]
    [MapperIgnoreSource(nameof(Event.SubmittedByUser))]
    [MapperIgnoreSource(nameof(Event.OrganizerActorId))]
    [MapperIgnoreSource(nameof(Event.OrganizerActor))]
    [MapperIgnoreSource(nameof(Event.SourcePublisherName))]
    [MapperIgnoreSource(nameof(Event.Tenant))]
    [MapperIgnoreSource(nameof(Event.Sessions))]
    [MapperIgnoreSource(nameof(Event.SessionGroups))]
    [MapperIgnoreSource(nameof(Event.AgendaItems))]
    [MapperIgnoreSource(nameof(Event.Days))]
    [MapperIgnoreSource(nameof(Event.ModerationRecords))]
    [MapperIgnoreSource(nameof(Event.OrganizerClaims))]
    [MapperIgnoreSource(nameof(Event.PublicActions))]
    [MapperIgnoreSource(nameof(Event.TicketCatalogVersions))]
    [MapperIgnoreSource(nameof(Event.CapacityPools))]
    [MapperIgnoreSource(nameof(Event.SourceTemplateId))]
    [MapperIgnoreSource(nameof(Event.SourceTemplateKey))]
    [MapperIgnoreSource(nameof(Event.SourceTemplateVersion))]
    [MapperIgnoreSource(nameof(Event.InstantiatedFromTemplateAt))]
    [MapperIgnoreSource(nameof(Event.LastSyncedFromTemplateAt))]
    [MapperIgnoreSource(nameof(Event.LastSessionStartUtc))]
    [MapperIgnoreSource(nameof(Event.EventTimeZoneId))]
    [MapperIgnoreSource(nameof(Event.ProvenanceSource))]
    [MapperIgnoreSource(nameof(Event.ProvenanceExternalId))]
    [MapperIgnoreSource(nameof(Event.EventSeriesId))]
    [MapperIgnoreSource(nameof(Event.SeriesOrder))]
    [MapperIgnoreSource(nameof(Event.CreatedBy))]
    [MapperIgnoreSource(nameof(Event.UpdatedAt))]
    [MapperIgnoreSource(nameof(Event.UpdatedBy))]
    [MapperIgnoreSource(nameof(Event.IsDeleted))]
    [MapperIgnoreSource(nameof(Event.DeletedAt))]
    [MapperIgnoreSource(nameof(Event.DeletedBy))]
    [MapperIgnoreSource(nameof(Event.IslamicAspect))]
    [MapperIgnoreSource(nameof(Event.TechAspect))]
    [MapperIgnoreSource(nameof(Event.BackgroundColor))]
    [MapperIgnoreSource(nameof(Event.BackgroundEffect))]
    [MapperIgnoreSource(nameof(Event.BackgroundImageId))]
    [MapperIgnoreSource(nameof(Event.BackgroundImage))]
    [MapperIgnoreSource(nameof(Event.AtprotoRecord))]
    [MapperIgnoreTarget(nameof(EventListDto.IsManagementView))]
    [MapperIgnoreTarget(nameof(EventListDto.IsReportingIntakeEnabled))]
    [MapperIgnoreTarget(nameof(EventListDto.AtprotoDeliveryStatus))]
    [MapperIgnoreTarget(nameof(EventListDto.AtprotoDeliveryFailureCode))]
    [MapperIgnoreTarget(nameof(EventListDto.ActorProfilePictureId))]
    [MapperIgnoreTarget(nameof(EventListDto.TicketPriceSummary))]
    [MapperIgnoreSource(nameof(Event.FeaturedImageId))]
    [MapperIgnoreSource(nameof(Event.EventTypeId))]
    [MapperIgnoreSource(nameof(Event.AudienceGenderId))]
    [MapperIgnoreSource(nameof(Event.AudienceAgeId))]
    [MapperIgnoreSource(nameof(Event.LastSessionEndUtc))]
    [MapperIgnoreTarget(nameof(EventListDto.FeaturedImageId))]
    [MapperIgnoreTarget(nameof(EventListDto.EventTypeId))]
    [MapperIgnoreTarget(nameof(EventListDto.AudienceGenderId))]
    [MapperIgnoreTarget(nameof(EventListDto.AudienceAgeId))]
    [MapperIgnoreTarget(nameof(EventListDto.ActorTypeId))]
    [MapperIgnoreTarget(nameof(EventListDto.IsPast))]
    [MapProperty(nameof(Event.EventProvenanceType), nameof(EventListDto.ProvenanceTypeCode), Use = nameof(ProvenanceCode))]
    [MapProperty(nameof(Event.EventType), nameof(EventListDto.EventTypeFullName), Use = nameof(EventTypeName))]
    [MapProperty(nameof(Event.AudienceGender), nameof(EventListDto.AudienceGenderFullName), Use = nameof(GenderName))]
    [MapProperty(nameof(Event.AudienceAge), nameof(EventListDto.AudienceAgeFullName), Use = nameof(AgeName))]
    [MapProperty(nameof(Event.AudienceAge), nameof(EventListDto.AudienceAgeMinAge), Use = nameof(MinimumAge))]
    [MapProperty(nameof(Event.AudienceAge), nameof(EventListDto.AudienceAgeMaxAge), Use = nameof(MaximumAge))]
    [MapProperty(nameof(Event.Actor), nameof(EventListDto.ActorDisplayName), Use = nameof(EventActorName))]
    [MapProperty(nameof(Event.Actor), nameof(EventListDto.ActorTypeFullName), Use = nameof(EventActorTypeName))]
    [MapProperty(nameof(Event.Actor), nameof(EventListDto.ActorUserId), Use = nameof(EventActorUserId))]
    [MapProperty(nameof(Event.Actor), nameof(EventListDto.ActorOrganizationId), Use = nameof(EventActorOrganizationId))]
    [MapProperty(nameof(Event.Actor), nameof(EventListDto.ActorGroupId), Use = nameof(EventActorGroupId))]
    [MapProperty(nameof(Event.Actor), nameof(EventListDto.ActorProfilePictureUri), Use = nameof(EventActorPicture))]
    [MapProperty(nameof(Event.FeaturedImage), nameof(EventListDto.FeaturedImageUri), Use = nameof(EventImageUri))]
    [MapProperty(nameof(Event.EventStatus), nameof(EventListDto.EventStatusFullName), Use = nameof(EventStatusName))]
    [MapProperty(nameof(Event.VisibilityType), nameof(EventListDto.VisibilityTypeFullName), Use = nameof(VisibilityName))]
    [MapProperty(nameof(Event.EventFormat), nameof(EventListDto.EventFormatFullName), Use = nameof(FormatName))]
    [MapProperty(nameof(Event.Madhab), nameof(EventListDto.MadhabFullName), Use = nameof(EventMadhabName))]
    [MapProperty(nameof(Event.EventSeries), nameof(EventListDto.EventSeriesTitle), Use = nameof(SeriesTitle))]
    [MapProperty(nameof(Event.RegistrationPolicy), nameof(EventListDto.RegistrationPolicyFullName), Use = nameof(PolicyName))]
    [MapProperty(nameof(Event.ParticipationConfiguration), nameof(EventListDto.ParticipationConfiguration), Use = nameof(Participation))]
    [MapProperty(nameof(Event.CreatedAt), nameof(EventListDto.CreatedAtUtc), Use = nameof(CreatedAtUtc))]
    private static partial EventListDto MapEventList(Event source);

    public static EventPublicActionDto ToDetail(EventPublicAction source) => MapPublicAction(source) with
    {
        EventActorId = source.Event?.ActorId ?? Guid.Empty,
        EventActorUserId = source.Event?.Actor?.UserId,
        EventActorOrganizationId = source.Event?.Actor?.OrganizationId,
        EventActorGroupId = source.Event?.Actor?.GroupId,
        EventProvenanceTypeId = source.Event?.EventProvenanceTypeId ?? 0,
        EventProvenanceTypeCode = source.Event?.EventProvenanceType?.MasterCode,
        EventOrganizerActorId = source.Event?.OrganizerActorId,
        EventSubmittedByUserId = source.Event?.SubmittedByUserId
    };

    // Action authority is a bounded set of hidden scalars, not a recursive event projection.
    [MapperIgnoreSource(nameof(EventPublicAction.Event))]
    [MapperIgnoreSource(nameof(EventPublicAction.Tenant))]
    [MapperIgnoreSource(nameof(EventPublicAction.CreatedAt))]
    [MapperIgnoreSource(nameof(EventPublicAction.CreatedBy))]
    [MapperIgnoreSource(nameof(EventPublicAction.UpdatedAt))]
    [MapperIgnoreSource(nameof(EventPublicAction.UpdatedBy))]
    [MapperIgnoreSource(nameof(EventPublicAction.IsDeleted))]
    [MapperIgnoreSource(nameof(EventPublicAction.DeletedAt))]
    [MapperIgnoreSource(nameof(EventPublicAction.DeletedBy))]
    [MapperIgnoreTarget(nameof(EventPublicActionDto.EventActorId))]
    [MapperIgnoreTarget(nameof(EventPublicActionDto.EventActorUserId))]
    [MapperIgnoreTarget(nameof(EventPublicActionDto.EventActorOrganizationId))]
    [MapperIgnoreTarget(nameof(EventPublicActionDto.EventActorGroupId))]
    [MapperIgnoreTarget(nameof(EventPublicActionDto.EventProvenanceTypeId))]
    [MapperIgnoreTarget(nameof(EventPublicActionDto.EventProvenanceTypeCode))]
    [MapperIgnoreTarget(nameof(EventPublicActionDto.EventOrganizerActorId))]
    [MapperIgnoreTarget(nameof(EventPublicActionDto.EventSubmittedByUserId))]
    [MapProperty(nameof(EventPublicAction.EventPublicActionKindId), nameof(EventPublicActionDto.KindId))]
    [MapProperty("EventPublicActionKind.MasterCode", nameof(EventPublicActionDto.KindCode))]
    [MapProperty("EventPublicActionKind.FullName", nameof(EventPublicActionDto.KindName))]
    [MapProperty("HealthState.MasterCode", nameof(EventPublicActionDto.HealthStateCode))]
    [MapProperty("HealthState.FullName", nameof(EventPublicActionDto.HealthStateName))]
    private static partial EventPublicActionDto MapPublicAction(EventPublicAction source);

    private static EventParticipationConfigurationDto? Participation(EventParticipationConfiguration? source) => source is null
        ? null
        : MapParticipation(source) with
        {
            HasValidOptionalQuestionnaire = source.RequirementAttachments.Any(attachment =>
                attachment.IsStandaloneQuestionnaire
                && attachment.RegistrationRequirement != null
                && !attachment.RegistrationRequirement.IsDeleted
                && attachment.RegistrationRequirement.CompletionEffectId == (int)RegistrationRequirementCompletionEffectEnum.NoRegistrationEffect
                && attachment.RegistrationFormVersion != null
                && !attachment.RegistrationFormVersion.IsDeleted
                && attachment.RegistrationFormVersion.StatusId == (int)RegistrationFormStatusEnum.Published
                && !string.IsNullOrWhiteSpace(attachment.RegistrationFormVersion.SchemaHash)
                && !string.IsNullOrWhiteSpace(attachment.RegistrationFormVersion.DataSchemaArtifact)
                && !string.IsNullOrWhiteSpace(attachment.RegistrationFormVersion.UiSchemaArtifact)
                && !string.IsNullOrWhiteSpace(attachment.RegistrationFormVersion.LogicSchemaArtifact)
                && !string.IsNullOrWhiteSpace(attachment.RegistrationFormVersion.MappingArtifact))
        };

    // Requirement graphs are evaluated above, never serialized; tenant and audit remain internal.
    [MapperIgnoreSource(nameof(EventParticipationConfiguration.Event))]
    [MapperIgnoreSource(nameof(EventParticipationConfiguration.TenantId))]
    [MapperIgnoreSource(nameof(EventParticipationConfiguration.Tenant))]
    [MapperIgnoreSource(nameof(EventParticipationConfiguration.RequirementAttachments))]
    [MapperIgnoreSource(nameof(EventParticipationConfiguration.CreatedAt))]
    [MapperIgnoreSource(nameof(EventParticipationConfiguration.CreatedBy))]
    [MapperIgnoreSource(nameof(EventParticipationConfiguration.UpdatedAt))]
    [MapperIgnoreSource(nameof(EventParticipationConfiguration.UpdatedBy))]
    [MapperIgnoreSource(nameof(EventParticipationConfiguration.IsDeleted))]
    [MapperIgnoreSource(nameof(EventParticipationConfiguration.DeletedAt))]
    [MapperIgnoreSource(nameof(EventParticipationConfiguration.DeletedBy))]
    [MapperIgnoreTarget(nameof(EventParticipationConfigurationDto.HasValidOptionalQuestionnaire))]
    [MapProperty(nameof(EventParticipationConfiguration.Id), nameof(EventParticipationConfigurationDto.EventId))]
    [MapProperty("ParticipationHandlingMode.MasterCode", nameof(EventParticipationConfigurationDto.ParticipationHandlingModeCode))]
    [MapProperty("ParticipationHandlingMode.FullName", nameof(EventParticipationConfigurationDto.ParticipationHandlingModeName))]
    [MapProperty("AdvanceRegistrationObligation.MasterCode", nameof(EventParticipationConfigurationDto.AdvanceRegistrationObligationCode))]
    [MapProperty("AdvanceRegistrationObligation.FullName", nameof(EventParticipationConfigurationDto.AdvanceRegistrationObligationName))]
    [MapProperty("IdentityAccessMode.MasterCode", nameof(EventParticipationConfigurationDto.IdentityAccessModeCode))]
    [MapProperty("IdentityAccessMode.FullName", nameof(EventParticipationConfigurationDto.IdentityAccessModeName))]
    private static partial EventParticipationConfigurationDto MapParticipation(EventParticipationConfiguration source);

    private static List<EventPublicActionDto> PublicActions(Event source) => source.PublicActions
        .Where(action => action.HealthStateId == (int)EventPublicActionHealthStateEnum.Active
            && source.ParticipationConfiguration != null
            && EventAuthorityRules.IsPublicActionAllowed(source.ParticipationConfiguration.ParticipationHandlingModeId, action.EventPublicActionKindId))
        .OrderBy(action => action.SortOrder).ThenBy(action => action.Id).Select(ToDetail).ToList();

    private static List<string> AvailableAspects(Event source)
    {
        var aspects = new List<string>();
        if (source.IslamicAspect is not null) aspects.Add("Islamic");
        if (source.TechAspect is not null) aspects.Add("Tech");
        return aspects;
    }

    // Missing lookup/PII navigations preserve absent labels rather than inventing display text.
    private static string? EventTypeName(EventType? source) => source?.FullName;
    private static string? EventTypeCode(EventType? source) => source?.MasterCode;
    private static string? GenderName(AudienceGender? source) => source?.FullName;
    private static string? GenderCode(AudienceGender? source) => source?.MasterCode;
    private static string? AgeName(AudienceAge? source) => source?.FullName;
    private static string? AgeCode(AudienceAge? source) => source?.MasterCode;
    private static int? MinimumAge(AudienceAge? source) => source?.MinAge;
    private static int? MaximumAge(AudienceAge? source) => source?.MaxAge;
    private static string? EventActorName(Actor? source) => source?.Pii?.DisplayName;
    private static string? EventActorPicture(Actor? source) => source?.Pii?.ProfilePictureUri;
    private static string? EventActorHandle(Actor? source) => source?.AtprotoIdentities.Select(identity => identity.Handle).FirstOrDefault();
    private static string? EventActorDid(Actor? source) => source?.AtprotoIdentities.Select(identity => identity.Did).FirstOrDefault();
    private static string? EventActorTypeName(Actor? source) => source?.ActorType?.FullName;
    private static Guid? EventActorUserId(Actor? source) => source?.UserId;
    private static Guid? EventActorOrganizationId(Actor? source) => source?.OrganizationId;
    private static Guid? EventActorGroupId(Actor? source) => source?.GroupId;
    private static string? ProvenanceName(EventProvenanceType? source) => source?.FullName;
    private static string? ProvenanceCode(EventProvenanceType? source) => source?.MasterCode;
    private static string? EventImageUri(StorageObject? source) => source?.Uri;
    private static string? EventStatusName(EventStatus? source) => source?.FullName;
    private static string? EventStatusCode(EventStatus? source) => source?.MasterCode;
    private static string? VisibilityName(VisibilityType? source) => source?.FullName;
    private static string? VisibilityCode(VisibilityType? source) => source?.MasterCode;
    private static string? FormatName(EventFormat? source) => source?.FullName;
    private static string? FormatCode(EventFormat? source) => source?.MasterCode;
    private static string? EventMadhabName(Madhab? source) => source?.FullName;
    private static string? EventMadhabCode(Madhab? source) => source?.MasterCode;
    private static string? EventRecordUri(AtprotoRecord? source) => source?.Uri;
    private static string? EventRecordCid(AtprotoRecord? source) => source?.Cid;
    private static string? PolicyName(EventRegistrationPolicy? source) => source?.FullName;
    private static string? PolicyCode(EventRegistrationPolicy? source) => source?.MasterCode;
    private static string? SeriesTitle(EventSeries? source) => source?.Title;
    private static DateTimeOffset CreatedAtUtc(DateTime source) => new(DateTime.SpecifyKind(source, DateTimeKind.Utc));
}

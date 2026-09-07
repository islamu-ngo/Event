using System.Collections.Immutable;

namespace Explore.Application.Features.Federation.Atproto.Services;

public enum AtprotoSourceFieldDisposition
{
    Native = 1,
    Description = 2,
    Excluded = 3
}

public sealed record AtprotoSourceFieldManifestEntry(
    string SourcePath,
    AtprotoSourceFieldDisposition Disposition,
    string Reason);

public static class AtprotoEventSourceFieldManifest
{
    public static ImmutableArray<AtprotoSourceFieldManifestEntry> Entries { get; } =
    [
        Native("Event.Title", "community event name"),
        Native("Event.Description", "single rendered community event description"),
        Native("Event.CreatedAt", "community event createdAt"),
        Excluded("Event.FirstSessionStartUtc", "cached schedule rollup; native time derives from eligible sessions"),
        Description("Event.LastSessionStartUtc"),
        Excluded("Event.LastSessionEndUtc", "cached schedule rollup; native time derives from eligible sessions"),
        Native("Event.EventFormat.MasterCode", "community event mode"),
        Native("Event.EventStatus.MasterCode", "community event status"),
        Native("Event.ParticipationConfiguration.AdvanceRegistrationObligationId", "community event rsvpExpected when required"),
        Native("Event.PublicActions[ExternalRegistration.Active].Url", "community event registration URI"),

        Description("Event.Subtitle"),
        Description("Event.Content"),
        Description("Event.EventType.MasterCode"),
        Description("Event.EventType.FullName"),
        Description("Event.EventType.Description"),
        Description("Event.AudienceGender.MasterCode"),
        Description("Event.AudienceGender.FullName"),
        Description("Event.AudienceGender.Description"),
        Description("Event.AudienceAge.MasterCode"),
        Description("Event.AudienceAge.FullName"),
        Description("Event.AudienceAge.Description"),
        Description("Event.AudienceAge.MinAge"),
        Description("Event.AudienceAge.MaxAge"),
        Description("Event.TotalViews"),
        Description("Event.VisibilityType.MasterCode"),
        Description("Event.VisibilityType.FullName"),
        Description("Event.VisibilityType.Description"),
        Description("Event.Madhab.MasterCode"),
        Description("Event.Madhab.FullName"),
        Description("Event.Madhab.Description"),
        Description("Event.Slug"),
        Description("Event.PublicCode"),
        Description("Event.FirstSessionDate"),
        Description("Event.LastSessionDate"),
        Description("Event.Timezone"),
        Description("Event.EventTimeZoneId"),
        Description("Event.SessionCount"),
        Description("Event.RegistrationPolicy.MasterCode"),
        Description("Event.RegistrationPolicy.FullName"),
        Description("Event.RegistrationPolicy.Description"),
        Description("Event.EventFormat.FullName"),
        Description("Event.EventFormat.Description"),
        Description("Event.EventStatus.FullName"),
        Description("Event.EventStatus.Description"),
        Description("Event.SeriesOrder"),
        Description("Event.BackgroundColor"),
        Description("Event.BackgroundEffect"),
        Excluded("Event.EventProvenanceTypeId", "internal ingestion provenance discriminator, never public payload"),
        Excluded("Event.OrganizerActorId", "internal organizer attribution foreign key; public organizer data comes from Event.Actor"),
        Excluded("Event.SourcePublisherName", "unverified source attribution metadata, never public payload"),
        Excluded("Event.SubmittedByUserId", "private submitting-user identifier, never public payload"),

        Description("Event.Actor.ActorType.MasterCode"),
        Description("Event.Actor.ActorType.FullName"),
        Description("Event.Actor.ActorType.Description"),
        Description("Event.Actor.Pii.DisplayName"),
        Description("Event.Actor.AtprotoIdentity.Handle"),
        Description("Event.Actor.Description"),
        Description("Event.Actor.BackgroundColor"),
        Description("Event.Actor.BackgroundEffect"),
        Description("Event.Actor.BannerColor"),
        Excluded("Event.Actor.ExternalActorSubjectId", "internal external-subject linkage, never public payload"),
        Excluded("Event.Actor.ServicePrincipalId", "internal service-principal linkage, never public payload"),
        Excluded("Event.Actor.IsSuspended", "moderation state is an eligibility gate, never public payload"),
        Excluded("Event.Actor.SuspendedAt", "private moderation timestamp, never public payload"),
        Excluded("Event.Actor.SuspendedBy", "private moderator identifier, never public payload"),
        Excluded("Event.Actor.ModerationReasonCode", "private moderation evidence, never public payload"),
        Description("Event.Actor.Organization.Pii.FullName"),
        Description("Event.Actor.Organization.WebsiteUrl"),
        Description("Event.Actor.Organization.Pii.Country"),
        Description("Event.Actor.Organization.Pii.City"),
        Description("Event.Actor.Group.FullName"),
        Description("Event.Actor.Group.Description"),

        Description("Event.EventSeries.Title"),
        Description("Event.EventSeries.Description"),
        Description("Event.EventSeries.Slug"),
        Description("Event.EventSeries.IsPublished"),
        Excluded("Event.EventSeries.IsDeleted", "series eligibility gate, never payload"),
        Description("Event.EventSeries.TotalViews"),
        Description("Event.EventSeries.VisibilityType.MasterCode"),
        Description("Event.EventSeries.VisibilityType.FullName"),
        Description("Event.EventSeries.VisibilityType.Description"),
        Description("Event.EventSeries.StartDateUtc"),
        Description("Event.EventSeries.EndDateUtc"),
        Description("Event.EventSeries.Actor.Pii.DisplayName"),
        Description("Event.EventSeries.Actor.AtprotoIdentity.Handle"),

        Description("Event.IslamicAspect.Madhab.MasterCode"),
        Description("Event.IslamicAspect.Madhab.FullName"),
        Description("Event.IslamicAspect.Madhab.Description"),
        Description("Event.IslamicAspect.ReferencePrayer"),
        Description("Event.IslamicAspect.PrayerTimeOffset"),
        Description("Event.IslamicAspect.GenderMode"),
        Description("Event.IslamicAspect.IncludesQuranRecitation"),
        Description("Event.IslamicAspect.PrimaryLanguage.MasterCode"),
        Description("Event.IslamicAspect.PrimaryLanguage.FullName"),
        Description("Event.IslamicAspect.PrimaryLanguage.Description"),
        Description("Event.TechAspect.GithubRepoUrl"),
        Description("Event.TechAspect.HackathonTrack"),
        Description("Event.TechAspect.SkillLevel"),
        Description("Event.TechAspect.TechStackTags"),
        Description("Event.TechAspect.RequiresLaptop"),
        Description("Event.TechAspect.IsCodingCompetition"),
        Description("Event.TechAspect.MaxTeamSize"),
        Description("Event.TechAspect.PrizePool"),
        Description("Event.TechAspect.PrizeCurrencyCode"),

        Description("EventLocationDisclosureResult.State"),
        Description("EventLocationDisclosureResult.Values.Country"),
        Description("EventLocationDisclosureResult.Values.Timezone"),
        Description("EventLocationDisclosureResult.Values.City"),
        Description("EventLocationDisclosureResult.Values.VenueName"),
        Description("EventLocationDisclosureResult.Values.RoomName"),
        Description("EventLocationDisclosureResult.Values.RoomDescription"),
        Description("EventLocationDisclosureResult.Values.StreetAddress"),
        Description("EventLocationDisclosureResult.Values.Postcode"),
        Description("EventLocationDisclosureResult.Values.Latitude"),
        Description("EventLocationDisclosureResult.Values.Longitude"),
        Description("EventLocationDisclosureResult.Values.FormattedAddress"),
        Description("EventLocationDisclosureResult.Values.MapUrl"),
        Description("EventLocationDisclosureResult.Values.Geohash"),

        Description("EventDay.LocalDate"),
        Description("EventDay.Label"),
        Description("EventDay.Description"),
        Description("EventDay.BannerText"),
        Description("EventDay.SortOrder"),
        Description("EventDay.AllowsDayScopeRegistration"),

        Description("EventSession.Title"),
        Description("EventSession.Description"),
        Native("EventSession.StartTime", "eligible sessions derive record startsAt and remain in the program description"),
        Native("EventSession.EndTime", "eligible sessions derive record endsAt and remain in the program description"),
        Description("EventSession.EndTimeType"),
        Description("EventSession.LocalStartDate"),
        Description("EventSession.LocalEndDate"),
        Description("EventSession.LocalStartTime"),
        Description("EventSession.LocalEndTime"),
        Excluded("EventSession.LocalStartMinuteOfDay", "cached local-time projection"),
        Excluded("EventSession.LocalEndMinuteOfDay", "cached local-time projection"),
        Description("EventSession.SortOrder"),
        Description("EventSession.Slug"),
        Description("EventSession.EventSessionKind.MasterCode"),
        Description("EventSession.EventSessionKind.FullName"),
        Description("EventSession.EventSessionKind.Description"),
        Description("EventSession.EventSessionStatus.MasterCode"),
        Description("EventSession.EventSessionStatus.FullName"),
        Description("EventSession.EventSessionStatus.Description"),
        Description("EventSession.MaxAudienceAttendees"),
        Description("EventSession.CurrentAudienceAttendees"),
        Description("EventSession.RegistrationMode.MasterCode"),
        Description("EventSession.RegistrationMode.FullName"),
        Description("EventSession.RegistrationMode.Description"),
        Description("EventSession.Speaker.Actor.Pii.DisplayName"),
        Description("EventSession.Speaker.Actor.AtprotoIdentity.Handle"),
        Description("EventSession.Speaker.Actor.Description"),
        Description("EventSession.Speaker.Actor.BackgroundColor"),
        Description("EventSession.Speaker.Actor.BackgroundEffect"),
        Description("EventSession.Speaker.Actor.BannerColor"),
        Excluded("EventSession.Speaker.Actor.ExternalActorSubjectId", "internal external-subject linkage, never public payload"),
        Excluded("EventSession.Speaker.Actor.ServicePrincipalId", "internal service-principal linkage, never public payload"),
        Excluded("EventSession.Speaker.Actor.IsSuspended", "moderation state is an eligibility gate, never public payload"),
        Excluded("EventSession.Speaker.Actor.SuspendedAt", "private moderation timestamp, never public payload"),
        Excluded("EventSession.Speaker.Actor.SuspendedBy", "private moderator identifier, never public payload"),
        Excluded("EventSession.Speaker.Actor.ModerationReasonCode", "private moderation evidence, never public payload"),
        Description("EventSession.IslamicAspect.StartTimeType"),
        Description("EventSession.IslamicAspect.ReferencePrayer"),
        Description("EventSession.IslamicAspect.OffsetMinutes"),
        Description("EventSession.IslamicAspect.EndReferencePrayer"),
        Description("EventSession.IslamicAspect.EndOffsetMinutes"),
        Description("EventSession.IslamicAspect.RequiresWudu"),
        Description("EventSession.IslamicAspect.RitualRequirementsJson"),

        Description("EventSessionGroup.Name"),
        Description("EventSessionGroup.Description"),
        Description("EventSessionGroup.Slug"),
        Description("EventSessionGroup.Color"),
        Description("EventSessionGroup.SortOrder"),
        Description("EventSessionGroupSession.IsPrimary"),
        Description("EventSessionGroupSession.SortOrder"),
        Description("EventAgendaItem.Title"),
        Description("EventAgendaItem.Description"),
        Description("EventAgendaItem.StartTime"),
        Description("EventAgendaItem.EndTime"),
        Description("EventAgendaItem.LocalStartDate"),
        Description("EventAgendaItem.LocalEndDate"),
        Description("EventAgendaItem.LocalStartTime"),
        Description("EventAgendaItem.LocalEndTime"),
        Description("EventAgendaItem.Kind.MasterCode"),
        Description("EventAgendaItem.Kind.FullName"),
        Description("EventAgendaItem.Kind.Description"),
        Description("EventAgendaItem.SortOrder"),
        Description("EventSessionAgendaItem.Title"),
        Description("EventSessionAgendaItem.Description"),
        Description("EventSessionAgendaItem.StartTime"),
        Description("EventSessionAgendaItem.EndTime"),

        Description("Event.Category.Parent.FullName"),
        Description("Event.Category.FullName"),
        Description("Event.Category.MasterCode"),
        Description("Event.Tag.FullName"),
        Description("Event.Tag.MasterCode"),
        Description("EventSession.Language.FullName"),
        Description("EventSession.Language.MasterCode"),
        Description("EventSession.Language.Description"),
        Description("EventCustomPropertyDefinition.Namespace"),
        Description("EventCustomPropertyDefinition.Key"),
        Description("EventCustomPropertyDefinition.DisplayName"),
        Description("EventCustomPropertyDefinition.Description"),
        Description("EventCustomPropertyDefinition.PropertyType"),
        Description("EventCustomPropertyDefinition.IsRequired"),
        Description("EventCustomPropertyDefinition.IsMulti"),
        Description("EventCustomPropertyDefinition.IsActive"),
        Description("EventCustomPropertyDefinition.SortOrder"),
        Description("EventCustomPropertyDefinition.ExposureLevel"),
        Description("EventCustomPropertyDefinition.IsSearchable"),
        Description("EventCustomPropertyDefinition.IsFilterable"),
        Description("EventCustomPropertyDefinition.IsExportable"),
        Description("EventCustomPropertyDefinition.IsModerationRelevant"),
        Description("EventCustomPropertyDefinition.IsAnalyticsRelevant"),
        Description("EventCustomPropertyDefinition.IsSystemOwned"),
        Description("EventCustomPropertyDefinition.DefaultTextValue"),
        Description("EventCustomPropertyDefinition.DefaultNumberValue"),
        Description("EventCustomPropertyDefinition.DefaultBooleanValue"),
        Description("EventCustomPropertyDefinition.DefaultDateTimeValue"),
        Description("EventCustomPropertyDefinition.DefaultOption.DisplayName"),
        Description("EventCustomPropertyDefinition.MinLength"),
        Description("EventCustomPropertyDefinition.MaxLength"),
        Description("EventCustomPropertyDefinition.RegexPattern"),
        Description("EventCustomPropertyDefinition.MinNumber"),
        Description("EventCustomPropertyDefinition.MaxNumber"),
        Description("EventCustomPropertyDefinition.MinDateTime"),
        Description("EventCustomPropertyDefinition.MaxDateTime"),
        Description("EventCustomPropertyDefinition.AllowedUrlSchemes"),
        Description("EventCustomPropertyOption.Namespace"),
        Description("EventCustomPropertyOption.Key"),
        Description("EventCustomPropertyOption.DisplayName"),
        Description("EventCustomPropertyOption.Description"),
        Description("EventCustomPropertyOption.Value"),
        Description("EventCustomPropertyOption.IsDefault"),
        Description("EventCustomPropertyOption.IsActive"),
        Description("EventCustomPropertyOption.SortOrder"),
        Description("EventCustomPropertyOption.ParentOption.DisplayName"),
        Description("EventCustomPropertyValue.Ordinal"),
        Description("EventCustomPropertyValue.TextValue"),
        Description("EventCustomPropertyValue.NumberValue"),
        Description("EventCustomPropertyValue.BooleanValue"),
        Description("EventCustomPropertyValue.DateTimeValue"),
        Description("EventCustomPropertyValue.Option.DisplayName"),
        Description("EventSessionCustomPropertyDefinition.Namespace"),
        Description("EventSessionCustomPropertyDefinition.Key"),
        Description("EventSessionCustomPropertyDefinition.DisplayName"),
        Description("EventSessionCustomPropertyDefinition.Description"),
        Description("EventSessionCustomPropertyDefinition.PropertyType"),
        Description("EventSessionCustomPropertyDefinition.IsRequired"),
        Description("EventSessionCustomPropertyDefinition.IsMulti"),
        Description("EventSessionCustomPropertyDefinition.IsActive"),
        Description("EventSessionCustomPropertyDefinition.SortOrder"),
        Description("EventSessionCustomPropertyDefinition.ExposureLevel"),
        Description("EventSessionCustomPropertyDefinition.IsSearchable"),
        Description("EventSessionCustomPropertyDefinition.IsFilterable"),
        Description("EventSessionCustomPropertyDefinition.IsExportable"),
        Description("EventSessionCustomPropertyDefinition.IsModerationRelevant"),
        Description("EventSessionCustomPropertyDefinition.IsAnalyticsRelevant"),
        Description("EventSessionCustomPropertyDefinition.IsSystemOwned"),
        Description("EventSessionCustomPropertyDefinition.DefaultTextValue"),
        Description("EventSessionCustomPropertyDefinition.DefaultNumberValue"),
        Description("EventSessionCustomPropertyDefinition.DefaultBooleanValue"),
        Description("EventSessionCustomPropertyDefinition.DefaultDateTimeValue"),
        Description("EventSessionCustomPropertyDefinition.DefaultOption.DisplayName"),
        Description("EventSessionCustomPropertyDefinition.MinLength"),
        Description("EventSessionCustomPropertyDefinition.MaxLength"),
        Description("EventSessionCustomPropertyDefinition.RegexPattern"),
        Description("EventSessionCustomPropertyDefinition.MinNumber"),
        Description("EventSessionCustomPropertyDefinition.MaxNumber"),
        Description("EventSessionCustomPropertyDefinition.MinDateTime"),
        Description("EventSessionCustomPropertyDefinition.MaxDateTime"),
        Description("EventSessionCustomPropertyDefinition.AllowedUrlSchemes"),
        Description("EventSessionCustomPropertyOption.Namespace"),
        Description("EventSessionCustomPropertyOption.Key"),
        Description("EventSessionCustomPropertyOption.DisplayName"),
        Description("EventSessionCustomPropertyOption.Description"),
        Description("EventSessionCustomPropertyOption.Value"),
        Description("EventSessionCustomPropertyOption.IsDefault"),
        Description("EventSessionCustomPropertyOption.IsActive"),
        Description("EventSessionCustomPropertyOption.SortOrder"),
        Description("EventSessionCustomPropertyOption.ParentOption.DisplayName"),
        Description("EventSessionCustomPropertyValue.Ordinal"),
        Description("EventSessionCustomPropertyValue.TextValue"),
        Description("EventSessionCustomPropertyValue.NumberValue"),
        Description("EventSessionCustomPropertyValue.BooleanValue"),
        Description("EventSessionCustomPropertyValue.DateTimeValue"),
        Description("EventSessionCustomPropertyValue.Option.DisplayName"),

        Native("StorageObject.Uri", "public media URI used by native URI fields and description media entries"),
        Description("StorageObject.SafeDisplayName"),
        Description("StorageObject.Extension"),
        Description("StorageObject.ContentType"),
        Description("StorageObject.Size"),
        Description("StorageObject.Purpose"),
        Description("StorageObject.FileType.MasterCode"),
        Description("StorageObject.FileType.FullName"),
        Description("StorageObject.FileType.Description"),

        .. ExcludedMany(
            [
                "Event.Actor.ActorTypeId", "Event.Actor.GroupId",
                "Event.Actor.Organization.Pii.OrganizationId",
                "Event.Actor.OrganizationId", "Event.Actor.Pii.ActorId",
                "Event.Actor.UserId", "Event.ActorId", "Event.AtprotoRecordId", "Event.AudienceAgeId",
                "Event.AudienceGenderId", "Event.BackgroundImageId", "Event.Category.ParentId",
                "Event.EventFormatId", "Event.EventSeries.ActorId", "Event.EventSeries.FeaturedImageId",
                "Event.EventSeries.VisibilityTypeId", "Event.EventSeriesId", "Event.EventStatusId",
                "Event.EventTypeId", "Event.FeaturedImageId", "Event.IslamicAspect.MadhabId",
                "Event.IslamicAspect.PrimaryLanguageId", "Event.MadhabId", "Event.RegistrationPolicyId",
                "Event.VisibilityTypeId", "EventAgendaItem.EventDayId", "EventAgendaItem.EventId",
                "EventAgendaItem.EventLocationId", "EventAgendaItem.KindId", "EventAgendaItem.LocationId",
                "EventAgendaItem.RoomId", "EventCategoryLink.CategoryId", "EventCategoryLink.EventId",
                "EventCustomPropertyDefinition.DefaultOptionId", "EventCustomPropertyDefinition.EventId",
                "EventCustomPropertyOption.EventCustomPropertyDefinitionId", "EventCustomPropertyOption.ParentOptionId",
                "EventCustomPropertyValue.EventCustomPropertyDefinitionId", "EventCustomPropertyValue.EventId",
                "EventCustomPropertyValue.OptionId", "EventDay.BannerImageId", "EventDay.EventId",
                "EventLocation.EventId", "EventLocation.FullDetailsAudienceId", "EventLocation.LocationId",
                "EventLocationDisclosureResult.EventLocationId", "EventLocationDisclosureResult.LocationId",
                "EventSession.EventDayId", "EventSession.EventId", "EventSession.EventLocationId",
                "EventSession.EventSessionKindId", "EventSession.EventSessionStatusId",
                "EventSession.FeaturedImageId", "EventSession.IslamicAspect.EventSessionId",
                "EventSession.LocationId", "EventSession.RegistrationModeId", "EventSession.RoomId",
                "EventSession.SourceTemplateId", "EventSession.Speaker.Actor.ActorTypeId",
                "EventSession.Speaker.Actor.GroupId",
                "EventSession.Speaker.Actor.OrganizationId", "EventSession.Speaker.Actor.Pii.ActorId",
                "EventSession.Speaker.Actor.UserId",
                "EventSession.Speaker.ActorId", "EventSession.Speaker.EventSessionId",
                "EventSessionAgendaItem.EventLocationId", "EventSessionAgendaItem.EventSessionId",
                "EventSessionAgendaItem.LocationId", "EventSessionCategoryLink.CategoryId",
                "EventSessionCategoryLink.EventSessionId", "EventSessionCustomPropertyDefinition.DefaultOptionId",
                "EventSessionCustomPropertyDefinition.EventSessionId",
                "EventSessionCustomPropertyOption.EventSessionCustomPropertyDefinitionId",
                "EventSessionCustomPropertyOption.ParentOptionId",
                "EventSessionCustomPropertyValue.EventSessionCustomPropertyDefinitionId",
                "EventSessionCustomPropertyValue.EventSessionId", "EventSessionCustomPropertyValue.OptionId",
                "EventSessionGroup.EventId", "EventSessionGroup.EventLocationId", "EventSessionGroup.LocationId",
                "EventSessionGroup.RoomId", "EventSessionGroupSession.EventId",
                "EventSessionGroupSession.EventSessionGroupId", "EventSessionGroupSession.EventSessionId",
                "EventSessionLanguageLink.EventSessionId", "EventSessionLanguageLink.LanguageId",
                "EventSessionTagLink.EventSessionId", "EventSessionTagLink.TagId",
                "EventTagLink.EventId", "EventTagLink.TagId", "LocationRoom.LocationId",
                "StorageObject.ActorId", "StorageObject.FileTypeId", "StorageObject.OwningResourceId"
            ],
            "internal persistence or relationship identifier"),

        .. ExcludedMany(
            [
                "Event.Actor.CreatedAt", "Event.Actor.Group.CreatedAt", "Event.Actor.Organization.CreatedAt",
                "Event.EventSeries.CreatedAt", "EventAgendaItem.CreatedAt", "EventCategoryLink.CreatedAt",
                "EventCustomPropertyDefinition.CreatedAt", "EventCustomPropertyOption.CreatedAt",
                "EventCustomPropertyValue.CreatedAt", "EventDay.CreatedAt", "EventLocation.CreatedAt",
                "EventSession.CreatedAt", "EventSession.Speaker.Actor.CreatedAt",
                "EventSessionCategoryLink.CreatedAt", "EventSessionCustomPropertyDefinition.CreatedAt",
                "EventSessionCustomPropertyOption.CreatedAt", "EventSessionCustomPropertyValue.CreatedAt",
                "EventSessionGroup.CreatedAt", "EventSessionGroupSession.CreatedAt",
                "EventSessionTagLink.CreatedAt", "EventTagLink.CreatedAt", "LocationRoom.CreatedAt",
                "StorageObject.CreatedAt"
            ],
            "audit creation timestamp; the event record has its own native createdAt"),

        .. ExcludedMany(
            [
                "Event.Actor.ProfilePictureCid", "Event.Actor.Pii.ProfilePictureUri",
                "Event.Actor.ProfilePictureUri",
                "EventSession.Speaker.Actor.Pii.ProfilePictureUri",
                "EventSession.Speaker.Actor.ProfilePictureCid", "EventSession.Speaker.Actor.ProfilePictureUri"
            ],
            "provider identity or legacy remote-media bookkeeping"),

        .. ExcludedMany(
            [
                "Event.Actor.DisplayName", "Event.Actor.Organization.City",
                "Event.Actor.Organization.Country", "Event.Actor.Organization.FullName",
                "EventSession.Speaker.Actor.DisplayName"
            ],
            "not-mapped alias; the canonical PII extension value is classified separately"),

        .. ExcludedMany(
            [
                "Event.Actor.Organization.Address", "Event.Actor.Organization.Email",
                "Event.Actor.Organization.Postcode"
            ],
            "organizer contact or precise-address PII"),

        .. ExcludedMany(
            [
                "Event.InstantiatedFromTemplateAt", "Event.LastSyncedFromTemplateAt",
                "EventSession.InstantiatedFromTemplateAt", "EventSession.LastSyncedFromTemplateAt",
                "EventSession.SourceTemplateKey", "EventSession.SourceTemplateVersion"
            ],
            "template provenance metadata"),

        .. ExcludedMany(
            [
                "EventAgendaItem.LocalEndMinuteOfDay", "EventAgendaItem.LocalStartMinuteOfDay"
            ],
            "cached local-time projection; readable local date and time values are rendered"),

        .. ExcludedMany(
            [
                "EventLocation.HasValidLocationOrTbaShape", "EventLocation.IsToBeAnnounced",
                "EventLocation.LastPolicyActorUserId", "EventLocation.LastPolicyChangedAtUtc",
                "EventLocation.NeedsPrivacyReview", "EventLocation.PolicyVersion",
                "EventLocation.RevealFullDetailsFromUtc", "EventLocation.ShowCity",
                "EventLocation.ShowCoordinates", "EventLocation.ShowCountry", "EventLocation.ShowPostcode",
                "EventLocation.ShowRoomName", "EventLocation.ShowStreetAddress", "EventLocation.ShowVenueName"
            ],
            "privacy-policy input; only the evaluated public disclosure result may be published"),
        Excluded("EventLocationDisclosureResult.Purpose", "privacy evaluator routing metadata; only public-purpose results are mapped"),

        .. ExcludedMany(
            [
                "LocationRoom.Name", "LocationRoom.Description", "LocationRoom.Slug",
                "LocationRoom.Capacity", "LocationRoom.SortOrder"
            ],
            "raw room data is not a publication source; only disclosure-authorized room values may be rendered"),

        Excluded("StorageObject.FullName", "untrusted original filename; SafeDisplayName is rendered instead"),
        Excluded("StorageObject.LifecycleState", "media availability eligibility gate"),
        Excluded("StorageObject.Visibility", "media publication eligibility gate"),
        .. ExcludedMany(
            [
                "StorageObject.ObjectKey", "StorageObject.Provider", "StorageObject.Sha256Checksum",
                "StorageObject.OwningResourceKind", "StorageObject.QuarantineReason",
                "StorageObject.QuarantinedAt", "StorageObject.QuarantinedBy"
            ],
            "storage-provider, integrity, ownership, or quarantine bookkeeping"),

        Description("Event.Category.Parent.MasterCode"),
        Excluded("Event.Category.Parent.ParentId", "internal parent-category relationship identifier"),
        Excluded("Event.EventSeries.Actor.Pii.ActorId", "internal actor relationship identifier"),
        Excluded("Event.EventSeries.Actor.Pii.ProfilePictureUri", "legacy remote-media bookkeeping"),
        Excluded("Event.Actor.AtprotoIdentity.*", "identity authority metadata; only the verified handle is rendered"),
        Excluded("Event.EventSeries.Actor.AtprotoIdentity.*", "identity authority metadata; only the verified handle is rendered"),
        Excluded("EventSession.Speaker.Actor.AtprotoIdentity.*", "identity authority metadata; only the verified handle is rendered"),

        .. ExcludedNestedOptionFields("EventCustomPropertyDefinition.DefaultOption"),
        .. ExcludedNestedOptionFields("EventCustomPropertyOption.ParentOption"),
        .. ExcludedNestedOptionFields("EventCustomPropertyValue.Option"),
        .. ExcludedNestedOptionFields("EventSessionCustomPropertyDefinition.DefaultOption"),
        .. ExcludedNestedOptionFields("EventSessionCustomPropertyOption.ParentOption"),
        .. ExcludedNestedOptionFields("EventSessionCustomPropertyValue.Option"),

        Excluded("*.Id", "internal persistence identifier"),
        Excluded("*.TenantId", "tenant isolation identifier"),
        Excluded("*.CreatedBy", "audit identity"),
        Excluded("*.UpdatedAt", "audit metadata"),
        Excluded("*.UpdatedBy", "audit identity"),
        Excluded("*.DeletedAt", "soft-delete metadata"),
        Excluded("*.DeletedBy", "soft-delete identity"),
        Excluded("*.ConcurrencyStamp", "optimistic concurrency metadata"),
        Excluded("Event.IsDeleted", "lifecycle gate, never payload"),
        Excluded("EventDay.IsPublished", "publication eligibility gate, never payload"),
        Excluded("EventSessionGroup.IsPublished", "publication eligibility gate, never payload"),
        Excluded("*.IsDeleted", "soft-delete eligibility gate, never payload"),
        Excluded("Event.SourceTemplate*", "template provenance"),
        Excluded("Event.Provenance*", "import provenance"),
        Excluded("Event.Actor.Organization.Pii.Email", "organizer PII"),
        Excluded("Event.Actor.Organization.Pii.Address", "organizer PII"),
        Excluded("Event.Actor.Organization.Pii.Postcode", "organizer PII"),
        Excluded("EventCustomPropertyDefinition.SourceTemplate*", "template provenance"),
        Excluded("EventCustomPropertyDefinition.InstantiatedAt", "template provenance timestamp"),
        Excluded("EventCustomPropertyDefinition.LastSyncedFromTemplateAt", "template provenance timestamp"),
        Excluded("EventCustomPropertyOption.SourceTemplate*", "template provenance"),
        Excluded("EventSessionCustomPropertyDefinition.SourceTemplate*", "template provenance"),
        Excluded("EventSessionCustomPropertyDefinition.InstantiatedAt", "template provenance timestamp"),
        Excluded("EventSessionCustomPropertyDefinition.LastSyncedFromTemplateAt", "template provenance timestamp"),
        Excluded("EventSessionCustomPropertyOption.SourceTemplate*", "template provenance"),
        Excluded("Location.*", "raw physical location is never a publication source"),
        Excluded("Location.TenantId", "raw physical-location tenant identifier"),
        Excluded("LocationPii.*", "raw physical PII is never a publication source"),
        Excluded("EventCustomPropertyDefinition.ExposureLevel!=Public", "non-public custom property"),
        Excluded("EventSessionCustomPropertyDefinition.ExposureLevel!=Public", "non-public custom property"),
        Excluded("StorageObject.Visibility!=public_image", "non-public media"),
        Excluded("StorageObject.LifecycleState!=active", "unavailable or quarantined media")
    ];

    private static AtprotoSourceFieldManifestEntry Native(string sourcePath, string reason)
        => new(sourcePath, AtprotoSourceFieldDisposition.Native, reason);

    private static AtprotoSourceFieldManifestEntry Description(string sourcePath)
        => new(sourcePath, AtprotoSourceFieldDisposition.Description, "rendered in the single description");

    private static AtprotoSourceFieldManifestEntry Excluded(string sourcePath, string reason)
        => new(sourcePath, AtprotoSourceFieldDisposition.Excluded, reason);

    private static IEnumerable<AtprotoSourceFieldManifestEntry> ExcludedMany(
        IEnumerable<string> sourcePaths,
        string reason)
        => sourcePaths.Select(sourcePath => Excluded(sourcePath, reason));

    private static IEnumerable<AtprotoSourceFieldManifestEntry> ExcludedNestedOptionFields(string prefix)
    {
        yield return Excluded($"{prefix}.CreatedAt", "option audit metadata");
        foreach (string suffix in new[]
                 {
                     "Description", "IsActive", "IsDefault", "Key", "Namespace", "SortOrder", "Value"
                 })
        {
            yield return Excluded(
                $"{prefix}.{suffix}",
                "duplicate relationship view; the canonical option collection renders this field");
        }

        foreach (string suffix in new[]
                 {
                     prefix.StartsWith("EventSession", StringComparison.Ordinal)
                         ? "EventSessionCustomPropertyDefinitionId"
                         : "EventCustomPropertyDefinitionId",
                     "ParentOptionId"
                 })
        {
            yield return Excluded($"{prefix}.{suffix}", "internal option relationship identifier");
        }

        yield return Excluded($"{prefix}.SourceTemplateOptionId", "template provenance identifier");
        yield return Excluded($"{prefix}.SourceTemplateVersion", "template provenance version");
    }
}

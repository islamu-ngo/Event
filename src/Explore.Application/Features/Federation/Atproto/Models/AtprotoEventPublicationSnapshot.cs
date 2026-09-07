using System.Collections.Immutable;
using Explore.Application.Contracts.LocationPrivacy;

namespace Explore.Application.Features.Federation.Atproto.Models;

public sealed record AtprotoEventPublicationSnapshot(
    string Name,
    string? AuthoredDescription,
    string? Content,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    string? Mode,
    string? Status,
    bool RsvpExpected,
    AtprotoEventDetailsSnapshot Details,
    AtprotoOrganizerSnapshot Organizer,
    AtprotoEventSeriesSnapshot? Series,
    AtprotoEventIslamicAspectSnapshot? IslamicAspect,
    AtprotoEventTechAspectSnapshot? TechAspect,
    AtprotoEventAppearanceSnapshot Appearance,
    ImmutableArray<string> Categories,
    ImmutableArray<string> Tags,
    ImmutableArray<AtprotoEventLocationSnapshot> Locations,
    ImmutableArray<AtprotoEventDaySnapshot> Days,
    ImmutableArray<AtprotoEventSessionSnapshot> Sessions,
    ImmutableArray<AtprotoEventSessionGroupSnapshot> SessionGroups,
    ImmutableArray<AtprotoEventAgendaItemSnapshot> AgendaItems,
    ImmutableArray<AtprotoCustomPropertySnapshot> CustomProperties,
    ImmutableArray<AtprotoEventUriSnapshot> Uris);

public sealed record AtprotoEventDetailsSnapshot(
    string? Subtitle,
    string? EventType,
    string? AudienceGender,
    string? AudienceAge,
    int TotalViews,
    string Visibility,
    string? Madhab,
    string TimeZone,
    int? SessionCount,
    string? RegistrationPolicy,
    string? Slug,
    string PublicCode,
    DateOnly? FirstLocalSessionDate,
    DateOnly? LastLocalSessionDate,
    DateTimeOffset? LastSessionStartUtc);

public sealed record AtprotoOrganizerSnapshot(
    string DisplayName,
    string ActorType,
    string? Handle,
    string? Description,
    string? OrganizationName,
    string? OrganizationWebsite,
    string? OrganizationCountry,
    string? OrganizationCity,
    string? GroupName,
    string? GroupDescription,
    string? ProfileImageUri,
    string? BannerImageUri,
    string? BackgroundImageUri,
    string? BackgroundColor,
    string? BackgroundEffect,
    string? BannerColor,
    string? GroupProfileImageUri);

public sealed record AtprotoEventSeriesSnapshot(
    string Title,
    string? Description,
    string? Slug,
    bool IsPublished,
    int TotalViews,
    string Visibility,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    int? EventOrder,
    string OrganizerName,
    string? OrganizerHandle,
    string? FeaturedImageUri);

public sealed record AtprotoEventIslamicAspectSnapshot(
    string? Madhab,
    string? ReferencePrayer,
    int? PrayerOffsetMinutes,
    string GenderMode,
    bool IncludesQuranRecitation,
    string? PrimaryLanguage);

public sealed record AtprotoEventTechAspectSnapshot(
    string? GithubRepositoryUri,
    string? HackathonTrack,
    string SkillLevel,
    ImmutableArray<string> TechnologyTags,
    bool RequiresLaptop,
    bool IsCodingCompetition,
    int? MaximumTeamSize,
    decimal? PrizePool,
    string? PrizeCurrencyCode);

public sealed record AtprotoEventAppearanceSnapshot(
    string? BackgroundColor,
    string? BackgroundEffect,
    string? FeaturedImageUri,
    string? BackgroundImageUri);

public sealed record AtprotoEventLocationSnapshot(
    EventLocationDisclosureState State,
    string? Country,
    string? TimeZone,
    string? City,
    string? VenueName,
    string? RoomName,
    string? RoomDescription,
    string? StreetAddress,
    string? Postcode,
    double? Latitude,
    double? Longitude,
    string? FormattedAddress,
    string? MapUri,
    string? Geohash);

public sealed record AtprotoEventDaySnapshot(
    DateOnly Date,
    string? Label,
    string? Description,
    string? BannerText,
    string? BannerImageUri,
    bool AllowsRegistration,
    int SortOrder);

public sealed record AtprotoEventSessionSnapshot(
    string Key,
    string? Title,
    string? Description,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    DateOnly? LocalStartDate,
    DateOnly? LocalEndDate,
    TimeOnly? LocalStartTime,
    TimeOnly? LocalEndTime,
    string EndTimeType,
    int SortOrder,
    string? Slug,
    string? Kind,
    string Status,
    string? RegistrationMode,
    int? MaximumAttendees,
    int? CurrentAttendees,
    string? FeaturedImageUri,
    AtprotoEventLocationSnapshot? Location,
    AtprotoSessionIslamicAspectSnapshot? IslamicAspect,
    ImmutableArray<string> Categories,
    ImmutableArray<string> Tags,
    ImmutableArray<string> Languages,
    ImmutableArray<AtprotoSpeakerSnapshot> Speakers,
    ImmutableArray<AtprotoSessionAgendaItemSnapshot> AgendaItems,
    ImmutableArray<AtprotoCustomPropertySnapshot> CustomProperties);

public sealed record AtprotoSessionIslamicAspectSnapshot(
    string StartTimeType,
    string? ReferencePrayer,
    int? OffsetMinutes,
    string? EndReferencePrayer,
    int? EndOffsetMinutes,
    bool RequiresWudu,
    string? RitualRequirements);

public sealed record AtprotoSpeakerSnapshot(
    string DisplayName,
    string? Handle,
    string? Description,
    string? ProfileImageUri,
    string? BannerImageUri,
    string? BackgroundImageUri,
    string? BackgroundColor,
    string? BackgroundEffect,
    string? BannerColor);

public sealed record AtprotoSessionAgendaItemSnapshot(
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Title,
    string? Description,
    AtprotoEventLocationSnapshot? Location);

public sealed record AtprotoEventSessionGroupSnapshot(
    string Name,
    string? Description,
    string? Slug,
    string? Color,
    int SortOrder,
    AtprotoEventLocationSnapshot? Location,
    ImmutableArray<AtprotoEventSessionGroupMemberSnapshot> Sessions);

public sealed record AtprotoEventSessionGroupMemberSnapshot(
    string SessionTitle,
    bool IsPrimary,
    int SortOrder);

public sealed record AtprotoEventAgendaItemSnapshot(
    string Title,
    string? Description,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    DateOnly LocalStartDate,
    DateOnly LocalEndDate,
    TimeOnly LocalStartTime,
    TimeOnly LocalEndTime,
    string? Kind,
    int SortOrder,
    AtprotoEventLocationSnapshot? Location);

public sealed record AtprotoCustomPropertySnapshot(
    string Namespace,
    string Key,
    string Name,
    string? Description,
    string PropertyType,
    bool IsRequired,
    bool IsMultiValue,
    bool IsActive,
    int SortOrder,
    string ExposureLevel,
    bool IsSearchable,
    bool IsFilterable,
    bool IsExportable,
    bool IsModerationRelevant,
    bool IsAnalyticsRelevant,
    bool IsSystemOwned,
    string? DefaultValue,
    int? MinimumLength,
    int? MaximumLength,
    string? Pattern,
    decimal? MinimumNumber,
    decimal? MaximumNumber,
    DateTimeOffset? MinimumDateTime,
    DateTimeOffset? MaximumDateTime,
    string? AllowedUrlSchemes,
    ImmutableArray<AtprotoCustomPropertyOptionSnapshot> Options,
    ImmutableArray<AtprotoCustomPropertyValueSnapshot> Values);

public sealed record AtprotoCustomPropertyOptionSnapshot(
    string Namespace,
    string Key,
    string DisplayName,
    string? Description,
    string Value,
    bool IsDefault,
    bool IsActive,
    int SortOrder,
    string? ParentDisplayName);

public sealed record AtprotoCustomPropertyValueSnapshot(
    int Ordinal,
    string ValueType,
    string Value,
    string? OptionDisplayName);

public sealed record AtprotoEventUriSnapshot(string Uri, string Name);

public sealed record AtprotoEventPublicationSnapshotResult(
    AtprotoEventPublicationSnapshot? Snapshot,
    ImmutableArray<string> Errors)
{
    public bool IsEligible => Snapshot is not null && Errors.IsEmpty;

    public static AtprotoEventPublicationSnapshotResult Eligible(AtprotoEventPublicationSnapshot snapshot)
        => new(snapshot, []);

    public static AtprotoEventPublicationSnapshotResult Ineligible(params IEnumerable<string> errors)
        => new(null, [.. errors]);
}

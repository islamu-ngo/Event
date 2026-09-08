using System.Globalization;
using System.Text;
using Explore.Application.Features.Federation.Atproto.Models;

namespace Explore.Application.Features.Federation.Atproto.Services;

public static class AtprotoEventDescriptionFormatter
{
    public static string Format(AtprotoEventPublicationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var output = new StringBuilder();
        AppendTextSection(output, "Description", snapshot.AuthoredDescription);
        AppendTextSection(output, "Content", snapshot.Content);
        AppendEventDetails(output, snapshot);
        AppendOrganizer(output, snapshot.Organizer);
        AppendSeries(output, snapshot.Series);
        AppendIslamicAspect(output, snapshot.IslamicAspect);
        AppendTechAspect(output, snapshot.TechAspect);
        AppendLocations(output, snapshot.Locations);
        AppendDays(output, snapshot.Days);
        AppendSessions(output, snapshot.Sessions);
        AppendSessionGroups(output, snapshot.SessionGroups);
        AppendAgenda(output, snapshot.AgendaItems);
        AppendCustomProperties(output, "Event properties", snapshot.CustomProperties);
        AppendAppearance(output, snapshot.Appearance);
        return output.ToString().TrimEnd();
    }

    private static void AppendEventDetails(StringBuilder output, AtprotoEventPublicationSnapshot snapshot)
    {
        AtprotoEventDetailsSnapshot details = snapshot.Details;
        StartSection(output, "Event details");
        Field(output, "Subtitle", details.Subtitle);
        Field(output, "Event type", details.EventType);
        Field(output, "Audience gender", details.AudienceGender);
        Field(output, "Audience age", details.AudienceAge);
        Field(output, "Views", details.TotalViews.ToString(CultureInfo.InvariantCulture));
        Field(output, "Visibility", details.Visibility);
        Field(output, "Madhab", details.Madhab);
        Field(output, "Time zone", details.TimeZone);
        Field(output, "Session count", details.SessionCount?.ToString(CultureInfo.InvariantCulture));
        Field(output, "Registration policy", details.RegistrationPolicy);
        Field(output, "Slug", details.Slug);
        Field(output, "Public code", details.PublicCode);
        Field(output, "First local session date", Date(details.FirstLocalSessionDate));
        Field(output, "Last local session date", Date(details.LastLocalSessionDate));
        Field(output, "Last session starts", Timestamp(details.LastSessionStartUtc));
        Field(output, "Categories", Join(snapshot.Categories));
        Field(output, "Tags", Join(snapshot.Tags));
        Field(output, "Registration requested", YesNo(snapshot.RsvpExpected));
    }

    private static void AppendOrganizer(StringBuilder output, AtprotoOrganizerSnapshot organizer)
    {
        StartSection(output, "Organizer");
        Field(output, "Name", organizer.DisplayName);
        Field(output, "Actor type", organizer.ActorType);
        Field(output, "Handle", organizer.Handle);
        Field(output, "Description", organizer.Description);
        Field(output, "Organization", organizer.OrganizationName);
        Field(output, "Organization website", organizer.OrganizationWebsite);
        Field(output, "Organization country", organizer.OrganizationCountry);
        Field(output, "Organization city", organizer.OrganizationCity);
        Field(output, "Group", organizer.GroupName);
        Field(output, "Group description", organizer.GroupDescription);
        Field(output, "Profile image", organizer.ProfileImageUri);
        Field(output, "Banner image", organizer.BannerImageUri);
        Field(output, "Background image", organizer.BackgroundImageUri);
        Field(output, "Background color", organizer.BackgroundColor);
        Field(output, "Background effect", organizer.BackgroundEffect);
        Field(output, "Banner color", organizer.BannerColor);
        Field(output, "Group profile image", organizer.GroupProfileImageUri);
    }

    private static void AppendSeries(StringBuilder output, AtprotoEventSeriesSnapshot? series)
    {
        if (series is null)
        {
            return;
        }

        StartSection(output, "Series");
        Field(output, "Title", series.Title);
        Field(output, "Description", series.Description);
        Field(output, "Slug", series.Slug);
        Field(output, "Published", YesNo(series.IsPublished));
        Field(output, "Views", series.TotalViews.ToString(CultureInfo.InvariantCulture));
        Field(output, "Visibility", series.Visibility);
        Field(output, "Starts", Timestamp(series.StartsAt));
        Field(output, "Ends", Timestamp(series.EndsAt));
        Field(output, "Event order", series.EventOrder?.ToString(CultureInfo.InvariantCulture));
        Field(output, "Organizer", series.OrganizerName);
        Field(output, "Organizer handle", series.OrganizerHandle);
        Field(output, "Featured image", series.FeaturedImageUri);
    }

    private static void AppendIslamicAspect(StringBuilder output, AtprotoEventIslamicAspectSnapshot? aspect)
    {
        if (aspect is null)
        {
            return;
        }

        StartSection(output, "Islamic event details");
        Field(output, "Madhab", aspect.Madhab);
        Field(output, "Reference prayer", aspect.ReferencePrayer);
        Field(output, "Prayer offset", Minutes(aspect.PrayerOffsetMinutes));
        Field(output, "Gender mode", aspect.GenderMode);
        Field(output, "Includes Quran recitation", YesNo(aspect.IncludesQuranRecitation));
        Field(output, "Primary language", aspect.PrimaryLanguage);
    }

    private static void AppendTechAspect(StringBuilder output, AtprotoEventTechAspectSnapshot? aspect)
    {
        if (aspect is null)
        {
            return;
        }

        StartSection(output, "Technology event details");
        Field(output, "GitHub repository", aspect.GithubRepositoryUri);
        Field(output, "Hackathon track", aspect.HackathonTrack);
        Field(output, "Skill level", aspect.SkillLevel);
        Field(output, "Technology stack", Join(aspect.TechnologyTags));
        Field(output, "Laptop required", YesNo(aspect.RequiresLaptop));
        Field(output, "Coding competition", YesNo(aspect.IsCodingCompetition));
        Field(output, "Maximum team size", aspect.MaximumTeamSize?.ToString(CultureInfo.InvariantCulture));
        Field(output, "Prize", Money(aspect.PrizePool, aspect.PrizeCurrencyCode));
    }

    private static void AppendLocations(
        StringBuilder output,
        IReadOnlyCollection<AtprotoEventLocationSnapshot> locations)
    {
        if (locations.Count == 0)
        {
            return;
        }

        StartSection(output, "Locations");
        int index = 0;
        foreach (AtprotoEventLocationSnapshot location in locations)
        {
            index++;
            output.Append("Location ").Append(index.ToString(CultureInfo.InvariantCulture)).AppendLine(":");
            Field(output, "Disclosure state", location.State.ToString(), 2);
            Field(output, "Venue", location.VenueName, 2);
            Field(output, "Room", location.RoomName, 2);
            Field(output, "Room description", location.RoomDescription, 2);
            Field(output, "Street", location.StreetAddress, 2);
            Field(output, "Postcode", location.Postcode, 2);
            Field(output, "City", location.City, 2);
            Field(output, "Country", location.Country, 2);
            Field(output, "Time zone", location.TimeZone, 2);
            Field(output, "Coordinates", Coordinates(location.Latitude, location.Longitude), 2);
            Field(output, "Formatted address", location.FormattedAddress, 2);
            Field(output, "Map", location.MapUri, 2);
            Field(output, "Geohash", location.Geohash, 2);
        }
    }

    private static void AppendDays(StringBuilder output, IReadOnlyCollection<AtprotoEventDaySnapshot> days)
    {
        if (days.Count == 0)
        {
            return;
        }

        StartSection(output, "Days");
        foreach (AtprotoEventDaySnapshot day in days)
        {
            output.Append(day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).AppendLine(":");
            Field(output, "Label", day.Label, 2);
            Field(output, "Description", day.Description, 2);
            Field(output, "Banner", day.BannerText, 2);
            Field(output, "Banner image", day.BannerImageUri, 2);
            Field(output, "Day registration allowed", YesNo(day.AllowsRegistration), 2);
            Field(output, "Order", day.SortOrder.ToString(CultureInfo.InvariantCulture), 2);
        }
    }

    private static void AppendSessions(
        StringBuilder output,
        IReadOnlyCollection<AtprotoEventSessionSnapshot> sessions)
    {
        if (sessions.Count == 0)
        {
            return;
        }

        StartSection(output, "Program");
        foreach (AtprotoEventSessionSnapshot session in sessions)
        {
            output.Append(session.Key).AppendLine(":");
            Field(output, "Title", session.Title, 2);
            Field(output, "Description", session.Description, 2);
            Field(output, "Starts", Timestamp(session.StartsAt), 2);
            Field(output, "Ends", Timestamp(session.EndsAt), 2);
            Field(output, "Local start date", Date(session.LocalStartDate), 2);
            Field(output, "Local end date", Date(session.LocalEndDate), 2);
            Field(output, "Local start time", Time(session.LocalStartTime), 2);
            Field(output, "Local end time", Time(session.LocalEndTime), 2);
            Field(output, "End time type", session.EndTimeType, 2);
            Field(output, "Order", session.SortOrder.ToString(CultureInfo.InvariantCulture), 2);
            Field(output, "Slug", session.Slug, 2);
            Field(output, "Kind", session.Kind, 2);
            Field(output, "Status", session.Status, 2);
            Field(output, "Registration mode", session.RegistrationMode, 2);
            Field(output, "Maximum attendees", session.MaximumAttendees?.ToString(CultureInfo.InvariantCulture), 2);
            Field(output, "Current attendees", session.CurrentAttendees?.ToString(CultureInfo.InvariantCulture), 2);
            Field(output, "Featured image", session.FeaturedImageUri, 2);
            Field(output, "Categories", Join(session.Categories), 2);
            Field(output, "Tags", Join(session.Tags), 2);
            Field(output, "Languages", Join(session.Languages), 2);
            AppendInlineLocation(output, session.Location, 2);
            AppendSessionIslamicAspect(output, session.IslamicAspect, 2);
            foreach (AtprotoSpeakerSnapshot speaker in session.Speakers)
            {
                Field(output, "Speaker", Speaker(speaker), 2);
            }

            foreach (AtprotoSessionAgendaItemSnapshot item in session.AgendaItems)
            {
                Field(output, "Agenda item", $"{Timestamp(item.StartsAt)}–{Timestamp(item.EndsAt)} | {item.Title}", 2);
                Field(output, "Agenda description", item.Description, 4);
                AppendInlineLocation(output, item.Location, 4);
            }

            AppendCustomProperties(output, "Session properties", session.CustomProperties, 2);
        }
    }

    private static void AppendSessionIslamicAspect(
        StringBuilder output,
        AtprotoSessionIslamicAspectSnapshot? aspect,
        int indent)
    {
        if (aspect is null)
        {
            return;
        }

        Field(output, "Islamic start type", aspect.StartTimeType, indent);
        Field(output, "Start prayer", aspect.ReferencePrayer, indent);
        Field(output, "Start prayer offset", Minutes(aspect.OffsetMinutes), indent);
        Field(output, "End prayer", aspect.EndReferencePrayer, indent);
        Field(output, "End prayer offset", Minutes(aspect.EndOffsetMinutes), indent);
        Field(output, "Requires wudu", YesNo(aspect.RequiresWudu), indent);
        Field(output, "Ritual requirements", aspect.RitualRequirements, indent);
    }

    private static void AppendSessionGroups(
        StringBuilder output,
        IReadOnlyCollection<AtprotoEventSessionGroupSnapshot> groups)
    {
        if (groups.Count == 0)
        {
            return;
        }

        StartSection(output, "Tracks and groups");
        foreach (AtprotoEventSessionGroupSnapshot group in groups)
        {
            output.Append(group.Name).AppendLine(":");
            Field(output, "Description", group.Description, 2);
            Field(output, "Slug", group.Slug, 2);
            Field(output, "Color", group.Color, 2);
            Field(output, "Order", group.SortOrder.ToString(CultureInfo.InvariantCulture), 2);
            foreach (AtprotoEventSessionGroupMemberSnapshot session in group.Sessions)
            {
                Field(
                    output,
                    "Session",
                    $"{session.SessionTitle} | Primary: {YesNo(session.IsPrimary)} | Order: {session.SortOrder.ToString(CultureInfo.InvariantCulture)}",
                    2);
            }
            AppendInlineLocation(output, group.Location, 2);
        }
    }

    private static void AppendAgenda(
        StringBuilder output,
        IReadOnlyCollection<AtprotoEventAgendaItemSnapshot> agendaItems)
    {
        if (agendaItems.Count == 0)
        {
            return;
        }

        StartSection(output, "Event agenda");
        foreach (AtprotoEventAgendaItemSnapshot item in agendaItems)
        {
            output.Append(item.Title).AppendLine(":");
            Field(output, "Description", item.Description, 2);
            Field(output, "Starts", Timestamp(item.StartsAt), 2);
            Field(output, "Ends", Timestamp(item.EndsAt), 2);
            Field(output, "Local start date", Date(item.LocalStartDate), 2);
            Field(output, "Local end date", Date(item.LocalEndDate), 2);
            Field(output, "Local start time", Time(item.LocalStartTime), 2);
            Field(output, "Local end time", Time(item.LocalEndTime), 2);
            Field(output, "Kind", item.Kind, 2);
            Field(output, "Order", item.SortOrder.ToString(CultureInfo.InvariantCulture), 2);
            AppendInlineLocation(output, item.Location, 2);
        }
    }

    private static void AppendCustomProperties(
        StringBuilder output,
        string heading,
        IReadOnlyCollection<AtprotoCustomPropertySnapshot> properties,
        int indent = 0)
    {
        if (properties.Count == 0)
        {
            return;
        }

        if (indent == 0)
        {
            StartSection(output, heading);
        }
        else
        {
            output.Append(' ', indent).Append(heading).AppendLine(":");
        }

        foreach (AtprotoCustomPropertySnapshot property in properties)
        {
            int fieldIndent = indent == 0 ? 0 : indent + 2;
            Field(output, property.Name, null, fieldIndent);
            Field(output, $"{property.Name} namespace", property.Namespace, fieldIndent + 2);
            Field(output, $"{property.Name} key", property.Key, fieldIndent + 2);
            Field(output, $"{property.Name} description", property.Description, fieldIndent + 2);
            Field(output, $"{property.Name} type", property.PropertyType, fieldIndent + 2);
            Field(output, $"{property.Name} required", YesNo(property.IsRequired), fieldIndent + 2);
            Field(output, $"{property.Name} multi-value", YesNo(property.IsMultiValue), fieldIndent + 2);
            Field(output, $"{property.Name} active", YesNo(property.IsActive), fieldIndent + 2);
            Field(output, $"{property.Name} order", property.SortOrder.ToString(CultureInfo.InvariantCulture), fieldIndent + 2);
            Field(output, $"{property.Name} exposure", property.ExposureLevel, fieldIndent + 2);
            Field(output, $"{property.Name} searchable", YesNo(property.IsSearchable), fieldIndent + 2);
            Field(output, $"{property.Name} filterable", YesNo(property.IsFilterable), fieldIndent + 2);
            Field(output, $"{property.Name} exportable", YesNo(property.IsExportable), fieldIndent + 2);
            Field(output, $"{property.Name} moderation relevant", YesNo(property.IsModerationRelevant), fieldIndent + 2);
            Field(output, $"{property.Name} analytics relevant", YesNo(property.IsAnalyticsRelevant), fieldIndent + 2);
            Field(output, $"{property.Name} system owned", YesNo(property.IsSystemOwned), fieldIndent + 2);
            Field(output, $"{property.Name} default", property.DefaultValue, fieldIndent + 2);
            Field(output, $"{property.Name} minimum length", Integer(property.MinimumLength), fieldIndent + 2);
            Field(output, $"{property.Name} maximum length", Integer(property.MaximumLength), fieldIndent + 2);
            Field(output, $"{property.Name} pattern", property.Pattern, fieldIndent + 2);
            Field(output, $"{property.Name} minimum number", Decimal(property.MinimumNumber), fieldIndent + 2);
            Field(output, $"{property.Name} maximum number", Decimal(property.MaximumNumber), fieldIndent + 2);
            Field(output, $"{property.Name} minimum date/time", Timestamp(property.MinimumDateTime), fieldIndent + 2);
            Field(output, $"{property.Name} maximum date/time", Timestamp(property.MaximumDateTime), fieldIndent + 2);
            Field(output, $"{property.Name} allowed URL schemes", property.AllowedUrlSchemes, fieldIndent + 2);
            foreach (AtprotoCustomPropertyOptionSnapshot option in property.Options)
            {
                Field(
                    output,
                    $"{property.Name} option",
                    $"{option.DisplayName} | Value: {option.Value} | Namespace: {option.Namespace} | Key: {option.Key}",
                    fieldIndent + 2);
                Field(output, "Option description", option.Description, fieldIndent + 4);
                Field(output, "Option default", YesNo(option.IsDefault), fieldIndent + 4);
                Field(output, "Option active", YesNo(option.IsActive), fieldIndent + 4);
                Field(output, "Option order", option.SortOrder.ToString(CultureInfo.InvariantCulture), fieldIndent + 4);
                Field(output, "Parent option", option.ParentDisplayName, fieldIndent + 4);
            }

            foreach (AtprotoCustomPropertyValueSnapshot value in property.Values)
            {
                Field(
                    output,
                    $"{property.Name} value",
                    $"{value.Value} | Type: {value.ValueType} | Order: {value.Ordinal.ToString(CultureInfo.InvariantCulture)}",
                    fieldIndent + 2);
                Field(output, "Selected option", value.OptionDisplayName, fieldIndent + 4);
            }
        }
    }

    private static void AppendAppearance(StringBuilder output, AtprotoEventAppearanceSnapshot appearance)
    {
        if (appearance.BackgroundColor is null
            && appearance.BackgroundEffect is null
            && appearance.FeaturedImageUri is null
            && appearance.BackgroundImageUri is null)
        {
            return;
        }

        StartSection(output, "Appearance and media");
        Field(output, "Background color", appearance.BackgroundColor);
        Field(output, "Background effect", appearance.BackgroundEffect);
        Field(output, "Featured image", appearance.FeaturedImageUri);
        Field(output, "Background image", appearance.BackgroundImageUri);
    }

    private static void AppendInlineLocation(
        StringBuilder output,
        AtprotoEventLocationSnapshot? location,
        int indent)
    {
        if (location is null)
        {
            return;
        }

        string?[] values =
        [
            location.VenueName,
            location.RoomName,
            location.StreetAddress,
            location.Postcode,
            location.City,
            location.Country,
            Coordinates(location.Latitude, location.Longitude),
            location.MapUri
        ];
        Field(output, "Location", string.Join(", ", values.Where(value => !string.IsNullOrWhiteSpace(value))), indent);
        Field(output, "Location state", location.State.ToString(), indent + 2);
        Field(output, "Location time zone", location.TimeZone, indent + 2);
        Field(output, "Room description", location.RoomDescription, indent + 2);
        Field(output, "Formatted address", location.FormattedAddress, indent + 2);
        Field(output, "Geohash", location.Geohash, indent + 2);
    }

    private static void AppendTextSection(StringBuilder output, string heading, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        StartSection(output, heading);
        output.AppendLine(value.Trim()).AppendLine();
    }

    private static void StartSection(StringBuilder output, string heading)
    {
        if (output.Length != 0 && output[^1] != '\n')
        {
            output.AppendLine();
        }

        output.Append("## ").AppendLine(heading);
    }

    private static void Field(StringBuilder output, string label, string? value, int indent = 0)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        string prefix = new(' ', indent);
        string[] lines = value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        output.Append(prefix).Append("- ").Append(label).Append(": ").AppendLine(lines[0].Trim());
        foreach (string line in lines.Skip(1))
        {
            output.Append(prefix).Append("  ").AppendLine(line.Trim());
        }
    }

    private static string? Timestamp(DateTimeOffset? value)
        => value?.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

    private static string Timestamp(DateTimeOffset value)
        => value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

    private static string? Date(DateOnly? value)
        => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string? Integer(int? value)
        => value?.ToString(CultureInfo.InvariantCulture);

    private static string? Decimal(decimal? value)
        => value?.ToString(CultureInfo.InvariantCulture);

    private static string Date(DateOnly value)
        => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string? Time(TimeOnly? value)
        => value?.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    private static string Time(TimeOnly value)
        => value.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    private static string? Money(decimal? amount, string? currency)
        => amount.HasValue
            ? string.Join(' ', new[] { amount.Value.ToString("0.############################", CultureInfo.InvariantCulture), currency }
                .Where(value => !string.IsNullOrWhiteSpace(value)))
            : null;

    private static string? Minutes(int? value)
        => value.HasValue ? $"{value.Value.ToString(CultureInfo.InvariantCulture)} minutes" : null;

    private static string? Coordinates(double? latitude, double? longitude)
        => latitude.HasValue && longitude.HasValue
            ? $"{latitude.Value.ToString("R", CultureInfo.InvariantCulture)}, {longitude.Value.ToString("R", CultureInfo.InvariantCulture)}"
            : null;

    private static string? Join(IEnumerable<string> values)
    {
        string joined = string.Join(", ", values.Where(value => !string.IsNullOrWhiteSpace(value)));
        return joined.Length == 0 ? null : joined;
    }

    private static string Speaker(AtprotoSpeakerSnapshot speaker)
        => string.Join(" | ", new[]
        {
            speaker.DisplayName,
            speaker.Handle,
            speaker.Description,
            speaker.ProfileImageUri,
            speaker.BannerImageUri,
            speaker.BackgroundImageUri,
            speaker.BackgroundColor,
            speaker.BackgroundEffect,
            speaker.BannerColor
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static string YesNo(bool value) => value ? "Yes" : "No";
}

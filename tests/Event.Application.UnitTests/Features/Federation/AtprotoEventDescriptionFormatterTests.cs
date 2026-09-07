using System.Collections.Immutable;
using System.Globalization;
using Explore.Application.Contracts.LocationPrivacy;
using Explore.Application.Features.Federation.Atproto.Models;
using Explore.Application.Features.Federation.Atproto.Services;

namespace Event.Application.UnitTests.Features.Federation;

public sealed class AtprotoEventDescriptionFormatterTests
{
    [Test]
    public async Task Format_RendersAllPublicCanaries_InOneDescription_WithoutPrivateValues()
    {
        AtprotoEventPublicationSnapshot snapshot = CreateSnapshot();

        string description = AtprotoEventDescriptionFormatter.Format(snapshot);

        string[] requiredCanaries =
        [
            "authored-canary",
            "content-canary",
            "public-code-canary",
            "event-category-canary",
            "event-tag-canary",
            "organizer-canary",
            "series-canary",
            "islamic-canary",
            "tech-canary",
            "venue-canary",
            "day-canary",
            "session-canary",
            "speaker-canary",
            "session-agenda-canary",
            "session-eav-canary",
            "group-canary",
            "event-agenda-canary",
            "event-eav-canary",
            "media-canary"
        ];
        foreach (string canary in requiredCanaries)
        {
            await Assert.That(description).Contains(canary);
        }

        await Assert.That(description).DoesNotContain("private-location-canary");
        await Assert.That(description).DoesNotContain("attendee-pii-canary");
        await Assert.That(description.Split("## Program", StringSplitOptions.None).Length).IsEqualTo(2);
    }

    [Test]
    public async Task Format_IsByteStableAcrossCultures()
    {
        AtprotoEventPublicationSnapshot snapshot = CreateSnapshot();
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ar-SA");
            string arabicCulture = AtprotoEventDescriptionFormatter.Format(snapshot);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-BE");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-BE");
            string frenchCulture = AtprotoEventDescriptionFormatter.Format(snapshot);

            await Assert.That(frenchCulture).IsEqualTo(arabicCulture);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Test]
    public async Task SourceFieldManifests_AreIndependentCompleteAndReasoned()
    {
        AtprotoSourceFieldManifestEntry[] eventEntries = AtprotoEventSourceFieldManifest.Entries.ToArray();

        string[] sourcePaths = AtprotoEventProjectionSourceContract.SourcePaths.ToArray();
        string[] uncovered = sourcePaths
            .Where(path => !eventEntries.Any(entry => Matches(entry.SourcePath, path)))
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] staleManifestRules = eventEntries
            .Where(entry => !sourcePaths.Any(path => Matches(entry.SourcePath, path)))
            .Select(entry => entry.SourcePath)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] ambiguous = sourcePaths
            .Where(path => MostSpecificMatches(eventEntries, path) != 1)
            .Order(StringComparer.Ordinal)
            .ToArray();

        await Assert.That(uncovered).IsEmpty();
        await Assert.That(staleManifestRules).IsEmpty();
        await Assert.That(ambiguous).IsEmpty();
        await Assert.That(eventEntries.All(entry => !string.IsNullOrWhiteSpace(entry.Reason))).IsTrue();
    }

    private static int MostSpecificMatches(
        IReadOnlyCollection<AtprotoSourceFieldManifestEntry> entries,
        string sourcePath)
    {
        AtprotoSourceFieldManifestEntry[] matches = entries
            .Where(entry => Matches(entry.SourcePath, sourcePath))
            .ToArray();
        if (matches.Length == 0)
        {
            return 0;
        }

        int maximumSpecificity = matches.Max(entry => entry.SourcePath.Count(character => character != '*'));
        return matches.Count(entry => entry.SourcePath.Count(character => character != '*') == maximumSpecificity);
    }

    private static bool Matches(string pattern, string sourcePath)
    {
        int wildcard = pattern.IndexOf('*', StringComparison.Ordinal);
        if (wildcard < 0)
        {
            return string.Equals(pattern, sourcePath, StringComparison.Ordinal);
        }

        string prefix = pattern[..wildcard];
        string suffix = pattern[(wildcard + 1)..];
        return sourcePath.StartsWith(prefix, StringComparison.Ordinal)
            && sourcePath.EndsWith(suffix, StringComparison.Ordinal);
    }

    private static AtprotoEventPublicationSnapshot CreateSnapshot()
    {
        var location = new AtprotoEventLocationSnapshot(
            EventLocationDisclosureState.Available,
            "BE",
            "Europe/Brussels",
            "Brussels",
            "venue-canary",
            "Room A",
            "Room description",
            "Public street",
            "1000",
            50.8503,
            4.3517,
            "Public formatted address",
            "https://maps.example/location",
            "u151");
        var customProperty = CreateCustomProperty("event-eav-canary", "event-eav-value");
        var sessionCustomProperty = CreateCustomProperty("session-eav-canary", "session-eav-value");
        var session = new AtprotoEventSessionSnapshot(
            "session-canary",
            "Session title",
            "Session description",
            new(2026, 8, 1, 9, 0, 0, TimeSpan.FromHours(2)),
            new(2026, 8, 1, 10, 30, 0, TimeSpan.FromHours(2)),
            new(2026, 8, 1),
            new(2026, 8, 1),
            new(9, 0),
            new(10, 30),
            "Fixed",
            1,
            "session-slug",
            "Workshop",
            "Published",
            "Required",
            100,
            25,
            "https://cdn.example/session.png",
            location,
            new("islamic-canary", "Fajr", 15, "Dhuhr", -10, true, "Ritual details"),
            ["Category / Child"],
            ["Tag"],
            ["English"],
            [new(
                "speaker-canary",
                "speaker.example",
                "Speaker description",
                "https://cdn.example/speaker.png",
                null,
                null,
                null,
                null,
                null)],
            [new(
                new(2026, 8, 1, 9, 15, 0, TimeSpan.Zero),
                new(2026, 8, 1, 9, 45, 0, TimeSpan.Zero),
                "session-agenda-canary",
                "Agenda details",
                location)],
            [sessionCustomProperty]);

        return new(
            "Event name",
            "authored-canary",
            "content-canary",
            new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero),
            new(2026, 8, 1, 7, 0, 0, TimeSpan.Zero),
            new(2026, 8, 1, 8, 30, 0, TimeSpan.Zero),
            "community.lexicon.calendar.event#hybrid",
            "community.lexicon.calendar.event#scheduled",
            true,
            new(
                "Subtitle",
                "Conference",
                "All",
                "Adults",
                7,
                "Public",
                "Hanafi",
                "Europe/Brussels",
                1,
                "Open",
                "event-slug",
                "public-code-canary",
                new(2026, 8, 1),
                new(2026, 8, 1),
                new(2026, 8, 1, 9, 0, 0, TimeSpan.Zero)),
            new(
                "organizer-canary",
                "Organization",
                "organizer.example",
                "Organizer description",
                "Organization name",
                "https://org.example",
                "Belgium",
                "Brussels",
                "Group name",
                "Group description",
                "https://cdn.example/profile.png",
                "https://cdn.example/banner.png",
                "https://cdn.example/background.png",
                "#000000",
                "stars",
                "#ffffff",
                "https://cdn.example/group.png"),
            new(
                "series-canary",
                "Series description",
                "series-slug",
                true,
                10,
                "Public",
                new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
                new(2026, 8, 31, 0, 0, 0, TimeSpan.Zero),
                2,
                "Series organizer",
                "series.organizer",
                "https://cdn.example/series.png"),
            new("Hanafi", "islamic-canary", 10, "Family", true, "Arabic"),
            new("https://github.com/example/repo", "tech-canary", "Beginner", [".NET"], true, true, 4, 1000m, "EUR"),
            new("#123456", "media-canary", "https://cdn.example/featured.png", "https://cdn.example/background.png"),
            ["event-category-canary"],
            ["event-tag-canary"],
            [location],
            [new(new(2026, 8, 1), "day-canary", "Day description", "Banner", "https://cdn.example/day.png", true, 1)],
            [session],
            [new("group-canary", "Group description", "group-slug", "#fff", 1, location, [new("session-canary", true, 1)])],
            [new(
                "event-agenda-canary",
                "Event agenda description",
                new(2026, 8, 1, 7, 0, 0, TimeSpan.Zero),
                new(2026, 8, 1, 7, 30, 0, TimeSpan.Zero),
                new(2026, 8, 1),
                new(2026, 8, 1),
                new(9, 0),
                new(9, 30),
                "Opening",
                1,
                location)],
            [customProperty],
            [new("https://event.example", "Event website")]);
    }

    private static AtprotoCustomPropertySnapshot CreateCustomProperty(string name, string value)
        => new(
            "public.namespace",
            $"{name}-key",
            name,
            "Public property",
            "Text",
            IsRequired: true,
            IsMultiValue: false,
            IsActive: true,
            SortOrder: 1,
            "Public",
            IsSearchable: true,
            IsFilterable: true,
            IsExportable: true,
            IsModerationRelevant: true,
            IsAnalyticsRelevant: true,
            IsSystemOwned: false,
            DefaultValue: "default-canary",
            MinimumLength: 1,
            MaximumLength: 100,
            Pattern: "pattern-canary",
            MinimumNumber: 1,
            MaximumNumber: 10,
            MinimumDateTime: new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            MaximumDateTime: new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero),
            AllowedUrlSchemes: "https",
            [new("public.namespace", "option-key", "option-canary", "Option description", "option-value", true, true, 1, null)],
            [new(1, "Text", value, null)]);
}

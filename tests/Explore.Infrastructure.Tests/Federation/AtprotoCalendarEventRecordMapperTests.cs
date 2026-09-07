using System.Collections.Immutable;
using CommunityLexicon.Calendar;
using CommunityLexicon.Location;
using Explore.Application.Contracts.LocationPrivacy;
using Explore.Application.Features.Federation.Atproto.Models;
using Explore.Infrastructure.Services.Federation;

namespace Explore.Infrastructure.Tests.Federation;

public sealed class AtprotoCalendarEventRecordMapperTests
{
    [Test]
    public async Task Map_UsesGeneratedNativeFields_AndPreservesNonnativeValuesInDescription()
    {
        AtprotoEventPublicationSnapshot snapshot = CreateSnapshot();

        Event record = AtprotoCalendarEventRecordMapper.Map(snapshot);
        AtprotoCalendarRecordValidationResult validation = AtprotoCalendarEventRecordValidator.Validate(record);

        await Assert.That(record.Name).IsEqualTo(snapshot.Name);
        await Assert.That(record.Mode).IsEqualTo("community.lexicon.calendar.event#hybrid");
        await Assert.That(record.Status).IsEqualTo("community.lexicon.calendar.event#scheduled");
        await Assert.That(record.Description).Contains("property-public-canary");
        await Assert.That(record.Description).Contains("session-public-canary");
        await Assert.That(record.Locations!.OfType<Address>()).Count().IsEqualTo(1);
        await Assert.That(record.Locations!.OfType<Geo>()).Count().IsEqualTo(1);
        await Assert.That(record.Locations!.OfType<EventUri>()).Count().IsEqualTo(1);
        await Assert.That(validation.IsValid).IsTrue();
        await Assert.That(validation.EncodedSize!.JsonBytes).IsGreaterThan(0);
        await Assert.That(validation.EncodedSize!.DagCborBytes).IsGreaterThan(0);
    }

    [Test]
    public async Task Validate_RejectsInvalidSemanticFieldsBeforeSizeEligibility()
    {
        Event record = AtprotoCalendarEventRecordMapper.Map(CreateSnapshot());
        record.Mode = "community.lexicon.calendar.event#invalid";
        record.Uris = [new EventUri { Uri = "https://user:secret@example.test/path#fragment", Name = "Unsafe" }];

        AtprotoCalendarRecordValidationResult result = AtprotoCalendarEventRecordValidator.Validate(record);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.EncodedSize).IsNull();
        await Assert.That(result.Errors).Contains(error => error.Contains("mode", StringComparison.Ordinal));
        await Assert.That(result.Errors).Contains(error => error.Contains("credentials", StringComparison.Ordinal));
    }

    [Test]
    public async Task Validate_LexiconMinimumNameAndCreatedAt_DoesNotRequireDescription()
    {
        Event record = AtprotoCalendarEventRecordMapper.Map(CreateSnapshot());
        record.Description = null;

        AtprotoCalendarRecordValidationResult result = AtprotoCalendarEventRecordValidator.Validate(record);

        await Assert.That(result.IsValid).IsTrue();
    }

    private static AtprotoEventPublicationSnapshot CreateSnapshot()
    {
        DateTimeOffset startsAt = new(2026, 7, 19, 9, 0, 0, TimeSpan.Zero);
        AtprotoEventLocationSnapshot location = new(
            EventLocationDisclosureState.Available,
            "BE",
            "Europe/Brussels",
            "Brussels",
            "Community Hall",
            "Room A",
            "Main room",
            "1 Test Street",
            "1000",
            50.8503,
            4.3517,
            "Community Hall, Brussels",
            "https://maps.example.test/hall",
            "u1516");
        AtprotoEventSessionSnapshot session = new(
            "session-1",
            "session-public-canary",
            "Session description",
            startsAt,
            startsAt.AddHours(1),
            DateOnly.FromDateTime(startsAt.UtcDateTime),
            DateOnly.FromDateTime(startsAt.UtcDateTime),
            new TimeOnly(9, 0),
            new TimeOnly(10, 0),
            "Fixed",
            1,
            "session-public-canary",
            "Talk",
            "Published",
            "Open",
            100,
            10,
            null,
            location,
            null,
            [],
            [],
            [],
            [],
            [],
            []);
        return new(
            "Mapped event",
            "Authored description",
            "Public content",
            startsAt.AddDays(-1),
            startsAt,
            startsAt.AddHours(2),
            "community.lexicon.calendar.event#hybrid",
            "community.lexicon.calendar.event#scheduled",
            true,
            new(
                "Subtitle",
                "Conference",
                "All",
                "All ages",
                12,
                "Public",
                null,
                "Europe/Brussels",
                1,
                "Open",
                "mapped-event",
                "EVENT-001",
                new DateOnly(2026, 7, 19),
                new DateOnly(2026, 7, 19),
                startsAt),
            new(
                "Organizer",
                "Organization",
                Handle: null,
                Description: null,
                OrganizationName: null,
                OrganizationWebsite: null,
                OrganizationCountry: null,
                OrganizationCity: null,
                GroupName: null,
                GroupDescription: null,
                ProfileImageUri: null,
                BannerImageUri: null,
                BackgroundImageUri: null,
                BackgroundColor: null,
                BackgroundEffect: null,
                BannerColor: null,
                GroupProfileImageUri: null),
            null,
            null,
            null,
            new(null, null, null, null),
            ["Category"],
            ["Tag"],
            [location],
            [],
            [session],
            [],
            [],
            [CreateCustomProperty()],
            [new("https://events.example.test/mapped-event", "Event page")]);
    }

    private static AtprotoCustomPropertySnapshot CreateCustomProperty()
        => new(
            "public.namespace",
            "property-key",
            "Property",
            "Definition",
            "Text",
            IsRequired: false,
            IsMultiValue: false,
            IsActive: true,
            SortOrder: 1,
            ExposureLevel: "Public",
            IsSearchable: true,
            IsFilterable: true,
            IsExportable: true,
            IsModerationRelevant: false,
            IsAnalyticsRelevant: false,
            IsSystemOwned: false,
            DefaultValue: null,
            MinimumLength: null,
            MaximumLength: null,
            Pattern: null,
            MinimumNumber: null,
            MaximumNumber: null,
            MinimumDateTime: null,
            MaximumDateTime: null,
            AllowedUrlSchemes: null,
            Options: [],
            Values: [new(0, "Text", "property-public-canary", null)]);
}

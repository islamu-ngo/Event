using Explore.API.Services.Calendar;
using Explore.Application.DTOs.Event;

namespace Event.Api.IntegrationTests.Features.Calendar;

public class IcalNetEventCalendarFileBuilderTests
{
    [Test]
    public async Task Build_WithEventExport_EmitsVEventWithStableFields()
    {
        var eventId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var export = new EventCalendarExportDto(
            eventId,
            "Community Iftar",
            "Bring a dish to share.",
            "community-iftar",
            new DateTimeOffset(2026, 5, 1, 18, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 1, 20, 0, 0, TimeSpan.Zero),
            "Main Hall, Brussels");
        var canonicalUrl = new Uri("https://events.example.org/events/11111111-2222-3333-4444-555555555555");

        var builder = new IcalNetEventCalendarFileBuilder();
        var content = builder.Build(export, canonicalUrl);
        var repeatedContent = builder.Build(export, canonicalUrl);

        await Assert.That(content).Contains("BEGIN:VCALENDAR");
        await Assert.That(content).Contains("VERSION:2.0");
        await Assert.That(content).Contains("BEGIN:VEVENT");
        await Assert.That(content).Contains($"UID:{eventId:D}");
        await Assert.That(content).Contains("SUMMARY:Community Iftar");
        await Assert.That(content).Contains("DTSTART:20260501T183000Z");
        await Assert.That(content).Contains("DTEND:20260501T200000Z");
        await Assert.That(content).Contains("LOCATION:Main Hall\\, Brussels");
        await Assert.That(content).Contains("URL:https://events.example.org/events/11111111-2222-3333-4444-555555555555");
        await Assert.That(repeatedContent).IsEqualTo(content);
    }

    [Test]
    [Category("CalendarPrivacy")]
    public async Task Build_WithAttendeeExport_EmitsAuthorizedExactLocation()
    {
        var export = new AttendeeEventCalendarExportDto(
            Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff"),
            "Attendee Calendar",
            "Registration-scoped description.",
            "attendee-calendar",
            new DateTimeOffset(2026, 7, 19, 16, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 19, 17, 0, 0, TimeSpan.Zero),
            "Private Home, Family Room, 17 Confidential Crescent, SECRET-1040");
        var canonicalUrl = new Uri("https://events.example.org/events/bbbbbbbb-cccc-dddd-eeee-ffffffffffff");

        string content = new IcalNetEventCalendarFileBuilder().Build(export, canonicalUrl);
        string unfoldedContent = content.Replace("\r\n ", string.Empty, StringComparison.Ordinal);

        await Assert.That(unfoldedContent)
            .Contains("LOCATION:Private Home\\, Family Room\\, 17 Confidential Crescent\\, SECRET-1040");
    }
}

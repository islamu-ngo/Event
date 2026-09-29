using Explore.Application.DTOs.Event;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;

namespace Explore.API.Services.Calendar;

public sealed class IcalNetEventCalendarFileBuilder : IEventCalendarFileBuilder
{
    private const string ProductIdentifier = "-//ISLAMU//Event Platform//EN";

    public string Build(EventCalendarExportDto calendarExport, Uri publicUrl)
    {
        ArgumentNullException.ThrowIfNull(calendarExport);
        ArgumentNullException.ThrowIfNull(publicUrl);

        return BuildCalendar(
            calendarExport.EventId,
            calendarExport.Title,
            calendarExport.Description,
            calendarExport.StartsAtUtc,
            calendarExport.EndsAtUtc,
            calendarExport.Location,
            publicUrl);
    }

    public string Build(AttendeeEventCalendarExportDto calendarExport, Uri publicUrl)
    {
        ArgumentNullException.ThrowIfNull(calendarExport);
        ArgumentNullException.ThrowIfNull(publicUrl);

        return BuildCalendar(
            calendarExport.EventId,
            calendarExport.Title,
            calendarExport.Description,
            calendarExport.StartsAtUtc,
            calendarExport.EndsAtUtc,
            calendarExport.Location,
            publicUrl);
    }

    private static string BuildCalendar(
        Guid eventId,
        string title,
        string? description,
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc,
        string? location,
        Uri publicUrl)
    {
        var calendar = new Ical.Net.Calendar
        {
            ProductId = ProductIdentifier,
            Method = "PUBLISH"
        };

        var calendarEvent = new CalendarEvent
        {
            Summary = title,
            Description = BuildDescription(title, description, publicUrl),
            DtStart = new CalDateTime(startsAtUtc.UtcDateTime),
            DtEnd = new CalDateTime(endsAtUtc.UtcDateTime),
            DtStamp = new CalDateTime(startsAtUtc.UtcDateTime),
            Uid = eventId.ToString("D"),
            Url = publicUrl
        };

        if (!string.IsNullOrWhiteSpace(location))
        {
            calendarEvent.Location = location;
        }

        calendar.Events.Add(calendarEvent);

        var serializer = new CalendarSerializer();
        return serializer.SerializeToString(calendar)
            ?? throw new InvalidOperationException("iCalendar serialization returned no content.");
    }

    private static string BuildDescription(string title, string? description, Uri publicUrl)
    {
        string value = string.IsNullOrWhiteSpace(description)
            ? title
            : description.Trim();

        return $"{value}{Environment.NewLine}{Environment.NewLine}{publicUrl}";
    }
}

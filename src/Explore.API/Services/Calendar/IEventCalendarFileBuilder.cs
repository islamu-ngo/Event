using Explore.Application.DTOs.Event;

namespace Explore.API.Services.Calendar;

public interface IEventCalendarFileBuilder
{
    string Build(EventCalendarExportDto calendarExport, Uri canonicalUrl);
    string Build(AttendeeEventCalendarExportDto calendarExport, Uri canonicalUrl);
}

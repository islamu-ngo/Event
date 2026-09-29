using Explore.Application.DTOs.Event;

namespace Explore.API.Services.Calendar;

public interface IEventCalendarFileBuilder
{
    string Build(EventCalendarExportDto calendarExport, Uri publicUrl);
    string Build(AttendeeEventCalendarExportDto calendarExport, Uri publicUrl);
}

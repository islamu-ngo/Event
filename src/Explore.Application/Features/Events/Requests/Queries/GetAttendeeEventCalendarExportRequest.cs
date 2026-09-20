using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Event;

namespace Explore.Application.Features.Events.Requests.Queries;

public sealed record GetAttendeeEventCalendarExportRequest(Guid EventId)
    : IQuery<AttendeeEventCalendarExportDto?>;

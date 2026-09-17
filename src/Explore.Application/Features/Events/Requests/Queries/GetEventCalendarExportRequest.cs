using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Event;

namespace Explore.Application.Features.Events.Requests.Queries;

public sealed record GetEventCalendarExportRequest(Guid EventId) : IQuery<EventCalendarExportDto?>;

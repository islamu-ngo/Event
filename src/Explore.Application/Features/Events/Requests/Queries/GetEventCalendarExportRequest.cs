using Explore.Application.DTOs.Event;
using MediatR;

namespace Explore.Application.Features.Events.Requests.Queries;

public sealed record GetEventCalendarExportRequest(Guid EventId) : IRequest<EventCalendarExportDto?>;

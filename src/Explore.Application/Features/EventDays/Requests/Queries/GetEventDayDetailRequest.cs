using Explore.Application.DTOs.EventDay;
using MediatR;

namespace Explore.Application.Features.EventDays.Requests.Queries;

public sealed record GetEventDayDetailRequest(Guid Id) : IRequest<EventDayDto?>;

using Explore.Application.DTOs.EventDay;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventDays.Requests.Queries;

public sealed record GetEventDayDetailRequest(Guid Id) : IQuery<EventDayDto?>;

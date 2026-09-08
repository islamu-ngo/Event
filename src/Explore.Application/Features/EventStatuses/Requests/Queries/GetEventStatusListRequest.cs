using System.Collections.Generic;
using Explore.Application.DTOs.EventStatus;
using MediatR;

namespace Explore.Application.Features.EventStatuses.Requests.Queries;

public sealed record GetEventStatusListRequest : IRequest<List<EventStatusListDto>>
{
}

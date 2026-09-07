using Explore.Application.DTOs.EventStatus;
using MediatR;

namespace Explore.Application.Features.EventStatuses.Requests.Queries;

public sealed record GetEventStatusDetailsRequest(int Id = default) : IRequest<EventStatusDto>;

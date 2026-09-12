using Explore.Application.DTOs.EventStatus;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventStatuses.Requests.Queries;

public sealed record GetEventStatusDetailsRequest(int Id = default) : IQuery<EventStatusDto?>;

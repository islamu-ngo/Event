using Explore.Application.DTOs.GroupPosition;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.GroupPositions.Requests.Queries;

public sealed record GetGroupPositionDetailsRequest(int Id = default) : IQuery<GroupPositionDto?>;

using Explore.Application.DTOs.Group;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Groups.Requests.Queries;

public sealed record GetGroupListRequest : IQuery<PaginatedResult<GroupListDto>>
{
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

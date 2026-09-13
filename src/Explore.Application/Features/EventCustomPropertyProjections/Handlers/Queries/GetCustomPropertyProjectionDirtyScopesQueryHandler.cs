using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.CustomPropertyProjection;
using Explore.Application.Features.EventCustomPropertyProjections.Requests.Queries;
using Explore.Application.Mappings;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventCustomPropertyProjections.Handlers.Queries;

public class GetCustomPropertyProjectionDirtyScopesQueryHandler
    : IQueryHandler<GetCustomPropertyProjectionDirtyScopesQuery, PaginatedResult<ProjectionDirtyScopeDto>>
{
    private readonly ICustomPropertyProjectionDirtyScopeRepository _dirtyScopeRepository;

    public GetCustomPropertyProjectionDirtyScopesQueryHandler(
        ICustomPropertyProjectionDirtyScopeRepository dirtyScopeRepository)
    {
        _dirtyScopeRepository = dirtyScopeRepository;
    }

    public async Task<PaginatedResult<ProjectionDirtyScopeDto>> QueryAsync(
        GetCustomPropertyProjectionDirtyScopesQuery request,
        CancellationToken cancellationToken)
    {
        var (pageNumber, pageSize) = PaginatedResult<ProjectionDirtyScopeDto>
            .NormalizeParameters(request.PageNumber, request.PageSize);

        var pendingCount = await _dirtyScopeRepository.CountPendingAsync(
            request.ProjectionName,
            1,
            request.TenantId,
            cancellationToken);

        var items = await _dirtyScopeRepository.GetPendingAsync(
            request.ProjectionName,
            1,
            request.TenantId,
            pageSize,
            cancellationToken,
            skip: (int)Math.Min((long)(pageNumber - 1) * pageSize, int.MaxValue));

        var dtos = items.Select(CustomPropertyProjectionMapper.ToDirtyScope).ToList();

        return PaginatedResult<ProjectionDirtyScopeDto>.Create(dtos, pendingCount, pageNumber, pageSize);
    }
}

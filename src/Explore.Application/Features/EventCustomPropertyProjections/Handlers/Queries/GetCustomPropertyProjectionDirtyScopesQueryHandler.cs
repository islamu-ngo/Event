using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.CustomPropertyProjection;
using Explore.Application.Features.EventCustomPropertyProjections.Requests.Queries;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventCustomPropertyProjections.Handlers.Queries;

public class GetCustomPropertyProjectionDirtyScopesQueryHandler
    : IRequestHandler<GetCustomPropertyProjectionDirtyScopesQuery, PaginatedResult<ProjectionDirtyScopeDto>>
{
    private readonly ICustomPropertyProjectionDirtyScopeRepository _dirtyScopeRepository;
    private readonly IMapper _mapper;

    public GetCustomPropertyProjectionDirtyScopesQueryHandler(
        ICustomPropertyProjectionDirtyScopeRepository dirtyScopeRepository,
        IMapper mapper)
    {
        _dirtyScopeRepository = dirtyScopeRepository;
        _mapper = mapper;
    }

    public async Task<PaginatedResult<ProjectionDirtyScopeDto>> Handle(
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
            cancellationToken);

        var dtos = _mapper.Map<List<ProjectionDirtyScopeDto>>(items);

        return PaginatedResult<ProjectionDirtyScopeDto>.Create(dtos, pendingCount, pageNumber, pageSize);
    }
}

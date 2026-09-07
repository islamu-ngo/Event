using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.CustomPropertyProjection;
using Explore.Application.Features.CustomProperties;
using Explore.Application.Features.EventCustomPropertyProjections.Requests.Queries;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventCustomPropertyProjections.Handlers.Queries;

public class GetEventCustomPropertyProjectionStatusQueryHandler
    : IRequestHandler<GetEventCustomPropertyProjectionStatusQuery, BaseCommandResponse<IReadOnlyList<ProjectionStatusDto>>>
{
    private readonly ICustomPropertyProjectionStatusRepository _statusRepository;
    private readonly ICustomPropertyProjectionDirtyScopeRepository _dirtyScopeRepository;
    private readonly IMapper _mapper;

    public GetEventCustomPropertyProjectionStatusQueryHandler(
        ICustomPropertyProjectionStatusRepository statusRepository,
        ICustomPropertyProjectionDirtyScopeRepository dirtyScopeRepository,
        IMapper mapper)
    {
        _statusRepository = statusRepository;
        _dirtyScopeRepository = dirtyScopeRepository;
        _mapper = mapper;
    }

    public async Task<BaseCommandResponse<IReadOnlyList<ProjectionStatusDto>>> Handle(
        GetEventCustomPropertyProjectionStatusQuery request,
        CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty)
        {
            return BaseCommandResponse.Validation<IReadOnlyList<ProjectionStatusDto>>(
                ["TenantId is required."],
                "TenantId is required.");
        }

        var status = await _statusRepository.GetAsync(
            IEventCustomPropertyProjectionUpdater.ProjectionName,
            IEventCustomPropertyProjectionUpdater.ProjectionVersion,
            request.TenantId,
            cancellationToken);

        var dtos = new List<ProjectionStatusDto>();
        if (status is not null)
        {
            var dto = _mapper.Map<ProjectionStatusDto>(status);
            var pendingDirtyScopes = await _dirtyScopeRepository.CountPendingAsync(
                IEventCustomPropertyProjectionUpdater.ProjectionName,
                IEventCustomPropertyProjectionUpdater.ProjectionVersion,
                request.TenantId,
                cancellationToken);

            CustomPropertyProjectionStatusSignals.Apply(dto, pendingDirtyScopes, DateTimeOffset.UtcNow);
            dtos.Add(dto);
        }

        return BaseCommandResponse.Success<IReadOnlyList<ProjectionStatusDto>>(
            dtos,
            "Projection status retrieved.");
    }
}

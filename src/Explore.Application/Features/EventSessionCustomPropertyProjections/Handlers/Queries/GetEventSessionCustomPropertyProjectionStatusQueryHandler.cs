using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.CustomPropertyProjection;
using Explore.Application.Features.CustomProperties;
using Explore.Application.Features.EventSessionCustomPropertyProjections.Requests.Queries;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventSessionCustomPropertyProjections.Handlers.Queries;

public class GetEventSessionCustomPropertyProjectionStatusQueryHandler
    : IRequestHandler<GetEventSessionCustomPropertyProjectionStatusQuery, BaseCommandResponse<IReadOnlyList<ProjectionStatusDto>>>
{
    private readonly ICustomPropertyProjectionStatusRepository _statusRepository;
    private readonly ICustomPropertyProjectionDirtyScopeRepository _dirtyScopeRepository;
    private readonly IMapper _mapper;

    public GetEventSessionCustomPropertyProjectionStatusQueryHandler(
        ICustomPropertyProjectionStatusRepository statusRepository,
        ICustomPropertyProjectionDirtyScopeRepository dirtyScopeRepository,
        IMapper mapper)
    {
        _statusRepository = statusRepository;
        _dirtyScopeRepository = dirtyScopeRepository;
        _mapper = mapper;
    }

    public async Task<BaseCommandResponse<IReadOnlyList<ProjectionStatusDto>>> Handle(
        GetEventSessionCustomPropertyProjectionStatusQuery request,
        CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty)
        {
            return BaseCommandResponse.Validation<IReadOnlyList<ProjectionStatusDto>>(
                ["TenantId is required."],
                "TenantId is required.");
        }

        var status = await _statusRepository.GetAsync(
            IEventSessionCustomPropertyProjectionUpdater.ProjectionName,
            IEventSessionCustomPropertyProjectionUpdater.ProjectionVersion,
            request.TenantId,
            cancellationToken);

        var dtos = new List<ProjectionStatusDto>();
        if (status is not null)
        {
            var dto = _mapper.Map<ProjectionStatusDto>(status);
            var pendingDirtyScopes = await _dirtyScopeRepository.CountPendingAsync(
                IEventSessionCustomPropertyProjectionUpdater.ProjectionName,
                IEventSessionCustomPropertyProjectionUpdater.ProjectionVersion,
                request.TenantId,
                cancellationToken);

            CustomPropertyProjectionStatusSignals.Apply(dto, pendingDirtyScopes, DateTimeOffset.UtcNow);
            dtos.Add(dto);
        }

        return BaseCommandResponse.Success<IReadOnlyList<ProjectionStatusDto>>(
            dtos,
            "Session projection status retrieved.");
    }
}

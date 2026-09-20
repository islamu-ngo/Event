using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.CustomPropertyProjection;
using Explore.Application.Features.CustomProperties;
using Explore.Application.Features.EventSessionCustomPropertyProjections.Requests.Queries;
using Explore.Application.Mappings;
using Explore.Application.Responses;

namespace Explore.Application.Features.EventSessionCustomPropertyProjections.Handlers.Queries;

public class GetEventSessionCustomPropertyProjectionStatusQueryHandler
    : IQueryHandler<GetEventSessionCustomPropertyProjectionStatusQuery, BaseCommandResponse<IReadOnlyList<ProjectionStatusDto>>>
{
    private readonly ICustomPropertyProjectionStatusRepository _statusRepository;
    private readonly ICustomPropertyProjectionDirtyScopeRepository _dirtyScopeRepository;

    public GetEventSessionCustomPropertyProjectionStatusQueryHandler(
        ICustomPropertyProjectionStatusRepository statusRepository,
        ICustomPropertyProjectionDirtyScopeRepository dirtyScopeRepository)
    {
        _statusRepository = statusRepository;
        _dirtyScopeRepository = dirtyScopeRepository;
    }

    public async Task<BaseCommandResponse<IReadOnlyList<ProjectionStatusDto>>> QueryAsync(
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
            var dto = CustomPropertyProjectionMapper.ToStatus(status);
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

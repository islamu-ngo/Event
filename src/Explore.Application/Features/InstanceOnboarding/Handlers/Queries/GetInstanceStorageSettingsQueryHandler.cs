using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Queries;

public class GetInstanceStorageSettingsQueryHandler : IQueryHandler<GetInstanceStorageSettingsQuery, InstanceStorageSettingsDto>
{
    private readonly IInstanceStorageSettingService _storageSettingService;

    public GetInstanceStorageSettingsQueryHandler(IInstanceStorageSettingService storageSettingService)
    {
        _storageSettingService = storageSettingService;
    }

    public async Task<InstanceStorageSettingsDto> QueryAsync(GetInstanceStorageSettingsQuery request, CancellationToken cancellationToken)
    {
        return await _storageSettingService.ReadSettingsAsync(cancellationToken);
    }
}

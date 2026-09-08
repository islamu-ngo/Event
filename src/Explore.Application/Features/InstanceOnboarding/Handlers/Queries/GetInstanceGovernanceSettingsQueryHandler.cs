using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Instance;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Queries;

public class GetInstanceGovernanceSettingsQueryHandler : IRequestHandler<GetInstanceGovernanceSettingsQuery, InstanceGovernanceSettings>
{
    private readonly IInstanceGovernanceSettingService _governanceSettingService;

    public GetInstanceGovernanceSettingsQueryHandler(IInstanceGovernanceSettingService governanceSettingService)
    {
        _governanceSettingService = governanceSettingService;
    }

    public async Task<InstanceGovernanceSettings> Handle(GetInstanceGovernanceSettingsQuery request, CancellationToken cancellationToken)
    {
        return await _governanceSettingService.ReadSettingsAsync();
    }
}

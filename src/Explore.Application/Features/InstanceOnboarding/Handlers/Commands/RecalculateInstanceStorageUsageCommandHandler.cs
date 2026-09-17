using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Commands;

public sealed class RecalculateInstanceStorageUsageCommandHandler : ICommandHandler<RecalculateInstanceStorageUsageCommand, InstanceStorageUsageDto>
{
    private readonly IInstanceStorageSettingService _storageSettingService;

    public RecalculateInstanceStorageUsageCommandHandler(IInstanceStorageSettingService storageSettingService)
    {
        _storageSettingService = storageSettingService;
    }

    public async Task<InstanceStorageUsageDto> ExecuteAsync(RecalculateInstanceStorageUsageCommand request, CancellationToken cancellationToken)
    {
        return await _storageSettingService.RecalculateUsageAsync(cancellationToken);
    }
}

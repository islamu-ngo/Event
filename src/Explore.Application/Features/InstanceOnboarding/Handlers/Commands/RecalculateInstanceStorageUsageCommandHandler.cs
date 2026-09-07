using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Commands;

public sealed class RecalculateInstanceStorageUsageCommandHandler : IRequestHandler<RecalculateInstanceStorageUsageCommand, InstanceStorageUsageDto>
{
    private readonly IInstanceStorageSettingService _storageSettingService;

    public RecalculateInstanceStorageUsageCommandHandler(IInstanceStorageSettingService storageSettingService)
    {
        _storageSettingService = storageSettingService;
    }

    public async Task<InstanceStorageUsageDto> Handle(RecalculateInstanceStorageUsageCommand request, CancellationToken cancellationToken)
    {
        return await _storageSettingService.RecalculateUsageAsync(cancellationToken);
    }
}

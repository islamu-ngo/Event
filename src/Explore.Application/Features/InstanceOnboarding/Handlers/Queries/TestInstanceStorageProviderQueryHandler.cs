using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Queries;

public sealed class TestInstanceStorageProviderQueryHandler :
    IQueryHandler<TestInstanceStorageProviderQuery, InstanceStorageProviderStatusDto>,
    IQueryHandler<TestInstanceStorageProviderDuringSetupQuery, InstanceStorageProviderStatusDto>
{
    private readonly IInstanceStorageSettingService _storageSettingService;

    public TestInstanceStorageProviderQueryHandler(IInstanceStorageSettingService storageSettingService)
    {
        _storageSettingService = storageSettingService;
    }

    public async Task<InstanceStorageProviderStatusDto> QueryAsync(TestInstanceStorageProviderQuery request, CancellationToken cancellationToken)
    {
        return await _storageSettingService.TestProviderAsync(request, cancellationToken);
    }

    public Task<InstanceStorageProviderStatusDto> QueryAsync(
        TestInstanceStorageProviderDuringSetupQuery request, CancellationToken cancellationToken) =>
        _storageSettingService.TestProviderAsync(request, cancellationToken);
}

using Explore.Application.DTOs.Instance;
using Explore.Application.DTOs.Onboarding;

namespace Explore.Application.Contracts.Services;

public interface IResolverConfigService
{
    Task<ResolverConfigurationDto> GetConfigurationAsync(CancellationToken cancellationToken = default);

    Task ApplyConfigurationAsync(
        PatchResolverConfigurationDto patch,
        ResolverConfigurationDto configuration,
        Guid? actorUserId,
        CancellationToken cancellationToken = default);

    void InvalidateCache();
}

using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Responses;

namespace Explore.Application.Contracts.Services;

public interface IResolverConfigService
{
    Task<ResolverConfigurationDto> GetConfigurationAsync(CancellationToken cancellationToken = default);

    Task<BaseCommandResponse<Guid>> ApplyConfigurationAsync(
        UpdateResolverConfigurationCommand command,
        CancellationToken cancellationToken = default);

    void InvalidateCache();
}

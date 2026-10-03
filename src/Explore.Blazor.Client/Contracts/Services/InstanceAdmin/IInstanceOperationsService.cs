using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.InstanceAdmin;

public interface IInstanceOperationsService
{
    Task<HalResourceOfInstanceOperationsDto> GetOperationsAsync(CancellationToken cancellationToken = default);

    Task<HalResourceOfInstanceDeploymentModeRunbookDto> GetDeploymentModeRunbookAsync(
        CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfInstanceDeploymentModeTransitionDto> TransitionDeploymentModeAsync(
        string targetMode,
        string confirmationText,
        string? reason = null,
        CancellationToken cancellationToken = default);
}

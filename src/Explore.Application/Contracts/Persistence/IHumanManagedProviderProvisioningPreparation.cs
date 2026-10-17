using Explore.Application.Features.ManagedProviderProvisioning.Requests.Commands;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Retains the original human provisioning request before enrollment opens its owner.
/// Managed outbox workers have separate authority and never consume this port.
/// </summary>
public interface IHumanManagedProviderProvisioningPreparation
{
    Task<IPreparedHumanManagedProviderProvisioning> PrepareAsync(
        EnsureManagedProviderClientProvisionedCommand request, Guid administrator,
        CancellationToken cancellationToken = default);
}

public interface IPreparedHumanManagedProviderProvisioning
{
    Task<IHumanManagedProviderProvisioningData> AcquireAsync(EnsureManagedProviderClientProvisionedCommand originalRequest,
        CancellationToken cancellationToken = default);
}


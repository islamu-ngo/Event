using Explore.Application.Features.Management;
using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Noncredential observations for scheduling, not transport or worker execution authority.</summary>
public interface IManagedTenantProvisioningSchedulingSource : IManagedTenantProvisioningPreflightSource
{
    Task<IAsyncDisposable> RetainAdministratorAsync(Guid? localSubject, CancellationToken cancellationToken);
    Task RequireCurrentAsync(CancellationToken cancellationToken);
    Task<DeploymentMode> ReadDeploymentModeAsync(CancellationToken cancellationToken);
    Task<ManagedTenantProvisioningRegistrationPolicy.Facts?> ReadRegistrationFactsAsync(CancellationToken cancellationToken);
    Task<TenantActivationCapacityAssessment> AssessCapacityAsync(bool requireMultiTenant, CancellationToken cancellationToken);
    Task<ManagedTenantProvisioningBinding?> ReadCustomerBindingAsync(string customer, CancellationToken cancellationToken);
    Task<ManagedTenantProvisioningBinding?> ReadOperationBindingAsync(Guid operation, Guid tenant, CancellationToken cancellationToken);
    Task<bool> TenantMatchesSlugAsync(Guid tenant, string slug, CancellationToken cancellationToken);
}

public sealed record ManagedTenantProvisioningBinding(string InternalType, Guid InternalId);

using Explore.Application.Contracts.Persistence;
using Explore.Application.Management;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.Extensions.Options;

namespace Explore.Application.Features.Management;

public sealed class TenantActivationCapacityPolicy(
    IInstanceBootstrapStateRepository bootstrapStateRepository,
    ITenantRepository tenantRepository,
    IManagedTenantProvisioningOperationRepository operationRepository,
    IOptions<ManagedControlPlaneOptions> options)
{
    public bool IsEnforced => options.Value.Enabled;

    public async Task<TenantActivationCapacityAssessment> EvaluateAsync(
        bool requireMultiTenant,
        Guid? excludedReservationOperationId = null,
        DeploymentMode? knownPersistedMode = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnforced)
        {
            return TenantActivationCapacityAssessment.Unmanaged;
        }

        DeploymentMode mode = knownPersistedMode
            ?? ResolvePersistedMode(await bootstrapStateRepository.GetCurrent(cancellationToken));
        int maximum = mode == DeploymentMode.SingleTenant ? 1 : options.Value.MaximumTenantCount;

        if (requireMultiTenant && mode != DeploymentMode.MultiTenant)
        {
            return new TenantActivationCapacityAssessment(
                false,
                mode,
                maximum,
                0,
                0,
                0,
                "tenant_provisioning_requires_multi_tenant",
                "Managed tenant provisioning is unavailable in SingleTenant mode.");
        }

        if (maximum == 0)
        {
            return new TenantActivationCapacityAssessment(
                false,
                mode,
                maximum,
                0,
                0,
                0,
                "tenant_provisioning_capacity_not_configured",
                "Managed tenant provisioning capacity is not configured.");
        }

        int active = await tenantRepository.GetActiveTenantCountAsync();
        int reserved = await operationRepository.CountActiveReservationsAsync(
            cancellationToken,
            excludedReservationOperationId);
        int available = Math.Max(0, maximum - active - reserved);

        return available > 0
            ? new TenantActivationCapacityAssessment(true, mode, maximum, active, reserved, available, null, null)
            : new TenantActivationCapacityAssessment(
                false,
                mode,
                maximum,
                active,
                reserved,
                available,
                "tenant_provisioning_capacity_exhausted",
                "This Event instance has no available tenant capacity.");
    }

    private static DeploymentMode ResolvePersistedMode(InstanceBootstrapState? bootstrap) =>
        bootstrap?.Status == InstanceBootstrapStatus.Completed
            ? bootstrap.DeploymentMode
            : DeploymentMode.SingleTenant;
}

public sealed record TenantActivationCapacityAssessment(
    bool Allowed,
    DeploymentMode Mode,
    int Maximum,
    int Active,
    int Reserved,
    int Available,
    string? FailureCode,
    string? Error)
{
    public static TenantActivationCapacityAssessment Unmanaged { get; } =
        new(true, DeploymentMode.SingleTenant, int.MaxValue, 0, 0, int.MaxValue, null, null);
}

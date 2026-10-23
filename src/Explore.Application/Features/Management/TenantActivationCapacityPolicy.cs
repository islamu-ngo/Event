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

        DeploymentMode? mode = knownPersistedMode
            ?? ResolvePersistedMode(await bootstrapStateRepository.GetCurrent(cancellationToken));
        int maximumTenantCount = options.Value.MaximumTenantCount;
        var configuration = Assess(true, requireMultiTenant, mode, maximumTenantCount, 0, 0);
        if (configuration.FailureCode is "tenant_provisioning_requires_multi_tenant"
            or "tenant_provisioning_capacity_not_configured")
            return configuration;

        int active = await tenantRepository.GetActiveTenantCountAsync();
        int reserved = await operationRepository.CountActiveReservationsAsync(
            cancellationToken,
            excludedReservationOperationId);

        return Assess(true, requireMultiTenant, mode, maximumTenantCount, active, reserved);
    }

    /// <summary>
    /// Assesses capacity from the caller's retained source image. A missing completed
    /// deployment mode uses the single-tenant limit; reservations consume capacity.
    /// </summary>
    public static TenantActivationCapacityAssessment Assess(bool isEnforced, bool requireMultiTenant,
        DeploymentMode? persistedMode, int maximumTenantCount, int active, int reserved)
    {
        if (!isEnforced)
            return TenantActivationCapacityAssessment.Unmanaged;

        DeploymentMode mode = persistedMode ?? DeploymentMode.SingleTenant;
        int maximum = mode == DeploymentMode.SingleTenant ? 1 : maximumTenantCount;

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

    private static DeploymentMode? ResolvePersistedMode(InstanceBootstrapState? bootstrap) =>
        bootstrap?.Status == InstanceBootstrapStatus.Completed
            ? bootstrap.DeploymentMode
            : null;
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

using Explore.Application.DTOs.Management;
using Explore.Application.Management;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Features.Management;

public static class ManagedTenantProvisioningRegistrationPolicy
{
    public static ManagementTenantProvisioningBlockerDto? Evaluate(
        ManagedControlPlaneRegistration? registration,
        Guid managedInstanceId,
        DeploymentMode deploymentMode)
    {
        if (registration?.Status != ManagedControlPlaneRegistrationStatus.Registered
            || registration.ManagedInstanceId != managedInstanceId)
        {
            return new ManagementTenantProvisioningBlockerDto(
                "managed_registration_unavailable",
                "An active matching managed Control Plane registration is required.");
        }

        if (!string.Equals(
                registration.ManagementApiVersion,
                ManagedControlPlaneContract.ManagementApiVersion,
                StringComparison.Ordinal))
        {
            return new ManagementTenantProvisioningBlockerDto(
                "managed_registration_api_incompatible",
                "The registered management API version is incompatible with this Event instance.");
        }

        return registration.DeploymentMode != deploymentMode
            ? new ManagementTenantProvisioningBlockerDto(
                "managed_registration_mode_incompatible",
                "The managed registration deployment mode does not match this Event instance.")
            : null;
    }
}

using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Management;

namespace Explore.Application.Features.Management.Requests.Queries;

public sealed record GetManagementCapabilitiesQuery : IQuery<ManagementCapabilitiesDto>;

public sealed record GetManagedEventInstanceStatusQuery : IQuery<ManagedEventInstanceStatusDto?>;

public sealed record GetManagementHealthQuery : IQuery<ManagementHealthDto>;

public sealed record GetManagementUpgradePreflightQuery(
    string TargetEventVersion,
    string TargetManagementApiVersion) : IQuery<ManagementUpgradePreflightDto>;

public sealed record GetManagementUpgradePostflightQuery(
    string ExpectedEventVersion,
    string ExpectedManagementApiVersion) : IQuery<ManagementUpgradePostflightDto>;

public sealed record GetManagedTenantProvisioningPreflightQuery(
    Guid ManagedInstanceId,
    ManagementTenantProvisioningRequestDto Request)
    : IQuery<ManagementTenantProvisioningPreflightDto>;

public sealed record GetManagedTenantProvisioningOperationQuery(
    Guid ManagedInstanceId,
    Guid OperationId) : IQuery<ManagementTenantProvisioningOperationDto?>;

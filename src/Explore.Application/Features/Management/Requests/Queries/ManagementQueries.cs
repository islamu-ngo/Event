using Explore.Application.DTOs.Management;
using MediatR;

namespace Explore.Application.Features.Management.Requests.Queries;

public sealed record GetManagementCapabilitiesQuery : IRequest<ManagementCapabilitiesDto>;

public sealed record GetManagedEventInstanceStatusQuery : IRequest<ManagedEventInstanceStatusDto?>;

public sealed record GetManagementHealthQuery : IRequest<ManagementHealthDto>;

public sealed record GetManagementUpgradePreflightQuery(
    string TargetEventVersion,
    string TargetManagementApiVersion) : IRequest<ManagementUpgradePreflightDto>;

public sealed record GetManagementUpgradePostflightQuery(
    string ExpectedEventVersion,
    string ExpectedManagementApiVersion) : IRequest<ManagementUpgradePostflightDto>;

public sealed record GetManagedTenantProvisioningPreflightQuery(
    Guid ManagedInstanceId,
    ManagementTenantProvisioningRequestDto Request)
    : IRequest<ManagementTenantProvisioningPreflightDto>;

public sealed record GetManagedTenantProvisioningOperationQuery(
    Guid ManagedInstanceId,
    Guid OperationId) : IRequest<ManagementTenantProvisioningOperationDto?>;

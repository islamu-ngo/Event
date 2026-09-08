using Explore.Application.DTOs.Management;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Management.Requests.Commands;

public sealed record TriggerManagedControlPlaneRegistrationCommand
    : IRequest<TriggerManagedRegistrationResultDto>;

public sealed record RotateManagedControlPlaneCredentialCommand(
    RotateManagedControlPlaneCredentialRequestDto Request) : IRequest<bool>;

public sealed record RevokeManagedControlPlaneRegistrationCommand : IRequest<bool>;

public sealed record ScheduleManagedTenantProvisioningCommand(
    Guid ManagedInstanceId,
    ManagementTenantProvisioningRequestDto Request)
    : IRequest<BaseCommandResponse<ManagementTenantProvisioningOperationDto>>;

public sealed record CancelManagedTenantProvisioningOperationCommand(
    Guid ManagedInstanceId,
    Guid OperationId) : IRequest<BaseCommandResponse<ManagementTenantProvisioningOperationDto>>;

public sealed record ProcessManagedTenantProvisioningOperationCommand(
    Guid OperationId,
    Guid OutboxMessageId) : IRequest<bool>;

public sealed record ReconcileManagedTenantProvisioningDeadLetterCommand(
    Guid OperationId,
    Guid OutboxMessageId) : IRequest<bool>;

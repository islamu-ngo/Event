using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Management;
using Explore.Application.Responses;

namespace Explore.Application.Features.Management.Requests.Commands;

public sealed record TriggerManagedControlPlaneRegistrationCommand
    : ICommand<TriggerManagedRegistrationResultDto>;

public sealed record RotateManagedControlPlaneCredentialCommand(
    RotateManagedControlPlaneCredentialRequestDto Request) : ICommand<bool>;

public sealed record RevokeManagedControlPlaneRegistrationCommand : ICommand<bool>;

public sealed record ScheduleManagedTenantProvisioningCommand(
    Guid ManagedInstanceId,
    ManagementTenantProvisioningRequestDto Request)
    : ICommand<BaseCommandResponse<ManagementTenantProvisioningOperationDto>>;

public sealed record CancelManagedTenantProvisioningOperationCommand(
    Guid ManagedInstanceId,
    Guid OperationId) : ICommand<BaseCommandResponse<ManagementTenantProvisioningOperationDto>>;

public sealed record ProcessManagedTenantProvisioningOperationCommand(
    Guid OperationId,
    Guid OutboxMessageId) : ICommand<bool>;

public sealed record ReconcileManagedTenantProvisioningDeadLetterCommand(
    Guid OperationId,
    Guid OutboxMessageId) : ICommand<bool>;

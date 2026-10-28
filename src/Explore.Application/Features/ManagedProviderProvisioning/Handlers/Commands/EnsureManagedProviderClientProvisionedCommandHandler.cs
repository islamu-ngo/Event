using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.ManagedProviderProvisioning;
using Explore.Application.DTOs.ManagedProviderProvisioning.Validators;
using Explore.Application.DTOs.Management;
using Explore.Application.DTOs.Management.Validators;
using Explore.Application.Features.ManagedProviderProvisioning.Requests.Commands;
using Explore.Application.Features.Management;
using Explore.Application.Responses;

namespace Explore.Application.Features.ManagedProviderProvisioning.Handlers.Commands;

/// <summary>Separates original human provisioning from admitted durable worker creation.</summary>
public sealed class EnsureManagedProviderClientProvisionedCommandHandler(
    HumanManagedProviderProvisioningCoordinator humanProvisioning,
    IManagedTenantWorkerCreationPort workerCreation)
    : ICommandHandler<EnsureManagedProviderClientProvisionedCommand, BaseCommandResponse<ManagedProviderClientProvisioningResultDto>>,
      IManagedProviderClientProvisioner
{
    public Task<BaseCommandResponse<ManagedProviderClientProvisioningResultDto>> ExecuteAsync(
        EnsureManagedProviderClientProvisionedCommand command, CancellationToken cancellationToken) =>
        command.ManagementRequest is null
            ? humanProvisioning.ExecuteAsync(command, cancellationToken)
            : EnsureAsync(command.ProvisioningDto, command.ManagementRequest, command.OperationId,
                command.ExpectedOutboxMessageId, cancellationToken);

    public async Task<BaseCommandResponse<ManagedProviderClientProvisioningResultDto>> EnsureAsync(
        ManagedProviderClientProvisioningDto provisioningDto,
        ManagementTenantProvisioningRequestDto? managementRequest,
        Guid? operationId, Guid? expectedOutboxMessageId, CancellationToken cancellationToken)
    {
        if (managementRequest is null)
            return await humanProvisioning.ExecuteAsync(new EnsureManagedProviderClientProvisionedCommand
            {
                ProvisioningDto = provisioningDto,
                OperationId = operationId,
                ExpectedOutboxMessageId = expectedOutboxMessageId
            }, cancellationToken);

        var validation = await new ManagedProviderClientProvisioningDtoValidator().ValidateAsync(provisioningDto, cancellationToken);
        if (!validation.IsValid)
            return BaseCommandResponse.Validation<ManagedProviderClientProvisioningResultDto>(
                validation.Errors.Select(error => error.ErrorMessage),
                "Managed provider client provisioning failed due to validation errors.");
        var managementValidation = await new ManagementTenantProvisioningRequestValidator().ValidateAsync(managementRequest, cancellationToken);
        if (!managementValidation.IsValid)
            return BaseCommandResponse.Validation<ManagedProviderClientProvisioningResultDto>(
                managementValidation.Errors.Select(error => error.ErrorMessage));

        var normalized = ManagedTenantProvisioningRequestCodec.Normalize(managementRequest);
        if (provisioningDto != ManagedTenantProvisioningRequestCodec.ToProvisioningRequest(normalized))
            throw new UnauthorizedAccessException("Only the complete durable request may select worker provisioning inputs.");
        if (operationId is not { } operation || operation == Guid.Empty
            || expectedOutboxMessageId is not { } message || message == Guid.Empty)
            return BaseCommandResponse.Failure<ManagedProviderClientProvisioningResultDto>(
                "tenant_provisioning_operation_missing", "Durable operation and outbox-generation identifiers are required.");

        // The port reloads the full persisted request under its original delivery authority.
        var response = await workerCreation.CreateAsync(operation, message, cancellationToken);
        return BaseCommandResponse.Restore(
            response.Id is { } outcome ? new ManagedProviderClientProvisioningResultDto
            {
                TenantId = outcome.TenantId,
                UserId = outcome.UserId,
                UserActorId = outcome.UserActorId,
                UserExternalLoginId = outcome.UserExternalLoginId,
                TenantUserId = outcome.TenantUserId,
                TenantUserProfileId = outcome.TenantUserProfileId,
                TenantUserRoleGrantId = outcome.TenantUserRoleGrantId,
                TenantPlanAssignmentId = outcome.TenantPlanAssignmentId
            } : null,
            response.IsSuccess,
            response.Message,
            response.Errors,
            response.FailureCode,
            response.QuotaExceeded);
    }
}

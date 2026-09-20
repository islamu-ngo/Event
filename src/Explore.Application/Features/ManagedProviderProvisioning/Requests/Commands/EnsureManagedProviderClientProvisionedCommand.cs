using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.ManagedProviderProvisioning;
using Explore.Application.DTOs.Management;
using Explore.Application.Responses;

namespace Explore.Application.Features.ManagedProviderProvisioning.Requests.Commands;

public sealed record EnsureManagedProviderClientProvisionedCommand : ICommand<BaseCommandResponse<ManagedProviderClientProvisioningResultDto>>
{
    public ManagedProviderClientProvisioningDto ProvisioningDto { get; init; } = null!;
    public ManagementTenantProvisioningRequestDto? ManagementRequest { get; init; }
    public Guid? OperationId { get; init; }
    public Guid? ExpectedOutboxMessageId { get; init; }
}

using Explore.Application.Contracts.Operations;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventResourceProviderActivation.Requests.Commands;
using Explore.Application.Services;
using Explore.Application.Settings;

namespace Explore.Application.Features.EventResourceProviderActivation.Handlers.Commands;

public sealed class BindEventResourceProviderCommandHandler(EventResourceProviderControlPlane controlPlane)
    : ICommandHandler<BindEventResourceProviderCommand, EventResourceProviderOperation>
{
    public async Task<EventResourceProviderOperation> ExecuteAsync(BindEventResourceProviderCommand command, CancellationToken cancellationToken = default)
    {
        try
        {
            return await controlPlane.BindAsync(command.Binding, command.ExpectedRevision, cancellationToken);
        }
        catch (ArgumentException exception)
        {
            throw new BadRequestException(exception.Message);
        }
    }
}
public sealed class BeginEventResourceProviderOperationCommandHandler(EventResourceProviderControlPlane controlPlane)
    : ICommandHandler<BeginEventResourceProviderOperationCommand, EventResourceProviderOperation>
{
    public async Task<EventResourceProviderOperation> ExecuteAsync(BeginEventResourceProviderOperationCommand command, CancellationToken cancellationToken = default)
    {
        try
        {
            return await controlPlane.BeginAsync(command.DeploymentId, cancellationToken);
        }
        catch (ArgumentException exception)
        {
            throw new BadRequestException(exception.Message);
        }
    }
}
public sealed class ActivateEventResourceProviderCommandHandler(EventResourceProviderControlPlane controlPlane)
    : ICommandHandler<ActivateEventResourceProviderCommand, bool>
{
    public async Task<bool> ExecuteAsync(ActivateEventResourceProviderCommand command, CancellationToken cancellationToken = default)
    {
        try
        {
            return await controlPlane.ActivateAsync(command.DeploymentId, command.OperationId, command.Epoch,
                command.Scope, command.PolicyVersion, command.PreviousWritersStopped, command.ReachableReplicaCount,
                command.DeclaredPolicyReplicaCount, command.FrozenParentPolicyContractConfirmed, cancellationToken);
        }
        catch (ArgumentException exception)
        {
            throw new BadRequestException(exception.Message);
        }
    }
}

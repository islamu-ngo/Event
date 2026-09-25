using Explore.Application.Contracts.Operations;
using Explore.Application.Exceptions;
using Explore.Application.Services;
using Explore.Application.Settings;

namespace Explore.Application.Features.EventResourceProviderActivation;

public sealed record BindEventResourceProviderCommand(EventResourceDeploymentBinding Binding, Guid ExpectedRevision)
    : ICommand<EventResourceProviderOperation>;

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

public sealed record BeginEventResourceProviderOperationCommand(Guid DeploymentId) : ICommand<EventResourceProviderOperation>;

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

/// <summary>The operator attests convergence; replica counts are not inferred from an Admin upload or a timeout.</summary>
public sealed record ActivateEventResourceProviderCommand(Guid DeploymentId, Guid OperationId, long Epoch,
    string Scope, string PolicyVersion, bool PreviousWritersStopped, int ReachableReplicaCount, int DeclaredPolicyReplicaCount,
    bool FrozenParentPolicyContractConfirmed)
    : ICommand<bool>;

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

public sealed record GetEventResourceProviderBindingsQuery : IQuery<EventResourceProviderBindingDocument>;

public sealed class GetEventResourceProviderBindingsQueryHandler(EventResourceProviderControlPlane controlPlane)
    : IQueryHandler<GetEventResourceProviderBindingsQuery, EventResourceProviderBindingDocument>
{
    public Task<EventResourceProviderBindingDocument> QueryAsync(GetEventResourceProviderBindingsQuery query, CancellationToken cancellationToken = default) =>
        controlPlane.ReadBindingsAsync(cancellationToken);
}

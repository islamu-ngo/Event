using Explore.Application.Contracts.Operations;
using Explore.Application.Services;
using Explore.Application.Settings;

namespace Explore.Application.Features.EventResourceProviderActivation.Requests.Commands;

public sealed record BindEventResourceProviderCommand(EventResourceDeploymentBinding Binding, Guid ExpectedRevision)
    : ICommand<EventResourceProviderOperation>;

public sealed record BeginEventResourceProviderOperationCommand(Guid DeploymentId) : ICommand<EventResourceProviderOperation>;

/// <summary>The operator attests convergence; replica counts are not inferred from an Admin upload or a timeout.</summary>
public sealed record ActivateEventResourceProviderCommand(Guid DeploymentId, Guid OperationId, long Epoch,
    string Scope, string PolicyVersion, bool PreviousWritersStopped, int ReachableReplicaCount, int DeclaredPolicyReplicaCount,
    bool FrozenParentPolicyContractConfirmed)
    : ICommand<bool>;

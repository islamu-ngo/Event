using Explore.Application.Contracts.Operations;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Features.Moderation.Requests.Commands;

/// <summary>Names one lifecycle action without accepting an actor, permission or caller-selected operation kind.</summary>
public abstract record ModerationLifecycleCommand : ICommand<ModerationLifecycleResponse>
{
    public required Guid OperationId { get; init; }
    public required Guid EffectId { get; init; }
    public required Guid DecisionId { get; init; }
    public required SubjectReference Subject { get; init; }
    public required AuthorityScope Target { get; init; }
    /// <summary>Current case revision for proposal; current decision revision for every later transition.</summary>
    public required long ExpectedRevision { get; init; }
    public required long ExpectedPolicyRevision { get; init; }

    internal abstract ModerationOperation ToOperation();
    public sealed override string ToString() => nameof(ModerationLifecycleCommand);

    protected ModerationOperation Transition(ModerationOperationKind kind, Guid referenceId = default) =>
        ModerationOperation.Transition(kind, Subject, Target, OperationId, EffectId, DecisionId,
            ExpectedRevision, ExpectedPolicyRevision, referenceId);
}

public sealed record ProposeModerationDecisionCommand : ModerationLifecycleCommand
{
    public required ModerationProposal Proposal { get; init; }
    internal override ModerationOperation ToOperation() => ModerationOperation.Propose(
        Subject, Target, OperationId, EffectId, DecisionId, ExpectedRevision, ExpectedPolicyRevision, Proposal);
}

public sealed record RecordModerationDecisionResponseCommand : ModerationLifecycleCommand
{
    internal override ModerationOperation ToOperation() => Transition(ModerationOperationKind.RecordResponse);
}

public sealed record AcceptModerationFindingCommand : ModerationLifecycleCommand
{
    public required Guid FindingId { get; init; }
    internal override ModerationOperation ToOperation() => Transition(ModerationOperationKind.AcceptFinding, FindingId);
}

public sealed record ApproveModerationDecisionCommand : ModerationLifecycleCommand
{
    internal override ModerationOperation ToOperation() => Transition(ModerationOperationKind.Approve);
}

/// <summary>Activation uses the existing decision interval; a future start schedules rather than immediately restricts.</summary>
public sealed record ActivateModerationDecisionCommand : ModerationLifecycleCommand
{
    public required Guid SanctionId { get; init; }
    internal override ModerationOperation ToOperation() => Transition(ModerationOperationKind.Activate, SanctionId);
}

public sealed record RejectModerationDecisionCommand : ModerationLifecycleCommand
{
    internal override ModerationOperation ToOperation() => Transition(ModerationOperationKind.Reject);
}

public sealed record WithdrawModerationDecisionCommand : ModerationLifecycleCommand
{
    internal override ModerationOperation ToOperation() => Transition(ModerationOperationKind.Withdraw);
}

public sealed record RevokeModerationSanctionCommand : ModerationLifecycleCommand
{
    public required Guid SanctionId { get; init; }
    internal override ModerationOperation ToOperation() => Transition(ModerationOperationKind.Revoke, SanctionId);
}

public sealed record ReconcileModerationSanctionCommand : ModerationLifecycleCommand
{
    public required Guid SanctionId { get; init; }
    public required long ExpectedSanctionRevision { get; init; }
    internal override ModerationOperation ToOperation() => ModerationOperation.Reconcile(
        Subject, Target, OperationId, EffectId, DecisionId, ExpectedRevision, ExpectedPolicyRevision,
        SanctionId, ExpectedSanctionRevision);
}

public sealed record SupersedeModerationSanctionCommand : ModerationLifecycleCommand
{
    public required Guid SanctionId { get; init; }
    public required Guid SupersededSanctionId { get; init; }
    public required AuthorityScope SupersededScope { get; init; }
    public required long ExpectedSanctionRevision { get; init; }
    internal override ModerationOperation ToOperation() => ModerationOperation.Supersede(
        Subject, Target, OperationId, EffectId, DecisionId, ExpectedRevision, ExpectedPolicyRevision,
        SanctionId, SupersededSanctionId, SupersededScope, ExpectedSanctionRevision);
}

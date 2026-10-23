namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// The event identifier is the only target selector. The target tenant is never caller input,
/// and neither discovery nor acquisition changes the selected conversation tenant.
/// </summary>
public interface IHumanAiModerationProposalTargetSourceFactory
{
    IHumanAiModerationProposalTargetSource Create(Guid eventId);
}

public interface IHumanAiModerationProposalTargetSource
{
    Task<HumanEventAssignmentSourceFacts?> ObserveAsync(CancellationToken cancellationToken = default);
    Task<IHumanEventAssignmentSourceLease> AcquireAsync(IPolicySourceSetLease sourceSet,
        IHumanPrincipalSourceLease principal, CancellationToken cancellationToken = default);
}

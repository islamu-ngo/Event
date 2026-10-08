using ISLAMU.Moderation.Domain;

namespace ISLAMU.Moderation.Application;

/// <summary>Resolves positive current native permission, subject/target identity and policy under the session's writer fences.</summary>
public interface IModerationAuthorityContext
{
    /// <remarks>
    /// Intent fields are not proof. AcceptFinding must qualify finding/case ownership; Approve must qualify
    /// the canonical human and all native conflicts. Protected remedies still require owned subject or reviewer proof.
    /// </remarks>
    Task<ModerationAuthorityResolution> ResolveAsync(
        IModerationSession session, ModerationOperation operation, CancellationToken cancellationToken);

    /// <summary>Positively rechecks current finding, adopted rule/profile, independent approvers and activation prerequisites.</summary>
    Task<ModerationAuthorityState> QualifyActivationAsync(
        IModerationSession session, ModerationDecision decision, ModerationAuthorityGrant grant,
        CancellationToken cancellationToken);
}

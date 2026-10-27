using ISLAMU.Moderation.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Case-specific procedural governance, independent of deployment and consumer conformance.
/// Both source and adoption must be read under their actual writer fences in the caller's transaction.
/// </summary>
public interface IModerationQualificationPolicySourceOwner
{
    Task<ModerationQualificationPolicyCurrentSource?> AcquireQualificationPolicyAsync(
        Guid hostBindingId, Guid transactionId, Guid caseId, AuthorityScope scope,
        DateTimeOffset observedAt, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the separately adopted source generation and lifetime limits.
    /// Missing, withdrawn or expired adoption never falls back to authoring defaults.
    /// </summary>
    Task<ModerationQualificationPolicyApproval?> ReadQualificationPolicyApprovalAsync(
        Guid hostBindingId, Guid transactionId, Guid caseId, AuthorityScope scope,
        DateTimeOffset observedAt, CancellationToken cancellationToken);
}

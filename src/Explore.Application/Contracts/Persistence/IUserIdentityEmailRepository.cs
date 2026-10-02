using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Persists canonical account-email ownership separately from editable contact data.
/// Mutations participate in the caller's account synchronization transaction.
/// </summary>
public interface IUserIdentityEmailRepository
{
    Task<UserIdentityEmailClaim?> GetByNormalizedEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<UserIdentityEmailClaim>> GetByUserAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<UserIdentityEmailEvidence?> GetEvidenceByBindingAsync(
        Guid externalLoginId,
        CancellationToken cancellationToken);

    Task<UserIdentityEmailClaim> CreateClaimAsync(
        UserIdentityEmailClaim claim,
        CancellationToken cancellationToken);

    Task<UserIdentityEmailEvidence> CreateEvidenceAsync(
        UserIdentityEmailEvidence evidence,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes this binding's proof and any claim left without active proof.
    /// Independent bindings supporting the same address remain authoritative.
    /// </summary>
    Task RemoveEvidenceByBindingAsync(
        Guid externalLoginId,
        CancellationToken cancellationToken);
}

using Explore.Application.Contracts.Persistence;
using Explore.Domain;

namespace Explore.Application.Services;

public sealed record IdentityEmailObservation(
    string? NormalizedEmail,
    bool EmailVerified,
    bool CanClaimIdentityEmail,
    Guid ClaimId,
    Guid EvidenceId,
    DateTime ObservedAtUtc);

public enum IdentityEmailSynchronizationOutcome
{
    Unsupported,
    Confirmed,
    ConflictingOwner
}

/// <summary>
/// Reconciles one exact binding's proof inside its account transaction without
/// revoking independently supported aliases or changing provider verification facts.
/// </summary>
public sealed class IdentityEmailSynchronizationOperation(IUserIdentityEmailRepository identityEmails)
{
    public async Task<IdentityEmailSynchronizationOutcome> ExecuteAsync(
        User user,
        UserExternalLogin binding,
        IdentityEmailObservation observation,
        CancellationToken cancellationToken)
    {
        if (binding.UserId != user.Id)
            throw new InvalidOperationException("Identity evidence requires its exact account binding.");

        UserIdentityEmailEvidence? previous = await identityEmails.GetEvidenceByBindingAsync(
            binding.Id, cancellationToken);
        IReadOnlyList<UserIdentityEmailClaim> supported = await identityEmails.GetByUserAsync(
            user.Id, cancellationToken);
        UserIdentityEmailClaim? previousClaim = previous is null
            ? null : supported.SingleOrDefault(claim => claim.Id == previous.ClaimId);
        if (observation.EmailVerified && previous is { IsActive: true } && previousClaim is not null
            && previousClaim.NormalizedEmail == observation.NormalizedEmail)
            return IdentityEmailSynchronizationOutcome.Confirmed;

        await identityEmails.RemoveEvidenceByBindingAsync(binding.Id, cancellationToken);
        if (!observation.CanClaimIdentityEmail || !observation.EmailVerified
            || string.IsNullOrWhiteSpace(observation.NormalizedEmail))
            return IdentityEmailSynchronizationOutcome.Unsupported;

        UserIdentityEmailClaim? claim = await identityEmails.GetByNormalizedEmailAsync(
            observation.NormalizedEmail, cancellationToken);
        if (claim is not null && claim.UserId != user.Id)
            return IdentityEmailSynchronizationOutcome.ConflictingOwner;

        claim ??= await identityEmails.CreateClaimAsync(UserIdentityEmailClaim.Create(
            user.Id, observation.NormalizedEmail, observation.ClaimId), cancellationToken);
        await identityEmails.CreateEvidenceAsync(UserIdentityEmailEvidence.Create(
            user.Id, claim.Id, binding.Id, observation.ObservedAtUtc, observation.EvidenceId), cancellationToken);
        return IdentityEmailSynchronizationOutcome.Confirmed;
    }
}

using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;

namespace Explore.Persistence.Identity;

internal sealed partial class LocalIdentityCredentialStateStore
{
    internal async Task<bool> RecoverVisitorAsync(
        VisitorEnrollmentRecoveryCandidate candidate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Guid tenant = RequireVisitorPreparationOwner();
        if (candidate.Id == Guid.Empty || candidate.TenantId == Guid.Empty || candidate.Generation <= 0
            || candidate.ExpiresAt.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Visitor recovery requires an original persisted version.", nameof(candidate));
        if (candidate.TenantId != tenant) return false;

        if (candidate.Stage == VisitorEnrollmentStage.PrimaryAdmitted)
        {
            var receipt = await ReadVisitorPrimaryReceiptAsync(candidate.Id, tenant, cancellationToken)
                .ConfigureAwait(false);
            RequireVisitorPreparationOwner(tenant);
            if (receipt is null || !MatchesVisitorRecoveryCandidate(receipt, candidate)
                || receipt.CredentialGeneration is not > 0)
                return false;
            var evidence = await ReadVisitorNativeEvidenceAsync(receipt, true, cancellationToken).ConfigureAwait(false);
            RequireVisitorPreparationOwner(tenant);
            var outcome = await ActivateVisitorCoreAsync(candidate.Id, evidence.Operation.ConcurrencyStamp,
                receipt.CredentialGeneration.Value, candidate, cancellationToken).ConfigureAwait(false);
            if (outcome is not (LocalCredentialActivationOutcome.Activated or LocalCredentialActivationOutcome.AlreadyActivated))
                return false;
            var acknowledged = await AcknowledgeVisitorActivationAsync(candidate.Id, cancellationToken)
                .ConfigureAwait(false);
            return acknowledged?.Stage == VisitorEnrollmentStage.Active;
        }
        if (candidate.Stage is not (VisitorEnrollmentStage.Reserved or VisitorEnrollmentStage.IdentityPrepared
                or VisitorEnrollmentStage.Canceled))
            return false;

        bool canceled = candidate.Stage == VisitorEnrollmentStage.Canceled;
        var current = await ExecuteVisitorCancellationAsync(candidate.Id, !canceled, canceled,
            candidate, false, cancellationToken).ConfigureAwait(false);
        if (current is null) return false;
        // Primary cancellation is durable before a separate native purge. Its successful
        // return confirms the native commit; only then may primary acknowledge cleanup.
        if (!canceled && !await DeleteCanceledVisitorAsync(candidate.Id, cancellationToken).ConfigureAwait(false))
            return false;
        var cleanupVersion = new VisitorEnrollmentRecoveryCandidate(
            current.Id, current.TenantId, current.Stage, current.Generation, current.ExpiresAt);
        var completed = await ExecuteVisitorCancellationAsync(candidate.Id, false, true,
            cleanupVersion, true, cancellationToken).ConfigureAwait(false);
        return completed?.CleanupCompletedAt.HasValue == true;
    }

    private static bool MatchesVisitorRecoveryCandidate(
        VisitorEnrollmentOperation receipt, VisitorEnrollmentRecoveryCandidate candidate) =>
        receipt.Id == candidate.Id && receipt.TenantId == candidate.TenantId
        && receipt.Stage == candidate.Stage && receipt.Generation == candidate.Generation
        && receipt.ExpiresAt == candidate.ExpiresAt
        && (receipt.Stage != VisitorEnrollmentStage.Canceled || !receipt.CleanupCompletedAt.HasValue);
}

using System.Data;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.QueryFilters;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Identity;

internal sealed partial class LocalIdentityCredentialStateStore
{
    internal Task<VisitorEnrollmentOperation?> CancelVisitorAsync(
        Guid enrollmentOperationId, bool requireExpired, CancellationToken cancellationToken) =>
        ExecuteVisitorCancellationAsync(enrollmentOperationId, requireExpired, false, null, false, cancellationToken);

    internal async Task<bool> DeleteCanceledVisitorAsync(
        Guid enrollmentOperationId, CancellationToken cancellationToken) =>
        await ExecuteVisitorCancellationAsync(enrollmentOperationId, false, true, null, false, cancellationToken)
            .ConfigureAwait(false) is not null;

    private async Task<VisitorEnrollmentOperation?> ExecuteVisitorCancellationAsync(
        Guid enrollmentOperationId, bool requireExpired, bool cleanup,
        VisitorEnrollmentRecoveryCandidate? candidate, bool acknowledgeCleanup, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Guid tenant = RequireVisitorPreparationOwner();
        if (enrollmentOperationId == Guid.Empty)
            throw new ArgumentException("The original visitor enrollment is required.", nameof(enrollmentOperationId));
        EnsureReconciliationContextsIdle();
        int entered = 0;
        var result = await ExecuteLifecycleMutationAsync((lifecycle, token) =>
        {
            if (Interlocked.Exchange(ref entered, 1) != 0)
                throw new InvalidOperationException("Visitor cancellation cannot replay a mutation.");
            RequireVisitorPreparationOwner(tenant);
            return CancelVisitorAttemptAsync(enrollmentOperationId, tenant, requireExpired, cleanup,
                candidate, acknowledgeCleanup, lifecycle, token);
        }, cancellationToken).ConfigureAwait(false);
        RequireVisitorPreparationOwner(tenant);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private async Task<VisitorEnrollmentOperation?> CancelVisitorAttemptAsync(
        Guid enrollmentOperationId, Guid tenant, bool requireExpired, bool cleanup,
        VisitorEnrollmentRecoveryCandidate? candidate, bool acknowledgeCleanup,
        LocalIdentityLifecycleExecutionCustody lifecycle, CancellationToken token)
    {
        VisitorEnrollmentOperation? owned = null;
        await using var transaction = await LocalIdentityMutationTransaction.BeginAsync(
            identityDbContext, applicationDbContext, identityFence, token).ConfigureAwait(false);
        var primary = applicationDbContext.Database.CurrentTransaction;
        void RequireCurrent()
        {
            token.ThrowIfCancellationRequested();
            RequireVisitorPreparationOwner(tenant);
            lifecycle.RequireCurrent();
            transaction.RequireOwner(identityDbContext, applicationDbContext);
            if (primary is null || !ReferenceEquals(applicationDbContext.Database.CurrentTransaction, primary)
                || primary.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
                throw new UnauthorizedAccessException("Visitor cancellation requires its original Serializable owner.");
        }

        try
        {
            RequireCurrent();
            var enrollment = await ReadVisitorPrimaryReceiptAsync(enrollmentOperationId, tenant, token)
                .ConfigureAwait(false);
            RequireCurrent();
            if (enrollment is null
                || candidate is not null && !MatchesVisitorRecoveryCandidate(enrollment, candidate)
                || enrollment.Stage is VisitorEnrollmentStage.PrimaryAdmitted or VisitorEnrollmentStage.Active
                || cleanup && enrollment.Stage != VisitorEnrollmentStage.Canceled)
                return null;
            if (enrollment.Stage is not (VisitorEnrollmentStage.Reserved or VisitorEnrollmentStage.IdentityPrepared
                    or VisitorEnrollmentStage.Canceled)
                || enrollment.TenantUserId.HasValue
                || enrollment.Stage == VisitorEnrollmentStage.Reserved && enrollment.CredentialGeneration.HasValue
                || enrollment.Stage == VisitorEnrollmentStage.IdentityPrepared && enrollment.CredentialGeneration is not > 0
                || enrollment.Stage == VisitorEnrollmentStage.Canceled && !enrollment.UpdatedAt.HasValue)
                throw new UnauthorizedAccessException("The original visitor cancellation evidence changed.");
            if (requireExpired && timeProvider.GetUtcNow().UtcDateTime < enrollment.ExpiresAt)
                return null;

            await identityFence.EnsureSubjectMayEnrollAsync(enrollment.LocalSubjectId, token).ConfigureAwait(false);
            RequireCurrent();
            // Deleted account evidence also vetoes a pending purge; tenant isolation remains enabled.
            string providerKey = enrollment.LocalSubjectId.ToString("D");
            if (await applicationDbContext.Users.IgnoreQueryFilters([QueryFilterNames.SoftDelete]).AsNoTracking()
                    .AnyAsync(row => row.Id == enrollment.LocalSubjectId, token).ConfigureAwait(false)
                || await applicationDbContext.Actors.IgnoreQueryFilters([QueryFilterNames.SoftDelete]).AsNoTracking()
                    .AnyAsync(row => row.Id == enrollment.PersonalActorId || row.UserId == enrollment.LocalSubjectId,
                        token).ConfigureAwait(false)
                || await applicationDbContext.UserExternalLogins.IgnoreQueryFilters([QueryFilterNames.SoftDelete]).AsNoTracking()
                    .AnyAsync(row => row.Id == enrollment.ExternalLoginId || row.UserId == enrollment.LocalSubjectId
                        || row.AuthenticationProviderId == (int)AuthenticationProviderKind.Local
                            && row.ProviderKey == providerKey, token).ConfigureAwait(false)
                || await applicationDbContext.Set<TenantUser>().IgnoreQueryFilters([QueryFilterNames.SoftDelete]).AsNoTracking()
                    .AnyAsync(row => row.UserId == enrollment.LocalSubjectId
                        || row.ActorId == enrollment.PersonalActorId, token).ConfigureAwait(false))
                throw new UnauthorizedAccessException("An existing account cannot be canceled or purged.");
            RequireCurrent();

            var operations = await identityDbContext.Set<LocalIdentityCredentialOperation>().AsNoTracking()
                .Where(row => row.Id == enrollment.CredentialOperationId
                    || row.LocalSubjectId == enrollment.LocalSubjectId
                    || row.VisitorEnrollmentOperationId == enrollment.Id
                    || row.PersonalActorId == enrollment.PersonalActorId
                    || row.ExternalLoginId == enrollment.ExternalLoginId
                    || row.PreviousOperationId == enrollment.CredentialOperationId)
                .ToListAsync(token).ConfigureAwait(false);
            RequireCurrent();
            var eligibilities = await identityDbContext.Set<LocalIdentityEligibility>().AsNoTracking()
                .Where(row => row.LocalSubjectId == enrollment.LocalSubjectId
                    || row.CurrentOperationId == enrollment.CredentialOperationId
                    || row.PersonalActorId == enrollment.PersonalActorId
                    || row.ExternalLoginId == enrollment.ExternalLoginId)
                .ToListAsync(token).ConfigureAwait(false);
            RequireCurrent();
            var user = await identityDbContext.Set<LocalIdentityUser>().AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == enrollment.LocalSubjectId, token).ConfigureAwait(false);
            RequireCurrent();
            var tokens = await identityDbContext.Set<IdentityUserToken<Guid>>().AsNoTracking()
                .Where(row => row.UserId == enrollment.LocalSubjectId).ToListAsync(token).ConfigureAwait(false);
            RequireCurrent();
            bool privileged = await identityDbContext.Set<IdentityUserRole<Guid>>().AsNoTracking()
                    .AnyAsync(row => row.UserId == enrollment.LocalSubjectId, token).ConfigureAwait(false)
                || await identityDbContext.Set<IdentityUserClaim<Guid>>().AsNoTracking()
                    .AnyAsync(row => row.UserId == enrollment.LocalSubjectId, token).ConfigureAwait(false)
                || await identityDbContext.Set<IdentityUserLogin<Guid>>().AsNoTracking()
                    .AnyAsync(row => row.UserId == enrollment.LocalSubjectId, token).ConfigureAwait(false);
            RequireCurrent();
            bool absent = operations.Count == 0 && eligibilities.Count == 0 && user is null && tokens.Count == 0
                && !privileged;
            if (acknowledgeCleanup && !absent)
                throw new UnauthorizedAccessException("Visitor cleanup acknowledgement requires confirmed native absence.");
            if (!absent)
            {
                if (operations.Count != 1 || eligibilities.Count != 1 || user is null || tokens.Count != 1
                    || privileged || user.EmailConfirmed || user.PhoneNumberConfirmed || user.PhoneNumber is not null
                    || user.TwoFactorEnabled || user.AccessFailedCount != 0
                    || tokens[0].LoginProvider != LocalCredentialStateMetadata.TokenLoginProvider
                    || tokens[0].Name != LocalCredentialStateMetadata.TokenName)
                    throw new UnauthorizedAccessException("Partial or privileged visitor credentials cannot be purged.");
                var evidence = await ReadVisitorNativeEvidenceAsync(enrollment, false, token).ConfigureAwait(false);
                RequireCurrent();
                if (evidence.Operation.CreatedAt < enrollment.CreatedAt
                    || evidence.Operation.CreatedAt >= enrollment.ExpiresAt
                    || evidence.Operation.CreatedAt > timeProvider.GetUtcNow().UtcDateTime
                    || evidence.Operation.UpdatedAt.HasValue || evidence.User.UpdatedAt.HasValue
                    || evidence.User.CreatedAt != evidence.Operation.CreatedAt
                    || evidence.Eligibility.CreatedAt != evidence.Operation.CreatedAt
                    || evidence.Eligibility.Generation != 1 || evidence.Eligibility.UpdatedAt.HasValue)
                    throw new UnauthorizedAccessException("The original pending visitor credential changed.");

                if (cleanup)
                {
                    // Restrict FKs require eligibility -> operation -> user. Each write consumes
                    // its original source image; a failed CAS rolls the entire native purge back.
                    int eligibilityRows = await identityDbContext.Set<LocalIdentityEligibility>()
                        .Where(row => row.Id == evidence.Eligibility.Id
                            && row.LocalSubjectId == enrollment.LocalSubjectId
                            && row.CurrentOperationId == enrollment.CredentialOperationId
                            && row.Generation == evidence.Eligibility.Generation
                            && row.State == LocalIdentityEligibilityState.ProvisioningPending
                            && row.PersonalActorId == enrollment.PersonalActorId
                            && row.ExternalLoginId == enrollment.ExternalLoginId)
                        .ExecuteDeleteAsync(token).ConfigureAwait(false);
                    RequireCurrent();
                    int metadataRows = await identityDbContext.Set<IdentityUserToken<Guid>>()
                        .Where(row => row.UserId == enrollment.LocalSubjectId
                            && row.LoginProvider == LocalCredentialStateMetadata.TokenLoginProvider
                            && row.Name == LocalCredentialStateMetadata.TokenName && row.Value == tokens[0].Value)
                        .ExecuteDeleteAsync(token).ConfigureAwait(false);
                    RequireCurrent();
                    int operationRows = await identityDbContext.Set<LocalIdentityCredentialOperation>()
                        .Where(row => row.Id == enrollment.CredentialOperationId
                            && row.ConcurrencyStamp == evidence.Operation.ConcurrencyStamp
                            && row.Kind == LocalCredentialOperationKind.VisitorCreate
                            && row.Stage == LocalCredentialOperationStage.ProvisioningPending
                            && row.LocalSubjectId == enrollment.LocalSubjectId
                            && row.VisitorEnrollmentOperationId == enrollment.Id
                            && row.PersonalActorId == enrollment.PersonalActorId
                            && row.ExternalLoginId == enrollment.ExternalLoginId
                            && row.InitiatingApplicationUserId == null && row.VerifiedByApplicationUserId == null
                            && row.VerifiedAt == null && row.PreviousOperationId == null
                            && row.PreviousOperationConcurrencyStamp == null && row.ResetReason == null)
                        .ExecuteDeleteAsync(token).ConfigureAwait(false);
                    RequireCurrent();
                    int userRows = await identityDbContext.Set<LocalIdentityUser>()
                        .Where(row => row.Id == enrollment.LocalSubjectId
                            && row.ConcurrencyStamp == evidence.User.ConcurrencyStamp
                            && row.SecurityStamp == evidence.User.SecurityStamp
                            && row.PasswordHash == evidence.User.PasswordHash && !row.EmailConfirmed
                            && !row.PhoneNumberConfirmed && !row.TwoFactorEnabled)
                        .ExecuteDeleteAsync(token).ConfigureAwait(false);
                    RequireCurrent();
                    if (eligibilityRows != 1 || metadataRows != 1 || operationRows != 1 || userRows != 1)
                        throw new DbUpdateConcurrencyException("The original pending visitor purge lost its source.");
                }
            }
            else if (enrollment.Stage == VisitorEnrollmentStage.IdentityPrepared)
                throw new UnauthorizedAccessException("Prepared visitor credentials are missing.");

            if (!cleanup && enrollment.Stage != VisitorEnrollmentStage.Canceled)
            {
                applicationDbContext.Attach(enrollment);
                owned = enrollment;
                enrollment.Cancel(enrollment.Generation, enrollment.CredentialOperationId,
                    timeProvider.GetUtcNow().UtcDateTime);
                await applicationDbContext.SaveChangesAsync(token).ConfigureAwait(false);
                RequireCurrent();
            }
            if (acknowledgeCleanup && !enrollment.CleanupCompletedAt.HasValue)
            {
                applicationDbContext.Attach(enrollment);
                owned = enrollment;
                enrollment.AcknowledgeCancellationCleanup(enrollment.Generation, enrollment.CredentialOperationId,
                    timeProvider.GetUtcNow().UtcDateTime);
                await applicationDbContext.SaveChangesAsync(token).ConfigureAwait(false);
                RequireCurrent();
            }
            var current = await ReadVisitorPrimaryReceiptAsync(enrollment.Id, tenant, token).ConfigureAwait(false);
            RequireCurrent();
            if (current is null || !SameVisitorPrimaryReceipt(enrollment, current))
                throw new DbUpdateConcurrencyException("The original canceled visitor receipt changed.");
            await transaction.CommitAsync(token, () =>
            {
                token.ThrowIfCancellationRequested();
                lifecycle.RequireCurrent();
                RequireVisitorPreparationOwner(tenant);
                if (requireExpired && timeProvider.GetUtcNow().UtcDateTime < enrollment.ExpiresAt)
                    throw new UnauthorizedAccessException("Visitor expiry cancellation lost its original deadline.");
            }).ConfigureAwait(false);
            RequireVisitorPreparationOwner(tenant);
            return enrollment;
        }
        catch
        {
            await RollbackBestEffortAsync(transaction).ConfigureAwait(false);
            throw;
        }
        finally
        {
            if (owned is not null)
                applicationDbContext.Entry(owned).State = EntityState.Detached;
        }
    }
}

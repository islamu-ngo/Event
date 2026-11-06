using System.Data;
using Explore.Application.Authentication;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Identity;

internal sealed partial class LocalIdentityCredentialStateStore
{
    internal async Task<VisitorEnrollmentOperation> ReserveVisitorAsync(
        Guid enrollmentOperationId, string proofDigest,
        Func<CancellationToken, Task> authorizeNewEnrollment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorizeNewEnrollment);
        cancellationToken.ThrowIfCancellationRequested();
        Guid tenant = RequireVisitorPreparationOwner();
        if (enrollmentOperationId == Guid.Empty || enrollmentOperationId.Version != 7
            || enrollmentOperationId.Variant is < 8 or > 11)
            throw new ArgumentException("A UUIDv7 enrollment operation is required.", nameof(enrollmentOperationId));
        if (proofDigest is null || proofDigest.Length != 64
            || proofDigest.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("A canonical lowercase SHA-256 proof digest is required.", nameof(proofDigest));
        EnsureReconciliationContextsIdle();

        var settings = new RelationalSettingMutationLock(applicationDbContext, new EfCoreUnitOfWork(applicationDbContext));
        int entered = 0;
        // Retain every authority key before lifecycle/privacy custody can open the primary transaction.
        var result = await settings.ExecuteOrderedGroupsOnceAsync(
            [VisitorAccessCapabilityResolver.AuthoritySettingKeys],
            token => ExecuteLifecycleMutationAsync((lifecycle, inner) =>
            {
                if (Interlocked.Exchange(ref entered, 1) != 0)
                    throw new InvalidOperationException("Visitor reservation cannot replay a mutation.");
                RequireVisitorPreparationOwner(tenant);
                return ReserveVisitorAttemptAsync(enrollmentOperationId, proofDigest, tenant,
                    authorizeNewEnrollment, lifecycle, inner);
            }, token), cancellationToken).ConfigureAwait(false);
        RequireVisitorPreparationOwner(tenant);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private async Task<VisitorEnrollmentOperation> ReserveVisitorAttemptAsync(
        Guid enrollmentOperationId, string proofDigest, Guid tenant,
        Func<CancellationToken, Task> authorizeNewEnrollment,
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
                throw new UnauthorizedAccessException("Visitor reservation requires its original Serializable primary owner.");
        }

        void RequireReceipt(VisitorEnrollmentOperation receipt)
        {
            if (receipt.Id != enrollmentOperationId || receipt.TenantId != tenant
                || !string.Equals(receipt.IntentDigest, proofDigest, StringComparison.Ordinal)
                || receipt.Stage is not (VisitorEnrollmentStage.Reserved or VisitorEnrollmentStage.IdentityPrepared
                    or VisitorEnrollmentStage.PrimaryAdmitted or VisitorEnrollmentStage.Active)
                || timeProvider.GetUtcNow().UtcDateTime >= receipt.ExpiresAt)
                throw new UnauthorizedAccessException("The visitor reservation is unavailable.");
        }

        try
        {
            RequireCurrent();
            var receipt = await ReadVisitorPrimaryReceiptAsync(enrollmentOperationId, tenant, token).ConfigureAwait(false);
            RequireCurrent();
            if (receipt is not null)
            {
                // Exact replay returns only the original data, without rerunning new-allocation policy.
                RequireReceipt(receipt);
            }
            else
            {
                await authorizeNewEnrollment(token).ConfigureAwait(false);
                RequireCurrent();
                if (applicationDbContext.ChangeTracker.HasChanges() || identityDbContext.ChangeTracker.HasChanges()
                    || await ReadVisitorPrimaryReceiptAsync(enrollmentOperationId, tenant, token).ConfigureAwait(false)
                        is not null)
                    throw new UnauthorizedAccessException("Visitor reservation lost its original allocation evidence.");
                RequireCurrent();

                // All allocation identifiers originate here, once, after final Application authorization.
                receipt = VisitorEnrollmentOperation.Reserve(enrollmentOperationId, tenant,
                    Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
                    proofDigest, timeProvider.GetUtcNow().UtcDateTime);
                owned = receipt;
                applicationDbContext.Set<VisitorEnrollmentOperation>().Add(receipt);
                await applicationDbContext.SaveChangesAsync(token).ConfigureAwait(false);
                RequireCurrent();
            }

            RequireCurrent();
            await transaction.CommitAsync(token, () =>
            {
                token.ThrowIfCancellationRequested();
                lifecycle.RequireCurrent();
                RequireVisitorPreparationOwner(tenant);
                RequireReceipt(receipt);
            }).ConfigureAwait(false);
            RequireVisitorPreparationOwner(tenant);
            return receipt;
        }
        catch (DbUpdateException failure) when (RegistrationUniqueConflictClassifier.IsProviderUniqueConflict(failure))
        {
            // A tenant-filtered foreign identifier or concurrent reservation is never adopted or disclosed.
            await RollbackBestEffortAsync(transaction).ConfigureAwait(false);
            throw new UnauthorizedAccessException("The visitor reservation is unavailable.");
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

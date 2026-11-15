using System.Data;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Identity;

internal sealed partial class LocalIdentityCredentialStateStore
{
    internal async Task<VisitorEnrollmentOperation?> AcknowledgeVisitorActivationAsync(
        Guid enrollmentOperationId, CancellationToken cancellationToken)
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
                throw new InvalidOperationException("Visitor acknowledgement cannot replay a mutation.");
            RequireVisitorPreparationOwner(tenant);
            return AcknowledgeVisitorActivationAttemptAsync(enrollmentOperationId, tenant, lifecycle, token);
        }, cancellationToken).ConfigureAwait(false);
        RequireVisitorPreparationOwner(tenant);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private async Task<VisitorEnrollmentOperation?> AcknowledgeVisitorActivationAttemptAsync(
        Guid enrollmentOperationId, Guid tenant, LocalIdentityLifecycleExecutionCustody lifecycle,
        CancellationToken token)
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
                throw new UnauthorizedAccessException(
                    "Visitor acknowledgement requires its original Serializable primary owner.");
        }

        try
        {
            RequireCurrent();
            var enrollment = await ReadVisitorPrimaryReceiptAsync(enrollmentOperationId, tenant, token)
                .ConfigureAwait(false);
            RequireCurrent();
            if (enrollment is null) return null;
            if (enrollment.Stage is not (VisitorEnrollmentStage.PrimaryAdmitted or VisitorEnrollmentStage.Active)
                || !enrollment.TenantUserId.HasValue || enrollment.CredentialGeneration is not > 0
                || !enrollment.UpdatedAt.HasValue)
                throw new UnauthorizedAccessException("Committed visitor admission is required for acknowledgement.");

            await identityFence.EnsureSubjectMayEnrollAsync(enrollment.LocalSubjectId, token).ConfigureAwait(false);
            RequireCurrent();
            var evidence = await ReadVisitorNativeEvidenceAsync(enrollment, true, token).ConfigureAwait(false);
            RequireCurrent();
            DateTime now = timeProvider.GetUtcNow().UtcDateTime;
            // The shared reader also permits pending admission replay. Only durable native
            // activation can acknowledge admission; lockout publications may advance Ready.
            if (evidence.Operation.Stage != LocalCredentialOperationStage.Replaced
                || evidence.Eligibility.State != LocalIdentityEligibilityState.Ready
                || evidence.Operation.CreatedAt < enrollment.CreatedAt
                || !evidence.Operation.UpdatedAt.HasValue
                || evidence.Operation.UpdatedAt > now || enrollment.UpdatedAt > now
                || (enrollment.Stage == VisitorEnrollmentStage.PrimaryAdmitted
                    ? evidence.Operation.UpdatedAt < enrollment.UpdatedAt
                    : enrollment.UpdatedAt < evidence.Operation.UpdatedAt))
                throw new UnauthorizedAccessException("The original visitor activation is not durably ready.");

            if (!await HasExactApplicationBindingAsync(evidence.Operation, token).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The original visitor account binding changed.");
            RequireCurrent();
            bool membership = await applicationDbContext.Set<TenantUser>().AsNoTracking().AnyAsync(
                row => row.Id == enrollment.TenantUserId && row.TenantId == tenant
                    && row.UserId == enrollment.LocalSubjectId && row.ActorId == enrollment.PersonalActorId
                    && row.StatusId == (int)TenantUserStatusEnum.Active && !row.IsDeleted, token)
                .ConfigureAwait(false);
            RequireCurrent();
            if (!membership)
                throw new UnauthorizedAccessException("The original active visitor membership changed.");
            await identityFence.MatchAndBindAsync(
                new ProviderAccountKey(AuthenticationProviderKind.Local, enrollment.LocalSubjectId.ToString("D")),
                enrollment.LocalSubjectId, enrollment.ExternalLoginId, tenant, replacementProvisioning: false, token)
                .ConfigureAwait(false);
            RequireCurrent();

            if (enrollment.Stage == VisitorEnrollmentStage.PrimaryAdmitted)
            {
                // Attach the original database image so EF's Generation concurrency token
                // settles the same receipt the domain transition consumed.
                applicationDbContext.Attach(enrollment);
                owned = enrollment;
                enrollment.Activate(enrollment.Generation, enrollment.CredentialOperationId, now);
                await applicationDbContext.SaveChangesAsync(token).ConfigureAwait(false);
                RequireCurrent();
            }
            RequireCurrent();
            await transaction.CommitAsync(token, () =>
            {
                token.ThrowIfCancellationRequested();
                lifecycle.RequireCurrent();
                RequireVisitorPreparationOwner(tenant);
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

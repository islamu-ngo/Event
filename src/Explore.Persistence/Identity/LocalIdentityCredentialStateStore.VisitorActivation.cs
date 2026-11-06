using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Identity;

internal sealed partial class LocalIdentityCredentialStateStore
{
    internal Task<LocalCredentialActivationOutcome> ActivateVisitorAsync(
        Guid enrollmentOperationId, Guid expectedOperationConcurrencyStamp, long expectedEligibilityGeneration,
        CancellationToken cancellationToken) =>
        ActivateVisitorCoreAsync(enrollmentOperationId, expectedOperationConcurrencyStamp,
            expectedEligibilityGeneration, null, cancellationToken);

    private async Task<LocalCredentialActivationOutcome> ActivateVisitorCoreAsync(
        Guid enrollmentOperationId, Guid expectedOperationConcurrencyStamp, long expectedEligibilityGeneration,
        VisitorEnrollmentRecoveryCandidate? candidate,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Guid tenant = RequireVisitorPreparationOwner();
        if (enrollmentOperationId == Guid.Empty || expectedOperationConcurrencyStamp == Guid.Empty)
            throw new ArgumentException("The original visitor enrollment and credential stamp are required.");
        if (expectedEligibilityGeneration <= 0 || expectedEligibilityGeneration == long.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(expectedEligibilityGeneration));
        EnsureReconciliationContextsIdle();
        int entered = 0;
        LocalCredentialActivationOutcome result = await ExecuteLifecycleMutationAsync((lifecycle, token) =>
        {
            if (Interlocked.Exchange(ref entered, 1) != 0)
                throw new InvalidOperationException("Visitor activation cannot replay a mutation within its owner.");
            RequireVisitorPreparationOwner(tenant);
            return ActivateVisitorAttemptAsync(enrollmentOperationId, expectedOperationConcurrencyStamp,
                expectedEligibilityGeneration, tenant, candidate, lifecycle, token);
        }, cancellationToken).ConfigureAwait(false);
        RequireVisitorPreparationOwner(tenant);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private async Task<LocalCredentialActivationOutcome> ActivateVisitorAttemptAsync(
        Guid enrollmentOperationId, Guid expectedOperationConcurrencyStamp, long expectedEligibilityGeneration,
        Guid tenant, VisitorEnrollmentRecoveryCandidate? candidate,
        LocalIdentityLifecycleExecutionCustody lifecycle, CancellationToken cancellationToken)
    {
        await using var transaction = await LocalIdentityMutationTransaction.BeginAsync(
            identityDbContext, applicationDbContext, identityFence, cancellationToken).ConfigureAwait(false);
        var applicationTransaction = applicationDbContext.Database.CurrentTransaction;
        void RequireCurrent()
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireVisitorPreparationOwner(tenant);
            lifecycle.RequireCurrent();
            transaction.RequireOwner(identityDbContext, applicationDbContext);
            if (!ReferenceEquals(applicationDbContext.Database.CurrentTransaction, applicationTransaction))
                throw new UnauthorizedAccessException("Visitor activation lost its original application transaction.");
        }

        try
        {
            RequireCurrent();
            VisitorEnrollmentOperation? enrollment = await applicationDbContext.Set<VisitorEnrollmentOperation>()
                .AsNoTracking().SingleOrDefaultAsync(row => row.Id == enrollmentOperationId && row.TenantId == tenant,
                    cancellationToken).ConfigureAwait(false);
            RequireCurrent();
            if (enrollment is null)
                return LocalCredentialActivationOutcome.NotFound;
            if (candidate is not null && !MatchesVisitorRecoveryCandidate(enrollment, candidate))
                return LocalCredentialActivationOutcome.Conflict;
            // Committed admission survives the reservation deadline and later enrollment closure.
            // This consumes existing membership evidence; it does not authorize another admission.
            if (enrollment.Stage is not (VisitorEnrollmentStage.PrimaryAdmitted or VisitorEnrollmentStage.Active)
                || enrollment.CredentialGeneration != expectedEligibilityGeneration
                || !enrollment.TenantUserId.HasValue)
                return LocalCredentialActivationOutcome.Conflict;

            await identityFence.EnsureSubjectMayEnrollAsync(enrollment.LocalSubjectId, cancellationToken)
                .ConfigureAwait(false);
            RequireCurrent();
            LocalIdentityCredentialOperation? operation = await identityDbContext.Set<LocalIdentityCredentialOperation>()
                .AsNoTracking().SingleOrDefaultAsync(row => row.Id == enrollment.CredentialOperationId,
                    cancellationToken).ConfigureAwait(false);
            RequireCurrent();
            if (operation is null)
                return LocalCredentialActivationOutcome.NotFound;
            if (operation.Kind != LocalCredentialOperationKind.VisitorCreate
                || operation.VisitorEnrollmentOperationId != enrollment.Id
                || operation.LocalSubjectId != enrollment.LocalSubjectId
                || operation.PersonalActorId != enrollment.PersonalActorId
                || operation.ExternalLoginId != enrollment.ExternalLoginId
                || operation.ConcurrencyStamp != expectedOperationConcurrencyStamp
                || operation.InitiatingApplicationUserId.HasValue
                || operation.VerifiedByApplicationUserId.HasValue || operation.VerifiedAt.HasValue
                || operation.PreviousOperationId.HasValue || operation.PreviousOperationConcurrencyStamp.HasValue
                || operation.ResetReason is not null)
                return LocalCredentialActivationOutcome.Conflict;

            IdentityUserToken<Guid>? metadataToken = await identityDbContext.Set<IdentityUserToken<Guid>>()
                .AsNoTracking().SingleOrDefaultAsync(row => row.UserId == enrollment.LocalSubjectId
                    && row.LoginProvider == LocalCredentialStateMetadata.TokenLoginProvider
                    && row.Name == LocalCredentialStateMetadata.TokenName, cancellationToken).ConfigureAwait(false);
            RequireCurrent();
            LocalCredentialStateMetadata? metadata = DeserializeMetadata(metadataToken?.Value);
            LocalIdentityEligibility? eligibility = await identityDbContext.Set<LocalIdentityEligibility>()
                .AsNoTracking().SingleOrDefaultAsync(row => row.LocalSubjectId == enrollment.LocalSubjectId,
                    cancellationToken).ConfigureAwait(false);
            RequireCurrent();
            LocalIdentityUser? user = await identityDbContext.Set<LocalIdentityUser>().AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == enrollment.LocalSubjectId, cancellationToken).ConfigureAwait(false);
            RequireCurrent();
            DateTime now = timeProvider.GetUtcNow().UtcDateTime;
            if (!IsCurrentMetadata(operation, metadata)
                || eligibility is null || eligibility.CurrentOperationId != operation.Id
                || eligibility.PersonalActorId != enrollment.PersonalActorId
                || eligibility.ExternalLoginId != enrollment.ExternalLoginId
                || user is null || string.IsNullOrWhiteSpace(user.PasswordHash)
                || string.IsNullOrWhiteSpace(user.SecurityStamp) || string.IsNullOrWhiteSpace(user.ConcurrencyStamp)
                || user.LockoutEnd > timeProvider.GetUtcNow()
                || eligibility.LockoutUntilUtc > now
                || !enrollment.UpdatedAt.HasValue || enrollment.UpdatedAt.Value > now)
                return LocalCredentialActivationOutcome.Conflict;

            bool pending = enrollment.Stage == VisitorEnrollmentStage.PrimaryAdmitted
                && operation.Stage == LocalCredentialOperationStage.ProvisioningPending
                && metadata!.State == LocalCredentialState.RegistrationPending
                && eligibility.State == LocalIdentityEligibilityState.ProvisioningPending
                && eligibility.Generation == expectedEligibilityGeneration;
            bool activated = operation.Stage == LocalCredentialOperationStage.Replaced
                && metadata!.State == LocalCredentialState.Ready
                && eligibility.State == LocalIdentityEligibilityState.Ready
                && eligibility.Generation > expectedEligibilityGeneration
                && operation.UpdatedAt.HasValue
                && operation.UpdatedAt <= now
                && eligibility.UpdatedAt >= operation.UpdatedAt
                && eligibility.UpdatedAt <= now;
            if (!pending && !activated)
                return LocalCredentialActivationOutcome.Conflict;
            if (!await HasExactApplicationBindingAsync(operation, cancellationToken).ConfigureAwait(false))
                return LocalCredentialActivationOutcome.BindingIncomplete;
            RequireCurrent();
            bool membership = await applicationDbContext.Set<TenantUser>().AsNoTracking()
                .AnyAsync(row => row.Id == enrollment.TenantUserId && row.TenantId == tenant
                    && row.UserId == enrollment.LocalSubjectId && row.ActorId == enrollment.PersonalActorId
                    && row.StatusId == (int)TenantUserStatusEnum.Active && !row.IsDeleted,
                    cancellationToken).ConfigureAwait(false);
            RequireCurrent();
            if (!membership)
                return LocalCredentialActivationOutcome.BindingIncomplete;
            if (activated)
                return LocalCredentialActivationOutcome.AlreadyActivated;

            string ready = JsonSerializer.Serialize(new LocalCredentialStateMetadata(
                LocalCredentialStateMetadata.CurrentVersion, LocalCredentialState.Ready,
                operation.Id, operation.LocalSubjectId));
            // Keep this operation's original stamp as the exact activation replay witness.
            // Its stage, metadata and eligibility generation are all part of the source CAS.
            int operationRows = await identityDbContext.Set<LocalIdentityCredentialOperation>()
                .Where(row => row.Id == operation.Id && row.Kind == LocalCredentialOperationKind.VisitorCreate
                    && row.Stage == LocalCredentialOperationStage.ProvisioningPending
                    && row.ConcurrencyStamp == expectedOperationConcurrencyStamp
                    && row.VisitorEnrollmentOperationId == enrollment.Id
                    && row.LocalSubjectId == enrollment.LocalSubjectId
                    && row.PersonalActorId == enrollment.PersonalActorId
                    && row.ExternalLoginId == enrollment.ExternalLoginId
                    && row.InitiatingApplicationUserId == null && row.VerifiedByApplicationUserId == null
                    && row.VerifiedAt == null && row.PreviousOperationId == null
                    && row.PreviousOperationConcurrencyStamp == null && row.ResetReason == null)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(row => row.Stage, LocalCredentialOperationStage.Replaced)
                    .SetProperty(row => row.UpdatedAt, now), cancellationToken).ConfigureAwait(false);
            RequireCurrent();
            int metadataRows = await identityDbContext.Set<IdentityUserToken<Guid>>()
                .Where(row => row.UserId == enrollment.LocalSubjectId
                    && row.LoginProvider == LocalCredentialStateMetadata.TokenLoginProvider
                    && row.Name == LocalCredentialStateMetadata.TokenName && row.Value == metadataToken!.Value)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, ready), cancellationToken)
                .ConfigureAwait(false);
            RequireCurrent();
            if (operationRows != 1 || metadataRows != 1)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return LocalCredentialActivationOutcome.Conflict;
            }
            await ChangeEligibilityAsync(operation.LocalSubjectId, operation.Id,
                row => row.CompleteVisitorAdmission(expectedEligibilityGeneration, operation.Id, now),
                cancellationToken).ConfigureAwait(false);
            RequireCurrent();
            await transaction.CommitAsync(cancellationToken, () =>
            {
                lifecycle.RequireCurrent();
                RequireVisitorPreparationOwner(tenant);
            }).ConfigureAwait(false);
            RequireVisitorPreparationOwner(tenant);
            return LocalCredentialActivationOutcome.Activated;
        }
        catch
        {
            await RollbackBestEffortAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }
}

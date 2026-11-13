using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Domain;
using Explore.Domain.Authentication;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Identity;

internal sealed partial class LocalIdentityCredentialStateStore
{
    internal async Task<LocalCredentialProvisioningSnapshot?> PrepareVisitorAsync(
        Guid enrollmentOperationId, string username, string password, string? email,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Guid tenant = RequireVisitorPreparationOwner();
        if (enrollmentOperationId == Guid.Empty)
            throw new ArgumentException("An enrollment operation is required.", nameof(enrollmentOperationId));
        EnsureReconciliationContextsIdle();
        if (string.IsNullOrWhiteSpace(username)
            || !LocalPasswordPolicy.TryNormalizeNewCredential(password, out _))
            return null;

        int entered = 0;
        LocalCredentialProvisioningSnapshot? result = await ExecuteLifecycleMutationAsync(
            (lifecycle, token) =>
            {
                // A lost acknowledgement requires a fresh, exact replay read, never another write attempt.
                if (Interlocked.Exchange(ref entered, 1) != 0)
                    throw new InvalidOperationException("Visitor credential preparation cannot be replayed within its owner.");
                RequireVisitorPreparationOwner(tenant);
                return PrepareVisitorAttemptAsync(enrollmentOperationId, tenant, username, password, email,
                    lifecycle, token);
            }, cancellationToken).ConfigureAwait(false);
        RequireVisitorPreparationOwner(tenant);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private Guid RequireVisitorPreparationOwner(Guid? originalTenant = null)
    {
        if (executionLease is null)
            throw new UnauthorizedAccessException("Visitor preparation requires the original selected Local identity owner.");
        executionLease.RequireCurrent(applicationDbContext, identityDbContext);
        RequireSelectedLifecycleCurrent();
        EventDiscoveryEnrollmentGate.RequireCurrent(applicationDbContext);
        Guid tenant = applicationDbContext.TenantFilterTenantId
            ?? throw new UnauthorizedAccessException("Visitor preparation requires a selected tenant.");
        if (tenant == Guid.Empty || originalTenant.HasValue && tenant != originalTenant.Value)
            throw new UnauthorizedAccessException("The original visitor tenant changed.");
        return tenant;
    }

    private async Task<LocalCredentialProvisioningSnapshot?> PrepareVisitorAttemptAsync(
        Guid enrollmentOperationId, Guid tenant, string username, string password, string? email,
        LocalIdentityLifecycleExecutionCustody lifecycle, CancellationToken cancellationToken)
    {
        List<object> ownedEntities = [];
        RequireVisitorPreparationOwner(tenant);
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
                throw new UnauthorizedAccessException("Visitor preparation lost its original application transaction.");
        }

        try
        {
            RequireCurrent();
            VisitorEnrollmentOperation enrollment = await applicationDbContext.Set<VisitorEnrollmentOperation>()
                .AsNoTracking().SingleOrDefaultAsync(row => row.Id == enrollmentOperationId && row.TenantId == tenant,
                    cancellationToken).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("The reserved visitor enrollment is unavailable.");
            RequireCurrent();
            RequireVisitorReservation(enrollment, tenant);
            await identityFence.EnsureSubjectMayEnrollAsync(enrollment.LocalSubjectId, cancellationToken)
                .ConfigureAwait(false);
            RequireCurrent();

            LocalIdentityCredentialOperation? operation = await identityDbContext
                .Set<LocalIdentityCredentialOperation>().AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == enrollment.CredentialOperationId, cancellationToken)
                .ConfigureAwait(false);
            RequireCurrent();
            LocalIdentityUser? user = await identityDbContext.Set<LocalIdentityUser>().AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == enrollment.LocalSubjectId, cancellationToken)
                .ConfigureAwait(false);
            RequireCurrent();
            LocalIdentityEligibility? eligibility = await identityDbContext.Set<LocalIdentityEligibility>()
                .AsNoTracking().SingleOrDefaultAsync(row => row.LocalSubjectId == enrollment.LocalSubjectId,
                    cancellationToken).ConfigureAwait(false);
            RequireCurrent();

            if (operation is not null)
            {
                if (operation.Kind != LocalCredentialOperationKind.VisitorCreate
                    || operation.Stage != LocalCredentialOperationStage.ProvisioningPending
                    || operation.VisitorEnrollmentOperationId != enrollment.Id
                    || operation.LocalSubjectId != enrollment.LocalSubjectId
                    || operation.PersonalActorId != enrollment.PersonalActorId
                    || operation.ExternalLoginId != enrollment.ExternalLoginId
                    || operation.InitiatingApplicationUserId.HasValue
                    || operation.VerifiedByApplicationUserId.HasValue || operation.VerifiedAt.HasValue
                    || operation.PreviousOperationId.HasValue || operation.PreviousOperationConcurrencyStamp.HasValue
                    || operation.ResetReason is not null
                    || user is null || user.EmailConfirmed || string.IsNullOrWhiteSpace(user.PasswordHash)
                    || string.IsNullOrWhiteSpace(user.SecurityStamp) || string.IsNullOrWhiteSpace(user.ConcurrencyStamp)
                    || eligibility is null || eligibility.CurrentOperationId != operation.Id
                    || eligibility.PersonalActorId != enrollment.PersonalActorId
                    || eligibility.ExternalLoginId != enrollment.ExternalLoginId
                    || eligibility.State != LocalIdentityEligibilityState.ProvisioningPending
                    || enrollment.CredentialGeneration.HasValue
                        && enrollment.CredentialGeneration.Value != eligibility.Generation)
                    throw new UnauthorizedAccessException("The reserved visitor credential binding changed.");

                string? metadataValue = await identityDbContext.Set<IdentityUserToken<Guid>>().AsNoTracking()
                    .Where(row => row.UserId == user.Id
                        && row.LoginProvider == LocalCredentialStateMetadata.TokenLoginProvider
                        && row.Name == LocalCredentialStateMetadata.TokenName)
                    .Select(row => row.Value).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                RequireCurrent();
                LocalCredentialStateMetadata? metadata = DeserializeMetadata(metadataValue);
                if (metadata?.State != LocalCredentialState.RegistrationPending || !IsCurrentMetadata(operation, metadata))
                    throw new UnauthorizedAccessException("The reserved visitor credential metadata changed.");
                if (!string.Equals(user.NormalizedUserName, userManager.NormalizeName(username), StringComparison.Ordinal)
                    || !string.Equals(user.NormalizedEmail, userManager.NormalizeEmail(email), StringComparison.Ordinal))
                    throw new UnauthorizedAccessException("The reserved visitor identifiers changed.");
                if (userManager.PasswordHasher.VerifyHashedPassword(user, user.PasswordHash, password)
                    == PasswordVerificationResult.Failed)
                    return null;
            }
            else
            {
                if (enrollment.Stage != VisitorEnrollmentStage.Reserved || user is not null || eligibility is not null)
                    throw new UnauthorizedAccessException("The reserved visitor credential binding is unavailable.");
                bool occupied = await identityDbContext.Set<LocalIdentityCredentialOperation>().AsNoTracking()
                    .AnyAsync(row => row.LocalSubjectId == enrollment.LocalSubjectId
                        || row.VisitorEnrollmentOperationId == enrollment.Id
                        || row.PersonalActorId == enrollment.PersonalActorId
                        || row.ExternalLoginId == enrollment.ExternalLoginId, cancellationToken).ConfigureAwait(false);
                RequireCurrent();
                if (occupied)
                    throw new UnauthorizedAccessException("The reserved visitor identifiers are already bound.");

                DateTime createdAt = timeProvider.GetUtcNow().UtcDateTime;
                user = new LocalIdentityUser
                {
                    Id = enrollment.LocalSubjectId,
                    UserName = username,
                    Email = email,
                    FirstName = username,
                    LastName = string.Empty,
                    EmailConfirmed = false,
                    LockoutEnabled = true,
                    CreatedAt = createdAt
                };
                ownedEntities.Add(user);
                RequireVisitorReservation(enrollment, tenant);
                IdentityResult creation = await userManager.CreateAsync(user, password).ConfigureAwait(false);
                RequireCurrent();
                if (!creation.Succeeded)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    RequireVisitorPreparationOwner(tenant);
                    return null;
                }
                RequireVisitorReservation(enrollment, tenant);
                var receipt = new LocalCredentialOperationReceipt(
                    operationId: enrollment.CredentialOperationId,
                    kind: LocalCredentialOperationKind.VisitorCreate,
                    stage: LocalCredentialOperationStage.ProvisioningPending,
                    initiatingApplicationUserId: null,
                    localSubjectId: enrollment.LocalSubjectId,
                    personalActorId: enrollment.PersonalActorId,
                    externalLoginId: enrollment.ExternalLoginId,
                    createdAt: createdAt,
                    visitorEnrollmentOperationId: enrollment.Id);
                operation = new LocalIdentityCredentialOperation(receipt, Guid.CreateVersion7());
                var metadata = new IdentityUserToken<Guid>
                {
                    UserId = user.Id,
                    LoginProvider = LocalCredentialStateMetadata.TokenLoginProvider,
                    Name = LocalCredentialStateMetadata.TokenName,
                    Value = JsonSerializer.Serialize(new LocalCredentialStateMetadata(
                        version: LocalCredentialStateMetadata.CurrentVersion,
                        state: LocalCredentialState.RegistrationPending,
                        operationId: operation.Id,
                        applicationUserId: user.Id))
                };
                ownedEntities.Add(operation);
                ownedEntities.Add(metadata);
                identityDbContext.AddRange(operation, metadata);
                await identityDbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                RequireCurrent();
                await AddEligibilityAsync(user.Id, operation.Id, enrollment.PersonalActorId,
                    enrollment.ExternalLoginId, createdAt, cancellationToken).ConfigureAwait(false);
                RequireCurrent();
            }

            VisitorEnrollmentOperation? current = await applicationDbContext.Set<VisitorEnrollmentOperation>()
                .AsNoTracking().SingleOrDefaultAsync(row => row.Id == enrollment.Id && row.TenantId == tenant,
                    cancellationToken).ConfigureAwait(false);
            RequireCurrent();
            if (current is null || current.Generation != enrollment.Generation || current.Stage != enrollment.Stage
                || current.LocalSubjectId != enrollment.LocalSubjectId
                || current.CredentialOperationId != enrollment.CredentialOperationId
                || current.PersonalActorId != enrollment.PersonalActorId
                || current.ExternalLoginId != enrollment.ExternalLoginId
                || current.CredentialGeneration != enrollment.CredentialGeneration
                || current.TenantUserId != enrollment.TenantUserId
                || current.IntentDigest != enrollment.IntentDigest
                || current.CreatedAt != enrollment.CreatedAt || current.ExpiresAt != enrollment.ExpiresAt
                || current.UpdatedAt != enrollment.UpdatedAt)
                throw new UnauthorizedAccessException("The original visitor reservation changed.");
            RequireVisitorReservation(current, tenant);
            var snapshot = new LocalCredentialProvisioningSnapshot(
                receipt: new LocalCredentialOperationReceipt(
                    operationId: operation.Id, kind: operation.Kind, stage: operation.Stage,
                    initiatingApplicationUserId: operation.InitiatingApplicationUserId,
                    localSubjectId: operation.LocalSubjectId, personalActorId: operation.PersonalActorId,
                    externalLoginId: operation.ExternalLoginId, createdAt: operation.CreatedAt,
                    visitorEnrollmentOperationId: operation.VisitorEnrollmentOperationId),
                operationConcurrencyStamp: operation.ConcurrencyStamp,
                email: user!.Email, firstName: user.FirstName, lastName: user.LastName,
                emailVerified: user.EmailConfirmed, username: user.UserName);
            RequireCurrent();
            await transaction.CommitAsync(cancellationToken, () =>
            {
                // This guard also runs at a deferred external-identity commit, after primary settlement.
                lifecycle.RequireCurrent();
                RequireVisitorReservation(enrollment, tenant);
            }).ConfigureAwait(false);
            RequireVisitorPreparationOwner(tenant);
            return snapshot;
        }
        finally
        {
            foreach (object entity in ownedEntities)
                identityDbContext.Entry(entity).State = EntityState.Detached;
        }
    }

    private void RequireVisitorReservation(VisitorEnrollmentOperation enrollment, Guid tenant)
    {
        RequireVisitorPreparationOwner(tenant);
        if (enrollment.TenantId != tenant
            || enrollment.Stage is not (VisitorEnrollmentStage.Reserved or VisitorEnrollmentStage.IdentityPrepared)
            || timeProvider.GetUtcNow().UtcDateTime >= enrollment.ExpiresAt
            || enrollment.TenantUserId.HasValue
            || enrollment.Stage == VisitorEnrollmentStage.Reserved && enrollment.CredentialGeneration.HasValue
            || enrollment.Stage == VisitorEnrollmentStage.IdentityPrepared && !enrollment.CredentialGeneration.HasValue)
            throw new UnauthorizedAccessException("An unexpired pending visitor reservation is required.");
    }
}

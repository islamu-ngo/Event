using System.Data;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Identity;

internal sealed partial class LocalIdentityCredentialStateStore
{
    internal Task<VisitorEnrollmentOperation?> RecordVisitorPreparedAsync(
        Guid enrollmentOperationId, CancellationToken cancellationToken) =>
        ExecuteVisitorPrimaryAsync(enrollmentOperationId, null, cancellationToken);

    internal Task<VisitorEnrollmentOperation?> AdmitVisitorAsync(
        Guid enrollmentOperationId, Func<CancellationToken, Task> authorizeNewMembership,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorizeNewMembership);
        return ExecuteVisitorPrimaryAsync(enrollmentOperationId, authorizeNewMembership, cancellationToken);
    }

    private async Task<VisitorEnrollmentOperation?> ExecuteVisitorPrimaryAsync(
        Guid enrollmentOperationId, Func<CancellationToken, Task>? authorizeNewMembership,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Guid tenant = RequireVisitorPreparationOwner();
        if (enrollmentOperationId == Guid.Empty)
            throw new ArgumentException("The original visitor enrollment is required.", nameof(enrollmentOperationId));
        EnsureReconciliationContextsIdle();
        var settings = new RelationalSettingMutationLock(applicationDbContext, new EfCoreUnitOfWork(applicationDbContext));
        int entered = 0;
        // Policy writers acquire these same complete locks before their primary transaction.
        // In particular, acquire them before the privacy gate can open a co-located transaction.
        VisitorEnrollmentOperation? result = await settings.ExecuteOrderedGroupsOnceAsync(
            [VisitorAccessCapabilityResolver.AuthoritySettingKeys],
            token => ExecuteLifecycleMutationAsync((lifecycle, inner) =>
            {
                if (Interlocked.Exchange(ref entered, 1) != 0)
                    throw new InvalidOperationException("Visitor primary settlement cannot replay a mutation.");
                RequireVisitorPreparationOwner(tenant);
                return SettleVisitorPrimaryAsync(enrollmentOperationId, tenant, authorizeNewMembership,
                    lifecycle, inner);
            }, token), cancellationToken).ConfigureAwait(false);
        RequireVisitorPreparationOwner(tenant);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private async Task<VisitorEnrollmentOperation?> SettleVisitorPrimaryAsync(
        Guid enrollmentOperationId, Guid tenant, Func<CancellationToken, Task>? authorizeNewMembership,
        LocalIdentityLifecycleExecutionCustody lifecycle, CancellationToken token)
    {
        List<object> owned = [];
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
                throw new UnauthorizedAccessException("Visitor admission requires its original Serializable primary owner.");
        }

        try
        {
            RequireCurrent();
            var enrollment = await ReadVisitorPrimaryReceiptAsync(enrollmentOperationId, tenant, token)
                .ConfigureAwait(false);
            RequireCurrent();
            if (enrollment is null) return null;
            bool admitted = enrollment.Stage is VisitorEnrollmentStage.PrimaryAdmitted or VisitorEnrollmentStage.Active;
            if (!admitted)
                RequireVisitorReservation(enrollment, tenant);
            else if (!enrollment.TenantUserId.HasValue || !enrollment.CredentialGeneration.HasValue)
                throw new UnauthorizedAccessException("Visitor admission evidence is incomplete.");
            await identityFence.EnsureSubjectMayEnrollAsync(enrollment.LocalSubjectId, token).ConfigureAwait(false);
            RequireCurrent();
            var evidence = await ReadVisitorNativeEvidenceAsync(enrollment, admitted, token).ConfigureAwait(false);
            RequireCurrent();

            if (admitted)
            {
                if (!await HasExactApplicationBindingAsync(evidence.Operation, token).ConfigureAwait(false)
                    || !await applicationDbContext.Set<TenantUser>().AsNoTracking().AnyAsync(
                        row => row.Id == enrollment.TenantUserId && row.TenantId == tenant
                            && row.UserId == enrollment.LocalSubjectId && row.ActorId == enrollment.PersonalActorId
                            && row.StatusId == (int)TenantUserStatusEnum.Active && !row.IsDeleted, token)
                        .ConfigureAwait(false))
                    throw new UnauthorizedAccessException("The committed visitor account or membership changed.");
                RequireCurrent();
                await identityFence.MatchAndBindAsync(
                    new ProviderAccountKey(AuthenticationProviderKind.Local, enrollment.LocalSubjectId.ToString("D")),
                    enrollment.LocalSubjectId, enrollment.ExternalLoginId, tenant, replacementProvisioning: false, token)
                    .ConfigureAwait(false);
            }
            else if (authorizeNewMembership is null)
            {
                if (enrollment.Stage == VisitorEnrollmentStage.Reserved)
                {
                    applicationDbContext.Attach(enrollment);
                    owned.Add(enrollment);
                    enrollment.RecordIdentityPrepared(enrollment.Generation, enrollment.CredentialOperationId,
                        evidence.Eligibility.Generation, timeProvider.GetUtcNow().UtcDateTime);
                    await applicationDbContext.SaveChangesAsync(token).ConfigureAwait(false);
                }
            }
            else
            {
                if (enrollment.Stage != VisitorEnrollmentStage.IdentityPrepared)
                    throw new UnauthorizedAccessException("Durable identity preparation is required before admission.");
                string providerKey = enrollment.LocalSubjectId.ToString("D");
                // No adoption or repair of partial accounts, actors, logins or memberships.
                if (await applicationDbContext.Users.AsNoTracking()
                        .AnyAsync(row => row.Id == enrollment.LocalSubjectId, token).ConfigureAwait(false)
                    || await applicationDbContext.Actors.AsNoTracking().AnyAsync(
                        row => row.Id == enrollment.PersonalActorId || row.UserId == enrollment.LocalSubjectId, token)
                        .ConfigureAwait(false)
                    || await applicationDbContext.UserExternalLogins.AsNoTracking().AnyAsync(
                        row => row.Id == enrollment.ExternalLoginId || row.UserId == enrollment.LocalSubjectId
                            || row.AuthenticationProviderId == (int)AuthenticationProviderKind.Local
                                && row.ProviderKey == providerKey, token).ConfigureAwait(false)
                    || await applicationDbContext.Set<TenantUser>().AsNoTracking().AnyAsync(
                        row => row.TenantId == tenant && row.UserId == enrollment.LocalSubjectId, token)
                        .ConfigureAwait(false))
                    throw new UnauthorizedAccessException("The visitor projection identifiers are already occupied.");
                RequireCurrent();

                // This is only the final Application-owned new membership policy.
                // It executes with all retained authority locks and the original primary transaction.
                await authorizeNewMembership(token).ConfigureAwait(false);
                RequireCurrent();
                var current = await ReadVisitorPrimaryReceiptAsync(enrollment.Id, tenant, token).ConfigureAwait(false);
                RequireCurrent();
                if (current is null || !SameVisitorPrimaryReceipt(enrollment, current)
                    || applicationDbContext.ChangeTracker.HasChanges() || identityDbContext.ChangeTracker.HasChanges())
                    throw new UnauthorizedAccessException("Visitor admission lost its original prepared evidence.");
                RequireVisitorReservation(current, tenant);
                var finalEvidence = await ReadVisitorNativeEvidenceAsync(current, false, token).ConfigureAwait(false);
                RequireCurrent();
                if (finalEvidence.Operation.ConcurrencyStamp != evidence.Operation.ConcurrencyStamp
                    || finalEvidence.User.ConcurrencyStamp != evidence.User.ConcurrencyStamp
                    || finalEvidence.User.SecurityStamp != evidence.User.SecurityStamp
                    || finalEvidence.User.PasswordHash != evidence.User.PasswordHash)
                    throw new UnauthorizedAccessException("The selected native visitor account changed.");
                await identityFence.EnsureSubjectMayEnrollAsync(enrollment.LocalSubjectId, token).ConfigureAwait(false);
                RequireCurrent();

                DateTime now = timeProvider.GetUtcNow().UtcDateTime;
                var user = new User
                {
                    Id = enrollment.LocalSubjectId, CreatedAt = now,
                    EmailVerified = finalEvidence.User.EmailConfirmed,
                    Pii = new UserPii
                    {
                        Email = finalEvidence.User.Email ?? string.Empty,
                        FirstName = finalEvidence.User.FirstName, LastName = finalEvidence.User.LastName
                    }
                };
                owned.Add(user);
                applicationDbContext.Users.Add(user);
                await applicationDbContext.SaveChangesAsync(token).ConfigureAwait(false);
                RequireCurrent();
                var actor = new Actor
                {
                    Id = enrollment.PersonalActorId, UserId = user.Id, User = user,
                    ActorTypeId = (int)ActorTypeEnum.User, ActorType = null!, CreatedAt = now,
                    Pii = new ActorPii
                    {
                        DisplayName = $"{finalEvidence.User.FirstName} {finalEvidence.User.LastName}".Trim()
                    }
                };
                owned.Add(actor);
                applicationDbContext.Actors.Add(actor);
                await applicationDbContext.SaveChangesAsync(token).ConfigureAwait(false);
                RequireCurrent();
                var login = new UserExternalLogin
                {
                    Id = enrollment.ExternalLoginId, UserId = user.Id, User = user,
                    AuthenticationProviderId = (int)AuthenticationProviderKind.Local, AuthenticationProvider = null!,
                    ProviderKey = providerKey, ProviderDisplayName = "Local", CreatedAt = now
                };
                owned.Add(login);
                applicationDbContext.UserExternalLogins.Add(login);
                await applicationDbContext.SaveChangesAsync(token).ConfigureAwait(false);
                RequireCurrent();
                await identityFence.MatchAndBindAsync(
                    new ProviderAccountKey(AuthenticationProviderKind.Local, providerKey),
                    user.Id, login.Id, tenant, replacementProvisioning: true, token).ConfigureAwait(false);
                RequireCurrent();
                var member = new TenantUser
                {
                    Id = Guid.CreateVersion7(), TenantId = tenant, Tenant = null!,
                    UserId = user.Id, User = user, ActorId = actor.Id, Actor = actor,
                    StatusId = (int)TenantUserStatusEnum.Active, JoinedAt = now, CreatedAt = now
                };
                owned.Add(member);
                applicationDbContext.Set<TenantUser>().Add(member);
                applicationDbContext.Attach(enrollment);
                owned.Add(enrollment);
                enrollment.RecordPrimaryAdmission(enrollment.Generation, enrollment.CredentialOperationId,
                    member.Id, timeProvider.GetUtcNow().UtcDateTime);
                await applicationDbContext.SaveChangesAsync(token).ConfigureAwait(false);
            }
            RequireCurrent();
            await transaction.CommitAsync(token, () =>
            {
                lifecycle.RequireCurrent();
                RequireVisitorPreparationOwner(tenant);
                if (enrollment.Stage == VisitorEnrollmentStage.IdentityPrepared)
                    RequireVisitorReservation(enrollment, tenant);
                if (!admitted && timeProvider.GetUtcNow().UtcDateTime >= enrollment.ExpiresAt)
                    throw new UnauthorizedAccessException("The visitor reservation expired before settlement.");
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
            foreach (object entity in owned)
                applicationDbContext.Entry(entity).State = EntityState.Detached;
        }
    }

    private Task<VisitorEnrollmentOperation?> ReadVisitorPrimaryReceiptAsync(Guid id, Guid tenant, CancellationToken token) =>
        applicationDbContext.Set<VisitorEnrollmentOperation>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == id && row.TenantId == tenant, token);

    private static bool SameVisitorPrimaryReceipt(VisitorEnrollmentOperation original, VisitorEnrollmentOperation current) =>
        original.Id == current.Id && original.TenantId == current.TenantId
        && original.LocalSubjectId == current.LocalSubjectId && original.PersonalActorId == current.PersonalActorId
        && original.ExternalLoginId == current.ExternalLoginId && original.CredentialOperationId == current.CredentialOperationId
        && original.CredentialGeneration == current.CredentialGeneration && original.TenantUserId == current.TenantUserId
        && original.IntentDigest == current.IntentDigest && original.Stage == current.Stage
        && original.Generation == current.Generation && original.CreatedAt == current.CreatedAt
        && original.ExpiresAt == current.ExpiresAt && original.UpdatedAt == current.UpdatedAt
        && original.CleanupCompletedAt == current.CleanupCompletedAt;

    private async Task<(LocalIdentityCredentialOperation Operation, LocalIdentityUser User, LocalIdentityEligibility Eligibility)>
        ReadVisitorNativeEvidenceAsync(VisitorEnrollmentOperation enrollment, bool admitted, CancellationToken token)
    {
        var operation = await identityDbContext.Set<LocalIdentityCredentialOperation>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == enrollment.CredentialOperationId, token).ConfigureAwait(false);
        RequireVisitorPreparationOwner(enrollment.TenantId);
        var user = await identityDbContext.Set<LocalIdentityUser>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == enrollment.LocalSubjectId, token).ConfigureAwait(false);
        RequireVisitorPreparationOwner(enrollment.TenantId);
        var eligibility = await identityDbContext.Set<LocalIdentityEligibility>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.LocalSubjectId == enrollment.LocalSubjectId, token).ConfigureAwait(false);
        RequireVisitorPreparationOwner(enrollment.TenantId);
        var metadataToken = await identityDbContext.Set<IdentityUserToken<Guid>>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.UserId == enrollment.LocalSubjectId
                && row.LoginProvider == LocalCredentialStateMetadata.TokenLoginProvider
                && row.Name == LocalCredentialStateMetadata.TokenName, token).ConfigureAwait(false);
        RequireVisitorPreparationOwner(enrollment.TenantId);
        var metadata = DeserializeMetadata(metadataToken?.Value);
        if (operation is null || operation.Kind != LocalCredentialOperationKind.VisitorCreate
            || operation.ConcurrencyStamp == Guid.Empty
            || operation.VisitorEnrollmentOperationId != enrollment.Id
            || operation.LocalSubjectId != enrollment.LocalSubjectId
            || operation.PersonalActorId != enrollment.PersonalActorId
            || operation.ExternalLoginId != enrollment.ExternalLoginId
            || operation.InitiatingApplicationUserId.HasValue || operation.VerifiedByApplicationUserId.HasValue
            || operation.VerifiedAt.HasValue || operation.PreviousOperationId.HasValue
            || operation.PreviousOperationConcurrencyStamp.HasValue || operation.ResetReason is not null
            || !IsCurrentMetadata(operation, metadata)
            || user is null || string.IsNullOrWhiteSpace(user.PasswordHash)
            || string.IsNullOrWhiteSpace(user.SecurityStamp) || string.IsNullOrWhiteSpace(user.ConcurrencyStamp)
            || eligibility is null || eligibility.CurrentOperationId != operation.Id
            || eligibility.Generation <= 0
            || eligibility.PersonalActorId != enrollment.PersonalActorId
            || eligibility.ExternalLoginId != enrollment.ExternalLoginId)
            throw new UnauthorizedAccessException("The original native visitor evidence changed.");

        bool pending = operation.Stage == LocalCredentialOperationStage.ProvisioningPending
            && metadata!.State == LocalCredentialState.RegistrationPending && !user.EmailConfirmed
            && eligibility.State == LocalIdentityEligibilityState.ProvisioningPending
            && (!enrollment.CredentialGeneration.HasValue || enrollment.CredentialGeneration == eligibility.Generation)
            && !(user.LockoutEnd > timeProvider.GetUtcNow())
            && !(eligibility.LockoutUntilUtc > timeProvider.GetUtcNow().UtcDateTime);
        bool ready = admitted && operation.Stage == LocalCredentialOperationStage.Replaced
            && metadata!.State == LocalCredentialState.Ready
            && eligibility.State == LocalIdentityEligibilityState.Ready
            && eligibility.Generation > enrollment.CredentialGeneration
            && operation.UpdatedAt.HasValue && eligibility.UpdatedAt >= operation.UpdatedAt
            && eligibility.UpdatedAt <= timeProvider.GetUtcNow().UtcDateTime;
        if ((!pending && !ready) || enrollment.Stage == VisitorEnrollmentStage.Active && !ready)
            throw new UnauthorizedAccessException("The original visitor credential generation is unavailable.");
        return (operation, user, eligibility);
    }
}

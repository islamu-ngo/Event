using System.Data;
using System.Data.Common;
using Explore.Domain;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationAdjudicatedCaseReadGrantRepository
{
    /// <summary>
    /// Acquires metadata-only read authority before the caller takes other native locks.
    /// The caller owns privacy serialization, the Serializable transaction and trust-frame cleanup.
    /// </summary>
    internal async Task<ReadAcquisition?> AcquireCurrentInTransactionAsync(Guid grantId, Guid caseId,
        long expectedCaseRevision, CancellationToken cancellationToken)
    {
        try
        {
            if (!context.Database.IsRelational() || context.Database.CurrentTransaction is not { } transaction
                || context.ChangeTracker.HasChanges() || context.ModerationTrustDomainId is not null
                || context.IsTenantFilterBypassed || context.TenantFilterTenantId is not { } tenant
                || tenant == Guid.Empty || machine.IsMachineCaller || !Uuid(grantId)
                || caseId == Guid.Empty || expectedCaseRevision is < 1 or long.MaxValue) return null;
            var native = transaction.GetDbTransaction();
            var connection = context.Database.GetDbConnection();
            var transactionId = transaction.TransactionId;
            var acquiredAt = clock.GetUtcNow();

            bool TransactionCurrent() => ReferenceEquals(context.Database.CurrentTransaction, transaction)
                && transaction.TransactionId == transactionId
                && ReferenceEquals(transaction.GetDbTransaction(), native)
                && native.IsolationLevel == IsolationLevel.Serializable
                && ReferenceEquals(context.Database.GetDbConnection(), connection)
                && ReferenceEquals(native.Connection, connection) && connection.State == ConnectionState.Open;

            if (!TransactionCurrent() || await identity.ResolveUserIdAsync(cancellationToken) is not { } caller
                || caller == Guid.Empty || !await AccountCurrentAsync(caller, cancellationToken)) return null;

            // Discover the entire Human set without retained planning locks or private payloads.
            var plan = await ReadPlanAsync(grantId, tenant, caseId, cancellationToken);
            if (plan?.Grant is not { } planned || plan.Case.Subject is not { } subject
                || planned.RecipientUserId != caller || planned.CaseId != caseId
                || planned.CaseRevision != expectedCaseRevision || plan.Case.Id != caseId
                || plan.Case.Revision != expectedCaseRevision || !planned.IsCurrentAt(InstantNow())) return null;
            var humans = new[] { caller, planned.IssuerUserId, planned.RecipientUserId }
                .Concat(subject.Kind == ModerationSubjectKind.Human ? new[] { subject.Id } : [])
                .Distinct().Order().ToArray();
            await using var installation = await RelationalNamedLock.AcquireTransactionAsync(
                context, "moderation.authority-source.installation", cancellationToken);
            var instance = AuthorityScope.Instance(AuthorityScope.Solution(
                AuthorityScope.TrustDomain(plan.Host.TrustDomainId), plan.Host.SolutionId), plan.Host.InstanceId);
            var qualification = new ModerationQualificationAuthority(context, identity, machine);
            var authority = await qualification.AcquireAdjudicatedReadAsync(
                AuthorityScope.Tenant(instance, tenant), humans, cancellationToken);
            if (authority is null || authority.UserId != caller || authority.Binding.Id != plan.Host.Id
                || authority.Binding.TrustDomainId != plan.Host.TrustDomainId
                || authority.Binding.SolutionId != plan.Host.SolutionId || authority.Binding.InstanceId != plan.Host.InstanceId
                || authority.Binding.MaintenanceServicePrincipalId != plan.Host.MaintenanceServicePrincipalId)
                return null;
            context.ModerationTrustDomainId = authority.Binding.TrustDomainId;
            await using var operationFence = await RelationalNamedLock.AcquireTransactionAsync(
                context, $"moderation.adjudicated-read:{tenant:N}:{grantId:N}", cancellationToken);
            await RelationalEntityRowFence.AcquireAsync<ModerationAdjudicatedCaseReadGrant>(
                context, tenant, row => row.Id, grantId, cancellationToken);
            var grant = await context.Set<ModerationAdjudicatedCaseReadGrant>().AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == grantId, cancellationToken);
            var snapshot = Snapshot(planned);
            if (grant is null || Snapshot(grant) != snapshot) return null;
            var administrator = await AdministratorAsync(grant.IssuerUserId, grant.IssuerRoleGrantId,
                grant.IssuerPlatformGrant, tenant, cancellationToken);
            if (administrator is null) return null;
            var epochId = authority.Epoch.Id;
            var epochRevision = authority.Epoch.Revision;
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationCase>(context, caseId, cancellationToken);

            bool FrameCurrent() => TransactionCurrent() && !machine.IsMachineCaller
                && !context.IsTenantFilterBypassed && context.TenantFilterTenantId == tenant
                && context.ModerationTrustDomainId == plan.Host.TrustDomainId;

            async Task<bool> Revalidate(DateTimeOffset? accessUntil, CancellationToken token)
            {
                try
                {
                    if (!FrameCurrent() || Snapshot(grant) != snapshot || clock.GetUtcNow() < acquiredAt
                        || accessUntil is { } beforeRead && clock.GetUtcNow() >= beforeRead
                        || await identity.ResolveUserIdAsync(token) != caller
                        || !await qualification.ProviderTenantMatchesAsync(tenant, token)
                        || !await context.Tenants.AsNoTracking().AnyAsync(row => row.Id == tenant && row.TenantStatus.IsActiveState, token)
                        || !await context.ModerationHostBindings.AsNoTracking().AnyAsync(row => row.Id == plan.Host.Id
                            && row.TrustDomainId == plan.Host.TrustDomainId && row.SolutionId == plan.Host.SolutionId
                            && row.InstanceId == plan.Host.InstanceId
                            && row.MaintenanceServicePrincipalId == plan.Host.MaintenanceServicePrincipalId, token)
                        || !await context.ServicePrincipals.AsNoTracking().AnyAsync(
                            row => row.Id == plan.Host.MaintenanceServicePrincipalId && !row.IsDeleted, token)
                        || !await context.ModerationQualificationEpochs.AsNoTracking().AnyAsync(row => row.Id == epochId
                            && row.HostBindingId == plan.Host.Id && row.TenantId == tenant && row.Revision == epochRevision, token))
                        return false;
                    foreach (var human in humans)
                        if (!await AccountCurrentAsync(human, token)) return false;
                    var actual = await context.Set<ModerationAdjudicatedCaseReadGrant>().AsNoTracking()
                        .SingleOrDefaultAsync(row => row.Id == grantId, token);
                    if (actual is null || Snapshot(actual) != snapshot || !actual.IsCurrentAt(InstantNow())
                        || actual.HostBindingId != plan.Host.Id || actual.TrustDomainId != plan.Host.TrustDomainId
                        || actual.TenantId != tenant || actual.RecipientUserId != caller
                        || await AdministratorAsync(actual.IssuerUserId, actual.IssuerRoleGrantId,
                            actual.IssuerPlatformGrant, tenant, token) != administrator) return false;
                    var readUntil = accessUntil is { } bound && bound < actual.ExpiresAt ? bound : actual.ExpiresAt;
                    var allegation = await context.ModerationCases.AsNoTracking().SingleOrDefaultAsync(row => row.Id == caseId, token);
                    if (allegation?.Subject is null || !CaseMatches(allegation, plan.Case)
                        || allegation.ScopeTrustDomainId != plan.Host.TrustDomainId
                        || await ReadOriginalAsync(caseId, expectedCaseRevision, actual.OriginalEffectId,
                            actual.OriginalSnapshotHash, actual.OriginalEffectBindingHash, authority, token, readUntil) != actual.OriginalRetainUntil)
                        return false;
                    if (await identity.ResolveUserIdAsync(token) != caller
                        || !await qualification.ProviderTenantMatchesAsync(tenant, token) || !FrameCurrent()) return false;
                    // Raw time, sampled after every await, must remain inside both finite purposes.
                    var now = clock.GetUtcNow();
                    return now >= acquiredAt && now >= actual.IssuedAt && now < readUntil && now < actual.OriginalRetainUntil;
                }
                catch (DbException) { return false; }
                catch (InvalidOperationException) { return false; }
                catch (ArgumentException) { return false; }
                catch (TimeoutException) { return false; }
            }

            if (context.ChangeTracker.HasChanges() || !await Revalidate(null, cancellationToken)
                || context.ChangeTracker.HasChanges()) return null;
            return new ReadAcquisition(grant, authority, Revalidate);
        }
        catch (DbException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (ArgumentException) { return null; }
        catch (TimeoutException) { return null; }
    }

    internal sealed class ReadAcquisition(
        ModerationAdjudicatedCaseReadGrant grant, ModerationSourceAuthority authority,
        Func<DateTimeOffset?, CancellationToken, Task<bool>> revalidate)
    {
        internal ModerationAdjudicatedCaseReadGrant Grant { get; } = grant;
        internal ModerationSourceAuthority Authority { get; } = authority;
        internal Task<bool> RevalidateAsync(CancellationToken cancellationToken, DateTimeOffset? accessUntil = null) =>
            revalidate(accessUntil, cancellationToken);
    }

    private async Task<ModerationAdjudicatedCaseReadGrant?> ReadCurrentOwnedAsync(Guid grantId, Guid caseId,
        long expectedCaseRevision, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Adjudicated-case permission requires its own clean native transaction.");
        if (!context.Database.IsRelational() || context.ModerationTrustDomainId is not null
            || context.IsTenantFilterBypassed || context.TenantFilterTenantId is not { } tenant
            || tenant == Guid.Empty || machine.IsMachineCaller || !Uuid(grantId)
            || caseId == Guid.Empty || expectedCaseRevision is < 1 or long.MaxValue) return null;
        try
        {
            return await EfCoreUnitOfWork.ExecuteBootstrapConflictRetryAsync(async () =>
                await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
                {
                    context.ChangeTracker.Clear();
                    ReadAcquisition? acquired = null;
                    async Task Recheck(CancellationToken token)
                    {
                        if (acquired is not null && !await acquired.RevalidateAsync(token)) throw new Unavailable();
                    }
                    return await privacy.ExecuteEnrollmentSerializedAsync(async privacyToken =>
                        await new EfCoreUnitOfWork(context).ExecuteRecognitionEnrollmentAttemptAsync(async token =>
                        {
                            acquired = await AcquireCurrentInTransactionAsync(grantId, caseId, expectedCaseRevision, token)
                                ?? throw new Unavailable();
                            return acquired.Grant;
                        }, Recheck, privacyToken), Recheck, cancellationToken);
                }), cancellationToken);
        }
        catch (Unavailable) { return null; }
        catch (DbUpdateConcurrencyException) { return null; }
        catch (DbException) { return null; }
        catch (TimeoutException) { return null; }
        finally { context.ChangeTracker.Clear(); context.ModerationTrustDomainId = null; }
    }
}

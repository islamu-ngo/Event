using System.Data;
using System.Data.Common;
using System.Security.Claims;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Services;

/// <summary>
/// Actual existing-Human self-consent for one native independently accepted original assessment.
/// Only source metadata is returned, never authenticated prerequisite or collection receipt.
/// Grant and historical terminal withdrawal retain the real privacy owner's only transaction
/// and identity scope through its true outer beforePrimaryCommit.
/// </summary>
public sealed class ModerationProfileSubjectConsentOwner(
    ExploreDbContext context, IAdminContext admin, IMachinePrincipalAccessor machine,
    IHttpContextAccessor http, IPrivacyIdentityFenceAuthority privacy,
    ModerationCapabilityRegistry inventory, TimeProvider clock) : IModerationProfileSubjectConsentOwner
{
    public async Task<ModerationProfileSubjectConsent?> GrantAsync(
        GrantModerationProfileSubjectConsent request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        RequireCleanOwner();
        if (!EligibleFrame() || request.Assessment?.Original is not { } intent
            || !Enum.IsDefined(intent.Mode) || intent.ProfileId == Guid.Empty)
            return null;
        var originalTrust = context.ModerationTrustDomainId;
        ModerationProfileCollectionIndividualAcquisition? acquisition = null;
        GrantState? retained = null;
        var observations = new ModerationProfileCollectionSubjectObservationOwner(
            context, admin, machine, http, privacy, inventory, clock);
        var assessments = new ModerationProfileCollectionAssessmentRepository(
            context, admin, machine, http, privacy, clock, inventory);
        try
        {
            return await privacy.ExecuteEnrollmentSerializedAsync(async token =>
            {
                token.ThrowIfCancellationRequested();
                context.ChangeTracker.Clear();
                acquisition = await ModerationProfileCollectionIndividualAcquisition.AcquireAsync(
                    context, admin, machine, http, privacy, inventory, clock, intent.ProfileId, intent.Mode, token);
                if (acquisition is null) throw new ConsentUnavailable();
                var observation = await observations.ReadCurrentAsync(acquisition, intent.ObservationId, token);
                // Assessment authors may read another original. Consent may not: actual self
                // equality is separate from their owner/reviewer access to that observation.
                if (observation is null || acquisition.SubjectId != observation.SubjectHumanId)
                    throw new ConsentUnavailable();
                var assessment = await assessments.ReadCurrentAsync(acquisition, request.Assessment, token);
                if (assessment is null || assessment.Source.SubjectHumanId != acquisition.SubjectId
                    || !assessment.Source.Required.Contains(ModerationCollectionPrerequisiteKind.SubjectConsent))
                    throw new ConsentUnavailable();
                var parents = await acquisition.ReadAssessmentParentsAsync(observation, token);
                if (parents is null) throw new ConsentUnavailable();
                await using var slot = await RelationalNamedLock.AcquireTransactionAsync(
                    context, Slot(observation.ProcedureSource, observation.SubjectHumanId), token);
                var generation = await GenerationAsync(observation.ProcedureSource.Id, observation.SubjectHumanId, token);
                if (generation != request.ExpectedGeneration || generation < 0 || generation >= long.MaxValue - 1
                    || await context.Set<ModerationProfileSubjectConsent>().AsNoTracking()
                        .AnyAsync(row => row.Id == request.ConsentId, token))
                    throw new ConsentUnavailable();
                // No new-ID renewal of any previous consent for this ORIGINAL observation,
                // including terminal or expired grants. A genuinely new original has a new
                // assessment but continues this subject/procedure slot's own generation.
                var prior = await SlotRows(observation.ProcedureSource.Id, observation.SubjectHumanId).ToListAsync(token);
                if (prior.Any(row => row.Binding.SourceId == observation.Id))
                    throw new ConsentUnavailable();
                var now = clock.GetUtcNow();
                ModerationProfileSubjectConsent consent;
                try
                {
                    // Only an independently qualified actual-native entity may produce this
                    // metadata. Client coordinates and stored consent snapshots never do.
                    var metadata = assessment.ToAssessment(assessment.Source, parents.ProcedureSource,
                        parents.ProcedureAdoption, parents.AgeSource, parents.AgeAdoption, observation.ToBinding(), now);
                    consent = ModerationProfileSubjectConsent.Grant(request.ConsentId,
                        parents.ProcedureSource, parents.ProcedureAdoption, parents.AgeSource,
                        parents.AgeAdoption, observation.ToBinding(), metadata,
                        acquisition.SubjectId, generation + 1, now, request.ValidUntil);
                }
                catch (ArgumentException) { throw new ConsentUnavailable(); }
                catch (InvalidOperationException) { throw new ConsentUnavailable(); }
                // Only existing navigation graph is attached; adding the consent cannot
                // reinsert or modify original procedure/age/assessment state.
                context.Attach(consent.Source);
                context.Set<ModerationProfileSubjectConsent>().Add(consent);
                retained = new(acquisition, observation, assessment, consent, request.Assessment);
                await context.SaveChangesAsync(token);
                if (!await GrantCurrentAsync(retained, observations, assessments, token))
                    throw new ConsentUnavailable();
                return consent;
            }, async token =>
            {
                token.ThrowIfCancellationRequested();
                if (retained is null || !await GrantCurrentAsync(retained, observations, assessments, token))
                    throw new ConsentUnavailable();
                token.ThrowIfCancellationRequested();
            }, cancellationToken);
        }
        catch (ConsentUnavailable) { return null; }
        catch (DbUpdateConcurrencyException) { return null; }
        finally
        {
            acquisition?.Dispose();
            context.ChangeTracker.Clear();
            context.ModerationTrustDomainId = originalTrust;
        }
    }

    public async Task<ModerationProfileSubjectConsent?> WithdrawAsync(
        WithdrawModerationProfileSubjectConsent request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        RequireCleanOwner();
        if (!EligibleFrame() || request.ExpectedConsentRevision != 1
            || request.ExpectedConsentGeneration is < 1 or long.MaxValue
            || request.ExpectedGeneration < request.ExpectedConsentGeneration
            || request.ExpectedGeneration == long.MaxValue)
            return null;
        var originalTrust = context.ModerationTrustDomainId;
        WithdrawalState? retained = null;
        try
        {
            return await privacy.ExecuteEnrollmentSerializedAsync(async token =>
            {
                token.ThrowIfCancellationRequested();
                context.ChangeTracker.Clear();
                if (!OwnsSerializableTransaction()) throw new ConsentUnavailable();
                var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
                if (host is null || originalTrust is { } trust && trust != host.TrustDomainId)
                    throw new ConsentUnavailable();
                context.ModerationTrustDomainId = host.TrustDomainId;
                var discovered = await context.Set<ModerationProfileSubjectConsent>().AsNoTracking()
                    .SingleOrDefaultAsync(row => row.Id == request.ConsentId, token);
                if (discovered is null) throw new ConsentUnavailable();
                try { discovered.ValidatePersistedState(); }
                catch (ArgumentException) { throw new ConsentUnavailable(); }
                if (discovered.Revision != 1 || discovered.WithdrawnAt is not null
                    || discovered.Generation != request.ExpectedConsentGeneration)
                    throw new ConsentUnavailable();
                var identity = await AcquireWithdrawalIdentityAsync(discovered, host, token);
                if (identity is null) throw new ConsentUnavailable();
                await RelationalEntityRowFence.AcquireGlobalAsync<ModerationProfileCollectionProcedureSource>(
                    context, discovered.Source.Id, token);
                await using var slot = await RelationalNamedLock.AcquireTransactionAsync(
                    context, Slot(discovered.Source, discovered.SubjectHumanId), token);
                if (await GenerationAsync(discovered.Source.Id, discovered.SubjectHumanId, token) != request.ExpectedGeneration)
                    throw new ConsentUnavailable();
                await RelationalEntityRowFence.AcquireGlobalAsync<ModerationProfileSubjectConsent>(
                    context, discovered.Id, token);
                var now = clock.GetUtcNow();
                if (!ModerationProfileCollectionAssessmentSource.IsInstant(now) || now < discovered.GrantedAt)
                    throw new ConsentUnavailable();
                retained = new(identity, discovered, request.ExpectedGeneration, now);
                if (!await WithdrawalCurrentAsync(retained, false, token)) throw new ConsentUnavailable();
                var consent = await context.Set<ModerationProfileSubjectConsent>()
                    .SingleAsync(row => row.Id == discovered.Id, token);
                consent.Withdraw(request.ExpectedConsentRevision, identity.SubjectId, now);
                await context.SaveChangesAsync(token);
                if (!await WithdrawalCurrentAsync(retained, true, token)) throw new ConsentUnavailable();
                return consent;
            }, async token =>
            {
                token.ThrowIfCancellationRequested();
                if (retained is null || !await WithdrawalCurrentAsync(retained, true, token))
                    throw new ConsentUnavailable();
                token.ThrowIfCancellationRequested();
            }, cancellationToken);
        }
        catch (ConsentUnavailable) { return null; }
        catch (DbUpdateConcurrencyException) { return null; }
        finally
        {
            context.ChangeTracker.Clear();
            context.ModerationTrustDomainId = originalTrust;
        }
    }

    /// <summary>
    /// Borrows an actual individual acquisition without opening another privacy transaction.
    /// The caller retains that acquisition through the real outer commit; returned metadata
    /// is rebuilt against latest native consent and assessment slots, never a stored verdict.
    /// </summary>
    internal async Task<ModerationProfileSubjectConsent?> ReadCurrentForCollectionAsync(
        ModerationProfileCollectionIndividualAcquisition acquisition,
        ModerationProfileCollectionSubjectObservation observation,
        ModerationProfileCollectionAssessmentAdoption assessment,
        ModerationProfileSubjectConsentAssessment expected, Guid consentId, CancellationToken token)
    {
        if (!acquisition.OwnsContext(context) || consentId == Guid.Empty
            || observation.SubjectHumanId != acquisition.SubjectId)
            return null;
        var consent = await context.Set<ModerationProfileSubjectConsent>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == consentId && row.SubjectHumanId == acquisition.SubjectId, token);
        if (consent is null) return null;
        var observations = new ModerationProfileCollectionSubjectObservationOwner(
            context, admin, machine, http, privacy, inventory, clock);
        var assessments = new ModerationProfileCollectionAssessmentRepository(
            context, admin, machine, http, privacy, clock, inventory);
        return await GrantCurrentAsync(new(acquisition, observation, assessment, consent, expected),
            observations, assessments, token) ? consent : null;
    }

    private async Task<bool> GrantCurrentAsync(GrantState original,
        ModerationProfileCollectionSubjectObservationOwner observations,
        ModerationProfileCollectionAssessmentRepository assessments, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var acquisition = original.Acquisition;
        var assessment = await assessments.ReadCurrentAsync(acquisition, original.Expected, token);
        if (assessment is null || !SameAssessment(assessment, original.Assessment)
            || assessment.Source.SubjectHumanId != acquisition.SubjectId)
            return false;
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(
            context, Slot(original.Consent.Source, original.Consent.SubjectHumanId), token);
        if (await GenerationAsync(original.Consent.Source.Id, original.Consent.SubjectHumanId, token)
            != original.Consent.Generation)
            return false;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationProfileSubjectConsent>(
            context, original.Consent.Id, token);
        var current = await context.Set<ModerationProfileSubjectConsent>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == original.Consent.Id, token);
        if (current is null || current.Revision != 1 || current.WithdrawnAt is not null
            || !SameConsentOriginal(current, original.Consent))
            return false;
        var observation = await observations.ReadCurrentAsync(acquisition, original.Observation.Id, token);
        if (observation is null || !observation.Matches(original.Observation)
            || observation.SubjectHumanId != acquisition.SubjectId)
            return false;
        var parents = await acquisition.ReadAssessmentParentsAsync(observation, token);
        if (parents is null || !await acquisition.RevalidateAsync(token)) return false;
        token.ThrowIfCancellationRequested();
        var now = clock.GetUtcNow();
        try
        {
            // Rebuild metadata only from the actual independently qualified native entity.
            // Neither current.Assessment nor original.Consent.Assessment is used as proof.
            var metadata = assessment.ToAssessment(assessment.Source, parents.ProcedureSource,
                parents.ProcedureAdoption, parents.AgeSource, parents.AgeAdoption, observation.ToBinding(), now);
            return observation.Covers(now)
                && current.MatchesCurrentMetadata(parents.ProcedureSource, parents.ProcedureAdoption,
                    parents.AgeSource, parents.AgeAdoption, observation.ToBinding(), metadata, now)
                && acquisition.OwnsContext(context);
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    /// <summary>
    /// Historical correction uses the same canonical identity/provider, global person/login,
    /// host/service, tenant/status and epoch fences as native assessment correction. It does
    /// not acquire ACTIVE policy or demand parent/assessment lifetime or administrator roles.
    /// No named filter or erasure bypass is weakened.
    /// </summary>
    private async Task<WithdrawalIdentity?> AcquireWithdrawalIdentityAsync(
        ModerationProfileSubjectConsent consent, ModerationHostBinding discoveredHost, CancellationToken token)
    {
        if (!OwnsSerializableTransaction() || context.TenantContext is not { } tenantContext
            || context.TenantFilterTenantId is not { } tenantId
            || http.HttpContext?.User is not { } principal || principal.Identity?.IsAuthenticated != true)
            return null;
        var human = await admin.ResolveUserIdAsync(token);
        var account = principal.GetProviderIdentity()?.AccountKey;
        if (human != consent.SubjectHumanId || human is null
            || account is null && principal.GetPlatformUserId() != human)
            return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<User>(context, human.Value, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<UserPii>(context, human.Value, token);
        var loginId = account is null ? null : await context.UserExternalLogins.AsNoTracking()
            .Where(row => row.UserId == human && row.AuthenticationProviderId == (int)account.ProviderKind
                && row.ProviderKey == account.Value).Select(row => (Guid?)row.Id).SingleOrDefaultAsync(token);
        if (account is not null)
        {
            if (loginId is null) return null;
            await RelationalEntityRowFence.AcquireGlobalAsync<UserExternalLogin>(context, loginId.Value, token);
        }
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationHostBinding>(context, discoveredHost.Id, token);
        var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
        if (host is null || host.Id != discoveredHost.Id || host.TrustDomainId != discoveredHost.TrustDomainId
            || host.SolutionId != discoveredHost.SolutionId || host.InstanceId != discoveredHost.InstanceId
            || host.MaintenanceServicePrincipalId != discoveredHost.MaintenanceServicePrincipalId)
            return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ServicePrincipal>(context, host.MaintenanceServicePrincipalId, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<Tenant>(context, tenantId, token);
        var tenant = await context.Tenants.AsNoTracking().SingleOrDefaultAsync(row => row.Id == tenantId, token);
        if (tenant is null) return null;
        await RelationalEntityRowFence.AcquireLookupAsync<TenantStatus>(context, [tenant.TenantStatusId], token);
        var epoch = await context.ModerationQualificationEpochs.AsNoTracking()
            .SingleOrDefaultAsync(row => row.HostBindingId == host.Id && row.TenantId == tenantId, token);
        if (epoch is null || epoch.Revision is < 1 or long.MaxValue) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationEpoch>(context, epoch.Id, token);
        var transaction = context.Database.CurrentTransaction!;
        var identity = new WithdrawalIdentity(transaction, context.Database.GetDbConnection(), tenantContext,
            principal, account, loginId, human.Value, host, tenantId, epoch.Id, epoch.Revision, clock.GetUtcNow());
        return await WithdrawalIdentityCurrentAsync(identity, consent, token) ? identity : null;
    }

    private async Task<bool> WithdrawalCurrentAsync(WithdrawalState original, bool terminal, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!await WithdrawalIdentityCurrentAsync(original.Identity, original.Consent, token)) return false;
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(
            context, Slot(original.Consent.Source, original.Consent.SubjectHumanId), token);
        if (await GenerationAsync(original.Consent.Source.Id, original.Consent.SubjectHumanId, token)
            != original.SlotGeneration)
            return false;
        var current = await context.Set<ModerationProfileSubjectConsent>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == original.Consent.Id, token);
        if (current is null) return false;
        try { current.ValidatePersistedState(); }
        catch (ArgumentException) { return false; }
        return SameConsentOriginal(current, original.Consent)
            && current.Revision == (terminal ? 2 : 1)
            && current.WithdrawnAt == (terminal ? original.WithdrawnAt : (DateTimeOffset?)null)
            && await WithdrawalIdentityCurrentAsync(original.Identity, current, token);
    }

    private async Task<bool> WithdrawalIdentityCurrentAsync(
        WithdrawalIdentity identity, ModerationProfileSubjectConsent consent, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var host = identity.Host;
        if (!WithdrawalFrameCurrent(identity) || machine.IsMachineCaller
            || await admin.ResolveUserIdAsync(token) != identity.SubjectId
            || !Equals(identity.Principal.GetProviderIdentity()?.AccountKey, identity.Account)
            || identity.Account is null && identity.Principal.GetPlatformUserId() != identity.SubjectId
            || await privacy.IsSubjectFencedAsync(identity.SubjectId, token)
            || !await context.Users.AsNoTracking().AnyAsync(row => row.Id == identity.SubjectId && !row.IsDeleted, token)
            || !await context.Set<UserPii>().AsNoTracking().AnyAsync(row => row.UserId == identity.SubjectId, token)
            || !await new ModerationQualificationAuthority(context, admin, machine)
                .ProviderTenantMatchesAsync(identity.TenantId, token)
            || !await context.Tenants.AsNoTracking().AnyAsync(
                row => row.Id == identity.TenantId && row.TenantStatus.IsActiveState, token)
            || !await context.ModerationHostBindings.AsNoTracking().AnyAsync(row =>
                row.Id == host.Id && row.TrustDomainId == host.TrustDomainId
                && row.SolutionId == host.SolutionId && row.InstanceId == host.InstanceId
                && row.MaintenanceServicePrincipalId == host.MaintenanceServicePrincipalId, token)
            || !await context.ServicePrincipals.AsNoTracking().AnyAsync(
                row => row.Id == host.MaintenanceServicePrincipalId && !row.IsDeleted, token)
            || !await context.ModerationQualificationEpochs.AsNoTracking().AnyAsync(row =>
                row.Id == identity.EpochId && row.HostBindingId == host.Id
                && row.TenantId == identity.TenantId && row.Revision == identity.EpochRevision, token))
            return false;
        if (identity.Account is { } account && !await context.UserExternalLogins.AsNoTracking().AnyAsync(row =>
            row.Id == identity.LoginId && row.UserId == identity.SubjectId
            && row.AuthenticationProviderId == (int)account.ProviderKind && row.ProviderKey == account.Value, token))
            return false;
        var instance = AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(host.TrustDomainId), host.SolutionId), host.InstanceId);
        var jurisdiction = consent.Binding.Jurisdiction;
        return consent.SubjectHumanId == identity.SubjectId
            && consent.Binding.Target.HumanSubject is { Kind: ModerationSubjectKind.Human } subject
            && subject.Id == identity.SubjectId && subject.Origin == instance
            && consent.Source.HostBindingId == host.Id && consent.Source.TrustDomainId == host.TrustDomainId
            && consent.Source.TenantId == identity.TenantId
            && (jurisdiction == instance || jurisdiction == AuthorityScope.Tenant(instance, identity.TenantId))
            && await admin.ResolveUserIdAsync(token) == identity.SubjectId && !machine.IsMachineCaller
            && Equals(identity.Principal.GetProviderIdentity()?.AccountKey, identity.Account)
            && (identity.Account is not null || identity.Principal.GetPlatformUserId() == identity.SubjectId)
            && WithdrawalFrameCurrent(identity);
    }

    private void RequireCleanOwner()
    {
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Subject consent owns its complete clean identity unit of work.");
    }

    private bool EligibleFrame() => context.Database.IsRelational() && !context.IsTenantFilterBypassed
        && context.TenantContext is not null && context.TenantFilterTenantId is not null
        && !machine.IsMachineCaller && http.HttpContext?.User.Identity?.IsAuthenticated == true;

    private bool OwnsSerializableTransaction() => EligibleFrame() && context.IdentityFenceOwnsTransaction
        && !context.IdentityFenceTransactionFailed && context.Database.CurrentTransaction is { } transaction
        && transaction.GetDbTransaction().IsolationLevel == IsolationLevel.Serializable
        && ReferenceEquals(transaction.GetDbTransaction().Connection, context.Database.GetDbConnection())
        && context.Database.GetDbConnection().State == ConnectionState.Open;

    private bool WithdrawalFrameCurrent(WithdrawalIdentity identity) => OwnsSerializableTransaction()
        && ReferenceEquals(context.Database.CurrentTransaction, identity.Transaction)
        && ReferenceEquals(context.Database.GetDbConnection(), identity.Connection)
        && ReferenceEquals(context.TenantContext, identity.TenantContext)
        && context.TenantFilterTenantId == identity.TenantId
        && context.ModerationTrustDomainId == identity.Host.TrustDomainId
        && ReferenceEquals(http.HttpContext?.User, identity.Principal) && identity.Principal.Identity?.IsAuthenticated == true
        && ModerationProfileCollectionAssessmentSource.IsInstant(clock.GetUtcNow())
        && clock.GetUtcNow() >= identity.AcquiredAt;

    private IQueryable<ModerationProfileSubjectConsent> SlotRows(Guid procedureId, Guid subjectId) =>
        context.Set<ModerationProfileSubjectConsent>().AsNoTracking().Where(row =>
            row.SubjectHumanId == subjectId && EF.Property<Guid>(row, "SourceId") == procedureId);

    private async Task<long> GenerationAsync(Guid procedureId, Guid subjectId, CancellationToken token)
    {
        var latest = await SlotRows(procedureId, subjectId).OrderByDescending(row => row.Generation)
            .Select(row => row.Generation).Take(2).ToListAsync(token);
        return latest.Count == 0 ? 0
            : latest.Count == 2 && latest[0] == latest[1] ? -1 : latest[0];
    }

    private static string Slot(ModerationProfileCollectionProcedureSource source, Guid subjectId) =>
        $"moderation.profile-subject-consent:{source.HostBindingId:N}:{source.TenantId:N}:{subjectId:N}:{source.Id:N}";

    private static bool SameAssessment(ModerationProfileCollectionAssessmentAdoption current,
        ModerationProfileCollectionAssessmentAdoption original) =>
        current.Id == original.Id && current.Revision == original.Revision && current.Generation == original.Generation
        && current.SourceId == original.SourceId && current.SourceRevision == original.SourceRevision
        && current.SourceGeneration == original.SourceGeneration && current.Source.Matches(original.Source)
        && current.AdoptedByHumanId == original.AdoptedByHumanId && current.AdoptedAt == original.AdoptedAt
        && current.ValidUntil == original.ValidUntil && current.WithdrawnAt is null && current.WithdrawnByHumanId is null;

    private static bool SameConsentOriginal(ModerationProfileSubjectConsent current, ModerationProfileSubjectConsent original) =>
        current.Id == original.Id && current.Source.Matches(original.Source)
        && current.SubjectHumanId == original.SubjectHumanId && current.Generation == original.Generation
        && current.ProcedureAdoptionId == original.ProcedureAdoptionId
        && current.ProcedureAdoptionRevision == original.ProcedureAdoptionRevision
        && current.ProcedureAdoptionWriterGeneration == original.ProcedureAdoptionWriterGeneration
        && current.ProcedureAdoptedByHumanId == original.ProcedureAdoptedByHumanId
        && current.ProcedureAdoptedAt == original.ProcedureAdoptedAt
        && current.ProcedureAdoptionValidUntil == original.ProcedureAdoptionValidUntil
        && current.Binding == original.Binding && current.Assessment.Binding == original.Assessment.Binding
        && current.Assessment.Reference == original.Assessment.Reference
        && current.Assessment.Required.SequenceEqual(original.Assessment.Required)
        && current.GrantedAt == original.GrantedAt && current.ValidUntil == original.ValidUntil;

    private sealed record GrantState(
        ModerationProfileCollectionIndividualAcquisition Acquisition,
        ModerationProfileCollectionSubjectObservation Observation,
        ModerationProfileCollectionAssessmentAdoption Assessment,
        ModerationProfileSubjectConsent Consent, ModerationProfileSubjectConsentAssessment Expected);

    private sealed record WithdrawalIdentity(
        IDbContextTransaction Transaction, DbConnection Connection, ITenantContext TenantContext,
        ClaimsPrincipal Principal, ProviderAccountKey? Account, Guid? LoginId, Guid SubjectId,
        ModerationHostBinding Host, Guid TenantId, Guid EpochId, long EpochRevision, DateTimeOffset AcquiredAt);

    private sealed record WithdrawalState(
        WithdrawalIdentity Identity, ModerationProfileSubjectConsent Consent, long SlotGeneration,
        DateTimeOffset WithdrawnAt);

    private sealed class ConsentUnavailable : Exception;
}

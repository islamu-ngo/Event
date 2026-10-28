using System.Data;
using System.Data.Common;
using System.Text.Json;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

/// <summary>
/// Owns serializable native maintenance leases and finalization of originally inert effects.
/// Substantive consequence completion and delivery remain separate.
/// The finite lease window bounds contention; only current fenced native credentials supply authority.
/// </summary>
public sealed class ModerationEffectLeaseRepository(
    ExploreDbContext context, IMachinePrincipalAccessor machine,
    IModerationMachineSubjectBindingRepository bindings, IExternalApiKeyAuthenticationRepository keys,
    TimeProvider clock) : IModerationEffectLeaseRepository
{
    private static readonly TimeSpan LeaseWindow = TimeSpan.FromMinutes(2);

    public Task<ModerationEffectLeaseResult> ReadAsync(
        Guid effectId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(effectId, null, null, LeaseOperation.Read, cancellationToken);

    public Task<ModerationEffectLeaseResult> ReadWithoutSubstantiveWorkAsync(
        Guid effectId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(effectId, null, null, LeaseOperation.ReadWithoutSubstantiveWork, cancellationToken);

    public Task<ModerationEffectLeaseResult> ReadOriginalCompletionAsync(
        Guid effectId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(effectId, null, null, LeaseOperation.ReadOriginalCompletion, cancellationToken);

    public Task<ModerationEffectLeaseResult> ClaimAsync(
        Guid effectId, long expectedRevision, CancellationToken cancellationToken = default) =>
        ExecuteAsync(effectId, expectedRevision, null, LeaseOperation.Claim, cancellationToken);

    public Task<ModerationEffectLeaseResult> ReleaseAsync(
        Guid effectId, Guid leaseId, long expectedRevision, CancellationToken cancellationToken = default) =>
        ExecuteAsync(effectId, expectedRevision, leaseId, LeaseOperation.Release, cancellationToken);

    public Task<ModerationEffectLeaseResult> CompleteWithoutSubstantiveWorkAsync(
        Guid effectId, Guid leaseId, long expectedRevision, CancellationToken cancellationToken = default) =>
        ExecuteAsync(effectId, expectedRevision, leaseId, LeaseOperation.CompleteWithoutSubstantiveWork, cancellationToken);

    private async Task<ModerationEffectLeaseResult> ExecuteAsync(
        Guid effectId, long? expectedRevision, Guid? ownerLeaseId, LeaseOperation operation, CancellationToken token)
    {
        var release = operation == LeaseOperation.Release;
        var complete = operation == LeaseOperation.CompleteWithoutSubstantiveWork;
        if (effectId == Guid.Empty || expectedRevision is < 1 or long.MaxValue
            || (release || complete) && (ownerLeaseId is null || ownerLeaseId == Guid.Empty))
            throw new ArgumentException("Lease intent requires an exact identity and positive revision.");
        if (!context.Database.IsRelational() || context.Database.CurrentTransaction is not null
            || context.IsTenantFilterBypassed || context.ModerationTrustDomainId is not null
            || context.ChangeTracker.Entries().Any()
            || context.TenantFilterTenantId is not { } tenantId || tenantId == Guid.Empty)
            return Unavailable(ModerationEffectLeaseUnavailableReason.NativeAuthority);
        if (machine.Current is not { CredentialSource: ApiKeyCredentialSource.Persisted } principal)
            return new(ModerationEffectLeaseOutcome.Denied);

        // Allocate all claim coordinates once. Replay must observe this same lease, never renew it.
        var startedAt = clock.GetUtcNow();
        var expiresAt = startedAt + LeaseWindow;
        var leaseId = release || complete ? ownerLeaseId : expectedRevision is null ? null : (Guid?)Guid.CreateVersion7();
        var resultRevision = expectedRevision is { } revision ? revision + 1 : (long?)null;
        var released = false;
        DateTimeOffset? completedAt = null;
        DateTimeOffset? completionLeaseStartedAt = null;
        DateTimeOffset? completionLeaseExpiresAt = null;
        try
        {
            return await context.Database.CreateExecutionStrategy().ExecuteInTransactionAsync(
                operation: async cancellationToken =>
                {
                    context.ChangeTracker.Clear();
                    context.ModerationTrustDomainId = null;
                    if (!await NativeTenantAsync(tenantId, cancellationToken))
                        throw new AbortLease(Unavailable(ModerationEffectLeaseUnavailableReason.NativeAuthority));
                    var host = await AcquireSourceAsync(tenantId, principal, cancellationToken)
                        ?? throw new AbortLease(new(ModerationEffectLeaseOutcome.Denied));
                    var effect = await ReadFencedAsync(effectId, tenantId, host,
                        complete || operation == LeaseOperation.ReadWithoutSubstantiveWork, cancellationToken);
                    if (effect is null) throw new AbortLease(new(ModerationEffectLeaseOutcome.NotFound));
                    if (!await SourceCurrentAsync(tenantId, principal, host, cancellationToken))
                        throw new AbortLease(new(ModerationEffectLeaseOutcome.Denied));
                    if (operation == LeaseOperation.ReadOriginalCompletion)
                    {
                        RequireOriginalCompletion(effect, tenantId, host);
                        if (!await SourceCurrentAsync(tenantId, principal, host, cancellationToken))
                            throw new AbortLease(new(ModerationEffectLeaseOutcome.Denied));
                    }
                    if (expectedRevision is null)
                        return new ModerationEffectLeaseResult(
                            effect.CompletedAt is null ? ModerationEffectLeaseOutcome.Known
                                : ModerationEffectLeaseOutcome.Completed, effect);
                    if (complete && ExactCompletion(effect))
                        return new ModerationEffectLeaseResult(ModerationEffectLeaseOutcome.Completed, effect);
                    if (effect.CompletedAt is not null)
                        throw new AbortLease(new(complete ? ModerationEffectLeaseOutcome.Conflict : ModerationEffectLeaseOutcome.Completed));
                    if (release && ExactRelease(effect))
                        return new ModerationEffectLeaseResult(ModerationEffectLeaseOutcome.Known, effect);
                    if (!release && !complete && ExactClaim(effect))
                        return new ModerationEffectLeaseResult(ModerationEffectLeaseOutcome.Known, effect);
                    if (effect.Revision != expectedRevision)
                        throw new AbortLease(new(ModerationEffectLeaseOutcome.Conflict));
                    var now = clock.GetUtcNow();
                    if (complete)
                    {
                        if (effect.LeaseId != leaseId || now < effect.LeaseStartedAt)
                            throw new AbortLease(new(ModerationEffectLeaseOutcome.Conflict));
                        if (effect.LeaseExpiresAt is null || now >= effect.LeaseExpiresAt)
                            throw new AbortLease(Unavailable(ModerationEffectLeaseUnavailableReason.LeaseWindowElapsed));
                        completedAt ??= now;
                        completionLeaseStartedAt ??= effect.LeaseStartedAt;
                        completionLeaseExpiresAt ??= effect.LeaseExpiresAt;
                        effect.Complete(leaseId!.Value, expectedRevision.Value, completedAt.Value);
                    }
                    else if (release)
                    {
                        if (effect.LeaseId != leaseId || now < effect.LeaseStartedAt)
                            throw new AbortLease(new(ModerationEffectLeaseOutcome.Conflict));
                        effect.Release(leaseId!.Value, expectedRevision.Value, now);
                        released = true;
                    }
                    else
                    {
                        if (effect.LeaseExpiresAt > startedAt)
                            throw new AbortLease(new(ModerationEffectLeaseOutcome.Busy));
                        if (now < startedAt || now >= expiresAt || startedAt < effect.CreatedAt)
                            throw new AbortLease(Unavailable(ModerationEffectLeaseUnavailableReason.LeaseWindowElapsed));
                        effect.Claim(leaseId!.Value, expectedRevision.Value, startedAt, expiresAt);
                    }
                    if (!await SourceCurrentAsync(tenantId, principal, host, cancellationToken))
                        throw new AbortLease(new(ModerationEffectLeaseOutcome.Denied));
                    await context.SaveChangesAsync(cancellationToken);
                    await context.FlushDisclosureAsync(cancellationToken);
                    if (!await SourceCurrentAsync(tenantId, principal, host, cancellationToken))
                        throw new AbortLease(new(ModerationEffectLeaseOutcome.Denied));
                    var flushedAt = clock.GetUtcNow();
                    if (complete && (!ExactCompletion(effect)
                            || flushedAt < completedAt || flushedAt >= completionLeaseExpiresAt)
                        || !release && !complete && !ExactClaim(effect))
                        throw new AbortLease(Unavailable(ModerationEffectLeaseUnavailableReason.LeaseWindowElapsed));
                    return new ModerationEffectLeaseResult(
                        complete ? ModerationEffectLeaseOutcome.Completed : ModerationEffectLeaseOutcome.Known, effect);
                },
                verifySucceeded: async cancellationToken =>
                {
                    context.ChangeTracker.Clear();
                    context.ModerationTrustDomainId = null;
                    if (expectedRevision is null) return false;
                    await using var transaction = await context.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable, cancellationToken);
                    var host = await AcquireSourceAsync(tenantId, principal, cancellationToken);
                    if (host is null) return false;
                    var effect = await ReadFencedAsync(effectId, tenantId, host, complete, cancellationToken);
                    return effect is not null && await SourceCurrentAsync(tenantId, principal, host, cancellationToken) && (complete
                        ? ExactCompletion(effect)
                        : release
                        ? ExactRelease(effect)
                        : ExactClaim(effect));
                },
                isolationLevel: IsolationLevel.Serializable,
                cancellationToken: token);
        }
        catch (AbortLease aborted)
        {
            return aborted.Result;
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(ModerationEffectLeaseOutcome.Conflict);
        }
        catch (DbException)
        {
            return Unavailable(ModerationEffectLeaseUnavailableReason.NativeAuthority);
        }
        catch (DbUpdateException exception) when (exception.InnerException is DbException)
        {
            return Unavailable(ModerationEffectLeaseUnavailableReason.NativeAuthority);
        }
        finally
        {
            context.ChangeTracker.Clear();
            context.ModerationTrustDomainId = null;
        }

        bool ExactClaim(ModerationEffectIntent effect)
        {
            var observedAt = clock.GetUtcNow();
            return effect.CompletedAt is null && effect.LeaseId == leaseId && effect.Revision == resultRevision
                && effect.LeaseStartedAt == startedAt && effect.LeaseExpiresAt == expiresAt
                && observedAt >= startedAt && observedAt < expiresAt;
        }

        bool ExactRelease(ModerationEffectIntent effect) =>
            released && effect.Revision == resultRevision && effect.CompletedAt is null
            && effect.LeaseId is null && effect.LeaseStartedAt is null && effect.LeaseExpiresAt is null;

        // This is a receipt of this invocation's write, not recognition of another completed owner.
        // A lost acknowledgement may be verified after expiry; the committing flush was bounded above.
        bool ExactCompletion(ModerationEffectIntent effect) =>
            completedAt is not null && effect.CompletedAt == completedAt && effect.Revision == resultRevision
            && effect.LeaseId == leaseId && effect.LeaseStartedAt == completionLeaseStartedAt
            && effect.LeaseExpiresAt == completionLeaseExpiresAt;
    }

    private async Task<ModerationHostBinding?> AcquireSourceAsync(
        Guid tenantId, ApiKeyPrincipalContext principal, CancellationToken token)
    {
        if (!await NativeTenantAsync(tenantId, token) || machine.Current != principal
            || principal.TenantId is { } credentialTenant && credentialTenant != tenantId)
            return null;
        var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
        var binding = await bindings.ReadCurrentAsync(principal.KeyId, ModerationMachineCredentialSource.Persisted, token);
        var key = await keys.GetByKeyIdForAuthentication(principal.KeyId, token);
        if (host is null || binding is null || key is null
            || binding.HostBindingId != host.Id || binding.ServicePrincipalId != host.MaintenanceServicePrincipalId
            || binding.AuthorityTenantId != principal.TenantId || binding.OwnerType != principal.OwnerType
            || binding.OwnerId != principal.OwnerId)
            return null;

        // The existing binding repository checks actual key liveness and the installed live service.
        // Fence every source row it consumes, then reread through that same native authority.
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationHostBinding>(context, host.Id, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ServicePrincipal>(context, binding.ServicePrincipalId, token);
        var actorIds = await context.Actors.AsNoTracking().IgnoreQueryFilters(["SoftDelete"])
            .Where(row => row.ServicePrincipalId == binding.ServicePrincipalId)
            .OrderBy(row => row.Id).Select(row => row.Id).ToListAsync(token);
        foreach (var actorId in actorIds)
            await RelationalEntityRowFence.AcquireGlobalAsync<Actor>(context, actorId, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ExternalApiKey>(context, key.Id, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationMachineSubjectBinding>(context, binding.Id, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<Tenant>(context, tenantId, token);
        var tenant = await context.Tenants.AsNoTracking().SingleOrDefaultAsync(row => row.Id == tenantId, token);
        if (tenant is null) return null;
        await RelationalEntityRowFence.AcquireLookupAsync<TenantStatus>(context, [tenant.TenantStatusId], token);
        var epoch = await context.ModerationQualificationEpochs.AsNoTracking()
            .SingleOrDefaultAsync(row => row.HostBindingId == host.Id && row.TenantId == tenantId, token);
        if (epoch is null) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationEpoch>(context, epoch.Id, token);
        var currentHost = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
        var currentBinding = await bindings.ReadCurrentAsync(principal.KeyId, ModerationMachineCredentialSource.Persisted, token);
        if (currentHost is null || currentHost.Id != host.Id || currentHost.TrustDomainId != host.TrustDomainId
            || currentHost.SolutionId != host.SolutionId || currentHost.InstanceId != host.InstanceId
            || currentHost.MaintenanceServicePrincipalId != binding.ServicePrincipalId
            || currentBinding is null || currentBinding.Id != binding.Id || currentBinding.Revision != binding.Revision
            || currentBinding.HostBindingId != host.Id || currentBinding.ServicePrincipalId != binding.ServicePrincipalId
            || currentBinding.AuthorityTenantId != principal.TenantId || currentBinding.OwnerType != principal.OwnerType
            || currentBinding.OwnerId != principal.OwnerId
            || !await context.ModerationQualificationEpochs.AsNoTracking().AnyAsync(
                row => row.Id == epoch.Id && row.HostBindingId == host.Id && row.TenantId == tenantId && row.Revision > 0, token)
            || !await SourceCurrentAsync(tenantId, principal, currentHost, token))
            return null;
        context.ModerationTrustDomainId = currentHost.TrustDomainId;
        return currentHost;
    }

    private async Task<bool> SourceCurrentAsync(
        Guid tenantId, ApiKeyPrincipalContext principal, ModerationHostBinding host, CancellationToken token)
    {
        if (machine.Current != principal || !await NativeTenantAsync(tenantId, token)) return false;
        var binding = await bindings.ReadCurrentAsync(principal.KeyId, ModerationMachineCredentialSource.Persisted, token);
        return binding is not null && binding.HostBindingId == host.Id
            && binding.ServicePrincipalId == host.MaintenanceServicePrincipalId
            && binding.AuthorityTenantId == principal.TenantId && binding.OwnerType == principal.OwnerType
            && binding.OwnerId == principal.OwnerId
            && await context.Tenants.AsNoTracking().AnyAsync(
                row => row.Id == tenantId && row.TenantStatus.IsActiveState, token);
    }

    private async Task<ModerationEffectIntent?> ReadFencedAsync(
        Guid effectId, Guid tenantId, ModerationHostBinding host, bool complete, CancellationToken token)
    {
        await using var fence = await RelationalNamedLock.AcquireTransactionAsync(
            context, $"moderation.effect:{effectId:D}", token);
        var discovered = await context.ModerationEffectIntents.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == effectId, token);
        if (discovered is null) return null;
        // Canonical tenancy is not stored on the effect row. Never return it on trust-domain filtering alone.
        var decision = await context.ModerationDecisions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == discovered.DecisionId, token);
        if (decision is null) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationDecision>(context, decision.Id, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationEffectIntent>(context, effectId, token);
        decision = await context.ModerationDecisions.AsNoTracking().SingleOrDefaultAsync(row => row.Id == decision.Id, token);
        var effect = await context.ModerationEffectIntents.SingleOrDefaultAsync(row => row.Id == effectId, token);
        if (decision is null || effect is null) return null;
        var instance = AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(host.TrustDomainId), host.SolutionId), host.InstanceId);
        var tenant = AuthorityScope.Tenant(instance, tenantId);
        if (decision.Scope != instance && !tenant.Contains(decision.Scope)) return null;
        // Instance jurisdiction is not a writer-tenant grant. Bind even a read/claim
        // to the original source owner before exposing or mutating this intent.
        if (decision.Scope == instance && !await context.ModerationNoticeSources.AsNoTracking()
            .AnyAsync(row => row.EffectId == effect.Id && row.TenantId == tenantId, token))
            return null;
        var receipt = await context.ModerationOperationReceipts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == effect.OperationId, token);
        if (receipt is null)
            throw new AbortLease(Unavailable(ModerationEffectLeaseUnavailableReason.MissingCause));
        var causeRevision = effect.OperationKind is (int)ModerationOperationKind.Revoke or (int)ModerationOperationKind.Reconcile
            && effect.SanctionId is not null ? effect.SanctionRevision : effect.DecisionRevision;
        if (!Enum.IsDefined((ModerationOperationKind)effect.OperationKind)
            || effect.OperationKind == (int)ModerationOperationKind.Evaluate
            || effect.DecisionId != decision.Id || effect.TrustDomainId != decision.ScopeTrustDomainId
            || effect.ScopeKind != (int)decision.ScopeKind || effect.ScopeId != decision.Scope.Id
            || effect.SubjectKind != (int)decision.SubjectKind || effect.SubjectId != decision.SubjectId
            || receipt.DecisionId != effect.DecisionId || receipt.SanctionId != effect.SanctionId
            || receipt.ResultRevision != causeRevision || receipt.TrustDomainId != effect.TrustDomainId
            || receipt.ScopeKind != effect.ScopeKind || receipt.ScopeId != effect.ScopeId
            || receipt.SubjectKind != effect.SubjectKind || receipt.SubjectId != effect.SubjectId
            || receipt.PolicyRevision != effect.PolicyRevision || receipt.QualificationRevision != effect.QualificationRevision)
            throw new AbortLease(Unavailable(ModerationEffectLeaseUnavailableReason.InconsistentCause));
        effect.ValidatePersistedState();
        if (complete)
        {
            await using var sourceFence = await RelationalNamedLock.AcquireTransactionAsync(
                context, $"moderation.notice:{effectId:D}", token);
            var ownership = await context.ModerationNoticeSources.AsNoTracking()
                .Where(row => row.EffectId == effectId)
                .Select(row => new { row.TenantId, row.RetainUntil }).SingleOrDefaultAsync(token);
            if (ownership is null)
                throw new AbortLease(Unavailable(ModerationEffectLeaseUnavailableReason.MissingOriginalCapture));
            // Canonical inert captures have no disclosure retention ceiling. A finite R
            // belongs to private substantive custody, which this lease owner cannot acquire.
            // Reject on metadata before reading that body, including after R or erasure.
            if (ownership.TenantId != tenantId || ownership.RetainUntil is not null)
                throw new AbortLease(Unavailable(ModerationEffectLeaseUnavailableReason.InvalidOrSubstantiveOriginalCapture));
            var source = await context.ModerationNoticeSources.AsNoTracking()
                .SingleOrDefaultAsync(row => row.EffectId == effectId, token);
            if (source is null)
                throw new AbortLease(Unavailable(ModerationEffectLeaseUnavailableReason.MissingOriginalCapture));
            if (!source.Matches(effect) || source.TenantId != tenantId)
                throw new AbortLease(Unavailable(ModerationEffectLeaseUnavailableReason.InvalidOrSubstantiveOriginalCapture));
            ModerationNoticeCaptureSnapshot? captured;
            try { captured = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(source.SnapshotJson); }
            catch (JsonException)
            {
                throw new AbortLease(Unavailable(ModerationEffectLeaseUnavailableReason.InvalidOrSubstantiveOriginalCapture));
            }
            if (captured is null || !captured.Matches(effect) || !captured.HasNoSubstantiveWork
                || captured.WriterTenantId != tenantId || captured.HostBindingId != host.Id
                || captured.HostSolutionId != host.SolutionId || captured.HostInstanceId != host.InstanceId
                || captured.Decision.CaseId != decision.CaseId
                || captured.Decision.Scope != ModerationNoticeScope.From(decision.Scope)
                || captured.Decision.SubjectOrigin != ModerationNoticeScope.From(decision.Subject.Origin)
                || !await context.ModerationQualificationEpochs.AsNoTracking().AnyAsync(
                    row => row.Id == captured.AuthoritySourceId && row.HostBindingId == host.Id
                        && row.TenantId == tenantId && row.Revision > 0, token))
                throw new AbortLease(Unavailable(ModerationEffectLeaseUnavailableReason.InvalidOrSubstantiveOriginalCapture));
        }
        // Subject and scope are immutable decision coordinates. State, policy selection and revisions
        // may advance: none replaces the original receipt or becomes a retrospective activation grant.
        return effect;
    }

    private void RequireOriginalCompletion(
        ModerationEffectIntent effect, Guid tenantId, ModerationHostBinding host)
    {
        // This is an acknowledgement of the exact original completed transition,
        // not new consequence authority or permission to read expired notice custody.
        if (effect.CompletedAt is null)
            throw new AbortLease(Unavailable(ModerationEffectLeaseUnavailableReason.InvalidOrSubstantiveOriginalCapture));
        if (!effect.HasOriginalCompletionAcknowledgement(host.Id, tenantId))
            throw new AbortLease(Unavailable(ModerationEffectLeaseUnavailableReason.MissingCause));
        if (effect.TrustDomainId != host.TrustDomainId || clock.GetUtcNow() < effect.CompletedAt)
            throw new AbortLease(Unavailable(ModerationEffectLeaseUnavailableReason.InconsistentCause));
    }

    private async Task<bool> NativeTenantAsync(Guid tenantId, CancellationToken token)
    {
        if (context.IsTenantFilterBypassed || context.TenantFilterTenantId != tenantId
            || context.Database.CurrentTransaction is not { } transaction
            || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable
            || context.Database.ProviderName is not (RelationalNamedLock.PostgreSqlProvider
                or RelationalNamedLock.SqlServerProvider or RelationalNamedLock.SqliteProvider or RelationalNamedLock.MySqlProvider))
            return false;
        if (context.Database.ProviderName != RelationalNamedLock.PostgreSqlProvider) return true;
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "SELECT current_setting('app.current_tenant_id', true)";
        var value = await command.ExecuteScalarAsync(token);
        return value is string setting && Guid.TryParse(setting, out var actual) && actual == tenantId;
    }

    private static ModerationEffectLeaseResult Unavailable(ModerationEffectLeaseUnavailableReason reason) =>
        new(ModerationEffectLeaseOutcome.Unavailable, UnavailableReason: reason);

    private enum LeaseOperation { Read, ReadWithoutSubstantiveWork, ReadOriginalCompletion, Claim, Release, CompleteWithoutSubstantiveWork }

    private sealed class AbortLease(ModerationEffectLeaseResult result) : Exception
    {
        public ModerationEffectLeaseResult Result { get; } = result;
    }
}

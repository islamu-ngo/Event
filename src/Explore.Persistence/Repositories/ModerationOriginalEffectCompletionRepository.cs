using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

/// <summary>
/// Original CFG09 completion is a join of native facts, not execution's receipt count.
/// Retained privacy precedes primary authority. The selected owner of the primary transaction
/// performs the final flush; both ownership forms requalify at that actual commit boundary.
/// </summary>
public sealed partial class ModerationOriginalEffectCompletionRepository(
    ExploreDbContext context, IMachinePrincipalAccessor machine,
    IModerationMachineSubjectBindingRepository bindings, IExternalApiKeyRepository keys,
    TimeProvider clock, IPrivacyIdentityFenceAuthority privacy,
    ModerationCapabilityRegistry inventory, IModerationPolicyPublicationRepository publications,
    IModerationActivationSourceOwner sources, IEnumerable<IModerationConsumerRuntimeSource> consumerSources,
    IModerationMeasurementReceiptVerifier measurementReceipts) : IModerationOriginalEffectCompletionRepository
{
    public async Task<ModerationOriginalEffectCompletionResult> CompleteAsync(
        Guid effectId, Guid leaseId, long expectedEffectRevision, CancellationToken cancellationToken = default)
    {
        if (effectId == Guid.Empty || leaseId == Guid.Empty || expectedEffectRevision is < 1 or long.MaxValue)
            throw new ArgumentException("Completion selects one exact pending native lease.");
        if (!context.Database.IsRelational() || context.Database.CurrentTransaction is not null
            || context.IsTenantFilterBypassed || context.ModerationTrustDomainId is not null
            || context.ChangeTracker.Entries().Any() || context.TenantFilterTenantId is not { } tenantId
            || tenantId == Guid.Empty || machine.Current is not { CredentialSource: ApiKeyCredentialSource.Persisted } principal)
            return Pending(ModerationOriginalEffectCompletionPendingReason.CurrentAuthority);

        var receiptId = Guid.CreateVersion7();
        ModerationOriginalEffectCompletionReceipt? expected = null;
        OriginalProof? first = null;
        Func<CancellationToken, Task> finalCommit = _ => throw new CompletionPending(
            ModerationOriginalEffectCompletionPendingReason.FinalCommitBoundary);
        try
        {
            return await context.Database.CreateExecutionStrategy().ExecuteAsync(() =>
                privacy.ExecuteEnrollmentSerializedAsync(async token =>
                {
                    context.ChangeTracker.Clear();
                    context.ModerationTrustDomainId = null;
                    finalCommit = _ => throw new CompletionPending(
                        ModerationOriginalEffectCompletionPendingReason.FinalCommitBoundary);
                    if (context.Database.CurrentTransaction is not null && !context.IdentityFenceOwnsTransaction)
                        throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.CurrentAuthority);
                    await using IDbContextTransaction? owned = context.IdentityFenceOwnsTransaction ? null
                        : await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                    var transaction = context.Database.CurrentTransaction
                        ?? throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.CurrentAuthority);

                    // Only this invocation knows these fixed coordinates. Another caller cannot
                    // turn a completed effect into a grant or reconstruct private expired inputs.
                    if (expected is not null && first is not null
                        && await ReplayAsync(expected, first, principal, transaction, token))
                    {
                        finalCommit = async finalToken =>
                        {
                            if (!await ReplayAsync(expected, first, principal, transaction, finalToken))
                                throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.FinalCommitBoundary);
                        };
                        await finalCommit(token);
                        if (owned is not null) await owned.CommitAsync(token);
                        return new ModerationOriginalEffectCompletionResult(
                            ModerationOriginalEffectCompletionOutcome.Completed, expected);
                    }

                    var authority = await new ModerationMaintenanceAuthority(context, machine, bindings, keys, clock)
                        .AcquireForEffectAsync(effectId, leaseId, expectedEffectRevision, privacy, token)
                        ?? throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.CurrentAuthority);
                    var credential = await bindings.ReadCurrentAsync(
                        principal.KeyId, ModerationMachineCredentialSource.Persisted, token)
                        ?? throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.CurrentAuthority);
                    var effect = await context.ModerationEffectIntents.SingleAsync(row => row.Id == effectId, token);
                    var proof = await ProveAsync(authority, effect, principal, credential, transaction, token);
                    // Preservation dimensions are not new work. Read owners cannot enlist
                    // a booking, seat, credential, recognition or resource mutation here.
                    if (context.ChangeTracker.HasChanges())
                        throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.FinalCommitBoundary);
                    var completedAt = expected?.CompletedAt ?? clock.GetUtcNow();
                    if (completedAt >= proof.ValidUntil || clock.GetUtcNow() >= proof.ValidUntil)
                        throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.FinalCommitBoundary);
                    var candidate = ModerationOriginalEffectCompletionReceipt.Record(receiptId, effect, proof.Source,
                        authority.Binding.Id, leaseId, expectedEffectRevision, proof.Capture.Notice!.PublicationId,
                        proof.Capture.Notice.ProfileId, proof.Selection.SelectionDigest, proof.Digest,
                        proof.Capture.Notice.Effects.Length, completedAt, proof.ValidUntil);
                    if (expected is not null && !SameReceipt(expected, candidate))
                        throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.FinalCommitBoundary);
                    expected ??= candidate;
                    first ??= proof;
                    effect.Complete(leaseId, expectedEffectRevision, completedAt);
                    effect.RecordOriginalCompletionAcknowledgement(expected);
                    context.ModerationOriginalEffectCompletionReceipts.Add(expected);
                    await context.SaveChangesAsync(token);
                    await context.FlushDisclosureAsync(token);

                    finalCommit = async finalToken =>
                    {
                        if (!CurrentTransaction(transaction, tenantId) || context.ChangeTracker.HasChanges()
                            || clock.GetUtcNow() < completedAt || clock.GetUtcNow() >= expected.QualifiedUntil)
                            throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.FinalCommitBoundary);
                        var actualEffect = await context.ModerationEffectIntents.AsNoTracking()
                            .SingleOrDefaultAsync(row => row.Id == effectId, finalToken);
                        var actualReceipt = await context.ModerationOriginalEffectCompletionReceipts.AsNoTracking()
                            .SingleOrDefaultAsync(row => row.EffectId == effectId, finalToken);
                        if (actualEffect is null || actualReceipt is null || !SameReceipt(expected, actualReceipt)
                            || !expected.MatchesCompletion(actualEffect)
                            || actualEffect.OriginalCompletionId != expected.Id
                            || actualEffect.OriginalCompletionRevision != expected.LeaseRevision + 1
                            || !actualEffect.HasOriginalCompletionAcknowledgement(expected.HostBindingId, expected.TenantId))
                            throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.FinalCommitBoundary);
                        var final = await ProveAsync(authority, actualEffect, principal, credential, transaction, finalToken);
                        var now = clock.GetUtcNow();
                        if (final.Digest != proof.Digest || final.Source.SnapshotHash != expected.SnapshotHash
                            || final.Source.EffectBindingHash != expected.EffectBindingHash
                            || final.Selection.SelectionDigest != expected.SelectionDigest
                            || now < completedAt || now >= expected.QualifiedUntil || now >= final.ValidUntil
                            || !CurrentTransaction(transaction, tenantId) || context.ChangeTracker.HasChanges())
                            throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.FinalCommitBoundary);
                    };
                    await finalCommit(token);
                    if (owned is not null) await owned.CommitAsync(token);
                    return new ModerationOriginalEffectCompletionResult(
                        ModerationOriginalEffectCompletionOutcome.Completed, expected);
                }, token => finalCommit(token), cancellationToken));
        }
        catch (CompletionPending pending) { return Pending(pending.Reason); }
        catch (DbUpdateConcurrencyException)
        {
            return new(ModerationOriginalEffectCompletionOutcome.Conflict);
        }
        catch (DbException) { return Pending(ModerationOriginalEffectCompletionPendingReason.CurrentAuthority); }
        catch (DbUpdateException exception) when (exception.InnerException is DbException)
        {
            return Pending(ModerationOriginalEffectCompletionPendingReason.CurrentAuthority);
        }
        finally
        {
            context.ChangeTracker.Clear();
            context.ModerationTrustDomainId = null;
        }
    }

    private bool CurrentTransaction(IDbContextTransaction transaction, Guid tenantId) =>
        ReferenceEquals(context.Database.CurrentTransaction, transaction)
        && transaction.GetDbTransaction().Connection == context.Database.GetDbConnection()
        && transaction.GetDbTransaction().IsolationLevel == IsolationLevel.Serializable
        && !context.IsTenantFilterBypassed && context.TenantFilterTenantId == tenantId;

    private async Task<bool> CurrentAuthorityAsync(ModerationSourceAuthority authority,
        ApiKeyPrincipalContext principal, ModerationMachineSubjectBinding credential,
        IDbContextTransaction transaction, CancellationToken token)
    {
        if (!CurrentTransaction(transaction, authority.Tenant.Id) || machine.Current != principal
            || context.ModerationTrustDomainId != authority.Binding.TrustDomainId)
            return false;
        if (context.Database.ProviderName == RelationalNamedLock.PostgreSqlProvider)
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = "SELECT current_setting('app.current_tenant_id', true)";
            if (await command.ExecuteScalarAsync(token) is not string setting
                || !Guid.TryParse(setting, out var actualTenant) || actualTenant != authority.Tenant.Id)
                return false;
        }
        var current = await bindings.ReadCurrentAsync(
            principal.KeyId, ModerationMachineCredentialSource.Persisted, token);
        if (current is null || current.Id != credential.Id || current.Revision != credential.Revision
            || current.HostBindingId != authority.Binding.Id
            || current.ServicePrincipalId != authority.Binding.MaintenanceServicePrincipalId
            || current.AuthorityTenantId != principal.TenantId || current.OwnerType != principal.OwnerType
            || current.OwnerId != principal.OwnerId
            || await keys.GetByKeyIdForAuthentication(principal.KeyId, token) is null
            || !await context.Tenants.AsNoTracking().AnyAsync(row =>
                row.Id == authority.Tenant.Id && row.TenantStatus.IsActiveState, token)
            || !await context.ModerationHostBindings.AsNoTracking().AnyAsync(row =>
                row.Id == authority.Binding.Id && row.TrustDomainId == authority.Binding.TrustDomainId
                && row.InstanceId == authority.Binding.InstanceId && row.SolutionId == authority.Binding.SolutionId
                && row.MaintenanceServicePrincipalId == current.ServicePrincipalId, token)
            || !await context.ModerationQualificationEpochs.AsNoTracking().AnyAsync(row =>
                row.Id == authority.Epoch.Id && row.Revision == authority.Epoch.Revision, token))
            return false;
        foreach (var human in authority.FencedHumans)
            if (await privacy.IsSubjectFencedAsync(human, token)
                || await context.PrivacyErasureSagas.AsNoTracking().AnyAsync(row => row.SubjectId == human, token))
                return false;
        foreach (var administrator in authority.Administrators)
        {
            var platform = await context.PlatformUserRoles.AsNoTracking().AnyAsync(row =>
                row.UserId == administrator && !row.User.IsDeleted
                && row.Role.RoleScopeId == (int)RoleScopeEnum.Platform
                && row.Role.MasterCode == "platform.admin", token);
            if (!platform && !await context.TenantUserRoleGrants.AsNoTracking().AnyAsync(row =>
                row.TenantId == authority.Tenant.Id && row.TenantUser.UserId == administrator
                && row.RevokedAt == null && row.RoleScopeId == (int)RoleScopeEnum.Tenant
                && row.RoleId == (int)RoleEnum.TenantAdmin && row.Role.RoleScopeId == (int)RoleScopeEnum.Tenant
                && row.TenantUser.StatusId == (int)TenantUserStatusEnum.Active
                && !row.TenantUser.IsDeleted && !row.TenantUser.User.IsDeleted, token))
                return false;
        }
        return machine.Current == principal && CurrentTransaction(transaction, authority.Tenant.Id);
    }

    private async Task<bool> ReplayAsync(ModerationOriginalEffectCompletionReceipt expected,
        OriginalProof original, ApiKeyPrincipalContext principal, IDbContextTransaction transaction, CancellationToken token)
    {
        // Receipt verification reads no private capture or new plan. No late minimized completion
        // contract exists: even invocation-local acknowledgement cannot bypass original custody.
        if (clock.GetUtcNow() >= expected.RetainUntil || machine.Current != principal) return false;
        context.ModerationTrustDomainId = expected.TrustDomainId;
        foreach (var human in original.Authority.FencedHumans.Order())
            await RelationalEntityRowFence.AcquireGlobalAsync<User>(context, human, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationHostBinding>(context, expected.HostBindingId, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ServicePrincipal>(
            context, original.Authority.Binding.MaintenanceServicePrincipalId, token);
        var actors = await context.Actors.AsNoTracking().IgnoreQueryFilters(["SoftDelete"])
            .Where(row => row.ServicePrincipalId == original.Authority.Binding.MaintenanceServicePrincipalId)
            .OrderBy(row => row.Id).Select(row => row.Id).ToListAsync(token);
        foreach (var actor in actors)
            await RelationalEntityRowFence.AcquireGlobalAsync<Actor>(context, actor, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ExternalApiKey>(context, original.KeyId, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationMachineSubjectBinding>(context, original.Credential.Id, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<Tenant>(context, expected.TenantId, token);
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(
            context, $"moderation.effect:{expected.EffectId:D}", token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationEffectIntent>(context, expected.EffectId, token);
        await using var noticeSlot = await RelationalNamedLock.AcquireTransactionAsync(
            context, $"moderation.notice:{expected.EffectId:D}", token);
        if (!await CurrentAuthorityAsync(original.Authority, principal, original.Credential, transaction, token))
            return false;
        var receipt = await context.ModerationOriginalEffectCompletionReceipts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == expected.Id && row.EffectId == expected.EffectId, token);
        var effect = await context.ModerationEffectIntents.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == expected.EffectId, token);
        var sourceMatches = await context.ModerationNoticeSources.AsNoTracking().AnyAsync(row =>
            row.EffectId == expected.EffectId && row.TenantId == expected.TenantId
            && row.RetainUntil == expected.RetainUntil && row.SnapshotHash == expected.SnapshotHash
            && row.EffectBindingHash == expected.EffectBindingHash, token);
        return receipt is not null && effect is not null && sourceMatches && original.Source.Matches(effect)
            && SameReceipt(expected, receipt) && receipt.MatchesCompletion(effect)
            && effect.OriginalCompletionId == expected.Id
            && effect.OriginalCompletionRevision == expected.LeaseRevision + 1
            && effect.HasOriginalCompletionAcknowledgement(expected.HostBindingId, expected.TenantId)
            && clock.GetUtcNow() >= expected.CompletedAt && clock.GetUtcNow() < expected.RetainUntil
            && await CurrentAuthorityAsync(original.Authority, principal, original.Credential, transaction, token);
    }

    private static bool SameReceipt(ModerationOriginalEffectCompletionReceipt left,
        ModerationOriginalEffectCompletionReceipt right) => Digest(left) == Digest(right);
    private static string Digest<T>(T value) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    private static ModerationOriginalEffectCompletionResult Pending(ModerationOriginalEffectCompletionPendingReason reason) =>
        new(ModerationOriginalEffectCompletionOutcome.Pending, PendingReason: reason);
    private sealed class CompletionPending(ModerationOriginalEffectCompletionPendingReason reason) : Exception
    {
        internal ModerationOriginalEffectCompletionPendingReason Reason { get; } = reason;
    }
    private sealed record OriginalProof(
        ModerationSourceAuthority Authority, ModerationMachineSubjectBinding Credential, Guid KeyId,
        ModerationNoticeSource Source, ModerationNoticeCaptureSnapshot Capture,
        ModerationContributionSelection Selection, DateTimeOffset ValidUntil, string Digest);
}

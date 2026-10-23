using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

/// <summary>
/// Native expiry of original disclosure payloads, never erasure of independently governed cases
/// or completion of substantive consequences. A missing/corrupt original write is not repaired
/// from current findings. Its expired payload can be removed without issuing a replay proof.
/// </summary>
public sealed partial class ModerationNoticeCleanupRepository(
    ExploreDbContext context, IMachinePrincipalAccessor machine,
    IModerationMachineSubjectBindingRepository bindings, IExternalApiKeyRepository keys,
    TimeProvider clock, IModerationNoticeNotificationCleanup? notifications = null)
    : IModerationNoticeCleanupRepository
{
    public async Task<ModerationNoticeCleanupResult> PruneAsync(
        Guid? afterApprovalId, int limit, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        if (afterApprovalId == Guid.Empty) throw new ArgumentException("A cursor must be a native identity.", nameof(afterApprovalId));
        if (!context.Database.IsRelational() || context.Database.CurrentTransaction is not null
            || context.ChangeTracker.Entries().Any() || context.IsTenantFilterBypassed
            || context.ModerationTrustDomainId is not null
            || context.TenantFilterTenantId is not { } tenantId || tenantId == Guid.Empty)
            return new(ModerationNoticeCleanupOutcome.Unavailable);
        if (machine.Current is not { CredentialSource: ApiKeyCredentialSource.Persisted } principal)
            return new(ModerationNoticeCleanupOutcome.Denied);
        return await context.Database.CreateExecutionStrategy().ExecuteAsync(
            () => PruneWindowAsync(afterApprovalId, limit, tenantId, principal, cancellationToken));
    }

    private async Task<ModerationNoticeCleanupResult> PruneWindowAsync(
        Guid? afterApprovalId, int limit, Guid tenantId, ApiKeyPrincipalContext principal, CancellationToken cancellationToken)
    {
        // No commit retry turns absence into success. A lost acknowledgement returns Unavailable;
        // retry the same cursor. The committed ledger marker, if any, remains exact replay evidence.
        var startedAt = clock.GetUtcNow();
        var cleanupId = Guid.CreateVersion7();
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var authority = await AcquireAuthorityAsync(tenantId, principal, cancellationToken);
            if (authority is null) return new(ModerationNoticeCleanupOutcome.Denied);
            var approvals = await context.ModerationNoticeDisclosureApprovals.AsNoTracking()
                .Where(row => row.HostBindingId == authority.Host.Id
                    && row.TrustDomainId == authority.Host.TrustDomainId && row.TenantId == tenantId
                    && (afterApprovalId == null || row.Id.CompareTo(afterApprovalId.Value) > 0))
                .OrderBy(row => row.Id).Take(limit).ToListAsync(cancellationToken);
            var due = await ResolveOriginalCleanupApprovalsAsync(
                approvals.Where(row => row.RetainUntil <= startedAt).ToArray(), authority.Host, tenantId, cancellationToken);
            if (due is null) return new(ModerationNoticeCleanupOutcome.Unavailable);
            var corrections = await DiscoverPendingCorrectionsAsync(due, authority.Host, tenantId, cancellationToken);
            if (corrections is null) return new(ModerationNoticeCleanupOutcome.Unavailable);
            if (!await FencePayloadsAsync(due, corrections, cancellationToken))
                return new(ModerationNoticeCleanupOutcome.Unavailable);
            var removed = new List<RemovedPayload>();
            foreach (var discovered in due)
            {
                var approval = await context.ModerationNoticeDisclosureApprovals.SingleOrDefaultAsync(
                    row => row.Id == discovered.Id, cancellationToken);
                if (approval is null || approval.HostBindingId != authority.Host.Id
                    || approval.TenantId != tenantId || approval.TrustDomainId != authority.Host.TrustDomainId
                    || approval.EffectId != discovered.EffectId || approval.RetainUntil != discovered.RetainUntil)
                    return new(ModerationNoticeCleanupOutcome.Unavailable);
                approval.ValidatePersistedState();
                if (clock.GetUtcNow() < startedAt || clock.GetUtcNow() < approval.RetainUntil)
                    return new(ModerationNoticeCleanupOutcome.Unavailable);

                var source = await context.ModerationNoticeSources.SingleOrDefaultAsync(
                    row => row.EffectId == approval.EffectId, cancellationToken);
                var effect = await context.ModerationEffectIntents.SingleOrDefaultAsync(
                    row => row.Id == approval.EffectId, cancellationToken);
                var occurrences = await context.ModerationNotices.Where(row => row.EffectId == approval.EffectId)
                    .OrderBy(row => row.Id).Take(1025).ToListAsync(cancellationToken);
                var acknowledgements = await context.Set<ModerationNoticeNotificationReceipt>()
                    .Where(row => row.EffectId == approval.EffectId).Take(2).ToListAsync(cancellationToken);
                // A valid 128 KiB original capture cannot name this many distinct reviewers.
                // An unbounded/cross-purpose corrupted graph needs recovery, never partial deletion.
                if (occurrences.Count > 1024 || acknowledgements.Count > 1
                    || source is not null && (source.TrustDomainId != approval.TrustDomainId
                        || source.RetainUntil != approval.RetainUntil || source.CapturedAt >= approval.RetainUntil
                        || source.TenantId is { } sourceTenant && sourceTenant != tenantId)
                    || occurrences.Any(row => row.TrustDomainId != approval.TrustDomainId
                        || row.TenantId is { } occurrenceTenant && occurrenceTenant != tenantId)
                    || acknowledgements.Any(row => row.TrustDomainId != approval.TrustDomainId
                        || row.TenantId != tenantId || row.RetainUntil != approval.RetainUntil))
                    return new(ModerationNoticeCleanupOutcome.Unavailable);

                var proven = source is not null && effect is not null
                    && await ProvesOriginalWriteAsync(approval, source, effect, occurrences, authority.Host, cancellationToken);
                if (effect?.OriginalNoticeCleanupId is not null)
                    return new(ModerationNoticeCleanupOutcome.Unavailable);
                var removedCorrections = await StagePendingCorrectionCleanupAsync(
                    approval, source, effect, occurrences, corrections, authority.Host, cancellationToken);
                if (removedCorrections is null) return new(ModerationNoticeCleanupOutcome.Unavailable);
                var targets = occurrences.Where(row => row.NativeIntentId is not null)
                    .Select(row => new ModerationNoticeNotificationCleanupTarget(
                        row.Id, row.NativeIntentId!.Value, row.RecipientSubjectId, (int)row.Audience))
                    .ToImmutableArray();
                var request = new ModerationNoticeNotificationCleanupRequest(transaction.TransactionId,
                    authority.Host.Id, authority.Host.TrustDomainId, tenantId, approval.EffectId,
                    approval.RetainUntil, targets);
                // A missing occurrence cannot hide an already materialized native graph.
                // The native materializer's exact effect prefix is not a notification receipt.
                var nativeCopies = !targets.IsEmpty
                    || await HasNativeCopiesAsync(tenantId, approval.EffectId, cancellationToken);
                // Response text and its finite submission receipt share the original source's
                // purpose. Remove both before its occurrences; do not keep a success tombstone
                // that could claim private text remains available after R.
                await context.Set<ModerationContentResponse>()
                    .Where(row => row.EffectId == approval.EffectId && row.TenantId == tenantId
                        && row.TrustDomainId == authority.Host.TrustDomainId)
                    .ExecuteDeleteAsync(cancellationToken);
                // Never acquire grant Humans after Host: the native owner takes Humans first.
                // Our held effect/source slot closes lawful issuance, including an absent source;
                // disposal needs neither original-write proof nor current/expired/revoked status.
                await context.ModerationAdjudicatedCaseReadGrants
                    .Where(row => row.OriginalEffectId == approval.EffectId && row.HostBindingId == authority.Host.Id
                        && row.TenantId == tenantId && row.TrustDomainId == authority.Host.TrustDomainId)
                    .ExecuteDeleteAsync(cancellationToken);
                context.RemoveRange(acknowledgements);
                context.RemoveRange(occurrences);
                if (nativeCopies && (notifications is null
                    || !await notifications.StageAsync(request, cancellationToken)))
                    return new(ModerationNoticeCleanupOutcome.Unavailable);
                if (source is not null) context.Remove(source);
                context.Remove(approval);
                var purgedAt = clock.GetUtcNow();
                if (purgedAt < startedAt || purgedAt < approval.RetainUntil)
                    return new(ModerationNoticeCleanupOutcome.Unavailable);
                if (proven)
                    effect!.RecordOriginalNoticeCleanup(source!, authority.Host.Id, tenantId,
                        cleanupId, effect.Revision, purgedAt);
                removed.Add(new(approval.Id, approval.EffectId, approval.RetainUntil, purgedAt, proven,
                    nativeCopies, request, removedCorrections.Value));
            }

            if (!await AuthorityCurrentAsync(authority, tenantId, principal, cancellationToken))
                return new(ModerationNoticeCleanupOutcome.Denied);
            await context.SaveChangesAsync(cancellationToken);
            foreach (var row in removed)
            {
                if (!await RemovedAsync(row, cleanupId, authority.Host.Id, tenantId, cancellationToken))
                    return new(ModerationNoticeCleanupOutcome.Unavailable);
            }
            if (!ReferenceEquals(context.Database.CurrentTransaction, transaction)
                || context.ModerationTrustDomainId != authority.Host.TrustDomainId
                || context.ChangeTracker.HasChanges() || clock.GetUtcNow() < startedAt
                || removed.Any(row => clock.GetUtcNow() < row.PurgedAt)
                || !await AuthorityCurrentAsync(authority, tenantId, principal, cancellationToken))
                return new(ModerationNoticeCleanupOutcome.Unavailable);
            // Final graph checks are adjacent to commit; the owning dispatch fences remain held.
            foreach (var row in removed.Where(row => row.NativeCopies))
                if (notifications is null || !await notifications.VerifyRemovedAsync(row.Notification, cancellationToken))
                    return new(ModerationNoticeCleanupOutcome.Unavailable);
            foreach (var row in removed)
                if (!await RemovedAsync(row, cleanupId, authority.Host.Id, tenantId, cancellationToken))
                    return new(ModerationNoticeCleanupOutcome.Unavailable);
            if (!ReferenceEquals(context.Database.CurrentTransaction, transaction) || context.ChangeTracker.HasChanges()
                || clock.GetUtcNow() < startedAt || removed.Any(row => clock.GetUtcNow() < row.PurgedAt)
                || !await AuthorityCurrentAsync(authority, tenantId, principal, cancellationToken))
                return new(ModerationNoticeCleanupOutcome.Unavailable);
            await transaction.CommitAsync(cancellationToken);
            return new(ModerationNoticeCleanupOutcome.Applied, approvals.LastOrDefault()?.Id,
                approvals.Count, removed.Count, removed.Count(row => row.Proven));
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(ModerationNoticeCleanupOutcome.Unavailable);
        }
        catch (DbException)
        {
            return new(ModerationNoticeCleanupOutcome.Unavailable);
        }
        catch (TimeoutException)
        {
            return new(ModerationNoticeCleanupOutcome.Unavailable);
        }
        catch (DbUpdateException exception) when (exception.InnerException is DbException)
        {
            return new(ModerationNoticeCleanupOutcome.Unavailable);
        }
        finally
        {
            context.ChangeTracker.Clear();
            context.ModerationTrustDomainId = null;
        }
    }

    private async Task<bool> FencePayloadsAsync(
        ModerationNoticeDisclosureApproval[] approvals, PendingCorrectionSource[] corrections, CancellationToken token)
    {
        // Host/tenant/epoch are already held. Preserve the read owner's occurrence -> original
        // source -> disclosure ordering, and sort every sibling set before taking its fences.
        var effects = approvals.Select(row => row.EffectId).Distinct().Order().ToArray();
        foreach (var id in approvals.Select(row => row.DecisionId).Distinct().Order())
            await RelationalEntityRowFence.AcquireGlobalAsync<ISLAMU.Moderation.Domain.ModerationDecision>(context, id, token);
        foreach (var id in effects)
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationEffectIntent>(context, id, token);
        var notices = await context.ModerationNotices.AsNoTracking().Where(row => effects.Contains(row.EffectId))
            .OrderBy(row => row.Id).Select(row => row.Id).Take(102401).ToArrayAsync(token);
        if (notices.Length > 102400)
            return false;
        foreach (var id in notices)
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationNotice>(context, id, token);
        foreach (var id in effects)
        {
            await using var fence = await RelationalNamedLock.AcquireTransactionAsync(context, $"moderation.notice:{id:D}", token);
        }
        // Capture resolves the original audience before taking its new Revoke slot. Never invert
        // that dependency by globally sorting original and correction effect/source identities.
        var dependentEffects = corrections.Select(row => row.EffectId).Order().ToArray();
        foreach (var id in dependentEffects)
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationEffectIntent>(context, id, token);
        var dependentNotices = await context.ModerationNotices.AsNoTracking()
            .Where(row => dependentEffects.Contains(row.EffectId)).OrderBy(row => row.Id)
            .Select(row => row.Id).Take(102401).ToArrayAsync(token);
        if (dependentNotices.Length > 102400)
            return false;
        foreach (var id in dependentNotices)
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationNotice>(context, id, token);
        foreach (var id in dependentEffects)
        {
            await using var fence = await RelationalNamedLock.AcquireTransactionAsync(context, $"moderation.notice:{id:D}", token);
        }
        foreach (var id in approvals.Select(row => row.Id).Order())
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationNoticeDisclosureApproval>(context, id, token);
        foreach (var id in corrections.Where(row => row.ApprovalId is not null)
            .Select(row => row.ApprovalId!.Value).Distinct().Order())
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationNoticeDisclosureApproval>(context, id, token);
        return true;
    }

    private async Task<bool> RemovedAsync(
        RemovedPayload row, Guid cleanupId, Guid hostId, Guid tenantId, CancellationToken token)
    {
        if (await context.ModerationNoticeDisclosureApprovals.AsNoTracking().AnyAsync(item => item.Id == row.ApprovalId, token)
            || await context.ModerationNoticeSources.AsNoTracking().AnyAsync(item => item.EffectId == row.EffectId, token)
            || await context.ModerationNotices.AsNoTracking().AnyAsync(item => item.EffectId == row.EffectId, token)
            || await context.Set<ModerationNoticeNotificationReceipt>().AsNoTracking()
                .AnyAsync(item => item.EffectId == row.EffectId, token)
            || await context.Set<ModerationContentResponse>().AsNoTracking()
                .AnyAsync(item => item.EffectId == row.EffectId, token)
            || await context.ModerationAdjudicatedCaseReadGrants.AsNoTracking()
                .AnyAsync(item => item.OriginalEffectId == row.EffectId && item.HostBindingId == hostId
                    && item.TenantId == tenantId && item.TrustDomainId == row.Notification.TrustDomainId, token))
            return false;
        if (await HasNativeCopiesAsync(tenantId, row.EffectId, token)
            || !await PendingCorrectionsRemovedAsync(row.Corrections, tenantId, token)) return false;
        if (row.NativeCopies && (notifications is null
            || !await notifications.VerifyRemovedAsync(row.Notification, token))) return false;
        if (!row.Proven) return true;
        var effect = await context.ModerationEffectIntents.AsNoTracking().SingleOrDefaultAsync(item => item.Id == row.EffectId, token);
        return effect is not null && effect.OriginalNoticeCleanupId == cleanupId
            && effect.OriginalNoticeRetainUntil == row.RetainUntil && effect.OriginalNoticePurgedAt == row.PurgedAt
            && effect.HasOriginalNoticeCleanup(hostId, tenantId, clock.GetUtcNow());
    }

    private Task<bool> HasNativeCopiesAsync(Guid tenantId, Guid effectId, CancellationToken token)
    {
        var prefix = $"moderation-notice:{effectId:N}:";
        return context.NotificationIntents.AsNoTracking().IgnoreQueryFilters(["SoftDelete"])
            .AnyAsync(row => row.TenantId == tenantId
                && row.CategoryId == (int)NotificationCategoryEnum.TrustSafetyModeration
                && row.TemplateKey == "moderation.notice.portal.v1"
                && row.DeduplicationKey.StartsWith(prefix), token);
    }

    private sealed record RemovedPayload(
        Guid ApprovalId, Guid EffectId, DateTimeOffset RetainUntil, DateTimeOffset PurgedAt,
        bool Proven, bool NativeCopies, ModerationNoticeNotificationCleanupRequest Notification,
        ImmutableArray<RemovedCorrection> Corrections);
}

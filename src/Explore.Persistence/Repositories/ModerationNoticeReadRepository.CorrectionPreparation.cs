using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Contracts.Notifications;
using Explore.Domain;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

internal sealed record ModerationNoticeCorrectionPreparationRequest(
    Guid PendingEffectId, string ExpectedPendingSnapshotHash, string ExpectedPendingEffectBindingHash,
    Guid ApprovalId, Guid GrantId, Guid CaseId, long ExpectedCaseRevision, Guid OriginalNoticeId)
{
    public override string ToString() => nameof(ModerationNoticeCorrectionPreparationRequest);
}

public sealed partial class ModerationNoticeReadRepository
{
    /// <summary>Joins current personal permission and approved text without returning or materializing either.</summary>
    internal async Task<bool> PrepareCorrectionAsync(ModerationNoticeCorrectionPreparationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Correction preparation requires its own clean native transaction.");
        if (!context.Database.IsRelational() || context.ModerationTrustDomainId is not null
            || context.IsTenantFilterBypassed || machine.IsMachineCaller || context.TenantFilterTenantId is not { } tenant
            || tenant == Guid.Empty || request.PendingEffectId == Guid.Empty || request.ApprovalId == Guid.Empty
            || request.OriginalNoticeId == Guid.Empty || !CorrectionHash(request.ExpectedPendingSnapshotHash)
            || !CorrectionHash(request.ExpectedPendingEffectBindingHash)) return false;
        try
        {
            return await EfCoreUnitOfWork.ExecuteBootstrapConflictRetryAsync(async () =>
                await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
                {
                    context.ChangeTracker.Clear();
                    context.ModerationTrustDomainId = null;
                    ModerationAdjudicatedCaseReadGrantRepository.ReadAcquisition? permission = null;
                    CorrectionPreparation? prepared = null;
                    async Task Recheck(CancellationToken token)
                    {
                        if (permission is null || prepared is null || clock.GetUtcNow() >= prepared.Until
                            || !await permission.RevalidateAsync(token, prepared.Until) || clock.GetUtcNow() >= prepared.Until
                            || await QualifyCorrectionPreparationAsync(request, permission, token, prepared.Until) != prepared
                            || !await permission.RevalidateAsync(token, prepared.Until) || clock.GetUtcNow() >= prepared.Until
                            || context.ChangeTracker.HasChanges()) throw new CorrectionUnavailable();
                    }
                    return await privacy.ExecuteEnrollmentSerializedAsync(async privacyToken =>
                        await new EfCoreUnitOfWork(context).ExecuteRecognitionEnrollmentAttemptAsync(async token =>
                        {
                            var grants = new ModerationAdjudicatedCaseReadGrantRepository(context, privacy, identity, machine, clock);
                            permission = await grants.AcquireCurrentInTransactionAsync(
                                request.GrantId, request.CaseId, request.ExpectedCaseRevision, token)
                                ?? throw new CorrectionUnavailable();
                            prepared = await QualifyCorrectionPreparationAsync(request, permission, token)
                                ?? throw new CorrectionUnavailable();
                            return true;
                        }, Recheck, privacyToken), Recheck, cancellationToken);
                }), cancellationToken);
        }
        catch (CorrectionUnavailable) { return false; }
        catch (DbException) { return false; }
        catch (TimeoutException) { return false; }
        catch (JsonException) { return false; }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        finally { context.ChangeTracker.Clear(); context.ModerationTrustDomainId = null; }
    }

    private async Task<CorrectionPreparation?> QualifyCorrectionPreparationAsync(
        ModerationNoticeCorrectionPreparationRequest request,
        ModerationAdjudicatedCaseReadGrantRepository.ReadAcquisition permission, CancellationToken token,
        DateTimeOffset? accessUntil = null, Action<ModerationNoticeDisclosureIntent>? approved = null)
    {
        var grant = permission.Grant;
        var authority = permission.Authority;
        var accessDeadline = accessUntil is { } access && access < grant.ExpiresAt ? access : grant.ExpiresAt;
        bool Live() => clock.GetUtcNow() >= grant.IssuedAt
            && clock.GetUtcNow() < accessDeadline && clock.GetUtcNow() < grant.OriginalRetainUntil;
        if (!Live() || machine.IsMachineCaller || await identity.ResolveUserIdAsync(token) != grant.RecipientUserId)
            return null;
        var decision = await context.ModerationDecisions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CaseId == request.CaseId && row.Id ==
                context.ModerationEffectIntents.Where(effect => effect.Id == request.PendingEffectId)
                    .Select(effect => effect.DecisionId).FirstOrDefault(), token);
        var effect = await context.ModerationEffectIntents.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == request.PendingEffectId, token);
        if (!Live() || decision is null || effect is null) return null;
        var original = await ModerationNoticeCorrectionAudience.ReadAsync(context, effect, decision, clock, token,
            accessDeadline);
        if (!Live() || original is null || original.OriginalEffectId != grant.OriginalEffectId
            || original.OriginalSnapshotHash != grant.OriginalSnapshotHash
            || original.OriginalEffectBindingHash != grant.OriginalEffectBindingHash
            || original.RetainUntil != grant.OriginalRetainUntil
            || !original.Recipients.Any(row => row.OriginalNoticeId == request.OriginalNoticeId
                && row.RecipientSubjectKind == (int)ModerationSubjectKind.Human
                && row.RecipientSubjectId == grant.RecipientUserId)) return null;

        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationEffectIntent>(context, effect.Id, token);
        var ids = await context.ModerationNotices.AsNoTracking().Where(row => row.EffectId == effect.Id)
            .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(token);
        foreach (var id in ids) await RelationalEntityRowFence.AcquireGlobalAsync<ModerationNotice>(context, id, token);
        await using var sourceSlot = await RelationalNamedLock.AcquireTransactionAsync(
            context, $"moderation.notice:{effect.Id:D}", token);
        if (!Live()) return null;
        var originalSource = await context.ModerationNoticeSources.AsNoTracking().SingleOrDefaultAsync(row =>
            row.EffectId == original.OriginalEffectId && row.SnapshotHash == original.OriginalSnapshotHash
                && row.EffectBindingHash == original.OriginalEffectBindingHash
                && row.RetainUntil == grant.OriginalRetainUntil && row.RetainUntil > clock.GetUtcNow(), token);
        if (!Live() || originalSource is null) return null;
        var originalCapture = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(originalSource.SnapshotJson);
        if (originalCapture?.Notice is not { } originalNotice) return null;
        // Retain the same original-before-dependent disclosure lock order as approval and purpose cleanup.
        await using var originalApprovalSlot = await RelationalNamedLock.AcquireTransactionAsync(
            context, $"moderation.disclosure:{authority.Binding.Id:N}:{original.OriginalEffectId:N}", token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationNoticeDisclosureApproval>(context, originalNotice.ApprovalId, token);
        await using var approvalSlot = await RelationalNamedLock.AcquireTransactionAsync(
            context, $"moderation.disclosure:{authority.Binding.Id:N}:{effect.Id:N}", token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationNoticeDisclosureApproval>(context, request.ApprovalId, token);
        var readAt = clock.GetUtcNow();
        if (!Live()) return null;
        var envelope = await context.ModerationNoticeDisclosureApprovals.AsNoTracking()
            .Where(row => row.Id == request.ApprovalId).Select(row => new
            {
                row.EffectId,
                row.CaseId,
                row.CaseRevision,
                row.DecisionId,
                row.DecisionRevision,
                row.HostBindingId,
                row.TrustDomainId,
                row.TenantId,
                row.RetainUntil,
                row.ValidUntil,
                row.Revision,
                row.RevokedAt
            }).SingleOrDefaultAsync(token);
        if (!Live() || envelope is null || envelope.EffectId != effect.Id || envelope.CaseId != request.CaseId
            || envelope.CaseRevision != request.ExpectedCaseRevision || envelope.DecisionId != decision.Id
            || envelope.DecisionRevision != decision.Revision || envelope.HostBindingId != authority.Binding.Id
            || envelope.TrustDomainId != authority.Binding.TrustDomainId || envelope.TenantId != authority.Tenant.Id
            || envelope.RetainUntil != grant.OriginalRetainUntil || envelope.Revision != 1 || envelope.RevokedAt is not null
            || clock.GetUtcNow() >= envelope.ValidUntil) return null;
        if (envelope.ValidUntil < accessDeadline) accessDeadline = envelope.ValidUntil;
        readAt = clock.GetUtcNow();
        if (!Live()) return null;
        var approval = await context.ModerationNoticeDisclosureApprovals.AsNoTracking().SingleOrDefaultAsync(row =>
            row.Id == request.ApprovalId && row.Revision == 1 && row.RevokedAt == null
                && row.RetainUntil == grant.OriginalRetainUntil && row.RetainUntil > readAt && row.ValidUntil > readAt, token);
        if (!Live() || approval is null || clock.GetUtcNow() >= approval.ValidUntil) return null;
        approval.ValidatePersistedState();
        var intent = approval.Intent;
        if (!approval.IsCurrentAt(intent, clock.GetUtcNow())) return null;
        var until = await ModerationNoticeCorrectionDisclosureQualification.ReadAsync(context, intent,
            request.ExpectedPendingSnapshotHash, request.ExpectedPendingEffectBindingHash,
            authority, clock, token, accessDeadline);
        if (!Live() || until is null || clock.GetUtcNow() >= until) return null;
        var source = await context.ModerationNoticeSources.AsNoTracking().SingleOrDefaultAsync(row =>
            row.EffectId == effect.Id && row.SnapshotHash == request.ExpectedPendingSnapshotHash
                && row.EffectBindingHash == request.ExpectedPendingEffectBindingHash
                && row.RetainUntil == grant.OriginalRetainUntil && row.RetainUntil > clock.GetUtcNow(), token);
        if (!Live() || clock.GetUtcNow() >= until || source is null
            || !await ModerationNoticeCorrectionApprovalProof.MatchesReceiptAsync(context, approval, source, decision.Scope, token))
            return null;
        var deadline = new[] { accessDeadline, grant.OriginalRetainUntil, approval.ValidUntil, until.Value }.Min();
        if (!Live() || clock.GetUtcNow() >= deadline) return null;
        var payload = JsonSerializer.SerializeToUtf8Bytes(approval);
        try
        {
            var digest = Convert.ToHexString(SHA256.HashData(payload));
            if (!Live() || clock.GetUtcNow() >= deadline) return null;
            approved?.Invoke(intent);
            return new(digest, deadline);
        }
        finally { CryptographicOperations.ZeroMemory(payload); }
    }

    private static bool CorrectionHash(string? value) => value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    private sealed record CorrectionPreparation(string ApprovalDigest, DateTimeOffset Until);
    private sealed class CorrectionUnavailable : Exception;
}

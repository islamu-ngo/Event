using Explore.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationRecognitionErasureRepository
{
    private async Task<ModerationRecognitionContinuityEvaluation> StageActionRecoveryAsync(
        Guid freshSubjectId, Guid? verifiedBindingId, IReadOnlyList<ModerationRecognitionCaptureItem> candidates,
        ModerationRecognitionContinuityEvaluation result, CancellationToken token)
    {
        if (verifiedBindingId is not { } bindingId || bindingId == Guid.Empty
            || result.ActionFailure == ModerationRecognitionContinuityFailure.None
            || result.AdmissionFailure != ModerationRecognitionContinuityFailure.None || result.AdmissionExcluded)
            return result;
        var actions = candidates.Where(item =>
            item.Purpose == ModerationReferenceRecognition.SeparatelyQualifiedFinalAction).ToArray();
        if (actions.Length == 0) return result;
        var now = clock.GetUtcNow();
        var intentIds = actions.Select(item => item.IntentId).Distinct().ToArray();
        var pending = await Rows<ModerationRecognitionPending>().Include(row => row.Material)
            .Where(row => intentIds.Contains(row.IntentId)).ToListAsync(token);
        if (actions.Any(item => !pending.Any(receipt => receipt.IntentId == item.IntentId
            && receipt.SubjectId == item.ErasureSubjectId
            && receipt.HasCompleteMaterial && receipt.AuthoritySequence is not null
            && receipt.State == ModerationRecognitionPendingState.AuthorityBound
            && receipt.Failure == ModerationRecognitionPendingFailure.None
            && receipt.Material.Any(original => original.Id == item.Id)))) return result;
        try
        {
            foreach (var item in actions)
                await RequireCaptureSettlementAsync(item, token);
        }
        catch (ContinuityUnavailable) { return result; }
        var itemIds = actions.Select(item => item.Id).ToArray();
        var existing = await Rows<ModerationRecognitionActionRecovery>()
            .Where(row => row.FreshSubjectId == freshSubjectId && row.VerifiedBindingId == bindingId
                && itemIds.Contains(row.CaptureItemId))
            .ToListAsync(token);
        foreach (var item in actions)
        {
            var receipt = pending.Single(row => row.IntentId == item.IntentId);
            var recovery = existing.SingleOrDefault(row => row.CaptureItemId == item.Id);
            if (recovery is null)
            {
                recovery = ModerationRecognitionActionRecovery.Receive(Guid.CreateVersion7(), freshSubjectId, bindingId,
                    item, receipt, now);
                context.Set<ModerationRecognitionActionRecovery>().Add(recovery);
            }
            else
            {
                recovery.ValidatePersistedState();
                if (recovery.CaptureMaterialReceipt != item.ComputeMaterialReceipt()
                    || recovery.ResolvedAt is not null || now >= recovery.DispositionBy)
                    return result;
            }
            result = result with { CurrentUntil = Earlier(result.CurrentUntil, recovery.DispositionBy) };
        }
        return result with { ActionRecoveryStaged = true };
    }

    private async Task<ModerationRecognitionContinuityEvaluation> CompleteActionRecoveryAsync(
        Guid freshSubjectId, Guid? verifiedBindingId, ModerationRecognitionContinuityEvaluation result,
        CancellationToken token)
    {
        if (verifiedBindingId is not { } bindingId || result.ActionFailure != ModerationRecognitionContinuityFailure.None
            || result.AdmissionFailure != ModerationRecognitionContinuityFailure.None || result.AdmissionExcluded)
            return result;
        var unresolved = await Rows<ModerationRecognitionActionRecovery>().Where(row =>
            row.FreshSubjectId == freshSubjectId && row.VerifiedBindingId == bindingId && row.ResolvedAt == null)
            .ToListAsync(token);
        if (unresolved.Count == 0) return result;
        var itemIds = unresolved.Select(row => row.CaptureItemId).Distinct().ToArray();
        var originals = await Rows<ModerationRecognitionCaptureItem>()
            .Where(row => itemIds.Contains(row.Id)).ToDictionaryAsync(row => row.Id, token);
        foreach (var recovery in unresolved)
        {
            recovery.ValidatePersistedState();
            if (!originals.TryGetValue(recovery.CaptureItemId, out var item)
                || item.IntentId != recovery.OriginalIntentId
                || item.ComputeMaterialReceipt() != recovery.CaptureMaterialReceipt)
                throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.CanonicalUnavailable);
        }
        foreach (var recovery in unresolved)
        {
            context.Attach(recovery);
            recovery.Resolve(clock.GetUtcNow());
        }
        return result;
    }
}

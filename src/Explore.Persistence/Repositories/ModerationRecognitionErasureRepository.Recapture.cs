using Explore.Domain;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationRecognitionErasureRepository
{
    private async Task<(ModerationRecognitionContinuityBinding[] Bindings, ModerationRecognitionCaptureItem[] Items)>
        ReadRecaptureInventoryAsync(Guid subjectId, CancellationToken token)
    {
        // Only completed native matches are contributions. Operational action-recovery rows
        // are deliberately absent: uncertainty is not an accusation or capture authority.
        var bindings = await Rows<ModerationRecognitionContinuityBinding>()
            .Where(row => row.FreshSubjectId == subjectId).OrderBy(row => row.Id).ToArrayAsync(token);
        var items = await Rows<ModerationRecognitionCaptureItem>()
            .Where(row => row.ErasureSubjectId == subjectId).ToDictionaryAsync(row => row.Id, token);
        var next = bindings.Select(row => row.CaptureItemId)
            .Concat(items.Values.Where(row => row.ParentCaptureItemId != null).Select(row => row.ParentCaptureItemId!.Value))
            .Distinct().ToArray();
        while (next.Length != 0)
        {
            var missing = next.Where(id => !items.ContainsKey(id)).ToArray();
            if (missing.Length == 0) break;
            var parents = await Rows<ModerationRecognitionCaptureItem>().Where(row => missing.Contains(row.Id)).ToArrayAsync(token);
            if (parents.Length != missing.Length)
                throw new CaptureUnavailable(ModerationRecognitionPendingFailure.SourceUnavailable);
            foreach (var parent in parents) items.Add(parent.Id, parent);
            next = parents.Where(row => row.ParentCaptureItemId != null).Select(row => row.ParentCaptureItemId!.Value).Distinct().ToArray();
        }
        return (bindings, items.Values.ToArray());
    }

    private async Task AcquireCaptureCanonicalAnchorsAsync(Guid subjectId, CaptureAuthority authority, CancellationToken token)
    {
        var decisionIds = authority.CaptureItems.Select(row => row.DecisionId).ToArray();
        var sanctions = await Rows<ModerationSanction>().Where(row =>
            row.SubjectKind == ModerationSubjectKind.Human && row.SubjectId == subjectId
            || decisionIds.Contains(row.DecisionId)).ToArrayAsync(token);
        decisionIds = decisionIds.Concat(sanctions.Select(row => row.DecisionId)).Distinct().ToArray();
        var decisions = await Rows<ModerationDecision>().Where(row => decisionIds.Contains(row.Id)).ToArrayAsync(token);
        foreach (var id in authority.CaptureItems.Select(row => row.IntentId).Distinct().Order())
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationRecognitionPending>(context, id, token);
        foreach (var item in authority.CaptureItems.OrderBy(row => row.Id))
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationRecognitionCaptureItem>(context, item.Id, token);
        foreach (var binding in authority.RecaptureBindings.OrderBy(row => row.Id))
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationRecognitionContinuityBinding>(context, binding.Id, token);
        foreach (var id in decisions.Select(row => row.CaseId).Distinct().Order())
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationCase>(context, id, token);
        foreach (var id in decisionIds.Order())
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationDecision>(context, id, token);
        foreach (var id in sanctions.Select(row => row.Id).Distinct().Order())
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationSanction>(context, id, token);
        foreach (var id in decisions.Where(row => row.FindingId != null).Select(row => row.FindingId!.Value).Distinct().Order())
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationFinding>(context, id, token);
    }

    private async Task<List<QualifiedContribution>> BoundContributionsAsync(
        CaptureAuthority authority, Guid erasureSubjectId, CancellationToken token)
    {
        var contributions = new List<QualifiedContribution>();
        if (authority.RecaptureBindings.Length != 0 && authority.Host is null)
            throw new CaptureUnavailable(ModerationRecognitionPendingFailure.SourceUnavailable);
        try
        {
            foreach (var discovered in authority.RecaptureBindings.OrderBy(row => row.SanctionId))
            {
                var binding = await Rows<ModerationRecognitionContinuityBinding>()
                    .SingleAsync(row => row.Id == discovered.Id && row.FreshSubjectId == erasureSubjectId, token);
                binding.ValidatePersistedState();
                var item = await Rows<ModerationRecognitionCaptureItem>()
                    .SingleOrDefaultAsync(row => row.Id == binding.CaptureItemId, token)
                    ?? throw new CaptureUnavailable(ModerationRecognitionPendingFailure.SourceUnavailable);
                if (binding.CaptureMaterialReceipt != item.ComputeMaterialReceipt())
                    throw new CaptureUnavailable(ModerationRecognitionPendingFailure.OriginalMaterialChanged);
                var qualified = await QualifyContinuityAsync(authority, item, token);
                if (qualified is null) continue;
                if (!binding.IsCurrent(item, qualified.Decision, qualified.Sanction, clock.GetUtcNow()))
                    throw new CaptureUnavailable(ModerationRecognitionPendingFailure.OriginalMaterialChanged);
                var source = await Rows<ModerationRecognitionPrivacySource>().Include(row => row.Retention)
                    .SingleAsync(row => row.Id == item.SourceId, token);
                var adoption = await Rows<ModerationRecognitionPrivacyAdoption>().SingleAsync(row => row.Id == item.AdoptionId, token);
                contributions.Add(new(qualified.Decision, qualified.Sanction, source, adoption, item.Purpose, item, binding));
            }
        }
        catch (ContinuityUnavailable)
        {
            throw new CaptureUnavailable(ModerationRecognitionPendingFailure.SourceUnavailable);
        }
        return contributions;
    }

    private async Task<Guid[]> ReadContinuityLineageSubjectsAsync(
        IEnumerable<ModerationRecognitionCaptureItem> captured, CancellationToken token)
    {
        var items = captured.ToDictionary(row => row.Id);
        var next = items.Values.Where(row => row.ParentCaptureItemId != null)
            .Select(row => row.ParentCaptureItemId!.Value).Distinct().ToArray();
        while (next.Length != 0)
        {
            var missing = next.Where(id => !items.ContainsKey(id)).ToArray();
            if (missing.Length == 0) break;
            var parents = await Rows<ModerationRecognitionCaptureItem>().Where(row => missing.Contains(row.Id)).ToArrayAsync(token);
            if (parents.Length != missing.Length)
                throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.CanonicalUnavailable);
            foreach (var parent in parents) items.Add(parent.Id, parent);
            next = parents.Where(row => row.ParentCaptureItemId != null)
                .Select(row => row.ParentCaptureItemId!.Value).Distinct().ToArray();
        }
        return items.Values.SelectMany(row => new[] { row.SubjectId, row.ErasureSubjectId }).Distinct().Order().ToArray();
    }

    /// <summary>
    /// Snapshot-only chain validation is shared by capture and post-resource continuity reads.
    /// It never borrows destroyed identifiers, resolves keys, or takes an identity/User lock.
    /// The immutable child receipt references a retained binding and its exact parent receipt;
    /// an absent/mutated link is unavailable, while a dropped/expired ancestor stops only this chain.
    /// </summary>
    private async Task<bool> RecaptureLineageCurrentAsync(ModerationRecognitionCaptureItem item, CancellationToken token)
    {
        var seen = new HashSet<Guid> { item.Id };
        while (item.ParentCaptureItemId is { } parentId)
        {
            if (!seen.Add(parentId))
                throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.CanonicalUnavailable);
            var parent = await Rows<ModerationRecognitionCaptureItem>().SingleOrDefaultAsync(row => row.Id == parentId, token);
            var binding = await Rows<ModerationRecognitionContinuityBinding>()
                .SingleOrDefaultAsync(row => row.Id == item.ContinuityBindingId, token);
            if (parent is null || binding is null)
                throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.CanonicalUnavailable);
            parent.ValidatePersistedState();
            binding.ValidatePersistedState();
            if (!item.PreservesOriginalContribution(parent) || item.IntentId == parent.IntentId
                || item.ErasureSubjectId == parent.ErasureSubjectId
                || item.CapturedAt < parent.CapturedAt || item.CapturedAt < binding.BoundAt
                || binding.FreshSubjectId != item.ErasureSubjectId || binding.CaptureItemId != parent.Id
                || binding.CaptureMaterialReceipt != parent.ComputeMaterialReceipt()
                || binding.SanctionId != item.SanctionId || binding.FindingId != item.FindingId
                || binding.RetainUntil != item.RetainUntil || parent.SubjectId == binding.FreshSubjectId)
                throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.CanonicalUnavailable);
            await RequireCaptureSettlementAsync(parent, token);
            if (!ModerationRecognitionContinuityBinding.WithinOriginalInterval(parent, clock.GetUtcNow())) return false;
            item = parent;
        }
        return true;
    }
}

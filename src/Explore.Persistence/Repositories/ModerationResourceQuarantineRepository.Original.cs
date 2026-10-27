using System.Data;
using System.Text.Json;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationResourceQuarantineRepository
{
    public async Task<ModerationOriginalQuarantineResult> ExecuteOriginalAsync(
        Guid effectId, Guid leaseId, long expectedEffectRevision, CancellationToken cancellationToken = default)
    {
        if (effectId == Guid.Empty || leaseId == Guid.Empty || expectedEffectRevision is < 1 or long.MaxValue)
            throw new ArgumentException("Original execution requires the exact native lease.");
        if (!context.Database.IsRelational() || context.Database.CurrentTransaction is not null
            || context.IsTenantFilterBypassed || context.ModerationTrustDomainId is not null
            || context.ChangeTracker.Entries().Any())
            return new(ModerationOriginalQuarantineOutcome.Unavailable, 0, []);
        var targets = new List<(int Ordinal, Guid ResourceId, Guid MaterialId)>();
        try
        {
            var discovered = await privacy.ExecuteSerializedAsync(async token =>
            {
                if (context.Database.CurrentTransaction is not null && !context.IdentityFenceOwnsTransaction)
                    return false;
                await using IDbContextTransaction? owned = context.IdentityFenceOwnsTransaction ? null
                    : await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                var authority = await new ModerationMaintenanceAuthority(context, machine, bindings, keys, clock)
                    .AcquireForEffectAsync(effectId, leaseId, expectedEffectRevision, privacy, token);
                if (authority is null) return false;
                var ownership = await context.ModerationNoticeSources.AsNoTracking()
                    .Where(row => row.EffectId == effectId)
                    .Select(row => new { row.TenantId, row.RetainUntil }).SingleOrDefaultAsync(token);
                if (ownership is null || ownership.TenantId != authority.Tenant.Id
                    || ownership.RetainUntil is not { } ceiling || clock.GetUtcNow() >= ceiling)
                    return false;
                var source = await context.ModerationNoticeSources.AsNoTracking()
                    .SingleOrDefaultAsync(row => row.EffectId == effectId, token);
                var effect = await context.ModerationEffectIntents.AsNoTracking()
                    .SingleOrDefaultAsync(row => row.Id == effectId, token);
                if (source is null || effect is null || !source.Matches(effect) || clock.GetUtcNow() >= ceiling)
                    return false;
                ModerationNoticeCaptureSnapshot? capture;
                try { capture = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(source.SnapshotJson); }
                catch (JsonException) { return false; }
                if (capture is null || !capture.Matches(effect) || capture.Notice is not { } notice
                    || notice.ReferenceProfile != ModerationReferenceProfileId.CFG09
                    || notice.Effects.IsDefaultOrEmpty || notice.Effects.Length > 64)
                    return false;
                for (int ordinal = 0; ordinal < notice.Effects.Length; ordinal++)
                {
                    var original = notice.Effects[ordinal];
                    if (original.Effect is not
                        {
                            Kind: ModerationReferenceEffectKind.UnsafeResourceVersion,
                            Mode: ModerationReferenceEffectMode.Quarantine,
                            Target: ModerationReferenceTarget.SelectedResourceVersion,
                            Capability: ModerationCapability.ReadResource,
                            Execution: ModerationReferenceExecution.NativeOwner
                        }
                        || original.Target.Owner != "event.resource-generation" || original.Target.Source is not { } stamp)
                        return false;
                    var material = await context.ModerationResourceGenerations.AsNoTracking()
                        .Where(row => row.Id == stamp.Id && row.ResourceId == original.Target.TargetId
                            && row.TenantId == authority.Tenant.Id && row.HostBindingId == authority.Binding.Id)
                        .Select(row => (Guid?)row.MaterialVersionId).SingleOrDefaultAsync(token);
                    if (material is not { } materialId || materialId == Guid.Empty)
                        return false;
                    targets.Add((ordinal, original.Target.TargetId, materialId));
                }
                return true;
            }, cancellationToken);
            if (!discovered)
                return new(ModerationOriginalQuarantineOutcome.Unavailable, 0, []);
        }
        finally
        {
            context.ChangeTracker.Clear();
            context.ModerationTrustDomainId = null;
        }

        // Discovery never supplies permission. Each action reacquires the current credential,
        // original purpose, exact lease, immutable plans and native target/material authority.
        var receipts = new List<EventResourceModerationQuarantine>();
        foreach (var target in targets)
        {
            var result = await ExecuteAsync(effectId, leaseId, expectedEffectRevision, target.Ordinal,
                target.ResourceId, target.MaterialId, cancellationToken);
            if (result.Outcome is not (ModerationResourceQuarantineOutcome.Applied
                    or ModerationResourceQuarantineOutcome.AlreadyApplied
                    or ModerationResourceQuarantineOutcome.Retired) || result.Receipt is null)
                return new(ModerationOriginalQuarantineOutcome.Partial, targets.Count, receipts);
            receipts.Add(result.Receipt);
        }
        return new(ModerationOriginalQuarantineOutcome.Accounted, targets.Count, receipts);
    }
}

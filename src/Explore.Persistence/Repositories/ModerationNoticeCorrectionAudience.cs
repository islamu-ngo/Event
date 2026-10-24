using System.Collections.Immutable;
using System.Data;
using System.Text.Json;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

/// <summary>Retained recipient identity is an obligation, never current disclosure authority.</summary>
internal static class ModerationNoticeCorrectionAudience
{
    internal static async Task<ModerationNoticeCorrectionObligations?> ReadAsync(
        ExploreDbContext context, ModerationEffectIntent revoke, ModerationDecision decision,
        TimeProvider clock, CancellationToken token, DateTimeOffset? accessUntil = null)
    {
        var now = clock.GetUtcNow();
        if (!context.Database.IsRelational() || context.Database.CurrentTransaction is not { } transaction
            || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable
            || context.IsTenantFilterBypassed || context.TenantFilterTenantId is not { } tenant
            || context.ModerationTrustDomainId != revoke.TrustDomainId
            || revoke.OperationKind != (int)ModerationOperationKind.Revoke || revoke.SanctionId is not { } sanctionId
            || revoke.DecisionId != decision.Id || revoke.DecisionRevision != decision.Revision
            || revoke.SubjectKind != (int)decision.Subject.Kind || revoke.SubjectId != decision.Subject.Id
            || revoke.ScopeKind != (int)decision.Scope.Kind || revoke.ScopeId != decision.Scope.Id
            || now < revoke.CreatedAt || accessUntil is { } initialBound && now >= initialBound)
            return null;
        var sanction = context.ModerationSanctions.Local.SingleOrDefault(row => row.Id == sanctionId)
            ?? await context.ModerationSanctions.AsNoTracking().SingleOrDefaultAsync(row => row.Id == sanctionId, token);
        if (sanction is null || sanction.State != ModerationSanctionState.Revoked
            || sanction.Revision != revoke.SanctionRevision || sanction.DecisionId != decision.Id
            || sanction.Subject != decision.Subject || sanction.Scope != decision.Scope || sanction.Rule != decision.Rule
            || sanction.CapabilityMask != decision.CapabilityMask || sanction.StartsAt != decision.StartsAt
            || sanction.EndsAt != decision.EndsAt || sanction.ReviewAt != decision.ReviewAt)
            return null;

        var creating = await Creators().Take(2).ToArrayAsync(token);
        if (creating.Length != 1) return null;
        var original = creating[0];
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationEffectIntent>(context, original.Id, token);
        Guid[] noticeIds = await context.ModerationNotices.AsNoTracking().Where(row => row.EffectId == original.Id)
            .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(token);
        foreach (var id in noticeIds)
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationNotice>(context, id, token);
        await using var sourceFence = await RelationalNamedLock.AcquireTransactionAsync(
            context, $"moderation.notice:{original.Id:D}", token);
        creating = await Creators().Take(2).ToArrayAsync(token);
        if (creating.Length != 1 || creating[0].Id != original.Id) return null;
        original = creating[0];
        var envelope = await context.ModerationNoticeSources.AsNoTracking()
            .Where(row => row.EffectId == original.Id)
            .Select(row => new { row.TenantId, row.RetainUntil, row.SnapshotHash, row.EffectBindingHash })
            .SingleOrDefaultAsync(token);
        now = clock.GetUtcNow();
        if (envelope is null || envelope.TenantId is { } envelopeTenant && envelopeTenant != tenant
            || envelope.RetainUntil is not { } until
            || until <= now || until == DateTimeOffset.MaxValue || now < revoke.CreatedAt
            || accessUntil is { } accessBound && now >= accessBound)
            return null;
        // The retention envelope is not private custody. Only fetch the body while its original purpose is live.
        var source = await context.ModerationNoticeSources.AsNoTracking()
            .SingleOrDefaultAsync(row => row.EffectId == original.Id && row.RetainUntil == until
                && row.RetainUntil > now && row.SnapshotHash == envelope.SnapshotHash
                && row.EffectBindingHash == envelope.EffectBindingHash, token);
        if (clock.GetUtcNow() >= until || accessUntil is { } afterSource && clock.GetUtcNow() >= afterSource) return null;
        var notices = await context.ModerationNotices.AsNoTracking().Where(row => row.EffectId == original.Id)
            .OrderBy(row => row.Id).ToArrayAsync(token);
        if (clock.GetUtcNow() >= until || accessUntil is { } afterNotices && clock.GetUtcNow() >= afterNotices) return null;
        if (source is null || source.TenantId is { } sourceTenant && sourceTenant != tenant || !source.Matches(original)
            || source.RetainUntil != until
            || !noticeIds.SequenceEqual(notices.Select(row => row.Id))
            || original.DecisionRevision != decision.Revision || original.CreatedAt > revoke.CreatedAt
            || original.SanctionRevision >= revoke.SanctionRevision)
            return null;
        ModerationNoticeCaptureSnapshot? captured;
        try { captured = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(source.SnapshotJson); }
        catch (JsonException) { return null; }
        if (captured is null || !captured.Matches(original) || captured.PendingCorrection is not null
            || captured.Notice is not { } notice || captured.WriterTenantId != tenant || notice.RetainUntil != until
            || captured.Decision.CaseId != decision.CaseId || captured.Decision.SubjectOrigin != ModerationNoticeScope.From(decision.Subject.Origin)
            || captured.Decision.Scope != ModerationNoticeScope.From(decision.Scope)
            || captured.Decision.Rule != new ModerationNoticeRule(decision.Rule.OwnerId, decision.Rule.Code, decision.Rule.Revision)
            || captured.Decision.CapabilityMask != decision.CapabilityMask || captured.Decision.StartsAt != decision.StartsAt
            || captured.Decision.EndsAt != decision.EndsAt || captured.Decision.ReviewAt != decision.ReviewAt
            || captured.Decision.FindingId != decision.FindingId)
            return null;
        var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
        if (host is null || host.Id != captured.HostBindingId || host.TrustDomainId != original.TrustDomainId
            || host.SolutionId != captured.HostSolutionId || host.InstanceId != captured.HostInstanceId)
            return null;

        var expected = new HashSet<(ModerationNoticeAudience, int, Guid)>
        {
            (ModerationNoticeAudience.Subject, original.SubjectKind, original.SubjectId)
        };
        foreach (var reviewer in captured.Reviewers)
            if (!expected.Add((ModerationNoticeAudience.Reviewer, (int)ModerationSubjectKind.Human, reviewer.ReviewerHumanId)))
                return null;
        foreach (var representative in captured.ContentRepresentatives)
            if (!expected.Add((ModerationNoticeAudience.ContentRepresentative, (int)ModerationSubjectKind.Human, representative.HumanId)))
                return null;
        if (notices.Length != expected.Count || notices.Any(row => row.TenantId != source.TenantId
            || row.TrustDomainId != original.TrustDomainId || row.CreatedAt != original.CreatedAt
            || !expected.Remove((row.Audience, row.RecipientSubjectKind, row.RecipientSubjectId))) || expected.Count != 0)
            return null;
        if (clock.GetUtcNow() >= until || clock.GetUtcNow() < revoke.CreatedAt
            || accessUntil is { } finalBound && clock.GetUtcNow() >= finalBound) return null;
        return new(original.Id, source.SnapshotHash, source.EffectBindingHash, until,
            notices.Select(row => new ModerationNoticeCorrectionRecipient(row.Id, row.Audience,
                row.RecipientSubjectKind, row.RecipientSubjectId, ModerationNoticeDeliveryDisposition.RecipientUnavailable))
                .ToImmutableArray());

        IQueryable<ModerationEffectIntent> Creators() => context.ModerationEffectIntents.AsNoTracking()
            .Where(row => row.SanctionId == sanctionId && row.DecisionId == decision.Id
                && row.TrustDomainId == revoke.TrustDomainId && row.ScopeKind == revoke.ScopeKind && row.ScopeId == revoke.ScopeId
                && row.SubjectKind == revoke.SubjectKind && row.SubjectId == revoke.SubjectId
                && (row.OperationKind == (int)ModerationOperationKind.Activate || row.OperationKind == (int)ModerationOperationKind.Supersede));
    }

    internal static bool Same(ModerationNoticeCorrectionObligations? left, ModerationNoticeCorrectionObligations? right) =>
        left is not null && right is not null && JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);

    internal static async Task<bool> PendingOccurrencesMatchAsync(ExploreDbContext context,
        ModerationNoticeSource source, ModerationNoticeCorrectionObligations pending, TimeProvider clock, CancellationToken token)
    {
        if (source.TenantId != context.TenantFilterTenantId || source.RetainUntil != pending.RetainUntil
            || clock.GetUtcNow() >= pending.RetainUntil) return false;
        var persisted = await context.ModerationNotices.AsNoTracking()
            .Where(row => row.EffectId == source.EffectId).ToArrayAsync(token);
        var notices = persisted.Concat(context.ChangeTracker.Entries<ModerationNotice>()
                .Where(entry => entry.State == EntityState.Added && entry.Entity.EffectId == source.EffectId)
                .Select(entry => entry.Entity))
            .DistinctBy(row => row.Id).ToArray();
        var expected = pending.Recipients.Select(row => (row.Audience, row.RecipientSubjectKind, row.RecipientSubjectId)).ToHashSet();
        if (notices.Length != expected.Count || notices.Any(row => row.NativeIntentId is not null
            || row.DeliveryDisposition != ModerationNoticeDeliveryDisposition.RecipientUnavailable
            || row.TrustDomainId != source.TrustDomainId || row.TenantId != source.TenantId || row.CreatedAt != source.CapturedAt
            || !expected.Remove((row.Audience, row.RecipientSubjectKind, row.RecipientSubjectId))) || expected.Count != 0
            || await context.Set<ModerationNoticeNotificationReceipt>().AnyAsync(row => row.EffectId == source.EffectId, token))
            return false;
        return clock.GetUtcNow() < pending.RetainUntil;
    }
}

using System.Text.Json;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationAdjudicatedCaseReadGrantRepository
{
    private async Task<DateTimeOffset?> ReadOriginalAsync(Guid caseId, long caseRevision, Guid originalId,
        string snapshotHash, string bindingHash, ModerationSourceAuthority authority, CancellationToken token,
        DateTimeOffset? accessUntil = null)
    {
        var allegation = await context.ModerationCases.AsNoTracking().SingleOrDefaultAsync(row => row.Id == caseId, token);
        if (allegation?.Subject is null || allegation.Revision != caseRevision
            || allegation.ScopeTrustDomainId != authority.Binding.TrustDomainId) return null;
        var original = await context.ModerationEffectIntents.AsNoTracking().SingleOrDefaultAsync(row => row.Id == originalId, token);
        if (original is null || original.SanctionId is null
            || original.OperationKind is not ((int)ModerationOperationKind.Activate) and not ((int)ModerationOperationKind.Supersede)
            || original.TrustDomainId != authority.Binding.TrustDomainId) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationDecision>(context, original.DecisionId, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationEffectIntent>(context, originalId, token);
        await using var sourceFence = await RelationalNamedLock.AcquireTransactionAsync(
            context, $"moderation.notice:{originalId:D}", token);
        original = await context.ModerationEffectIntents.AsNoTracking().SingleOrDefaultAsync(row => row.Id == originalId, token);
        var decision = original is null ? null : await context.ModerationDecisions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == original.DecisionId, token);
        if (original is null || decision is null || original.SanctionId is null
            || original.OperationKind is not ((int)ModerationOperationKind.Activate) and not ((int)ModerationOperationKind.Supersede)
            || decision.CaseId != caseId || decision.Subject != allegation.Subject || !OwnsDecisionScope(allegation, decision)
            || decision.Revision != original.DecisionRevision || original.TrustDomainId != authority.Binding.TrustDomainId)
            return null;
        var creators = await context.ModerationEffectIntents.AsNoTracking().Where(row => row.SanctionId == original.SanctionId
            && row.DecisionId == decision.Id && row.TrustDomainId == original.TrustDomainId
            && (row.OperationKind == (int)ModerationOperationKind.Activate || row.OperationKind == (int)ModerationOperationKind.Supersede))
            .Select(row => row.Id).Take(2).ToArrayAsync(token);
        if (creators.Length != 1 || creators[0] != originalId) return null;

        // Authenticate the finite envelope before fetching or processing its retained private snapshot.
        var envelope = await context.ModerationNoticeSources.AsNoTracking().Where(row => row.EffectId == originalId)
            .Select(row => new { row.TenantId, row.RetainUntil, row.SnapshotHash, row.EffectBindingHash }).SingleOrDefaultAsync(token);
        var now = clock.GetUtcNow();
        if (envelope is null || envelope.TenantId is { } projectedTenant && projectedTenant != authority.Tenant.Id
            || envelope.SnapshotHash != snapshotHash || envelope.EffectBindingHash != bindingHash
            || envelope.RetainUntil is not { } ceiling || ceiling == DateTimeOffset.MaxValue || now >= ceiling
            || now < original.CreatedAt || accessUntil is { } access && now >= access) return null;
        var source = await context.ModerationNoticeSources.AsNoTracking().SingleOrDefaultAsync(row => row.EffectId == originalId
            && row.RetainUntil == ceiling && row.SnapshotHash == snapshotHash && row.EffectBindingHash == bindingHash, token);
        if (clock.GetUtcNow() >= ceiling || accessUntil is { } afterRead && clock.GetUtcNow() >= afterRead
            || source is null || !source.Matches(original)) return null;
        ModerationNoticeCaptureSnapshot? capture;
        try { capture = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(source.SnapshotJson); }
        catch (JsonException) { return null; }
        if (capture is null || !capture.Matches(original) || capture.PendingCorrection is not null
            || capture.Notice is not { } notice || notice.RetainUntil != ceiling
            || capture.WriterTenantId != authority.Tenant.Id || capture.HostBindingId != authority.Binding.Id
            || capture.HostSolutionId != authority.Binding.SolutionId || capture.HostInstanceId != authority.Binding.InstanceId
            || capture.Decision.CaseId != caseId || capture.Decision.SubjectOrigin != ModerationNoticeScope.From(decision.Subject.Origin)
            || capture.Decision.Scope != ModerationNoticeScope.From(decision.Scope)
            || capture.Decision.Rule != new ModerationNoticeRule(decision.Rule.OwnerId, decision.Rule.Code, decision.Rule.Revision)
            || capture.Decision.FindingId != decision.FindingId || capture.Decision.CapabilityMask != decision.CapabilityMask
            || capture.Decision.StartsAt != decision.StartsAt || capture.Decision.EndsAt != decision.EndsAt
            || capture.Decision.ReviewAt != decision.ReviewAt) return null;
        var currentCase = await context.ModerationCases.AsNoTracking().SingleOrDefaultAsync(row => row.Id == caseId, token);
        return clock.GetUtcNow() < ceiling && clock.GetUtcNow() >= original.CreatedAt
            && (accessUntil is null || clock.GetUtcNow() < accessUntil.Value)
            && currentCase?.Revision == caseRevision && currentCase.Subject == decision.Subject
            && OwnsDecisionScope(currentCase, decision) ? ceiling : null;
    }

    private static bool OwnsDecisionScope(ModerationCase owner, ModerationDecision decision) =>
        owner.Scope == decision.Scope
        || owner.AttributionSourceId is not null && decision.Scope.Kind == ModerationScopeKind.Resource
            && decision.Scope.Parent == owner.Scope;
}

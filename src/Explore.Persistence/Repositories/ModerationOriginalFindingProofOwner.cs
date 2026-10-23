using Explore.Application.Contracts.Notifications;
using Explore.Domain;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

/// <summary>Reads retained native finding ownership, not fresh adjudication or disclosure permission.</summary>
internal static class ModerationOriginalFindingProofOwner
{
    internal static async Task<ModerationFinding?> AcquireAsync(
        ExploreDbContext context, ModerationCase owner, ModerationNoticeOriginalFinding? captured,
        AuthorityScope actionScope, ModerationHostBinding host, DateTimeOffset capturedAt,
        CancellationToken token)
    {
        if (captured is null || actionScope.Kind != ModerationScopeKind.Resource
            || actionScope.Parent is not { Kind: ModerationScopeKind.Tenant } tenant)
            return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationFinding>(context, captured.Id, token);
        var finding = await context.ModerationFindings.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == captured.Id, token);
        if (finding is null) return null;
        finding.ValidatePersistedState();
        if (!finding.MatchesRetainedCase(owner) || finding.Scope != actionScope
            || finding.Revision != captured.Revision || finding.State != captured.State
            || finding.Reason.OwnerId != captured.Reason.OwnerId || finding.Reason.Code != captured.Reason.Code
            || finding.Reason.Revision != captured.Reason.Revision || finding.FinalizedAt > capturedAt
            || owner.Scope != actionScope && (owner.AttributionSourceId is null
                || owner.Scope.Kind != ModerationScopeKind.Tenant
                || actionScope.Kind != ModerationScopeKind.Resource || actionScope.Parent != owner.Scope))
            return null;
        if (finding.AttributionSourceId is not { } sourceId) return finding;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationCaseAttributionSource>(context, sourceId, token);
        var source = await context.ModerationCaseAttributionSources.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == sourceId, token);
        if (source is null) return null;
        source.ValidatePersistedState();
        if (source.CaseId != owner.Id || source.Generation != finding.AttributionGeneration
            || source.OccurrenceCaseId != owner.OccurrenceCaseId
            || source.EvidenceCaseRevision < owner.OccurrenceEvidenceRevision
            || source.SubjectKind != finding.SubjectKind || source.SubjectId != finding.SubjectId
            || source.HostBindingId != host.Id || source.TenantId != tenant.Id
            || source.TrustDomainId != host.TrustDomainId || source.QualifiedAt < owner.CreatedAt
            || finding.FinalizedAt < source.QualifiedAt || finding.FinalizedAt >= source.ValidUntil
            || source.WithdrawnAt is { } withdrawnAt && finding.FinalizedAt >= withdrawnAt)
            return null;
        return finding;
    }
}

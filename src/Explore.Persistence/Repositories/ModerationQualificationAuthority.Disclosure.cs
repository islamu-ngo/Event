using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database.ProviderPrimitives;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

internal sealed partial class ModerationQualificationAuthority
{
    private Task<EventAuthorityProviderOperations.AuthorityPlan?> DiscoverDisclosureAuthorityAsync(
        ModerationHostBinding host, Guid tenantId, ApproveModerationNoticeDisclosureSource source,
        CancellationToken token) => EventAuthorityProviderOperations.ReadWithoutRetainingLocksAsync(
        context, async native =>
        {
            var representatives = source.Intent.ContentRepresentatives;
            if (representatives.IsDefaultOrEmpty || representatives.Length > 32
                || representatives.Any(row => row is null)) return null;
            var decision = await native.ModerationDecisions.AsNoTracking().Where(row =>
                    row.Id == source.Intent.DecisionId && row.SubjectKind == ModerationSubjectKind.Content
                    && row.SubjectOriginKind == ModerationScopeKind.Tenant
                    && row.SubjectOriginTrustDomainId == host.TrustDomainId
                    && row.SubjectOriginSolutionId == host.SolutionId
                    && row.SubjectOriginInstanceId == host.InstanceId && row.SubjectOriginTenantId == tenantId)
                .Select(row => new { row.SubjectId }).SingleOrDefaultAsync(token);
            if (decision is null) return null;
            var parents = await EventAuthorityProviderOperations.ReadOwnershipAsync(
                native, tenantId, [decision.SubjectId], token);
            var assignments = await EventAuthorityProviderOperations.ReadAssignmentsAsync(native, tenantId,
                representatives.Select(row => row.HumanId).Distinct().ToArray(), [decision.SubjectId], token);
            return new EventAuthorityProviderOperations.AuthorityPlan(parents, assignments);
        }, token);

    private Task<EventAuthorityProviderOperations.AuthorityPlan?> DiscoverContentResponseAuthorityAsync(
        ModerationHostBinding host, Guid tenantId, Guid noticeId, Guid recipientId,
        CancellationToken token) => EventAuthorityProviderOperations.ReadWithoutRetainingLocksAsync(
        context, async native =>
        {
            var original = await (from notice in native.ModerationNotices.AsNoTracking()
                                  join source in native.ModerationNoticeSources.AsNoTracking()
                                      on notice.EffectId equals source.EffectId
                                  where notice.Id == noticeId && notice.TenantId == tenantId
                                      && notice.TrustDomainId == host.TrustDomainId
                                      && notice.Audience == ModerationNoticeAudience.ContentRepresentative
                                      && notice.RecipientSubjectKind == 1 && notice.RecipientSubjectId == recipientId
                                      && source.TenantId == tenantId && source.TrustDomainId == host.TrustDomainId
                                      && source.SubjectKind == (int)ModerationSubjectKind.Content
                                  select new
                                  {
                                      source.SubjectId,
                                      source.StatusMetadataJson,
                                      source.StatusMetadataHash,
                                      source.SnapshotHash,
                                      source.EffectBindingHash
                                  }).SingleOrDefaultAsync(token);
            if (original is null) return null;
            var metadata = ModerationNoticeSource.StatusMetadata.Read(
                original.StatusMetadataJson, original.StatusMetadataHash);
            if (metadata is null || metadata.HostBindingId != host.Id || metadata.WriterTenantId != tenantId
                || metadata.SnapshotHash != original.SnapshotHash
                || metadata.EffectBindingHash != original.EffectBindingHash
                || !metadata.ContentRepresentatives.Any(row => row.HumanId == recipientId)) return null;
            // Original metadata nominates sources only; the caller still qualifies the full notice and custody.
            var parents = await EventAuthorityProviderOperations.ReadOwnershipAsync(
                native, tenantId, [original.SubjectId], token);
            var assignments = await EventAuthorityProviderOperations.ReadAssignmentsAsync(
                native, tenantId, [recipientId], [original.SubjectId], token);
            return new EventAuthorityProviderOperations.AuthorityPlan(parents, assignments);
        }, token);
}

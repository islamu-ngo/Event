using System.Collections.Immutable;
using Explore.Domain;
using Explore.Persistence.Database.ProviderPrimitives;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

internal sealed partial class ModerationQualificationAuthority
{
    internal const int MaximumPreFindingNotices = 64;

    /// <summary>Discovery is metadata only and completes before the shared helper takes any human or epoch fence.</summary>
    internal async Task<ModerationSourceAuthority?> AcquirePreFindingAsync(
        Guid caseId, IReadOnlyCollection<Guid> requestedHumans, CancellationToken token,
        ModerationNoticeContentRepresentative? requestedRecipient = null,
        PreFindingHumanDiscovery? discovered = null)
    {
        discovered ??= await DiscoverPreFindingHumansAsync(context, caseId, requestedHumans, token, requestedRecipient);
        return discovered is null ? null : await AcquireAsync(discovered.Tenant, true, false, null, null, token,
            responseReview: (caseId, discovered.Humans), preFindingDiscovery: discovered);
    }

    internal static Task<PreFindingHumanDiscovery?> DiscoverPreFindingHumansAsync(
        ExploreDbContext context, Guid caseId, IReadOnlyCollection<Guid> requestedHumans, CancellationToken token,
        ModerationNoticeContentRepresentative? requestedRecipient = null) =>
        EventAuthorityProviderOperations.ReadWithoutRetainingLocksAsync(context,
            source => DiscoverPreFindingCoreAsync(source, caseId, requestedHumans, requestedRecipient, token), token);

    private static async Task<PreFindingHumanDiscovery?> DiscoverPreFindingCoreAsync(
        ExploreDbContext context, Guid caseId, IReadOnlyCollection<Guid> requestedHumans,
        ModerationNoticeContentRepresentative? requestedRecipient, CancellationToken token)
    {
        var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
        if (host is null || context.TenantFilterTenantId is not { } tenantId) return null;
        context.ModerationTrustDomainId = host.TrustDomainId;
        var tenant = AuthorityScope.Tenant(AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(host.TrustDomainId), host.SolutionId), host.InstanceId), tenantId);
        var owner = await context.ModerationCases.AsNoTracking().SingleOrDefaultAsync(row => row.Id == caseId, token);
        if (owner is null || owner.Scope != tenant || owner.Subject is null || owner.AttributionSourceId is null)
            return null;
        var humans = requestedHumans.ToHashSet();
        if (owner.SubjectKind == ModerationSubjectKind.Human) humans.Add(owner.SubjectId!.Value);
        var reviewers = await context.ModerationReviewerAssignments.AsNoTracking()
            .Where(row => row.CaseId == caseId && row.WithdrawnAt == null)
            .Select(row => new { row.ReviewerHumanId, row.ControllerHumanId, row.QualifiedByHumanId, row.AssessedByHumanId })
            .ToArrayAsync(token);
        foreach (var reviewer in reviewers)
        {
            humans.Add(reviewer.ReviewerHumanId);
            if (reviewer.ControllerHumanId is { } controller) humans.Add(controller);
            if (reviewer.QualifiedByHumanId is { } qualifier) humans.Add(qualifier);
            if (reviewer.AssessedByHumanId is { } assessor) humans.Add(assessor);
        }
        var custody = await context.Set<ModerationPreFindingCustody>().AsNoTracking()
            .Where(row => row.CaseId == caseId)
            .Select(row => new { row.ProposedByHumanId, row.PrivacyOwnerHumanId }).ToArrayAsync(token);
        foreach (var row in custody)
        {
            humans.Add(row.ProposedByHumanId);
            humans.Add(row.PrivacyOwnerHumanId);
        }
        var policyHumans = await (
            from source in context.ModerationQualificationPolicySources.AsNoTracking()
            join adoption in context.ModerationQualificationPolicyAdoptions.AsNoTracking()
                on source.Id equals adoption.SourceId
            where source.HostBindingId == host.Id && source.CaseId == caseId
            select new { source.PublishedByHumanId, adoption.AdoptedByHumanId }).ToArrayAsync(token);
        foreach (var row in policyHumans)
        {
            humans.Add(row.PublishedByHumanId);
            humans.Add(row.AdoptedByHumanId);
        }
        var recipients = await context.Set<ModerationPreFindingNotice>().AsNoTracking()
            .Where(row => row.CaseId == caseId && row.HostBindingId == host.Id && row.TenantId == tenantId)
            .Select(row => new ModerationNoticeContentRepresentative(row.RecipientAssignmentId,
                row.RecipientAssignmentRevision, row.RecipientHumanId)).Take(MaximumPreFindingNotices + 1).ToListAsync(token);
        if (recipients.Count > MaximumPreFindingNotices) return null;
        if (requestedRecipient is not null) recipients.Add(requestedRecipient);
        var originalRecipients = recipients.Distinct().ToImmutableArray();
        humans.UnionWith(originalRecipients.Select(row => row.HumanId));
        var attribution = await (
            from head in context.ModerationCaseAttributionHeads.AsNoTracking()
            join source in context.ModerationCaseAttributionSources.AsNoTracking() on head.SourceId equals source.Id
            where head.CaseId == owner.Id && source.CaseId == owner.Id && source.Generation == head.Generation
            select new
            {
                source.QualifiedByHumanId,
                source.ReadGrantId,
                source.InvestigationSourceId,
                source.HostBindingId,
                source.TenantId
            }).SingleOrDefaultAsync(token);
        if (attribution is null) return null;
        humans.Add(attribution.QualifiedByHumanId);
        if (attribution.InvestigationSourceId is { } investigationId)
            humans.UnionWith(await ModerationParticipantInvestigationHumanDiscovery.DiscoverAsync(
                context, investigationId, attribution.HostBindingId, attribution.TenantId, token));
        var grant = await context.ModerationCaseReadGrants.AsNoTracking()
            .Where(row => row.Id == attribution.ReadGrantId)
            .Select(row => new { row.CallerUserId, row.IssuerUserId }).SingleOrDefaultAsync(token);
        if (grant is null) return null;
        humans.Add(grant.CallerUserId);
        humans.Add(grant.IssuerUserId);
        EventAuthorityProviderOperations.AuthorityPlan? representativeAuthority = null;
        if (owner.Subject.Kind == ModerationSubjectKind.Content && originalRecipients.Length > 0)
            representativeAuthority = await EventAuthorityProviderOperations.ReadPlanAsync(context, tenantId,
                originalRecipients.Select(row => row.HumanId).Distinct().ToArray(), [owner.Subject.Id], token);
        return new(tenant, Array.AsReadOnly(humans.Order().ToArray()), owner.Id, owner.Revision,
            owner.Subject, owner.AttributionSourceId.Value, originalRecipients, representativeAuthority);
    }

    internal sealed record PreFindingHumanDiscovery(
        AuthorityScope Tenant, IReadOnlyList<Guid> Humans, Guid CaseId, long CaseRevision,
        SubjectReference Subject, Guid AttributionSourceId,
        ImmutableArray<ModerationNoticeContentRepresentative> Recipients,
        EventAuthorityProviderOperations.AuthorityPlan? RepresentativeAuthority)
    {
        internal bool Matches(ModerationCase owner, ModerationNoticeContentRepresentative recipient) =>
            owner.Id == CaseId && owner.Revision == CaseRevision && owner.Subject == Subject
            && owner.Scope == Tenant && owner.AttributionSourceId == AttributionSourceId
            && Recipients.Contains(recipient);
    }
}

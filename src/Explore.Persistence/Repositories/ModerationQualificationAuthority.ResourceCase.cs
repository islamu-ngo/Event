using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

internal sealed partial class ModerationQualificationAuthority
{
    internal static bool IsRetainedCaseCorrection(ModerationQualificationMutation? mutation) =>
        mutation is RevokeModerationNoticeDisclosureSource or RevokeModerationProfileSource
            or RevokeModerationEventTargetSource or RevokeModerationResourceTargetSource
            or RevokeModerationQualificationPolicyAdoption or WithdrawModerationReviewerSource
            or ReverseModerationFindingSource or ApproveModerationNoticeCorrectionDisclosureSource;

    private async Task<bool> QualifyRetainedTargetCorrectionAsync(
        ModerationQualificationMutation mutation, ModerationSourceAuthority authority, CancellationToken token)
    {
        if (mutation.Target.Parent != authority.Tenant) return false;
        var caseId = mutation switch
        {
            RevokeModerationNoticeDisclosureSource source => (Guid?)source.CaseId,
            ApproveModerationNoticeCorrectionDisclosureSource source => source.Intent.CaseId,
            RevokeModerationProfileSource source => source.CaseId,
            RevokeModerationEventTargetSource source => source.CaseId,
            RevokeModerationResourceTargetSource source => source.CaseId,
            RevokeModerationQualificationPolicyAdoption source => source.CaseId,
            WithdrawModerationReviewerSource source => await context.ModerationReviewerAssignments.AsNoTracking()
                .Where(row => row.Id == source.AssignmentId).Select(row => (Guid?)row.CaseId).SingleOrDefaultAsync(token),
            ReverseModerationFindingSource source => await context.ModerationFindings.AsNoTracking()
                .Where(row => row.Id == source.FindingId).Select(row => (Guid?)row.CaseId).SingleOrDefaultAsync(token),
            _ => null
        };
        if (caseId is not { } ownerId) return false;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationCase>(context, ownerId, token);
        var owner = await context.ModerationCases.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == ownerId, token);
        if (owner is null) return false;
        owner.ValidatePersistedState();
        if (owner.Subject is null || owner.Scope != mutation.Target
            && (mutation is not (ReverseModerationFindingSource or ApproveModerationNoticeCorrectionDisclosureSource)
                 || owner.AttributionSourceId is null || owner.Scope != authority.Tenant))
            return false;
        if (mutation is ReverseModerationFindingSource reversal)
        {
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationFinding>(context, reversal.FindingId, token);
            var finding = await context.ModerationFindings.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == reversal.FindingId, token);
            if (finding is null) return false;
            finding.ValidatePersistedState();
            return finding.Scope == mutation.Target && finding.MatchesRetainedCase(owner);
        }
        if (mutation is ApproveModerationNoticeCorrectionDisclosureSource correction)
        {
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationDecision>(context, correction.Intent.DecisionId, token);
            var decision = await context.ModerationDecisions.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == correction.Intent.DecisionId, token);
            if (decision is null) return false;
            decision.ValidatePersistedState();
            return decision.CaseId == owner.Id && decision.Subject == owner.Subject && decision.Scope == mutation.Target
                && (owner.Scope == decision.Scope || owner.AttributionSourceId is not null
                    && decision.Scope.Kind == ModerationScopeKind.Resource && decision.Scope.Parent == owner.Scope);
        }
        return true;
    }

    private static bool IsContentResourceCaseMutation(ModerationQualificationMutation mutation) =>
        mutation is OpenModerationCaseSource { SubjectKind: ModerationSubjectKind.Content }
            or PublishModerationQualificationPolicySource or AdoptModerationQualificationPolicySource
            or RevokeModerationQualificationPolicyAdoption or AssignModerationReviewerSource
            or QualifyModerationReviewerController or AssessModerationReviewerConflict
            or WithdrawModerationReviewerSource or FinalizeModerationFindingSource
            || mutation.Target.Kind == ModerationScopeKind.Resource
                && mutation is (QualifyModerationCaseUrgency or RecordModerationContributionSelectionSource
                    or AdoptModerationResourceTargetSource or ApproveModerationNoticeDisclosureSource);

    private Task<ContentTargetCasePlan?> DiscoverContentTargetCasePlanAsync(
        ModerationQualificationMutation mutation, AuthorityScope tenant, CancellationToken token) =>
        EventAuthorityProviderOperations.ReadWithoutRetainingLocksAsync(context, async source =>
        {
            var caseId = await ContentTargetCaseIdAsync(source, mutation, token);
            SubjectReference? subject;
            if (mutation is OpenModerationCaseSource opening)
                subject = SubjectReference.Create(opening.SubjectKind, opening.SubjectId, tenant);
            else
                subject = (await source.ModerationCases.AsNoTracking()
                    .SingleOrDefaultAsync(row => row.Id == caseId, token))?.Subject;
            if (caseId is null || subject is not { Kind: ModerationSubjectKind.Content } || subject.Origin != tenant)
                return null;
            if (mutation.Target.Kind == ModerationScopeKind.Resource)
            {
                if (!await source.EventResources.AsNoTracking().AnyAsync(row => row.Id == mutation.Target.Id
                    && row.TenantId == tenant.Id && row.EventId == subject.Id && !row.IsDeleted, token)) return null;
            }
            else if (mutation.Target.Kind != ModerationScopeKind.Event || mutation.Target.Id != subject.Id)
                return null;
            var parents = await EventAuthorityProviderOperations.ReadOwnershipAsync(source, tenant.Id, [subject.Id], token);
            return parents.SingleOrDefault() is { } parent
                ? new ContentTargetCasePlan(caseId.Value, mutation.Target, subject, parent) : null;
        }, token);

    private static async Task<Guid?> ContentTargetCaseIdAsync(
        ExploreDbContext source, ModerationQualificationMutation mutation, CancellationToken token) => mutation switch
        {
            OpenModerationCaseSource item => item.CaseId,
            PublishModerationQualificationPolicySource item => item.CaseId,
            AdoptModerationQualificationPolicySource item => item.CaseId,
            RevokeModerationQualificationPolicyAdoption item => item.CaseId,
            AssignModerationReviewerSource item => item.CaseId,
            FinalizeModerationFindingSource item => item.CaseId,
            QualifyModerationCaseUrgency item => item.CaseId,
            RecordModerationContributionSelectionSource item => item.CaseId,
            AdoptModerationResourceTargetSource item => item.CaseId,
            ApproveModerationNoticeDisclosureSource item => item.Intent.CaseId,
            QualifyModerationReviewerController item => await ReviewerCaseAsync(source, item.AssignmentId, token),
            AssessModerationReviewerConflict item => await ReviewerCaseAsync(source, item.AssignmentId, token),
            WithdrawModerationReviewerSource item => await ReviewerCaseAsync(source, item.AssignmentId, token),
            _ => null
        };

    private static Task<Guid?> ReviewerCaseAsync(ExploreDbContext source, Guid assignmentId, CancellationToken token) =>
        source.ModerationReviewerAssignments.AsNoTracking().Where(row => row.Id == assignmentId)
            .Select(row => (Guid?)row.CaseId).SingleOrDefaultAsync(token);

    internal sealed record ContentTargetCasePlan(
        Guid CaseId, AuthorityScope Target, SubjectReference Subject, EventAuthorityProviderOperations.Ownership Parents)
    {
        public override string ToString() => nameof(ContentTargetCasePlan);
    }

    /// <summary>
    /// Administrative acquisition uses the actual parent tenant; immutable mutation semantics
    /// retain the typed Event or Resource target. Only this closed Content-case prerequisite family uses it.
    /// Original Actors are acquired before Tenant; exact case and descendant ancestry is checked afterwards.
    /// </summary>
    private async Task<bool> QualifyContentTargetCaseAsync(
        ModerationQualificationMutation mutation, ModerationSourceAuthority authority, CancellationToken token)
    {
        if (mutation.Target.Kind is not (ModerationScopeKind.Resource or ModerationScopeKind.Event)
            || mutation.Target.Parent != authority.Tenant)
            return false;
        var eventTarget = mutation.Target.Kind == ModerationScopeKind.Event;
        var nativeKind = eventTarget ? ModerationCaseTargetKind.EventVersion : ModerationCaseTargetKind.Resource;
        var caseId = await ContentTargetCaseIdAsync(context, mutation, token);
        SubjectReference? subject;
        if (mutation is OpenModerationCaseSource opening)
        {
            subject = SubjectReference.Create(opening.SubjectKind, opening.SubjectId, authority.Tenant);
        }
        else
        {
            if (caseId is not { } existingCaseId) return false;
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationCase>(context, existingCaseId, token);
            var existing = await context.ModerationCases.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == existingCaseId, token);
            if (existing is null || existing.Scope != mutation.Target
                && (mutation is not FinalizeModerationFindingSource
                    || existing.AttributionSourceId is null || existing.Scope != authority.Tenant))
                return false;
            existing.ValidatePersistedState();
            if (existing.AttributionSourceId is not null
                && !await (
                    from head in context.ModerationCaseAttributionHeads.AsNoTracking()
                    join attribution in context.ModerationCaseAttributionSources.AsNoTracking()
                        on head.SourceId equals attribution.Id
                    join material in context.ModerationCaseTargetSources.AsNoTracking()
                        on attribution.EvidenceSourceId equals material.Id
                    where head.CaseId == existingCaseId && attribution.CaseId == existingCaseId
                        && attribution.Generation == head.Generation
                        && material.NativeKind == nativeKind
                        && material.NativeTargetId == mutation.Target.Id
                    select attribution.Id).AnyAsync(token))
                return false;
            subject = existing.Subject;
        }
        if (subject is not { Kind: ModerationSubjectKind.Content }
            || subject.Origin != authority.Tenant)
            return false;
        if (authority.ContentTargetCase is not { } plan || plan.CaseId != caseId
            || plan.Target != mutation.Target || plan.Subject != subject || plan.Parents.Id != subject.Id)
            return false;

        if (eventTarget)
        {
            if (mutation.Target.Id != subject.Id) return false;
        }
        else
        {
            var resource = await context.EventResources.AsNoTracking()
                .Where(row => row.Id == mutation.Target.Id && row.TenantId == authority.Tenant.Id && !row.IsDeleted)
                .Select(row => new { row.EventId }).SingleOrDefaultAsync(token);
            if (resource is null || resource.EventId != subject.Id) return false;
        }
        var owningEvent = await context.Events.AsNoTracking()
            .Where(row => row.Id == subject.Id && row.TenantId == authority.Tenant.Id && !row.IsDeleted)
            .Select(row => new { row.ActorId, row.OrganizerActorId }).SingleOrDefaultAsync(token);
        if (owningEvent is null) return false;
        if (owningEvent.ActorId != plan.Parents.ActorId || owningEvent.OrganizerActorId != plan.Parents.OrganizerActorId)
            return false;
        var actors = new[] { (Guid?)owningEvent.ActorId, owningEvent.OrganizerActorId }
            .OfType<Guid>().Distinct().Order().ToArray();
        foreach (var actor in actors)
            await RelationalEntityRowFence.AcquireGlobalAsync<Actor>(context, actor, token);
        if (await context.Actors.AsNoTracking().CountAsync(row => actors.Contains(row.Id) && !row.IsDeleted, token)
            != actors.Length)
            return false;
        await RelationalEntityRowFence.AcquireGlobalAsync<Event>(context, subject.Id, token);
        if (!await context.Events.AsNoTracking().AnyAsync(row => row.Id == subject.Id
            && row.TenantId == authority.Tenant.Id && !row.IsDeleted
            && row.ActorId == owningEvent.ActorId && row.OrganizerActorId == owningEvent.OrganizerActorId, token))
            return false;
        if (eventTarget) return true;
        await RelationalEntityRowFence.AcquireGlobalAsync<EventResource>(context, mutation.Target.Id, token);
        return await context.EventResources.AsNoTracking().AnyAsync(row => row.Id == mutation.Target.Id
            && row.EventId == subject.Id && row.TenantId == authority.Tenant.Id && !row.IsDeleted, token);
    }
}

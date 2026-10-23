using System.Data;
using System.Text.Json;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

/// <summary>
/// A transaction-bound authority for one retained original CFG09 recipe. Only this owner can
/// mint it, after the caller has acquired retained privacy and sorted primary human fences.
/// It cannot select a policy, activate an effect or extend a measurement's signed lifetime.
/// </summary>
internal sealed class ModerationOriginalRecipeAuthority
{
    private readonly ExploreDbContext context;
    private readonly ModerationSourceAuthority authority;
    private readonly IPrivacyIdentityFenceAuthority privacy;
    private readonly TimeProvider clock;
    private readonly Guid transactionId;
    private readonly Guid publisher;
    private readonly Guid? preparer;
    private readonly ModerationNoticeCaptureSnapshot capture;
    private readonly DateTimeOffset capturedAt;

    private ModerationOriginalRecipeAuthority(ExploreDbContext context, ModerationSourceAuthority authority,
        IPrivacyIdentityFenceAuthority privacy, TimeProvider clock, Guid transactionId,
        ModerationOriginalEffectAnchor anchor, ModerationPolicyCompiledDraft policy, Guid definitionId,
        ModerationActivationBinding binding, Guid publisher, Guid? preparer, DateTimeOffset validUntil,
        ModerationNoticeCaptureSnapshot capture, DateTimeOffset capturedAt)
    {
        this.context = context;
        this.authority = authority;
        this.privacy = privacy;
        this.clock = clock;
        this.transactionId = transactionId;
        this.publisher = publisher;
        this.preparer = preparer;
        this.capture = capture;
        this.capturedAt = capturedAt;
        Anchor = anchor;
        Policy = policy;
        DefinitionId = definitionId;
        Binding = binding;
        ValidUntil = validUntil;
    }

    internal ModerationOriginalEffectAnchor Anchor { get; }
    internal ModerationPolicyCompiledDraft Policy { get; }
    internal Guid DefinitionId { get; }
    internal ModerationActivationBinding Binding { get; }
    internal DateTimeOffset ValidUntil { get; }

    internal static async Task<ModerationOriginalRecipeAuthority?> AcquireAsync(
        ExploreDbContext context, ModerationSourceAuthority authority, Guid transactionId,
        ModerationOriginalEffectAnchor anchor, IPrivacyIdentityFenceAuthority privacy,
        TimeProvider clock, CancellationToken token)
    {
        if (anchor.EffectId == Guid.Empty || string.IsNullOrEmpty(anchor.SnapshotHash)
            || string.IsNullOrEmpty(anchor.EffectBindingHash)
            || !TransactionMatches(context, authority, transactionId))
            return null;
        // Retained erasure can precede the local saga. Never read the private snapshot first.
        foreach (var human in authority.FencedHumans)
            if (await privacy.IsSubjectFencedAsync(human, token)
                || !await context.Users.AsNoTracking().AnyAsync(row => row.Id == human && !row.IsDeleted, token)
                || await context.PrivacyErasureSagas.AsNoTracking().AnyAsync(row => row.SubjectId == human, token))
                return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationEffectIntent>(context, anchor.EffectId, token);
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(
            context, $"moderation.notice:{anchor.EffectId:D}", token);
        var now = clock.GetUtcNow();
        var envelope = await context.ModerationNoticeSources.AsNoTracking()
            .Where(row => row.EffectId == anchor.EffectId && row.TenantId == authority.Tenant.Id)
            .Select(row => new { row.RetainUntil, row.SnapshotHash, row.EffectBindingHash }).SingleOrDefaultAsync(token);
        if (envelope?.RetainUntil is not { } ceiling || now >= ceiling
            || envelope.SnapshotHash != anchor.SnapshotHash || envelope.EffectBindingHash != anchor.EffectBindingHash)
            return null;
        var effect = await context.ModerationEffectIntents.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == anchor.EffectId, token);
        if (effect is null || effect.OriginalNoticeCleanupId is not null
            || effect.TrustDomainId != authority.Binding.TrustDomainId) return null;
        var source = await context.ModerationNoticeSources.AsNoTracking()
            .SingleOrDefaultAsync(row => row.EffectId == anchor.EffectId && row.TenantId == authority.Tenant.Id
                && row.RetainUntil > now, token);
        if (source is null || source.CapturedAt > now || !source.Matches(effect) || source.SnapshotHash != anchor.SnapshotHash
            || source.EffectBindingHash != anchor.EffectBindingHash || clock.GetUtcNow() >= ceiling) return null;
        ModerationNoticeCaptureSnapshot? capture;
        try { capture = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(source.SnapshotJson); }
        catch (JsonException) { return null; }
        if (capture is null || !capture.Matches(effect) || capture.HasNoSubstantiveWork
            || capture.HostBindingId != authority.Binding.Id || capture.WriterTenantId != authority.Tenant.Id
            || capture.HostSolutionId != authority.Binding.SolutionId || capture.HostInstanceId != authority.Binding.InstanceId
            || capture.AuthoritySourceId != authority.Epoch.Id || capture.QualificationRevision > authority.Epoch.Revision
            || capture.ValidUntil is not { } captureUntil || now >= captureUntil
            || capture.Notice is not
            {
                ReferenceProfile: ModerationReferenceProfileId.CFG09,
                SubjectKind: ModerationSubjectKind.Content
            } notice
            || notice.RetainUntil != ceiling || notice.SubjectOrigin != ModerationNoticeScope.From(authority.Tenant))
            return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationCase>(context, capture.Decision.CaseId, token);
        var owner = await context.ModerationCases.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == capture.Decision.CaseId, token);
        if (owner is null) return null;
        var actionScope = AuthorityScope.Resource(authority.Tenant, effect.ScopeId);
        var finding = await ModerationOriginalFindingProofOwner.AcquireAsync(
            context, owner, capture.Finding, actionScope, authority.Binding, source.CapturedAt, token);
        if (finding is null || finding.Id != capture.Decision.FindingId
            || finding.Subject.Kind != (ModerationSubjectKind)effect.SubjectKind
            || finding.Subject.Id != effect.SubjectId || finding.Subject.Origin != authority.Tenant)
            return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationPolicyPublication>(context, notice.PublicationId, token);
        var publication = await ModerationPolicyPublicationGraph.Include(context.ModerationPolicyPublications.AsNoTracking())
            .SingleOrDefaultAsync(row => row.Id == notice.PublicationId, token);
        if (publication is null || publication.AdoptedRevision != publication.Revision
            || publication.HostBindingId != authority.Binding.Id || publication.TenantId != authority.Tenant.Id
            || publication.TrustDomainId != authority.Binding.TrustDomainId
            || publication.AuthoritySourceId != authority.Epoch.Id
            || publication.QualificationRevision > authority.Epoch.Revision
            || publication.PolicyId != notice.PolicyId || publication.Revision != notice.PolicyRevision
            || publication.Revision != effect.PolicyRevision || publication.CatalogueRevision != notice.CatalogueRevision
            || publication.PublishedAt > source.CapturedAt
            || publication.Jurisdiction != authority.Tenant && publication.Jurisdiction != authority.Instance
            || notice.ProfileSource != new ModerationNoticeSourceStamp(
                publication.Id, publication.Revision, publication.WriterGeneration)) return null;
        var policy = ModerationPolicyPublicationProjection.RestoreHistory(publication);
        var profile = policy?.Profiles.SingleOrDefault(row => row.Definition.Id == notice.ProfileId)?.Definition;
        if (policy is null || profile is null || profile.ReferenceProfile != notice.ReferenceProfile
            || profile.ReferenceRevision != notice.ReferenceRevision
            || new ModerationNoticeRule(profile.Rule.OwnerId, profile.Rule.Code, profile.Rule.Revision) != notice.Rule
            || new ModerationNoticeRule(profile.Reason.OwnerId, profile.Reason.Code, profile.Reason.Revision) != notice.Reason
            || profile.ContestationCalendarMonths != notice.ContestationCalendarMonths) return null;
        var selection = await context.ModerationContributionSelections.AsNoTracking()
            .SingleOrDefaultAsync(row => row.EffectId == anchor.EffectId, token);
        if (selection is null || selection.HostBindingId != authority.Binding.Id
            || selection.TrustDomainId != authority.Binding.TrustDomainId || selection.TenantId != authority.Tenant.Id
            || selection.DecisionId != effect.DecisionId || selection.DecisionRevision != effect.DecisionRevision
            || selection.CaseId != owner.Id || selection.CaseRevision != finding.CaseRevision
            || selection.PolicyId != publication.PolicyId || selection.PolicyRevision != publication.Revision
            || selection.PublicationId != publication.Id || selection.ProfileId != notice.ProfileId
            || !authority.FencedHumans.Contains(selection.AuthoredByHumanId)) return null;
        var prepared = await context.ModerationPolicyPublicationCandidates.AsNoTracking().Include(row => row.Definition)
            .SingleOrDefaultAsync(row => row.Id == publication.Id, token);
        if (prepared is not null)
        {
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationPolicyPublicationCandidate>(context, prepared.Id, token);
            prepared.ValidatePersistedState();
            if (prepared.Definition is null || prepared.HostBindingId != authority.Binding.Id || prepared.TenantId != authority.Tenant.Id
                || prepared.TrustDomainId != authority.Binding.TrustDomainId || prepared.PolicyId != publication.PolicyId
                || prepared.Jurisdiction != publication.Jurisdiction || prepared.ExpectedRevision + 1 != publication.Revision
                || prepared.AuthoritySourceId != authority.Epoch.Id || prepared.WriterGeneration > authority.Epoch.Revision
                || prepared.PreparedAt > publication.PublishedAt
                || prepared.ProposedStateDigest != ModerationPolicyPublicationProjection.ProposedDigest(policy.Proposed))
                return null;
        }
        // Captured people must all have been discovered before primary fences. An incomplete
        // discovery set is unavailable, never permission to acquire late out-of-order fences.
        if (capture.ContentRepresentatives.Any(row => !authority.FencedHumans.Contains(row.HumanId))
            || capture.Reviewers.Any(row => !authority.FencedHumans.Contains(row.ReviewerHumanId)
                || !authority.FencedHumans.Contains(row.ControllerHumanId))) return null;
        var binding = new ModerationActivationBinding(publication.PolicyId, publication.Revision, publication.Id,
            publication.Jurisdiction, profile.Rule, ModerationCapability.ReadResource, ModerationNativeMode.Embedded);
        if (!policy.Consumers.Any(row => row.Binding == binding)) return null;
        var result = new ModerationOriginalRecipeAuthority(context, authority, privacy, clock, transactionId,
            anchor, policy, prepared?.DefinitionId ?? publication.Id, binding, publication.PrincipalId,
            prepared?.PreparedByHumanId, ceiling < captureUntil ? ceiling : captureUntil, capture, source.CapturedAt);
        return await result.CurrentAsync(token) ? result : null;
    }

    internal async Task<bool> CurrentAsync(CancellationToken token)
    {
        if (!TransactionMatches(context, authority, transactionId) || clock.GetUtcNow() >= ValidUntil
            || !await context.Tenants.AsNoTracking().AnyAsync(row => row.Id == authority.Tenant.Id
                && row.TenantStatus.IsActiveState, token)
            || !await context.ModerationHostBindings.AsNoTracking().AnyAsync(row => row.Id == authority.Binding.Id
                && row.TrustDomainId == authority.Binding.TrustDomainId && row.InstanceId == authority.Binding.InstanceId
                && row.SolutionId == authority.Binding.SolutionId, token)) return false;
        foreach (var human in authority.FencedHumans)
            if (await privacy.IsSubjectFencedAsync(human, token)
                || !await context.Users.AsNoTracking().AnyAsync(row => row.Id == human && !row.IsDeleted, token)
                || await context.PrivacyErasureSagas.AsNoTracking().AnyAsync(row => row.SubjectId == human, token))
                return false;
        foreach (var human in authority.Administrators.Concat(
            new[] { (Guid?)publisher, preparer }.OfType<Guid>()).Distinct())
        {
            if (!authority.FencedHumans.Contains(human) || !authority.Administrators.Contains(human)) return false;
            var platform = await context.PlatformUserRoles.AsNoTracking().AnyAsync(row => row.UserId == human
                && !row.User.IsDeleted && row.Role.RoleScopeId == (int)RoleScopeEnum.Platform
                && row.Role.MasterCode == "platform.admin", token);
            var publicationActor = human == publisher || human == preparer;
            if (!platform && (publicationActor && Policy.Publication.Jurisdiction != authority.Tenant
                || !await context.TenantUserRoleGrants.AsNoTracking().AnyAsync(row =>
                    row.TenantId == authority.Tenant.Id && row.TenantUser.UserId == human && row.RevokedAt == null
                    && row.RoleScopeId == (int)RoleScopeEnum.Tenant && row.RoleId == (int)RoleEnum.TenantAdmin
                    && row.Role.RoleScopeId == (int)RoleScopeEnum.Tenant
                    && row.TenantUser.StatusId == (int)TenantUserStatusEnum.Active
                    && !row.TenantUser.IsDeleted && !row.TenantUser.User.IsDeleted, token))) return false;
        }
        var owner = await context.ModerationCases.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == capture.Decision.CaseId, token);
        if (owner is null) return false;
        var finding = await ModerationOriginalFindingProofOwner.AcquireAsync(
            context, owner, capture.Finding, AuthorityScope.Resource(authority.Tenant, capture.Decision.Scope.Id),
            authority.Binding, capturedAt, token);
        if (finding is null || finding.Id != capture.Decision.FindingId
            || finding.Subject.Kind != capture.Decision.SubjectKind || finding.Subject.Id != capture.Decision.SubjectId
            || finding.Subject.Origin != authority.Tenant)
            return false;
        return await context.ModerationNoticeSources.AsNoTracking().AnyAsync(row => row.EffectId == Anchor.EffectId
                && row.TenantId == authority.Tenant.Id && row.SnapshotHash == Anchor.SnapshotHash
                && row.EffectBindingHash == Anchor.EffectBindingHash && row.RetainUntil > clock.GetUtcNow(), token)
            && await context.ModerationContributionSelections.AsNoTracking().AnyAsync(row =>
                row.EffectId == Anchor.EffectId && row.CaseId == owner.Id
                && row.CaseRevision == finding.CaseRevision, token)
            && await context.ModerationEffectIntents.AsNoTracking().AnyAsync(row => row.Id == Anchor.EffectId
                && row.OriginalNoticeCleanupId == null, token)
            && clock.GetUtcNow() < ValidUntil && TransactionMatches(context, authority, transactionId);
    }

    private static bool TransactionMatches(ExploreDbContext context, ModerationSourceAuthority authority, Guid id) =>
        context.Database.CurrentTransaction is { } transaction && transaction.TransactionId == id
        && transaction.GetDbTransaction().IsolationLevel == IsolationLevel.Serializable
        && transaction.GetDbTransaction().Connection == context.Database.GetDbConnection()
        && !context.IsTenantFilterBypassed && context.TenantFilterTenantId == authority.Tenant.Id
        && context.ModerationTrustDomainId == authority.Binding.TrustDomainId;
}

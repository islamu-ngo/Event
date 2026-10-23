using System.Data;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

/// <summary>
/// Service-owned reconciliation and leased-effect identity acquisition. Persisted credentials must resolve to the installed
/// maintenance service itself; neither a key's human owner nor an administrator becomes that service.
/// Configured credentials need their native generation-fence owner and are not accepted here.
/// </summary>
internal sealed class ModerationMaintenanceAuthority(
    ExploreDbContext context, IMachinePrincipalAccessor machine,
    IModerationMachineSubjectBindingRepository bindings, IExternalApiKeyRepository keys, TimeProvider clock)
{
    public Task<ModerationSourceAuthority?> AcquireAsync(ModerationOperation operation, CancellationToken token) =>
        operation.Kind == ModerationOperationKind.Reconcile
            ? AcquireCoreAsync(operation.DecisionId, operation.Subject, operation.Target, operation.EffectId,
                SourceDiscovery.CurrentLifecycle, token)
            : Task.FromResult<ModerationSourceAuthority?>(null);

    /// <summary>
    /// Acquires maintenance identity and participating human fences for an actual owned live effect.
    /// This is not a lifecycle grant, original-plan interpretation, target adoption or completion.
    /// The native consequence owner must separately qualify those facts and recheck final authority.
    /// </summary>
    public async Task<ModerationSourceAuthority?> AcquireForEffectAsync(
        Guid effectId, Guid leaseId, long expectedRevision,
        IPrivacyIdentityFenceAuthority privacy, CancellationToken token)
    {
        if (effectId == Guid.Empty || leaseId == Guid.Empty || expectedRevision is < 1 or long.MaxValue
            || machine.Current is not { CredentialSource: ApiKeyCredentialSource.Persisted } principal
            || !await NativeTransactionAsync(token))
            return null;
        var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
        if (host is null) return null;
        context.ModerationTrustDomainId = host.TrustDomainId;
        var discovered = await context.ModerationEffectIntents.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == effectId, token);
        var decision = discovered is null ? null : await context.ModerationDecisions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == discovered.DecisionId, token);
        if (decision is null || discovered is null) return null;
        var source = await AcquireCoreAsync(decision.Id, decision.Subject, decision.Scope, effectId,
            SourceDiscovery.OriginalEffect, token);
        if (source is null || source.Binding.Id != host.Id) return null;
        // The caller holds the selected retained gate before any primary acquisition.
        // A retained fact can precede the application saga and still closes original custody.
        foreach (var humanId in source.FencedHumans)
            if (await privacy.IsSubjectFencedAsync(humanId, token)) return null;
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(
            context, $"moderation.effect:{effectId:D}", token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationDecision>(context, decision.Id, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationEffectIntent>(context, effectId, token);
        decision = await context.ModerationDecisions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == decision.Id, token);
        var effect = await context.ModerationEffectIntents.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == effectId, token);
        if (decision is null || effect is null) return null;
        decision.ValidatePersistedState();
        effect.ValidatePersistedState();
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationOperationReceipt>(context, effect.OperationId, token);
        var receipt = await context.ModerationOperationReceipts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == effect.OperationId, token);
        if (receipt is null) return null;
        var causeRevision = effect.OperationKind is (int)ModerationOperationKind.Revoke or (int)ModerationOperationKind.Reconcile
            && effect.SanctionId is not null ? effect.SanctionRevision : effect.DecisionRevision;
        if (!Enum.IsDefined((ModerationOperationKind)effect.OperationKind)
            || effect.OperationKind == (int)ModerationOperationKind.Evaluate
            || effect.DecisionId != decision.Id || effect.TrustDomainId != source.Binding.TrustDomainId
            || effect.SubjectKind != (int)decision.Subject.Kind || effect.SubjectId != decision.Subject.Id
            || effect.ScopeKind != (int)decision.Scope.Kind || effect.ScopeId != decision.Scope.Id
            || decision.Scope != source.Instance && !source.Tenant.Contains(decision.Scope)
            || receipt.DecisionId != effect.DecisionId || receipt.SanctionId != effect.SanctionId
            || receipt.ResultRevision != causeRevision || receipt.TrustDomainId != effect.TrustDomainId
            || receipt.ScopeKind != effect.ScopeKind || receipt.ScopeId != effect.ScopeId
            || receipt.SubjectKind != effect.SubjectKind || receipt.SubjectId != effect.SubjectId
            || receipt.PolicyRevision != effect.PolicyRevision || receipt.QualificationRevision != effect.QualificationRevision)
            return null;
        var qualifiedAt = clock.GetUtcNow();
        if (effect.CompletedAt is not null || effect.LeaseId != leaseId || effect.Revision != expectedRevision
            || effect.LeaseStartedAt is null || qualifiedAt < effect.LeaseStartedAt
            || effect.LeaseExpiresAt is null || qualifiedAt >= effect.LeaseExpiresAt)
            return null;
        await using var noticeSlot = await RelationalNamedLock.AcquireTransactionAsync(
            context, $"moderation.notice:{effectId:D}", token);
        var ownership = await context.ModerationNoticeSources.AsNoTracking()
            .Where(row => row.EffectId == effectId)
            .Select(row => new { row.TenantId, row.RetainUntil }).SingleOrDefaultAsync(token);
        if (ownership is null || ownership.TenantId != source.Tenant.Id
            || ownership.RetainUntil is { } deadline && clock.GetUtcNow() >= deadline)
            return null;
        var original = await context.ModerationNoticeSources.AsNoTracking()
            .SingleOrDefaultAsync(row => row.EffectId == effectId, token);
        var current = await bindings.ReadCurrentAsync(principal.KeyId, ModerationMachineCredentialSource.Persisted, token);
        var now = clock.GetUtcNow();
        return machine.Current == principal && current is not null
            && current.HostBindingId == source.Binding.Id
            && current.ServicePrincipalId == source.Binding.MaintenanceServicePrincipalId
            && current.AuthorityTenantId == principal.TenantId
            && current.OwnerType == principal.OwnerType && current.OwnerId == principal.OwnerId
            && original is not null && original.TenantId == source.Tenant.Id && original.Matches(effect)
            && (original.RetainUntil is null || now < original.RetainUntil)
            && effect.CompletedAt is null && effect.LeaseId == leaseId && effect.Revision == expectedRevision
            && effect.LeaseStartedAt is not null && now >= effect.LeaseStartedAt
            && effect.LeaseExpiresAt is not null && now < effect.LeaseExpiresAt
            ? source : null;
    }

    private async Task<ModerationSourceAuthority?> AcquireCoreAsync(
        Guid decisionId, SubjectReference subject, AuthorityScope target, Guid effectId,
        SourceDiscovery discovery, CancellationToken token)
    {
        if (machine.Current is not { CredentialSource: ApiKeyCredentialSource.Persisted } principal
            || context.Database.CurrentTransaction is null || context.IsTenantFilterBypassed
            || context.TenantFilterTenantId is not { } tenantId || tenantId == Guid.Empty
            || !await NativeTransactionAsync(token))
            return null;
        var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
        var key = await keys.GetByKeyIdForAuthentication(principal.KeyId, token);
        // Discovery proposes source/Actor anchors; authoritative credential/service reads follow them.
        var binding = host is null ? null : await EventAuthorityProviderOperations.ReadWithoutRetainingLocksAsync(context,
            source => source.ModerationMachineSubjectBindings.AsNoTracking().SingleOrDefaultAsync(
                row => row.HostBindingId == host.Id && row.KeyId == principal.KeyId
                    && row.CredentialSource == ModerationMachineCredentialSource.Persisted && row.RevokedAt == null, token), token);
        if (host is null || key is null || binding is null
            || binding.HostBindingId != host.Id || binding.ServicePrincipalId != host.MaintenanceServicePrincipalId
            || binding.AuthorityTenantId != principal.TenantId
            || binding.OwnerId != principal.OwnerId || binding.OwnerType != principal.OwnerType
            || binding.AuthorityTenantId is { } credentialTenant && credentialTenant != tenantId)
            return null;
        context.ModerationTrustDomainId = host.TrustDomainId;
        var instance = AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(host.TrustDomainId), host.SolutionId), host.InstanceId);
        var tenant = AuthorityScope.Tenant(instance, tenantId);
        if (target != instance && !tenant.Contains(target))
            return null;

        var nativeTargets = await ModerationNativeTargetPlan.DiscoverAsync(context, host.Id, tenantId, effectId, token);
        var materialGenerations = await EventAuthorityProviderOperations.ReadMaterialGenerationPlansAsync(context, host.Id, tenantId, token);
        var serviceActors = await EventAuthorityProviderOperations.ReadWithoutRetainingLocksAsync(context,
            source => source.Actors.AsNoTracking().IgnoreQueryFilters(["SoftDelete"])
                .Where(row => row.ServicePrincipalId == binding.ServicePrincipalId).Select(row => row.Id).ToArrayAsync(token), token);

        // A service has no human caller to feed the human authority helper. Discover only humans
        // actually participating in this exact contribution; retain their real writer rows below.
        var decision = await context.ModerationDecisions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == decisionId, token);
        if (decision is null || decision.Subject != subject || decision.Scope != target)
            return null;
        var humans = new HashSet<Guid>();
        var activePublications = discovery == SourceDiscovery.CurrentLifecycle
            ? await context.ModerationPolicySelections.AsNoTracking()
            .Where(row => row.HostBindingId == host.Id
                && (row.JurisdictionTenantId == null || row.JurisdictionTenantId == tenantId))
            .Select(row => row.ActivePublicationId).ToListAsync(token) : [];
        // Discover the exact retained effect's publication before the globally sorted human
        // fences. This adds provenance to discovery, never current lifecycle authority.
        var originalPublication = await context.ModerationContributionSelections.AsNoTracking()
            .Where(row => row.EffectId == effectId && row.HostBindingId == host.Id && row.TenantId == tenantId)
            .Select(row => (Guid?)row.PublicationId).SingleOrDefaultAsync(token);
        if (discovery == SourceDiscovery.OriginalEffect && originalPublication is { } originalId)
            activePublications.Add(originalId);
        if (discovery == SourceDiscovery.OriginalEffect)
            foreach (var publisher in await context.ModerationPolicyPublications.AsNoTracking()
                .Where(row => activePublications.Contains(row.Id) && row.HostBindingId == host.Id && row.TenantId == tenantId)
                .Select(row => row.PrincipalId).Distinct().ToListAsync(token))
                humans.Add(publisher);
        var activePreparers = await context.ModerationPolicyPublicationCandidates.AsNoTracking()
            .Where(row => activePublications.Contains(row.Id))
            .Select(row => row.PreparedByHumanId).Distinct().ToListAsync(token);
        foreach (var preparer in activePreparers) humans.Add(preparer);
        var deployments = await context.Set<ModerationDeploymentSource>().AsNoTracking()
            .Where(row => row.HostBindingId == host.Id && row.TenantId == tenantId
                && activePublications.Contains(row.PublicationId))
            .Select(row => new { row.Id, row.RecordedByHumanId }).ToListAsync(token);
        foreach (var deployment in deployments) humans.Add(deployment.RecordedByHumanId);
        var deploymentIds = deployments.Select(row => row.Id).ToArray();
        var deploymentAdoptions = await context.Set<ModerationDeploymentAdoption>().AsNoTracking()
            .Where(row => deploymentIds.Contains(row.SourceId)).ToListAsync(token);
        foreach (var adoption in deploymentAdoptions)
        {
            humans.Add(adoption.AdoptedByHumanId);
            if (adoption.WithdrawnByHumanId is { } human) humans.Add(human);
        }
        var constitutions = await context.Set<ModerationConstitutionSource>().AsNoTracking()
            .Include(row => row.Reviewers)
            .Where(row => row.HostBindingId == host.Id && row.TenantId == tenantId
                && activePublications.Contains(row.PublicationId)).ToListAsync(token);
        foreach (var constitution in constitutions)
        {
            humans.Add(constitution.RecordedByHumanId);
            humans.Add(constitution.CoordinatorHumanId);
            humans.Add(constitution.FallbackHumanId);
            foreach (var reviewer in constitution.Reviewers)
            {
                humans.Add(reviewer.ReviewerHumanId);
                humans.Add(reviewer.ControllerHumanId);
            }
        }
        var constitutionIds = constitutions.Select(row => row.Id).ToArray();
        var constitutionAdoptions = await context.Set<ModerationConstitutionAdoption>().AsNoTracking()
            .Where(row => constitutionIds.Contains(row.SourceId)).ToListAsync(token);
        foreach (var adoption in constitutionAdoptions)
        {
            humans.Add(adoption.AdoptedByHumanId);
            if (adoption.WithdrawnByHumanId is { } human) humans.Add(human);
        }
        var privacySources = await context.Set<ModerationRecognitionPrivacySource>().AsNoTracking()
            .Where(row => row.HostBindingId == host.Id && row.TenantId == tenantId
                && activePublications.Contains(row.PublicationId)).ToListAsync(token);
        foreach (var privacy in privacySources)
        {
            humans.Add(privacy.RecordedByHumanId);
            humans.Add(privacy.PrivacyOwnerHumanId);
            humans.Add(privacy.PrivacyControllerHumanId);
            humans.Add(privacy.RecoveryOwnerHumanId);
            humans.Add(privacy.RecoveryControllerHumanId);
            humans.Add(privacy.GovernanceControllerHumanId);
        }
        var privacySourceIds = privacySources.Select(row => row.Id).ToArray();
        var privacyAdoptions = await context.Set<ModerationRecognitionPrivacyAdoption>().AsNoTracking()
            .Where(row => privacySourceIds.Contains(row.SourceId)).ToListAsync(token);
        foreach (var adoption in privacyAdoptions)
        {
            humans.Add(adoption.AdoptedByHumanId);
            if (adoption.WithdrawnByHumanId is { } human) humans.Add(human);
        }
        var childSources = await context.Set<ModerationChildAgePolicySource>().AsNoTracking()
            .Where(row => row.HostBindingId == host.Id && row.TenantId == tenantId
                && activePublications.Contains(row.PublicationId)).ToListAsync(token);
        foreach (var child in childSources)
        {
            humans.Add(child.RecordedByHumanId);
            humans.Add(child.ReviewerHumanId);
            humans.Add(child.ReviewerControllerHumanId);
            humans.Add(child.CollectionOwnerHumanId);
            humans.Add(child.CollectionControllerHumanId);
            humans.Add(child.GovernanceControllerHumanId);
        }
        var childSourceIds = childSources.Select(row => row.Id).ToArray();
        var childAdoptions = await context.Set<ModerationChildAgePolicyAdoption>().AsNoTracking()
            .Where(row => childSourceIds.Contains(row.SourceId)).ToListAsync(token);
        foreach (var adoption in childAdoptions)
        {
            humans.Add(adoption.AdoptedByHumanId);
            if (adoption.WithdrawnByHumanId is { } human) humans.Add(human);
        }
        var rightsSources = await context.Set<ModerationEventRightsSource>().AsNoTracking()
            .Where(row => row.HostBindingId == host.Id && row.TenantId == tenantId
                && activePublications.Contains(row.PublicationId)).ToListAsync(token);
        foreach (var rights in rightsSources)
        {
            humans.Add(rights.RecordedByHumanId);
            humans.Add(rights.ReviewerHumanId);
            humans.Add(rights.ReviewerControllerHumanId);
            humans.Add(rights.ContinuityMappingOwnerHumanId);
            humans.Add(rights.ContinuityMappingControllerHumanId);
            humans.Add(rights.SettlementMappingOwnerHumanId);
            humans.Add(rights.SettlementMappingControllerHumanId);
            humans.Add(rights.GovernanceControllerHumanId);
        }
        var rightsSourceIds = rightsSources.Select(row => row.Id).ToArray();
        var rightsAdoptions = await context.Set<ModerationEventRightsAdoption>().AsNoTracking()
            .Where(row => rightsSourceIds.Contains(row.SourceId)).ToListAsync(token);
        foreach (var adoption in rightsAdoptions)
        {
            humans.Add(adoption.AdoptedByHumanId);
            if (adoption.WithdrawnByHumanId is { } human) humans.Add(human);
        }
        var operationalSources = await context.Set<ModerationMeasuredOperationsSource>().AsNoTracking()
            .Where(row => row.HostBindingId == host.Id && row.TenantId == tenantId
                && activePublications.Contains(row.PublicationId)).ToListAsync(token);
        foreach (var source in operationalSources)
        {
            humans.Add(source.RecordedByHumanId);
            humans.Add(source.ReviewerHumanId);
            humans.Add(source.ReviewerControllerHumanId);
            humans.Add(source.RecorderControllerHumanId);
        }
        var operationalIds = operationalSources.Select(row => row.Id).ToArray();
        var operationalAcceptances = await context.Set<ModerationMeasuredOperationsAcceptance>().AsNoTracking()
            .Where(row => operationalIds.Contains(row.SourceId)).ToListAsync(token);
        foreach (var acceptance in operationalAcceptances)
        {
            humans.Add(acceptance.AcceptedByHumanId);
            if (acceptance.WithdrawnByHumanId is { } human) humans.Add(human);
        }
        var consumers = await context.ModerationConsumerInstallations.AsNoTracking()
            .Where(row => row.HostBindingId == host.Id
                && !context.ModerationConsumerInstallations.Any(newer =>
                    newer.HostBindingId == row.HostBindingId && newer.ConsumerId == row.ConsumerId
                    && newer.ModeCode == row.ModeCode && newer.WriterGeneration > row.WriterGeneration))
            .ToListAsync(token);
        foreach (var consumer in consumers)
        {
            humans.Add(consumer.InstalledByHumanId);
            if (consumer.WithdrawnByHumanId is { } withdrawer) humans.Add(withdrawer);
        }
        if (consumers.Count != 0)
        {
            var installationIds = consumers.Select(row => row.Id).ToArray();
            // Bounded discovery precedes human/role fences and never grants service permission.
            var measurements = await (
                from receipt in context.ModerationConsumerMeasurementReceipts.AsNoTracking()
                join challenge in context.ModerationConsumerMeasurementChallenges.AsNoTracking()
                    on receipt.ChallengeId equals challenge.Id
                where installationIds.Contains(challenge.InstallationId)
                    && challenge.HostBindingId == host.Id && challenge.TenantId == tenantId
                    && (discovery != SourceDiscovery.OriginalEffect || activePublications.Contains(challenge.PublicationId))
                    && receipt.HostBindingId == host.Id && receipt.TenantId == tenantId
                select new { receipt.AcceptedByHumanId, challenge.PreparedByHumanId, receipt.IssuerEnrollmentId })
                .ToListAsync(token);
            foreach (var measurement in measurements)
            {
                humans.Add(measurement.AcceptedByHumanId);
                humans.Add(measurement.PreparedByHumanId);
            }
            var issuerIds = measurements.Select(row => row.IssuerEnrollmentId).Distinct().ToArray();
            foreach (var approver in await context.ModerationMeasurementIssuerEnrollments.AsNoTracking()
                .Where(row => issuerIds.Contains(row.Id) && row.HostBindingId == host.Id && row.TenantId == tenantId)
                .Select(row => row.ApprovedByHumanId).ToListAsync(token))
                humans.Add(approver);
        }
        var selection = await context.ModerationContributionSelections.AsNoTracking()
            .SingleOrDefaultAsync(row => row.EffectId == effectId, token);
        if (selection is not null)
        {
            humans.Add(selection.AuthoredByHumanId);
            var adoptions = await context.ModerationResourceTargetAdoptions.AsNoTracking()
                .Where(row => row.SelectionId == selection.Id).ToListAsync(token);
            foreach (var adoption in adoptions)
            {
                humans.Add(adoption.AdoptedByHumanId);
                if (adoption.RevokedByHumanId is { } revoker) humans.Add(revoker);
            }
            var generationIds = adoptions.Select(row => row.GenerationId).Distinct().ToArray();
            foreach (var observer in await context.ModerationResourceGenerations.AsNoTracking()
                .Where(row => generationIds.Contains(row.Id)).Select(row => row.ObservedByHumanId).ToListAsync(token))
                humans.Add(observer);
            var eventAdoptions = await context.ModerationEventTargetAdoptions.AsNoTracking()
                .Where(row => row.SelectionId == selection.Id).ToListAsync(token);
            foreach (var adoption in eventAdoptions)
            {
                humans.Add(adoption.AdoptedByHumanId);
                if (adoption.RevokedByHumanId is { } revoker) humans.Add(revoker);
            }
            var eventGenerationIds = eventAdoptions.Select(row => row.GenerationId).Distinct().ToArray();
            foreach (var observer in await context.ModerationEventGenerations.AsNoTracking()
                .Where(row => eventGenerationIds.Contains(row.Id)).Select(row => row.ObservedByHumanId).ToListAsync(token))
                humans.Add(observer);
            var profile = await context.ModerationProfileAdoptions.AsNoTracking()
                .SingleOrDefaultAsync(row => row.SelectionId == selection.Id, token);
            if (profile is not null)
            {
                humans.Add(profile.AdoptedByHumanId);
                if (profile.RevokedByHumanId is { } revoker) humans.Add(revoker);
            }
        }
        var disclosure = await context.ModerationNoticeDisclosureApprovals.AsNoTracking()
            .SingleOrDefaultAsync(row => row.EffectId == effectId, token);
        if (disclosure is not null)
        {
            humans.Add(disclosure.ApprovedByHumanId);
            if (disclosure.RevokedByHumanId is { } revoker) humans.Add(revoker);
            foreach (var representative in disclosure.Intent.ContentRepresentatives)
                humans.Add(representative.HumanId);
        }
        if (subject.Kind == ModerationSubjectKind.Human) humans.Add(subject.Id);
        if (decision.IssuerKind == ModerationSubjectKind.Human) humans.Add(decision.IssuerId);
        foreach (var approval in decision.Approvals) humans.Add(approval.Id);
        var reviewers = await context.ModerationReviewerAssignments.AsNoTracking()
            .Where(row => row.CaseId == decision.CaseId && row.WithdrawnAt == null).ToListAsync(token);
        foreach (var reviewer in reviewers)
        {
            humans.Add(reviewer.ReviewerHumanId);
            foreach (var id in new[] { reviewer.ControllerHumanId, reviewer.QualifiedByHumanId, reviewer.AssessedByHumanId })
                if (id is { } human) humans.Add(human);
        }
        if (decision.FindingId is { } findingId)
        {
            var finding = await context.ModerationFindings.AsNoTracking().SingleOrDefaultAsync(row => row.Id == findingId, token);
            if (finding is not null)
            {
                humans.Add(finding.AdjudicatorId);
                var evidence = await context.ModerationQualificationEvidence.AsNoTracking()
                    .SingleOrDefaultAsync(row => row.Id == finding.EvidenceReferenceId, token);
                if (evidence is not null) humans.Add(evidence.AssertedByHumanId);
            }
        }
        // Complete attributed-case privacy/recipient discovery before the first human fence.
        // The response observer later borrows this acquisition; it must never add humans late.
        ModerationQualificationAuthority.PreFindingHumanDiscovery? preFinding = null;
        if (discovery == SourceDiscovery.CurrentLifecycle
            && await context.ModerationCases.AsNoTracking().AnyAsync(
                row => row.Id == decision.CaseId && row.AttributionSourceId != null, token))
        {
            var responseHumans = await ModerationQualificationAuthority
                .DiscoverPreFindingHumansAsync(context, decision.CaseId, [], token);
            if (responseHumans is null) return null;
            humans.UnionWith(responseHumans.Humans);
            preFinding = responseHumans;
        }
        var humanIds = humans.Order().ToArray();
        var materialAuthority = materialGenerations.Count == 0 ? null
            : await EventAuthorityProviderOperations.ReadPlanAsync(context, tenantId, humanIds,
                materialGenerations.Values.Select(row => row.Id).Distinct().ToArray(), token);
        foreach (var human in humanIds)
            await RelationalEntityRowFence.AcquireGlobalAsync<User>(context, human, token);
        var loginIds = await context.UserExternalLogins.AsNoTracking()
            .Where(row => humanIds.Contains(row.UserId)).OrderBy(row => row.Id).Select(row => row.Id).ToListAsync(token);
        foreach (var id in loginIds)
            await RelationalEntityRowFence.AcquireGlobalAsync<UserExternalLogin>(context, id, token);
        var platform = await context.PlatformUserRoles.AsNoTracking()
            .Where(row => humanIds.Contains(row.UserId)).OrderBy(row => row.Id).ToListAsync(token);
        foreach (var row in platform)
            await RelationalEntityRowFence.AcquireGlobalAsync<PlatformUserRole>(context, row.Id, token);
        var members = await context.TenantUsers.AsNoTracking()
            .Where(row => row.TenantId == tenantId && humanIds.Contains(row.UserId)).OrderBy(row => row.Id).ToListAsync(token);
        foreach (var row in members)
            await RelationalEntityRowFence.AcquireAsync<TenantUser>(context, tenantId, value => value.Id, row.Id, token);
        var memberIds = members.Select(row => row.Id).ToArray();
        var grants = await context.TenantUserRoleGrants.AsNoTracking()
            .Where(row => memberIds.Contains(row.TenantUserId)).OrderBy(row => row.Id).ToListAsync(token);
        foreach (var row in grants)
            await RelationalEntityRowFence.AcquireAsync<TenantUserRoleGrant>(context, tenantId, value => value.Id, row.Id, token);
        foreach (var id in platform.Select(row => row.RoleId).Concat(grants.Select(row => row.RoleId))
            .Concat(nativeTargets.RepresentativeRoles)
            .Concat(preFinding?.RepresentativeAuthority?.Assignments.Select(row => row.RoleId) ?? [])
            .Concat(materialAuthority?.Assignments.Select(row => row.RoleId) ?? []).Distinct().Order())
            await RelationalEntityRowFence.AcquireLookupAsync<Role>(context, [id], token);
        await EventAuthoritySnapshotService.AcquirePermissionSourcesAsync(context,
            nativeTargets.RepresentativeRoles.Concat(materialAuthority?.Assignments.Select(row => row.RoleId) ?? [])
                .Concat(preFinding?.RepresentativeAuthority?.Assignments.Select(row => row.RoleId) ?? [])
                .Distinct().Order().ToArray(), token);

        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationHostBinding>(context, host.Id, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ServicePrincipal>(context, binding.ServicePrincipalId, token);
        foreach (var id in serviceActors.Concat(nativeTargets.Actors)
            .Concat(materialGenerations.Values.SelectMany(row => new Guid?[] { row.ActorId, row.OrganizerActorId }).OfType<Guid>())
            .Concat((materialAuthority?.Parents ?? []).SelectMany(row => new Guid?[] { row.ActorId, row.OrganizerActorId }).OfType<Guid>())
            .Concat((preFinding?.RepresentativeAuthority?.Parents ?? []).SelectMany(row => new Guid?[] { row.ActorId, row.OrganizerActorId }).OfType<Guid>())
            .Distinct().Order())
            await RelationalEntityRowFence.AcquireGlobalAsync<Actor>(context, id, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ExternalApiKey>(context, key.Id, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationMachineSubjectBinding>(context, binding.Id, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<Tenant>(context, tenantId, token);
        var tenantRow = await context.Tenants.AsNoTracking().SingleOrDefaultAsync(row => row.Id == tenantId, token);
        if (tenantRow is null) return null;
        await RelationalEntityRowFence.AcquireLookupAsync<TenantStatus>(context, [tenantRow.TenantStatusId], token);
        var epoch = await context.ModerationQualificationEpochs.AsNoTracking()
            .SingleOrDefaultAsync(row => row.HostBindingId == host.Id && row.TenantId == tenantId, token);
        if (epoch is null) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationEpoch>(context, epoch.Id, token);

        var currentHost = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
        var current = await bindings.ReadCurrentAsync(principal.KeyId, ModerationMachineCredentialSource.Persisted, token);
        var currentEpoch = await context.ModerationQualificationEpochs.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == epoch.Id, token);
        if (machine.Current != principal || currentHost is null || current is null || currentEpoch is null
            || currentHost.Id != host.Id || currentHost.TrustDomainId != host.TrustDomainId
            || currentHost.SolutionId != host.SolutionId || currentHost.InstanceId != host.InstanceId
            || currentHost.MaintenanceServicePrincipalId != binding.ServicePrincipalId
            || current.Id != binding.Id || current.Revision != binding.Revision
            || current.ServicePrincipalId != binding.ServicePrincipalId || current.HostBindingId != host.Id
            || current.AuthorityTenantId != principal.TenantId || current.OwnerType != principal.OwnerType
            || current.OwnerId != principal.OwnerId || currentEpoch.Revision < 1
            || currentEpoch.HostBindingId != host.Id || currentEpoch.TenantId != tenantId)
            return null;
        var platformIds = platform.Select(row => row.Id).ToArray();
        var nativeAdmins = await context.PlatformUserRoles.AsNoTracking()
            .Where(row => platformIds.Contains(row.Id) && !row.User.IsDeleted
                && row.Role.RoleScopeId == (int)RoleScopeEnum.Platform && row.Role.MasterCode == "platform.admin")
            .Select(row => row.UserId).ToListAsync(token);
        if (target != instance)
        {
            var grantIds = grants.Select(row => row.Id).ToArray();
            var tenantAdmins = await context.TenantUserRoleGrants.AsNoTracking()
                .Where(row => grantIds.Contains(row.Id) && row.TenantId == tenantId && row.RevokedAt == null
                    && row.RoleScopeId == (int)RoleScopeEnum.Tenant && row.RoleId == (int)RoleEnum.TenantAdmin
                    && row.Role.RoleScopeId == (int)RoleScopeEnum.Tenant
                    && row.TenantUser.StatusId == (int)TenantUserStatusEnum.Active
                    && !row.TenantUser.IsDeleted && !row.TenantUser.User.IsDeleted)
                .Select(row => row.TenantUser.UserId).ToListAsync(token);
            nativeAdmins.AddRange(tenantAdmins);
        }
        return new(binding.ServicePrincipalId, currentHost, currentEpoch, instance, tenant,
            false, humanIds, nativeAdmins.Distinct().ToArray())
        {
            NativeTargets = nativeTargets,
            MaterialGenerations = materialGenerations,
            MaterialAuthority = materialAuthority,
            PreFinding = preFinding
        };
    }

    private enum SourceDiscovery { CurrentLifecycle, OriginalEffect }

    private async Task<bool> NativeTransactionAsync(CancellationToken token)
    {
        if (!context.Database.IsRelational() || context.IsTenantFilterBypassed
            || context.TenantFilterTenantId is not { } tenantId || tenantId == Guid.Empty
            || context.Database.CurrentTransaction is not { } transaction
            || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable
            || context.Database.ProviderName is not (RelationalNamedLock.PostgreSqlProvider
                or RelationalNamedLock.SqlServerProvider or RelationalNamedLock.SqliteProvider or RelationalNamedLock.MySqlProvider))
            return false;
        if (context.Database.ProviderName != RelationalNamedLock.PostgreSqlProvider) return true;
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "SELECT current_setting('app.current_tenant_id', true)";
        var value = await command.ExecuteScalarAsync(token);
        return value is string setting && Guid.TryParse(setting, out var actual) && actual == tenantId;
    }
}

using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

/// <summary>Actual native qualification sources acquired on the owning transaction's connection.</summary>
internal sealed partial class ModerationQualificationAuthority(
    ExploreDbContext context, IAdminContext admin, IMachinePrincipalAccessor machine)
{
    public Task<ModerationSourceAuthority?> AcquireAsync(
        ModerationQualificationMutation mutation, CancellationToken token) => AcquireAsync(mutation, null, token);

    internal async Task<ModerationSourceAuthority?> AcquireAsync(
        ModerationQualificationMutation mutation, ModerationSourceAuthority? borrowedDisclosureAuthority,
        CancellationToken token)
    {
        if (!ValidClassificationMaterialRequest(mutation) || !ValidClassificationControlRequest(mutation)) return null;
        if (mutation is (RecordModerationOperationalBudgetSource or AdoptModerationOperationalBudgetSource
                or WithdrawModerationOperationalBudgetAdoption)
            && !ModerationOperationalBudgetQualification.IsValid(mutation))
            return null;
        if (mutation is RecordModerationParticipantIncidentResponsibilitySource investigation
            && !ModerationParticipantInvestigationQualification.IsValid(investigation))
            return null;
        var contentTargetCase = mutation.Target.Kind is (ModerationScopeKind.Resource or ModerationScopeKind.Event)
            && IsContentResourceCaseMutation(mutation);
        var retainedTarget = mutation.Target.Kind is (ModerationScopeKind.Resource or ModerationScopeKind.Event)
            && IsRetainedCaseCorrection(mutation);
        var classificationControl = mutation is RecordModerationClassificationControlSelection
            or AdoptModerationClassificationEventTarget or RevokeModerationClassificationEventTarget;
        var administrativeScope = contentTargetCase || retainedTarget || classificationControl
            ? mutation.Target.Parent! : mutation.Target;
        var acquired = await AcquireAsync(administrativeScope,
            mutation is RecordModerationCaseResponse or RevokeModerationNoticeDisclosureSource
            or RevokeModerationResourceTargetSource or RevokeModerationEventTargetSource or RevokeModerationProfileSource
            or WithdrawModerationConsumerSource or RevokeModerationMeasurementIssuerSource or WithdrawModerationDeploymentAdoption
            or WithdrawModerationConstitutionAdoption or WithdrawModerationConstitutionAvailability
            or WithdrawModerationRecognitionPrivacyAdoption or WithdrawModerationChildAgePolicyAdoption
            or WithdrawModerationClassificationTaxonomyAdoption or WithdrawModerationProfileCollectionProcedureAdoption
            or EndModerationClassificationMaterialReview
            or RevokeModerationClassificationEventTarget
            or WithdrawModerationEventRightsAdoption or WithdrawModerationMeasuredOperationsAcceptance
            or WithdrawModerationCaseNarrativeAdoption or WithdrawModerationOperationalBudgetAdoption
            || IsRetainedCaseCorrection(mutation),
            mutation is not (RecordModerationCaseResponse or RecordModerationClassificationMaterialSource
                or AcceptModerationClassificationMaterialSource), mutation, null, token,
            borrowedDisclosureAuthority: borrowedDisclosureAuthority);
        if (acquired is null) return null;
        if (retainedTarget)
            return await QualifyRetainedTargetCorrectionAsync(mutation, acquired, token) ? acquired : null;
        return contentTargetCase && !await QualifyContentTargetCaseAsync(mutation, acquired, token) ? null : acquired;
    }

    public Task<ModerationSourceAuthority?> AcquireAsync(
        AuthorityScope target, CancellationToken token) =>
        AcquireAsync(target, false, true, null, null, token);

    public Task<ModerationSourceAuthority?> AcquireAsync(
        AuthorityScope target, Guid? publicationCandidateId, CancellationToken token) =>
        AcquireAsync(target, false, true, null, null, token, publicationCandidateId);

    public Task<ModerationSourceAuthority?> AcquireAsync(
        ModerationOperation operation, CancellationToken token) =>
        AcquireAsync(operation.Target,
            operation.Kind is ModerationOperationKind.RecordResponse or ModerationOperationKind.Withdraw
                or ModerationOperationKind.Revoke
                || operation.Kind == ModerationOperationKind.Evaluate
                    && operation.Capability is { } capability && EffectiveRestrictions.IsProtectedRemedy(capability),
            operation.Kind is not (ModerationOperationKind.Evaluate or ModerationOperationKind.RecordResponse
                or ModerationOperationKind.AcceptFinding or ModerationOperationKind.Approve
                or ModerationOperationKind.Withdraw),
            null, operation, token);

    private async Task<ModerationSourceAuthority?> AcquireAsync(
        AuthorityScope target, bool allowInactiveTenant, bool requireAdministrator,
        ModerationQualificationMutation? mutation,
        ModerationOperation? operation, CancellationToken token, Guid? publicationCandidateId = null,
        ModerationCaseTargetKind? narrativeKind = null,
        (Guid CaseId, IReadOnlyCollection<Guid> Representatives)? responseReview = null,
        ModerationCaseNarrativePurpose narrativePurpose = ModerationCaseNarrativePurpose.ConfidentialAllegation,
        ModerationSourceAuthority? borrowedDisclosureAuthority = null,
        Guid? contentResponseNoticeId = null, PreFindingHumanDiscovery? preFindingDiscovery = null,
        ModerationCaseReadSourcePlan? protectedRead = null, IReadOnlyList<Guid>? adjudicatedReadHumans = null)
    {
        if (machine.IsMachineCaller || context.Database.CurrentTransaction is null
            || context.IsTenantFilterBypassed || context.TenantFilterTenantId is not { } tenantId
            || tenantId == Guid.Empty || !await ProviderTenantMatchesAsync(tenantId, token))
            return null;

        var userId = await admin.ResolveUserIdAsync(token);
        var discoveredBinding = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
        if (userId is null || discoveredBinding is null)
            return null;
        if (protectedRead is not null && (protectedRead.TransactionId != context.Database.CurrentTransaction.TransactionId
            || protectedRead.CallerUserId != userId || protectedRead.Scope != target
            || protectedRead.Scope.Id != tenantId || protectedRead.HostBindingId != discoveredBinding.Id
            || protectedRead.Scope.TrustDomainId != discoveredBinding.TrustDomainId
            || protectedRead.Scope.Coordinates.SolutionId != discoveredBinding.SolutionId
            || protectedRead.Scope.Coordinates.InstanceId != discoveredBinding.InstanceId
            || protectedRead.MaintenanceServicePrincipalId != discoveredBinding.MaintenanceServicePrincipalId
            || protectedRead.TargetKind != narrativeKind)) return null;

        // This bounded discovery scope is provenance, not permission. Every fact used below
        // is subsequently fenced and reread before an authority result is returned.
        context.ModerationTrustDomainId = discoveredBinding.TrustDomainId;
        var nativeTargets = operation is not null && operation.Kind is not
            (ModerationOperationKind.Revoke or ModerationOperationKind.Reject or ModerationOperationKind.Withdraw)
            ? await ModerationNativeTargetPlan.DiscoverAsync(context, discoveredBinding.Id, tenantId, operation.EffectId, token)
            : null;
        // Terminal checks reuse the attempt's exact early sources, even when its plan is null.
        var disclosureAuthority = borrowedDisclosureAuthority is not null
            ? borrowedDisclosureAuthority.DisclosureAuthority
            : mutation is ApproveModerationNoticeDisclosureSource disclosureRequest
                ? await DiscoverDisclosureAuthorityAsync(discoveredBinding, tenantId, disclosureRequest, token)
                : contentResponseNoticeId is { } noticeId
                    ? await DiscoverContentResponseAuthorityAsync(discoveredBinding, tenantId, noticeId, userId.Value, token)
                    : null;
        var humans = new HashSet<Guid> { userId.Value };
        var requiredHumans = new HashSet<Guid> { userId.Value };
        if (adjudicatedReadHumans is not null)
        {
            if (adjudicatedReadHumans.Any(id => id == Guid.Empty)) return null;
            humans.UnionWith(adjudicatedReadHumans);
            requiredHumans.UnionWith(adjudicatedReadHumans);
        }
        if (protectedRead is not null)
        {
            humans.Add(protectedRead.IssuerUserId);
            requiredHumans.Add(protectedRead.IssuerUserId);
        }
        Guid? preFindingCaseId = responseReview?.CaseId
            ?? (mutation is FinalizeModerationFindingSource findingSource ? findingSource.CaseId : null);
        ModerationEventGeneration? retainedEventGeneration = null;
        ModerationResourceGeneration? retainedResourceGeneration = null;
        var candidateId = publicationCandidateId
            ?? (mutation as PrepareModerationConsumerMeasurementSource)?.Binding.PublicationId
            ?? (mutation as RecordModerationDeploymentSource)?.PublicationId
            ?? (mutation as RecordModerationConstitutionSource)?.PublicationId
            ?? (mutation as RecordModerationRecognitionPrivacySource)?.PublicationId
            ?? (mutation as RecordModerationChildAgePolicySource)?.PublicationId
            ?? (mutation as RecordModerationClassificationTaxonomySource)?.PublicationId
            ?? (mutation as RecordModerationClassificationMaterialSource)?.PublicationId
            ?? (mutation as RecordModerationEventRightsSource)?.PublicationId
            ?? (mutation as RecordModerationMeasuredOperationsSource)?.Binding.PublicationId;
        if (mutation is RecordModerationProfileCollectionProcedureSource or AdoptModerationProfileCollectionProcedureSource
            or WithdrawModerationProfileCollectionProcedureAdoption)
            candidateId = await ProfileCollectionProcedurePublicationAsync(mutation, token);
        if (mutation is RecordModerationClassificationControlSelection or AdoptModerationClassificationEventTarget
            or RevokeModerationClassificationEventTarget)
            candidateId = await ClassificationControlPublicationAsync(mutation, token);
        if (mutation is AcceptModerationMeasuredOperationsSource measuredAcceptance)
            candidateId = await context.Set<ModerationMeasuredOperationsSource>().AsNoTracking()
                .Where(row => row.Id == measuredAcceptance.SourceId).Select(row => (Guid?)row.PublicationId)
                .SingleOrDefaultAsync(token);
        if (mutation is AdoptModerationEventRightsSource eventRightsAdoption)
            candidateId = await context.Set<ModerationEventRightsSource>().AsNoTracking()
                .Where(row => row.Id == eventRightsAdoption.SourceId).Select(row => (Guid?)row.PublicationId)
                .SingleOrDefaultAsync(token);
        if (mutation is AcceptModerationClassificationMaterialSource materialAcceptance)
            candidateId = await context.Set<ModerationClassificationMaterialSource>().AsNoTracking()
                .Where(row => row.Id == materialAcceptance.SourceId).Select(row => (Guid?)row.PublicationId)
                .SingleOrDefaultAsync(token);
        if (mutation is AdoptModerationClassificationTaxonomySource taxonomyAdoption)
            candidateId = await context.Set<ModerationClassificationTaxonomySource>().AsNoTracking()
                .Where(row => row.Id == taxonomyAdoption.SourceId).Select(row => (Guid?)row.PublicationId)
                .SingleOrDefaultAsync(token);
        if (mutation is AdoptModerationChildAgePolicySource childAgeAdoption)
            candidateId = await context.Set<ModerationChildAgePolicySource>().AsNoTracking()
                .Where(row => row.Id == childAgeAdoption.SourceId).Select(row => (Guid?)row.PublicationId)
                .SingleOrDefaultAsync(token);
        if (mutation is AdoptModerationRecognitionPrivacySource privacyAdoption)
            candidateId = await context.Set<ModerationRecognitionPrivacySource>().AsNoTracking()
                .Where(row => row.Id == privacyAdoption.SourceId).Select(row => (Guid?)row.PublicationId)
                .SingleOrDefaultAsync(token);
        if (mutation is AdoptModerationConstitutionSource constitutionAdoption)
            candidateId = await context.Set<ModerationConstitutionSource>().AsNoTracking()
                .Where(row => row.Id == constitutionAdoption.SourceId).Select(row => (Guid?)row.PublicationId)
                .SingleOrDefaultAsync(token);
        if (mutation is AdoptModerationDeploymentSource deploymentAdoption)
            candidateId = await context.Set<ModerationDeploymentSource>().AsNoTracking()
                .Where(row => row.Id == deploymentAdoption.SourceId).Select(row => (Guid?)row.PublicationId)
                .SingleOrDefaultAsync(token);
        if (mutation is AcceptModerationConsumerMeasurementSource accepting)
            candidateId = await context.ModerationConsumerMeasurementChallenges.AsNoTracking()
                .Where(row => row.Id == accepting.ChallengeId).Select(row => (Guid?)row.PublicationId)
                .SingleOrDefaultAsync(token);
        if (candidateId is { } futurePublicationId)
        {
            var preparer = await context.ModerationPolicyPublicationCandidates.AsNoTracking()
                .Where(row => row.Id == futurePublicationId && row.HostBindingId == discoveredBinding.Id)
                .Select(row => (Guid?)row.PreparedByHumanId).SingleOrDefaultAsync(token);
            if (preparer is { } preparedBy)
            {
                humans.Add(preparedBy);
                requiredHumans.Add(preparedBy);
            }
        }
        if (operation is not null || publicationCandidateId is not null)
        {
            var activePreparers = await (
                from selection in context.ModerationPolicySelections.AsNoTracking()
                join candidate in context.ModerationPolicyPublicationCandidates.AsNoTracking()
                    on selection.ActivePublicationId equals (Guid?)candidate.Id
                where selection.HostBindingId == discoveredBinding.Id
                    && (selection.JurisdictionTenantId == null || selection.JurisdictionTenantId == tenantId)
                select candidate.PreparedByHumanId).Distinct().ToListAsync(token);
            foreach (var preparer in activePreparers) humans.Add(preparer);
        }
        if (mutation is AcceptModerationConsumerMeasurementSource measurement)
        {
            var challenge = await context.ModerationConsumerMeasurementChallenges.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == measurement.ChallengeId, token);
            if (challenge is not null)
            {
                humans.Add(challenge.PreparedByHumanId);
                var installed = await context.ModerationConsumerInstallations.AsNoTracking()
                    .SingleOrDefaultAsync(row => row.Id == challenge.InstallationId, token);
                if (installed is not null)
                {
                    humans.Add(installed.InstalledByHumanId);
                    if (installed.WithdrawnByHumanId is { } withdrawer) humans.Add(withdrawer);
                }
            }
            var issuer = await context.ModerationMeasurementIssuerEnrollments.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == measurement.IssuerEnrollmentId, token);
            if (issuer is not null)
            {
                humans.Add(issuer.ApprovedByHumanId);
                if (issuer.RevokedByHumanId is { } revoker) humans.Add(revoker);
            }
        }
        if (mutation is RevokeModerationMeasurementIssuerSource issuerRevocation)
        {
            var enrollment = await context.ModerationMeasurementIssuerEnrollments.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == issuerRevocation.EnrollmentId, token);
            if (enrollment is not null)
            {
                humans.Add(enrollment.ApprovedByHumanId);
                if (enrollment.RevokedByHumanId is { } revoker) humans.Add(revoker);
            }
        }
        if (operation is not null || publicationCandidateId is not null)
        {
            var consumers = await context.ModerationConsumerInstallations.AsNoTracking()
                .Where(row => row.HostBindingId == discoveredBinding.Id
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
                // Accepted assertions discover accountable humans only for these actual installations.
                // Discovery never supplies permission; retained native reads requalify every row.
                var measurements = await (
                    from receipt in context.ModerationConsumerMeasurementReceipts.AsNoTracking()
                    join challenge in context.ModerationConsumerMeasurementChallenges.AsNoTracking()
                        on receipt.ChallengeId equals challenge.Id
                    where installationIds.Contains(challenge.InstallationId)
                        && challenge.HostBindingId == discoveredBinding.Id && challenge.TenantId == tenantId
                        && receipt.HostBindingId == discoveredBinding.Id && receipt.TenantId == tenantId
                    select new { receipt.AcceptedByHumanId, challenge.PreparedByHumanId, receipt.IssuerEnrollmentId })
                    .ToListAsync(token);
                foreach (var acceptedMeasurement in measurements)
                {
                    humans.Add(acceptedMeasurement.AcceptedByHumanId);
                    humans.Add(acceptedMeasurement.PreparedByHumanId);
                }
                var issuerIds = measurements.Select(row => row.IssuerEnrollmentId).Distinct().ToArray();
                foreach (var approver in await context.ModerationMeasurementIssuerEnrollments.AsNoTracking()
                    .Where(row => issuerIds.Contains(row.Id) && row.HostBindingId == discoveredBinding.Id && row.TenantId == tenantId)
                    .Select(row => row.ApprovedByHumanId).ToListAsync(token))
                    humans.Add(approver);
            }
        }
        if (mutation is InstallModerationConsumerSource installConsumer)
        {
            var previous = await context.ModerationConsumerInstallations.AsNoTracking()
                .Where(row => row.HostBindingId == discoveredBinding.Id && row.ConsumerId == installConsumer.ConsumerId
                    && row.ModeCode == (int)installConsumer.Mode)
                .OrderByDescending(row => row.WriterGeneration).FirstOrDefaultAsync(token);
            if (previous is not null)
            {
                humans.Add(previous.InstalledByHumanId);
                if (previous.WithdrawnByHumanId is { } withdrawer) humans.Add(withdrawer);
            }
        }
        var installedConsumerId = mutation switch
        {
            WithdrawModerationConsumerSource source => source.InstallationId,
            PrepareModerationConsumerMeasurementSource source => source.InstallationId,
            _ => (Guid?)null
        };
        if (installedConsumerId is { } consumerInstallationId)
        {
            var installed = await context.ModerationConsumerInstallations.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == consumerInstallationId, token);
            if (installed is not null)
            {
                humans.Add(installed.InstalledByHumanId);
                if (installed.WithdrawnByHumanId is { } withdrawer) humans.Add(withdrawer);
            }
        }
        var proceduralSourceId = mutation switch
        {
            PublishModerationQualificationPolicySource source => (Guid?)source.SourceId,
            AdoptModerationQualificationPolicySource source => source.SourceId,
            RevokeModerationQualificationPolicyAdoption source => source.SourceId,
            _ => null
        };
        if (proceduralSourceId is { } policySourceId)
        {
            var source = await context.ModerationQualificationPolicySources.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == policySourceId, token);
            if (source is not null)
            {
                humans.Add(source.PublishedByHumanId);
                if (mutation is AdoptModerationQualificationPolicySource)
                    requiredHumans.Add(source.PublishedByHumanId);
            }
        }
        if (mutation is RevokeModerationQualificationPolicyAdoption revokePolicy)
        {
            var adoption = await context.ModerationQualificationPolicyAdoptions.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == revokePolicy.AdoptionId, token);
            if (adoption is not null)
            {
                humans.Add(adoption.AdoptedByHumanId);
                if (adoption.RevokedByHumanId is { } revoker)
                    humans.Add(revoker);
            }
        }
        if (mutation is AdoptModerationProfileSource adoptProfile)
        {
            var selection = await context.ModerationContributionSelections.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == adoptProfile.SelectionId, token);
            if (selection is not null)
            {
                humans.Add(selection.AuthoredByHumanId);
                requiredHumans.Add(selection.AuthoredByHumanId);
                var decision = await context.ModerationDecisions.AsNoTracking()
                    .SingleOrDefaultAsync(row => row.Id == selection.DecisionId, token);
                if (decision?.IssuerKind == ModerationSubjectKind.Human) humans.Add(decision.IssuerId);
            }
        }
        if (mutation is RevokeModerationProfileSource revokeProfile)
        {
            var adoption = await context.ModerationProfileAdoptions.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == revokeProfile.AdoptionId, token);
            if (adoption is not null)
            {
                humans.Add(adoption.AdoptedByHumanId);
                if (adoption.RevokedByHumanId is { } revoker) humans.Add(revoker);
            }
        }
        if (mutation is AdoptModerationEventTargetSource adoptEvent)
        {
            var selection = await context.ModerationContributionSelections.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == adoptEvent.SelectionId, token);
            if (selection is not null)
            {
                humans.Add(selection.AuthoredByHumanId);
                requiredHumans.Add(selection.AuthoredByHumanId);
                var decision = await context.ModerationDecisions.AsNoTracking()
                    .SingleOrDefaultAsync(row => row.Id == selection.DecisionId, token);
                if (decision?.IssuerKind == ModerationSubjectKind.Human) humans.Add(decision.IssuerId);
            }
            var generation = await context.ModerationEventGenerations.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == adoptEvent.GenerationId, token);
            if (generation is not null)
            {
                retainedEventGeneration = generation;
                humans.Add(generation.ObservedByHumanId);
                requiredHumans.Add(generation.ObservedByHumanId);
            }
        }
        if (mutation is RevokeModerationEventTargetSource revokeEvent)
        {
            var adoption = await context.ModerationEventTargetAdoptions.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == revokeEvent.AdoptionId, token);
            if (adoption is not null)
            {
                humans.Add(adoption.AdoptedByHumanId);
                if (adoption.RevokedByHumanId is { } revoker) humans.Add(revoker);
            }
        }
        if (mutation is AdoptModerationResourceTargetSource adoptResource)
        {
            var selection = await context.ModerationContributionSelections.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == adoptResource.SelectionId, token);
            if (selection is not null)
            {
                humans.Add(selection.AuthoredByHumanId);
                requiredHumans.Add(selection.AuthoredByHumanId);
                var decision = await context.ModerationDecisions.AsNoTracking()
                    .SingleOrDefaultAsync(row => row.Id == selection.DecisionId, token);
                if (decision?.IssuerKind == ModerationSubjectKind.Human) humans.Add(decision.IssuerId);
            }
            var generation = await context.ModerationResourceGenerations.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == adoptResource.GenerationId, token);
            if (generation is not null)
            {
                retainedResourceGeneration = generation;
                humans.Add(generation.ObservedByHumanId);
                requiredHumans.Add(generation.ObservedByHumanId);
            }
        }
        if (mutation is RevokeModerationResourceTargetSource revokeResource)
        {
            var adoption = await context.ModerationResourceTargetAdoptions.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == revokeResource.AdoptionId, token);
            if (adoption is not null)
            {
                humans.Add(adoption.AdoptedByHumanId);
                if (adoption.RevokedByHumanId is { } revoker) humans.Add(revoker);
            }
        }
        if (mutation is RevokeModerationNoticeDisclosureSource revokeDisclosure)
        {
            var approval = await context.ModerationNoticeDisclosureApprovals.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == revokeDisclosure.ApprovalId, token);
            if (approval is not null)
            {
                humans.Add(approval.ApprovedByHumanId);
                if (approval.RevokedByHumanId is { } revoker) humans.Add(revoker);
            }
        }
        if (mutation is ApproveModerationNoticeDisclosureSource contentDisclosure)
        {
            if (contentDisclosure.Intent.ContentRepresentatives.IsDefault
                || contentDisclosure.Intent.ContentRepresentatives.Any(row => row is null))
                return null;
            foreach (var representative in contentDisclosure.Intent.ContentRepresentatives)
            {
                humans.Add(representative.HumanId);
                requiredHumans.Add(representative.HumanId);
            }
        }
        switch (mutation)
        {
            case OpenModerationCaseSource { SubjectKind: ModerationSubjectKind.Human } source:
                humans.Add(source.SubjectId);
                requiredHumans.Add(source.SubjectId);
                break;
            case AssignModerationReviewerSource source:
                humans.Add(source.ReviewerHumanId);
                requiredHumans.Add(source.ReviewerHumanId);
                break;
            case QualifyModerationReviewerController source:
                humans.Add(source.ControllerHumanId);
                requiredHumans.Add(source.ControllerHumanId);
                await DiscoverReviewerAsync(source.AssignmentId, humans, requiredHumans, true, false, token);
                break;
            case AssessModerationReviewerConflict source:
                await DiscoverReviewerAsync(source.AssignmentId, humans, requiredHumans, true, true, token);
                break;
            case WithdrawModerationReviewerSource source:
                await DiscoverReviewerAsync(source.AssignmentId, humans, requiredHumans, false, false, token,
                    requireCurrentCase: false);
                break;
            case FinalizeModerationFindingSource source:
                var assignment = await context.ModerationReviewerAssignments.AsNoTracking()
                    .SingleOrDefaultAsync(row => row.CaseId == source.CaseId
                        && row.ReviewerHumanId == userId && row.WithdrawnAt == null, token);
                if (assignment is not null)
                    await DiscoverReviewerAsync(assignment.Id, humans, requiredHumans, true, true, token);
                var evidence = await context.ModerationQualificationEvidence.AsNoTracking()
                    .SingleOrDefaultAsync(row => row.Id == source.EvidenceId, token);
                if (evidence?.Purpose == ModerationQualificationEvidencePurpose.Urgency)
                {
                    humans.Add(evidence.AssertedByHumanId);
                    requiredHumans.Add(evidence.AssertedByHumanId);
                }
                break;
            case ReverseModerationFindingSource source:
                var retained = await context.ModerationFindings.AsNoTracking()
                    .SingleOrDefaultAsync(row => row.Id == source.FindingId, token);
                if (retained is not null)
                {
                    humans.Add(retained.AdjudicatorId);
                    if (retained.SubjectKind == ModerationSubjectKind.Human)
                    {
                        humans.Add(retained.SubjectId);
                    }
                }
                break;
        }
        var mutationCaseId = mutation switch
        {
            AdoptModerationProfileSource source => (Guid?)source.CaseId,
            RevokeModerationProfileSource source => source.CaseId,
            AdoptModerationEventTargetSource source => (Guid?)source.CaseId,
            RevokeModerationEventTargetSource source => source.CaseId,
            AdoptModerationResourceTargetSource source => (Guid?)source.CaseId,
            RevokeModerationResourceTargetSource source => source.CaseId,
            RecordModerationContributionSelectionSource source => (Guid?)source.CaseId,
            ApproveModerationNoticeDisclosureSource source => (Guid?)source.Intent.CaseId,
            ApproveModerationNoticeCorrectionDisclosureSource source => (Guid?)source.Intent.CaseId,
            RevokeModerationNoticeDisclosureSource source => source.CaseId,
            PublishModerationQualificationPolicySource source => (Guid?)source.CaseId,
            AdoptModerationQualificationPolicySource source => source.CaseId,
            RevokeModerationQualificationPolicyAdoption source => source.CaseId,
            RecordModerationCaseResponse response => (Guid?)response.CaseId,
            AssignModerationReviewerSource reviewer => reviewer.CaseId,
            QualifyModerationCaseUrgency urgency => urgency.CaseId,
            FinalizeModerationFindingSource finding => finding.CaseId,
            _ => null
        };
        if (mutationCaseId is { } actualMutationCase)
            await DiscoverCaseSubjectAsync(actualMutationCase, humans, requiredHumans, token,
                requireCurrentAttribution: !IsRetainedCaseCorrection(mutation));
        if (operation is not null)
        {
            var retainedCorrection = operation.Kind is ModerationOperationKind.Revoke
                or ModerationOperationKind.Reject or ModerationOperationKind.Withdraw;
            var selection = await context.ModerationContributionSelections.AsNoTracking()
                .SingleOrDefaultAsync(row => row.EffectId == operation.EffectId, token);
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
                .SingleOrDefaultAsync(row => row.EffectId == operation.EffectId, token);
            if (disclosure is not null)
            {
                humans.Add(disclosure.ApprovedByHumanId);
                if (disclosure.RevokedByHumanId is { } revoker) humans.Add(revoker);
                foreach (var representative in disclosure.Intent.ContentRepresentatives)
                {
                    humans.Add(representative.HumanId);
                    if (!retainedCorrection) requiredHumans.Add(representative.HumanId);
                }
            }
            if (operation.Subject.Kind == ModerationSubjectKind.Human)
            {
                humans.Add(operation.Subject.Id);
                if (!retainedCorrection) requiredHumans.Add(operation.Subject.Id);
            }
            var discoveredDecision = operation.Kind == ModerationOperationKind.Propose ? null
                : await context.ModerationDecisions.AsNoTracking()
                    .SingleOrDefaultAsync(row => row.Id == operation.DecisionId, token);
            var caseId = operation.Proposal?.CaseId ?? discoveredDecision?.CaseId;
            if (!retainedCorrection) preFindingCaseId = caseId;
            if (discoveredDecision is not null)
            {
                if (discoveredDecision.IssuerKind == ModerationSubjectKind.Human)
                    humans.Add(discoveredDecision.IssuerId);
                foreach (var approval in discoveredDecision.Approvals)
                    humans.Add(approval.Id);
            }
            var findingId = operation.Kind == ModerationOperationKind.AcceptFinding
                ? operation.ReferenceId : discoveredDecision?.FindingId;
            if (findingId is { } requestedFindingId)
            {
                var finding = await context.ModerationFindings.AsNoTracking()
                    .SingleOrDefaultAsync(row => row.Id == requestedFindingId, token);
                if (finding is not null)
                {
                    humans.Add(finding.AdjudicatorId);
                    if (!retainedCorrection) requiredHumans.Add(finding.AdjudicatorId);
                    var findingEvidence = await context.ModerationQualificationEvidence.AsNoTracking()
                        .SingleOrDefaultAsync(row => row.Id == finding.EvidenceReferenceId, token);
                    if (findingEvidence is not null)
                    {
                        humans.Add(findingEvidence.AssertedByHumanId);
                        if (!retainedCorrection) requiredHumans.Add(findingEvidence.AssertedByHumanId);
                    }
                }
            }
            if (caseId is { } actualCaseId)
            {
                await DiscoverCaseSubjectAsync(actualCaseId, humans, requiredHumans, token,
                    requireCurrentAttribution: !retainedCorrection);
                var assignments = await context.ModerationReviewerAssignments.AsNoTracking()
                    .Where(row => row.CaseId == actualCaseId && row.WithdrawnAt == null)
                    .Select(row => row.Id).ToListAsync(token);
                foreach (var id in assignments)
                    await DiscoverReviewerAsync(id, humans, requiredHumans, false, false, token,
                        requireCurrentCase: !retainedCorrection);
            }
        }

        var measuredCandidate = mutation is RecordModerationMeasuredOperationsSource or AcceptModerationMeasuredOperationsSource
            or RecordModerationClassificationTaxonomySource or AdoptModerationClassificationTaxonomySource
            or RecordModerationClassificationMaterialSource or AcceptModerationClassificationMaterialSource
            or RecordModerationClassificationControlSelection or AdoptModerationClassificationEventTarget
            or RevokeModerationClassificationEventTarget
            or RecordModerationProfileCollectionProcedureSource or AdoptModerationProfileCollectionProcedureSource
            or WithdrawModerationProfileCollectionProcedureAdoption
            ? candidateId : publicationCandidateId;
        await DiscoverDeploymentHumansAsync(mutation, operation, measuredCandidate, candidateId,
            discoveredBinding.Id, tenantId, humans, token);
        await DiscoverConstitutionHumansAsync(mutation, operation, measuredCandidate, candidateId,
            discoveredBinding.Id, tenantId, humans, token);
        await DiscoverRecognitionPrivacyHumansAsync(mutation, operation, measuredCandidate, candidateId,
            discoveredBinding.Id, tenantId, humans, token);
        await DiscoverChildAgePolicyHumansAsync(mutation, operation, measuredCandidate, candidateId,
            discoveredBinding.Id, tenantId, humans, token);
        await DiscoverProfileCollectionProcedureHumansAsync(mutation, operation, measuredCandidate, candidateId,
            discoveredBinding.Id, tenantId, humans, token);
        await DiscoverClassificationTaxonomyHumansAsync(mutation, operation, measuredCandidate, candidateId,
            discoveredBinding.Id, tenantId, humans, token);
        await DiscoverClassificationMaterialHumansAsync(mutation, operation, measuredCandidate, candidateId,
            discoveredBinding.Id, tenantId, humans, token);
        var materialGenerations = operation is not null || measuredCandidate is not null || candidateId is not null
            || mutation is RecordModerationClassificationMaterialSource or AcceptModerationClassificationMaterialSource
                or RecordModerationClassificationControlSelection or AdoptModerationClassificationEventTarget
            ? await EventAuthorityProviderOperations.ReadMaterialGenerationPlansAsync(context,
                discoveredBinding.Id, tenantId, token, (mutation as RecordModerationClassificationMaterialSource)?.EventGenerationId)
            : System.Collections.Immutable.ImmutableDictionary<Guid, EventAuthorityProviderOperations.Ownership>.Empty;
        await DiscoverClassificationControlHumansAsync(mutation, operation, measuredCandidate, candidateId,
            discoveredBinding.Id, tenantId, humans, token);
        await DiscoverEventRightsHumansAsync(mutation, operation, publicationCandidateId, candidateId,
            discoveredBinding.Id, tenantId, humans, token);
        await DiscoverMeasuredOperationsHumansAsync(mutation, operation, publicationCandidateId, candidateId,
            discoveredBinding.Id, tenantId, humans, token);
        await DiscoverCaseNarrativeHumansAsync(mutation, narrativeKind, narrativePurpose,
            discoveredBinding.Id, tenantId, humans, token);
        await DiscoverOperationalBudgetHumansAsync(mutation,
            narrativeKind is not null && narrativePurpose == ModerationCaseNarrativePurpose.ConfidentialAllegation
                ? ModerationOperationalAction.SubmitReport : null,
            discoveredBinding.Id, tenantId, humans, token);
        await DiscoverParticipantInvestigationHumansAsync(mutation,
            discoveredBinding.Id, tenantId, humans, requiredHumans, token);
        if (responseReview is { } review)
        {
            humans.UnionWith(review.Representatives);
            requiredHumans.UnionWith(review.Representatives);
            var reviewerIds = await context.ModerationReviewerAssignments.AsNoTracking()
                .Where(row => row.CaseId == review.CaseId && row.ReviewerHumanId == userId
                    && row.WithdrawnAt == null)
                .Select(row => row.Id).ToArrayAsync(token);
            foreach (var id in reviewerIds)
                await DiscoverReviewerAsync(id, humans, requiredHumans, true, true, token);
            var policyHumans = await (
                from source in context.ModerationQualificationPolicySources.AsNoTracking()
                join adoption in context.ModerationQualificationPolicyAdoptions.AsNoTracking()
                    on source.Id equals adoption.SourceId
                where source.HostBindingId == discoveredBinding.Id && source.CaseId == review.CaseId
                select new { source.PublishedByHumanId, adoption.AdoptedByHumanId }).ToArrayAsync(token);
            foreach (var source in policyHumans)
            {
                humans.Add(source.PublishedByHumanId);
                humans.Add(source.AdoptedByHumanId);
            }
        }
        var originalAnchor = mutation switch
        {
            PrepareModerationConsumerMeasurementSource preparation => preparation.OriginalEffect,
            AcceptModerationConsumerMeasurementSource acceptance => acceptance.OriginalEffect,
            RecordModerationConstitutionSource constitution => constitution.OriginalEffect,
            AdoptModerationConstitutionSource adoption => adoption.OriginalEffect,
            RecordModerationMeasuredOperationsSource measuredSource => measuredSource.OriginalEffect,
            AcceptModerationMeasuredOperationsSource originalMeasuredAcceptance => originalMeasuredAcceptance.OriginalEffect,
            _ => null
        };
        if (originalAnchor is not null)
            await DiscoverOriginalMeasurementHumansAsync(originalAnchor, discoveredBinding.Id, tenantId, humans, token);
        if (preFindingDiscovery is null && preFindingCaseId is { } responseCaseId)
            preFindingDiscovery = await DiscoverPreFindingHumansAsync(context, responseCaseId, [], token);
        if (preFindingDiscovery is not null) humans.UnionWith(preFindingDiscovery.Humans);
        var materialAuthority = materialGenerations.Count == 0 ? null
            : await EventAuthorityProviderOperations.ReadPlanAsync(context, tenantId, humans.ToArray(),
                materialGenerations.Values.Select(row => row.Id).Distinct().ToArray(), token);
        foreach (var id in humans.Order())
            await RelationalEntityRowFence.AcquireGlobalAsync<User>(context, id, token);
        var humanIds = humans.ToArray();
        var requiredIds = requiredHumans.ToArray();
        if (await context.Users.AsNoTracking().CountAsync(row => requiredIds.Contains(row.Id) && !row.IsDeleted, token)
            != requiredIds.Length)
            return null;

        var logins = await context.UserExternalLogins.AsNoTracking().Where(row => humanIds.Contains(row.UserId))
            .OrderBy(row => row.Id).Select(row => row.Id).ToListAsync(token);
        foreach (var id in logins)
            await RelationalEntityRowFence.AcquireGlobalAsync<UserExternalLogin>(context, id, token);

        var platformGrants = await context.PlatformUserRoles.AsNoTracking().Where(row => humanIds.Contains(row.UserId))
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.RoleId }).ToListAsync(token);
        foreach (var grant in platformGrants)
            await RelationalEntityRowFence.AcquireGlobalAsync<PlatformUserRole>(context, grant.Id, token);

        var memberships = await context.TenantUsers.AsNoTracking()
            .Where(row => humanIds.Contains(row.UserId) && row.TenantId == tenantId)
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.StatusId }).ToListAsync(token);
        foreach (var membership in memberships)
            await RelationalEntityRowFence.AcquireAsync<TenantUser>(context, tenantId, row => row.Id, membership.Id, token);
        var membershipIds = memberships.Select(row => row.Id).ToArray();
        var tenantGrants = await context.TenantUserRoleGrants.AsNoTracking()
            .Where(row => membershipIds.Contains(row.TenantUserId))
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.RoleId }).ToListAsync(token);
        foreach (var grant in tenantGrants)
            await RelationalEntityRowFence.AcquireAsync<TenantUserRoleGrant>(context, tenantId, row => row.Id, grant.Id, token);
        if (protectedRead is not null && (protectedRead.IssuerPlatformGrant
            ? !platformGrants.Any(row => row.Id == protectedRead.IssuerRoleGrantId)
            : !tenantGrants.Any(row => row.Id == protectedRead.IssuerRoleGrantId))) return null;
        var roleIds = platformGrants.Select(row => row.RoleId).Concat(tenantGrants.Select(row => row.RoleId))
            .Concat(nativeTargets?.RepresentativeRoles ?? [])
            .Concat(disclosureAuthority?.Assignments.Select(row => row.RoleId) ?? [])
            .Concat(preFindingDiscovery?.RepresentativeAuthority?.Assignments.Select(row => row.RoleId) ?? [])
            .Concat(materialAuthority?.Assignments.Select(row => row.RoleId) ?? []).Distinct().Order().ToArray();
        foreach (var roleId in roleIds)
            await RelationalEntityRowFence.AcquireLookupAsync<Role>(context, [roleId], token);
        await Explore.Persistence.Services.EventAuthoritySnapshotService.AcquirePermissionSourcesAsync(context,
            (nativeTargets?.RepresentativeRoles ?? []).Concat(materialAuthority?.Assignments.Select(row => row.RoleId) ?? [])
                .Concat(disclosureAuthority?.Assignments.Select(row => row.RoleId) ?? [])
                .Concat(preFindingDiscovery?.RepresentativeAuthority?.Assignments.Select(row => row.RoleId) ?? [])
                .Distinct().Order().ToArray(), token);
        if (await admin.ResolveUserIdAsync(token) != userId)
            return null;

        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationHostBinding>(context, discoveredBinding.Id, token);
        var binding = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
        if (binding is null || binding.Id != discoveredBinding.Id
            || binding.TrustDomainId != discoveredBinding.TrustDomainId
            || binding.SolutionId != discoveredBinding.SolutionId || binding.InstanceId != discoveredBinding.InstanceId
            || protectedRead is not null && binding.MaintenanceServicePrincipalId != protectedRead.MaintenanceServicePrincipalId)
            return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ServicePrincipal>(context, binding.MaintenanceServicePrincipalId, token);
        if (!await context.ServicePrincipals.AsNoTracking().AnyAsync(
            row => row.Id == binding.MaintenanceServicePrincipalId && !row.IsDeleted, token))
            return null;

        EventAuthorityProviderOperations.Ownership? eventGenerationPlan = null;
        Guid? eventGenerationPlanSourceId = null;
        EventAuthorityProviderOperations.ResourceAncestry? resourceGenerationPlan = null;
        Guid? resourceGenerationPlanSourceId = null;
        if (mutation is RecordModerationEventGenerationSource eventSource)
        {
            var plan = await EventAuthorityProviderOperations.ReadPlanAsync(
                context, tenantId, [], [eventSource.EventId], token);
            // Fresh writes require this plan; an accepted receipt does not require live ancestry.
            eventGenerationPlan = plan.Parents.SingleOrDefault();
            eventGenerationPlanSourceId = eventSource.SourceId;
        }
        else if (retainedEventGeneration is { } retained
            && retained.HostBindingId == binding.Id && retained.TrustDomainId == binding.TrustDomainId
            && retained.TenantId == tenantId && retained.EventId != Guid.Empty && retained.ActorId != Guid.Empty
            && retained.OrganizerActorId != Guid.Empty)
        {
            // Retained ancestry proposes fences; live Event existence is not required for receipt replay.
            eventGenerationPlan = new(retained.EventId, retained.ActorId, retained.OrganizerActorId);
            eventGenerationPlanSourceId = retained.Id;
        }
        if (mutation is RecordModerationResourceGenerationSource resourceSource)
        {
            // Missing live ancestry must not veto an already accepted receipt.
            resourceGenerationPlan = await EventAuthorityProviderOperations.ReadResourcePlanAsync(
                context, tenantId, resourceSource.ResourceId, token);
            resourceGenerationPlanSourceId = resourceSource.SourceId;
        }
        else if (retainedResourceGeneration is { } retainedResource
            && retainedResource.HostBindingId == binding.Id && retainedResource.TrustDomainId == binding.TrustDomainId
            && retainedResource.TenantId == tenantId && retainedResource.ResourceId != Guid.Empty
            && retainedResource.EventId != Guid.Empty && retainedResource.ActorId != Guid.Empty
            && retainedResource.OrganizerActorId != Guid.Empty && retainedResource.StorageObjectId != Guid.Empty)
        {
            resourceGenerationPlan = await EventAuthorityProviderOperations.ReadResourcePlanAsync(context, retainedResource, token);
            resourceGenerationPlanSourceId = retainedResource.Id;
        }
        var instance = AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(binding.TrustDomainId), binding.SolutionId),
            binding.InstanceId);
        var tenantScope = AuthorityScope.Tenant(instance, tenantId);
        // Terminal disclosure checks retain the original parent plan, including its absence.
        var contentTargetCasePlan = borrowedDisclosureAuthority is not null
            ? borrowedDisclosureAuthority.ContentTargetCase
            : mutation is not null
                && mutation.Target.Kind is (ModerationScopeKind.Resource or ModerationScopeKind.Event)
                && IsContentResourceCaseMutation(mutation) && !IsRetainedCaseCorrection(mutation)
                ? await DiscoverContentTargetCasePlanAsync(mutation, tenantScope, token) : null;
        // Ordinary Actor source saves take Actor before Tenant, including stored-file owners.
        foreach (var actorId in new Guid?[]
        {
            eventGenerationPlan?.ActorId, eventGenerationPlan?.OrganizerActorId,
            resourceGenerationPlan?.ActorId, resourceGenerationPlan?.OrganizerActorId, resourceGenerationPlan?.StorageActorId,
            contentTargetCasePlan?.Parents.ActorId, contentTargetCasePlan?.Parents.OrganizerActorId
        }.OfType<Guid>().Concat(nativeTargets?.Actors ?? [])
            .Concat(materialGenerations.Values.SelectMany(row => new Guid?[] { row.ActorId, row.OrganizerActorId }).OfType<Guid>())
            .Concat((materialAuthority?.Parents ?? []).SelectMany(row => new Guid?[] { row.ActorId, row.OrganizerActorId }).OfType<Guid>())
            .Concat((disclosureAuthority?.Parents ?? []).SelectMany(row => new Guid?[] { row.ActorId, row.OrganizerActorId }).OfType<Guid>())
            .Concat((preFindingDiscovery?.RepresentativeAuthority?.Parents ?? []).SelectMany(row => new Guid?[] { row.ActorId, row.OrganizerActorId }).OfType<Guid>())
            .Concat(new[] { protectedRead?.PublisherActorId, protectedRead?.OrganizerActorId }.OfType<Guid>())
            .Distinct().Order())
            await RelationalEntityRowFence.AcquireGlobalAsync<Actor>(context, actorId, token);

        await RelationalEntityRowFence.AcquireGlobalAsync<Tenant>(context, tenantId, token);
        var tenant = await context.Tenants.AsNoTracking().SingleOrDefaultAsync(row => row.Id == tenantId, token);
        if (tenant is null)
            return null;
        await RelationalEntityRowFence.AcquireLookupAsync<TenantStatus>(context, [tenant.TenantStatusId], token);
        if (!await context.Tenants.AsNoTracking().AnyAsync(
            row => row.Id == tenantId && (row.TenantStatus.IsActiveState || allowInactiveTenant), token))
            return null;
        var discoveredEpoch = await context.ModerationQualificationEpochs.AsNoTracking()
            .SingleOrDefaultAsync(row => row.HostBindingId == binding.Id && row.TenantId == tenantId, token);
        if (discoveredEpoch is null)
            return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationEpoch>(context, discoveredEpoch.Id, token);
        var epoch = await context.ModerationQualificationEpochs
            .SingleOrDefaultAsync(row => row.Id == discoveredEpoch.Id, token);
        if (epoch is null || epoch.Revision < 1)
            return null;

        var platformIds = platformGrants.Select(row => row.Id).ToArray();
        var instanceAdministrators = await context.PlatformUserRoles.AsNoTracking().Where(row =>
            platformIds.Contains(row.Id) && roleIds.Contains(row.RoleId) && !row.User.IsDeleted
            && row.Role.RoleScopeId == (int)RoleScopeEnum.Platform && row.Role.MasterCode == "platform.admin")
            .Select(row => row.UserId).Distinct().ToListAsync(token);
        var isInstanceAdmin = instanceAdministrators.Contains(userId.Value);
        var tenantIds = tenantGrants.Select(row => row.Id).ToArray();
        var tenantAdministrators = await context.TenantUserRoleGrants.AsNoTracking().Where(row =>
            tenantIds.Contains(row.Id) && roleIds.Contains(row.RoleId) && membershipIds.Contains(row.TenantUserId)
            && row.TenantId == tenantId && row.RevokedAt == null && row.RoleScopeId == (int)RoleScopeEnum.Tenant
            && row.TenantUser.StatusId == (int)TenantUserStatusEnum.Active
            && !row.TenantUser.IsDeleted && !row.TenantUser.User.IsDeleted
            && row.RoleId == (int)RoleEnum.TenantAdmin && row.Role.RoleScopeId == (int)RoleScopeEnum.Tenant)
            .Select(row => row.TenantUser.UserId).Distinct().ToListAsync(token);
        var isTenantAdmin = tenantAdministrators.Contains(userId.Value);
        if (!(mutation is null ? tenantScope.Contains(target) : target == tenantScope)
            && (target != instance || !isInstanceAdmin && requireAdministrator))
            return null;
        if (requireAdministrator && !isInstanceAdmin && !isTenantAdmin)
            return null;

        var administrators = target == instance ? instanceAdministrators.ToArray()
            : instanceAdministrators.Concat(tenantAdministrators).Distinct().ToArray();
        return new ModerationSourceAuthority(userId.Value, binding, epoch, instance, tenantScope,
            isInstanceAdmin, humanIds, administrators)
        {
            EventGenerationPlan = eventGenerationPlan,
            EventGenerationPlanSourceId = eventGenerationPlanSourceId,
            ResourceGenerationPlan = resourceGenerationPlan,
            ResourceGenerationPlanSourceId = resourceGenerationPlanSourceId,
            ContentTargetCase = contentTargetCasePlan,
            NativeTargets = nativeTargets,
            MaterialGenerations = materialGenerations,
            MaterialAuthority = materialAuthority,
            DisclosureAuthority = disclosureAuthority,
            PreFinding = preFindingDiscovery
        };
    }

    private async Task DiscoverReviewerAsync(
        Guid assignmentId, HashSet<Guid> humans, HashSet<Guid> requiredHumans,
        bool requireReviewer, bool requireController, CancellationToken token,
        bool requireCurrentCase = true)
    {
        var assignment = await context.ModerationReviewerAssignments.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == assignmentId, token);
        if (assignment is null) return;
        await DiscoverCaseSubjectAsync(assignment.CaseId, humans, requiredHumans, token,
            requireCurrentAttribution: requireCurrentCase);
        humans.Add(assignment.ReviewerHumanId);
        if (requireReviewer)
            requiredHumans.Add(assignment.ReviewerHumanId);
        if (assignment.ControllerHumanId is { } controller)
        {
            humans.Add(controller);
            if (requireController)
                requiredHumans.Add(controller);
        }
        foreach (var assessor in new[] { assignment.QualifiedByHumanId, assignment.AssessedByHumanId })
        {
            if (assessor is not { } id) continue;
            humans.Add(id);
            if (requireController)
                requiredHumans.Add(id);
        }
    }

    private async Task DiscoverCaseSubjectAsync(
        Guid caseId, ISet<Guid> humans, ISet<Guid> requiredHumans, CancellationToken token,
        bool requireCurrentAttribution = true)
    {
        var allegation = await context.ModerationCases.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == caseId, token);
        if (allegation is null) return;
        if (allegation is { SubjectKind: ModerationSubjectKind.Human, SubjectId: { } subjectId })
        {
            humans.Add(subjectId);
            if (requireCurrentAttribution) requiredHumans.Add(subjectId);
        }
        if (!requireCurrentAttribution || allegation.AttributionSourceId is null) return;
        var attribution = await (
            from head in context.ModerationCaseAttributionHeads.AsNoTracking()
            join source in context.ModerationCaseAttributionSources.AsNoTracking() on head.SourceId equals source.Id
            where head.CaseId == caseId && source.CaseId == caseId && source.Generation == head.Generation
            select source).SingleOrDefaultAsync(token);
        if (attribution is null) return;
        humans.Add(attribution.QualifiedByHumanId);
        if (attribution.InvestigationSourceId is { } investigationId)
        {
            var investigationHumans = await ModerationParticipantInvestigationHumanDiscovery.DiscoverAsync(
                context, investigationId, attribution.HostBindingId, attribution.TenantId, token);
            humans.UnionWith(investigationHumans);
            requiredHumans.UnionWith(investigationHumans);
        }
        var grant = await context.ModerationCaseReadGrants.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == attribution.ReadGrantId, token);
        if (grant is not null)
        {
            humans.Add(grant.CallerUserId);
            humans.Add(grant.IssuerUserId);
        }
        var responseHumans = await DiscoverPreFindingHumansAsync(context, caseId, [], token);
        if (responseHumans is not null)
        {
            humans.UnionWith(responseHumans.Humans);
            requiredHumans.UnionWith(responseHumans.Humans);
        }
    }

    public async Task<bool> ProviderTenantMatchesAsync(Guid tenantId, CancellationToken token)
    {
        if (context.TenantFilterTenantId != tenantId || context.IsTenantFilterBypassed)
            return false;
        if (context.Database.ProviderName != RelationalNamedLock.PostgreSqlProvider)
            return true;
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT current_setting('app.current_tenant_id', true)";
        var value = await command.ExecuteScalarAsync(token);
        return Guid.TryParse(value?.ToString(), out var actual) && actual == tenantId;
    }
}

internal sealed record ModerationSourceAuthority(
    Guid UserId, ModerationHostBinding Binding, ModerationQualificationEpoch Epoch,
    AuthorityScope Instance, AuthorityScope Tenant, bool IsInstanceAdmin,
    IReadOnlyList<Guid> FencedHumans, IReadOnlyList<Guid> Administrators) : IModerationSourceProvenance
{
    internal EventAuthorityProviderOperations.Ownership? EventGenerationPlan { get; init; }
    internal Guid? EventGenerationPlanSourceId { get; init; }
    internal EventAuthorityProviderOperations.ResourceAncestry? ResourceGenerationPlan { get; init; }
    internal Guid? ResourceGenerationPlanSourceId { get; init; }
    internal ModerationQualificationAuthority.ContentTargetCasePlan? ContentTargetCase { get; init; }
    internal ModerationNativeTargetPlan? NativeTargets { get; init; }
    internal EventAuthorityProviderOperations.AuthorityPlan? DisclosureAuthority { get; init; }
    internal ModerationQualificationAuthority.PreFindingHumanDiscovery? PreFinding { get; init; }
    public EventAuthorityProviderOperations.AuthorityPlan? MaterialAuthority { get; init; }
    public IReadOnlyDictionary<Guid, EventAuthorityProviderOperations.Ownership> MaterialGenerations { get; init; } =
        System.Collections.Immutable.ImmutableDictionary<Guid, EventAuthorityProviderOperations.Ownership>.Empty;
}

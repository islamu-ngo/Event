using System.Data;
using System.Text.Json;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationOriginalEffectCompletionRepository
{
    private async Task<OriginalProof> ProveAsync(
        ModerationSourceAuthority authority, ModerationEffectIntent effect, ApiKeyPrincipalContext principal,
        ModerationMachineSubjectBinding credential, IDbContextTransaction transaction, CancellationToken token)
    {
        var observedAt = clock.GetUtcNow();
        if (!await CurrentAuthorityAsync(authority, principal, credential, transaction, token))
            throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.CurrentAuthority);
        var envelope = await context.ModerationNoticeSources.AsNoTracking()
            .Where(row => row.EffectId == effect.Id)
            .Select(row => new { row.TenantId, row.RetainUntil, row.SnapshotHash, row.EffectBindingHash })
            .SingleOrDefaultAsync(token);
        if (envelope is null || envelope.TenantId != authority.Tenant.Id
            || envelope.RetainUntil is not { } ceiling || clock.GetUtcNow() >= ceiling
            || effect.OriginalNoticeCleanupId is not null)
            throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.OriginalCustody);
        var readAt = clock.GetUtcNow();
        var source = await context.ModerationNoticeSources.AsNoTracking().SingleOrDefaultAsync(row =>
            row.EffectId == effect.Id && row.TenantId == authority.Tenant.Id && row.RetainUntil > readAt
            && row.SnapshotHash == envelope.SnapshotHash && row.EffectBindingHash == envelope.EffectBindingHash, token);
        if (source is null || !source.Matches(effect) || clock.GetUtcNow() >= ceiling)
            throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.OriginalCustody);
        ModerationNoticeCaptureSnapshot? capture;
        try { capture = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(source.SnapshotJson); }
        catch (JsonException) { throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.OriginalRecipe); }
        if (capture is null || !capture.Matches(effect) || capture.HasNoSubstantiveWork
            || capture.HostBindingId != authority.Binding.Id || capture.WriterTenantId != authority.Tenant.Id
            || capture.HostSolutionId != authority.Binding.SolutionId || capture.HostInstanceId != authority.Binding.InstanceId
            || capture.AuthoritySourceId != authority.Epoch.Id || capture.QualificationRevision > authority.Epoch.Revision
            || capture.ValidUntil is not { } captureUntil || captureUntil <= observedAt
            || capture.Finding is not { State: ModerationFindingState.Final }
            || capture.Decision.State != ModerationDecisionState.Active || capture.Decision.ApprovalCount < 1
            || capture.Notice is not { } notice || notice.ReferenceProfile != ModerationReferenceProfileId.CFG09
            || notice.SubjectKind != ModerationSubjectKind.Content || notice.RetainUntil != ceiling
            || notice.SubjectOrigin != ModerationNoticeScope.From(authority.Tenant)
            || notice.Effects.IsDefaultOrEmpty || notice.Effects.Length > 64
            || notice.Consequences.IsDefaultOrEmpty || notice.Consequences.Length > 64
            || effect.OperationKind is not ((int)ModerationOperationKind.Activate)
                and not ((int)ModerationOperationKind.Reconcile) and not ((int)ModerationOperationKind.Supersede)
            || notice.OperationKind != effect.OperationKind
            || notice.DecisionState != capture.Decision.State || notice.SanctionState != ModerationSanctionState.Active)
            throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.OriginalRecipe);

        var decision = await context.ModerationDecisions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == effect.DecisionId, token);
        if (decision is null || decision.CaseId != capture.Decision.CaseId
            || decision.Subject.Kind != ModerationSubjectKind.Content || decision.Subject.Id != notice.SubjectId
            || decision.Subject.Origin != authority.Tenant || decision.Scope.Kind != ModerationScopeKind.Resource
            || decision.Scope.Parent != authority.Tenant || ModerationNoticeScope.From(decision.Scope) != notice.Scope
            || notice.Scope != capture.Decision.Scope || notice.SubjectId != capture.Decision.SubjectId
            || notice.SubjectKind != capture.Decision.SubjectKind || notice.SubjectOrigin != capture.Decision.SubjectOrigin
            || notice.Rule != capture.Decision.Rule
            || notice.Rule != new ModerationNoticeRule(decision.Rule.OwnerId, decision.Rule.Code, decision.Rule.Revision)
            || decision.CapabilityMask != capture.Decision.CapabilityMask
            || !decision.Capabilities.SequenceEqual([ModerationCapability.ReadResource])
            || decision.StartsAt != capture.Decision.StartsAt || decision.EndsAt != capture.Decision.EndsAt
            || decision.ReviewAt != capture.Decision.ReviewAt)
            throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.OriginalRecipe);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationCase>(context, decision.CaseId, token);
        var allegation = await context.ModerationCases.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == decision.CaseId, token);
        var finding = allegation is null ? null : await ModerationOriginalFindingProofOwner.AcquireAsync(
            context, allegation, capture.Finding, decision.Scope, authority.Binding, source.CapturedAt, token);
        if (allegation is null || finding is null || finding.Id != decision.FindingId
            || allegation.Subject != decision.Subject)
            throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.OriginalRecipe);

        await using var selectionSlot = await RelationalNamedLock.AcquireTransactionAsync(
            context, $"moderation.contribution:{authority.Binding.Id:N}:{effect.Id:N}", token);
        var selection = await context.ModerationContributionSelections.AsNoTracking().Include(row => row.Bindings)
            .SingleOrDefaultAsync(row => row.EffectId == effect.Id, token)
            ?? throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.OriginalRecipe);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationContributionSelection>(context, selection.Id, token);
        selection.ValidatePersistedState();
        if (selection.HostBindingId != authority.Binding.Id || selection.TrustDomainId != effect.TrustDomainId
            || selection.TenantId != authority.Tenant.Id || selection.DecisionId != effect.DecisionId
            || selection.DecisionRevision != effect.DecisionRevision || selection.CaseId != allegation.Id
            || selection.CaseRevision != finding.CaseRevision || selection.PolicyId != notice.PolicyId
            || selection.PolicyRevision != effect.PolicyRevision || selection.PublicationId != notice.PublicationId
            || selection.ProfileId != notice.ProfileId || selection.DecisionId != notice.ContributionId
            || selection.StartsAt != capture.Decision.StartsAt || selection.RecordedAt > source.CapturedAt
            || selection.ValidUntil <= observedAt || selection.WriterGeneration > effect.QualificationRevision
            || !authority.Administrators.Contains(selection.AuthoredByHumanId))
            throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.OriginalRecipe);
        var contribution = ModerationContributionSelectionProjection.Restore(selection, decision);
        if (ModerationNoticeSnapshotFactory.SelectionDigest(contribution) != selection.SelectionDigest)
            throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.OriginalRecipe);

        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationPolicyPublication>(context, notice.PublicationId, token);
        var publication = await ModerationPolicyPublicationGraph.Include(context.ModerationPolicyPublications.AsNoTracking())
            .SingleOrDefaultAsync(row => row.Id == notice.PublicationId, token);
        if (publication is null || publication.HostBindingId != authority.Binding.Id
            || publication.TrustDomainId != effect.TrustDomainId || publication.TenantId != authority.Tenant.Id
            || publication.PolicyId != notice.PolicyId || publication.Revision != notice.PolicyRevision
            || publication.Revision != effect.PolicyRevision || publication.CatalogueRevision != notice.CatalogueRevision
            || !publication.Jurisdiction.Contains(decision.Scope) || publication.PublishedAt > source.CapturedAt
            || notice.ProfileSource != new ModerationNoticeSourceStamp(publication.Id, publication.Revision, publication.WriterGeneration))
            throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.OriginalRecipe);
        var policy = ModerationPolicyPublicationProjection.RestoreHistory(publication);
        var profile = policy?.Profiles.SingleOrDefault(row => row.Definition.Id == selection.ProfileId);
        if (policy is null || profile is null || profile.Definition.Rule != decision.Rule
            || profile.Definition.ReferenceProfile != notice.ReferenceProfile
            || profile.Definition.ReferenceRevision != notice.ReferenceRevision
            || notice.Reason != new ModerationNoticeRule(profile.Definition.Reason.OwnerId,
                profile.Definition.Reason.Code, profile.Definition.Reason.Revision)
            || profile.Definition.RequiredApprovals > capture.Decision.ApprovalCount
            || profile.Definition.ContestationCalendarMonths != notice.ContestationCalendarMonths)
            throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.OriginalRecipe);
        var composed = ModerationPolicyProposedStateCompiler.ComposeRetained(policy, [contribution], source.CapturedAt);
        if (composed.Outcome != ModerationPolicyCompositionOutcome.Preview
            || composed.Effects.Length != notice.Effects.Length || composed.Consequences.Length != notice.Consequences.Length
            || !composed.Effects.Zip(notice.Effects).All(pair => pair.First.Effect == pair.Second.Effect
                && pair.First.Interval == pair.Second.Interval && SameTarget(pair.First.Target, pair.Second.Target))
            || !composed.Consequences.Zip(notice.Consequences).All(pair => pair.First.Plan == pair.Second.Plan
                && pair.First.Strategy == pair.Second.Strategy && pair.First.ProtectionPurpose == pair.Second.ProtectionPurpose
                && pair.First.Interval == pair.Second.Interval && SameTarget(pair.First.Target, pair.Second.Target))
            || notice.Consequences.Any(row => row.Strategy is not null || row.ProtectionPurpose is not null
                || !OriginalModes(row.Plan)
                || !notice.Effects.Any(effectRow => effectRow.Target == row.Target && effectRow.Interval == row.Interval))
            || notice.Effects.Any(row => !notice.Consequences.Any(plan =>
                plan.Target == row.Target && plan.Interval == row.Interval)))
            throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.OriginalRecipe);
        await using var profileSlot = await RelationalNamedLock.AcquireTransactionAsync(
            context, $"moderation.profile-adoption:{selection.Id:N}", token);
        var adoption = await context.ModerationProfileAdoptions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.SelectionId == selection.Id, token)
            ?? throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.OriginalRecipe);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationProfileAdoption>(context, adoption.Id, token);
        adoption.ValidatePersistedState();
        var gates = policy.Prerequisites.SingleOrDefault(row =>
            row.Rule == decision.Rule && row.Mode == ModerationNativeMode.Embedded)?.ActivationGates;
        if (gates is null || gates.Value.IsDefaultOrEmpty
            || !gates.Value.Contains(ModerationActivationPrerequisite.ConstitutionAndReviewers)
            || !gates.Value.Contains(ModerationActivationPrerequisite.MeasuredOperations)
            || adoption.HostBindingId != authority.Binding.Id || adoption.TenantId != authority.Tenant.Id
            || adoption.TrustDomainId != effect.TrustDomainId || adoption.SelectionRevision != selection.Revision
            || adoption.SelectionWriterGeneration != selection.WriterGeneration || adoption.PublicationId != publication.Id
            || adoption.ProfileId != notice.ProfileId || adoption.ModeCode != (int)ModerationNativeMode.Embedded
            || adoption.CaseId != selection.CaseId || adoption.CaseRevision != selection.CaseRevision
            || adoption.PrerequisiteMask != gates.Value.Aggregate(0, (mask, gate) => mask | 1 << ((int)gate - 1))
            || adoption.Revision != 1 || adoption.RevokedAt is not null || adoption.AdoptedAt > source.CapturedAt
            || adoption.ValidUntil <= observedAt || adoption.ValidUntil > selection.ValidUntil
            || adoption.WriterGeneration > effect.QualificationRevision
            || !authority.Administrators.Contains(adoption.AdoptedByHumanId)
            || adoption.AdoptedByHumanId == selection.AuthoredByHumanId || adoption.AdoptedByHumanId == decision.IssuerId)
            throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.OriginalRecipe);

        var key = await keys.GetByKeyIdForAuthentication(principal.KeyId, token)
            ?? throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.CurrentAuthority);
        var until = new[] { ceiling, captureUntil, selection.ValidUntil, adoption.ValidUntil, effect.LeaseExpiresAt!.Value,
            key.ExpiresAt is { } expires ? new DateTimeOffset(DateTime.SpecifyKind(expires, DateTimeKind.Utc))
                : DateTimeOffset.MaxValue }.Min();
        var facts = new List<string> { source.SnapshotHash, source.EffectBindingHash,
            selection.SelectionDigest, Digest(adoption),
            Digest(new { finding.Id, finding.Revision, finding.CaseId, finding.CaseOccurrenceId,
                finding.CaseCreatedAt, finding.AttributionSourceId, finding.AttributionGeneration }),
            Digest(new
            {
                HostId = authority.Binding.Id, authority.Binding.TrustDomainId,
                authority.Binding.SolutionId, authority.Binding.InstanceId,
                authority.Binding.MaintenanceServicePrincipalId,
                EpochId = authority.Epoch.Id, EpochRevision = authority.Epoch.Revision,
                CredentialId = credential.Id, CredentialRevision = credential.Revision,
                KeyId = key.Id, key.ExpiresAt,
                Humans = authority.FencedHumans.Order().ToArray(),
                Administrators = authority.Administrators.Order().ToArray()
            }) };
        until = await AccountAsync(authority, effect, notice, selection, observedAt, until, facts, token);
        until = await NoticeAsync(authority, effect, source, capture, decision, selection.SelectionDigest,
            observedAt, until, facts, token);
        var originalRecipe = await ModerationOriginalRecipeAuthority.AcquireAsync(context, authority,
            transaction.TransactionId, new(effect.Id, source.SnapshotHash, source.EffectBindingHash),
            privacy, clock, token)
            ?? throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.OriginalRecipe);
        using var session = new ModerationConsumerEvidenceSession(context, transaction, authority, originalRecipe.Policy,
            currentToken => CurrentAuthorityAsync(authority, principal, credential, transaction, currentToken),
            publications, inventory, consumerSources, measurementReceipts, clock, null, originalRecipe);
        until = await ConsumersAsync(session, policy, decision, gates.Value, observedAt, until, facts, token);
        if (!await CurrentAuthorityAsync(authority, principal, credential, transaction, token)
            || clock.GetUtcNow() < observedAt || clock.GetUtcNow() >= until)
            throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.FinalCommitBoundary);
        return new(authority, credential, key.Id, source, capture, selection, until, Digest(facts));
    }

    private static bool SameTarget(ModerationPolicyTarget target, ModerationNoticeTarget original) =>
        ModerationNoticeScope.From(target.Scope) == original.Scope && target.SubjectKind == original.SubjectKind
        && target.SubjectId == original.SubjectId && target.TargetId == original.TargetId
        && target.TargetRevision == original.TargetRevision && target.Operation == original.Operation
        && target.WindowEnd == original.WindowEnd;

    // An exact thirteen-dimensional supported contract, not today's catalogue as a substitute.
    // Preservation modes authorize no mutation to bookings, capacity, credentials or recognition.
    private static bool OriginalModes(ModerationConsequenceModes plan) =>
        plan.Content == ModerationContentMode.QuarantineNamedVersion
        && plan.Management == ModerationManagementMode.CurrentAuthority
        && plan.Resources == ModerationResourceReadMode.QuarantineNamedVersionSafeStatusOnly
        && plan.NewAdmission == ModerationNewAdmissionMode.CurrentEligibility
        && plan.ExistingAdmission == ModerationExistingAdmissionMode.CurrentEligibility
        && plan.PrivateDisclosure == ModerationPrivateDisclosureMode.MaskQuarantinedEmbeddedDetails
        && plan.Settlement == ModerationSettlementMode.NoBookingChange
        && plan.Capacity == ModerationCapacityMode.NoSeatChange
        && plan.Credentials == ModerationCredentialMode.CurrentCredentialLifecycle
        && plan.Notifications == ModerationNotificationMode.DurableSafeSubjectAndAdmin
        && plan.Recognition == ModerationRecognitionMode.NoCaptureFromThisControl
        && plan.PendingWork == ModerationPendingWorkMode.OwnedObjectReviewNeverAutomaticRelease
        && plan.Restoration == ModerationRestorationMode.AuthorizedObjectDecision;
}

using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationQualificationRepository
{
    // Material acquisition requires a clean tracker. Stage only after its last pre-save
    // observation; the existing epoch/receipt save and custody commit remain atomic.
    private ModerationClassificationControlSelection? pendingClassificationSelection;
    private ModerationClassificationEventTargetAdoption? pendingClassificationAdoption;

    private static bool IsClassificationControlMutation(ModerationQualificationMutation request) =>
        request is RecordModerationClassificationControlSelection or AdoptModerationClassificationEventTarget
            or RevokeModerationClassificationEventTarget;

    private ModerationPolicyDefinitionSourceOwner ClassificationControlDefinitions() =>
        new(context, admin, machine, deploymentInventory!, clock);

    private async Task<bool> ClassificationControlAuthorityCurrentAsync(
        ModerationSourceAuthority authority, IDbContextTransaction transaction, CancellationToken token) =>
        await ClassificationMaterialAuthorityCurrentAsync(authority, transaction, token)
        && await RequireClassificationControlAdministratorAsync(authority, authority.UserId, token);

    private async Task<bool> RequireClassificationControlAdministratorAsync(
        ModerationSourceAuthority authority, Guid humanId, CancellationToken token) =>
        deploymentInventory is not null
        && await new ModerationConstitutionSourceOwner(context, ClassificationControlDefinitions(), clock)
            .AdministratorAsync(authority, humanId, authority.Tenant, token)
        && !await context.PrivacyErasureSagas.AsNoTracking().AnyAsync(row => row.SubjectId == humanId, token);

    private async Task<ModerationConsumerEvidenceSession?> AcquireClassificationControlWriteAsync(
        ModerationQualificationMutation request, ModerationSourceAuthority authority, DateTimeOffset now, CancellationToken token)
    {
        if (request is not (RecordModerationClassificationControlSelection or AdoptModerationClassificationEventTarget)) return null;
        pendingClassificationSelection = null;
        pendingClassificationAdoption = null;
        if (deploymentInventory is null || request.Target.Kind != ModerationScopeKind.Event
            || request.Target.Parent != authority.Tenant)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        var transaction = context.Database.CurrentTransaction
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        if (!await ClassificationControlAuthorityCurrentAsync(authority, transaction, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var sourceId = request is RecordModerationClassificationControlSelection recording ? recording.MaterialSourceId
            : (await ClassificationControlGraph().SingleOrDefaultAsync(
                row => row.Id == ((AdoptModerationClassificationEventTarget)request).SelectionId, token)
                ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound)).SourceId;
        var source = await context.Set<ModerationClassificationMaterialSource>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == sourceId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        if (source.HostBindingId != authority.Binding.Id || source.TrustDomainId != authority.Binding.TrustDomainId
            || source.TenantId != authority.Tenant.Id || source.EventId != request.Target.Id)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        var definition = await ClassificationControlDefinitions().ReadDefinitionAsync(authority, transaction.TransactionId,
            authority.Tenant, source.PolicyId, source.PublicationId, now, token);
        if (definition is null || !await ClassificationControlAuthorityCurrentAsync(authority, transaction, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        return new(context, transaction, authority, definition.Policy,
            cancellation => ClassificationControlAuthorityCurrentAsync(authority, transaction, cancellation),
            publications, deploymentInventory, consumerSources, measurementReceipts, clock, null);
    }

    private IQueryable<ModerationClassificationControlSelection> ClassificationControlGraph() =>
        context.Set<ModerationClassificationControlSelection>().AsNoTracking().Include(row => row.Bindings.OrderBy(binding => binding.Ordinal));

    private static string ClassificationControlSlot(ModerationSourceAuthority authority, Guid eventId) =>
        $"moderation.classification-control:{authority.Binding.Id:N}:{authority.Tenant.Id:N}:{eventId:N}";

    private static string ClassificationControlAdoptionSlot(Guid selectionId, int ordinal) =>
        $"moderation.classification-event-target:{selectionId:N}:{ordinal}";

    private async Task<ModerationClassificationMaterialObservation> AcquireClassificationControlMaterialAsync(
        Guid sourceId, ModerationSourceAuthority authority, ModerationConsumerEvidenceSession session,
        DateTimeOffset now, CancellationToken token)
    {
        var discovered = await context.Set<ModerationClassificationMaterialSource>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == sourceId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        if (discovered.HostBindingId != authority.Binding.Id || discovered.TrustDomainId != authority.Binding.TrustDomainId
            || discovered.TenantId != authority.Tenant.Id)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        var live = await ClassificationMaterialOwner().AcquireCurrentAsync(session, session.TransactionId, discovered.EventId,
            discovered.ProfileId, (ModerationNativeMode)discovered.ModeCode, now, token);
        if (live is null || live.Source.Id != sourceId || live.TransactionId != session.TransactionId
            || clock.GetUtcNow() >= live.ValidUntil)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        return live;
    }

    private static void RequireClassificationControlComposition(ModerationPolicyCompiledDraft policy,
        Guid profileId, Guid selectionId, ModerationPolicyContribution contribution,
        ModerationEventGeneration generation, DateTimeOffset acceptedAt, DateTimeOffset now)
    {
        var profile = policy.Profiles.SingleOrDefault(row => row.Definition.Id == profileId);
        var scope = AuthorityScope.Event(policy.Publication.Jurisdiction, generation.EventId);
        if (policy.Proposed.CatalogueRevision != 2 || profile is null
            || profile.Reference.Id != ModerationReferenceProfileId.CFG29
            || profile.Definition.Timing.Kind != ModerationReferenceTimingKind.CurrentControlUntilQualifiedResolution
            || profile.Definition.Timing.Duration != TimeSpan.Zero
            || !profile.Definition.ScopeKinds.Contains(ModerationScopeKind.Event)
            || !profile.Definition.SubjectKinds.Contains(ModerationSubjectKind.Content)
            || contribution.Id != selectionId || contribution.Rule != profile.Definition.Rule || contribution.Scope != scope
            || contribution.SubjectKind != ModerationSubjectKind.Content || contribution.SubjectId != generation.EventId
            || contribution.StartsAt != acceptedAt || contribution.EventWindowEnd is not null
            || contribution.Effects.IsDefaultOrEmpty || contribution.Consequences.IsDefaultOrEmpty
            || contribution.Effects.Any(row => !ExactTarget(row.Target))
            || contribution.Consequences.Any(row => !ExactTarget(row.Target)))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        var composed = ModerationPolicyProposedStateCompiler.ComposeRetained(policy, [contribution], now);
        if (composed.Outcome != ModerationPolicyCompositionOutcome.Preview || composed.Effects.IsEmpty || composed.Consequences.IsEmpty
            || composed.Effects.Any(row => row.Interval.EndsAt is not null || row.Interval.ReviewAt is not null
                || row.Interval.EventWindowEnd is not null)
            || composed.Consequences.Any(row => row.Interval.EndsAt is not null || row.Interval.ReviewAt is not null
                || row.Interval.EventWindowEnd is not null))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);

        bool ExactTarget(ModerationPolicyTarget target) => target.Scope == scope
            && target.SubjectKind == ModerationSubjectKind.Content && target.SubjectId == generation.EventId
            && target.TargetId == generation.EventId && target.TargetRevision == generation.Revision && target.WindowEnd is null;
    }

    private async Task<QualifiedSource> RecordClassificationControlSelectionAsync(
        RecordModerationClassificationControlSelection request, ModerationSourceAuthority authority,
        ModerationConsumerEvidenceSession session, DateTimeOffset now, CancellationToken token)
    {
        var live = await AcquireClassificationControlMaterialAsync(request.MaterialSourceId, authority, session, now, token);
        if (live.Source.Revision != request.ExpectedMaterialSourceRevision || live.Source.WriterGeneration != request.ExpectedMaterialWriterGeneration
            || live.Acceptance.Id != request.MaterialReviewId || live.Acceptance.Revision != request.ExpectedMaterialReviewRevision
            || live.Acceptance.WriterGeneration != request.ExpectedReviewWriterGeneration || live.Generation.Id != request.EventGenerationId
            || live.Generation.Revision != request.ExpectedEventGenerationRevision || live.Generation.WriterGeneration != request.ExpectedEventWriterGeneration
            || request.ValidUntil <= now || request.ValidUntil > live.ValidUntil)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        if (new[] { live.Source.OwnerHumanId, live.Source.OwnerControllerHumanId,
                live.Source.ReviewerHumanId, live.Source.ReviewerControllerHumanId }.Contains(authority.UserId))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        var native = await session.AcquireNativeAsync(session.TransactionId, now, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        RequireClassificationControlComposition(native.Policy, live.Source.ProfileId, request.SelectionId,
            request.Contribution, live.Generation, live.Acceptance.AcceptedAt, now);
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            ClassificationControlSlot(authority, live.Generation.EventId), token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationClassificationControlSelection>(context, request.SelectionId, token);
        if (await context.Set<ModerationClassificationControlSelection>().AsNoTracking().AnyAsync(row => row.Id == request.SelectionId, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        pendingClassificationSelection = ModerationClassificationControlSelection.Record(request.SelectionId,
            live.Source, live.Acceptance, live.Generation, authority.UserId,
            ModerationContributionSelectionProjection.Bindings(request.SelectionId, request.Contribution),
            ModerationNoticeSnapshotFactory.SelectionDigest(request.Contribution), checked(authority.Epoch.Revision + 1),
            now, request.ValidUntil);
        return new(ModerationQualificationSourceKind.ClassificationControlSelection, request.SelectionId, 1,
            MutationValidUntil: request.ValidUntil);
    }

    private async Task RequireCurrentClassificationControlSelectionAsync(ModerationClassificationControlSelection selection,
        ModerationSourceAuthority authority, ModerationConsumerEvidenceSession session,
        ModerationClassificationMaterialObservation live, DateTimeOffset now, bool retainedHead, CancellationToken token)
    {
        selection.ValidatePersistedState();
        if (selection.HostBindingId != authority.Binding.Id || selection.TrustDomainId != authority.Binding.TrustDomainId
            || selection.TenantId != authority.Tenant.Id)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            ClassificationControlSlot(authority, selection.EventId), token);
        if (retainedHead)
        {
            // Pick the logical Event head first; never resurrect an earlier live/accepted row.
            var head = await context.Set<ModerationClassificationControlSelection>().AsNoTracking()
                .Where(row => row.HostBindingId == authority.Binding.Id && row.TenantId == authority.Tenant.Id && row.EventId == selection.EventId)
                .OrderByDescending(row => row.WriterGeneration).Select(row => (Guid?)row.Id).FirstOrDefaultAsync(token);
            if (head != selection.Id) throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        }
        if (!selection.MatchesCurrent(live.Source, live.Acceptance, live.Generation, now) || selection.ValidUntil > live.ValidUntil
            || !await RequireClassificationControlAdministratorAsync(authority, selection.AuthoredByHumanId, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var native = await session.AcquireNativeAsync(session.TransactionId, now, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var contribution = ModerationClassificationControlSelectionProjection.Restore(selection, native.Policy);
        RequireClassificationControlComposition(native.Policy, selection.ProfileId, selection.Id, contribution,
            live.Generation, live.Acceptance.AcceptedAt, now);
        if (ModerationNoticeSnapshotFactory.SelectionDigest(contribution) != selection.SelectionDigest)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        if (retainedHead)
            await RequireTargetSourceReceiptAsync(ClassificationControlRecordRequest(selection, contribution,
                    AuthorityScope.Event(authority.Tenant, selection.EventId)),
                ModerationQualificationSourceKind.ClassificationControlSelection, selection.Id, 1, selection.AuthoredByHumanId,
                selection.WriterGeneration, selection.RecordedAt, authority, token);
    }

    private async Task<QualifiedSource> AdoptClassificationEventTargetAsync(
        AdoptModerationClassificationEventTarget request, ModerationSourceAuthority authority,
        ModerationConsumerEvidenceSession session, DateTimeOffset now, CancellationToken token)
    {
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationClassificationControlSelection>(context, request.SelectionId, token);
        var selection = await ClassificationControlGraph().SingleOrDefaultAsync(row => row.Id == request.SelectionId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        var live = await AcquireClassificationControlMaterialAsync(selection.SourceId, authority, session, now, token);
        await RequireCurrentClassificationControlSelectionAsync(selection, authority, session, live, now, true, token);
        if (selection.Revision != request.ExpectedSelectionRevision || live.Generation.Id != request.EventGenerationId
            || live.Generation.Revision != request.ExpectedEventGenerationRevision
            || !selection.Bindings.Any(row => row.Ordinal == request.BindingOrdinal)
            || new[] { selection.OwnerHumanId, selection.OwnerControllerHumanId, selection.ReviewerHumanId,
                selection.ReviewerControllerHumanId, selection.AuthoredByHumanId }.Contains(authority.UserId)
            || request.ValidUntil <= now || request.ValidUntil > selection.ValidUntil || request.ValidUntil > live.ValidUntil)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            ClassificationControlAdoptionSlot(selection.Id, request.BindingOrdinal), token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationClassificationEventTargetAdoption>(context, request.AdoptionId, token);
        if (await context.Set<ModerationClassificationEventTargetAdoption>().AsNoTracking().AnyAsync(row =>
            row.Id == request.AdoptionId || row.SelectionId == selection.Id && row.BindingOrdinal == request.BindingOrdinal, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        pendingClassificationAdoption = ModerationClassificationEventTargetAdoption.Adopt(request.AdoptionId, selection,
            request.BindingOrdinal, live.Generation, authority.UserId, checked(authority.Epoch.Revision + 1), now, request.ValidUntil);
        return new(ModerationQualificationSourceKind.ClassificationEventTargetAdoption, request.AdoptionId, 1,
            MutationValidUntil: request.ValidUntil);
    }

    private async Task<QualifiedSource> RevokeClassificationEventTargetAsync(
        RevokeModerationClassificationEventTarget request, ModerationSourceAuthority authority, DateTimeOffset now, CancellationToken token)
    {
        var discovered = await context.Set<ModerationClassificationEventTargetAdoption>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == request.AdoptionId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            ClassificationControlAdoptionSlot(discovered.SelectionId, discovered.BindingOrdinal), token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationClassificationEventTargetAdoption>(context, request.AdoptionId, token);
        var adoption = await context.Set<ModerationClassificationEventTargetAdoption>().SingleAsync(row => row.Id == request.AdoptionId, token);
        await RequireClassificationControlProvenanceAsync(request, adoption, authority, token);
        if (adoption.Revision != request.ExpectedAdoptionRevision || adoption.RevokedAt is not null)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        adoption.Revoke(request.ExpectedAdoptionRevision, authority.UserId, checked(authority.Epoch.Revision + 1), request.Grounds, now);
        return new(ModerationQualificationSourceKind.ClassificationEventTargetAdoption, adoption.Id, adoption.Revision);
    }

    private async Task<ModerationPolicyCompiledDraft> ClassificationControlRetainedPolicyAsync(
        ModerationClassificationControlSelection selection, CancellationToken token)
    {
        var definitions = ClassificationControlDefinitions();
        var candidate = await definitions.CandidateGraph().SingleOrDefaultAsync(row => row.Id == selection.PublicationId, token);
        var policy = candidate is null ? null : ModerationPolicyPublicationProjection.RestoreCandidate(candidate, null);
        if (policy is null)
        {
            var publication = await definitions.Graph().SingleOrDefaultAsync(row => row.Id == selection.PublicationId, token);
            policy = publication is null ? null : ModerationPolicyPublicationProjection.RestoreHistory(publication);
        }
        return policy ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
    }

    private async Task RequireClassificationControlProvenanceAsync(RevokeModerationClassificationEventTarget request,
        ModerationClassificationEventTargetAdoption adoption, ModerationSourceAuthority authority, CancellationToken token)
    {
        adoption.ValidatePersistedState();
        if (request.Target != AuthorityScope.Event(authority.Tenant, adoption.EventId)
            || !await RequireClassificationControlAdministratorAsync(authority, authority.UserId, token)
            || adoption.HostBindingId != authority.Binding.Id || adoption.TrustDomainId != authority.Binding.TrustDomainId
            || adoption.TenantId != authority.Tenant.Id || !authority.FencedHumans.Contains(adoption.AdoptedByHumanId))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationClassificationControlSelection>(context, adoption.SelectionId, token);
        var selection = await ClassificationControlGraph().SingleOrDefaultAsync(row => row.Id == adoption.SelectionId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        selection.ValidatePersistedState();
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationClassificationMaterialSource>(context, selection.SourceId, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationClassificationMaterialReview>(context, selection.ReviewId, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationEventGeneration>(context, selection.GenerationId, token);
        var source = await ClassificationMaterialOwner().Graph().SingleOrDefaultAsync(row => row.Id == selection.SourceId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        var review = await context.Set<ModerationClassificationMaterialReview>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == selection.ReviewId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        var generation = await context.ModerationEventGenerations.AsNoTracking().SingleOrDefaultAsync(row => row.Id == selection.GenerationId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        source.ValidatePersistedState();
        review.ValidatePersistedState();
        generation.ValidatePersistedState();
        // Immutable birth provenance survives expiry, review withdrawal and native deletion.
        // This is not a current material acquisition or a grant of replacement control.
        if (selection.HostBindingId != authority.Binding.Id || selection.TrustDomainId != authority.Binding.TrustDomainId
            || selection.TenantId != authority.Tenant.Id || selection.SourceRevision != source.Revision
            || selection.SourceWriterGeneration != source.WriterGeneration || selection.SourceRecordedAt != source.RecordedAt
            || selection.SourceValidUntil != source.ValidUntil || selection.ReviewWriterGeneration != review.WriterGeneration
            || selection.ReviewRevision != 1 || selection.StartsAt != review.AcceptedAt || selection.ReviewValidUntil != review.ValidUntil
            || review.SourceId != source.Id || review.SourceRevision != source.Revision || review.SourceWriterGeneration != source.WriterGeneration
            || review.HostBindingId != selection.HostBindingId || review.TrustDomainId != selection.TrustDomainId || review.TenantId != selection.TenantId
            || source.HostBindingId != selection.HostBindingId || source.TrustDomainId != selection.TrustDomainId || source.TenantId != selection.TenantId
            || selection.PolicyId != source.PolicyId || selection.PolicyRevision != source.PolicyRevision
            || selection.PublicationId != source.PublicationId || selection.ProfileId != source.ProfileId || selection.ModeCode != source.ModeCode
            || selection.OwnerHumanId != source.OwnerHumanId || selection.OwnerControllerHumanId != source.OwnerControllerHumanId
            || selection.ReviewerHumanId != source.ReviewerHumanId || selection.ReviewerHumanId != review.ReviewerHumanId
            || selection.ReviewerControllerHumanId != source.ReviewerControllerHumanId
            || source.EventGenerationId != generation.Id || source.EventGenerationRevision != generation.Revision
            || source.EventWriterGeneration != generation.WriterGeneration || source.EventId != generation.EventId || source.EventStamp != generation.EventStamp
            || selection.GenerationRevision != generation.Revision || selection.GenerationWriterGeneration != generation.WriterGeneration
            || selection.GenerationObservedAt != generation.ObservedAt || selection.GenerationValidUntil != generation.ValidUntil
            || selection.HostBindingId != generation.HostBindingId || selection.TrustDomainId != generation.TrustDomainId
            || selection.TenantId != generation.TenantId || selection.EventId != generation.EventId || selection.EventStamp != generation.EventStamp
            || adoption.SelectionRevision != selection.Revision || adoption.SelectionWriterGeneration != selection.WriterGeneration
            || adoption.SelectionRecordedAt != selection.RecordedAt || adoption.SelectionValidUntil != selection.ValidUntil
            || !selection.Bindings.Any(row => row.Ordinal == adoption.BindingOrdinal)
            || adoption.GenerationId != generation.Id || adoption.GenerationRevision != generation.Revision
            || adoption.GenerationWriterGeneration != generation.WriterGeneration || adoption.GenerationObservedAt != generation.ObservedAt
            || adoption.GenerationValidUntil != generation.ValidUntil || adoption.EventId != generation.EventId || adoption.EventStamp != generation.EventStamp
            || adoption.OwnerHumanId != selection.OwnerHumanId || adoption.OwnerControllerHumanId != selection.OwnerControllerHumanId
            || adoption.ReviewerHumanId != selection.ReviewerHumanId || adoption.ReviewerControllerHumanId != selection.ReviewerControllerHumanId
            || adoption.SelectionAuthoredByHumanId != selection.AuthoredByHumanId || !authority.FencedHumans.Contains(selection.AuthoredByHumanId))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        var policy = await ClassificationControlRetainedPolicyAsync(selection, token);
        var contribution = ModerationClassificationControlSelectionProjection.Restore(selection, policy);
        RequireClassificationControlComposition(policy, selection.ProfileId, selection.Id, contribution, generation, review.AcceptedAt, selection.RecordedAt);
        if (ModerationNoticeSnapshotFactory.SelectionDigest(contribution) != selection.SelectionDigest)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        await RequireTargetSourceReceiptAsync(ModerationClassificationMaterialSourceOwner.RecordRequest(source, authority.Tenant),
            ModerationQualificationSourceKind.ClassificationMaterialSource, source.Id, 1, source.OwnerHumanId, source.WriterGeneration,
            source.RecordedAt, authority, token);
        await RequireTargetSourceReceiptAsync(ModerationClassificationMaterialSourceOwner.AcceptanceRequest(review, authority.Tenant),
            ModerationQualificationSourceKind.ClassificationMaterialReview, review.Id, 1, review.ReviewerHumanId, review.WriterGeneration,
            review.AcceptedAt, authority, token);
        await RequireTargetSourceReceiptAsync(new RecordModerationEventGenerationSource
        {
            OperationId = Guid.Empty, Target = authority.Tenant, ExpectedQualificationRevision = generation.WriterGeneration - 1,
            SourceId = generation.Id, EventId = generation.EventId, ExpectedEventRevision = generation.Revision - 1, ValidUntil = generation.ValidUntil
        }, ModerationQualificationSourceKind.EventGeneration, generation.Id, generation.Revision, generation.ObservedByHumanId,
            generation.WriterGeneration, generation.ObservedAt, authority, token);
        await RequireTargetSourceReceiptAsync(ClassificationControlRecordRequest(selection, contribution,
                AuthorityScope.Event(authority.Tenant, selection.EventId)),
            ModerationQualificationSourceKind.ClassificationControlSelection, selection.Id, 1, selection.AuthoredByHumanId,
            selection.WriterGeneration, selection.RecordedAt, authority, token);
        await RequireTargetSourceReceiptAsync(ClassificationControlAdoptionRequest(adoption,
                AuthorityScope.Event(authority.Tenant, adoption.EventId)),
            ModerationQualificationSourceKind.ClassificationEventTargetAdoption, adoption.Id, 1, adoption.AdoptedByHumanId,
            adoption.WriterGeneration, adoption.AdoptedAt, authority, token);
    }

    private static RecordModerationClassificationControlSelection ClassificationControlRecordRequest(
        ModerationClassificationControlSelection selection, ModerationPolicyContribution contribution, AuthorityScope target) => new()
    {
        OperationId = Guid.Empty, Target = target, ExpectedQualificationRevision = selection.WriterGeneration - 1,
        SelectionId = selection.Id, MaterialSourceId = selection.SourceId, ExpectedMaterialSourceRevision = selection.SourceRevision,
        ExpectedMaterialWriterGeneration = selection.SourceWriterGeneration, MaterialReviewId = selection.ReviewId,
        ExpectedMaterialReviewRevision = selection.ReviewRevision, ExpectedReviewWriterGeneration = selection.ReviewWriterGeneration,
        EventGenerationId = selection.GenerationId, ExpectedEventGenerationRevision = selection.GenerationRevision,
        ExpectedEventWriterGeneration = selection.GenerationWriterGeneration, Contribution = contribution, ValidUntil = selection.ValidUntil
    };

    private static AdoptModerationClassificationEventTarget ClassificationControlAdoptionRequest(
        ModerationClassificationEventTargetAdoption adoption, AuthorityScope target) => new()
    {
        OperationId = Guid.Empty, Target = target, ExpectedQualificationRevision = adoption.WriterGeneration - 1,
        AdoptionId = adoption.Id, SelectionId = adoption.SelectionId, ExpectedSelectionRevision = adoption.SelectionRevision,
        BindingOrdinal = adoption.BindingOrdinal, EventGenerationId = adoption.GenerationId,
        ExpectedEventGenerationRevision = adoption.GenerationRevision, ValidUntil = adoption.ValidUntil
    };

    private async Task VerifyClassificationControlBeforeSaveAsync(ModerationQualificationMutation request,
        ModerationConsumerEvidenceSession? session, ModerationSourceAuthority authority, CancellationToken token)
    {
        if (!IsClassificationControlMutation(request)) return;
        var transaction = context.Database.CurrentTransaction;
        if (transaction is null || !await ClassificationControlAuthorityCurrentAsync(authority, transaction, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        if (request is RevokeModerationClassificationEventTarget revoking)
        {
            var row = context.ChangeTracker.Entries<ModerationClassificationEventTargetAdoption>()
                .Single(entry => entry.Entity.Id == revoking.AdoptionId).Entity;
            await RequireClassificationControlProvenanceAsync(revoking, row, authority, token);
            return;
        }
        if (session is null || context.ChangeTracker.HasChanges())
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var selection = pendingClassificationSelection ?? await ClassificationControlGraph().SingleOrDefaultAsync(
            row => row.Id == ((AdoptModerationClassificationEventTarget)request).SelectionId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var now = clock.GetUtcNow();
        var live = await AcquireClassificationControlMaterialAsync(selection.SourceId, authority, session, now, token);
        await RequireCurrentClassificationControlSelectionAsync(selection, authority, session, live, now,
            request is AdoptModerationClassificationEventTarget, token);
        if (request is RecordModerationClassificationControlSelection recording)
        {
            if (pendingClassificationSelection?.Id != recording.SelectionId)
                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
            context.Set<ModerationClassificationControlSelection>().Add(selection);
            pendingClassificationSelection = null;
        }
        else
        {
            var adoption = pendingClassificationAdoption
                ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
            if (adoption.Id != ((AdoptModerationClassificationEventTarget)request).AdoptionId
                || !adoption.MatchesCurrent(selection, live.Source, live.Acceptance, live.Generation, now)
                || adoption.ValidUntil > live.ValidUntil)
                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
            context.Set<ModerationClassificationEventTargetAdoption>().Add(adoption);
            pendingClassificationAdoption = null;
        }
    }

    private async Task VerifyClassificationControlWriteAsync(ModerationQualificationMutation request,
        ModerationConsumerEvidenceSession? session, ModerationSourceAuthority authority, CancellationToken token)
    {
        if (!IsClassificationControlMutation(request)) return;
        var transaction = context.Database.CurrentTransaction;
        if (transaction is null || context.ChangeTracker.HasChanges()
            || !await ClassificationControlAuthorityCurrentAsync(authority, transaction, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        if (request is RevokeModerationClassificationEventTarget revoking)
        {
            var row = await context.Set<ModerationClassificationEventTargetAdoption>().AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == revoking.AdoptionId, token);
            if (row is null || row.Revision != revoking.ExpectedAdoptionRevision + 1 || row.RevokedByHumanId != authority.UserId
                || row.RevocationGrounds != revoking.Grounds || row.RevokedAt is null || row.RevocationWriterGeneration is null)
                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
            await RequireClassificationControlProvenanceAsync(revoking, row, authority, token);
            await RequireTargetSourceReceiptAsync(request, ModerationQualificationSourceKind.ClassificationEventTargetAdoption,
                row.Id, row.Revision, authority.UserId, row.RevocationWriterGeneration.Value, row.RevokedAt.Value, authority, token);
            return;
        }
        if (session is null) throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var id = request is RecordModerationClassificationControlSelection recording ? recording.SelectionId
            : ((AdoptModerationClassificationEventTarget)request).SelectionId;
        var selection = await ClassificationControlGraph().SingleOrDefaultAsync(row => row.Id == id, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var now = clock.GetUtcNow();
        var live = await AcquireClassificationControlMaterialAsync(selection.SourceId, authority, session, now, token);
        await RequireCurrentClassificationControlSelectionAsync(selection, authority, session, live, now, true, token);
        if (request is RecordModerationClassificationControlSelection recorded)
        {
            if (selection.AuthoredByHumanId != authority.UserId)
                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
            await RequireTargetSourceReceiptAsync(recorded, ModerationQualificationSourceKind.ClassificationControlSelection,
                selection.Id, 1, selection.AuthoredByHumanId, selection.WriterGeneration, selection.RecordedAt, authority, token);
            return;
        }
        var adopt = (AdoptModerationClassificationEventTarget)request;
        var adoption = await context.Set<ModerationClassificationEventTargetAdoption>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == adopt.AdoptionId, token);
        if (adoption is null || adoption.AdoptedByHumanId != authority.UserId
            || !adoption.MatchesCurrent(selection, live.Source, live.Acceptance, live.Generation, now)
            || adoption.BindingOrdinal != adopt.BindingOrdinal || adoption.ValidUntil > live.ValidUntil
            || !await RequireClassificationControlAdministratorAsync(authority, adoption.AdoptedByHumanId, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        adoption.ValidatePersistedState();
        await RequireTargetSourceReceiptAsync(adopt, ModerationQualificationSourceKind.ClassificationEventTargetAdoption,
            adoption.Id, 1, adoption.AdoptedByHumanId, adoption.WriterGeneration, adoption.AdoptedAt, authority, token);
    }

    private async Task VerifyClassificationControlAtCommitAsync(ModerationQualificationMutation request,
        ModerationSourceAuthority authority, IDbContextTransaction transaction, CancellationToken token)
    {
        if (context.ChangeTracker.HasChanges() || !ReferenceEquals(context.Database.CurrentTransaction, transaction))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        using var session = await AcquireClassificationControlWriteAsync(request, authority, clock.GetUtcNow(), token);
        await VerifyClassificationControlWriteAsync(request, session, authority, token);
    }
}

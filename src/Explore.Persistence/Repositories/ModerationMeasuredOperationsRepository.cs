using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationQualificationRepository
{
    private ModerationMeasuredOperationsSourceOwner MeasuredOwner() => new(context,
        new ModerationPolicyDefinitionSourceOwner(context, admin, machine, deploymentInventory!, clock),
        deploymentInventory!, clock, measuredConsumerConformance, consumerSources, measuredOperationalProofs);

    private async Task<ModerationConsumerEvidenceSession?> AcquireMeasuredOperationsWriteAsync(
        ModerationQualificationMutation request, ModerationSourceAuthority authority, DateTimeOffset now, CancellationToken token)
    {
        if (request is not (RecordModerationMeasuredOperationsSource or AcceptModerationMeasuredOperationsSource)) return null;
        if (deploymentInventory is null) throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var definitions = new ModerationPolicyDefinitionSourceOwner(context, admin, machine, deploymentInventory, clock);
        var constitution = new ModerationConstitutionSourceOwner(context, definitions, clock);
        var transaction = context.Database.CurrentTransaction!;
        async Task<bool> Current(CancellationToken cancellation)
        {
            if (!ReferenceEquals(context.Database.CurrentTransaction, transaction) || machine.IsMachineCaller
                || await admin.ResolveUserIdAsync(cancellation) != authority.UserId
                || !await new ModerationQualificationAuthority(context, admin, machine).ProviderTenantMatchesAsync(authority.Tenant.Id, cancellation)
                || context.ModerationTrustDomainId != authority.Binding.TrustDomainId
                || !await context.ModerationQualificationEpochs.AsNoTracking().AnyAsync(row => row.Id == authority.Epoch.Id
                    && row.HostBindingId == authority.Binding.Id && row.TenantId == authority.Tenant.Id
                    && row.Revision == authority.Epoch.Revision, cancellation)) return false;
            foreach (var human in authority.Administrators)
                if (!await constitution.AdministratorAsync(authority, human, request.Target, cancellation)) return false;
            return ReferenceEquals(context.Database.CurrentTransaction, transaction);
        }
        if (!await Current(token)) throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        ModerationMeasuredOperationsSource? retained = null;
        if (request is AcceptModerationMeasuredOperationsSource accepting)
        {
            retained = await context.Set<ModerationMeasuredOperationsSource>().AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == accepting.SourceId, token)
                ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
            retained.ValidatePersistedState();
            if (retained.HostBindingId != authority.Binding.Id || retained.TrustDomainId != authority.Binding.TrustDomainId
                || retained.TenantId != authority.Tenant.Id || retained.ReviewerHumanId != authority.UserId
                || retained.RecordedByHumanId == authority.UserId || retained.Revision != accepting.ExpectedSourceRevision
                || retained.WriterGeneration != accepting.ExpectedSourceWriterGeneration
                || ModerationMeasuredOperationsSourceOwner.OriginalAnchor(retained) != accepting.OriginalEffect
                || accepting.ValidUntil <= now || accepting.ValidUntil > retained.ValidUntil)
                throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        }
        var recording = request as RecordModerationMeasuredOperationsSource;
        var anchor = OriginalMeasuredOperationsAnchor(request);
        var original = anchor is not null ? await ModerationOriginalRecipeAuthority.AcquireAsync(
            context, authority, transaction.TransactionId, anchor, privacy, clock, token) : null;
        if (anchor is not null && (original is null || request.Target != original.Policy.Publication.Jurisdiction
            || recording is not null && recording.Binding != original.Binding
            || (recording?.ProfileId ?? retained!.ProfileId) != original.Policy.Profiles
                .Single(row => row.Definition.Rule == original.Binding.Rule).Definition.Id
            || retained is not null && (retained.PublicationId != original.Binding.PublicationId
                || retained.PolicyId != original.Binding.PolicyId || retained.PolicyRevision != original.Binding.PolicyRevision
                || retained.ModeCode != (int)original.Binding.Mode || retained.DefinitionId != original.DefinitionId
                || retained.BindingDigest != ModerationPolicyPublicationProjection.Digest(original.Binding)
                || !ModerationMeasuredOperationsSourceOwner.MatchesPurpose(retained, original))
            || (recording?.ValidUntil ?? ((AcceptModerationMeasuredOperationsSource)request).ValidUntil) > original.ValidUntil))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var definition = original is null
            ? await definitions.ReadDefinitionAsync(authority, transaction.TransactionId, request.Target,
                recording?.Binding.PolicyId ?? retained!.PolicyId, recording?.Binding.PublicationId ?? retained!.PublicationId, now, token)
            : new ModerationPolicyDefinitionSourceOwner.Definition(original.Policy, original.DefinitionId, original.ValidUntil);
        if (definition is null || !await Current(token)) throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        if (recording is not null && (recording.Binding.Jurisdiction != request.Target
            || recording.Binding.PolicyRevision != definition.Policy.Publication.ProposedRevision
            || recording.ValidUntil <= now || recording.ValidUntil > definition.ValidUntil))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var session = new ModerationConsumerEvidenceSession(context, transaction, authority, definition.Policy,
            Current, publications, deploymentInventory, consumerSources, measurementReceipts, clock, null, original);
        try
        {
            if (retained is not null)
            {
                var binding = definition.Policy.Consumers.Select(row => row.Binding).SingleOrDefault(row =>
                    ModerationPolicyPublicationProjection.Digest(row) == retained.BindingDigest);
                if (binding is null || (await MeasuredOwner().AcquireSourceAsync(session, session.TransactionId,
                    retained.ProfileId, binding, now, token))?.Source.Id != retained.Id)
                    throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
            }
            return session;
        }
        catch { session.Dispose(); throw; }
    }

    private async Task<QualifiedSource> RecordMeasuredOperationsAsync(RecordModerationMeasuredOperationsSource request,
        ModerationSourceAuthority authority, ModerationConsumerEvidenceSession session, DateTimeOffset now, CancellationToken token)
    {
        var live = await MeasuredOwner().ObserveAsync(session, session.TransactionId, request.ProfileId, request.Binding,
            authority.UserId, request.ReviewerHumanId, request.ReviewerControllerHumanId, request.RecorderControllerHumanId, now, token);
        if (live is null || request.ValidUntil > live.ValidUntil)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            ModerationMeasuredOperationsSourceOwner.Slot(authority, request.Binding), token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationMeasuredOperationsSource>(context, request.SourceId, token);
        if (await context.Set<ModerationMeasuredOperationsSource>().AsNoTracking().AnyAsync(row => row.Id == request.SourceId, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        var native = await session.AcquireNativeAsync(session.TransactionId, now, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var source = ModerationMeasuredOperationsSource.Record(request.SourceId, authority.Binding.Id, authority.Binding.TrustDomainId,
            authority.Tenant.Id, request.Binding.PolicyId, request.Binding.PolicyRevision, request.Binding.PublicationId,
            live.DefinitionId, request.ProfileId, (int)request.Binding.Mode, ModerationPolicyPublicationProjection.Digest(request.Binding),
            ModerationPolicyPublicationProjection.ProposedDigest(native.Policy.Proposed), live.Evidence,
            request.ReviewerHumanId, request.ReviewerControllerHumanId, request.RecorderControllerHumanId,
            authority.UserId, checked(authority.Epoch.Revision + 1), now, request.ValidUntil,
            native.OriginalRecipe?.Anchor.EffectId, native.OriginalRecipe?.Anchor.SnapshotHash,
            native.OriginalRecipe?.Anchor.EffectBindingHash, native.OriginalRecipe?.ValidUntil);
        context.Set<ModerationMeasuredOperationsSource>().Add(source);
        return new(ModerationQualificationSourceKind.MeasuredOperationsSource, source.Id, 1, MutationValidUntil: source.ValidUntil);
    }

    private async Task<QualifiedSource> AcceptMeasuredOperationsAsync(AcceptModerationMeasuredOperationsSource request,
        ModerationSourceAuthority authority, ModerationConsumerEvidenceSession session, DateTimeOffset now, CancellationToken token)
    {
        var source = await context.Set<ModerationMeasuredOperationsSource>().AsNoTracking().SingleAsync(row => row.Id == request.SourceId, token);
        var native = await session.AcquireNativeAsync(session.TransactionId, now, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var binding = native.Policy.Consumers.Select(row => row.Binding)
            .SingleOrDefault(row => ModerationPolicyPublicationProjection.Digest(row) == source.BindingDigest);
        var current = binding is null ? null : await MeasuredOwner().AcquireSourceAsync(
            session, session.TransactionId, source.ProfileId, binding, now, token);
        if (current?.Source.Id != request.SourceId) throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationMeasuredOperationsAcceptance>(context, request.AcceptanceId, token);
        if (await context.Set<ModerationMeasuredOperationsAcceptance>().AsNoTracking()
            .AnyAsync(row => row.Id == request.AcceptanceId || row.SourceId == request.SourceId, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        var acceptance = ModerationMeasuredOperationsAcceptance.Accept(request.AcceptanceId, current.Source, authority.UserId,
            checked(authority.Epoch.Revision + 1), now, request.ValidUntil);
        context.Set<ModerationMeasuredOperationsAcceptance>().Add(acceptance);
        return new(ModerationQualificationSourceKind.MeasuredOperationsAcceptance, acceptance.Id, 1, MutationValidUntil: acceptance.ValidUntil);
    }

    private async Task<QualifiedSource> WithdrawMeasuredOperationsAsync(WithdrawModerationMeasuredOperationsAcceptance request,
        ModerationSourceAuthority authority, DateTimeOffset now, CancellationToken token)
    {
        var discovered = await context.Set<ModerationMeasuredOperationsAcceptance>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == request.AcceptanceId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        var source = await context.Set<ModerationMeasuredOperationsSource>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == discovered.SourceId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        var binding = await HistoricalMeasuredBindingAsync(source, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            ModerationMeasuredOperationsSourceOwner.Slot(authority, binding), token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationMeasuredOperationsAcceptance>(context, request.AcceptanceId, token);
        var acceptance = await context.Set<ModerationMeasuredOperationsAcceptance>().SingleOrDefaultAsync(row => row.Id == request.AcceptanceId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        await RequireMeasuredOperationsProvenanceAsync(request, acceptance, authority, token);
        if (acceptance.Revision != request.ExpectedAcceptanceRevision || acceptance.WithdrawnAt is not null)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        acceptance.Withdraw(request.ExpectedAcceptanceRevision, authority.UserId, now);
        return new(ModerationQualificationSourceKind.MeasuredOperationsAcceptance, acceptance.Id, acceptance.Revision);
    }

    private async Task RequireMeasuredOperationsProvenanceAsync(WithdrawModerationMeasuredOperationsAcceptance request,
        ModerationMeasuredOperationsAcceptance acceptance, ModerationSourceAuthority authority, CancellationToken token)
    {
        acceptance.ValidatePersistedState();
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationMeasuredOperationsSource>(context, acceptance.SourceId, token);
        var source = await context.Set<ModerationMeasuredOperationsSource>().AsNoTracking().SingleOrDefaultAsync(row => row.Id == acceptance.SourceId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        source.ValidatePersistedState();
        var binding = await HistoricalMeasuredBindingAsync(source, token);
        if (source.HostBindingId != authority.Binding.Id || source.TrustDomainId != authority.Binding.TrustDomainId
            || source.TenantId != authority.Tenant.Id || acceptance.HostBindingId != authority.Binding.Id
            || acceptance.TrustDomainId != authority.Binding.TrustDomainId || acceptance.TenantId != authority.Tenant.Id
            || acceptance.SourceRevision != source.Revision || acceptance.SourceWriterGeneration != source.WriterGeneration
            || acceptance.AcceptedByHumanId != source.ReviewerHumanId || binding?.Jurisdiction != request.Target
            || new[] { source.RecordedByHumanId, source.ReviewerHumanId, source.ReviewerControllerHumanId,
                source.RecorderControllerHumanId, acceptance.AcceptedByHumanId }.Any(human => !authority.FencedHumans.Contains(human)))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        // Terminal withdrawal needs current acting authority and original receipts, not live old
        // staff, measurements, selection, recognition purpose, or original account membership.
        await RequireTargetSourceReceiptAsync(ModerationMeasuredOperationsSourceOwner.RecordRequest(source, binding),
            ModerationQualificationSourceKind.MeasuredOperationsSource, source.Id, 1, source.RecordedByHumanId,
            source.WriterGeneration, source.RecordedAt, authority, token);
        await RequireTargetSourceReceiptAsync(ModerationMeasuredOperationsSourceOwner.AcceptanceRequest(acceptance, binding, source),
            ModerationQualificationSourceKind.MeasuredOperationsAcceptance, acceptance.Id, 1, acceptance.AcceptedByHumanId,
            acceptance.WriterGeneration, acceptance.AcceptedAt, authority, token);
    }

    private async Task<ModerationActivationBinding?> HistoricalMeasuredBindingAsync(ModerationMeasuredOperationsSource source, CancellationToken token)
    {
        var definition = await context.ModerationPolicyPublications.AsNoTracking()
            .Include(row => row.Profiles).SingleOrDefaultAsync(row => row.Id == source.DefinitionId, token);
        var profile = definition?.Profiles.SingleOrDefault(row => row.ProfileId == source.ProfileId);
        if (definition is null || profile is null || definition.HostBindingId != source.HostBindingId
            || definition.TrustDomainId != source.TrustDomainId || definition.TenantId != source.TenantId
            || definition.PolicyId != source.PolicyId || definition.Revision != source.PolicyRevision) return null;
        // Recover only the exact finite binding named by the immutable report. No live selection
        // or current catalogue is required for historical receipt verification.
        return Enum.GetValues<ModerationCapability>().Select(capability => new ModerationActivationBinding(
            source.PolicyId, source.PolicyRevision, source.PublicationId, definition.Jurisdiction,
            ModerationRuleReference.Create(definition.Jurisdiction.Id, profile.RuleCode, profile.RuleRevision),
            capability, (ModerationNativeMode)source.ModeCode))
            .SingleOrDefault(binding => ModerationPolicyPublicationProjection.Digest(binding) == source.BindingDigest);
    }

    private async Task VerifyMeasuredOperationsWriteAsync(ModerationQualificationMutation request,
        ModerationConsumerEvidenceSession? session, ModerationSourceAuthority authority, CancellationToken token)
    {
        if (request is not (RecordModerationMeasuredOperationsSource or AcceptModerationMeasuredOperationsSource
            or WithdrawModerationMeasuredOperationsAcceptance)) return;
        if (context.ChangeTracker.HasChanges())
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var reader = new ModerationQualificationAuthority(context, admin, machine);
        if (machine.IsMachineCaller || await admin.ResolveUserIdAsync(token) != authority.UserId
            || !await reader.ProviderTenantMatchesAsync(authority.Tenant.Id, token)
            || context.ModerationTrustDomainId != authority.Binding.TrustDomainId
            || !await context.ModerationQualificationEpochs.AsNoTracking().AnyAsync(row => row.Id == authority.Epoch.Id
                && row.HostBindingId == authority.Binding.Id && row.TenantId == authority.Tenant.Id
                && row.Revision == authority.Epoch.Revision, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        if (request is WithdrawModerationMeasuredOperationsAcceptance withdrawal)
        {
            var current = await reader.AcquireAsync(request, token);
            if (current is null || current.UserId != authority.UserId || current.Binding.Id != authority.Binding.Id
                || current.Tenant != authority.Tenant || current.Epoch.Id != authority.Epoch.Id
                || current.Epoch.Revision != authority.Epoch.Revision)
                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
            var accepted = await context.Set<ModerationMeasuredOperationsAcceptance>().AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == withdrawal.AcceptanceId, token);
            if (accepted is null || accepted.Revision != withdrawal.ExpectedAcceptanceRevision + 1
                || accepted.WithdrawnAt is null || accepted.WithdrawnByHumanId != authority.UserId)
                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
            await RequireMeasuredOperationsProvenanceAsync(withdrawal, accepted, current, token);
            return;
        }
        if (session is null || deploymentInventory is null) throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var id = request is RecordModerationMeasuredOperationsSource record ? record.SourceId
            : ((AcceptModerationMeasuredOperationsSource)request).SourceId;
        var source = await context.Set<ModerationMeasuredOperationsSource>().AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, token);
        var binding = source is null ? null : await HistoricalMeasuredBindingAsync(source, token);
        if (source is null || binding is null || (await MeasuredOwner().AcquireSourceAsync(session, session.TransactionId,
            source.ProfileId, binding, clock.GetUtcNow(), token))?.Source.Id != id)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        if (request is AcceptModerationMeasuredOperationsSource accept
            && (await MeasuredOwner().ReadAcceptanceAsync(session, session.TransactionId, source.ProfileId,
                binding, clock.GetUtcNow(), token))?.Acceptance.Id != accept.AcceptanceId)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
    }
}

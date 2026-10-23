using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Services;

/// <summary>
/// Captures native operational reports and independently qualifies the strict AG05 composite.
/// Partial reports retain gaps; acceptance never fills them. No conformance verdict is returned.
/// </summary>
public sealed class ModerationMeasuredOperationsSourceOwner(
    ExploreDbContext context, ModerationPolicyDefinitionSourceOwner definitions,
    ModerationCapabilityRegistry inventory, TimeProvider clock,
    ModerationConsumerConformanceReader? consumerConformance = null,
    IEnumerable<IModerationConsumerRuntimeSource>? consumerSources = null,
    IModerationMeasuredOperationsProofSource? operationalProofs = null)
{
    public async Task<ModerationMeasuredOperationsSourceObservation?> AcquireSourceAsync(
        IModerationConsumerEvidenceSession evidenceSession, Guid transactionId, Guid profileId,
        ModerationActivationBinding binding, DateTimeOffset observedAt, CancellationToken token)
    {
        if (evidenceSession is not ModerationConsumerEvidenceSession session
            || await AcquireOriginalAsync(session, transactionId, observedAt, token) is not { } native
            || !native.Policy.Consumers.Any(row => row.Binding == binding)) return null;
        var original = native.OriginalRecipe;
        if (original is not null && (binding != original.Binding
            || !native.Policy.Profiles.Any(row => row.Definition.Id == profileId
                && row.Definition.Rule == original.Binding.Rule))) return null;
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            Slot(native.Authority, binding), token);
        var bindingDigest = ModerationPolicyPublicationProjection.Digest(binding);
        var id = await context.Set<ModerationMeasuredOperationsSource>().AsNoTracking()
            .Where(row => row.HostBindingId == native.Authority.Binding.Id && row.TenantId == native.Authority.Tenant.Id
                && row.BindingDigest == bindingDigest && row.ProfileId == profileId
                && (row.OriginalEffectId == null || original != null
                    && row.OriginalEffectId == original.Anchor.EffectId
                    && row.OriginalSnapshotHash == original.Anchor.SnapshotHash
                    && row.OriginalEffectBindingHash == original.Anchor.EffectBindingHash))
            .OrderByDescending(row => row.WriterGeneration).Select(row => (Guid?)row.Id).FirstOrDefaultAsync(token);
        if (id is null) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationMeasuredOperationsSource>(context, id.Value, token);
        var source = await context.Set<ModerationMeasuredOperationsSource>().AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, token);
        if (source is null) return null;
        source.ValidatePersistedState();
        var constitution = new ModerationConstitutionSourceOwner(context, definitions, clock);
        if (source.HostBindingId != native.Authority.Binding.Id || source.TrustDomainId != native.Authority.Binding.TrustDomainId
            || source.TenantId != native.Authority.Tenant.Id || source.PolicyId != binding.PolicyId
            || source.PolicyRevision != binding.PolicyRevision || source.PublicationId != binding.PublicationId
            || source.ProfileId != profileId || source.ModeCode != (int)binding.Mode
            || !MatchesPurpose(source, original)
            || source.RecordedAt > observedAt || source.ValidUntil <= observedAt || source.WriterGeneration > native.Authority.Epoch.Revision
            || !await constitution.HasReceiptAsync(native.Authority, RecordRequest(source, binding),
                ModerationQualificationSourceKind.MeasuredOperationsSource, source.Id, 1, source.RecordedByHumanId,
                source.WriterGeneration, source.RecordedAt, token)) return null;
        var live = await ObserveAsync(session, transactionId, profileId, binding, source.RecordedByHumanId,
            source.ReviewerHumanId, source.ReviewerControllerHumanId, source.RecorderControllerHumanId, observedAt, token);
        if (live is null || live.DefinitionId != source.DefinitionId || live.ValidUntil < source.ValidUntil
            || source.ProposedStateDigest != ModerationPolicyPublicationProjection.ProposedDigest(native.Policy.Proposed)
            || JsonSerializer.Serialize(live.Evidence) != source.EvidenceJson
            || await AcquireOriginalAsync(session, transactionId, observedAt, token) is null
            || clock.GetUtcNow() >= source.ValidUntil) return null;
        return new(transactionId, source, observedAt,
            original is not null && original.ValidUntil < source.ValidUntil ? original.ValidUntil : source.ValidUntil);
    }

    public async Task<ModerationMeasuredOperationsAcceptanceObservation?> ReadAcceptanceAsync(
        IModerationConsumerEvidenceSession evidenceSession, Guid transactionId, Guid profileId,
        ModerationActivationBinding binding, DateTimeOffset observedAt, CancellationToken token)
    {
        if (evidenceSession is not ModerationConsumerEvidenceSession session
            || await AcquireOriginalAsync(session, transactionId, observedAt, token) is not { } native) return null;
        var source = await AcquireSourceAsync(session, transactionId, profileId, binding, observedAt, token);
        if (source is null) return null;
        var id = await context.Set<ModerationMeasuredOperationsAcceptance>().AsNoTracking()
            .Where(row => row.SourceId == source.Source.Id).Select(row => (Guid?)row.Id).SingleOrDefaultAsync(token);
        if (id is null) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationMeasuredOperationsAcceptance>(context, id.Value, token);
        var acceptance = await context.Set<ModerationMeasuredOperationsAcceptance>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == id, token);
        if (acceptance is null) return null;
        acceptance.ValidatePersistedState();
        var constitution = new ModerationConstitutionSourceOwner(context, definitions, clock);
        if (!acceptance.MatchesCurrent(source.Source, observedAt) || acceptance.WriterGeneration > native.Authority.Epoch.Revision
            || !await constitution.AdministratorAsync(native.Authority, acceptance.AcceptedByHumanId, binding.Jurisdiction, token)
            || !await constitution.HasReceiptAsync(native.Authority, AcceptanceRequest(acceptance, binding, source.Source),
                ModerationQualificationSourceKind.MeasuredOperationsAcceptance, acceptance.Id, 1, acceptance.AcceptedByHumanId,
                acceptance.WriterGeneration, acceptance.AcceptedAt, token)) return null;
        var completed = await AcquireSourceAsync(session, transactionId, profileId, binding, observedAt, token);
        return completed is not null && acceptance.MatchesCurrent(completed.Source, clock.GetUtcNow())
            && await AcquireOriginalAsync(session, transactionId, observedAt, token) is not null
            ? new(transactionId, acceptance, observedAt,
                source.ValidUntil < acceptance.ValidUntil ? source.ValidUntil : acceptance.ValidUntil) : null;
    }

    public async Task<ModerationPrerequisiteObservation?> AcquirePrerequisiteAsync(
        IModerationConsumerEvidenceSession evidenceSession, Guid transactionId, ModerationActivationBinding binding,
        DateTimeOffset observedAt, CancellationToken token)
    {
        if (evidenceSession is not ModerationConsumerEvidenceSession session
            || await AcquireOriginalAsync(session, transactionId, observedAt, token) is not { } native
            || !native.Policy.Consumers.Any(row => row.Binding == binding)) return null;
        var profile = native.Policy.Profiles.SingleOrDefault(row => row.Definition.Rule == binding.Rule);
        if (profile is null) return null;
        var source = await AcquireSourceAsync(session, transactionId, profile.Definition.Id, binding, observedAt, token);
        var acceptance = await ReadAcceptanceAsync(session, transactionId, profile.Definition.Id, binding, observedAt, token);
        if (source is null || acceptance is null || !acceptance.Acceptance.MatchesCurrent(source.Source, clock.GetUtcNow())
            || !source.Source.Evidence.IsComplete(inventory.RequiredConsumers(binding.Capability, binding.Mode),
                (int)binding.Mode, Purpose(profile.Definition.Recognition), clock.GetUtcNow())
            || await AcquireOriginalAsync(session, transactionId, observedAt, token) is null) return null;
        return new(transactionId, binding, ModerationActivationPrerequisite.MeasuredOperations,
            acceptance.Acceptance.Id, acceptance.Acceptance.Revision, source.Source.Id, acceptance.Acceptance.SourceRevision,
            acceptance.Acceptance.SourceWriterGeneration.ToString(CultureInfo.InvariantCulture),
            source.Source.Revision, source.Source.WriterGeneration.ToString(CultureInfo.InvariantCulture),
            acceptance.Acceptance.AcceptedAt, acceptance.ValidUntil, observedAt);
    }

    internal async Task<MeasuredLiveState?> ObserveAsync(
        ModerationConsumerEvidenceSession session, Guid transactionId, Guid profileId, ModerationActivationBinding binding,
        Guid recorder, Guid reviewer, Guid reviewerController, Guid recorderController,
        DateTimeOffset observedAt, CancellationToken token)
    {
        if (await AcquireOriginalAsync(session, transactionId, observedAt, token) is not { } native
            || !native.Policy.Consumers.Any(row => row.Binding == binding)) return null;
        if (native.OriginalRecipe is { } original && (binding != original.Binding
            || !native.Policy.Profiles.Any(row => row.Definition.Id == profileId
                && row.Definition.Rule == original.Binding.Rule))) return null;
        var definition = await definitions.ReadDefinitionAsync(native, observedAt, token);
        var profile = definition?.Policy.Profiles.SingleOrDefault(row => row.Definition.Id == profileId)?.Definition;
        if (definition is null || profile is null || profile.Rule != binding.Rule
            || definition.Policy.Publication.ProposedRevision != binding.PolicyRevision
            || ModerationPolicyPublicationProjection.ProposedDigest(definition.Policy.Proposed)
                != ModerationPolicyPublicationProjection.ProposedDigest(native.Policy.Proposed)
            || !definition.Policy.Proposed.Modes.Contains(binding.Mode)) return null;
        var constitution = new ModerationConstitutionSourceOwner(context, definitions, clock);
        foreach (var human in new[] { recorder, reviewer })
            if (!await constitution.AdministratorAsync(native.Authority, human, binding.Jurisdiction, token)) return null;
        foreach (var human in new[] { reviewerController, recorderController })
            if (!native.Authority.FencedHumans.Contains(human)
                || !await context.Users.AsNoTracking().AnyAsync(row => row.Id == human && !row.IsDeleted, token)) return null;
        if (recorder == reviewer || reviewerController == recorderController
            || recorder == reviewerController || reviewer == recorderController) return null;

        var primary = await session.AcquirePrimaryDatabaseAsync(observedAt, token);
        if (primary is null || primary.TransactionId != transactionId || primary.ObservedAt != observedAt) return null;
        // This owner rechecks the adopted constitution, every finite availability, independence,
        // native role and actual current unresolved caseload under the original authority fences.
        var staff = await constitution.ReadAdoptionAsync(session, transactionId, profileId, binding.Mode, observedAt, token);
        ModerationMeasuredOperationsProofReference? staffing = staff is null ? null
            : new(staff.Adoption.Id, staff.Adoption.Revision, staff.Adoption.WriterGeneration, staff.Adoption.AdoptedAt, staff.ValidUntil);
        var purpose = Purpose(profile.Recognition);
        var privacy = purpose == 0 ? null : await new ModerationRecognitionPrivacySourceOwner(context, definitions, clock)
            .ReadAdoptionAsync(session, transactionId, profileId, binding.Mode, observedAt, token);
        var privacySource = privacy is null ? null : await new ModerationRecognitionPrivacySourceOwner(context, definitions, clock)
            .AcquireSourceAsync(session, transactionId, profileId, binding.Mode, observedAt, token);
        ModerationMeasuredOperationsProofReference? privacyReference = privacy is null || privacySource is null ? null
            : new(privacy.Adoption.Id, privacy.Adoption.Revision, privacy.Adoption.WriterGeneration,
                privacy.Adoption.AdoptedAt, privacy.ValidUntil);
        var scope = new ModerationMeasuredOperationsProofScope(native.Authority.Binding.Id, native.Authority.Tenant.Id,
            profileId, binding, primary.ConfigurationDigest, primary.ProviderCode, primary.ProviderVersion,
            privacySource?.Source.Id, privacy?.Adoption.Id, privacySource?.Source.WriterGeneration ?? 0, purpose,
            privacySource is null ? TimeSpan.Zero
                : privacySource.Source.Terms.MaximumBackupHorizon + privacySource.Source.Terms.BackupSafetyMargin);
        var consumers = ImmutableArray.CreateBuilder<ModerationMeasuredOperationsConsumer>();
        foreach (var id in inventory.RequiredConsumers(binding.Capability, binding.Mode).Order(StringComparer.Ordinal))
        {
            ModerationMeasuredOperationsProofReference? reference = null;
            // An inventory entry is not proof. Each actual runtime requires its own exact
            // independently observed installation, signed receipt and approved catalogue.
            if (consumerConformance is not null)
            {
                var runtime = consumerSources?.SingleOrDefault(row => row.ConsumerId == id && row.Mode == binding.Mode);
                var measured = await session.AcquireMeasurementAsync(binding, id, observedAt, token);
                var conformance = runtime is null ? null : await consumerConformance.ReadAsync(
                    session, runtime, transactionId, binding, observedAt, token);
                var rechecked = await session.AcquireMeasurementAsync(binding, id, observedAt, token);
                if (measured is not null && rechecked is not null && conformance is not null
                    && measured.Receipt.ReceiptId == rechecked.Receipt.ReceiptId
                    && measured.TokenDigest == rechecked.TokenDigest
                    && measured.WriterGeneration == rechecked.WriterGeneration
                    && conformance.Binding == binding && conformance.VerifiedAt == observedAt)
                    reference = new(measured.Receipt.ReceiptId, measured.Revision, measured.WriterGeneration,
                        measured.AcceptedAt, conformance.ValidUntil < measured.ValidUntil ? conformance.ValidUntil : measured.ValidUntil);
            }
            consumers.Add(new(id, reference));
        }
        var recovery = await ProofAsync(session, transactionId, scope, ModerationMeasuredOperationsProofKind.Recovery, observedAt, token);
        var keys = purpose == 0 || privacyReference is null ? null
            : await ProofAsync(session, transactionId, scope, ModerationMeasuredOperationsProofKind.KeyConvergence, observedAt, token);
        var transport = binding.Mode == ModerationNativeMode.Embedded ? null
            : await ProofAsync(session, transactionId, scope, ModerationMeasuredOperationsProofKind.Transport, observedAt, token);
        var evidence = new ModerationMeasuredOperationsEvidence(inventory.Fingerprint, primary.ConfigurationDigest,
            primary.ProviderCode, primary.ProviderVersion, consumers.ToImmutable(), staffing, privacyReference,
            purpose, recovery, keys, transport);
        evidence.Validate(observedAt);
        var until = evidence.References().Select(row => row.ValidUntil).Append(definition.ValidUntil).Min();
        if (await session.AcquirePrimaryDatabaseAsync(observedAt, token) != primary
            || await AcquireOriginalAsync(session, transactionId, observedAt, token) is null
            || clock.GetUtcNow() >= until) return null;
        return new(definition.DefinitionId, evidence, until);
    }

    private async Task<ModerationMeasuredOperationsProofReference?> ProofAsync(
        ModerationConsumerEvidenceSession session, Guid transactionId, ModerationMeasuredOperationsProofScope scope,
        ModerationMeasuredOperationsProofKind kind, DateTimeOffset observedAt, CancellationToken token)
    {
        if (operationalProofs is null) return null;
        var current = await operationalProofs.AcquireCurrentAsync(session, transactionId, scope, kind, observedAt, token);
        var measured = await operationalProofs.ReadMeasurementAsync(session, transactionId, scope, kind, observedAt, token);
        var reference = ModerationMeasuredOperationsProofJoin.Match(current, measured, scope, kind, observedAt);
        if (reference is null) return null;
        var rechecked = await operationalProofs.AcquireCurrentAsync(session, transactionId, scope, kind, observedAt, token);
        var repeated = await operationalProofs.ReadMeasurementAsync(session, transactionId, scope, kind, observedAt, token);
        return JsonSerializer.Serialize(current) == JsonSerializer.Serialize(rechecked)
            && JsonSerializer.Serialize(measured) == JsonSerializer.Serialize(repeated)
            && await AcquireOriginalAsync(session, transactionId, observedAt, token) is not null
            && clock.GetUtcNow() < reference.ValidUntil ? reference : null;
    }

    private async Task<ModerationNativeEvidenceAcquisition?> AcquireOriginalAsync(
        ModerationConsumerEvidenceSession session, Guid transactionId, DateTimeOffset observedAt, CancellationToken token)
    {
        var native = await session.AcquireNativeAsync(transactionId, observedAt, token);
        if (native is null || !ReferenceEquals(native.Context, context)
            || !await context.ModerationQualificationEpochs.AsNoTracking().AnyAsync(row =>
                row.Id == native.Authority.Epoch.Id && row.HostBindingId == native.Authority.Binding.Id
                && row.TenantId == native.Authority.Tenant.Id && row.Revision == native.Authority.Epoch.Revision, token)) return null;
        if (context.Database.ProviderName == RelationalNamedLock.PostgreSqlProvider)
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.Transaction = native.Transaction.GetDbTransaction();
            command.CommandText = "SELECT current_setting('app.current_tenant_id', true)";
            if (!Guid.TryParse((await command.ExecuteScalarAsync(token))?.ToString(), out var tenant)
                || tenant != native.Authority.Tenant.Id) return null;
        }
        return await session.AcquireNativeAsync(transactionId, observedAt, token);
    }

    internal static RecordModerationMeasuredOperationsSource RecordRequest(
        ModerationMeasuredOperationsSource source, ModerationActivationBinding binding) => new()
        {
            OriginalEffect = OriginalAnchor(source),
            OperationId = Guid.Empty,
            Target = binding.Jurisdiction,
            ExpectedQualificationRevision = source.WriterGeneration - 1,
            SourceId = source.Id,
            ProfileId = source.ProfileId,
            Binding = binding,
            ReviewerHumanId = source.ReviewerHumanId,
            ReviewerControllerHumanId = source.ReviewerControllerHumanId,
            RecorderControllerHumanId = source.RecorderControllerHumanId,
            ValidUntil = source.ValidUntil
        };
    internal static AcceptModerationMeasuredOperationsSource AcceptanceRequest(
        ModerationMeasuredOperationsAcceptance acceptance, ModerationActivationBinding binding,
        ModerationMeasuredOperationsSource source) => new()
        {
            OriginalEffect = OriginalAnchor(source),
            OperationId = Guid.Empty,
            Target = binding.Jurisdiction,
            ExpectedQualificationRevision = acceptance.WriterGeneration - 1,
            SourceId = acceptance.SourceId,
            ExpectedSourceRevision = acceptance.SourceRevision,
            ExpectedSourceWriterGeneration = acceptance.SourceWriterGeneration,
            AcceptanceId = acceptance.Id,
            ValidUntil = acceptance.ValidUntil
        };
    internal static string Slot(ModerationSourceAuthority authority, ModerationActivationBinding binding) =>
        $"moderation.measured-operations:{authority.Binding.Id:N}:{authority.Tenant.Id:N}:{ModerationPolicyPublicationProjection.Digest(binding)}";
    internal static ModerationOriginalEffectAnchor? OriginalAnchor(ModerationMeasuredOperationsSource source) =>
        source.OriginalEffectId is { } effect
            ? new(effect, source.OriginalSnapshotHash!, source.OriginalEffectBindingHash!) : null;
    internal static bool MatchesPurpose(ModerationMeasuredOperationsSource source, ModerationOriginalRecipeAuthority? original) =>
        source.OriginalEffectId is null || original is not null
            && OriginalAnchor(source) == original.Anchor && source.OriginalCustodyUntil == original.ValidUntil
            && source.ValidUntil <= original.ValidUntil;
    private static int Purpose(ModerationReferenceRecognition recognition) => recognition switch
    {
        ModerationReferenceRecognition.Never => 0,
        ModerationReferenceRecognition.SeparatelyQualifiedFinalAction => 1,
        ModerationReferenceRecognition.SeparatelyQualifiedFinalEnrollment => 3,
        _ => throw new ArgumentException("Unknown recognition purpose.")
    };
    internal sealed record MeasuredLiveState(Guid DefinitionId, ModerationMeasuredOperationsEvidence Evidence, DateTimeOffset ValidUntil);
}

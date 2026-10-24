using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationRecognitionErasureRepository
{
    public Task<ModerationRecognitionOwnerContext?> ReadOwnerContextAsync(
        Guid escalationId, Guid authenticatedHuman, CancellationToken token) =>
        identity.ExecuteSerializedAsync<ModerationRecognitionOwnerContext?>(async ct =>
        {
            await using var transaction = await BeginAsync(ct);
            try
            {
                var discovered = await Rows<ModerationRecognitionEscalation>().SingleOrDefaultAsync(row =>
                    row.Id == escalationId && row.RecipientHumanId == authenticatedHuman, ct);
                if (discovered is null) return null;
                var pending = await RecoveryRequestAsync(discovered.IntentId, ct);
                var authority = await AcquireSubjectAsync(RecoveryRequest(pending), ct);
                await RelationalEntityRowFence.AcquireGlobalAsync<ModerationRecognitionEscalation>(
                    context, escalationId, ct);
                var escalation = await Rows<ModerationRecognitionEscalation>().SingleOrDefaultAsync(row =>
                    row.Id == escalationId && row.RecipientHumanId == authenticatedHuman, ct);
                if (escalation is null || clock.GetUtcNow() >= escalation.RetainUntil) return null;
                escalation.ValidatePersistedState();
                // Read after the original subject and pending fences, never from the notice snapshot.
                pending = await RecoveryRequestAsync(escalation.IntentId, ct);
                pending.ValidatePersistedState();
                if (await EscalationOwnerAsync(authority, pending, escalation, ct) is null) return null;
                var coverage = await RequiredDispositionSourcesAsync(authority, pending, ct);
                if (!coverage.Contains(escalation.SourceId)) return null;
                var (source, adoption) = await QualifyDispositionSourceAsync(authority, pending, escalation.SourceId, ct);
                var disposition = await Rows<ModerationRecognitionDisposition>().SingleOrDefaultAsync(row =>
                    row.IntentId == pending.IntentId && row.PendingRevision == pending.Revision
                    && row.SourceId == source.Id, ct);
                disposition?.ValidatePersistedState();
                var now = clock.GetUtcNow();
                ModerationRecognitionDispositionGrounds? grounds = pending.Failure switch
                {
                    ModerationRecognitionPendingFailure.KeyUnavailable =>
                        ModerationRecognitionDispositionGrounds.OriginalKeyRecoveryUnavailable,
                    ModerationRecognitionPendingFailure.StoreUnavailable =>
                        ModerationRecognitionDispositionGrounds.OriginalStoreRecoveryUnavailable,
                    ModerationRecognitionPendingFailure.IncompleteMaterial or ModerationRecognitionPendingFailure.OriginalMaterialChanged
                        when !pending.HasCompleteMaterial =>
                        ModerationRecognitionDispositionGrounds.CompleteOriginalMaterialUnavailable,
                    _ => null
                };
                if (pending.MaterialReceipt is not null && !pending.HasCompleteMaterial
                    && grounds != ModerationRecognitionDispositionGrounds.CompleteOriginalMaterialUnavailable)
                    grounds = null;
                var maximumValidUntil = source.ValidUntil < adoption.ValidUntil ? source.ValidUntil : adoption.ValidUntil;
                var canAssess = escalation.ActionRecoveryId is null && pending.AuthoritySequence is null
                    && now >= pending.EscalateAt && now < maximumValidUntil && grounds is not null
                    && disposition is null && authenticatedHuman == source.RecoveryOwnerHumanId;
                var canAuthorize = escalation.ActionRecoveryId is null && pending.AuthoritySequence is null
                    && authenticatedHuman == source.PrivacyOwnerHumanId && disposition is { Revision: 1 }
                    && disposition.RecoveryOwnerHumanId != authenticatedHuman
                    && disposition.PreservesRequest(pending) && disposition.SourceRevision == source.Revision
                    && disposition.SourceGeneration == source.WriterGeneration
                    && disposition.AdoptionId == adoption.Id && disposition.AdoptionRevision == adoption.Revision
                    && disposition.AdoptionGeneration == adoption.WriterGeneration
                    && disposition.CoverageCount == coverage.Length
                    && disposition.CoverageReceipt == ModerationRecognitionDisposition.ComputeCoverageReceipt(coverage)
                    && now >= disposition.AssessedAt && now < disposition.ValidUntil;
                if (canAuthorize)
                {
                    var acceptance = await Rows<ModerationRecognitionDispositionAcceptance>().SingleOrDefaultAsync(row =>
                        row.DispositionId == disposition!.Id
                        && row.Role == ModerationRecognitionDispositionAcceptanceRole.RecoveryOwner, ct);
                    canAuthorize = acceptance is not null && acceptance.Matches(disposition!);
                }
                var result = new ModerationRecognitionOwnerContext(escalation.Id, pending.IntentId, authenticatedHuman,
                    pending.Revision, pending.StateAt(now), pending.Failure, pending.ReceivedAt, pending.SettleBy,
                    pending.EscalateAt, coverage.Length, source.Id, source.Revision, source.WriterGeneration,
                    adoption.Id, adoption.Revision, adoption.WriterGeneration, maximumValidUntil, grounds,
                    disposition?.Id, disposition?.Revision, disposition?.Grounds, disposition?.AssessedAt,
                    disposition?.ValidUntil, disposition?.AuthorizedAt, canAssess, canAuthorize);
                await CommitAsync(transaction, ct);
                return result;
            }
            catch (CaptureUnavailable)
            {
                throw new InvalidOperationException("moderation_recognition_disposition_qualification_unavailable");
            }
            finally { context.ChangeTracker.Clear(); }
        }, token);
}

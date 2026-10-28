using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationProfileCollectionAssessmentRepository
{
    public async Task<ModerationProfileCollectionAssessmentSource?> RecordAsync(
        RecordModerationProfileCollectionAssessment request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await WriteAssessmentAsync(request, null, cancellationToken);
        return result?.Source;
    }

    public async Task<ModerationProfileCollectionAssessmentAdoption?> AdoptAsync(
        AdoptModerationProfileCollectionAssessment request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await WriteAssessmentAsync(null, request, cancellationToken);
        return result?.Adoption;
    }

    private async Task<AssessmentWrite?> WriteAssessmentAsync(
        RecordModerationProfileCollectionAssessment? proposal,
        AdoptModerationProfileCollectionAssessment? acceptance, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Assessment writing owns its complete clean identity unit of work.");
        var intent = proposal?.Original ?? acceptance!.Original;
        if (intent is null || !context.Database.IsRelational() || context.IsTenantFilterBypassed
            || context.TenantContext is null || context.TenantFilterTenantId is null
            || machine.IsMachineCaller || http.HttpContext?.User.Identity?.IsAuthenticated != true
            || !Enum.IsDefined(intent.Mode) || intent.ProfileId == Guid.Empty
            || proposal is not null && (proposal.Required is null || proposal.Required.Count > 3
                || proposal.Required.Any(kind => !Enum.IsDefined(kind))
                || proposal.Required.Distinct().Count() != proposal.Required.Count))
            return null;
        var originalTrust = context.ModerationTrustDomainId;
        ModerationProfileCollectionIndividualAcquisition? acquisition = null;
        AssessmentWrite? retained = null;
        var observations = new ModerationProfileCollectionSubjectObservationOwner(
            context, admin, machine, http, privacy, inventory, clock);
        try
        {
            return await privacy.ExecuteEnrollmentSerializedAsync(async token =>
            {
                token.ThrowIfCancellationRequested();
                context.ChangeTracker.Clear();
                acquisition = await ModerationProfileCollectionIndividualAcquisition.AcquireAsync(
                    context, admin, machine, http, privacy, inventory, clock, intent.ProfileId, intent.Mode, token);
                if (acquisition is null) throw new AssessmentUnavailable();
                var original = await observations.ReadCurrentAsync(acquisition, intent.ObservationId, token);
                if (original is null || !MatchesOriginal(original, intent)) throw new AssessmentUnavailable();
                var parents = await acquisition.ReadAssessmentParentsAsync(original, token);
                if (parents is null) throw new AssessmentUnavailable();
                var actor = acquisition.SubjectId; // Actual authenticated ACTOR, never the original SUBJECT.
                if (actor != (proposal is not null ? parents.ProcedureSource.OwnerHumanId : parents.ProcedureSource.ReviewerHumanId))
                    throw new AssessmentUnavailable();
                await using var slot = await RelationalNamedLock.AcquireTransactionAsync(
                    context, AssessmentSlot(original), token);
                var generation = await AssessmentGenerationAsync(original, token);
                var expected = proposal?.ExpectedGeneration ?? acceptance!.ExpectedGeneration;
                if (generation != expected || generation < 0 || generation >= long.MaxValue - 1)
                    throw new AssessmentUnavailable();
                var now = clock.GetUtcNow();
                ModerationProfileCollectionAssessmentSource source;
                ModerationProfileCollectionAssessmentAdoption? adoption = null;
                if (proposal is not null)
                {
                    if (await context.Set<ModerationProfileCollectionAssessmentSource>().AsNoTracking()
                            .AnyAsync(row => row.Id == proposal.SourceId, token))
                        throw new AssessmentUnavailable();
                    // A new ID cannot renew an accepted window from the same original observation.
                    // A genuinely new observation remains a separate original, not an echoed binding.
                    var acceptedSources = await AssessmentAdoptions(original).Select(row => row.Source).ToListAsync(token);
                    if (acceptedSources.Any(row => row.Binding.SourceId == original.Id))
                        throw new AssessmentUnavailable();
                    try
                    {
                        source = ModerationProfileCollectionAssessmentSource.Record(
                            proposal.SourceId, parents.ProcedureSource, parents.ProcedureAdoption,
                            parents.AgeSource, parents.AgeAdoption, original.ToBinding(), proposal.Required,
                            actor, generation + 1, now, proposal.ValidUntil);
                    }
                    catch (ArgumentException) { throw new AssessmentUnavailable(); }
                    // All navigation references already exist; never Add the original graph.
                    context.Attach(source.ProcedureSource);
                    context.Set<ModerationProfileCollectionAssessmentSource>().Add(source);
                }
                else
                {
                    var current = await LatestAssessmentSourceAsync(original, token);
                    if (current is null || current.Id != acceptance!.SourceId
                        || current.Revision != acceptance.ExpectedSourceRevision
                        || current.Generation != acceptance.ExpectedSourceGeneration
                        || generation != current.Generation
                        || await context.Set<ModerationProfileCollectionAssessmentAdoption>().AsNoTracking()
                            .AnyAsync(row => row.SourceId == current.Id || row.Id == acceptance.AdoptionId, token))
                        throw new AssessmentUnavailable();
                    source = current;
                    try
                    {
                        adoption = ModerationProfileCollectionAssessmentAdoption.Adopt(
                            acceptance.AdoptionId, source, parents.ProcedureSource, parents.ProcedureAdoption,
                            parents.AgeSource, parents.AgeAdoption, original.ToBinding(), actor,
                            generation + 1, now, acceptance.ValidUntil);
                    }
                    catch (ArgumentException) { throw new AssessmentUnavailable(); }
                    context.Attach(source);
                    context.Set<ModerationProfileCollectionAssessmentAdoption>().Add(adoption);
                }
                retained = new(acquisition, original, source, adoption);
                await context.SaveChangesAsync(token);
                if (!await AssessmentCurrentAsync(retained, observations, token)) throw new AssessmentUnavailable();
                return retained;
            }, async token =>
            {
                // Retained through the actual identity owner's outer primary commit.
                // No nested callback, detached transaction or actor-as-subject reconstruction.
                token.ThrowIfCancellationRequested();
                if (retained is null || !await AssessmentCurrentAsync(retained, observations, token))
                    throw new AssessmentUnavailable();
                token.ThrowIfCancellationRequested();
            }, cancellationToken);
        }
        catch (AssessmentUnavailable) { return null; }
        catch (DbUpdateConcurrencyException) { return null; }
        finally
        {
            acquisition?.Dispose();
            context.ChangeTracker.Clear();
            context.ModerationTrustDomainId = originalTrust;
        }
    }

    private async Task<bool> AssessmentCurrentAsync(AssessmentWrite original,
        ModerationProfileCollectionSubjectObservationOwner observations, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var observation = await observations.ReadCurrentAsync(original.Acquisition, original.Observation.Id, token);
        if (observation is null || !observation.Matches(original.Observation)) return false;
        var parents = await original.Acquisition.ReadAssessmentParentsAsync(observation, token);
        if (parents is null) return false;
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(
            context, AssessmentSlot(observation), token);
        var source = await LatestAssessmentSourceAsync(observation, token);
        if (source is null) return false;
        ModerationProfileCollectionAssessmentAdoption? currentAdoption = null;
        try
        {
            source.ValidatePersistedState();
            if (!source.Matches(original.Source)
                || !source.MatchesCurrentMetadata(parents.ProcedureSource, parents.ProcedureAdoption,
                    parents.AgeSource, parents.AgeAdoption, observation.ToBinding(), clock.GetUtcNow()))
                return false;
            var generation = await AssessmentGenerationAsync(observation, token);
            var actor = original.Acquisition.SubjectId;
            if (original.Adoption is null)
            {
                if (actor != source.ProcedureSource.OwnerHumanId || generation != source.Generation
                    || await context.Set<ModerationProfileCollectionAssessmentAdoption>().AsNoTracking()
                        .AnyAsync(row => row.SourceId == source.Id, token))
                    return false;
            }
            else
            {
                await RelationalEntityRowFence.AcquireGlobalAsync<ModerationProfileCollectionAssessmentAdoption>(
                    context, original.Adoption.Id, token);
                var adoption = await context.Set<ModerationProfileCollectionAssessmentAdoption>().AsNoTracking()
                    .SingleOrDefaultAsync(row => row.SourceId == source.Id, token);
                var prior = original.Adoption;
                if (adoption is null || actor != source.ProcedureSource.ReviewerHumanId
                    || generation != prior.Generation || adoption.Id != prior.Id
                    || adoption.Generation != prior.Generation || adoption.Revision != prior.Revision
                    || adoption.SourceRevision != prior.SourceRevision || adoption.SourceGeneration != prior.SourceGeneration
                    || adoption.AdoptedByHumanId != prior.AdoptedByHumanId || adoption.AdoptedAt != prior.AdoptedAt
                    || adoption.ValidUntil != prior.ValidUntil || adoption.WithdrawnAt is not null
                    || adoption.WithdrawnByHumanId is not null
                    || !adoption.MatchesCurrentMetadata(source, parents.ProcedureSource, parents.ProcedureAdoption,
                        parents.AgeSource, parents.AgeAdoption, observation.ToBinding(), clock.GetUtcNow()))
                    return false;
                currentAdoption = adoption;
            }
        }
        catch (ArgumentException) { return false; }
        // Recheck original identity/policy, then ALL finite windows after the last async read.
        if (!await original.Acquisition.RevalidateAsync(token)) return false;
        var now = clock.GetUtcNow();
        return observation.Covers(now)
            && source.MatchesCurrentMetadata(parents.ProcedureSource, parents.ProcedureAdoption,
                parents.AgeSource, parents.AgeAdoption, observation.ToBinding(), now)
            && (currentAdoption is null || currentAdoption.MatchesCurrentMetadata(source,
                parents.ProcedureSource, parents.ProcedureAdoption, parents.AgeSource,
                parents.AgeAdoption, observation.ToBinding(), now));
    }

    private static bool MatchesOriginal(ModerationProfileCollectionSubjectObservation row,
        ModerationProfileCollectionAssessmentOriginal intent) =>
        row.Id == intent.ObservationId && row.Revision == intent.ExpectedObservationRevision
        && row.Generation == intent.ExpectedObservationGeneration && row.ToBinding().Jurisdiction == intent.Jurisdiction
        && row.PolicyId == intent.PolicyId && row.PolicyRevision == intent.PolicyRevision
        && row.PublicationId == intent.PublicationId && row.ProfileId == intent.ProfileId && row.ModeCode == (int)intent.Mode
        && row.ProcedureSourceId == intent.ProcedureSourceId && row.ProcedureSourceRevision == intent.ExpectedProcedureSourceRevision
        && row.ProcedureSourceWriterGeneration == intent.ExpectedProcedureSourceWriterGeneration
        && row.ProcedureAdoptionId == intent.ProcedureAdoptionId && row.ProcedureAdoptionRevision == intent.ExpectedProcedureAdoptionRevision
        && row.ProcedureAdoptionWriterGeneration == intent.ExpectedProcedureAdoptionWriterGeneration;

    private IQueryable<ModerationProfileCollectionAssessmentSource> AssessmentSources(
        ModerationProfileCollectionSubjectObservation original) =>
        context.Set<ModerationProfileCollectionAssessmentSource>().AsNoTracking().Where(row =>
            row.SubjectHumanId == original.SubjectHumanId && row.ProcedureSourceId == original.ProcedureSourceId);

    private IQueryable<ModerationProfileCollectionAssessmentAdoption> AssessmentAdoptions(
        ModerationProfileCollectionSubjectObservation original) =>
        context.Set<ModerationProfileCollectionAssessmentAdoption>().AsNoTracking().Where(row =>
            row.Source.SubjectHumanId == original.SubjectHumanId && row.Source.ProcedureSourceId == original.ProcedureSourceId);

    private async Task<long> AssessmentGenerationAsync(
        ModerationProfileCollectionSubjectObservation original, CancellationToken token)
    {
        var latest = await AssessmentSources(original).Select(row => row.Generation)
            .Concat(AssessmentAdoptions(original).Select(row => row.Generation))
            .OrderByDescending(generation => generation).Take(2).ToListAsync(token);
        return latest.Count == 0 ? 0
            : latest.Count == 2 && latest[0] == latest[1] ? -1 : latest[0];
    }

    private async Task<ModerationProfileCollectionAssessmentSource?> LatestAssessmentSourceAsync(
        ModerationProfileCollectionSubjectObservation original, CancellationToken token)
    {
        // Select the latest identity BEFORE qualifying it; no latest-valid or accepted fallback.
        var latest = await AssessmentSources(original).OrderByDescending(row => row.Generation)
            .Select(row => new { row.Id, row.Generation }).Take(2).ToListAsync(token);
        if (latest.Count == 0 || latest.Count == 2 && latest[0].Generation == latest[1].Generation) return null;
        var id = latest[0].Id;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationProfileCollectionAssessmentSource>(context, id, token);
        return await AssessmentSources(original).SingleOrDefaultAsync(row => row.Id == id, token);
    }

    private static string AssessmentSlot(ModerationProfileCollectionSubjectObservation original) =>
        $"moderation.profile-assessment:{original.HostBindingId:N}:{original.TenantId:N}:{original.SubjectHumanId:N}:{original.ProcedureSourceId:N}";

    private sealed record AssessmentWrite(
        ModerationProfileCollectionIndividualAcquisition Acquisition,
        ModerationProfileCollectionSubjectObservation Observation,
        ModerationProfileCollectionAssessmentSource Source,
        ModerationProfileCollectionAssessmentAdoption? Adoption);

    private sealed class AssessmentUnavailable : Exception;
}

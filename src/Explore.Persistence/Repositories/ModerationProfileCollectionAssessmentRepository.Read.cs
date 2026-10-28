using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationProfileCollectionAssessmentRepository
{
    /// <summary>
    /// Returns the actual independently accepted native entity on the retained real acquisition.
    /// Latest source identity is selected BEFORE qualification; no stored consent snapshot,
    /// client-built accepted reference, latest-valid lookup or accepted fallback is authority.
    /// The caller retains the acquisition and repeats this read at the true outer commit.
    /// </summary>
    internal async Task<ModerationProfileCollectionAssessmentAdoption?> ReadCurrentAsync(
        ModerationProfileCollectionIndividualAcquisition acquisition,
        ModerationProfileSubjectConsentAssessment expected, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (expected?.Original is not { } intent || !acquisition.OwnsContext(context)) return null;
        var observations = new ModerationProfileCollectionSubjectObservationOwner(
            context, admin, machine, http, privacy, inventory, clock);
        var observation = await observations.ReadCurrentAsync(acquisition, intent.ObservationId, token);
        if (observation is null || !MatchesOriginal(observation, intent)) return null;
        var parents = await acquisition.ReadAssessmentParentsAsync(observation, token);
        if (parents is null) return null;
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(
            context, AssessmentSlot(observation), token);
        var source = await LatestAssessmentSourceAsync(observation, token);
        if (source is null || source.Id != expected.SourceId
            || source.Revision != expected.ExpectedSourceRevision
            || source.Generation != expected.ExpectedSourceGeneration)
            return null;
        // Resolve the exact latest source's live independent acceptance, never search for
        // another accepted source when this one is invalid, superseded, tied or unaccepted.
        var discovered = await AssessmentAdoptions(observation)
            .Where(row => row.SourceId == source.Id)
            .Select(row => row.Id).Take(2).ToListAsync(token);
        if (discovered.Count != 1 || discovered[0] != expected.AdoptionId) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationProfileCollectionAssessmentAdoption>(
            context, discovered[0], token);
        var adoption = await AssessmentAdoptions(observation)
            .SingleOrDefaultAsync(row => row.Id == discovered[0], token);
        if (adoption is null || adoption.Revision != expected.ExpectedAdoptionRevision
            || adoption.Generation != expected.ExpectedAdoptionGeneration
            || adoption.SourceId != source.Id || adoption.SourceRevision != source.Revision
            || adoption.SourceGeneration != source.Generation
            || await AssessmentGenerationAsync(observation, token) != adoption.Generation)
            return null;
        try
        {
            source.ValidatePersistedState();
            adoption.ValidatePersistedState();
            if (!adoption.Source.Matches(source)
                || !adoption.MatchesCurrentMetadata(source, parents.ProcedureSource, parents.ProcedureAdoption,
                    parents.AgeSource, parents.AgeAdoption, observation.ToBinding(), clock.GetUtcNow()))
                return null;
        }
        catch (ArgumentException) { return null; }
        // Reread authentic originals after subordinate qualification. All windows are the
        // stored originals, not the later acquisition's time or a freshly minted reference.
        var current = await observations.ReadCurrentAsync(acquisition, observation.Id, token);
        if (current is null || !current.Matches(observation)) return null;
        var currentParents = await acquisition.ReadAssessmentParentsAsync(current, token);
        if (currentParents is null || !await acquisition.RevalidateAsync(token)) return null;
        token.ThrowIfCancellationRequested();
        var now = clock.GetUtcNow();
        return observation.Covers(now) && adoption.MatchesCurrentMetadata(source,
            currentParents.ProcedureSource, currentParents.ProcedureAdoption,
            currentParents.AgeSource, currentParents.AgeAdoption, current.ToBinding(), now)
            ? adoption : null;
    }
}

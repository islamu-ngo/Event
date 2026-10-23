using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Services;

public sealed partial class ModerationProfileCollectionAdmissionOwner
{
    public async Task<IReadOnlyList<ModerationProfileCollectionOption>> ReadOptionsAsync(Guid subjectId,
        CancellationToken cancellationToken = default)
    {
        if (current is not null || context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Profile readiness requires its own clean native owner.");
        var nominations = await privacy.ExecuteEnrollmentSerializedAsync(async token =>
        {
            await RequireSelfAsync(subjectId, token);
            var slots = await context.Set<ModerationProfileCollectionSubjectObservation>().AsNoTracking()
                .Where(row => row.SubjectHumanId == subjectId && row.TenantId == context.TenantFilterTenantId)
                .Select(row => new { row.ProfileId, row.ModeCode }).Distinct()
                .OrderBy(row => row.ProfileId).ThenBy(row => row.ModeCode).Take(33).ToListAsync(token);
            if (slots.Count > 32) return new List<ModerationProfileCollectionOption>();
            var found = new List<ModerationProfileCollectionOption>();
            foreach (var slot in slots)
            {
                // Pick identity before qualification: never fall back to an older valid observation.
                var latest = await context.Set<ModerationProfileCollectionSubjectObservation>().AsNoTracking()
                    .Where(row => row.SubjectHumanId == subjectId && row.TenantId == context.TenantFilterTenantId
                        && row.ProfileId == slot.ProfileId && row.ModeCode == slot.ModeCode)
                    .OrderByDescending(row => row.ObservationSequence).Take(2).ToListAsync(token);
                if (latest.Count == 0 || latest.Count == 2 && latest[0].ObservationSequence == latest[1].ObservationSequence) continue;
                var observation = latest[0];
                var sources = await context.Set<ModerationProfileCollectionAssessmentSource>().AsNoTracking()
                    .Where(row => row.SubjectHumanId == subjectId && row.ProcedureSourceId == observation.ProcedureSourceId)
                    .OrderByDescending(row => row.Generation).Take(2).ToListAsync(token);
                if (sources.Count == 0 || sources.Count == 2 && sources[0].Generation == sources[1].Generation) continue;
                var source = sources[0];
                var adoptions = await context.Set<ModerationProfileCollectionAssessmentAdoption>().AsNoTracking()
                    .Where(row => row.SourceId == source.Id).Take(2).ToListAsync(token);
                if (adoptions.Count != 1) continue;
                var adoption = adoptions[0];
                var consents = await context.Set<ModerationProfileSubjectConsent>().AsNoTracking()
                    .Where(row => row.SubjectHumanId == subjectId && row.Source.Id == observation.ProcedureSourceId)
                    .OrderByDescending(row => row.Generation).Take(2).ToListAsync(token);
                if (consents.Count == 2 && consents[0].Generation == consents[1].Generation) continue;
                var consent = source.Required.Contains(ModerationCollectionPrerequisiteKind.SubjectConsent)
                    ? consents.FirstOrDefault()?.Id : null;
                var original = new ModerationProfileCollectionAssessmentOriginal(observation.Id, observation.Revision,
                    observation.Generation, observation.ToBinding().Jurisdiction, observation.PolicyId,
                    observation.PolicyRevision, observation.PublicationId, observation.ProfileId,
                    (ModerationNativeMode)observation.ModeCode, observation.ProcedureSourceId,
                    observation.ProcedureSourceRevision, observation.ProcedureSourceWriterGeneration,
                    observation.ProcedureAdoptionId, observation.ProcedureAdoptionRevision,
                    observation.ProcedureAdoptionWriterGeneration);
                found.Add(new(new(original, source.Id, source.Revision, source.Generation,
                    adoption.Id, adoption.Revision, adoption.Generation), consent, observation.ValidUntil));
            }
            return found;
        }, token => RequireSelfAsync(subjectId, token), cancellationToken);

        // Discovery supplied no authority. Reacquire every complete graph through the actual consumer owner.
        var options = new List<ModerationProfileCollectionOption>();
        foreach (var nomination in nominations)
        {
            ModerationProfileCollectionOption? qualified = null;
            if (await ExecuteAsync(nomination.Assessment, nomination.ConsentId, async (admission, token) =>
                {
                    var declared = await ReadDeclaredAgeAsync(admission, token);
                    qualified = nomination with { ValidUntil = admission.ValidUntil, DeclaredAge = declared };
                }, cancellationToken))
                options.Add(qualified!);
        }
        cancellationToken.ThrowIfCancellationRequested();
        var completedAt = clock.GetUtcNow();
        return options.Where(option => completedAt < option.ValidUntil)
            .Select(option => option.DeclaredAge is { } display && completedAt >= display.ValidUntil
                ? option with { DeclaredAge = null } : option).ToList().AsReadOnly();
    }

    private async Task RequireSelfAsync(Guid subjectId, CancellationToken token)
    {
        if (subjectId == Guid.Empty || http.HttpContext?.User.Identity?.IsAuthenticated != true
            || machine.IsMachineCaller || await admin.ResolveUserIdAsync(token) != subjectId
            || await privacy.IsSubjectFencedAsync(subjectId, token)
            || !await context.Users.AsNoTracking().AnyAsync(row => row.Id == subjectId && !row.IsDeleted, token))
            throw new AuthorizationException("Only the current Human may manage this private profile.");
    }
}

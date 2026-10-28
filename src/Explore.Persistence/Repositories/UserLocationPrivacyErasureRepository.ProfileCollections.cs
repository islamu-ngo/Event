using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class UserLocationPrivacyErasureRepository
{
    private async Task EraseProfileCollectionMetadataAsync(Guid subjectId, CancellationToken token)
    {
        // The exact native saga was verified by the caller; current policy filters must not veto erasure.
        const string reason = TenantFilterBypassReasons.UserPrivacyErasure;
        bool changed = await EraseProfileCollectionRowsAsync(dbContext.Set<ModerationProfileSubjectConsent>()
            .IgnoreAllFilters(reason).Where(row => row.SubjectHumanId == subjectId), token);
        changed |= await EraseProfileCollectionRowsAsync(dbContext.Set<ModerationProfileCollectionAssessmentAdoption>()
            .IgnoreAllFilters(reason).Where(row => row.Source.SubjectHumanId == subjectId), token);
        changed |= await EraseProfileCollectionRowsAsync(dbContext.Set<ModerationProfileCollectionAssessmentSource>()
            .IgnoreAllFilters(reason).Where(row => row.SubjectHumanId == subjectId), token);
        changed |= await EraseProfileCollectionRowsAsync(dbContext.Set<ModerationProfileCollectionSubjectObservation>()
            .IgnoreAllFilters(reason).Where(row => row.SubjectHumanId == subjectId), token);
        if (changed)
            await dbContext.RecordModerationRecoveryBulkMutationAsync(token);
    }

    private async Task<bool> EraseProfileCollectionRowsAsync<T>(IQueryable<T> rows, CancellationToken token)
        where T : class
    {
        const int batchSize = 100;
        bool changed = false;
        while (true)
        {
            Guid[] ids = await rows.AsNoTracking().OrderBy(row => EF.Property<Guid>(row, "Id"))
                .Select(row => EF.Property<Guid>(row, "Id")).Take(batchSize).ToArrayAsync(token);
            if (ids.Length == 0) return changed;
            var tracked = dbContext.ChangeTracker.Entries<T>()
                .Where(entry => ids.Contains(entry.Property<Guid>("Id").CurrentValue)).ToArray();
            if (tracked.Any(entry => entry.State != EntityState.Unchanged))
                throw new InvalidOperationException("Profile metadata erasure cannot discard pending native changes.");
            int removed = await rows.Where(row => ids.Contains(EF.Property<Guid>(row, "Id")))
                .ExecuteDeleteAsync(token);
            if (removed != ids.Length)
                throw new InvalidOperationException("Profile metadata erasure did not settle its selected batch.");
            // Otherwise a later tracked PII delete could cascade the already-disposed rows a second time.
            foreach (var entry in tracked) entry.State = EntityState.Detached;
            changed = true;
        }
    }
}

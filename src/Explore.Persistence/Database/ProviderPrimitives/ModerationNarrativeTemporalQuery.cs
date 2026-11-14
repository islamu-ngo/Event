using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class ModerationNarrativeTemporalQuery
{
    internal static IOrderedQueryable<ModerationCaseIntakeAcknowledgement> OrderOriginalAcceptance(
        ExploreDbContext context, IQueryable<ModerationCaseIntakeAcknowledgement> query) =>
        // Acceptance timestamps are domain-validated UTC; SQLite stores their sortable text form.
        context.Database.IsSqlite()
            ? query.OrderBy(row => (string)(object)row.AcceptedAt).ThenBy(row => row.Id)
            : query.OrderBy(row => row.AcceptedAt).ThenBy(row => row.Id);
}

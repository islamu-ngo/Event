using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Privacy.ErasureAuthority;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class SubjectPrivacyErasureSettlementSelection
{
    internal static ISubjectPrivacyErasureSettlement? Create(ExploreDbContext context) =>
        context.Database.IsNpgsql() ? new PostgresSubjectPrivacyErasureSettlement(context) : null;
}

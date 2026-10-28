using Explore.Domain.Interfaces;
using Explore.Domain.Learning;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class LearningLifecyclePersistence
{
    internal static async Task PurgeSubjectAsync(
        ExploreDbContext context, Guid subjectId, CancellationToken cancellationToken)
    {
        var states = context.UserLearningStates.IgnoreTenantFilter(TenantFilterBypassReasons.UserPrivacyErasure)
            .Where(value => value.UserId == subjectId);
        if (context.Database.IsSqlServer())
        {
            // ExecuteDelete bypasses client cascades; SQL Server retains NO ACTION FKs.
            await context.LearningPracticeStates.IgnoreTenantFilter(TenantFilterBypassReasons.UserPrivacyErasure)
                .Where(child => states.Any(state => state.TenantId == child.TenantId
                    && state.Id == child.UserLearningStateId))
                .ExecuteDeleteAsync(cancellationToken);
            await context.LearningTourProgress.IgnoreTenantFilter(TenantFilterBypassReasons.UserPrivacyErasure)
                .Where(child => states.Any(state => state.TenantId == child.TenantId
                    && state.Id == child.UserLearningStateId))
                .ExecuteDeleteAsync(cancellationToken);
        }
        // PostgreSQL Global authority needs rights only on the learner root.
        await states.ExecuteDeleteAsync(cancellationToken);
    }

    internal static async Task PrepareTenantDeletionAsync(
        ExploreDbContext context, Guid tenantId, CancellationToken cancellationToken)
    {
        if (!context.Database.IsSqlServer())
            return;

        await context.LearningPracticeStates
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .Where(row => row.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
        await context.LearningTourProgress
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .Where(row => row.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
        await context.UserLearningStates
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .Where(row => row.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
        await context.TenantLearningSpaces
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .Where(row => row.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
        foreach (var entry in context.ChangeTracker.Entries()
                     .Where(entry => entry.Entity is ITenantEntity owned && owned.TenantId == tenantId
                         && entry.Entity is LearningPracticeState or LearningTourProgress
                             or UserLearningState or TenantLearningSpace)
                     .ToArray())
            entry.State = EntityState.Detached;
    }
}

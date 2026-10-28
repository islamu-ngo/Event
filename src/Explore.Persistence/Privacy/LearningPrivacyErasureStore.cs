using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Privacy;

public sealed class LearningPrivacyErasureStore(ExploreDbContext context) : ILearningPrivacyErasureStore
{
    public async Task PurgeAsync(PrivacyErasureIntent intent, CancellationToken cancellationToken)
    {
        RequireProtectedTransaction(intent);
        await LearningLifecyclePersistence.PurgeSubjectAsync(context, intent.SubjectId, cancellationToken);
    }

    public async Task RequireAbsentAsync(PrivacyErasureIntent intent, CancellationToken cancellationToken)
    {
        RequireProtectedTransaction(intent);
        if (await context.UserLearningStates.IgnoreTenantFilter(TenantFilterBypassReasons.UserPrivacyErasure)
            .AsNoTracking().AnyAsync(value => value.UserId == intent.SubjectId, cancellationToken))
            throw new InvalidOperationException("learning_erasure_incomplete");
    }

    private void RequireProtectedTransaction(PrivacyErasureIntent intent)
    {
        context.RequireBoundTenantLease();
        if (intent.SubjectKind != PrivacyErasureSubjectKind.User || intent.SubjectId == Guid.Empty
            || context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Learning erasure requires the protected subject transaction.");
    }
}

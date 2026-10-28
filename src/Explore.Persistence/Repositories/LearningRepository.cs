using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Domain.Learning;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class LearningRepository(ExploreDbContext context) : ILearningRepository
{
    public Task<TenantLearningSpace?> GetSpaceAsync(
        Guid tenantId, bool tracked, CancellationToken cancellationToken)
    {
        var query = context.TenantLearningSpaces.Where(value => value.TenantId == tenantId);
        return (tracked ? query : query.AsNoTracking()).SingleOrDefaultAsync(cancellationToken);
    }

    public Task<UserLearningState?> GetStateAsync(
        Guid tenantId, Guid userId, bool tracked, CancellationToken cancellationToken)
    {
        var query = context.UserLearningStates
            .Where(value => value.TenantId == tenantId && value.UserId == userId)
            .Include(value => value.Tours).Include(value => value.Practices).AsSplitQuery();
        return (tracked ? query : query.AsNoTracking()).SingleOrDefaultAsync(cancellationToken);
    }

    public Task<LearningPracticeState?> GetPracticeAsync(
        Guid tenantId, Guid userId, Guid practiceId, CancellationToken cancellationToken) =>
        context.UserLearningStates.AsNoTracking()
            .Where(value => value.TenantId == tenantId && value.UserId == userId)
            .SelectMany(value => value.Practices)
            .SingleOrDefaultAsync(value => value.Id == practiceId && value.TenantId == tenantId, cancellationToken);

    public async Task AddSpaceAsync(TenantLearningSpace space, CancellationToken cancellationToken)
    {
        RequireTenantWrite(space.TenantId);
        await context.TenantLearningSpaces.AddAsync(space, cancellationToken);
    }

    public async Task AddStateAsync(UserLearningState state, CancellationToken cancellationToken)
    {
        RequireTenantWrite(state.TenantId);
        await context.UserLearningStates.AddAsync(state, cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw Conflict(exception);
        }
        catch (DbUpdateException exception) when (LearningWriteConflict.IsUniqueViolation(exception))
        {
            throw Conflict(exception);
        }
    }

    private void RequireTenantWrite(Guid tenantId)
    {
        context.RequireBoundTenantLease();
        if (context.IsTenantFilterBypassed || context.TenantFilterTenantId != tenantId)
            throw new UnauthorizedAccessException("Learning writes require their resolved tenant.");
    }

    private static ConcurrencyConflictException Conflict(Exception exception) => new(
        "learning_conflict", "Learning changed before this operation settled. Refresh the private context.",
        innerException: exception);
}

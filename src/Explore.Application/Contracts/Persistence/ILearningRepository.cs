using Explore.Domain.Learning;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Loads learning entities by the resolved tenant and internal owner.</summary>
public interface ILearningRepository
{
    Task<TenantLearningSpace?> GetSpaceAsync(
        Guid tenantId, bool tracked, CancellationToken cancellationToken);

    Task<UserLearningState?> GetStateAsync(
        Guid tenantId, Guid userId, bool tracked, CancellationToken cancellationToken);

    Task<LearningPracticeState?> GetPracticeAsync(
        Guid tenantId, Guid userId, Guid practiceId, CancellationToken cancellationToken);

    Task AddSpaceAsync(TenantLearningSpace space, CancellationToken cancellationToken);

    Task AddStateAsync(UserLearningState state, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

using Explore.Application.Features.Learning;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Owns fixed private reads under the original authenticated human and selected tenant.</summary>
public interface IHumanLearningContextReader
{
    Task<LearningOperationState> ReadContextAsync(
        string contextKey, Guid? eventContextId, CancellationToken cancellationToken);

    Task<LearningOperationState> ReadPracticeAsync(
        Guid practiceId, CancellationToken cancellationToken);
}

using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Removes private learning for a recognized subject inside the existing protected
/// erasure transaction. It neither grants erasure authority nor records coverage.
/// </summary>
public interface ILearningPrivacyErasureStore
{
    Task PurgeAsync(PrivacyErasureIntent intent, CancellationToken cancellationToken);

    Task RequireAbsentAsync(PrivacyErasureIntent intent, CancellationToken cancellationToken);
}

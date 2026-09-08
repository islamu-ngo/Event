using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IPrivacyErasureReplayCheckpointRepository
{
    Task<PrivacyErasureReplayCheckpoint> AppendAsync(
        PrivacyErasureReplayCheckpoint checkpoint,
        CancellationToken cancellationToken);
    Task<PrivacyErasureReplayCheckpoint?> GetLatestAsync(CancellationToken cancellationToken);
}

using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class PrivacyErasureReplayCheckpointRepository(ExploreDbContext dbContext)
    : IPrivacyErasureReplayCheckpointRepository
{
    public async Task<PrivacyErasureReplayCheckpoint> AppendAsync(
        PrivacyErasureReplayCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        PrivacyErasureReplayCheckpoint? latest =
            await GetLatestAsync(cancellationToken);
        bool startsChain = latest is null
            && checkpoint.AuthoritySequence == 1
            && checkpoint.PreviousCheckpointId is null;
        bool continuesChain = latest is not null
            && checkpoint.AuthoritySequence == latest.AuthoritySequence + 1
            && checkpoint.PreviousCheckpointId == latest.Id;
        if (!startsChain && !continuesChain)
        {
            throw new InvalidOperationException(
                "A local erasure checkpoint must append the next contiguous authority sequence.");
        }

        dbContext.PrivacyErasureReplayCheckpoints.Add(checkpoint);
        await dbContext.SaveChangesAsync(cancellationToken);
        return checkpoint;
    }

    public Task<PrivacyErasureReplayCheckpoint?> GetLatestAsync(
        CancellationToken cancellationToken) =>
        dbContext.PrivacyErasureReplayCheckpoints
            .AsNoTracking()
            .OrderByDescending(item => item.AuthoritySequence)
            .ThenByDescending(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);
}

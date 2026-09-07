using Explore.Application.Features.Federation.Atproto.Models;

namespace Explore.Application.Contracts.Infrastructure;

public interface IAtprotoPdsSnapshotGateway
{
    Task<AtprotoPdsSnapshotFetchResult> FetchAsync(
        string did,
        long snapshotVersion,
        CancellationToken cancellationToken);
}

using Explore.Application.Features.Federation.Atproto.Models;
using Explore.Application.Models.Storage;

namespace Explore.Application.Contracts.Infrastructure;

public interface IAtprotoThumbnailBlobGateway
{
    Task<FileStorageWriteResult?> FetchAndStageAsync(
        AtprotoThumbnailBlobCandidate? candidate,
        Guid tenantId,
        CancellationToken cancellationToken);

    Task CleanupAsync(
        FileStorageWriteResult staged,
        CancellationToken cancellationToken);
}

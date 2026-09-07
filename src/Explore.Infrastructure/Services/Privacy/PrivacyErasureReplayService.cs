using Explore.Application.Contracts.Services;

namespace Explore.Infrastructure.Services.Privacy;

public sealed class PrivacyErasureReplayService(
    IPrivacyErasureService erasureService) : IPrivacyErasureReplayService
{
    public Task ReplayAsync(CancellationToken cancellationToken) =>
        erasureService.ReplayPendingAsync(cancellationToken);
}

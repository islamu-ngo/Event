namespace Explore.Application.Contracts.Services;

public interface IPrivacyErasureReplayService
{
    Task ReplayAsync(CancellationToken cancellationToken);
}

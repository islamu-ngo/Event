namespace Explore.Application.Contracts.Services;

public interface IJwtAuthorityRefreshNotifier
{
    Task ReloadAsync(CancellationToken ct = default);
}

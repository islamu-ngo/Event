namespace Explore.Application.Contracts.Services;

/// <summary>
/// Commits a closed deployment epoch before a known writer performs any remote mutation.
/// Successful publication does not activate resource authorization.
/// </summary>
public interface IEventResourcePolicyPublicationFence
{
    Task BeginPublicationAsync(string grpcEndpoint, CancellationToken cancellationToken);
}

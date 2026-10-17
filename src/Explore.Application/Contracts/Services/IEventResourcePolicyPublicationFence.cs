using Explore.Application.Features.InstanceOnboarding.Requests.Commands;

namespace Explore.Application.Contracts.Services;

/// <summary>
/// Commits a closed deployment epoch before a known writer performs any remote mutation.
/// Successful publication does not activate resource authorization.
/// </summary>
public interface IEventResourcePolicyPublicationFence
{
    Task<IEventResourceProviderPublicationSelection> BeginPublicationAsync(
        SyncAuthorizationPolicyPackageCommand command, CancellationToken cancellationToken);
}

/// <summary>Nonsecret target metadata; the package service accepts only its own issued selection.</summary>
public interface IEventResourceProviderPublicationSelection
{
    string GrpcEndpoint { get; }
}

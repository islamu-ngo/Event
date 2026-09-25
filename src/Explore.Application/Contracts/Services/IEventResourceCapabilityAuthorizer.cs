using Explore.Application.Contracts.Infrastructure;

namespace Explore.Application.Contracts.Services;

/// <summary>Native capability boundary for fresh, server-derived event resource authority.</summary>
public interface IEventResourceCapabilityAuthorizer
{
    Task<IReadOnlyList<AuthorizationDecision>> AuthorizeBatchAsync(
        IReadOnlyList<AuthorizationRequest> requests, CancellationToken cancellationToken);
}

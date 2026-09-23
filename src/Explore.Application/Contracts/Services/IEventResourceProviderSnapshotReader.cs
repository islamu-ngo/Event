using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Services;

/// <summary>Reads fresh configuration and activation facts in the caller's transaction, without secret or Admin API access.</summary>
public interface IEventResourceProviderSnapshotReader
{
    Task<EventResourceProviderSnapshot?> ReadAsync(Guid tenantId, CancellationToken cancellationToken);
}

public enum EventResourceProviderMode { Local, Remote }

/// <summary>ActivationEpoch is a local coordination fence, not an observed remote policy revision.</summary>
public sealed record EventResourceProviderSnapshot(
    EventResourceProviderMode Mode,
    string Scope,
    string PolicyVersion,
    string? GrpcEndpoint = null,
    Guid? DeploymentId = null,
    long ActivationEpoch = 0,
    EventResourceProviderActivationStateEnum? ActivationState = null,
    EventResourceParentPolicyRoute? ParentEventPolicy = null)
{
    public bool IsUsable => Scope is not null && Scope == Scope.Trim()
        && !string.IsNullOrWhiteSpace(PolicyVersion)
        && (Mode == EventResourceProviderMode.Local
            || Mode == EventResourceProviderMode.Remote
                && Uri.TryCreate(GrpcEndpoint, UriKind.Absolute, out var endpoint)
                && endpoint.Scheme is "http" or "https" && string.IsNullOrEmpty(endpoint.UserInfo)
                && string.IsNullOrEmpty(endpoint.Query) && string.IsNullOrEmpty(endpoint.Fragment)
                && DeploymentId.HasValue && DeploymentId != Guid.Empty && ActivationEpoch > 0
                && ActivationState == EventResourceProviderActivationStateEnum.Active);
}

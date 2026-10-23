using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Observes nonsecret bootstrap workflow facts on the original synchronization owner.</summary>
public interface IIdentitySynchronizationBootstrapReader
{
    Task<IdentitySynchronizationBootstrapState?> GetCurrentAsync(CancellationToken cancellationToken);
}

/// <summary>Workflow observation only; contains no configured-administrator claim fingerprints.</summary>
public sealed record IdentitySynchronizationBootstrapState(
    InstanceBootstrapStatus Status,
    InstanceBootstrapMode Mode,
    AuthenticationProviderKind? ProviderKind,
    Guid? CompletedByUserId);

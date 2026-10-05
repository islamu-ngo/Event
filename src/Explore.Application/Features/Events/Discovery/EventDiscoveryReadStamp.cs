namespace Explore.Application.Features.Events.Discovery;

/// <summary>Server-only authorization evidence used after complete response and HAL materialization.</summary>
public sealed record EventDiscoveryReadStamp(
    Guid TenantId,
    long IdentityEpoch,
    long DisclosureEpoch,
    DateTimeOffset ValidUntilUtc);

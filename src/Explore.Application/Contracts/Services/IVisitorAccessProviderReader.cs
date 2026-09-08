// ABOUTME: Uncached native authentication configuration read port for the visitor authority.
// ABOUTME: Provider ownership supplies all enabled tenant-usable providers under the caller's lease.

using Explore.Application.Models;

namespace Explore.Application.Contracts.Services;

public interface IVisitorAccessProviderReader
{
    /// <summary>
    /// Read effective provider configuration in the caller's current snapshot, respecting
    /// deployment ownership and tenant usability. Do not use cached dispatchers, discovery,
    /// SMTP readiness, or primary-provider selection as public-onboarding evidence.
    /// Configurable signup URLs come only from trusted operator configuration, not request input.
    /// Caller acquires VisitorAccessCapabilityResolver.AuthoritySettingKeys before the snapshot.
    /// </summary>
    Task<IReadOnlyList<VisitorAccessProviderState>> ReadProvidersAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}

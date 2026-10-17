
using Explore.Application.Models;
using Explore.Domain;

namespace Explore.Application.Contracts.Services;

public interface IVisitorAccessProviderReader
{
    /// <summary>
    /// Project effective provider configuration from the caller's current instance snapshot, respecting
    /// deployment ownership and tenant usability. Do not use cached dispatchers, discovery,
    /// SMTP readiness, or primary-provider selection as public-onboarding evidence.
    /// Configurable signup URLs come only from trusted operator configuration, not request input.
    /// Caller reads VisitorAccessCapabilityResolver.AuthoritySettingKeys in one authoritative
    /// snapshot shared with mode resolution. This projection performs no database reads.
    /// </summary>
    IReadOnlyList<VisitorAccessProviderState> ReadProviders(IReadOnlyList<SystemSetting> instanceSettings);
}

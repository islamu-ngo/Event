using Explore.Application.Analytics;
using Explore.Application.Models;
using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Services;

public interface IAnalyticsGovernanceService
{
    bool AllowsIdentify(AnalyticsProviderEnum provider, AnalyticsConsentMode consentMode);

    bool AllowsGroupIdentify(AnalyticsProviderEnum provider, AnalyticsConsentMode consentMode);

    SanitizedAnalyticsTrackPayload? CreateTrackRequest(
        AnalyticsConfiguration configuration,
        string distinctId,
        AnalyticsEventDefinition definition,
        IReadOnlyDictionary<string, object?>? properties = null);

    SanitizedAnalyticsPageViewPayload? CreatePageViewRequest(
        AnalyticsConfiguration configuration,
        string distinctId,
        string pagePath,
        IReadOnlyDictionary<string, object?>? properties = null);
}

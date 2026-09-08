using Explore.Domain;

namespace Explore.Infrastructure.Webhooks;

public interface IWebhookProviderReconciliationCapabilityPolicy
{
    bool SupportsExactMessageLookup(
        WebhookProviderKind providerKind,
        string providerVersion,
        string providerEnvironment);
}

public sealed class ConformanceBackedWebhookProviderReconciliationCapabilityPolicy
    : IWebhookProviderReconciliationCapabilityPolicy
{
    public bool SupportsExactMessageLookup(
        WebhookProviderKind providerKind,
        string providerVersion,
        string providerEnvironment) =>
        SvixConformanceProfileRegistry.SupportsExactMessageLookup(
            providerKind,
            providerVersion,
            providerEnvironment);
}

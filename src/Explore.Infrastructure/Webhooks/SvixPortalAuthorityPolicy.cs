using Explore.Domain;
using Explore.Infrastructure.Configuration;

namespace Explore.Infrastructure.Webhooks;

internal static class SvixPortalAuthorityPolicy
{
    public static bool IsRuntimeEnabled(WebhookOptions options) =>
        !options.IsDisabled &&
        (options.IsProvider(WebhookOptions.ProviderSvix) || options.IsProvider(WebhookOptions.ProviderComposite)) &&
        options.Svix.AppPortalEnabled &&
        !string.IsNullOrWhiteSpace(options.Svix.Environment) &&
        !string.IsNullOrWhiteSpace(options.Svix.ProviderVersion) &&
        !string.IsNullOrWhiteSpace(options.Svix.CapabilityPolicyVersion);

    public static bool AllowsBinding(
        WebhookConsumerProviderBinding binding,
        WebhookOptions options,
        Guid? tenantId,
        Guid consumerId) =>
        IsRuntimeEnabled(options) &&
        binding.ProviderKind == WebhookProviderKind.Svix &&
        string.Equals(
            binding.ProviderVersion,
            options.Svix.ProviderVersion.Trim(),
            StringComparison.Ordinal) &&
        string.Equals(
            binding.CapabilityResolutionVersion,
            options.Svix.CapabilityPolicyVersion.Trim(),
            StringComparison.Ordinal) &&
        binding.CanIssueAppPortalFor(tenantId, consumerId);
}

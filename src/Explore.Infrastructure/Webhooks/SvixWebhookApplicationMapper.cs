using Explore.Domain;

namespace Explore.Infrastructure.Webhooks;

internal static class SvixWebhookApplicationMapper
{
    public static bool IsVerifiedConsumerBinding(
        SvixApplicationBindingResult application,
        WebhookConsumer consumer,
        WebhookConsumerProviderBinding binding)
    {
        return string.Equals(application.AppId, binding.ExternalApplicationId, StringComparison.Ordinal)
            && string.Equals(application.AppUid, binding.ApplicationUid, StringComparison.Ordinal)
            && SvixWebhookOwnershipMetadata.Matches(
                application.Metadata,
                consumer.Ownership,
                consumer.Id);
    }
}

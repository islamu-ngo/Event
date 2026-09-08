using Explore.Domain;

namespace Explore.Application.Contracts.Webhooks;

public sealed record WebhookLocalTargetResolution(
    WebhookEndpoint Endpoint,
    int EndpointConfigurationVersion,
    DateTimeOffset CredentialValidFromUtc,
    DateTimeOffset? CredentialValidUntilUtc);

public sealed record WebhookProviderTargetResolution(
    WebhookConsumerProviderBinding Binding,
    string CredentialReference,
    string CredentialVersion,
    DateTime IdempotencyValidUntil);

public sealed record WebhookDeliveryPlanResolution(
    bool Succeeded,
    Guid? WebhookConsumerId,
    WebhookProviderMode ProviderMode,
    string? ConfigurationVersion,
    int? EventContractVersion,
    string? RetentionPolicy,
    string? RetentionPolicyVersion,
    DateTimeOffset? PayloadRetentionUntil,
    DateTimeOffset? AttemptRetentionUntil,
    DateTimeOffset? DeadLetterEvidenceRetentionUntil,
    DateTime? PublicationRetentionUntil,
    DateTimeOffset? OperationalLogRetentionUntil,
    IReadOnlyCollection<WebhookLocalTargetResolution> LocalTargets,
    IReadOnlyCollection<WebhookProviderTargetResolution> ProviderTargets,
    string? FailureCategory,
    string? SafeDetail)
{
    public static WebhookDeliveryPlanResolution Success(
        Guid webhookConsumerId,
        WebhookProviderMode providerMode,
        string configurationVersion,
        int eventContractVersion,
        string retentionPolicy,
        string retentionPolicyVersion,
        DateTimeOffset payloadRetentionUntil,
        DateTimeOffset attemptRetentionUntil,
        DateTimeOffset deadLetterEvidenceRetentionUntil,
        DateTime publicationRetentionUntil,
        DateTimeOffset operationalLogRetentionUntil,
        IReadOnlyCollection<WebhookLocalTargetResolution>? localTargets = null,
        IReadOnlyCollection<WebhookProviderTargetResolution>? providerTargets = null) =>
        new(
            true,
            webhookConsumerId,
            providerMode,
            configurationVersion,
            eventContractVersion,
            retentionPolicy,
            retentionPolicyVersion,
            payloadRetentionUntil,
            attemptRetentionUntil,
            deadLetterEvidenceRetentionUntil,
            publicationRetentionUntil,
            operationalLogRetentionUntil,
            localTargets ?? [],
            providerTargets ?? [],
            null,
            null);

    public static WebhookDeliveryPlanResolution Unavailable(
        string failureCategory,
        string? safeDetail = null) =>
        new(
            false,
            null,
            WebhookProviderMode.Disabled,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            [],
            failureCategory,
            safeDetail);
}

public interface IWebhookDeliveryPlanResolver
{
    Task<WebhookDeliveryPlanResolution> ResolveAsync(
        WebhookEventBuildContext context,
        CancellationToken cancellationToken);
}

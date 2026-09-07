using System.Globalization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Exceptions;
using Explore.Application.Telemetry;
using Explore.Domain;

namespace Explore.Application.Webhooks;

public sealed class DefaultWebhookEventPublisher(
    IWebhookPayloadBuilder payloadBuilder,
    IWebhookDeliveryPlanResolver deliveryPlanResolver,
    IWebhookDeliveryPlanMaterializer deliveryPlanMaterializer,
    BusinessMetrics metrics,
    TimeProvider timeProvider) : IWebhookEventPublisher
{
    public async Task<WebhookEventPublishResult> PublishAsync(
        WebhookEventBuildContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var resolution = await deliveryPlanResolver.ResolveAsync(context, cancellationToken);
        if (!resolution.Succeeded)
        {
            return WebhookEventPublishResult.SkippedResult(
                resolution.FailureCategory ?? "webhook_delivery_plan_unavailable",
                resolution.SafeDetail);
        }

        var payload = await payloadBuilder.BuildAsync(context, cancellationToken);
        if (!payload.Succeeded)
        {
            return WebhookEventPublishResult.Failure(
                payload.FailureCategory ?? "webhook_payload_build_failed",
                isRetryable: false,
                payload.SafeDetail);
        }

        try
        {
            var materialization = CreateMaterialization(
                context,
                payload,
                resolution,
                timeProvider.GetUtcNow().UtcDateTime);
            var result = await deliveryPlanMaterializer.MaterializeAsync(
                materialization,
                cancellationToken);
            if (result.Created)
            {
                metrics.RecordWebhookMessageCreated(
                    context.EventType,
                    resolution.ProviderMode.ToString(),
                    "created");
            }

            return WebhookEventPublishResult.Success(result.Message.Id);
        }
        catch (WebhookMaterializationConflictException)
        {
            return WebhookEventPublishResult.Failure(
                "webhook_payload_conflict",
                isRetryable: false,
                "The message identity already exists with different immutable data.");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return WebhookEventPublishResult.Failure(
                "invalid_webhook_delivery_plan",
                isRetryable: false,
                exception.GetType().Name);
        }
    }

    private static WebhookDeliveryMaterialization CreateMaterialization(
        WebhookEventBuildContext context,
        WebhookPayloadBuildResult payload,
        WebhookDeliveryPlanResolution resolution,
        DateTime materializedAt)
    {
        if (resolution.WebhookConsumerId is not { } consumerId ||
            resolution.ConfigurationVersion is null ||
            resolution.EventContractVersion is not { } eventContractVersion ||
            resolution.RetentionPolicy is null ||
            resolution.RetentionPolicyVersion is null ||
            resolution.PayloadRetentionUntil is not { } payloadRetentionUntil ||
            resolution.AttemptRetentionUntil is not { } attemptRetentionUntil ||
            resolution.DeadLetterEvidenceRetentionUntil is not { } deadLetterEvidenceRetentionUntil ||
            resolution.PublicationRetentionUntil is not { } publicationRetentionUntil ||
            resolution.OperationalLogRetentionUntil is not { } operationalLogRetentionUntil ||
            payload.Envelope is not { } envelope ||
            payload.PayloadBytes is null ||
            payload.PayloadHash is null ||
            payload.PayloadRetentionUntil is null)
        {
            throw new InvalidOperationException("The resolved delivery plan is incomplete.");
        }

        if (envelope.Id != context.MessageId ||
            envelope.TenantId != context.TenantId ||
            !string.Equals(envelope.Type, context.EventType, StringComparison.Ordinal) ||
            envelope.Version != eventContractVersion ||
            envelope.OccurredAt != context.OccurredAt ||
            payload.PayloadRetentionUntil.Value != payloadRetentionUntil)
        {
            throw new InvalidOperationException("The payload does not match the authoritative delivery plan.");
        }

        var message = WebhookMessage.Create(
            context.TenantId,
            context.EventType,
            context.EventId,
            context.AggregateKind,
            context.AggregateId,
            consumerId,
            payload.PayloadBytes.Value.Span,
            "application/json",
            "utf-8",
            context.OccurredAt.UtcDateTime,
            payloadRetentionUntil.UtcDateTime,
            materializedAt);
        if (!string.Equals(message.PayloadHash, payload.PayloadHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Payload builder hash does not match its exact bytes.");
        }

        var deliveryPlan = WebhookDeliveryPlanSnapshot.Create(
            message.TenantId,
            message.Id,
            consumerId,
            resolution.ProviderMode,
            resolution.ConfigurationVersion,
            eventContractVersion.ToString(CultureInfo.InvariantCulture),
            resolution.RetentionPolicy,
            resolution.RetentionPolicyVersion,
            payloadRetentionUntil,
            attemptRetentionUntil,
            deadLetterEvidenceRetentionUntil,
            new DateTimeOffset(publicationRetentionUntil),
            operationalLogRetentionUntil,
            new DateTimeOffset(materializedAt));
        var localTargets = resolution.LocalTargets
            .Select(target => WebhookLocalTargetSnapshot.Create(
                deliveryPlan,
                target.Endpoint,
                target.EndpointConfigurationVersion,
                target.CredentialValidFromUtc,
                target.CredentialValidUntilUtc,
                new DateTimeOffset(materializedAt)))
            .ToArray();
        var providerPublications = resolution.ProviderTargets
            .Select(target => CreateProviderPublication(
                message,
                deliveryPlan,
                target,
                resolution,
                eventContractVersion,
                publicationRetentionUntil,
                materializedAt))
            .ToArray();

        return new WebhookDeliveryMaterialization(
            message,
            deliveryPlan,
            localTargets,
            providerPublications);
    }

    private static WebhookProviderPublication CreateProviderPublication(
        WebhookMessage message,
        WebhookDeliveryPlanSnapshot deliveryPlan,
        WebhookProviderTargetResolution target,
        WebhookDeliveryPlanResolution resolution,
        int eventContractVersion,
        DateTime publicationRetentionUntil,
        DateTime materializedAt)
    {
        var binding = target.Binding;
        if (!binding.IsVerifiedFor(binding.TenantId, deliveryPlan.WebhookConsumerId) ||
            string.IsNullOrWhiteSpace(binding.ExternalApplicationId))
        {
            throw new InvalidOperationException("Provider publication requires a verified consumer binding.");
        }

        var stableIdentity = $"{message.Id:N}:{binding.Id:N}";
        return WebhookProviderPublication.Create(
            message.TenantId,
            message.Id,
            deliveryPlan.Id,
            binding.ProviderKind,
            binding.Id,
            binding.ProviderVersion,
            stableIdentity,
            $"publication:{stableIdentity}",
            message.PayloadHash,
            binding.ApplicationUid,
            binding.ExternalApplicationId,
            binding.ProviderEnvironment,
            target.CredentialReference,
            target.CredentialVersion,
            resolution.ProviderMode,
            resolution.ConfigurationVersion!,
            eventContractVersion,
            resolution.RetentionPolicyVersion!,
            message.PayloadRetentionUntil,
            publicationRetentionUntil,
            target.IdempotencyValidUntil,
            materializedAt);
    }
}

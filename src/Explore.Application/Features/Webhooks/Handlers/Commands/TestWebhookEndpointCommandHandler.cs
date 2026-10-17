using System.Globalization;
using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;
using Explore.Domain;

namespace Explore.Application.Features.Webhooks.Handlers.Commands;

public sealed class TestWebhookEndpointCommandHandler(
    IWebhookEndpointRepository endpointRepository,
    IWebhookDeliveryPlanMaterializer deliveryPlanMaterializer,
    IWebhookPayloadBuilder payloadBuilder,
    IWebhookProviderCapabilityResolver capabilityResolver,
    IWebhookRetentionPolicyResolver retentionPolicyResolver,
    IWebhookAuditEventWriter auditWriter,
    IUnitOfWork unitOfWork,
    IMachinePrincipalAccessor machinePrincipalAccessor,
    IHumanWebhookMutationPreparation preparation,
    IHumanActionCommitFinalizer finalizer,
    TimeProvider timeProvider,
    IWebhookMessageRepository messageRepository)
    : ICommandHandler<TestWebhookEndpointCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        TestWebhookEndpointCommand request,
        CancellationToken cancellationToken)
    {
        var validationErrors = Validate(request);
        if (validationErrors.Count > 0)
        {
            return Failure("webhook_endpoint_test_validation_failed", validationErrors);
        }

        var human = machinePrincipalAccessor.Current is null;
        // Factories allocate private IDs. Retain this immutable graph only after an actual
        // owner rollback; every new attempt reloads and compares the current endpoint first.
        WebhookDeliveryMaterialization? delivery = null;
        async Task<BaseCommandResponse<Guid>> MutateAsync(CancellationToken token)
        {
            var endpoint = await endpointRepository.GetByIdForOwnerOperationAsync(
                request.EndpointId, forUpdate: false, token);
            if (endpoint is null || endpoint.Status == WebhookEndpointStatus.Archived)
                return Failure("webhook_endpoint_not_found", ["Webhook endpoint was not found."]);
            if (endpoint.Status != WebhookEndpointStatus.Active)
                return Failure("webhook_endpoint_not_active", ["Webhook endpoint must be active before a test delivery can be scheduled."]);
            if (endpoint.Consumer is null || endpoint.Consumer.Status != WebhookConsumerStatus.Active)
                return Failure("webhook_consumer_not_found", ["Webhook consumer was not found."]);
            if (endpoint.TenantId.HasValue && endpoint.TenantId != request.SourceTenantId)
                return Failure("webhook_endpoint_test_source_tenant_mismatch",
                    ["The endpoint owner tenant does not match the source tenant."]);
            if (!WebhookEndpointCapabilityPolicy.CanManageLocalEndpoint(
                    capabilityResolver, endpoint.Consumer.ProviderMode, out _))
                return Failure("webhook_endpoint_test_provider_managed",
                    ["Endpoint tests are only scheduled locally for Local or Composite webhook consumers. Use the provider portal for Svix-managed endpoints."]);

            if (delivery is null)
            {
                var now = timeProvider.GetUtcNow();
                var messageId = Guid.CreateVersion7();
                // The registered builder uses the in-process event catalog and exact JSON bytes;
                // it does not resolve secrets or send a webhook under the source leases.
                var payload = await payloadBuilder.BuildAsync(
                    CreateBuildContext(request.SourceTenantId, endpoint, messageId, now), token);
                if (!payload.Succeeded)
                    return Failure(payload.FailureCategory ?? "webhook_endpoint_test_payload_failed",
                        [payload.SafeDetail ?? "Webhook endpoint test payload could not be built."]);
                if (payload.Envelope is null || payload.PayloadBytes is null ||
                    payload.PayloadRetentionUntil is null || endpoint.SecretActivatedAt == default ||
                    endpoint.SecretActivatedAt.Kind != DateTimeKind.Utc)
                    return Failure("webhook_endpoint_test_plan_invalid",
                        ["Authoritative endpoint-test delivery facts are unavailable."]);

                var message = WebhookMessage.Create(
                    request.SourceTenantId, WebhookEventNames.WebhookTest, messageId.ToString("D"),
                    "WebhookEndpoint", endpoint.Id, endpoint.ConsumerId, payload.PayloadBytes.Value.Span,
                    "application/json", "utf-8", now.UtcDateTime,
                    payload.PayloadRetentionUntil.Value.UtcDateTime, now.UtcDateTime);
                var retention = retentionPolicyResolver.Resolve(now, now);
                var plan = WebhookDeliveryPlanSnapshot.Create(
                    request.SourceTenantId, message.Id, endpoint.ConsumerId, endpoint.Consumer.ProviderMode,
                    $"consumer-v{endpoint.Consumer.ConfigurationVersion}:endpoint-v{endpoint.ConfigurationVersion}",
                    payload.Envelope.Version.ToString(CultureInfo.InvariantCulture), "webhook-endpoint-test",
                    retention.PolicyVersion, payload.PayloadRetentionUntil.Value,
                    retention.ProcessingAttemptRetentionUntil, retention.DeadLetterEvidenceRetentionUntil,
                    retention.ProviderPublicationRetentionUntil, retention.OperationalLogRetentionUntil, now);
                var localTarget = WebhookLocalTargetSnapshot.Create(
                    plan, endpoint, endpoint.ConfigurationVersion, new DateTimeOffset(endpoint.SecretActivatedAt), null, now);
                delivery = new(message, plan, [localTarget], []);
            }

            var target = delivery.LocalTargets.Single();
            // W retains endpoint/consumer custody, not the newly allocated delivery products.
            // A re-prepared attempt cannot reuse a graph for a different configuration/credential.
            void RequireSnapshot(WebhookEndpoint? current)
            {
                if (current?.Consumer is null || current.Id != endpoint.Id ||
                    current.Status != WebhookEndpointStatus.Active || current.Consumer.Status != WebhookConsumerStatus.Active ||
                    current.TenantId != endpoint.TenantId || current.InstanceId != endpoint.InstanceId ||
                    delivery.Message.TenantId != request.SourceTenantId ||
                    delivery.Message.AggregateId != current.Id || delivery.Message.ConsumerId != current.ConsumerId ||
                    delivery.DeliveryPlan.WebhookConsumerId != current.ConsumerId ||
                    delivery.DeliveryPlan.ProviderMode != current.Consumer.ProviderMode ||
                    delivery.DeliveryPlan.ConfigurationVersion !=
                        $"consumer-v{current.Consumer.ConfigurationVersion}:endpoint-v{current.ConfigurationVersion}" ||
                    target.WebhookEndpointId != current.Id || target.EndpointConfigurationVersion != current.ConfigurationVersion ||
                    target.DestinationUrl != current.Url.Trim() || target.CredentialReference != current.SecretRef.Trim() ||
                    target.CredentialVersion != current.SecretVersion ||
                    target.CredentialValidFromUtc != new DateTimeOffset(current.SecretActivatedAt) ||
                    target.MaxAttempts != current.MaxAttempts || target.TimeoutSeconds != current.TimeoutSeconds ||
                    target.RateLimitPerMinute != current.RateLimitPerMinute)
                    throw new UnauthorizedAccessException("The endpoint-test graph no longer matches its original endpoint.");
            }
            RequireSnapshot(endpoint);

            var materialized = await deliveryPlanMaterializer.MaterializeAsync(
                delivery, token);
            if (materialized.Message.Id != delivery.Message.Id || materialized.DeliveryPlan.Id != delivery.DeliveryPlan.Id ||
                materialized.Message.TenantId != request.SourceTenantId ||
                materialized.Message.ConsumerId != endpoint.ConsumerId ||
                materialized.Message.AggregateId != endpoint.Id ||
                materialized.DeliveryPlan.WebhookMessageId != materialized.Message.Id ||
                materialized.DeliveryPlan.WebhookConsumerId != endpoint.ConsumerId ||
                target.WebhookMessageId != materialized.Message.Id ||
                target.DeliveryPlanSnapshotId != materialized.DeliveryPlan.Id ||
                target.TenantId != request.SourceTenantId)
                throw new UnauthorizedAccessException("The endpoint-test products must retain their original lineage.");

            if (human)
            {
                WebhookMessage? finalMessage = null;
                finalizer.Register(async finalToken =>
                {
                    RequireSnapshot(await endpointRepository.GetByIdForOwnerOperationAsync(
                        request.EndpointId, forUpdate: false, finalToken));
                    finalMessage = await messageRepository.GetByTenantAndIdAsync(
                        request.SourceTenantId, materialized.Message.Id, finalToken);
                    if (finalMessage is null || finalMessage.ConsumerId != endpoint.ConsumerId ||
                        finalMessage.AggregateId != endpoint.Id || finalMessage.EventType != delivery.Message.EventType ||
                        finalMessage.EventId != delivery.Message.EventId ||
                        finalMessage.PayloadHash != delivery.Message.PayloadHash ||
                        finalMessage.PayloadRetentionUntil != delivery.Message.PayloadRetentionUntil ||
                        finalMessage.PayloadClearedAt is not null || finalMessage.GetPayloadBytes() is null)
                        throw new UnauthorizedAccessException("The original endpoint-test payload changed before commit.");
                }, now =>
                {
                    if (finalMessage is null || finalMessage.PayloadRetentionUntil <= now.UtcDateTime ||
                        materialized.DeliveryPlan.PayloadRetentionUntilUtc <= now ||
                        target.CredentialValidFromUtc > now ||
                        target.CredentialValidUntilUtc is { } until && until <= now)
                        throw new UnauthorizedAccessException("The endpoint-test payload or credential is no longer live.");
                });
            }

            await auditWriter.AppendAsync(
                new WebhookAuditWriteRequest(
                    endpoint.TenantId,
                    WebhookAuditAction.EndpointTestScheduled,
                    WebhookAuditTargetKind.Endpoint,
                    endpoint.Id,
                    "endpoint_test_scheduled",
                    WebhookAuditOutcome.Succeeded,
                    SafeAfterJson: JsonSerializer.Serialize(new
                    {
                        messageId = materialized.Message.Id,
                        localTargetId = target.Id,
                        endpoint.ConsumerId,
                        providerMode = endpoint.Consumer.ProviderMode.ToString(),
                        targetStatus = target.DeliveryStatus.ToString()
                    }),
                    ConfigurationVersion: $"endpoint-v{endpoint.ConfigurationVersion}:credential-v{endpoint.SecretVersion}",
                    EffectiveScopeKind: endpoint.Consumer.Ownership.AuditScopeKind,
                    EffectiveScopeId: endpoint.Consumer.OwnerId),
                token);

            return BaseCommandResponse.Success(materialized.Message.Id, "Webhook endpoint test scheduled.");
        }

        return human
            ? await preparation.ExecuteAsync(request, MutateAsync, cancellationToken)
            : await unitOfWork.ExecuteInTransactionAsync(MutateAsync, cancellationToken);
    }

    private static WebhookEventBuildContext CreateBuildContext(
        Guid tenantId,
        WebhookEndpoint endpoint,
        Guid messageId,
        DateTimeOffset now) =>
        new(
            messageId,
            tenantId,
            WebhookEventNames.WebhookTest,
            messageId.ToString("D"),
            "WebhookEndpoint",
            endpoint.Id,
            now,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["endpointId"] = endpoint.Id.ToString("D"),
                ["consumerId"] = endpoint.ConsumerId.ToString("D"),
                ["providerMode"] = endpoint.Consumer!.ProviderMode.ToString(),
                ["requestedAt"] = now.ToString("O")
            },
            endpoint.ConsumerId);

    private static List<string> Validate(TestWebhookEndpointCommand request)
    {
        var errors = new List<string>();
        if (request.SourceTenantId == Guid.Empty)
        {
            errors.Add("Source tenant id is required.");
        }

        if (request.EndpointId == Guid.Empty)
        {
            errors.Add("Endpoint id is required.");
        }

        return errors;
    }

    private static BaseCommandResponse<Guid> Failure(string code, IReadOnlyList<string> errors) =>
        BaseCommandResponse.Failure<Guid>(code, errors[0], errors);
}

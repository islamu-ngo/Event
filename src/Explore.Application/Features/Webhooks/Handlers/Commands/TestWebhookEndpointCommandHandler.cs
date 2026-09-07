using System.Globalization;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.Webhooks.Handlers.Commands;

public sealed class TestWebhookEndpointCommandHandler(
    IWebhookEndpointRepository endpointRepository,
    IWebhookDeliveryPlanMaterializer deliveryPlanMaterializer,
    IWebhookPayloadBuilder payloadBuilder,
    IWebhookProviderCapabilityResolver capabilityResolver,
    IWebhookRetentionPolicyResolver retentionPolicyResolver,
    IWebhookAuditEventWriter auditWriter,
    IUnitOfWork unitOfWork)
    : IRequestHandler<TestWebhookEndpointCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        TestWebhookEndpointCommand request,
        CancellationToken cancellationToken)
    {
        var validationErrors = Validate(request);
        if (validationErrors.Count > 0)
        {
            return Failure("webhook_endpoint_test_validation_failed", validationErrors);
        }

        var endpoint = await endpointRepository.GetByIdForOwnerOperationAsync(
            request.EndpointId,
            forUpdate: false,
            cancellationToken);
        if (endpoint is null || endpoint.Status == WebhookEndpointStatus.Archived)
        {
            return Failure("webhook_endpoint_not_found", ["Webhook endpoint was not found."]);
        }

        if (endpoint.Status != WebhookEndpointStatus.Active)
        {
            return Failure("webhook_endpoint_not_active", ["Webhook endpoint must be active before a test delivery can be scheduled."]);
        }

        if (endpoint.Consumer is null || endpoint.Consumer.Status != WebhookConsumerStatus.Active)
        {
            return Failure("webhook_consumer_not_found", ["Webhook consumer was not found."]);
        }

        if (endpoint.TenantId.HasValue && endpoint.TenantId != request.SourceTenantId)
        {
            return Failure(
                "webhook_endpoint_test_source_tenant_mismatch",
                ["The endpoint owner tenant does not match the source tenant."]);
        }

        if (!WebhookEndpointCapabilityPolicy.CanManageLocalEndpoint(
                capabilityResolver,
                endpoint.Consumer.ProviderMode,
                out _))
        {
            return Failure(
                "webhook_endpoint_test_provider_managed",
                ["Endpoint tests are only scheduled locally for Local or Composite webhook consumers. Use the provider portal for Svix-managed endpoints."]);
        }

        var now = DateTimeOffset.UtcNow;
        var messageId = Guid.CreateVersion7();
        var payload = await payloadBuilder.BuildAsync(
            CreateBuildContext(request.SourceTenantId, endpoint, messageId, now),
            cancellationToken);
        if (!payload.Succeeded)
        {
            return Failure(
                payload.FailureCategory ?? "webhook_endpoint_test_payload_failed",
                [payload.SafeDetail ?? "Webhook endpoint test payload could not be built."]);
        }

        if (payload.Envelope is null ||
            payload.PayloadBytes is null ||
            payload.PayloadRetentionUntil is null ||
            endpoint.SecretActivatedAt == default ||
            endpoint.SecretActivatedAt.Kind != DateTimeKind.Utc)
        {
            return Failure(
                "webhook_endpoint_test_plan_invalid",
                ["Authoritative endpoint-test delivery facts are unavailable."]);
        }

        var message = WebhookMessage.Create(
            request.SourceTenantId,
            WebhookEventNames.WebhookTest,
            messageId.ToString("D"),
            "WebhookEndpoint",
            endpoint.Id,
            endpoint.ConsumerId,
            payload.PayloadBytes.Value.Span,
            "application/json",
            "utf-8",
            now.UtcDateTime,
            payload.PayloadRetentionUntil.Value.UtcDateTime,
            now.UtcDateTime);
        var retention = retentionPolicyResolver.Resolve(now, now);
        var deliveryPlan = WebhookDeliveryPlanSnapshot.Create(
            request.SourceTenantId,
            message.Id,
            endpoint.ConsumerId,
            endpoint.Consumer.ProviderMode,
            $"consumer-v{endpoint.Consumer.ConfigurationVersion}:endpoint-v{endpoint.ConfigurationVersion}",
            payload.Envelope.Version.ToString(CultureInfo.InvariantCulture),
            "webhook-endpoint-test",
            retention.PolicyVersion,
            payload.PayloadRetentionUntil.Value,
            retention.ProcessingAttemptRetentionUntil,
            retention.DeadLetterEvidenceRetentionUntil,
            retention.ProviderPublicationRetentionUntil,
            retention.OperationalLogRetentionUntil,
            now);
        var target = WebhookLocalTargetSnapshot.Create(
            deliveryPlan,
            endpoint,
            endpoint.ConfigurationVersion,
            new DateTimeOffset(endpoint.SecretActivatedAt),
            null,
            now);

        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var materialized = await deliveryPlanMaterializer.MaterializeAsync(
                new WebhookDeliveryMaterialization(message, deliveryPlan, [target], []),
                token);

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
        }, cancellationToken);
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

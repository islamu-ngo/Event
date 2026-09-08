using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.Webhooks.Handlers.Commands;

public sealed class CreateWebhookEndpointCommandHandler(
    IWebhookEndpointRepository endpointRepository,
    IWebhookConsumerRepository consumerRepository,
    IWebhookEventTypeRepository eventTypeRepository,
    IWebhookProviderCapabilityResolver capabilityResolver,
    IWebhookAuditEventWriter auditWriter,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CreateWebhookEndpointCommand, BaseCommandResponse<Guid>>
{
    private const int DefaultMaxAttempts = 8;
    private const int DefaultTimeoutSeconds = 15;

    public async Task<BaseCommandResponse<Guid>> Handle(
        CreateWebhookEndpointCommand request,
        CancellationToken cancellationToken)
    {
        var validationErrors = Validate(request, out var normalizedUrl, out var eventTypeIds);
        if (validationErrors.Count > 0)
        {
            return Failure("webhook_endpoint_validation_failed", validationErrors);
        }

        var consumer = await consumerRepository.GetByIdForOwnerOperationAsync(
            request.ConsumerId,
            forUpdate: false,
            cancellationToken);
        if (consumer is null || consumer.Status != WebhookConsumerStatus.Active)
        {
            return Failure("webhook_consumer_not_found", ["Webhook consumer was not found."]);
        }

        if (!WebhookEndpointCapabilityPolicy.CanManageLocalEndpoint(
                capabilityResolver,
                consumer.ProviderMode,
                out var capabilityFailure))
        {
            return Failure("webhook_endpoint_management_unavailable", [capabilityFailure]);
        }

        var existingEndpoint = await endpointRepository.GetByConsumerAndUrlForOwnerOperationAsync(
            request.ConsumerId,
            normalizedUrl,
            cancellationToken);
        if (existingEndpoint is not null)
        {
            return Failure("webhook_endpoint_url_conflict", ["Webhook endpoint URL is already configured for this consumer."]);
        }

        var eventTypes = await eventTypeRepository.GetByIdsAsync(eventTypeIds, cancellationToken);
        if (eventTypes.Count != eventTypeIds.Length || eventTypes.Any(eventType => !eventType.IsEnabled))
        {
            return Failure("webhook_endpoint_event_types_invalid", ["One or more webhook event types are invalid or disabled."]);
        }

        var now = DateTime.UtcNow;
        var endpoint = new WebhookEndpoint
        {
            Id = Guid.CreateVersion7(),
            TenantId = consumer.TenantId,
            InstanceId = consumer.InstanceId,
            ConsumerId = request.ConsumerId,
            Url = normalizedUrl,
            Description = NormalizeOptional(request.Description),
            Status = WebhookEndpointStatus.Active,
            SecretRef = request.SecretRef.Trim(),
            SecretVersion = 1,
            SecretActivatedAt = now,
            ConfigurationVersion = 1,
            MaxAttempts = request.MaxAttempts ?? DefaultMaxAttempts,
            TimeoutSeconds = request.TimeoutSeconds ?? DefaultTimeoutSeconds,
            RateLimitPerMinute = request.RateLimitPerMinute,
            CreatedAt = now
        };

        var subscriptions = eventTypes
            .Select(eventType => new WebhookEndpointSubscription
            {
                Id = Guid.CreateVersion7(),
                TenantId = consumer.TenantId,
                InstanceId = consumer.InstanceId,
                EndpointId = endpoint.Id,
                EventTypeId = eventType.Id,
                IsEnabled = true,
                CreatedAt = now
            })
            .ToArray();

        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var persisted = await endpointRepository.CreateWithSubscriptionsAsync(
                endpoint,
                subscriptions,
                token);
            await auditWriter.AppendAsync(
                new WebhookAuditWriteRequest(
                    persisted.TenantId,
                    WebhookAuditAction.EndpointCreated,
                    WebhookAuditTargetKind.Endpoint,
                    persisted.Id,
                    "endpoint_created",
                    WebhookAuditOutcome.Succeeded,
                    SafeAfterJson: JsonSerializer.Serialize(new
                    {
                        persisted.ConsumerId,
                        destinationHost = new Uri(persisted.Url).DnsSafeHost,
                        status = persisted.Status.ToString(),
                        persisted.ConfigurationVersion,
                        persisted.SecretVersion,
                        persisted.MaxAttempts,
                        persisted.TimeoutSeconds,
                        persisted.RateLimitPerMinute,
                        subscriptionCount = subscriptions.Length
                    }),
                    ConfigurationVersion: $"endpoint-v{persisted.ConfigurationVersion}:credential-v{persisted.SecretVersion}",
                    EffectiveScopeKind: consumer.Ownership.AuditScopeKind,
                    EffectiveScopeId: consumer.OwnerId),
                token);

            return BaseCommandResponse.Success(persisted.Id, "Webhook endpoint created.");
        }, cancellationToken);
    }

    private static List<string> Validate(
        CreateWebhookEndpointCommand request,
        out string normalizedUrl,
        out Guid[] eventTypeIds)
    {
        var errors = new List<string>();
        normalizedUrl = string.Empty;
        eventTypeIds = request.EventTypeIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        if (request.ConsumerId == Guid.Empty)
        {
            errors.Add("Consumer id is required.");
        }

        if (string.IsNullOrWhiteSpace(request.SecretRef))
        {
            errors.Add("Secret reference is required.");
        }
        else if (request.SecretRef.Trim().Length > 500)
        {
            errors.Add("Secret reference must be 500 characters or fewer.");
        }

        if (!string.IsNullOrWhiteSpace(request.Description) && request.Description.Trim().Length > 1000)
        {
            errors.Add("Description must be 1000 characters or fewer.");
        }

        if (request.EventTypeIds.Count == 0 || eventTypeIds.Length == 0)
        {
            errors.Add("At least one event type is required.");
        }

        if (request.EventTypeIds.Count != eventTypeIds.Length)
        {
            errors.Add("Event type ids must be non-empty and unique.");
        }

        if (request.MaxAttempts is < 1 or > 20)
        {
            errors.Add("Max attempts must be between 1 and 20.");
        }

        if (request.TimeoutSeconds is < 1 or > 60)
        {
            errors.Add("Timeout seconds must be between 1 and 60.");
        }

        if (request.RateLimitPerMinute is < 1 or > 10_000)
        {
            errors.Add("Rate limit per minute must be between 1 and 10000.");
        }

        if (string.IsNullOrWhiteSpace(request.Url)
            || request.Url.Trim().Length > 2048
            || !Uri.TryCreate(request.Url.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add("Endpoint URL must be an absolute HTTP or HTTPS URL.");
        }
        else
        {
            normalizedUrl = uri.ToString();
        }

        return errors;
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static BaseCommandResponse<Guid> Failure(string code, IReadOnlyList<string> errors) =>
        BaseCommandResponse.Failure<Guid>(code, errors[0], errors);
}

internal static class WebhookEndpointCapabilityPolicy
{
    public static bool CanManageLocalEndpoint(
        IWebhookProviderCapabilityResolver capabilityResolver,
        WebhookProviderMode providerMode,
        out string failure)
    {
        var resolution = capabilityResolver.Resolve(providerMode);
        if (resolution.SupportsLocalConfiguration(WebhookProviderCapability.EndpointManagement))
        {
            failure = string.Empty;
            return true;
        }

        failure = resolution.UnavailableReasonCode ??
            "Local endpoint management is unavailable for this webhook provider mode.";
        return false;
    }
}

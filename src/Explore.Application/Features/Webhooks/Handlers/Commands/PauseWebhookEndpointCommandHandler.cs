using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Application.Features.Webhooks.Validators;
using Explore.Application.Lookups;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.Webhooks.Handlers.Commands;

public sealed class PauseWebhookEndpointCommandHandler(
    IWebhookEndpointRepository endpointRepository,
    IWebhookAuditEventWriter auditWriter,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<PauseWebhookEndpointCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        PauseWebhookEndpointCommand request,
        CancellationToken cancellationToken)
    {
        var validator = new PauseWebhookEndpointCommandValidator();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Failure(
                request.EndpointId,
                "webhook_endpoint_pause_validation_failed",
                "Webhook endpoint pause request failed validation.",
                validation.Errors.Select(error => error.ErrorMessage));
        }

        var endpoint = await endpointRepository.GetByIdForOwnerOperationAsync(
            request.EndpointId,
            forUpdate: false,
            cancellationToken);
        if (endpoint is null || endpoint.Status == WebhookEndpointStatus.Archived)
        {
            return Failure(request.EndpointId, "webhook_endpoint_not_found", "Webhook endpoint was not found.");
        }

        if (!SupportsLocalDelivery(endpoint))
        {
            return Failure(
                request.EndpointId,
                "webhook_endpoint_pause_unsupported",
                "Only Local or Composite webhook endpoints can be paused by this operation.");
        }

        if (endpoint.Status != WebhookEndpointStatus.Active)
        {
            return Failure(
                request.EndpointId,
                "webhook_endpoint_not_active",
                "Only an active webhook endpoint can be paused.");
        }

        if (endpoint.DeliveryStateVersion != request.ExpectedDeliveryStateVersion)
        {
            return Failure(
                request.EndpointId,
                "webhook_endpoint_pause_conflict",
                "Webhook endpoint delivery state changed. Reload it before pausing.");
        }

        var pausedAt = timeProvider.GetUtcNow().UtcDateTime;
        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var paused = await endpointRepository.TryPauseAsync(
                endpoint.TenantId,
                request.EndpointId,
                request.ExpectedDeliveryStateVersion,
                pausedAt,
                request.ActorUserId,
                token);
            if (!paused)
            {
                return Failure(
                    request.EndpointId,
                    "webhook_endpoint_pause_conflict",
                    "Webhook endpoint state changed before it could be paused.");
            }

            await auditWriter.AppendAsync(
                new WebhookAuditWriteRequest(
                    endpoint.TenantId,
                    WebhookAuditAction.EndpointPaused,
                    WebhookAuditTargetKind.Endpoint,
                    endpoint.Id,
                    request.ReasonCode,
                    WebhookAuditOutcome.Succeeded,
                    SafeBeforeJson: JsonSerializer.Serialize(new
                    {
                        status = NormalizedLookupMetadata.WebhookEndpointStatus(endpoint.StatusId).Code,
                        endpoint.DeliveryStateVersion
                    }),
                    SafeAfterJson: JsonSerializer.Serialize(new
                    {
                        status = NormalizedLookupMetadata.WebhookEndpointStatus((int)WebhookEndpointStatus.Disabled).Code,
                        deliveryStateVersion = endpoint.DeliveryStateVersion + 1
                    }),
                    ConfigurationVersion: $"endpoint-v{endpoint.ConfigurationVersion}:delivery-v{endpoint.DeliveryStateVersion + 1}",
                    EffectiveScopeKind: endpoint.Consumer!.Ownership.AuditScopeKind,
                    EffectiveScopeId: endpoint.Consumer.OwnerId,
                    PrincipalKind: WebhookAuditPrincipalKind.User,
                    PrincipalReference: $"user:{request.ActorUserId:D}"),
                token);

            return BaseCommandResponse.Success(request.EndpointId, "Webhook endpoint paused.");
        }, cancellationToken);
    }

    private static bool SupportsLocalDelivery(WebhookEndpoint endpoint) =>
        endpoint.Consumer?.ProviderMode is WebhookProviderMode.Local or WebhookProviderMode.Composite;

    private static BaseCommandResponse<Guid> Failure(
        Guid endpointId,
        string code,
        string message,
        IEnumerable<string>? errors = null) =>
        BaseCommandResponse.Failure(code, message, errors ?? [message], endpointId);
}

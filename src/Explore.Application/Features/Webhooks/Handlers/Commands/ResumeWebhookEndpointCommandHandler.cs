using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Application.Features.Webhooks.Validators;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.Webhooks.Handlers.Commands;

public sealed class ResumeWebhookEndpointCommandHandler(
    IWebhookEndpointRepository endpointRepository,
    IWebhookAuditEventWriter auditWriter,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<ResumeWebhookEndpointCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        ResumeWebhookEndpointCommand request,
        CancellationToken cancellationToken)
    {
        var validator = new ResumeWebhookEndpointCommandValidator();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Failure(
                request.EndpointId,
                "webhook_endpoint_resume_validation_failed",
                "Webhook endpoint resume request failed validation.");
        }

        var endpoint = await endpointRepository.GetByIdForOwnerOperationAsync(
            request.EndpointId,
            forUpdate: false,
            cancellationToken);
        if (endpoint is null || endpoint.Status == WebhookEndpointStatus.Archived)
        {
            return Failure(
                request.EndpointId,
                "webhook_endpoint_not_found",
                "Webhook endpoint was not found.");
        }

        if (endpoint.Consumer?.ProviderMode is not (WebhookProviderMode.Local or WebhookProviderMode.Composite))
        {
            return Failure(
                request.EndpointId,
                "webhook_endpoint_resume_unsupported",
                "Only Local or Composite webhook endpoints can be resumed by this operation.");
        }

        if (endpoint.Status is not (WebhookEndpointStatus.AutoPaused or WebhookEndpointStatus.Disabled))
        {
            return Failure(
                request.EndpointId,
                "webhook_endpoint_not_paused",
                "Only a paused webhook endpoint can be resumed.");
        }

        if (endpoint.DeliveryStateVersion != request.ExpectedDeliveryStateVersion)
        {
            return Failure(
                request.EndpointId,
                "webhook_endpoint_resume_conflict",
                "Webhook endpoint delivery state changed. Reload it before resuming.");
        }

        var resumedAt = timeProvider.GetUtcNow().UtcDateTime;

        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var resumed = await endpointRepository.TryResumeAsync(
                endpoint.TenantId,
                request.EndpointId,
                request.ExpectedDeliveryStateVersion,
                resumedAt,
                request.ActorUserId,
                token);
            if (!resumed)
            {
                return Failure(
                    request.EndpointId,
                    "webhook_endpoint_resume_conflict",
                    "Webhook endpoint state changed before it could be resumed.");
            }

            await auditWriter.AppendAsync(
                new WebhookAuditWriteRequest(
                    endpoint.TenantId,
                    WebhookAuditAction.EndpointResumed,
                    WebhookAuditTargetKind.Endpoint,
                    endpoint.Id,
                    request.ReasonCode,
                    WebhookAuditOutcome.Succeeded,
                    SafeBeforeJson: JsonSerializer.Serialize(new
                    {
                        status = endpoint.Status.ToString(),
                        endpoint.ConsecutiveFailureCount,
                        endpoint.AutoPauseReason,
                        endpoint.DeliveryStateVersion
                    }),
                    SafeAfterJson: JsonSerializer.Serialize(new
                    {
                        status = WebhookEndpointStatus.Active.ToString(),
                        consecutiveFailureCount = 0,
                        deliveryStateVersion = endpoint.DeliveryStateVersion + 1
                    }),
                    ConfigurationVersion: $"endpoint-v{endpoint.ConfigurationVersion}:delivery-v{endpoint.DeliveryStateVersion + 1}",
                    EffectiveScopeKind: endpoint.Consumer!.Ownership.AuditScopeKind,
                    EffectiveScopeId: endpoint.Consumer.OwnerId,
                    PrincipalKind: WebhookAuditPrincipalKind.User,
                    PrincipalReference: $"user:{request.ActorUserId:D}"),
                token);

            return BaseCommandResponse.Success(request.EndpointId, "Webhook endpoint resumed.");
        }, cancellationToken);
    }

    private static BaseCommandResponse<Guid> Failure(Guid endpointId, string code, string message) =>
        BaseCommandResponse.Failure(code, message, [message], endpointId);
}

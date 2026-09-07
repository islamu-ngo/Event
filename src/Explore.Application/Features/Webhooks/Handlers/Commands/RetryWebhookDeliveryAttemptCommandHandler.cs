using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.Webhooks.Handlers.Commands;

public sealed class RetryWebhookDeliveryAttemptCommandHandler(
    IWebhookDeliveryDrainService deliveryDrainService,
    IWebhookDeliveryAttemptRepository attemptRepository,
    ICurrentUserService currentUserService,
    IMachinePrincipalAccessor machinePrincipalAccessor)
    : IRequestHandler<RetryWebhookDeliveryAttemptCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        RetryWebhookDeliveryAttemptCommand request,
        CancellationToken cancellationToken)
    {
        if (request.AttemptId == Guid.Empty)
        {
            return Failure(
                request.AttemptId,
                "webhook_delivery_retry_validation_failed",
                "A delivery attempt id is required.");
        }

        if (!TryResolvePrincipal(out var principalKind, out var principalReference))
        {
            return Failure(
                request.AttemptId,
                "webhook_delivery_retry_actor_required",
                "An authenticated operator identity is required.");
        }

        var attempt = await attemptRepository.GetByIdForOwnerOperationAsync(
            request.AttemptId,
            cancellationToken);
        if (attempt is null)
        {
            return Failure(
                request.AttemptId,
                "webhook_delivery_attempt_not_found",
                "Webhook delivery attempt was not found.");
        }

        var result = await deliveryDrainService.ScheduleManualRetryAsync(
            attempt.TenantId,
            request.AttemptId,
            principalKind,
            principalReference,
            cancellationToken);

        return result.Outcome switch
        {
            WebhookDeliveryDrainOutcome.RetryScheduled => BaseCommandResponse.Success(result.AttemptId ?? request.AttemptId, "Webhook delivery retry scheduled."),
            WebhookDeliveryDrainOutcome.Missing => Failure(
                request.AttemptId,
                "webhook_delivery_attempt_not_found",
                "Webhook delivery attempt was not found."),
            WebhookDeliveryDrainOutcome.Skipped => Failure(
                request.AttemptId,
                "webhook_delivery_retry_skipped",
                "Webhook delivery retry could not be scheduled for the current provider or endpoint state."),
            WebhookDeliveryDrainOutcome.Deferred => Failure(
                request.AttemptId,
                "webhook_delivery_retry_deferred",
                "A scheduled or active delivery attempt already exists for this message and endpoint."),
            WebhookDeliveryDrainOutcome.AlreadyClaimed => Failure(
                request.AttemptId,
                "webhook_delivery_attempt_active",
                "Webhook delivery attempt is currently being processed."),
            WebhookDeliveryDrainOutcome.AlreadySettled => Failure(
                request.AttemptId,
                "webhook_delivery_attempt_not_retryable",
                "Webhook delivery attempt is already settled and cannot be manually retried."),
            _ => Failure(
                request.AttemptId,
                "webhook_delivery_retry_failed",
                "Webhook delivery retry could not be scheduled.")
        };
    }

    private bool TryResolvePrincipal(
        out WebhookAuditPrincipalKind principalKind,
        out string principalReference)
    {
        if (currentUserService.UserId is { } userId)
        {
            principalKind = WebhookAuditPrincipalKind.User;
            principalReference = $"user:{userId:D}";
            return true;
        }

        if (machinePrincipalAccessor.Current is { } machine)
        {
            principalKind = WebhookAuditPrincipalKind.Machine;
            principalReference = $"machine:{machine.OwnerType}:{machine.OwnerId:D}";
            return true;
        }

        principalKind = default;
        principalReference = string.Empty;
        return false;
    }

    private static BaseCommandResponse<Guid> Failure(Guid attemptId, string code, string message) =>
        BaseCommandResponse.Failure(code, message, [message], attemptId);
}

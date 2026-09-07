using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Application.Features.Webhooks.Validators;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.Webhooks.Handlers.Commands;

public sealed class RedriveIncomingWebhookCommandHandler(
    IIncomingWebhookMessageRepository messageRepository,
    IWebhookAuditEventWriter auditWriter,
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    IMachinePrincipalAccessor machinePrincipalAccessor,
    TimeProvider timeProvider)
    : IRequestHandler<RedriveIncomingWebhookCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        RedriveIncomingWebhookCommand request,
        CancellationToken cancellationToken)
    {
        var validator = new RedriveIncomingWebhookCommandValidator();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Failure(
                request.IncomingWebhookMessageId,
                "incoming_webhook_redrive_validation_failed",
                "Incoming webhook redrive request failed validation.",
                validation.Errors.Select(error => error.ErrorMessage));
        }

        if (!TryResolveActor(out var actorReference, out _))
        {
            return Failure(
                request.IncomingWebhookMessageId,
                "incoming_webhook_redrive_actor_required",
                "An authenticated operator identity is required.");
        }

        var requestedAt = timeProvider.GetUtcNow().UtcDateTime;
        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var message = await messageRepository.GetByTenantAndIdForUpdateAsync(
                request.TenantId,
                request.IncomingWebhookMessageId,
                token);
            if (message is null)
            {
                return Failure(
                    request.IncomingWebhookMessageId,
                    "incoming_webhook_not_found",
                    "Incoming webhook was not found.");
            }

            if (message.ProcessingGeneration != request.ExpectedProcessingGeneration)
            {
                return Failure(
                    message.Id,
                    "incoming_webhook_redrive_generation_conflict",
                    "Incoming webhook processing generation changed before redrive.");
            }

            if (message.Status == IncomingWebhookMessageStatus.Processing)
            {
                return Failure(
                    message.Id,
                    "incoming_webhook_redrive_active_lease",
                    "An actively processing incoming webhook cannot be redriven.");
            }

            if (message.Status != IncomingWebhookMessageStatus.DeadLettered)
            {
                return Failure(
                    message.Id,
                    "incoming_webhook_redrive_not_eligible",
                    "Only dead-lettered incoming webhooks can be redriven.");
            }

            if (message.ReplayWindowUntil <= requestedAt || message.PayloadBytes.IsEmpty)
            {
                return Failure(
                    message.Id,
                    "incoming_webhook_redrive_payload_unavailable",
                    "The incoming webhook payload is no longer retained for redrive.");
            }

            var sourceGeneration = message.ProcessingGeneration;
            var record = message.Redrive(
                request.ExpectedProcessingGeneration,
                actorReference,
                request.Reason,
                requestedAt);
            messageRepository.TrackAppendedEvidence(message);
            await messageRepository.SaveChangesAsync(token);

            token.ThrowIfCancellationRequested();
            await auditWriter.AppendAsync(
                new WebhookAuditWriteRequest(
                    message.TenantId,
                    WebhookAuditAction.IncomingRedriveScheduled,
                    WebhookAuditTargetKind.IncomingMessage,
                    message.Id,
                    "operator_redrive",
                    WebhookAuditOutcome.Succeeded,
                    SafeBeforeJson: JsonSerializer.Serialize(new
                    {
                        status = IncomingWebhookMessageStatus.DeadLettered.ToString(),
                        processingGeneration = sourceGeneration
                    }),
                    SafeAfterJson: JsonSerializer.Serialize(new
                    {
                        status = message.Status.ToString(),
                        message.ProcessingGeneration,
                        redriveRecordId = record.Id,
                        result = record.Result.ToString()
                    }),
                    ConfigurationVersion: $"processing-generation-v{message.ProcessingGeneration}"),
                token);

            return Success(message.Id, "Incoming webhook redrive scheduled.");
        }, cancellationToken);
    }

    private bool TryResolveActor(out string actorReference, out Guid? actorUserId)
    {
        if (currentUserService.UserId is { } userId)
        {
            actorReference = $"user:{userId:D}";
            actorUserId = userId;
            return true;
        }

        if (machinePrincipalAccessor.Current is { } machine)
        {
            actorReference = $"machine:{machine.OwnerType}:{machine.OwnerId:D}";
            actorUserId = null;
            return true;
        }

        actorReference = string.Empty;
        actorUserId = null;
        return false;
    }

    private static BaseCommandResponse<Guid> Success(Guid id, string message) =>
        BaseCommandResponse.Success(id, message);

    private static BaseCommandResponse<Guid> Failure(
        Guid id,
        string code,
        string message,
        IEnumerable<string>? errors = null) =>
        BaseCommandResponse.Failure(code, message, errors ?? [message], id);
}

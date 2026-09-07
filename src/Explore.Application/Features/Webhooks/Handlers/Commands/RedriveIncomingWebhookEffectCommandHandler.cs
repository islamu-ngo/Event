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

public sealed class RedriveIncomingWebhookEffectCommandHandler(
    IIncomingWebhookEffectOutboxRepository pointerRepository,
    IIncomingWebhookMessageRepository messageRepository,
    IWebhookAuditEventWriter auditWriter,
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    IMachinePrincipalAccessor machinePrincipalAccessor,
    TimeProvider timeProvider)
    : IRequestHandler<RedriveIncomingWebhookEffectCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        RedriveIncomingWebhookEffectCommand request,
        CancellationToken cancellationToken)
    {
        var validation = await new RedriveIncomingWebhookEffectCommandValidator()
            .ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Failure(
                request.EffectOutboxId,
                "incoming_webhook_effect_redrive_validation_failed",
                "Incoming Coop effect redrive request failed validation.",
                validation.Errors.Select(error => error.ErrorMessage));
        }

        if (!HasAuthenticatedActor())
        {
            return Failure(
                request.EffectOutboxId,
                "incoming_webhook_effect_redrive_actor_required",
                "An authenticated operator identity is required.");
        }

        var requestedAt = timeProvider.GetUtcNow().UtcDateTime;
        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var pointer = await pointerRepository.GetByTenantAndIdForUpdateAsync(
                request.TenantId,
                request.EffectOutboxId,
                token);
            if (pointer is null)
            {
                return Failure(
                    request.EffectOutboxId,
                    "incoming_webhook_effect_not_found",
                    "Incoming Coop effect was not found.");
            }

            if (pointer.ProcessingGeneration != request.ExpectedProcessingGeneration)
            {
                return Failure(
                    pointer.Id,
                    "incoming_webhook_effect_redrive_generation_conflict",
                    "Incoming Coop effect generation changed before redrive.");
            }

            var message = pointer.IncomingWebhookMessage ??
                await messageRepository.GetByTenantAndIdForUpdateAsync(
                    pointer.TenantId,
                    pointer.IncomingWebhookMessageId,
                    token);
            if (message is null || message.PayloadBytes.IsEmpty || message.ReplayWindowUntil <= requestedAt)
            {
                return Failure(
                    pointer.Id,
                    "incoming_webhook_effect_redrive_payload_unavailable",
                    "The retained Coop callback is no longer available for redrive.");
            }

            var sourceGeneration = pointer.ProcessingGeneration;
            try
            {
                pointer.Redrive(request.ExpectedProcessingGeneration, requestedAt);
            }
            catch (InvalidOperationException)
            {
                return Failure(
                    pointer.Id,
                    "incoming_webhook_effect_redrive_not_eligible",
                    "Only dead-lettered incoming Coop effects can be redriven.");
            }

            await pointerRepository.SaveChangesAsync(token);
            await auditWriter.AppendAsync(
                new WebhookAuditWriteRequest(
                    pointer.TenantId,
                    WebhookAuditAction.IncomingRedriveScheduled,
                    WebhookAuditTargetKind.IncomingMessage,
                    pointer.IncomingWebhookMessageId,
                    "operator_effect_redrive",
                    WebhookAuditOutcome.Succeeded,
                    SafeBeforeJson: JsonSerializer.Serialize(new
                    {
                        effectOutboxId = pointer.Id,
                        status = OutboxMessageStatus.DeadLettered.ToString(),
                        processingGeneration = sourceGeneration
                    }),
                    SafeAfterJson: JsonSerializer.Serialize(new
                    {
                        effectOutboxId = pointer.Id,
                        status = pointer.Status.ToString(),
                        pointer.ProcessingGeneration
                    }),
                    ConfigurationVersion: $"effect-processing-generation-v{pointer.ProcessingGeneration}"),
                token);

            return BaseCommandResponse.Success(pointer.Id, "Incoming Coop effect redrive scheduled.");
        }, cancellationToken);
    }

    private bool HasAuthenticatedActor() =>
        currentUserService.UserId.HasValue || machinePrincipalAccessor.Current is not null;

    private static BaseCommandResponse<Guid> Failure(
        Guid id,
        string code,
        string message,
        IEnumerable<string>? errors = null) =>
        BaseCommandResponse.Failure(code, message, errors ?? [message], id);
}

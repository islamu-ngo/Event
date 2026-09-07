using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Exceptions;
using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Application.Features.Webhooks.Validators;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.Webhooks.Handlers.Commands;

public sealed class CancelWebhookBulkReplayCommandHandler(
    IWebhookBulkReplayRepository repository,
    IWebhookBulkReplayPolicyResolver policyResolver,
    IWebhookAuditEventWriter auditWriter,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<CancelWebhookBulkReplayCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        CancelWebhookBulkReplayCommand request,
        CancellationToken cancellationToken)
    {
        var validator = new CancelWebhookBulkReplayCommandValidator();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Failure(
                request.OperationId,
                "webhook_bulk_replay_cancel_validation_failed",
                "Webhook bulk replay cancellation failed validation.",
                validation.Errors.Select(error => error.ErrorMessage));
        }

        try
        {
            return await unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var operation = await repository.GetByTenantAndIdAsync(
                    request.TenantId,
                    request.OperationId,
                    token);
                if (operation is null)
                {
                    return await RejectAsync(
                        request,
                        "webhook_bulk_replay_not_found",
                        "Webhook bulk replay operation was not found.",
                        null,
                        token);
                }

                if (operation.ConcurrencyVersion != request.ExpectedConcurrencyVersion)
                {
                    return await RejectAsync(
                        request,
                        "webhook_bulk_replay_concurrency_conflict",
                        "Webhook bulk replay operation changed. Reload it before cancelling.",
                        operation,
                        token);
                }

                if (operation.Status != WebhookBulkReplayStatus.Queued)
                {
                    return await RejectAsync(
                        request,
                        "webhook_bulk_replay_not_cancellable",
                        "Only a queued webhook bulk replay can be cancelled.",
                        operation,
                        token);
                }

                var previousStatus = operation.StatusId;
                operation.Cancel(request.ReasonCode, timeProvider.GetUtcNow().UtcDateTime);
                await repository.UpdateAsync(operation, token);
                await auditWriter.AppendAsync(
                    new WebhookAuditWriteRequest(
                        operation.TenantId,
                        WebhookAuditAction.BulkReplayCancelled,
                        WebhookAuditTargetKind.BulkReplayOperation,
                        operation.Id,
                        request.ReasonCode,
                        WebhookAuditOutcome.Succeeded,
                        SafeBeforeJson: JsonSerializer.Serialize(new
                        {
                            statusId = previousStatus,
                            concurrencyVersion = request.ExpectedConcurrencyVersion
                        }),
                        SafeAfterJson: JsonSerializer.Serialize(new
                        {
                            operation.StatusId,
                            operation.ConcurrencyVersion
                        }),
                        ConfigurationVersion: policyResolver.Resolve().PolicyVersion,
                        PrincipalKind: WebhookAuditPrincipalKind.User,
                        PrincipalReference: $"user:{request.ActorUserId:D}"),
                    token);
                return Success(operation.Id, "Webhook bulk replay operation cancelled.");
            }, cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            await RejectAsync(
                request,
                "webhook_bulk_replay_concurrency_conflict",
                "Webhook bulk replay operation changed. Reload it before cancelling.",
                null,
                cancellationToken);
            return Conflict(request.OperationId);
        }
    }

    private async Task<BaseCommandResponse<Guid>> RejectAsync(
        CancelWebhookBulkReplayCommand request,
        string failureCode,
        string message,
        WebhookBulkReplayOperation? operation,
        CancellationToken cancellationToken)
    {
        await auditWriter.AppendAsync(
            new WebhookAuditWriteRequest(
                request.TenantId,
                WebhookAuditAction.BulkReplayCancelled,
                WebhookAuditTargetKind.BulkReplayOperation,
                request.OperationId,
                request.ReasonCode,
                WebhookAuditOutcome.Rejected,
                SafeBeforeJson: JsonSerializer.Serialize(new
                {
                    statusId = operation?.StatusId,
                    actualConcurrencyVersion = operation?.ConcurrencyVersion,
                    expectedConcurrencyVersion = request.ExpectedConcurrencyVersion,
                    failureCode
                }),
                ConfigurationVersion: policyResolver.Resolve().PolicyVersion,
                PrincipalKind: WebhookAuditPrincipalKind.User,
                PrincipalReference: $"user:{request.ActorUserId:D}"),
            cancellationToken);
        return Failure(request.OperationId, failureCode, message);
    }

    private static BaseCommandResponse<Guid> Success(Guid id, string message) =>
        BaseCommandResponse.Success(id, message);

    private static BaseCommandResponse<Guid> Conflict(Guid id) =>
        Failure(
            id,
            "webhook_bulk_replay_concurrency_conflict",
            "Webhook bulk replay operation changed. Reload it before cancelling.");

    private static BaseCommandResponse<Guid> Failure(
        Guid id,
        string code,
        string message,
        IEnumerable<string>? errors = null) =>
        BaseCommandResponse.Failure(code, message, errors ?? [message], id);
}

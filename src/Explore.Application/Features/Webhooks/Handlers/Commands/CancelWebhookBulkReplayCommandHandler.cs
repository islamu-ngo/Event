using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Exceptions;
using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Application.Features.Webhooks.Validators;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;
using Explore.Domain;

namespace Explore.Application.Features.Webhooks.Handlers.Commands;

public sealed class CancelWebhookBulkReplayCommandHandler(
    IWebhookBulkReplayRepository repository,
    IWebhookBulkReplayPolicyResolver policyResolver,
    IWebhookAuditEventWriter auditWriter,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IMachinePrincipalAccessor machinePrincipalAccessor,
    IHumanWebhookMutationPreparation preparation,
    IOriginalOwnerSettlementBoundary settlementBoundary,
    IHumanActionCommitFinalizer finalizer)
    : ICommandHandler<CancelWebhookBulkReplayCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
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

        if (machinePrincipalAccessor.Current is null)
            return await ExecuteHumanAsync(request, cancellationToken);

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
            // An outer conflict is not an admitted business refusal or rollback proof.
            return Conflict(request.OperationId);
        }
    }

    private async Task<BaseCommandResponse<Guid>> ExecuteHumanAsync(
        CancelWebhookBulkReplayCommand request, CancellationToken cancellationToken)
    {
        var prepared = await preparation.PrepareCancellationAsync(request, cancellationToken);
        var invocation = new object();
        var settled = await settlementBoundary.ExecuteAsync<HumanRejection?>(invocation, async token =>
        {
            await prepared.AcquireAsync(request, token);
            var source = prepared.Operation;
            // Only these branches, after genuine acquisition, classify business refusals.
            // Repository exceptions, failure responses and provider errors cannot mint this value.
            if (source is null)
                return OriginalOwnerDecision<HumanRejection?>.RollBack(new(null,
                    "webhook_bulk_replay_not_found", "Webhook bulk replay operation was not found.",
                    timeProvider.GetUtcNow()));
            if (source.ConcurrencyVersion != request.ExpectedConcurrencyVersion)
                return OriginalOwnerDecision<HumanRejection?>.RollBack(new(source,
                    "webhook_bulk_replay_concurrency_conflict",
                    "Webhook bulk replay operation changed. Reload it before cancelling.",
                    timeProvider.GetUtcNow()));
            if (source.StatusId != (int)WebhookBulkReplayStatus.Queued)
                return OriginalOwnerDecision<HumanRejection?>.RollBack(new(source,
                    "webhook_bulk_replay_not_cancellable",
                    "Only a queued webhook bulk replay can be cancelled.", timeProvider.GetUtcNow()));

            var operation = await repository.GetByTenantAndIdAsync(request.TenantId, request.OperationId, token);
            if (operation is null || operation.TenantId != source.TenantId
                || operation.Id != source.Id || operation.OperationKey != source.OperationKey
                || operation.WebhookConsumerId != source.WebhookConsumerId
                || operation.WebhookEndpointId != source.WebhookEndpointId
                || operation.StatusId != source.StatusId || operation.ConcurrencyVersion != source.ConcurrencyVersion
                || operation.CancelledAt != source.CancelledAt || operation.CompletedAt != source.CompletedAt)
                throw new UnauthorizedAccessException("The acquired cancellation source changed.");

            operation.Cancel(request.ReasonCode, timeProvider.GetUtcNow().UtcDateTime);
            await repository.UpdateAsync(operation, token);
            await auditWriter.AppendAsync(new WebhookAuditWriteRequest(
                source.TenantId, WebhookAuditAction.BulkReplayCancelled,
                WebhookAuditTargetKind.BulkReplayOperation, source.Id, request.ReasonCode,
                WebhookAuditOutcome.Succeeded,
                SafeBeforeJson: JsonSerializer.Serialize(new
                {
                    statusId = source.StatusId,
                    concurrencyVersion = source.ConcurrencyVersion
                }),
                SafeAfterJson: JsonSerializer.Serialize(new { operation.StatusId, operation.ConcurrencyVersion }),
                ConfigurationVersion: policyResolver.Resolve().PolicyVersion,
                PrincipalKind: WebhookAuditPrincipalKind.User,
                PrincipalReference: $"user:{prepared.PrincipalId:D}"), token);
            return OriginalOwnerDecision<HumanRejection?>.Commit(null);
        }, cancellationToken);

        if (settled.Disposition == OriginalOwnerDisposition.Committed)
        {
            if (settlementBoundary.Consume(invocation, settled, OriginalOwnerDisposition.Committed) is not null)
                throw new InvalidOperationException("A rejected cancellation cannot be a successful commit.");
            return Success(request.OperationId, "Webhook bulk replay operation cancelled.");
        }

        var rejection = settlementBoundary.Consume(invocation, settled,
            OriginalOwnerDisposition.WholeTransactionRolledBack)
            ?? throw new InvalidOperationException("The original cancellation owner did not retain a business refusal.");

        // The actual whole owner has rolled back, disposed, cleared tracking/disclosure and
        // released its transaction-bound P/route/H/W leases. A new decision is outside all locks.
        // Reusing this now-idle context preserves the canonical subject/account/credential lease;
        // ExecuteAsync below creates a different physical transaction, never a borrowed owner.
        var evidence = await prepared.PrepareEvidenceAsync(cancellationToken);
        var evidenceInvocation = new object();
        var committed = await settlementBoundary.ExecuteAsync(evidenceInvocation, async token =>
        {
            await evidence.AcquireAsync(request, token);
            // No repository mutation, target scheduling, business replay or successful effect.
            var audit = await auditWriter.AppendAsync(new WebhookAuditWriteRequest(
                request.TenantId, WebhookAuditAction.BulkReplayCancelled,
                WebhookAuditTargetKind.BulkReplayOperation, request.OperationId, request.ReasonCode,
                WebhookAuditOutcome.Rejected,
                SafeBeforeJson: JsonSerializer.Serialize(new
                {
                    statusId = rejection.Source?.StatusId,
                    actualConcurrencyVersion = rejection.Source?.ConcurrencyVersion,
                    expectedConcurrencyVersion = request.ExpectedConcurrencyVersion,
                    failureCode = rejection.Code,
                    observedAt = rejection.ObservedAt
                }),
                ConfigurationVersion: policyResolver.Resolve().PolicyVersion,
                PrincipalKind: WebhookAuditPrincipalKind.User,
                PrincipalReference: $"user:{prepared.PrincipalId:D}"), token);
            var retentionUntil = audit.RetentionUntil;
            finalizer.Register(_ => Task.CompletedTask, now =>
            {
                if (now < rejection.ObservedAt || now.UtcDateTime >= retentionUntil)
                    throw new UnauthorizedAccessException("Cancellation evidence is outside its retained time bounds.");
            });
            return OriginalOwnerDecision<Guid>.Commit(audit.Id);
        }, cancellationToken);
        _ = settlementBoundary.Consume(evidenceInvocation, committed, OriginalOwnerDisposition.Committed);
        return Failure(request.OperationId, rejection.Code, rejection.Message);
    }

    private sealed record HumanRejection(HumanWebhookBulkReplayFact? Source, string Code,
        string Message, DateTimeOffset ObservedAt);

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

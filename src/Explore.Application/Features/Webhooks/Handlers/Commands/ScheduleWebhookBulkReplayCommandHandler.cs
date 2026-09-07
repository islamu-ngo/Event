using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Features.Webhooks.Handlers.Queries;
using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Application.Features.Webhooks.Requests.Queries;
using Explore.Application.Features.Webhooks.Validators;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.Webhooks.Handlers.Commands;

public sealed class ScheduleWebhookBulkReplayCommandHandler(
    IWebhookBulkReplayRepository repository,
    IWebhookBulkReplayPolicyResolver policyResolver,
    IWebhookAuditEventWriter auditWriter,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<ScheduleWebhookBulkReplayCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        ScheduleWebhookBulkReplayCommand request,
        CancellationToken cancellationToken)
    {
        var validator = new ScheduleWebhookBulkReplayCommandValidator();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Failure(
                "webhook_bulk_replay_validation_failed",
                "Webhook bulk replay request failed validation.",
                validation.Errors.Select(error => error.ErrorMessage));
        }

        var limits = policyResolver.Resolve();
        var previewRequest = ToPreviewRequest(request);
        var policyErrors = PreviewWebhookBulkReplayQueryHandler.ValidatePolicyLimits(previewRequest, limits);
        if (policyErrors.Count > 0)
        {
            return Failure(
                "webhook_bulk_replay_limit_exceeded",
                "Webhook bulk replay request exceeds configured safety limits.",
                policyErrors);
        }

        var requestHash = WebhookBulkReplayRequestIdentity.Compute(request);
        var queuedAt = timeProvider.GetUtcNow().UtcDateTime;
        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            await repository.AcquireTenantScheduleLockAsync(request.TenantId, token);
            var existing = await repository.GetByOperationKeyAsync(
                request.TenantId,
                request.OperationKey,
                token);
            if (existing is not null)
            {
                return string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal)
                    ? Success(existing.Id, "Webhook bulk replay operation already exists.")
                    : await RejectAsync(
                        request,
                        "webhook_bulk_replay_idempotency_conflict",
                        "The operation key was already used with different replay parameters.",
                        limits.PolicyVersion,
                        token);
            }

            var reservedItems = await repository.CountReservedItemsAsync(request.TenantId, token);
            if (reservedItems > limits.MaximumReservedItemsPerTenant - request.MaxItems)
            {
                return await RejectAsync(
                    request,
                    "webhook_bulk_replay_tenant_capacity_exceeded",
                    "The tenant already has the maximum reserved bulk replay capacity.",
                    limits.PolicyVersion,
                    token);
            }

            var filter = PreviewWebhookBulkReplayQueryHandler.ToFilter(previewRequest);
            var preview = await repository.PreviewAsync(
                request.TenantId,
                filter,
                queuedAt,
                token);
            if (preview.EligibleCount == 0)
            {
                return await RejectAsync(
                    request,
                    "webhook_bulk_replay_no_eligible_work",
                    "No Local webhook targets are eligible for replay.",
                    limits.PolicyVersion,
                    token,
                    preview);
            }

            var operation = WebhookBulkReplayOperation.Create(
                request.TenantId,
                request.OperationKey,
                requestHash,
                filter.FromUtc,
                filter.ToUtc,
                filter.WebhookConsumerId,
                filter.WebhookEndpointId,
                filter.EventType,
                request.MaxItems,
                request.ReasonCode,
                preview,
                queuedAt);
            operation.CreatedBy = request.ActorUserId;
            await repository.CreateAsync(operation, token);
            await auditWriter.AppendAsync(
                new WebhookAuditWriteRequest(
                    operation.TenantId,
                    WebhookAuditAction.BulkReplayScheduled,
                    WebhookAuditTargetKind.BulkReplayOperation,
                    operation.Id,
                    operation.ReasonCode,
                    WebhookAuditOutcome.Succeeded,
                    SafeAfterJson: JsonSerializer.Serialize(new
                    {
                        operation.RequestedMaxItems,
                        operation.EstimatedEligibleCount,
                        operation.EstimatedSelectedCount,
                        operation.EstimatedExcludedCount,
                        hasConsumerFilter = operation.WebhookConsumerId is not null,
                        hasEndpointFilter = operation.WebhookEndpointId is not null,
                        hasEventTypeFilter = operation.EventType is not null
                    }),
                    ConfigurationVersion: limits.PolicyVersion,
                    PrincipalKind: WebhookAuditPrincipalKind.User,
                    PrincipalReference: $"user:{request.ActorUserId:D}"),
                token);
            return Success(operation.Id, "Webhook bulk replay operation queued.");
        }, cancellationToken);
    }

    private async Task<BaseCommandResponse<Guid>> RejectAsync(
        ScheduleWebhookBulkReplayCommand request,
        string failureCode,
        string message,
        string policyVersion,
        CancellationToken cancellationToken,
        WebhookBulkReplayPreviewSnapshot? preview = null)
    {
        await auditWriter.AppendAsync(
            new WebhookAuditWriteRequest(
                request.TenantId,
                WebhookAuditAction.BulkReplayScheduled,
                WebhookAuditTargetKind.BulkReplayOperation,
                request.OperationKey,
                failureCode,
                WebhookAuditOutcome.Rejected,
                SafeAfterJson: JsonSerializer.Serialize(new
                {
                    request.MaxItems,
                    eligibleCount = preview?.EligibleCount,
                    excludedCount = preview?.TotalExcludedCount
                }),
                ConfigurationVersion: policyVersion,
                PrincipalKind: WebhookAuditPrincipalKind.User,
                PrincipalReference: $"user:{request.ActorUserId:D}"),
            cancellationToken);
        return Failure(failureCode, message);
    }

    private static PreviewWebhookBulkReplayQuery ToPreviewRequest(ScheduleWebhookBulkReplayCommand request) =>
        new()
        {
            TenantId = request.TenantId,
            FromUtc = request.FromUtc,
            ToUtc = request.ToUtc,
            WebhookConsumerId = request.WebhookConsumerId,
            WebhookEndpointId = request.WebhookEndpointId,
            EventType = request.EventType,
            MaxItems = request.MaxItems
        };

    private static BaseCommandResponse<Guid> Success(Guid id, string message) =>
        BaseCommandResponse.Success(id, message);

    private static BaseCommandResponse<Guid> Failure(
        string code,
        string message,
        IEnumerable<string>? errors = null) =>
        BaseCommandResponse.Failure<Guid>(code, message, errors ?? [message]);
}

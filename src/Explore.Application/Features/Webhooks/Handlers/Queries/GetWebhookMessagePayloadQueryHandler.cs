using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.DTOs.Webhooks;
using Explore.Application.Features.Webhooks.Requests.Queries;
using Explore.Application.Features.Webhooks.Validators;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.Webhooks.Handlers.Queries;

public sealed class GetWebhookMessagePayloadQueryHandler(
    IWebhookMessageRepository messageRepository,
    IWebhookAuditEventWriter auditWriter,
    TimeProvider timeProvider)
    : IRequestHandler<GetWebhookMessagePayloadQuery, WebhookMessagePayloadReadResult>
{
    public async Task<WebhookMessagePayloadReadResult> Handle(
        GetWebhookMessagePayloadQuery request,
        CancellationToken cancellationToken)
    {
        var validator = new GetWebhookMessagePayloadQueryValidator();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return WebhookMessagePayloadReadResult.NotFound();
        }

        var message = await messageRepository.GetByIdForOwnerOperationAsync(
            request.MessageId,
            cancellationToken);
        if (message is null)
        {
            return WebhookMessagePayloadReadResult.NotFound();
        }

        var retrievedAt = timeProvider.GetUtcNow().UtcDateTime;
        var payloadBytes = message.GetPayloadBytes();
        if (message.PayloadClearedAt is not null ||
            message.PayloadRetentionUntil <= retrievedAt ||
            payloadBytes is null)
        {
            await AuditAsync(
                message.Consumer?.Ownership,
                message.Consumer is null ? message.TenantId : message.Consumer.TenantId,
                message.Id,
                "payload.retention-expired",
                WebhookAuditOutcome.Rejected,
                JsonSerializer.Serialize(new
                {
                    availability = "gone",
                    cleared = message.PayloadClearedAt is not null,
                    retentionUntil = message.PayloadRetentionUntil
                }),
                cancellationToken);
            return WebhookMessagePayloadReadResult.Gone();
        }

        await AuditAsync(
            message.Consumer?.Ownership,
            message.Consumer is null ? message.TenantId : message.Consumer.TenantId,
            message.Id,
            "payload.viewed",
            WebhookAuditOutcome.Succeeded,
            JsonSerializer.Serialize(new
            {
                availability = "available",
                byteLength = message.PayloadByteLength,
                message.ContentType,
                message.ContentEncoding,
                retentionUntil = message.PayloadRetentionUntil
            }),
            cancellationToken);

        return WebhookMessagePayloadReadResult.Available(new WebhookMessagePayloadDto
        {
            MessageId = message.Id,
            ContentType = message.ContentType,
            ContentEncoding = message.ContentEncoding,
            PayloadBase64 = Convert.ToBase64String(payloadBytes),
            PayloadHash = message.PayloadHash,
            PayloadByteLength = message.PayloadByteLength,
            PayloadRetentionUntil = message.PayloadRetentionUntil,
            RetrievedAt = retrievedAt
        });
    }

    private Task AuditAsync(
        WebhookOwnershipScope? ownership,
        Guid? tenantId,
        Guid messageId,
        string reasonCode,
        WebhookAuditOutcome outcome,
        string? safeAfterJson,
        CancellationToken cancellationToken) =>
        auditWriter.AppendAsync(
            new WebhookAuditWriteRequest(
                tenantId,
                WebhookAuditAction.PayloadViewed,
                WebhookAuditTargetKind.Payload,
                messageId,
                reasonCode,
                outcome,
                SafeAfterJson: safeAfterJson,
                EffectiveScopeKind: ownership?.AuditScopeKind ?? WebhookAuditScopeKind.Tenant,
                EffectiveScopeId: ownership?.OwnerId ?? tenantId),
            cancellationToken);
}

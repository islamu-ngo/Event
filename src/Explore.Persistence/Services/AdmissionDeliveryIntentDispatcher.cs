// ABOUTME: Routes committed admission delivery-intent pointers through the production composite dispatcher.
// ABOUTME: Re-routes every incomplete handoff and returns typed pending state without putting bearer material in JSON.

using System.Text.Json;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Explore.Persistence.Services;

public sealed class AdmissionDeliveryIntentDispatcher(
    ExploreDbContext dbContext,
    IOutboxMessageDispatcher dispatcher,
    ILogger<AdmissionDeliveryIntentDispatcher> logger) : IAdmissionDeliveryDispatcher
{
    public async Task<AdmissionDeliveryDispatchResult> DispatchAsync(
        AdmissionDeliveryDispatchRequest request,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Pending(AdmissionDeliveryFailure.Cancelled);
        }
        if (request.DeliveryIntentId == Guid.Empty)
        {
            return new AdmissionDeliveryDispatchResult(
                AdmissionDeliveryOutcome.Unrecoverable,
                AdmissionDeliveryFailure.InvalidIntent);
        }

        AdmissionDeliveryIntent? intent;
        try
        {
            intent = await dbContext.AdmissionDeliveryIntents
                .SingleOrDefaultAsync(value => value.Id == request.DeliveryIntentId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return Pending(AdmissionDeliveryFailure.Cancelled);
        }
        if (intent is null)
        {
            return new AdmissionDeliveryDispatchResult(
                AdmissionDeliveryOutcome.Unrecoverable,
                AdmissionDeliveryFailure.InvalidIntent);
        }
        if (intent.HandoffCompletedAt is not null)
        {
            return new AdmissionDeliveryDispatchResult(AdmissionDeliveryOutcome.Delivered);
        }

        var message = new OutboxMessage
        {
            Id = intent.Id,
            AggregateType = nameof(AdmissionTicket),
            AggregateId = intent.AdmissionTicketId,
            EventType = AdmissionDeliveryEvents.CredentialDeliveryRequested,
            Payload = JsonSerializer.Serialize(new AdmissionCredentialDeliveryPointer(
                intent.TenantId,
                intent.AdmissionTicketId,
                intent.Id)),
            Status = OutboxMessageStatus.Pending,
            CreatedAt = intent.CreatedAt,
            MaxRetries = 10
        };

        try
        {
            await dispatcher.DispatchAsync(message, cancellationToken);
            return intent.HandoffCompletedAt is not null
                ? new AdmissionDeliveryDispatchResult(AdmissionDeliveryOutcome.Delivered)
                : Pending(AdmissionDeliveryFailure.RouteUnavailable);
        }
        catch (AdmissionContactRetentionExpiredException)
        {
            return new AdmissionDeliveryDispatchResult(AdmissionDeliveryOutcome.Unrecoverable, AdmissionDeliveryFailure.RetentionExpired);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning(
                "Admission delivery cancelled for tenant {TenantId}, intent {DeliveryIntentId}; protected handoff remains pending",
                intent.TenantId,
                intent.Id);
            return Pending(AdmissionDeliveryFailure.Cancelled);
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Admission delivery failed for tenant {TenantId}, intent {DeliveryIntentId}; protected handoff remains pending. Failure type: {FailureType}",
                intent.TenantId,
                intent.Id,
                exception.GetType().Name);
            return Pending(AdmissionDeliveryFailure.RouteUnavailable);
        }
    }

    private static AdmissionDeliveryDispatchResult Pending(AdmissionDeliveryFailure failure) =>
        new(AdmissionDeliveryOutcome.RecoverablePending, failure);
}

public sealed record AdmissionCredentialDeliveryPointer(
    Guid TenantId,
    Guid AdmissionTicketId,
    Guid DeliveryIntentId);

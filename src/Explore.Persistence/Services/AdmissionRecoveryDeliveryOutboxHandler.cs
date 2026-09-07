using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.Application.Contracts.Admissions;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Services;

public sealed class AdmissionRecoveryDeliveryOutboxHandler(
    ExploreDbContext dbContext,
    IAdmissionRecoveryDeliveryEnvelopeProtector envelopeProtector,
    IAdmissionRecoveryDirectDeliveryChannel deliveryChannel,
    TimeProvider timeProvider) : IAdmissionRecoveryDeliveryOutboxHandler
{
    private static readonly JsonSerializerOptions StrictJson = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        AdmissionRecoveryDeliveryPointer pointer = Parse(message);
        AdmissionRecoveryDeliveryIntent? intent = await dbContext.AdmissionRecoveryDeliveryIntents
            .SingleOrDefaultAsync(value =>
                value.TenantId == pointer.TenantId &&
                value.Id == pointer.DeliveryIntentId &&
                value.AdmissionTicketId == pointer.AdmissionTicketId,
                cancellationToken);
        if (intent?.HandoffCompletedAt is not null)
        {
            return;
        }
        if (intent is null || string.IsNullOrWhiteSpace(intent.ProtectedMaterial) ||
            intent.ProtectionVersion < 1)
        {
            throw new InvalidOperationException("Recovery delivery intent is not recoverable for handoff.");
        }

        AdmissionRecoveryDeliveryEnvelope envelope = envelopeProtector.Unprotect(
            intent.ProtectedMaterial,
            intent.ProtectionVersion);
        if (envelope.RecoveryRequestId != intent.RecoveryRequestId)
        {
            throw new InvalidOperationException("Recovery delivery envelope lineage is invalid.");
        }

        intent.MarkRouted(timeProvider.GetUtcNow().UtcDateTime);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        AdmissionRecoveryDirectDeliveryResult delivered = await deliveryChannel.DeliverAsync(
            new AdmissionRecoveryDirectDeliveryRequest(
                intent.TenantId,
                intent.Id,
                intent.AdmissionTicketId,
                intent.RecoveryRequestId,
                envelope.RecipientAddress,
                envelope.Capability),
            cancellationToken);
        if (delivered.Outcome != AdmissionRecoveryDirectDeliveryOutcome.Accepted ||
            string.IsNullOrWhiteSpace(delivered.ReceiptId))
        {
            throw new InvalidOperationException("Recovery channel acceptance remains ambiguous.");
        }

        intent.CompleteHandoff(delivered.ReceiptId, timeProvider.GetUtcNow().UtcDateTime);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static AdmissionRecoveryDeliveryPointer Parse(OutboxMessage message)
    {
        AdmissionRecoveryDeliveryPointer pointer;
        try
        {
            pointer = JsonSerializer.Deserialize<AdmissionRecoveryDeliveryPointer>(
                    message.Payload,
                    StrictJson)
                ?? throw new JsonException();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Recovery delivery pointer is malformed.", exception);
        }

        if (pointer.TenantId == Guid.Empty || pointer.AdmissionTicketId == Guid.Empty ||
            pointer.DeliveryIntentId == Guid.Empty || pointer.DeliveryIntentId != message.Id ||
            pointer.AdmissionTicketId != message.AggregateId)
        {
            throw new InvalidOperationException("Recovery delivery pointer lineage is invalid.");
        }

        return pointer;
    }
}

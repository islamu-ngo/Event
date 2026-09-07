using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.Application.Contracts.Admissions;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Services;

public sealed class AdmissionCredentialDeliveryOutboxHandler(
    ExploreDbContext dbContext,
    IAdmissionDeliveryEnvelopeProtector envelopeProtector,
    IAdmissionCredentialDirectDeliveryChannel deliveryChannel,
    TimeProvider timeProvider) : IAdmissionCredentialDeliveryOutboxHandler
{
    private static readonly JsonSerializerOptions StrictJson = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        AdmissionCredentialDeliveryPointer pointer = Parse(message);
        AdmissionDeliveryIntent? intent = await dbContext.AdmissionDeliveryIntents
            .SingleOrDefaultAsync(value =>
                value.TenantId == pointer.TenantId &&
                value.Id == pointer.DeliveryIntentId &&
                value.AdmissionTicketId == pointer.AdmissionTicketId,
                cancellationToken);
        if (intent?.HandoffCompletedAt is not null)
        {
            return;
        }
        if (intent is null || string.IsNullOrWhiteSpace(intent.ProtectedCredential) || intent.ProtectionVersion < 1)
        {
            throw new InvalidOperationException("Admission delivery intent is not recoverable for handoff.");
        }

        AdmissionCredentialDeliveryEnvelope envelope = envelopeProtector.Unprotect(
            intent.ProtectedCredential,
            intent.ProtectionVersion);
        DateTime routedAt = timeProvider.GetUtcNow().UtcDateTime;
        intent.MarkRouted(routedAt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        AdmissionCredentialDirectDeliveryResult delivered = await deliveryChannel.DeliverAsync(
            new AdmissionCredentialDirectDeliveryRequest(
                intent.TenantId,
                intent.Id,
                intent.AdmissionTicketId,
                envelope.RecipientAddress,
                envelope.PlaintextCredential),
            cancellationToken);
        if (delivered.Outcome != AdmissionCredentialDirectDeliveryOutcome.Accepted ||
            string.IsNullOrWhiteSpace(delivered.ReceiptId))
        {
            throw new InvalidOperationException("Admission credential channel acceptance remains ambiguous.");
        }

        intent.CompleteHandoff(delivered.ReceiptId, timeProvider.GetUtcNow().UtcDateTime);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static AdmissionCredentialDeliveryPointer Parse(OutboxMessage message)
    {
        AdmissionCredentialDeliveryPointer pointer;
        try
        {
            pointer = JsonSerializer.Deserialize<AdmissionCredentialDeliveryPointer>(message.Payload, StrictJson)
                ?? throw new JsonException();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Admission delivery pointer is malformed.", exception);
        }

        if (pointer.TenantId == Guid.Empty || pointer.AdmissionTicketId == Guid.Empty ||
            pointer.DeliveryIntentId == Guid.Empty || pointer.DeliveryIntentId != message.Id ||
            pointer.AdmissionTicketId != message.AggregateId)
        {
            throw new InvalidOperationException("Admission delivery pointer lineage is invalid.");
        }

        return pointer;
    }
}

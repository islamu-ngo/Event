// ABOUTME: Handles the production composite-outbox route by unprotecting and directly handing off admission credentials.
// ABOUTME: Retains ciphertext on ambiguous acceptance and erases it only after a receipt-bearing channel success.

using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Notifications;
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

        RegistrationOrder? order = await (
            from ticket in dbContext.AdmissionTickets.AsNoTracking()
            join owner in dbContext.RegistrationOrders.AsNoTracking()
                on new { ticket.TenantId, Id = ticket.RegistrationOrderId } equals new { owner.TenantId, owner.Id }
            where ticket.TenantId == intent.TenantId && ticket.Id == intent.AdmissionTicketId
            select owner).SingleOrDefaultAsync(cancellationToken);
        AdmissionContactDeliveryPayload payload = AdmissionContactDeliveryPayload.Read(intent.ProtectedCredential, intent.ProtectionVersion);
        string? accountEmail = await ResolveAccountEmailAsync(payload.AccountUserId, cancellationToken);
        payload.RequireDisclosure(order, timeProvider.GetUtcNow().UtcDateTime, accountEmail);
        AdmissionCredentialDeliveryEnvelope envelope = envelopeProtector.Unprotect(
            intent.ProtectedCredential,
            intent.ProtectionVersion);
        DateTime routedAt = timeProvider.GetUtcNow().UtcDateTime;
        intent.MarkRouted(routedAt);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        accountEmail = await ResolveAccountEmailAsync(payload.AccountUserId, cancellationToken);
        payload.RequireDisclosure(order, timeProvider.GetUtcNow().UtcDateTime, accountEmail);
        if (payload.AccountUserId.HasValue && !string.Equals(envelope.RecipientAddress, accountEmail, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Admission account contact has changed.");

        AdmissionCredentialDirectDeliveryResult delivered = await deliveryChannel.DeliverAsync(
            new AdmissionCredentialDirectDeliveryRequest(
                intent.TenantId,
                intent.Id,
                intent.AdmissionTicketId,
                envelope.RecipientAddress,
                envelope.PlaintextCredential)
            {
                DisclosureUntilUtc = payload.DisclosureUntilUtc
            },
            cancellationToken);
        if (delivered.Outcome == AdmissionCredentialDirectDeliveryOutcome.RetentionExpired)
            throw new AdmissionContactRetentionExpiredException();
        if (delivered.Outcome != AdmissionCredentialDirectDeliveryOutcome.Accepted ||
            string.IsNullOrWhiteSpace(delivered.ReceiptId))
        {
            throw new InvalidOperationException("Admission credential channel acceptance remains ambiguous.");
        }

        intent.CompleteHandoff(delivered.ReceiptId, timeProvider.GetUtcNow().UtcDateTime);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<string?> ResolveAccountEmailAsync(Guid? userId, CancellationToken cancellationToken)
    {
        if (!userId.HasValue) return null;
        User? user = await dbContext.Users.AsNoTracking().Include(value => value.Pii)
            .SingleOrDefaultAsync(value => value.Id == userId.Value, cancellationToken);
        return RecipientEmailAddressResolver.Resolve(user, userId.Value).Email;
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

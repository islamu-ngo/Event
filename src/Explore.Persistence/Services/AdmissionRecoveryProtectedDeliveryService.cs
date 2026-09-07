using System.Text.Json;
using Explore.Application.Contracts.Admissions;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Services;

public sealed class AdmissionRecoveryProtectedDeliveryService(
    ExploreDbContext dbContext,
    IAdmissionRecoveryDeliveryEnvelopeProtector envelopeProtector,
    TimeProvider timeProvider) :
    IAdmissionRecoveryDeliveryStager
{
    public async Task<AdmissionRecoveryDeliveryResult> StageAsync(
        AdmissionRecoveryDeliveryRequest request,
        CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty || request.RecoveryRequestId == Guid.Empty ||
            request.AdmissionTicketId == Guid.Empty ||
            request.Purpose != AdmissionRecoveryPurpose.TicketRecovery ||
            request.CapabilityVersion < 1 || string.IsNullOrWhiteSpace(request.Capability))
        {
            return new AdmissionRecoveryDeliveryResult(AdmissionRecoveryDeliveryOutcome.Pending);
        }

        bool alreadyStaged = await dbContext.AdmissionRecoveryDeliveryIntents
            .AsNoTracking()
            .AnyAsync(value =>
                value.TenantId == request.TenantId &&
                value.RecoveryRequestId == request.RecoveryRequestId &&
                value.AdmissionTicketId == request.AdmissionTicketId &&
                value.Purpose == request.Purpose.ToString() &&
                value.CapabilityVersion == request.CapabilityVersion,
                cancellationToken);
        if (alreadyStaged)
        {
            return new AdmissionRecoveryDeliveryResult(AdmissionRecoveryDeliveryOutcome.Accepted);
        }

        string? recipient = await (
                from ticket in dbContext.AdmissionTickets.AsNoTracking()
                join pii in dbContext.RegistrationOrderPii.AsNoTracking()
                    on new { ticket.TenantId, ticket.RegistrationOrderId }
                    equals new { pii.TenantId, pii.RegistrationOrderId }
                where ticket.TenantId == request.TenantId &&
                    ticket.Id == request.AdmissionTicketId &&
                    pii.IsEmailVerified
                select pii.Email)
            .SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(recipient))
        {
            return new AdmissionRecoveryDeliveryResult(AdmissionRecoveryDeliveryOutcome.Pending);
        }

        AdmissionRecoveryProtectedDeliveryMaterial protectedMaterial = envelopeProtector.Protect(
            new AdmissionRecoveryDeliveryEnvelope(
                recipient,
                request.RecoveryRequestId,
                request.Capability));
        Guid intentId = Guid.CreateVersion7();
        DateTime nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var intent = new AdmissionRecoveryDeliveryIntent(
            intentId,
            request.TenantId,
            request.RecoveryRequestId,
            request.AdmissionTicketId,
            request.Purpose.ToString(),
            request.CapabilityVersion,
            protectedMaterial.Ciphertext,
            protectedMaterial.ProtectionVersion,
            nowUtc);
        var pointer = new AdmissionRecoveryDeliveryPointer(
            request.TenantId,
            request.AdmissionTicketId,
            intentId);
        var outbox = new OutboxMessage
        {
            Id = intentId,
            AggregateType = nameof(AdmissionRecoveryCapability),
            AggregateId = request.AdmissionTicketId,
            EventType = AdmissionRecoveryDeliveryEvents.RecoveryDeliveryRequested,
            Payload = JsonSerializer.Serialize(pointer),
            Status = OutboxMessageStatus.Pending,
            CreatedAt = nowUtc,
            MaxRetries = 10
        };
        await dbContext.AdmissionRecoveryDeliveryIntents.AddAsync(intent, cancellationToken);
        await dbContext.OutboxMessages.AddAsync(outbox, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new AdmissionRecoveryDeliveryResult(AdmissionRecoveryDeliveryOutcome.Accepted);
    }
}

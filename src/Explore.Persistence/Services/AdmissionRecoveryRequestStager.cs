using System.Text.Json;
using Explore.Application.Contracts.Admissions;
using Explore.Domain;

namespace Explore.Persistence.Services;

public sealed class AdmissionRecoveryRequestStager(
    ExploreDbContext dbContext,
    IAdmissionRecoveryRequestEnvelopeProtector protector,
    TimeProvider timeProvider) : IAdmissionRecoveryRequestStager
{
    public async Task StageAsync(
        Guid tenantId,
        AdmissionRecoveryRequestEnvelope envelope,
        CancellationToken cancellationToken)
    {
        AdmissionRecoveryProtectedDeliveryMaterial protectedMaterial = protector.Protect(envelope);
        Guid intentId = Guid.CreateVersion7();
        DateTime createdAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        var intent = new AdmissionRecoveryRequestIntent(
            intentId,
            tenantId,
            protectedMaterial.Ciphertext,
            protectedMaterial.ProtectionVersion,
            createdAtUtc);
        var outbox = new OutboxMessage
        {
            Id = intentId,
            AggregateType = nameof(AdmissionRecoveryRequestIntent),
            AggregateId = intentId,
            EventType = AdmissionRecoveryDeliveryEvents.RecoveryRequestProcessingRequested,
            Payload = JsonSerializer.Serialize(
                new AdmissionRecoveryRequestPointer(tenantId, intentId)),
            Status = OutboxMessageStatus.Pending,
            CreatedAt = createdAtUtc,
            MaxRetries = 10
        };
        await dbContext.AdmissionRecoveryRequestIntents.AddAsync(intent, cancellationToken);
        await dbContext.OutboxMessages.AddAsync(outbox, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

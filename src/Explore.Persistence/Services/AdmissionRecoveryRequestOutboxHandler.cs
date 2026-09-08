using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Services;

public sealed class AdmissionRecoveryRequestOutboxHandler(
    ExploreDbContext dbContext,
    IAdmissionRecoveryRequestEnvelopeProtector protector,
    AdmissionRecoveryService recoveryService,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IAdmissionRecoveryRequestOutboxHandler
{
    private static readonly JsonSerializerOptions StrictJson = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        AdmissionRecoveryRequestPointer pointer = Parse(message);
        await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                AdmissionRecoveryRequestIntent? intent =
                    await dbContext.AdmissionRecoveryRequestIntents
                        .SingleOrDefaultAsync(value =>
                            value.TenantId == pointer.TenantId &&
                            value.Id == pointer.RequestIntentId,
                            token);
                if (intent?.ProcessedAt is not null)
                {
                    return;
                }
                if (intent is null || string.IsNullOrWhiteSpace(intent.ProtectedIdentity))
                {
                    throw new InvalidOperationException("Recovery request intent is unavailable.");
                }

                AdmissionRecoveryRequestEnvelope envelope = protector.Unprotect(
                    intent.ProtectedIdentity,
                    intent.ProtectionVersion);
                await recoveryService.ProcessStagedRequestAsync(
                    new AdmissionRecoveryRequest(
                        intent.TenantId,
                        envelope.NormalizedIdentity,
                        envelope.Purpose),
                    token);
                intent.Complete(timeProvider.GetUtcNow().UtcDateTime);
                await dbContext.SaveChangesAsync(token);
            },
            cancellationToken);
    }

    private static AdmissionRecoveryRequestPointer Parse(OutboxMessage message)
    {
        try
        {
            AdmissionRecoveryRequestPointer pointer =
                JsonSerializer.Deserialize<AdmissionRecoveryRequestPointer>(
                    message.Payload,
                    StrictJson) ?? throw new JsonException();
            if (pointer.TenantId == Guid.Empty || pointer.RequestIntentId == Guid.Empty ||
                pointer.RequestIntentId != message.Id ||
                pointer.RequestIntentId != message.AggregateId)
            {
                throw new JsonException();
            }

            return pointer;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Recovery request pointer is malformed.", exception);
        }
    }
}

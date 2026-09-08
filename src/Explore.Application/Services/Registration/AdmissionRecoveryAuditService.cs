using System.Text.Json;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;

namespace Explore.Application.Services.Registration;

public sealed class AdmissionRecoveryAuditService(IAuditLogRepository repository) :
    IAdmissionRecoveryAuditService
{
    public async Task AppendAsync(
        AdmissionRecoveryAuditFact fact,
        CancellationToken cancellationToken)
    {
        if (fact.TenantId == Guid.Empty || fact.RecoveryRequestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(fact.ActionCode) || fact.CapabilityVersion < 1 ||
            fact.OccurredAtUtc == default)
        {
            throw new ArgumentException("Complete recovery audit lineage is required.", nameof(fact));
        }

        await repository.Create(new AuditLog
        {
            Id = Guid.CreateVersion7(),
            TenantId = fact.TenantId,
            Tenant = null!,
            EntityType = nameof(AdmissionRecoveryCapability),
            EntityId = fact.RecoveryRequestId.ToString("D"),
            Action = fact.ActionCode,
            NewValues = JsonSerializer.Serialize(new
            {
                fact.CapabilityVersion
            }),
            AffectedColumns = "[]",
            ActorId = null,
            Timestamp = fact.OccurredAtUtc.UtcDateTime
        });
    }
}

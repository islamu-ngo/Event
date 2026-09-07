using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface ISupportAccessAuditEventRepository
{
    Task<SupportAccessAuditEvent> CreateAsync(
        SupportAccessAuditEvent auditEvent,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SupportAccessAuditEvent>> ListForSessionAsync(
        Guid sessionId,
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SupportAccessAuditEvent>> ListForTargetTenantAsync(
        Guid targetTenantId,
        int limit,
        CancellationToken cancellationToken = default);
}

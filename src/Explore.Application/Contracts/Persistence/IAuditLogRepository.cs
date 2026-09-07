using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IAuditLogRepository : IGenericRepository<AuditLog, Guid>
{
    Task<(IReadOnlyList<AuditLog> Items, int TotalCount)> GetTemplateSyncHistoryAsync(
        string entityType,
        string entityId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken);
}

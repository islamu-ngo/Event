namespace Explore.Application.DTOs.CustomPropertyDefinition;

public sealed record CustomPropertyPurgeResultDto(
    Guid DefinitionId,
    Guid TenantId,
    string Scope,
    bool Purged,
    Guid? AuditLogId,
    string Reason,
    int OptionCount,
    int ValueCount,
    int ProjectionCount,
    int AuditLogCount,
    int SyncProvenanceCount)
{
    public bool HasBlockingDependencies =>
        ValueCount > 0
        || ProjectionCount > 0
        || AuditLogCount > 0
        || SyncProvenanceCount > 0;
}

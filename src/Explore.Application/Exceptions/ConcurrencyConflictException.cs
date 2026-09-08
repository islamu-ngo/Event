namespace Explore.Application.Exceptions;

public class ConcurrencyConflictException : ApplicationException
{
    /// <summary>Technical persistence conflict — EF detected a stale <c>ConcurrencyStamp</c>.</summary>
    public const string ConcurrentUpdate = "concurrent_update";

    /// <summary>Business-level sync conflict — caller's <c>baseProvenanceVersion</c> is stale.</summary>
    public const string StaleSyncBase = "stale_sync_base";

    public string Code { get; }
    public string? EntityType { get; }
    public string? EntityId { get; }

    public ConcurrencyConflictException(
        string code,
        string message,
        string? entityType = null,
        string? entityId = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        EntityType = entityType;
        EntityId = entityId;
    }
}

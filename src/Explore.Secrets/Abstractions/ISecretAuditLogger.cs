// Enables compliance tracking and security monitoring.

namespace Explore.Secrets.Abstractions;

/// <summary>
/// Types of secret operations that can be audited.
/// </summary>
public enum SecretOperation
{
    /// <summary>
    /// A secret was read/accessed.
    /// </summary>
    Access,

    /// <summary>
    /// Secrets were refreshed from the provider.
    /// </summary>
    Refresh,

    /// <summary>
    /// Secret refresh failed.
    /// </summary>
    RefreshFailed,

    /// <summary>
    /// Provider initialization.
    /// </summary>
    Initialize,

    /// <summary>
    /// Provider initialization failed.
    /// </summary>
    InitializeFailed
}

/// <summary>
/// Audit entry for secret operations.
/// </summary>
/// <param name="Operation">The type of operation.</param>
/// <param name="ProviderType">The provider that handled the operation.</param>
/// <param name="KeyPattern">Redacted key pattern for logging (e.g., "Database:***").</param>
/// <param name="Timestamp">When the operation occurred.</param>
/// <param name="UserId">User who triggered the operation (if applicable).</param>
/// <param name="CorrelationId">Request correlation ID.</param>
/// <param name="Success">Whether the operation succeeded.</param>
/// <param name="ErrorMessage">Error message if operation failed.</param>
public sealed record SecretAuditEntry(
    SecretOperation Operation,
    SecretProviderType ProviderType,
    string? KeyPattern,
    DateTimeOffset Timestamp,
    string? UserId = null,
    string? CorrelationId = null,
    bool Success = true,
    string? ErrorMessage = null);

/// <summary>
/// Logs audit entries for secret operations.
/// Implementations may write to structured logs, SIEM, or audit tables.
/// </summary>
public interface ISecretAuditLogger
{
    /// <summary>
    /// Logs an audit entry for a secret operation.
    /// </summary>
    /// <param name="entry">The audit entry to log.</param>
    void Log(SecretAuditEntry entry);

    /// <summary>
    /// Logs an audit entry asynchronously.
    /// </summary>
    /// <param name="entry">The audit entry to log.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task LogAsync(SecretAuditEntry entry, CancellationToken cancellationToken = default);
}

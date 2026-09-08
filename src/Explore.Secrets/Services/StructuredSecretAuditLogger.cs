// Outputs audit entries as structured JSON for OpenTelemetry/Loki ingestion.
// Designed for Serilog but works with any ILogger implementation.

using Explore.Secrets.Abstractions;
using Microsoft.Extensions.Logging;

namespace Explore.Secrets.Services;

/// <summary>
/// Default audit logger that writes secret operation audit entries
/// as structured log events. Designed to work with Serilog's structured
/// logging for OpenTelemetry export to Loki.
/// </summary>
public sealed class StructuredSecretAuditLogger : ISecretAuditLogger
{
    private readonly ILogger<StructuredSecretAuditLogger> _logger;

    /// <summary>
    /// Log level used for successful operations.
    /// </summary>
    private const LogLevel SuccessLevel = LogLevel.Information;

    /// <summary>
    /// Log level used for failed operations.
    /// </summary>
    private const LogLevel FailureLevel = LogLevel.Warning;

    public StructuredSecretAuditLogger(ILogger<StructuredSecretAuditLogger> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public void Log(SecretAuditEntry entry)
    {
        LogEntry(entry);
    }

    /// <inheritdoc />
    public Task LogAsync(SecretAuditEntry entry, CancellationToken cancellationToken = default)
    {
        LogEntry(entry);
        return Task.CompletedTask;
    }

    private void LogEntry(SecretAuditEntry entry)
    {
        var level = entry.Success ? SuccessLevel : FailureLevel;

        // Use structured logging with semantic property names
        // These will be indexed by Loki when exported via OpenTelemetry
        _logger.Log(
            logLevel: level,
            eventId: new EventId((int)entry.Operation, entry.Operation.ToString()),
            message: "SecretAudit: {Operation} on {ProviderType} {Status}. Key: {KeyPattern}",
            args:
            [
                entry.Operation.ToString(),
                entry.ProviderType.ToString(),
                entry.Success ? "succeeded" : "failed",
                entry.KeyPattern ?? "(none)"
            ]);

        // Log additional context as a separate structured log for detailed querying
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "SecretAudit details - Operation: {Operation}, Provider: {ProviderType}, " +
                "Key: {KeyPattern}, Success: {Success}, UserId: {UserId}, " +
                "CorrelationId: {CorrelationId}, Timestamp: {Timestamp}, Error: {ErrorMessage}",
                entry.Operation,
                entry.ProviderType,
                entry.KeyPattern,
                entry.Success,
                entry.UserId ?? "anonymous",
                entry.CorrelationId ?? "none",
                entry.Timestamp.ToString("O"),
                entry.ErrorMessage ?? "none");
        }

        // Log errors at warning level with full context
        if (!entry.Success && !string.IsNullOrEmpty(entry.ErrorMessage))
        {
            _logger.LogWarning(
                "SecretAudit FAILURE - Operation: {Operation}, Provider: {ProviderType}, " +
                "Error: {ErrorMessage}, CorrelationId: {CorrelationId}",
                entry.Operation,
                entry.ProviderType,
                entry.ErrorMessage,
                entry.CorrelationId ?? "none");
        }
    }
}

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
        var operation = Enum.IsDefined(entry.Operation) ? entry.Operation.ToString() : "Unknown";
        var provider = Enum.IsDefined(entry.ProviderType) ? entry.ProviderType.ToString() : "Unknown";
        var correlation = entry.CorrelationId is { Length: <= 38 } &&
            Guid.TryParse(entry.CorrelationId, out var correlationId)
            ? correlationId.ToString("N")
            : "none";
        var reason = entry.Success ? "none" : entry.Operation switch
        {
            SecretOperation.InitializeFailed => "secret_provider_initialization_failed",
            SecretOperation.RefreshFailed => "secret_provider_refresh_failed",
            _ => "secret_operation_failed"
        };
        _logger.Log(
            logLevel: level,
            eventId: new EventId(Enum.IsDefined(entry.Operation) ? (int)entry.Operation : -1, operation),
            message: "SecretAudit: {Operation} on {ProviderType} {Status}. Reason: {ReasonCode}. CorrelationId: {CorrelationId}",
            args:
            [
                operation,
                provider,
                entry.Success ? "succeeded" : "failed",
                reason,
                correlation
            ]);
    }
}

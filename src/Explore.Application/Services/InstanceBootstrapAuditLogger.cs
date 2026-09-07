using Explore.Application.Contracts.Services;
using Explore.Application.Onboarding;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Services;

public sealed class InstanceBootstrapAuditLogger : IInstanceBootstrapAuditLogger
{
    private const int MaxLoggedFieldLength = 200;
    private readonly ILogger<InstanceBootstrapAuditLogger> _logger;

    public InstanceBootstrapAuditLogger(ILogger<InstanceBootstrapAuditLogger> logger)
    {
        _logger = logger;
    }

    public void Log(InstanceBootstrapAuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        var eventName = auditEvent.EventType.ToString();
        _logger.Log(
            GetLogLevel(auditEvent.EventType),
            new EventId((int)auditEvent.EventType, eventName),
            "Instance bootstrap audit event. Event={BootstrapAuditEvent} Operation={Operation} Outcome={Outcome} ActorPresent={ActorPresent} RouteName={RouteName} TraceId={TraceId} FailureCode={FailureCode} Provider={Provider} Mode={Mode} RealmPresent={RealmPresent} ClientIdPresent={ClientIdPresent} DeploymentMode={DeploymentMode}",
            eventName,
            Normalize(auditEvent.Operation),
            Normalize(auditEvent.Outcome),
            auditEvent.ActorUserId.HasValue,
            Normalize(auditEvent.RouteName),
            Normalize(auditEvent.TraceId),
            Normalize(auditEvent.FailureCode),
            Normalize(auditEvent.Provider),
            Normalize(auditEvent.Mode),
            !string.IsNullOrWhiteSpace(auditEvent.Realm),
            !string.IsNullOrWhiteSpace(auditEvent.ClientId),
            Normalize(auditEvent.DeploymentMode));
    }

    private static LogLevel GetLogLevel(InstanceBootstrapAuditEventType eventType)
        => eventType switch
        {
            InstanceBootstrapAuditEventType.SetupSecretRejected => LogLevel.Warning,
            InstanceBootstrapAuditEventType.SetupModeInactive => LogLevel.Warning,
            InstanceBootstrapAuditEventType.KeycloakBootstrapFailed => LogLevel.Warning,
            InstanceBootstrapAuditEventType.SetupModeDisabled => LogLevel.Warning,
            _ => LogLevel.Information
        };

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = new string(value
            .Trim()
            .Where(character => !char.IsControl(character))
            .ToArray());

        if (normalized.Length == 0)
        {
            return null;
        }

        return normalized.Length <= MaxLoggedFieldLength
            ? normalized
            : normalized[..MaxLoggedFieldLength];
    }
}

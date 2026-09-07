namespace Explore.Application.Onboarding;

public enum InstanceBootstrapAuditEventType
{
    SetupSecretAccepted = 41001,
    SetupSecretRejected = 41002,
    SetupModeInactive = 41003,
    KeycloakBootstrapStarted = 41010,
    KeycloakBootstrapSucceeded = 41011,
    KeycloakBootstrapFailed = 41012,
    SetupModeDisabled = 41020,
    SetupProfileSaved = 41021
}

public sealed record InstanceBootstrapAuditEvent(
    InstanceBootstrapAuditEventType EventType,
    string Operation,
    string Outcome,
    Guid? ActorUserId = null,
    string? RouteName = null,
    string? TraceId = null,
    string? FailureCode = null,
    string? Provider = null,
    string? Mode = null,
    string? Realm = null,
    string? ClientId = null,
    string? DeploymentMode = null);

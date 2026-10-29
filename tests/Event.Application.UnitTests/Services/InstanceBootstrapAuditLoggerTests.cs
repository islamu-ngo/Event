using System.Text.Json;
using Explore.Application.Onboarding;
using Explore.Application.Services;
using Microsoft.Extensions.Logging;

namespace Event.Application.UnitTests.Services;

public class InstanceBootstrapAuditLoggerTests
{
    private const string MessageTemplate = "Instance bootstrap audit event. Event={BootstrapAuditEvent} Operation={Operation} Outcome={Outcome} ActorPresent={ActorPresent} RouteName={RouteName} TraceId={TraceId} FailureCode={FailureCode} Provider={Provider} Mode={Mode} RealmPresent={RealmPresent} ClientIdPresent={ClientIdPresent} DeploymentMode={DeploymentMode}";
    private readonly CapturingLogger _logger = new();

    [Test]
    [Arguments(InstanceBootstrapAuditEventType.SetupSecretAccepted, 41001, "SetupSecretAccepted", LogLevel.Information)]
    [Arguments(InstanceBootstrapAuditEventType.SetupSecretRejected, 41002, "SetupSecretRejected", LogLevel.Warning)]
    [Arguments(InstanceBootstrapAuditEventType.SetupModeInactive, 41003, "SetupModeInactive", LogLevel.Warning)]
    [Arguments(InstanceBootstrapAuditEventType.SetupModeDisabled, 41020, "SetupModeDisabled", LogLevel.Warning)]
    [Arguments(InstanceBootstrapAuditEventType.SetupProfileSaved, 41021, "SetupProfileSaved", LogLevel.Information)]
    public async Task Log_PreservesEventIdentityAndSeverityWithoutExposingIdentity(
        InstanceBootstrapAuditEventType eventType, int eventId, string eventName, LogLevel level)
    {
        Guid actorUserId = Guid.CreateVersion7();
        string realm = $"private-realm-{Guid.NewGuid():N}";
        string clientId = $"private-client-{Guid.NewGuid():N}";

        new InstanceBootstrapAuditLogger(_logger).Log(new InstanceBootstrapAuditEvent(
            eventType, "setup_secret_gate", "failed", ActorUserId: actorUserId,
            FailureCode: "keycloak_admin_rejected", Realm: realm, ClientId: clientId));

        var entry = _logger.Entries.Single();
        await Assert.That(entry.Level).IsEqualTo(level);
        await Assert.That(entry.EventId.Id).IsEqualTo(eventId);
        await Assert.That(entry.EventId.Name).IsEqualTo(eventName);
        await Assert.That(entry.State["BootstrapAuditEvent"]).IsEqualTo(eventName);
        await Assert.That(entry.State["{OriginalFormat}"]).IsEqualTo(MessageTemplate);
        await Assert.That(entry.State["Operation"]).IsEqualTo("setup_secret_gate");
        await Assert.That(entry.State["Outcome"]).IsEqualTo("failed");
        await Assert.That(entry.State["FailureCode"]).IsEqualTo("keycloak_admin_rejected");
        await Assert.That(entry.State["ActorPresent"]).IsEqualTo(true);
        await Assert.That(entry.State["RealmPresent"]).IsEqualTo(true);
        await Assert.That(entry.State["ClientIdPresent"]).IsEqualTo(true);
        await Assert.That(entry.Exception).IsNull();

        string exported = JsonSerializer.Serialize(entry);
        foreach (string canary in new[] { actorUserId.ToString("D"), actorUserId.ToString("N"), realm, clientId })
        {
            await Assert.That(entry.Message.Contains(canary, StringComparison.Ordinal)).IsFalse();
            await Assert.That(exported.Contains(canary, StringComparison.Ordinal)).IsFalse();
        }
    }

    [Test]
    public async Task Log_WhenProfileSaved_EmitsSafeOperationalFacts()
    {
        new InstanceBootstrapAuditLogger(_logger).Log(new InstanceBootstrapAuditEvent(
            InstanceBootstrapAuditEventType.SetupProfileSaved,
            Operation: "setup_profile", Outcome: "succeeded",
            RouteName: "setup_profile_save", TraceId: "trace-123",
            Provider: "keycloak", Mode: "PatchExistingRealm",
            Realm: "ISLAMU", ClientId: "islamu-event-blazor", DeploymentMode: "single_tenant"));

        var entry = _logger.Entries.Single();
        await Assert.That(entry.State["Operation"]).IsEqualTo("setup_profile");
        await Assert.That(entry.State["Outcome"]).IsEqualTo("succeeded");
        await Assert.That(entry.State["RouteName"]).IsEqualTo("setup_profile_save");
        await Assert.That(entry.State["TraceId"]).IsEqualTo("trace-123");
        await Assert.That(entry.State["Provider"]).IsEqualTo("keycloak");
        await Assert.That(entry.State["Mode"]).IsEqualTo("PatchExistingRealm");
        await Assert.That(entry.State["DeploymentMode"]).IsEqualTo("single_tenant");
        await Assert.That(entry.State["FailureCode"]).IsNull();
        await Assert.That(entry.State["ActorPresent"]).IsEqualTo(false);
        await Assert.That(entry.Message).IsEqualTo(
            "Instance bootstrap audit event. Event=SetupProfileSaved Operation=setup_profile Outcome=succeeded ActorPresent=False RouteName=setup_profile_save TraceId=trace-123 FailureCode=(null) Provider=keycloak Mode=PatchExistingRealm RealmPresent=True ClientIdPresent=True DeploymentMode=single_tenant");
    }

    [Test]
    public async Task Log_WhenSetupSecretRejected_EmitsFailureWithoutInventingIdentity()
    {
        new InstanceBootstrapAuditLogger(_logger).Log(new InstanceBootstrapAuditEvent(
            InstanceBootstrapAuditEventType.SetupSecretRejected,
            Operation: "setup_secret_gate", Outcome: "rejected", FailureCode: "invalid_setup_secret",
            Realm: " \t", ClientId: ""));

        var entry = _logger.Entries.Single();
        await Assert.That(entry.State["Outcome"]).IsEqualTo("rejected");
        await Assert.That(entry.State["FailureCode"]).IsEqualTo("invalid_setup_secret");
        await Assert.That(entry.State["ActorPresent"]).IsEqualTo(false);
        await Assert.That(entry.State["RealmPresent"]).IsEqualTo(false);
        await Assert.That(entry.State["ClientIdPresent"]).IsEqualTo(false);
        await Assert.That(entry.State["RouteName"]).IsNull();
        await Assert.That(entry.State["TraceId"]).IsNull();
        await Assert.That(entry.State["Provider"]).IsNull();
        await Assert.That(entry.State["Mode"]).IsNull();
        await Assert.That(entry.State["DeploymentMode"]).IsNull();
    }

    [Test]
    [Arguments(" \r\n\t", null)]
    [Arguments(" \0\u0001 ", null)]
    [Arguments(" safe\r\nvalue\t ", "safevalue")]
    public async Task Log_NormalizesOperationalFieldsWithoutLogInjection(string value, string? expected)
    {
        new InstanceBootstrapAuditLogger(_logger).Log(new InstanceBootstrapAuditEvent(
            InstanceBootstrapAuditEventType.SetupProfileSaved, value, value,
            RouteName: value, TraceId: value, FailureCode: value, Provider: value,
            Mode: value, DeploymentMode: value));

        var entry = _logger.Entries.Single();
        foreach (string field in new[] { "Operation", "Outcome", "RouteName", "TraceId", "FailureCode", "Provider", "Mode", "DeploymentMode" })
        {
            await Assert.That(entry.State[field]).IsEqualTo(expected);
        }
        await Assert.That(entry.Message.Any(char.IsControl)).IsFalse();
    }

    [Test]
    public async Task Log_BoundsOperationalFields()
    {
        string value = new('a', 201);
        new InstanceBootstrapAuditLogger(_logger).Log(new InstanceBootstrapAuditEvent(
            InstanceBootstrapAuditEventType.SetupProfileSaved, value, value,
            RouteName: value, TraceId: value, FailureCode: value, Provider: value,
            Mode: value, DeploymentMode: value));

        foreach (string field in new[] { "Operation", "Outcome", "RouteName", "TraceId", "FailureCode", "Provider", "Mode", "DeploymentMode" })
        {
            await Assert.That(_logger.Entries.Single().State[field]).IsEqualTo(new string('a', 200));
        }
    }

    private sealed record CapturedLog(LogLevel Level, EventId EventId,
        Dictionary<string, object?> State, string Message, Exception? Exception);

    private sealed class CapturingLogger : ILogger<InstanceBootstrapAuditLogger>
    {
        public List<CapturedLog> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = (IEnumerable<KeyValuePair<string, object?>>)state!;
            Entries.Add(new CapturedLog(logLevel, eventId, properties.ToDictionary(), formatter(state, exception), exception));
        }
    }
}

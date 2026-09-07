using Explore.Application.Onboarding;
using Explore.Application.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Event.Application.UnitTests.Services;

public class InstanceBootstrapAuditLoggerTests
{
    private readonly ILogger<InstanceBootstrapAuditLogger> _logger = Substitute.For<ILogger<InstanceBootstrapAuditLogger>>();
    private readonly InstanceBootstrapAuditLogger _auditLogger;

    public InstanceBootstrapAuditLoggerTests()
    {
        _auditLogger = new InstanceBootstrapAuditLogger(_logger);
    }

    [Test]
    public async Task Log_WhenBootstrapSucceeds_UsesInformationEventId()
    {
        _auditLogger.Log(new InstanceBootstrapAuditEvent(
            InstanceBootstrapAuditEventType.KeycloakBootstrapSucceeded,
            Operation: "keycloak_bootstrap",
            Outcome: "succeeded",
            Provider: "keycloak",
            Mode: "PatchExistingRealm",
            Realm: "ISLAMU",
            ClientId: "islamu-event-blazor"));

        _logger.Received(1).Log(
            LogLevel.Information,
            Arg.Is<EventId>(eventId =>
                eventId.Id == (int)InstanceBootstrapAuditEventType.KeycloakBootstrapSucceeded
                && eventId.Name == nameof(InstanceBootstrapAuditEventType.KeycloakBootstrapSucceeded)),
            Arg.Is<object>(state =>
                LogStateContains(state, "BootstrapAuditEvent", nameof(InstanceBootstrapAuditEventType.KeycloakBootstrapSucceeded))
                && LogStateContains(state, "Operation", "keycloak_bootstrap")
                && LogStateContains(state, "Outcome", "succeeded")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());

        await Assert.That(true).IsTrue();
    }

    [Test]
    public async Task Log_WhenSetupSecretRejected_UsesWarningAndFailureCode()
    {
        _auditLogger.Log(new InstanceBootstrapAuditEvent(
            InstanceBootstrapAuditEventType.SetupSecretRejected,
            Operation: "setup_secret_gate",
            Outcome: "rejected",
            FailureCode: "invalid_setup_secret"));

        _logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Is<EventId>(eventId =>
                eventId.Id == (int)InstanceBootstrapAuditEventType.SetupSecretRejected
                && eventId.Name == nameof(InstanceBootstrapAuditEventType.SetupSecretRejected)),
            Arg.Is<object>(state =>
                LogStateContains(state, "BootstrapAuditEvent", nameof(InstanceBootstrapAuditEventType.SetupSecretRejected))
                && LogStateContains(state, "FailureCode", "invalid_setup_secret")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());

        await Assert.That(true).IsTrue();
    }

    [Test]
    public async Task Log_WhenIdentityFieldsArePresent_EmitsOnlyPresenceMetadata()
    {
        Guid actorUserId = Guid.CreateVersion7();
        const string realm = "private-realm";
        const string clientId = "private-client";
        _auditLogger.Log(new InstanceBootstrapAuditEvent(
            InstanceBootstrapAuditEventType.KeycloakBootstrapFailed,
            Operation: "keycloak_bootstrap",
            Outcome: "failed",
            ActorUserId: actorUserId,
            FailureCode: "keycloak_admin_rejected",
            Realm: realm,
            ClientId: clientId));

        _logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(state =>
                LogStateContains(state, "ActorPresent", true)
                && LogStateContains(state, "RealmPresent", true)
                && LogStateContains(state, "ClientIdPresent", true)
                && !state.ToString()!.Contains(actorUserId.ToString("D"), StringComparison.Ordinal)
                && !state.ToString()!.Contains(realm, StringComparison.Ordinal)
                && !state.ToString()!.Contains(clientId, StringComparison.Ordinal)),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());

        await Assert.That(true).IsTrue();
    }

    private static bool LogStateContains(object state, string key, object? expectedValue)
    {
        if (state is not IEnumerable<KeyValuePair<string, object?>> values)
        {
            return false;
        }

        return values.Any(value =>
            value.Key == key
            && Equals(value.Value, expectedValue));
    }
}

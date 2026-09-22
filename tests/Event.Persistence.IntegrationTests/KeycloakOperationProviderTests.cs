using Explore.Domain.Keycloak;

namespace Event.Persistence.IntegrationTests.Keycloak;

public sealed class KeycloakOperationProviderTests
{
    private static readonly Guid InstanceId =
        Guid.Parse("55555555-5555-7555-8555-555555555555");
    private static readonly DateTimeOffset Created =
        new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Target_NormalizesAuthorityWithoutChangingProviderIdentifiers()
    {
        var target = new KeycloakTarget(
            InstanceId,
            " HTTPS://Identity.Example.Test/auth/ ",
            " Operators ",
            " Event-Bff ");

        await Assert.That(target.Authority)
            .IsEqualTo("https://identity.example.test/auth");
        await Assert.That(target.Realm).IsEqualTo("Operators");
        await Assert.That(target.Client).IsEqualTo("Event-Bff");
    }

    [Test]
    public async Task ReceiptContract_HasNoCredentialOrRawProviderPayloadFields()
    {
        string[] propertyNames = typeof(KeycloakOperation)
            .GetProperties()
            .Select(property => property.Name)
            .Concat(typeof(KeycloakTarget)
                .GetProperties()
                .Select(property => property.Name))
            .Concat(typeof(KeycloakChangeSet)
                .GetProperties()
                .Select(property => property.Name))
            .Concat(typeof(KeycloakChangeStep)
                .GetProperties()
                .Select(property => property.Name))
            .Concat(typeof(KeycloakStepOutcome)
                .GetProperties()
                .Select(property => property.Name))
            .ToArray();

        await Assert.That(propertyNames.Any(name =>
            name.Contains("Password", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Credential", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Token", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Secret", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Raw", StringComparison.OrdinalIgnoreCase)))
            .IsFalse();
    }

    [Test]
    public async Task OutcomeUnknown_RemainsUnsettledUntilReadBack()
    {
        var operation = new KeycloakOperation(
            new KeycloakChangeSet(
            [
                new KeycloakChangeStep(
                    "mapper-update",
                    KeycloakStep.UpdateMapper,
                    KeycloakResourceKind.ProtocolMapper,
                    "event-bff:audience",
                    KeycloakStepPrecondition.MustMatchFingerprint,
                    "expected-fingerprint",
                    "desired-fingerprint",
                    "binding-fingerprint")
            ]),
            new KeycloakTarget(
                InstanceId,
                "https://identity.example.test",
                "operators",
                "event-bff"),
            "actor",
            7,
            "digest",
            Created,
            Created.AddHours(1));
        operation.AuthorizeApply(
            "actor",
            7,
            operation.Target,
            "digest",
            Created.AddMinutes(1));

        operation.RecordStepOutcome(new KeycloakStepOutcome(
            "mapper-update",
            KeycloakStepOutcomeKind.OutcomeUnknown));
        operation.MarkOutcomeUnknown();

        await Assert.That(operation.State)
            .IsEqualTo(KeycloakOperationState.OutcomeUnknown);
        await Assert.That(operation.SettledAtUtc).IsNull();
    }
}

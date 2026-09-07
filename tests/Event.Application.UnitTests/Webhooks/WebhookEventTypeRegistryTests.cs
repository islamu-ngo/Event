using System.Text.Json;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Webhooks;

namespace Event.Application.UnitTests.Webhooks;

public sealed class WebhookEventTypeRegistryTests
{
    private readonly WebhookEventTypeRegistry _registry = new();
    private readonly WebhookEventSchemaProvider _schemaProvider = new();

    [Test]
    public async Task GetAll_ReturnsInitialCanonicalEventCatalog()
    {
        var eventTypes = _registry.GetAll().ToArray();

        await Assert.That(eventTypes).Count().IsEqualTo(13);
        await Assert.That(eventTypes.Select(eventType => eventType.Name)).IsEquivalentTo(
        [
            WebhookEventNames.EventCreated,
            WebhookEventNames.EventPublished,
            WebhookEventNames.EventUpdated,
            WebhookEventNames.EventCancelled,
            WebhookEventNames.EventLightModerated,
            WebhookEventNames.EventHeavyRedacted,
            WebhookEventNames.RegistrationCreated,
            WebhookEventNames.RegistrationApproved,
            WebhookEventNames.RegistrationCancelled,
            WebhookEventNames.ReportCreated,
            WebhookEventNames.ReportDecisionCreated,
            WebhookEventNames.OrganizationVerified,
            WebhookEventNames.WebhookTest
        ]);
    }

    [Test]
    public async Task GetAll_UsesSvixCompatibleNamesAndStableVersions()
    {
        foreach (var descriptor in _registry.GetAll())
        {
            await Assert.That(WebhookEventTypeRegistry.IsValidEventTypeName(descriptor.Name)).IsTrue();
            var expectedVersion = descriptor.Name == WebhookEventNames.RegistrationCreated ? 2 : 1;
            await Assert.That(descriptor.SchemaVersion).IsEqualTo(expectedVersion);
            await Assert.That(descriptor.IsEnabled).IsTrue();
            await Assert.That(descriptor.IsPublic).IsTrue();
            await Assert.That(descriptor.DataFields).IsNotEmpty();
        }
    }

    [Test]
    public async Task RegistrationCreated_DeclaresConsentAndOptionalAttendeeFields()
    {
        var descriptor = _registry.FindByName(WebhookEventNames.RegistrationCreated)!;

        await Assert.That(descriptor.SchemaVersion).IsEqualTo(2);
        await Assert.That(descriptor.DataFields.Single(field => field.Name == "consentToEmailShare").Required).IsTrue();
        await Assert.That(descriptor.DataFields.Single(field => field.Name == "consentToEmailShare").JsonType).IsEqualTo(WebhookJsonSchemaTypes.Flag);
        await Assert.That(descriptor.DataFields.Single(field => field.Name == "attendeeEmail").Required).IsFalse();
        await Assert.That(descriptor.DataFields.Single(field => field.Name == "attendeeFirstName").Required).IsFalse();
        await Assert.That(descriptor.DataFields.Single(field => field.Name == "attendeeLastName").Required).IsFalse();
    }

    [Test]
    public async Task SchemaProvider_CreatesParsableSchemasAndExamplesForEveryEventType()
    {
        foreach (var descriptor in _registry.GetAll())
        {
            using var schema = JsonDocument.Parse(_schemaProvider.CreateSchemaJson(descriptor));
            using var example = JsonDocument.Parse(_schemaProvider.CreateExamplePayloadJson(descriptor));

            await Assert.That(schema.RootElement.GetProperty("title").GetString()).IsEqualTo(descriptor.Name);
            await Assert.That(schema.RootElement.GetProperty("properties").TryGetProperty("data", out _)).IsTrue();
            await Assert.That(example.RootElement.GetProperty("type").GetString()).IsEqualTo(descriptor.Name);
            await Assert.That(example.RootElement.GetProperty("version").GetInt32()).IsEqualTo(descriptor.SchemaVersion);
            await Assert.That(example.RootElement.GetProperty("data").EnumerateObject().Any()).IsTrue();
        }
    }
}

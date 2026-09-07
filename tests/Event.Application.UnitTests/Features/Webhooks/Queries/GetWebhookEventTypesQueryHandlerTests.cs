using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Features.Webhooks.Handlers.Queries;
using Explore.Application.Features.Webhooks.Requests.Queries;
using Explore.Application.Webhooks;
using Explore.Domain;
using NSubstitute;

namespace Event.Application.UnitTests.Features.Webhooks.Queries;

public sealed class GetWebhookEventTypesQueryHandlerTests
{
    [Test]
    public async Task Handle_ReturnsCanonicalCatalogWithSchemaExamplesAndFields()
    {
        var persistedId = Guid.CreateVersion7();
        var eventTypeRepository = Substitute.For<IWebhookEventTypeRepository>();
        eventTypeRepository.GetByNamesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(
                [
                    new WebhookEventType
                    {
                        Id = persistedId,
                        Name = WebhookEventNames.EventPublished,
                        GroupName = "event",
                        Description = "Raised when an event becomes publicly published.",
                        SchemaJson = """{"type":"object"}""",
                        SchemaVersion = 1,
                        IsPublic = true,
                        IsEnabled = true,
                        PayloadRetentionDays = 14
                    }
                ]);
        var handler = new GetWebhookEventTypesQueryHandler(
            new WebhookEventTypeRegistry(),
            new WebhookEventSchemaProvider(),
            eventTypeRepository);

        var result = await handler.Handle(new GetWebhookEventTypesQuery(), CancellationToken.None);

        await Assert.That(result).Count().IsEqualTo(13);
        await Assert.That(result.Select(eventType => eventType.Name)).Contains(WebhookEventNames.EventPublished);
        await Assert.That(result.Select(eventType => eventType.Name)).Contains(WebhookEventNames.EventHeavyRedacted);
        await Assert.That(result.Select(eventType => eventType.Name)).Contains(WebhookEventNames.WebhookTest);

        var published = result.Single(eventType => eventType.Name == WebhookEventNames.EventPublished);
        await Assert.That(published.Id).IsEqualTo(persistedId);
        await Assert.That(published.GroupName).IsEqualTo("event");
        await Assert.That(published.SchemaVersion).IsEqualTo(1);
        await Assert.That(published.IsPublic).IsTrue();
        await Assert.That(published.IsEnabled).IsTrue();
        await Assert.That(published.PayloadRetentionDays).IsEqualTo(14);
        await Assert.That(published.DataFields.Select(field => field.Name)).Contains("eventId");
        await Assert.That(published.DataFields.Single(field => field.Name == "publicUrl").Required).IsFalse();

        using var schema = JsonDocument.Parse(published.SchemaJson);
        using var example = JsonDocument.Parse(published.ExamplePayloadJson);
        await Assert.That(schema.RootElement.GetProperty("type").GetString()).IsEqualTo("object");
        await Assert.That(example.RootElement.GetProperty("type").GetString()).IsEqualTo(WebhookEventNames.EventPublished);
    }
}

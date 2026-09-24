using System.Collections.Immutable;
using System.Text.Json;
using Explore.Application.DTOs.Ai;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.EventTemplate;
using Explore.Application.Features.Federation.Atproto.Models;
using Explore.Application.Mappings;
using Explore.Domain;

namespace Event.Application.UnitTests.Features.EventResources;

public sealed class EventResourceProjectionBoundaryTests
{
    private static readonly JsonSerializerOptions WireOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task GenericEventSurfaces_DoNotSerializeResourceDestinationsOrProviderIdentity()
    {
        // The same event projection feeds API detail/list and AI event-reference enrichment.
        var entity = new Explore.Domain.Event
        {
            Title = "Public gathering",
            Actor = new Actor { ActorType = null!, Pii = new ActorPii { DisplayName = "Host" } },
            Tenant = null!,
            EventStatus = null!,
            VisibilityType = null!,
            EventFormat = null!
        };
        EventDto detail = EventMapper.ToDetail(entity)!;
        EventListDto list = EventMapper.ToListItem(entity);
        object[] published =
        [
            detail,
            list,
            new EventTemplateDto { DisplayName = "Public template" },
            new EventTemplateListDto { DisplayName = "Public template" },
            new EventCalendarExportDto(Guid.NewGuid(), "Public gathering", null, null,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), null),
            new AiReferenceSearchResultDto("Event", Guid.NewGuid(), "Public gathering", null,
                null, null, null, null, null),
            new AiSelectedReferenceDto("Event", Guid.NewGuid(), "Public gathering", null),
            new AtprotoEventPublicationSnapshot(
                "Public gathering", null, null, DateTimeOffset.UtcNow, null, null, null, null, false,
                new AtprotoEventDetailsSnapshot(null, null, null, null, 0, "public", null, "UTC", null,
                    null, null, "public", null, null, null),
                new AtprotoOrganizerSnapshot("Host", "user", null, null, null, null, null, null,
                    null, null, null, null, null, null, null, null, null),
                null, null, null, new AtprotoEventAppearanceSnapshot(null, null, null, null),
                [], [], [], [], [], [], [], [], [])
        ];

        foreach (object projection in published)
        {
            using JsonDocument wire = JsonDocument.Parse(JsonSerializer.Serialize(projection, projection.GetType(), WireOptions));
            string json = wire.RootElement.GetRawText();
            await Assert.That(json).Contains(projection is EventTemplateDto or EventTemplateListDto
                ? "Public template" : "Public gathering");
            foreach (string forbidden in new[]
                     {
                         "eventResources", "resources", "eventResourceId", "externalDestinationCiphertext",
                         "externalDestinationSafeOrigin", "externalDestinationProtectionVersion", "sensitiveNotes",
                         "storageProviderBindingId", "providerVersionId", "storageObjectId"
                     })
            {
                await Assert.That(json).DoesNotContain($"\"{forbidden}\"", StringComparison.OrdinalIgnoreCase)
                    .Because($"{projection.GetType().Name} must not expose {forbidden} on a generic projection");
            }
        }
    }
}

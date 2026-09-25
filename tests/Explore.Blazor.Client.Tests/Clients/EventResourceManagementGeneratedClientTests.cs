using System.Text.Json;

namespace Explore.Blazor.Client.Tests.Clients;

public sealed class EventResourceManagementGeneratedClientTests
{
    [Test]
    public async Task ManagementDetailAndCollectionPreserveTypedVersionsMetadataAndActions()
    {
        Guid id = Guid.CreateVersion7(), version = Guid.CreateVersion7();
        var edit = $"/api/eventresource/{id:D}";
        var resource = new
        {
            id, eventId = Guid.CreateVersion7(), version, publicationState = "Draft",
            createdAt = DateTimeOffset.UtcNow,
            draft = new
            {
                title = "Private workshop notes", kind = "GeneralDocument", disclosureMode = "EligibleOnly",
                deliveryType = "StoredFile", availability = new { },
                audienceRules = new[] { new { kind = "Public" } }
            },
            _links = new { edit = new { href = edit, method = "PUT" } }
        };
        var detail = JsonSerializer.Deserialize<HalResourceOfEventResourceManagementDto>(JsonSerializer.Serialize(resource))
            ?? throw new InvalidOperationException("Missing generated detail.");
        await Assert.That(detail.Id).IsEqualTo(id);
        await Assert.That(detail.Version).IsEqualTo(version);
        await Assert.That(detail.PublicationState.ToString()).IsEqualTo("Draft");
        await Assert.That(detail.Draft.Title).IsEqualTo(resource.draft.title);
        await Assert.That(detail.Draft.Kind.ToString()).IsEqualTo("GeneralDocument");
        await Assert.That(detail._links?["edit"].Href).IsEqualTo(edit);

        var collection = JsonSerializer.Deserialize<EventResourceManagementCollectionDto>(JsonSerializer.Serialize(new
        {
            pageNumber = 2, pageSize = 20, _links = new { self = new { href = "/api/event/test/resources/management?page=2" } },
            _embedded = new { items = new[] { resource } }
        })) ?? throw new InvalidOperationException("Missing generated collection.");
        var item = (collection._embedded.Items
            ?? throw new InvalidOperationException("Missing generated items.")).Single();
        await Assert.That(item.Version).IsEqualTo(version);
        await Assert.That(item.Draft.Title).IsEqualTo(resource.draft.title);
        await Assert.That(item._links?["edit"].Href).IsEqualTo(edit);
        await Assert.That(collection.PageNumber).IsEqualTo(2);
    }
}

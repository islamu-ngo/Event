using System.Text.Json;

namespace Explore.Blazor.Client.Tests.Clients;

public sealed class EventResourceAudienceGeneratedClientTests
{
    [Test]
    public async Task AudienceDetailAndContinuationRemainTypedAcrossGeneratedSerialization()
    {
        Guid id = Guid.CreateVersion7(), eventId = Guid.CreateVersion7();
        string cursor = Guid.CreateVersion7().ToString("N");
        var resource = new
        {
            id, eventId, title = "Public material notice", kind = "GeneralDocument",
            isTeaser = true, availability = "unavailable", requirements = "eligibility-required",
            _links = new { self = new { href = $"/api/eventresource/{id:D}" } }
        };
        var detail = JsonSerializer.Deserialize<HalResourceOfEventResourceAudienceDetailDto>(JsonSerializer.Serialize(resource))
            ?? throw new InvalidOperationException("Missing generated audience detail.");
        await Assert.That(detail.Id).IsEqualTo(id);
        await Assert.That(detail.Title).IsEqualTo(resource.title);
        await Assert.That(detail.Kind.ToString()).IsEqualTo("GeneralDocument");
        await Assert.That(detail.IsTeaser).IsTrue();
        await Assert.That(detail.Description).IsNull();
        await Assert.That(detail.AccessibleAlternativeEventResourceId).IsNull();
        await Assert.That(detail.File).IsNull();
        await Assert.That(detail._links?["self"].Href).IsEqualTo($"/api/eventresource/{id:D}");

        var page = JsonSerializer.Deserialize<EventResourceAudiencePageResource>(JsonSerializer.Serialize(new
        {
            nextCursor = cursor,
            _links = new { next = new { href = $"/api/event/{eventId:D}/resources?cursor={cursor}" } },
            _embedded = new { items = new[] { resource } }
        })) ?? throw new InvalidOperationException("Missing generated audience page.");
        await Assert.That(page.NextCursor).IsEqualTo(cursor);
        var item = (page._embedded.Items ?? throw new InvalidOperationException("Missing generated audience items.")).Single();
        await Assert.That(item.Id).IsEqualTo(id);
        await Assert.That(item.IsTeaser).IsTrue();
        await Assert.That(page._links?["next"].Href).IsEqualTo($"/api/event/{eventId:D}/resources?cursor={cursor}");
    }

    [Test]
    public async Task EligibleFileDescriptorAndDownloadAffordanceRemainTyped()
    {
        Guid id = Guid.CreateVersion7(), eventId = Guid.CreateVersion7();
        var resource = new
        {
            id, eventId, title = "Handout", kind = "GeneralDocument",
            isTeaser = false, availability = "available",
            file = new { fileName = "handout.pdf", contentType = "application/pdf", sizeBytes = 123L, safetyState = "unscanned" },
            _links = new { download = new { href = $"/api/eventresource/{id:D}/content" } }
        };
        var detail = JsonSerializer.Deserialize<HalResourceOfEventResourceAudienceDetailDto>(JsonSerializer.Serialize(resource))!;
        EventResourceFileMetadataDto file = detail.File!;
        await Assert.That(file.FileName).IsEqualTo("handout.pdf");
        await Assert.That(file.ContentType).IsEqualTo("application/pdf");
        await Assert.That(file.SizeBytes).IsEqualTo(123L);
        await Assert.That(file.SafetyState).IsEqualTo("unscanned");
        await Assert.That(detail._links!["download"].Href).IsEqualTo($"/api/eventresource/{id:D}/content");
    }
}

using System.Text.Json;

namespace Explore.Blazor.Client.Tests.Clients;

public sealed class EventResourceMetadataExportGeneratedClientTests
{
    [Test]
    public async Task PortableMetadataKeepsTypedAudienceAndRelativeTiming()
    {
        Guid id = Guid.CreateVersion7(), eventId = Guid.CreateVersion7();
        long offset = TimeSpan.FromHours(2).Ticks;
        var page = JsonSerializer.Deserialize<EventResourceMetadataExportPageDto>(JsonSerializer.Serialize(new
        {
            eventId, page = 1, pageSize = 20,
            items = new[]
            {
                new
                {
                    id, publicationState = "Draft", title = "Portable material",
                    kind = "GeneralDocument", disclosureMode = "EligibleOnly", deliveryType = "StoredFile",
                    file = new { fileName = "handout.pdf", contentType = "application/pdf", sizeBytes = 123L, safetyState = "unscanned" },
                    download = new { href = $"/api/eventresource/{id:D}/content" },
                    availability = new { startAnchor = "EventEnd", startOffsetTicks = offset },
                    audienceRules = new[] { new { kind = "Public" } }
                }
            }
        })) ?? throw new InvalidOperationException("Missing generated metadata export.");
        var item = (page.Items ?? throw new InvalidOperationException("Missing export items.")).Single();
        await Assert.That(page.EventId).IsEqualTo(eventId);
        await Assert.That(item.Id).IsEqualTo(id);
        await Assert.That(item.PublicationState.ToString()).IsEqualTo("Draft");
        await Assert.That(item.Availability.StartAnchor.ToString()).IsEqualTo("EventEnd");
        await Assert.That(item.Availability.StartOffsetTicks).IsEqualTo(offset);
        EventResourceFileMetadataDto file = item.File!;
        await Assert.That(file.FileName).IsEqualTo("handout.pdf");
        await Assert.That(file.ContentType).IsEqualTo("application/pdf");
        await Assert.That(file.SizeBytes).IsEqualTo(123L);
        await Assert.That(file.SafetyState).IsEqualTo("unscanned");
        await Assert.That(item.Download!.Href).IsEqualTo($"/api/eventresource/{id:D}/content");
        await Assert.That((item.AudienceRules ?? throw new InvalidOperationException("Missing audience semantics."))
            .Single().Kind.ToString()).IsEqualTo("Public");
    }
}

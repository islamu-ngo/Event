using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Components.Events;
using Explore.Blazor.Client.Localization;
using Explore.Blazor.Client.Models.Events;

namespace Explore.Blazor.Client.Tests.Accessibility;

/// <summary>
/// The privacy UI is the surface where a mistake exposes someone's home address, so its accessibility and
/// localization guarantees are asserted rather than assumed.
/// </summary>
public sealed class EventLocationPrivacyAccessibilityTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();

    [Test]
    public async Task ArabicRemainsRegisteredAsARightToLeftCulture()
    {
        var arabic = CultureRegistry.GetAll().Single(culture => culture.Code == "ar");

        await Assert.That(arabic.IsRtl).IsTrue();
    }

    [Test]
    public async Task DisclosureCard_AnnouncesTheToBeAnnouncedStateAsTextNotOnlyColor()
    {
        var cut = RenderCard(null);

        // A colour-only badge fails WCAG 1.4.1; the state must also be readable.
        await Assert.That(cut.Find("[data-testid='event-location-tba-badge']").TextContent.Trim())
            .IsNotEmpty();
        await Assert.That(cut.Find("[data-testid='event-location-headline']").TextContent.Trim())
            .IsNotEmpty();
    }

    [Test]
    public async Task DisclosureCard_ExposesTheMapLinkWithSafeExternalRelAttributes()
    {
        var cut = RenderCard(EventLocationDisclosureView.FromAttendee(new EventLocationAttendeeDto
        {
            EventLocationId = Guid.NewGuid(),
            State = EventLocationDisclosureState.Available,
            Fields = new EventLocationAttendeeFieldsDto
            {
                StreetAddress = "Rue Neuve 1",
                MapUrl = "https://maps.example/1"
            }
        }));

        var link = cut.Find("[data-testid='event-location-map']");
        await Assert.That(link.GetAttribute("target")).IsEqualTo("_blank");
        await Assert.That(link.GetAttribute("rel")).IsEqualTo("noopener noreferrer");
    }

    [Test]
    public async Task ManagementTable_LabelsEveryDataCellForSmallViewports()
    {
        var cut = _ctx.Render<EventLocationManagementTable>(parameters => parameters
            .Add(component => component.Rows, ManagementRows())
            .Add(component => component.EmptyMessage, "Nothing to review"));

        // MudTable renders DataLabel as the stacked-view header; a missing label leaves an orphan cell.
        var cells = cut.FindAll("td[data-label]");
        await Assert.That(cells.Count).IsGreaterThanOrEqualTo(5);
        foreach (var cell in cells)
        {
            await Assert.That(cell.GetAttribute("data-label")?.Trim()).IsNotEmpty();
        }
    }

    [Test]
    public async Task ManagementTable_MarksTheEmptyStateAsAStatusRegion()
    {
        var cut = _ctx.Render<EventLocationManagementTable>(parameters => parameters
            .Add(component => component.Rows, Array.Empty<HalResourceOfEventLocationManagementDto>())
            .Add(component => component.EmptyMessage, "Nothing is waiting for privacy review."));

        await Assert.That(cut.FindAll("[role='status']").Count).IsGreaterThanOrEqualTo(1);
    }

    public void Dispose() => _ctx.Dispose();

    private IRenderedComponent<EventLocationDisclosureCard> RenderCard(EventLocationDisclosureView? view) =>
        _ctx.Render<EventLocationDisclosureCard>(parameters =>
            parameters.Add(component => component.View, view));

    private static IReadOnlyList<HalResourceOfEventLocationManagementDto> ManagementRows() =>
    [
        new HalResourceOfEventLocationManagementDto
        {
            EventLocationId = Guid.NewGuid(),
            State = EventLocationDisclosureState.Available,
            NeedsPrivacyReview = false,
            Policy = new EventLocationDisclosurePolicyDto { FullDetailsAudienceId = 3 },
            Fields = new EventLocationManagementFieldsDto { VenueName = "Community Centre" },
            _links = new Dictionary<string, HalLink>()
        }
    ];

}

using System.Text.Json;
using Explore.Blazor.Client.Components.Events;
using Explore.Blazor.Client.Services;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Components.Event;

public sealed class EventDetailsSidebarTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();

    public void Dispose()
    {
        _ctx.Dispose();
    }

    [Test]
    public async Task Render_WhenParticipationLinksAreMissing_HidesParticipationActions()
    {
        var eventId = Guid.NewGuid();

        var cut = _ctx.RenderMudComponent<EventDetailsSidebar>(parameters => parameters
            .Add(component => component.SelectedEvent, CreateEventListItem(eventId))
            .Add(component => component.EventDetail, CreateEventDetail(eventId, includeStartRegistrationLink: false)));

        await Assert.That(cut.Markup).DoesNotContain("Register for this Event");
        await Assert.That(cut.FindAll("[data-testid='external-participation-action']")).IsEmpty();
    }

    [Test]
    public async Task Render_WhenExternalRegistrationHalLinkExists_UsesStoredRedirectHrefAndTitle()
    {
        var eventId = Guid.NewGuid();
        const string href = "/api/events/public-actions/123/redirect";
        const string title = "Reserve with the organizer";
        var detail = CreateEventDetail(eventId, includeStartRegistrationLink: false);
        detail = detail with { AdditionalProperties = CreateHalLink("external-registration", href, title) };

        var cut = _ctx.RenderMudComponent<EventDetailsSidebar>(parameters => parameters
            .Add(component => component.SelectedEvent, CreateEventListItem(eventId))
            .Add(component => component.EventDetail, detail));

        var link = cut.Find("[data-testid='external-participation-action']");
        await Assert.That(link.GetAttribute("href")).IsEqualTo(href);
        await Assert.That(link.GetAttribute("target")).IsEqualTo("_blank");
        await Assert.That(link.GetAttribute("rel")).IsEqualTo("noopener noreferrer");
        await Assert.That(link.TextContent).Contains(title);
        await Assert.That(cut.Markup).DoesNotContain("Register for this Event");
    }

    [Test]
    [Arguments("event_list")]
    [Arguments("event_preview")]
    public async Task Render_WhenExternalSurfaceIsBounded_ReplacesOnlySurfaceQuery(string surface)
    {
        var eventId = Guid.NewGuid();
        const string href = "/api/events/public-actions/123/redirect?campaign=summer&surface=event_detail#registration";
        var detail = CreateEventDetail(eventId, includeStartRegistrationLink: false);
        detail = detail with { AdditionalProperties = CreateHalLink("external-registration", href, "Reserve with the organizer") };

        var cut = _ctx.RenderMudComponent<EventDetailsSidebar>(parameters => parameters
            .Add(component => component.SelectedEvent, CreateEventListItem(eventId))
            .Add(component => component.EventDetail, detail)
            .Add(component => component.ExternalParticipationSurface, surface));

        var renderedHref = cut.Find("[data-testid='external-participation-action']").GetAttribute("href")!;
        var renderedUri = _ctx.Services.GetRequiredService<NavigationManager>().ToAbsoluteUri(renderedHref);
        await Assert.That(renderedUri.AbsolutePath).IsEqualTo("/api/events/public-actions/123/redirect");
        await Assert.That(renderedUri.Query).Contains("campaign=summer");
        await Assert.That(renderedUri.Query).Contains($"surface={surface}");
        await Assert.That(renderedUri.Query.Split("surface=", StringSplitOptions.None).Length).IsEqualTo(2);
        await Assert.That(renderedUri.Fragment).IsEqualTo("#registration");
    }

    [Test]
    public async Task Render_WhenActualEventImageIsReady_ShowsAccessibleLightboxTrigger()
    {
        var eventId = Guid.NewGuid();
        var eventItem = CreateEventListItem(eventId);
        eventItem = eventItem with { FeaturedImageUri = "https://example.test/event-image.webp" };

        var cut = _ctx.RenderMudComponent<EventDetailsSidebar>(parameters => parameters
            .Add(component => component.SelectedEvent, eventItem)
            .Add(component => component.EventDetail, CreateEventDetail(eventId, includeStartRegistrationLink: false))
            .Add(component => component.IsDetailImageLoading, false));

        await Assert.That(cut.Markup).Contains("aria-label=\"View full-size image for Registration Affordance Event\"");
        await Assert.That(cut.Markup).Contains("aria-haspopup=\"dialog\"");
    }

    [Test]
    public async Task Render_WhenEventUsesFallbackImage_ShowsAccessibleLightboxTrigger()
    {
        var eventId = Guid.NewGuid();

        var cut = _ctx.RenderMudComponent<EventDetailsSidebar>(parameters => parameters
            .Add(component => component.SelectedEvent, CreateEventListItem(eventId))
            .Add(component => component.EventDetail, CreateEventDetail(eventId, includeStartRegistrationLink: false)));

        await Assert.That(cut.Markup).Contains("aria-label=\"View full-size image for Registration Affordance Event\"");
        await Assert.That(cut.Markup).Contains("aria-haspopup=\"dialog\"");
    }

    [Test]
    public async Task Render_WhenFederatedSourceExists_ShowsNewTabOpenActionAtEndOfHeaderActions()
    {
        var eventId = Guid.NewGuid();
        var eventItem = CreateEventListItem(eventId);
        const string sourceHref = "/api/event/source/registration-event";
        eventItem.AdditionalProperties["eventDiscoverySource"] = "atproto";
        eventItem.AdditionalProperties["_links"] = JsonSerializer.SerializeToElement(
            new Dictionary<string, HalLink>
            {
                ["source"] = new() { Href = sourceHref, Method = "GET" }
            });

        var cut = _ctx.RenderMudComponent<EventDetailsSidebar>(parameters => parameters
            .Add(component => component.SelectedEvent, eventItem)
            .Add(component => component.EventDetail, CreateEventDetail(eventId, includeStartRegistrationLink: false)));

        var link = cut.Find("a.event-details-sidebar__external-link");
        await Assert.That(link.TextContent).Contains("Open");
        await Assert.That(link.GetAttribute("href")).IsEqualTo(sourceHref);
        await Assert.That(link.GetAttribute("target")).IsEqualTo("_blank");
        await Assert.That(link.GetAttribute("rel")).IsEqualTo("noopener noreferrer");
        await Assert.That(link.GetAttribute("aria-label"))
            .IsEqualTo("Open Registration Affordance Event on its external platform in a new tab");
    }

    [Test]
    public async Task Render_WhenExternalEventUrlIsMissing_HidesExternalOpenAction()
    {
        var eventId = Guid.NewGuid();

        var cut = _ctx.RenderMudComponent<EventDetailsSidebar>(parameters => parameters
            .Add(component => component.SelectedEvent, CreateEventListItem(eventId))
            .Add(component => component.EventDetail, CreateEventDetail(eventId, includeStartRegistrationLink: false)));

        await Assert.That(cut.FindAll("a.event-details-sidebar__external-link")).IsEmpty();
    }

    [Test]
    public async Task Render_WhenFederatedSourceExists_ShowsNewTabOpenAction()
    {
        var eventItem = CreateEventListItem(Guid.NewGuid());
        const string sourceHref = "/api/event/federated/record/source";
        eventItem = eventItem with { Id = null };
        eventItem.AdditionalProperties["eventDiscoverySource"] = "atproto";
        eventItem.AdditionalProperties["_links"] = JsonSerializer.SerializeToElement(
            new Dictionary<string, HalLink>
            {
                ["source"] = new() { Href = sourceHref, Method = "GET" }
            });

        var cut = _ctx.RenderMudComponent<EventDetailsSidebar>(parameters => parameters
            .Add(component => component.SelectedEvent, eventItem));

        var link = cut.Find("a.event-details-sidebar__external-link");
        await Assert.That(link.GetAttribute("href")).IsEqualTo(sourceHref);
        await Assert.That(link.GetAttribute("target")).IsEqualTo("_blank");
    }

    [Test]
    public async Task LightboxDialog_ClickOutsideImage_ClosesDialog()
    {
        _ctx.Render<MudPopoverProvider>();
        var provider = _ctx.Render<MudDialogProvider>();
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<EventImageLightboxDialog>
        {
            { component => component.ImageSource, "https://example.test/event-image.webp" },
            { component => component.Alt, "Registration Affordance Event" }
        };

        var dialog = await dialogService.ShowAsync<EventImageLightboxDialog>(
            string.Empty,
            parameters,
            DialogOptionsFactory.ImageLightbox());
        var surface = provider.WaitForElement(".event-image-lightbox-dialog__surface");

        await Assert.That(provider.Markup).Contains("alt=\"Registration Affordance Event\"");
        await Assert.That(provider.Markup).DoesNotContain("Close full-size image");
        surface.Click();

        await Assert.That((await dialog.Result).Canceled).IsTrue();
    }

    private static EventListDto CreateEventListItem(Guid eventId) => new()
    {
        Id = eventId,
        Title = "Registration Affordance Event",
        Description = "An event used to verify HAL-gated registration affordances.",
        EventTypeFullName = "Conference",
        EventStatusFullName = "Published",
        FirstSessionDate = TestTime.UtcNow.AddDays(7)
    };

    private static EventDto CreateEventDetail(Guid eventId, bool includeStartRegistrationLink) =>
        CreateEventDetail(eventId, includeStartRegistrationLink ? "start-registration" : null);

    private static EventDto CreateEventDetail(Guid eventId, string? participationRelation) => new()
    {
        Id = eventId,
        Title = "Registration Affordance Event",
        Description = "An event used to verify HAL-gated registration affordances.",
        EventStatusMasterCode = "PUBLISHED",
        AdditionalProperties = participationRelation is not null
            ? CreateHalLinks(participationRelation)
            : []
    };

    private static Dictionary<string, object> CreateHalLink(string relation, string href, string title)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            [relation] = new { href, method = "GET", title }
        }));

        return new Dictionary<string, object>
        {
            ["_links"] = doc.RootElement.Clone()
        };
    }

    private static Dictionary<string, object> CreateHalLinks(params string[] linkRels)
    {
        var links = string.Join(
            ',',
            linkRels.Select(rel => $"\"{rel}\":{{\"href\":\"/api/eventregistration\",\"method\":\"POST\"}}"));
        using var doc = JsonDocument.Parse($"{{\"_links\":{{{links}}}}}");

        return new Dictionary<string, object>
        {
            ["_links"] = doc.RootElement.GetProperty("_links").Clone()
        };
    }
}

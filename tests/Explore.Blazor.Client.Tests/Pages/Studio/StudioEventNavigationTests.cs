using System.Text.Json;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Components.Shell.Workspaces;
using Explore.Blazor.Client.Pages.Studio;
using Explore.Blazor.Client.Services;

namespace Explore.Blazor.Client.Tests.Pages.Studio;

public sealed class StudioEventNavigationTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly IEventService _eventService;
    private readonly IEventTicketingService _ticketingService;
    private readonly IEventPromotionService _promotionService;

    public StudioEventNavigationTests()
    {
        _eventService = _ctx.AddMockService<IEventService>();
        _ticketingService = _ctx.AddMockService<IEventTicketingService>();
        _promotionService = _ctx.AddMockService<IEventPromotionService>();
        _ticketingService.GetCatalogAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((EventTicketCatalogState?)null);
        _ctx.Services.AddScoped<StudioEventContextState>();
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    [Arguments("edit", "Details")]
    [Arguments("publish-readiness", "Publication")]
    [Arguments("sessions", "Schedule")]
    [Arguments("configure-participation", "Registration")]
    [Arguments("view-registration-orders", "Orders")]
    [Arguments("manage-registration-channels", "Integrations")]
    [Arguments("view-registration-provider-health", "Integrations")]
    [Arguments("view-participants", "Attendees")]
    [Arguments("manage-registration-workflow", "Forms")]
    [Arguments("team", "Team")]
    [Arguments("delete", "Danger zone")]
    public async Task Render_ShowsSectionOnlyWhenMappedHalRelationExists(string relation, string expectedLabel)
    {
        var resource = CreateEvent(relation);
        _eventService.GetEventByIdAsync(resource.Id!.Value).Returns(resource);

        var cut = _ctx.RenderMudComponent<StudioEventNavigation>(parameters => parameters
            .Add(component => component.EventId, resource.Id.Value));

        cut.WaitForAssertion(() => cut.FindAll("a").Any(link => link.TextContent.Contains(expectedLabel, StringComparison.Ordinal)));
        await Assert.That(cut.FindAll("[data-event-section]").Count).IsEqualTo(1);
        await Assert.That(cut.Markup).Contains("All events");
        await Assert.That(cut.Markup).DoesNotContain("studio-actor-switcher");
    }

    [Test]
    public async Task Render_WithLegacyRegistrationRelation_OmitsRegistrationSection()
    {
        var resource = CreateEvent("registration");
        _eventService.GetEventByIdAsync(resource.Id!.Value).Returns(resource);

        var cut = _ctx.RenderMudComponent<StudioEventNavigation>(parameters => parameters
            .Add(component => component.EventId, resource.Id.Value));

        cut.WaitForAssertion(() => cut.Find("[data-testid='studio-event-navigation']"));
        await Assert.That(cut.FindAll("[data-event-section='registration']")).IsEmpty();
        await Assert.That(cut.Markup).DoesNotContain("Registration");
    }

    [Test]
    public async Task Render_WithLegacyTicketingRelation_OmitsTicketsSection()
    {
        var resource = CreateEvent("ticketing");
        _eventService.GetEventByIdAsync(resource.Id!.Value).Returns(resource);

        var cut = _ctx.RenderMudComponent<StudioEventNavigation>(parameters => parameters
            .Add(component => component.EventId, resource.Id.Value));

        cut.WaitForAssertion(() => cut.Find("[data-testid='studio-event-navigation']"));
        await Assert.That(cut.FindAll("[data-event-section='tickets']")).IsEmpty();
        await Assert.That(cut.Markup).DoesNotContain("Tickets");
    }

    [Test]
    [Arguments("manage-ticket-types")]
    [Arguments("manage-capacity-pools")]
    public async Task Render_TicketsSectionAcceptsEitherEventManagementRelation(string relation)
    {
        var resource = CreateEvent(relation);
        _eventService.GetEventByIdAsync(resource.Id!.Value).Returns(resource);
        var cut = _ctx.RenderMudComponent<StudioEventNavigation>(parameters => parameters
            .Add(component => component.EventId, resource.Id.Value));

        cut.WaitForElement("[data-event-section='tickets']");
        await Assert.That(cut.FindAll("[data-event-section='tickets']").Count).IsEqualTo(1);
    }

    [Test]
    public async Task Render_TicketsSectionRequiresAtLeastOneEventManagementRelation()
    {
        var resource = CreateEvent();
        _eventService.GetEventByIdAsync(resource.Id!.Value).Returns(resource);
        var cut = _ctx.RenderMudComponent<StudioEventNavigation>(parameters => parameters
            .Add(component => component.EventId, resource.Id.Value));

        cut.WaitForElement("[data-testid='studio-event-navigation']");
        await Assert.That(cut.FindAll("[data-event-section='tickets']")).IsEmpty();
    }

    [Test]
    public async Task Render_WithoutPromotionManagementRelation_OmitsPromotionsSection()
    {
        var resource = CreateEvent("edit");
        _eventService.GetEventByIdAsync(resource.Id!.Value).Returns(resource);

        var cut = _ctx.RenderMudComponent<StudioEventNavigation>(parameters => parameters
            .Add(component => component.EventId, resource.Id.Value));

        cut.WaitForElement("[data-testid='studio-event-navigation']");
        await Assert.That(cut.FindAll("[data-event-section='promotions']")).IsEmpty();
    }

    [Test]
    public async Task Render_PromotionsSectionRequiresCreatePromotionCollectionRelation()
    {
        var resource = CreateEvent("manage-ticket-types");
        var catalogVersionId = Guid.CreateVersion7();
        _eventService.GetEventByIdAsync(resource.Id!.Value).Returns(resource);
        _ticketingService.GetCatalogAsync(resource.Id.Value, Arg.Any<CancellationToken>())
            .Returns(Catalog(resource.Id.Value, catalogVersionId));
        _promotionService.GetPromotionsAsync(resource.Id.Value, catalogVersionId, Arg.Any<CancellationToken>())
            .Returns(PromotionManagementCollectionState.Create(
                resource.Id.Value,
                catalogVersionId,
                [],
                new Dictionary<string, HalLink>
                {
                    ["create-promotion"] = new() { Href = $"/api/events/{resource.Id}/promotions", Method = "POST" }
                }));

        var cut = _ctx.RenderMudComponent<StudioEventNavigation>(parameters => parameters
            .Add(component => component.EventId, resource.Id.Value));

        cut.WaitForElement("[data-event-section='promotions']");
        await Assert.That(cut.FindAll("[data-event-section='promotions']").Count).IsEqualTo(1);
    }

    [Test]
    public async Task Render_PromotionCollectionWithoutCreateRelation_OmitsPromotionsSection()
    {
        var resource = CreateEvent("manage-ticket-types");
        var catalogVersionId = Guid.CreateVersion7();
        _eventService.GetEventByIdAsync(resource.Id!.Value).Returns(resource);
        _ticketingService.GetCatalogAsync(resource.Id.Value, Arg.Any<CancellationToken>())
            .Returns(Catalog(resource.Id.Value, catalogVersionId));
        _promotionService.GetPromotionsAsync(resource.Id.Value, catalogVersionId, Arg.Any<CancellationToken>())
            .Returns(PromotionManagementCollectionState.Create(resource.Id.Value, catalogVersionId, []));

        var cut = _ctx.RenderMudComponent<StudioEventNavigation>(parameters => parameters
            .Add(component => component.EventId, resource.Id.Value));

        cut.WaitForElement("[data-testid='studio-event-navigation']");
        await Assert.That(cut.FindAll("[data-event-section='promotions']")).IsEmpty();
    }

    [Test]
    public async Task Render_FormsSectionRequiresManageRegistrationWorkflowRelation()
    {
        var resource = CreateEvent();
        _eventService.GetEventByIdAsync(resource.Id!.Value).Returns(resource);
        var cut = _ctx.RenderMudComponent<StudioEventNavigation>(parameters => parameters
            .Add(component => component.EventId, resource.Id.Value));

        cut.WaitForElement("[data-testid='studio-event-navigation']");
        await Assert.That(cut.FindAll("[data-event-section='forms']")).IsEmpty();
    }

    [Test]
    public async Task Render_TeamSection_UsesCanonicalPublicEventRouteInsteadOfRawGuidRoute()
    {
        var resource = CreateEvent("team");
        _eventService.GetEventByIdAsync(resource.Id!.Value).Returns(resource);

        var cut = _ctx.RenderMudComponent<StudioEventNavigation>(parameters => parameters
            .Add(component => component.EventId, resource.Id.Value));

        cut.WaitForAssertion(() => cut.Find("[data-event-section='team']"));
        await Assert.That(cut.FindAll("a[href='/events/community-gathering-EVT123']").Count).IsEqualTo(1);
    }

    private static EventDto CreateEvent(params string[] relations)
    {
        var eventId = Guid.CreateVersion7();
        var resource = new EventDto
        {
            Id = eventId,
            Title = "Community gathering",
            Slug = "community-gathering",
            PublicCode = "EVT123",
            EventStatusFullName = "Draft"
        };
        SetRelations(resource, relations);
        return resource;
    }

    private static void SetRelations(EventDto resource, params string[] relations)
    {
        resource.AdditionalProperties["_links"] = JsonSerializer.SerializeToElement(
            relations.ToDictionary(
                relation => relation,
                relation => (object)new { href = $"/api/event/{resource.Id}", method = "GET" }));
    }

    private static EventTicketCatalogState Catalog(Guid eventId, Guid catalogVersionId) => new(
        eventId,
        catalogVersionId,
        1,
        "USD",
        1,
        "DRAFT",
        "Draft",
        [],
        [],
        new Dictionary<string, HalLink>(StringComparer.Ordinal));
}

using System.Text.Json;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Components.Shell.Workspaces;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Pages.Studio;
using Explore.Blazor.Client.Services;

namespace Explore.Blazor.Client.Tests.Pages.Studio;

public sealed class StudioEventNavigationTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly IEventService _eventService;
    private readonly IEventTicketingService _ticketingService;
    private readonly IEventPromotionService _promotionService;
    private readonly IEventResourceService _resources;

    public StudioEventNavigationTests()
    {
        _eventService = _ctx.AddMockService<IEventService>();
        _ticketingService = _ctx.AddMockService<IEventTicketingService>();
        _promotionService = _ctx.AddMockService<IEventPromotionService>();
        _resources = _ctx.AddMockService<IEventResourceService>();
        _resources.AudienceAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ResourcePage(false));
        _ticketingService.GetCatalogAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((EventTicketCatalogState?)null);
        _ctx.Services.AddScoped<StudioEventContextState>();
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Render_ResourceNavigationRequiresCollectionManageRelation(bool advertised)
    {
        var resource = CreateEvent("edit");
        _eventService.GetEventByIdAsync(resource.Id!.Value).Returns(resource);
        _resources.AudienceAsync(resource.Id.Value, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ResourcePage(advertised));

        var cut = _ctx.RenderMudComponent<StudioEventNavigation>(parameters => parameters
            .Add(component => component.EventId, resource.Id.Value));

        cut.WaitForElement("[data-testid='studio-event-navigation']");
        if (advertised)
            cut.WaitForElement("[data-event-section='resources']");
        else
            await Assert.That(cut.FindAll("[data-event-section='resources']")).IsEmpty();
    }

    [Test]
    [Arguments("edit", "details")]
    [Arguments("sessions", "schedule")]
    [Arguments("configure-participation", "registration")]
    [Arguments("view-registration-orders", "orders")]
    [Arguments("manage-registration-channels", "integrations")]
    [Arguments("view-registration-provider-health", "integrations")]
    [Arguments("view-participants", "attendees")]
    [Arguments("manage-registration-workflow", "forms")]
    [Arguments("view-registration-analytics", "analytics")]
    public async Task Render_ShowsSectionOnlyWhenMappedHalRelationExists(string relation, string section)
    {
        var resource = CreateEvent(relation);
        _eventService.GetEventByIdAsync(resource.Id!.Value).Returns(resource);

        var cut = _ctx.RenderMudComponent<StudioEventNavigation>(parameters => parameters
            .Add(component => component.EventId, resource.Id.Value));

        var link = cut.Find($"[data-event-section='{section}']");
        await Assert.That(link.QuerySelector("a")!.GetAttribute("href")).IsEqualTo($"/studio/events/{resource.Id}/{section}");
        await Assert.That(cut.FindAll("[data-event-section]").Count).IsEqualTo(2);
        await Assert.That(cut.Markup).Contains("All events");
        await Assert.That(cut.Markup).DoesNotContain("studio-actor-switcher");
    }

    [Test]
    public async Task Render_PublicationReadinessUsesCanonicalOverviewWithoutOrphanRoute()
    {
        var resource = CreateEvent("publish-readiness");
        _eventService.GetEventByIdAsync(resource.Id!.Value).Returns(resource);
        var cut = _ctx.RenderMudComponent<StudioEventNavigation>(p => p.Add(c => c.EventId, resource.Id.Value));
        await Assert.That(cut.Find("[data-event-section='overview'] a").GetAttribute("href"))
            .IsEqualTo($"/studio/events/{resource.Id}");
        await Assert.That(cut.FindAll($"a[href='/studio/events/{resource.Id}/publication']")).IsEmpty();
    }

    [Test]
    [Arguments("team")]
    [Arguments("delete")]
    public async Task Render_UnimplementedModulesNeverLinkToPublicEvent(string relation)
    {
        var resource = CreateEvent(relation);
        _eventService.GetEventByIdAsync(resource.Id!.Value).Returns(resource);
        var cut = _ctx.RenderMudComponent<StudioEventNavigation>(p => p.Add(c => c.EventId, resource.Id.Value));
        await Assert.That(cut.FindAll("[data-event-section='team'], [data-event-section='danger']")).IsEmpty();
        await Assert.That(cut.Find("[data-event-section='overview'] a").GetAttribute("href"))
            .IsEqualTo($"/studio/events/{resource.Id}");
    }

    [Test]
    public async Task ResourceLoad_FromPreviousAuthorityCannotRestoreNavigationAfterDenial()
    {
        var resource = CreateEvent("edit");
        var eventId = resource.Id!.Value;
        _eventService.GetEventByIdAsync(eventId).Returns(resource);
        var cut = _ctx.RenderMudComponent<StudioEventNavigation>(p => p.Add(c => c.EventId, eventId));
        var state = _ctx.Services.GetRequiredService<StudioEventContextState>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<EventResourceAudiencePageResource>(TaskCreationOptions.RunContinuationsAsynchronously);
        _resources.AudienceAsync(eventId, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { started.TrySetResult(); return completion.Task; });
        await cut.InvokeAsync(() => state.ReloadAsync(eventId));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _eventService.GetEventByIdAsync(eventId).Returns((EventDto?)null);
        await cut.InvokeAsync(() => state.ReloadAsync(eventId));
        var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cut.OnAfterRender += (_, _) => rendered.TrySetResult();
        completion.SetResult(ResourcePage(true));
        await rendered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(state.Event).IsNull();
        await Assert.That(cut.FindAll("[data-event-section='resources']")).IsEmpty();
        await Assert.That(cut.FindAll("[data-event-section='details']")).IsEmpty();
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
    public async Task Render_TeamRelationCannotExposePublicRouteAsManagement()
    {
        var resource = CreateEvent("team");
        _eventService.GetEventByIdAsync(resource.Id!.Value).Returns(resource);

        var cut = _ctx.RenderMudComponent<StudioEventNavigation>(parameters => parameters
            .Add(component => component.EventId, resource.Id.Value));

        await Assert.That(cut.FindAll("[data-event-section='team']")).IsEmpty();
        await Assert.That(cut.FindAll("a[href='/events/community-gathering-EVT123']")).IsEmpty();
        await Assert.That(cut.Find("[data-event-section='overview'] a").GetAttribute("href"))
            .IsEqualTo($"/studio/events/{resource.Id}");
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

    private static EventResourceAudiencePageResource ResourcePage(bool canManage) => new()
    {
        _links = canManage
            ? new Dictionary<string, HalLink>
            {
                ["manage-resources"] = new()
                {
                    Href = "/api/eventresource/event/manage",
                    Method = "GET"
                }
            }
            : new Dictionary<string, HalLink>(),
        _embedded = new HalCollectionEmbeddedOfEventResourceAudienceDetailDto()
    };

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

using System.Text.Json;
using Blazouter.Services;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Contracts.Services.Events;
using Explore.Blazor.Client.Pages.Studio;
using Explore.Blazor.Client.Services;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Studio;

public sealed class StudioEventShellTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly IEventService _eventService;
    private readonly IEventTicketingService _ticketingService;
    private readonly IEventPromotionService _promotionService;

    public StudioEventShellTests()
    {
        _eventService = _ctx.AddMockService<IEventService>();
        _ticketingService = _ctx.AddMockService<IEventTicketingService>();
        _promotionService = _ctx.AddMockService<IEventPromotionService>();
        _ticketingService.GetCatalogAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((EventTicketCatalogState?)null);
        _ctx.AddMockService<IRegistrationFormAuthoringService>();
        _ctx.AddMockService<IEventDayService>();
        _ctx.AddMockService<IEventSessionService>();
        _ctx.AddMockService<Explore.Blazor.Client.Contracts.Services.Accessibility.IAccessibilityAnnouncerService>();
        _ctx.AddMockService<Explore.Blazor.Client.Contracts.Services.Accessibility.IAccessibilityFocusService>();
        _ctx.Services.AddSingleton(Substitute.For<IDialogService>());
        _ctx.Services.AddScoped<RouterStateService>();
        _ctx.Services.AddScoped<StudioEventContextState>();
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task PromotionsRoute_WithoutCatalog_FailsClosedInsidePromotionComponent()
    {
        var resource = CreateEvent();
        _eventService.GetEventByIdAsync(resource.Id!.Value).Returns(resource);
        _ctx.Services.GetRequiredService<NavigationManager>()
            .NavigateTo($"/studio/events/{resource.Id}/promotions");

        var cut = _ctx.RenderMudComponent<StudioEventShell>(parameters => parameters
            .Add(component => component.EventId, resource.Id.Value));

        cut.WaitForElement("[data-testid='studio-event-promotions']");
        cut.WaitForAssertion(() => cut.Markup.Contains("Promotion management is not available", StringComparison.Ordinal));
        await Assert.That(cut.FindAll("[data-testid='show-create-promotion']")).IsEmpty();
        await _promotionService.DidNotReceive().GetPromotionsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PromotionsRoute_WithCreateCollectionRelation_RendersManagementSurface()
    {
        var resource = CreateEvent("manage-ticket-types");
        var catalogVersionId = Guid.CreateVersion7();
        _eventService.GetEventByIdAsync(resource.Id!.Value).Returns(resource);
        _ticketingService.GetCatalogAsync(resource.Id.Value, Arg.Any<CancellationToken>()).Returns(new EventTicketCatalogState(
            resource.Id.Value,
            catalogVersionId,
            1,
            "USD",
            1,
            "DRAFT",
            "Draft",
            [],
            [],
            new Dictionary<string, HalLink>(StringComparer.Ordinal)));
        _promotionService.GetPromotionsAsync(resource.Id.Value, catalogVersionId, Arg.Any<CancellationToken>())
            .Returns(PromotionManagementCollectionState.Create(
                resource.Id.Value,
                catalogVersionId,
                [],
                new Dictionary<string, HalLink>
                {
                    ["create-promotion"] = new() { Href = $"/api/events/{resource.Id}/promotions", Method = "POST" }
                }));
        _ctx.Services.GetRequiredService<NavigationManager>()
            .NavigateTo($"/studio/events/{resource.Id}/promotions");

        var cut = _ctx.RenderMudComponent<StudioEventShell>(parameters => parameters
            .Add(component => component.EventId, resource.Id.Value));

        cut.WaitForElement("[data-testid='show-create-promotion']");
        await Assert.That(cut.FindAll("[data-testid='studio-event-promotions']").Count).IsEqualTo(1);
    }

    [Test]
    public async Task RegistrationRoute_WithoutConfigureParticipationRelation_FailsClosed()
    {
        var resource = CreateEvent();
        _eventService.GetEventByIdAsync(resource.Id!.Value).Returns(resource);
        _ctx.Services.GetRequiredService<NavigationManager>()
            .NavigateTo($"/studio/events/{resource.Id}/registration");

        var cut = _ctx.RenderMudComponent<StudioEventShell>(parameters => parameters
            .Add(component => component.EventId, resource.Id.Value));

        cut.WaitForElement("[data-testid='participation-route-unavailable']");
        await Assert.That(cut.FindAll("[data-testid='participation-configuration-editor']")).IsEmpty();
        await Assert.That(cut.Find("h1").TextContent).IsEqualTo("Community gathering");
        await Assert.That(cut.FindAll("h1").Count).IsEqualTo(1);
    }

    [Test]
    public async Task RegistrationRoute_WithConfigureParticipationRelation_RendersEditor()
    {
        var resource = CreateEvent("configure-participation");
        _eventService.GetEventByIdAsync(resource.Id!.Value).Returns(resource);
        _ctx.Services.GetRequiredService<NavigationManager>()
            .NavigateTo($"/studio/events/{resource.Id}/registration");

        var cut = _ctx.RenderMudComponent<StudioEventShell>(parameters => parameters
            .Add(component => component.EventId, resource.Id.Value));

        cut.WaitForElement("[data-testid='participation-configuration-editor']");
        await Assert.That(cut.FindAll("[data-testid='participation-route-unavailable']")).IsEmpty();
    }

    [Test]
    public async Task TicketsRoute_WithoutEventManagementRelations_FailsClosed()
    {
        var resource = CreateEvent();
        _eventService.GetEventByIdAsync(resource.Id!.Value).Returns(resource);
        _ctx.Services.GetRequiredService<NavigationManager>()
            .NavigateTo($"/studio/events/{resource.Id}/tickets");

        var cut = _ctx.RenderMudComponent<StudioEventShell>(parameters => parameters
            .Add(component => component.EventId, resource.Id.Value));

        cut.WaitForElement("[data-testid='ticketing-route-unavailable']");
        await Assert.That(cut.FindAll("[data-testid='event-ticket-catalog-editor']")).IsEmpty();
    }

    [Test]
    [Arguments("manage-ticket-types", true, false)]
    [Arguments("manage-capacity-pools", false, true)]
    public async Task TicketsRoute_WithEitherEventManagementRelation_RendersEditor(
        string relation,
        bool canManageTicketTypes,
        bool canManageCapacityPools)
    {
        var resource = CreateEvent(relation);
        _eventService.GetEventByIdAsync(resource.Id!.Value).Returns(resource);
        _ctx.Services.GetRequiredService<NavigationManager>()
            .NavigateTo($"/studio/events/{resource.Id}/tickets");

        var cut = _ctx.RenderMudComponent<StudioEventShell>(parameters => parameters
            .Add(component => component.EventId, resource.Id.Value));

        cut.WaitForElement("[data-testid='event-ticket-catalog-editor']");
        await Assert.That(cut.FindAll("[data-testid='ticketing-route-unavailable']")).IsEmpty();
        var editor = cut.FindComponent<EventTicketCatalogEditor>();
        await Assert.That(editor.Instance.CanManageTicketTypes).IsEqualTo(canManageTicketTypes);
        await Assert.That(editor.Instance.CanManageCapacityPools).IsEqualTo(canManageCapacityPools);
    }

    [Test]
    public async Task FormsRoute_WithoutManageRegistrationWorkflowRelation_FailsClosed()
    {
        var resource = CreateEvent();
        _eventService.GetEventByIdAsync(resource.Id!.Value).Returns(resource);
        _ctx.Services.GetRequiredService<NavigationManager>()
            .NavigateTo($"/studio/events/{resource.Id}/forms");

        var cut = _ctx.RenderMudComponent<StudioEventShell>(parameters => parameters
            .Add(component => component.EventId, resource.Id.Value));

        cut.WaitForElement("[data-testid='registration-forms-route-unavailable']");
        await Assert.That(cut.FindAll("[data-testid='registration-form-builder']")).IsEmpty();
    }

    [Test]
    public async Task FormsRoute_WithManageRegistrationWorkflowRelation_UsesSameShellBuilder()
    {
        var resource = CreateEvent("manage-registration-workflow");
        _eventService.GetEventByIdAsync(resource.Id!.Value).Returns(resource);
        _ctx.Services.GetRequiredService<NavigationManager>()
            .NavigateTo($"/studio/events/{resource.Id}/forms");

        var cut = _ctx.RenderMudComponent<StudioEventShell>(parameters => parameters
            .Add(component => component.EventId, resource.Id.Value));

        cut.WaitForElement("[data-testid='registration-form-builder']");
        await Assert.That(cut.FindAll("h1").Count).IsEqualTo(1);
        await _eventService.Received(1).GetEventByIdAsync(resource.Id.Value);
    }

    [Test]
    public async Task AnalyticsRoute_WithoutAnalyticsRelation_FailsClosed()
    {
        var resource = CreateEvent();
        _eventService.GetEventByIdAsync(resource.Id!.Value).Returns(resource);
        _ctx.Services.GetRequiredService<NavigationManager>()
            .NavigateTo($"/studio/events/{resource.Id}/analytics");

        var cut = _ctx.RenderMudComponent<StudioEventShell>(parameters => parameters
            .Add(component => component.EventId, resource.Id.Value));

        cut.WaitForElement("[data-testid='registration-analytics-route-unavailable']");
        await Assert.That(cut.FindAll("[data-testid='registration-analytics']")).IsEmpty();
    }

    [Test]
    public async Task AnalyticsRoute_WithAnalyticsRelation_RendersAggregateCells()
    {
        var resource = CreateEvent("view-registration-analytics");
        _eventService.GetEventByIdAsync(resource.Id!.Value).Returns(resource);
        var formId = Guid.CreateVersion7();
        var versionId = Guid.CreateVersion7();
        var authoring = _ctx.Services.GetRequiredService<IRegistrationFormAuthoringService>();
        authoring.GetWorkflowAsync(resource.Id.Value, Arg.Any<CancellationToken>()).Returns(new HalResourceOfRegistrationWorkflowDto
        {
            Id = Guid.CreateVersion7(),
            EventId = resource.Id.Value,
            Purpose = "registration",
            Forms =
            [
                new RegistrationFormDto
                {
                    Id = formId,
                    Name = "Registration",
                    Versions = [new RegistrationFormVersionSummaryDto { Id = versionId, Version = 1, StatusName = "Published" }]
                }
            ]
        });
        authoring.GetAnalyticsAsync(resource.Id.Value, formId, versionId, Arg.Any<HalLink>(), Arg.Any<CancellationToken>())
            .Returns(new HalResourceOfRegistrationAnswerAnalyticsDto
            {
                EventId = resource.Id.Value,
                FormId = formId,
                FormVersionId = versionId,
                MinimumCellSize = 3,
                Fields =
                [
                    new RegistrationAnswerFieldAggregateDto
                    {
                        Label = "Age band",
                        Namespace = "person",
                        Key = "age_band",
                        FieldTypeCode = "SINGLE_CHOICE",
                        ResponseCount = 3,
                        Cells = [new RegistrationAnswerAggregateCellDto { Value = "adult", Count = 3 }]
                    }
                ]
            });
        _ctx.Services.GetRequiredService<NavigationManager>()
            .NavigateTo($"/studio/events/{resource.Id}/analytics");

        var cut = _ctx.RenderMudComponent<StudioEventShell>(parameters => parameters
            .Add(component => component.EventId, resource.Id.Value));

        cut.WaitForElement("[data-testid='registration-analytics']");
        cut.WaitForAssertion(() => cut.Markup.Contains("Age band", StringComparison.Ordinal));
        await Assert.That(cut.Markup).Contains("adult");
        await Assert.That(cut.Markup).DoesNotContain("registration-analytics-route-unavailable");
    }

    [Test]
    public async Task IntegrationsRoute_WithoutProviderRelations_FailsClosed()
    {
        var resource = CreateEvent();
        _eventService.GetEventByIdAsync(resource.Id!.Value).Returns(resource);
        _ctx.Services.GetRequiredService<NavigationManager>()
            .NavigateTo($"/studio/events/{resource.Id}/integrations");

        var cut = _ctx.RenderMudComponent<StudioEventShell>(parameters => parameters
            .Add(component => component.EventId, resource.Id.Value));

        cut.WaitForElement("[data-testid='integrations-route-unavailable']");
        await Assert.That(cut.FindAll("[data-testid='studio-event-integrations']")).IsEmpty();
    }

    [Test]
    [Arguments("manage-registration-channels")]
    [Arguments("view-registration-provider-health")]
    public async Task IntegrationsRoute_WithProviderRelation_RendersManagementPage(string relation)
    {
        var resource = CreateEvent(relation) with { TenantId = Guid.CreateVersion7() };
        _eventService.GetEventByIdAsync(resource.Id!.Value).Returns(resource);
        var integrationService = _ctx.AddMockService<IRegistrationProviderIntegrationService>();
        integrationService.GetConnectionsAsync(resource.TenantId.Value, resource.Id.Value, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HalCollectionResourceOfRegistrationProviderConnectionDto()));
        integrationService.GetBindingsAsync(resource.TenantId.Value, resource.Id.Value, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HalCollectionResourceOfRegistrationProviderBindingDto()));
        integrationService.GetHealthAsync(resource.TenantId.Value, resource.Id.Value, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HalCollectionResourceOfRegistrationProviderBindingHealthDto()));
        integrationService.GetQueueAsync(resource.TenantId.Value, resource.Id.Value, 50, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HalCollectionResourceOfRegistrationProviderParkedQueueItemDto()));
        _ctx.Services.GetRequiredService<NavigationManager>()
            .NavigateTo($"/studio/events/{resource.Id}/integrations");

        var cut = _ctx.RenderMudComponent<StudioEventShell>(parameters => parameters
            .Add(component => component.EventId, resource.Id.Value));

        cut.WaitForElement("[data-testid='studio-event-integrations']");
        await Assert.That(cut.FindAll("[data-testid='integrations-route-unavailable']")).IsEmpty();
    }

    private static EventDto CreateEvent(params string[] relations)
    {
        var eventId = Guid.CreateVersion7();
        var resource = new EventDto
        {
            Id = eventId,
            Title = "Community gathering",
            EventStatusFullName = "Draft",
            ParticipationConfiguration = new ParticipationConfiguration
            {
                EventId = eventId,
                ConcurrencyStamp = Guid.CreateVersion7(),
                ParticipationHandlingModeId = 1,
                ParticipationHandlingModeCode = "INFORMATION_ONLY",
                ParticipationHandlingModeName = "Information only",
                AdvanceRegistrationObligationId = 1,
                AdvanceRegistrationObligationCode = "NOT_APPLICABLE",
                AdvanceRegistrationObligationName = "Not applicable"
            }
        };

        if (relations.Length > 0)
        {
            resource.AdditionalProperties["_links"] = JsonSerializer.SerializeToElement(
                relations.ToDictionary(
                    relation => relation,
                    relation => (object)new { href = $"/api/events/{eventId}/{relation}", method = "GET" }));
        }

        return resource;
    }
}

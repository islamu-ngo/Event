using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Components.Shell;
using Explore.Blazor.Client.Components.Shell.Workspaces;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Contracts.Services.Events;
using Explore.Blazor.Client.Contracts.Services.Shell;
using Explore.Blazor.Client.Pages.Studio;
using Explore.Blazor.Client.Services;
using Explore.Blazor.Client.Services.Ai;
using Explore.Blazor.Client.Services.Docking;
using Explore.Blazor.Client.Services.Shell;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Components.Shell;

public sealed class WorkspaceNavigationHostTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();

    public WorkspaceNavigationHostTests()
    {
        _ctx.Services.AddScoped<IWorkspaceRegistry, WorkspaceRegistry>();
        _ctx.Services.AddScoped<WorkspaceRouteClassifier>();
        _ctx.Services.AddScoped<UiShellState>();
        _ctx.Services.AddScoped<AiAssistantConversationState>();
        _ctx.Services.AddScoped<StudioEventContextState>();
        _ctx.Services.AddScoped<DockLayoutState>();
        _ctx.Services.AddScoped<IDockPanelRegistry>(p => p.GetRequiredService<DockLayoutState>());
        _ctx.AddMockService<IStudioContextService>();
        _ctx.AddMockService<IEventTicketingService>();
        _ctx.AddMockService<IEventPromotionService>();

        var publicExperience = Substitute.For<IPublicExperienceService>();
        publicExperience.GetCachedSettingsAsync().Returns(new PublicExperienceSettingsDto());
        _ctx.Services.AddSingleton(publicExperience);

        _ctx.Services.AddSingleton(new TenantNavLinksState());

        var shellContextService = Substitute.For<IUiShellContextService>();
        shellContextService.GetCachedContextAsync(Arg.Any<CancellationToken>())
            .Returns(new UiShellContextDto
            {
                ManagedActors =
                [
                    new ManagedActorDto
                    {
                        ActorId = Guid.CreateVersion7(),
                        ActorType = "Organization",
                        DisplayName = "Studio Organization"
                    }
                ]
            });
        _ctx.Services.AddSingleton(shellContextService);

        var eventService = Substitute.For<IEventService>();
        eventService.GetEventByIdAsync(Arg.Any<Guid>()).Returns(call => new EventDto
        {
            Id = call.Arg<Guid>(),
            Title = "Community gathering",
            EventStatusFullName = "Draft"
        });
        _ctx.Services.AddSingleton(eventService);
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task Render_EventsWorkspace_ShowsEventsNavigationContent()
    {
        _ctx.SetAnonymousUser();
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/events");

        var cut = _ctx.Render<WorkspaceNavigationHost>();

        await Assert.That(cut.Markup).Contains("events-workspace-navigation");
        await Assert.That(cut.Find("nav[data-testid='events-workspace-navigation']").GetAttribute("aria-label"))
            .IsEqualTo("Events workspace navigation");
    }

    [Test]
    public async Task Render_DedicatedSettingsWorkspace_HasNoShellNavigationProvider()
    {
        _ctx.SetAuthenticatedUser(Guid.NewGuid(), "Test User");
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/settings");

        var cut = _ctx.Render<WorkspaceNavigationHost>();

        await Assert.That(cut.Markup).IsEmpty();
    }

    [Test]
    public async Task Render_StudioEventsDeepLink_ShowsStudioNavigationContent()
    {
        _ctx.SetAuthenticatedUser(Guid.NewGuid(), "Studio User");
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/studio/events");

        var cut = _ctx.Render<WorkspaceNavigationHost>();

        cut.WaitForAssertion(() => cut.Find("[data-testid='studio-workspace-navigation']"));
        await Assert.That(cut.Markup).Contains("Studio Organization");
        await Assert.That(cut.Find("a[href='/studio/events']").GetAttribute("aria-current")).IsEqualTo("page");
    }

    [Test]
    public async Task Render_AiWorkspace_ShowsSearchableRecentConversations()
    {
        _ctx.SetAuthenticatedUser(Guid.NewGuid(), "AI User");
        var conversationState = _ctx.Services.GetRequiredService<AiAssistantConversationState>();
        conversationState.SetConversations(
        [
            new() { Id = Guid.CreateVersion7(), Title = "Budget planning", Status = "Active" },
            new() { Id = Guid.CreateVersion7(), Title = "Venue options", Status = "Active" }
        ]);
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/ai");

        var cut = _ctx.Render<WorkspaceNavigationHost>();

        await Assert.That(cut.Markup).Contains("ai-workspace-navigation");
        await Assert.That(cut.FindAll("[data-testid='ai-workspace-conversation']").Count).IsEqualTo(2);

        await cut.Find("[data-testid='ai-workspace-search']").InputAsync(new ChangeEventArgs { Value = "budget" });

        await Assert.That(cut.FindAll("[data-testid='ai-workspace-conversation']").Count).IsEqualTo(1);
        await Assert.That(cut.Markup).Contains("Budget planning");
        await Assert.That(cut.Markup).DoesNotContain("Venue options");
    }

    [Test]
    public async Task Render_StudioEventDeepLink_ReplacesActorNavigationWithEventNavigation()
    {
        _ctx.SetAuthenticatedUser(Guid.NewGuid(), "Studio User");
        var eventId = Guid.CreateVersion7();
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"/studio/events/{eventId}");

        var cut = _ctx.Render<WorkspaceNavigationHost>();

        cut.WaitForAssertion(() => cut.Find("[data-testid='studio-event-navigation']"));
        await Assert.That(cut.Markup).Contains("Community gathering");
        await Assert.That(cut.Markup).Contains("All events");
        await Assert.That(cut.Markup).DoesNotContain("studio-actor-switcher");
    }

    [Test]
    public async Task ContextualPersonalSettings_PreservesOriginWorkspaceNavigationContent()
    {
        _ctx.SetAuthenticatedUser(Guid.NewGuid(), "Test User");
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/events");

        var cut = _ctx.Render<WorkspaceNavigationHost>();
        await Assert.That(cut.Markup).Contains("events-workspace-navigation");

        var shellState = _ctx.Services.GetRequiredService<UiShellState>();
        await cut.InvokeAsync(() => shellState.NavigateToPersonalSettings("/settings/personal/appearance"));

        await Assert.That(cut.Markup).Contains("events-workspace-navigation");
        await Assert.That(cut.Markup).DoesNotContain("settings-workspace-navigation");
    }

    [Test]
    public async Task Render_HasWorkspaceNavHostTestHook()
    {
        _ctx.SetAnonymousUser();
        var cut = _ctx.Render<WorkspaceNavigationHost>();

        await Assert.That(cut.Find("[data-testid='workspace-nav-host']")).IsNotNull();
    }

    [Test]
    public async Task Render_EventsWithOverlay_ShowsSharedOverlayChrome()
    {
        _ctx.SetAnonymousUser();
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/events");

        var closeCount = 0;
        var entry = CreateOverlayDockPanelEntry();
        var cut = _ctx.Render<CascadingValue<DockPanelEntry>>(parameters => parameters
            .Add(p => p.Value, entry)
            .Add(p => p.IsFixed, true)
            .AddChildContent<WorkspaceNavigationHost>(child => child
                .Add(p => p.OnCloseRequested, EventCallback.Factory.Create(this, () => closeCount++))));

        await Assert.That(cut.Markup).Contains("workspace-nav-host__overlay-header");
        await Assert.That(cut.Markup).Contains("Close sidebar navigation");
        await Assert.That(cut.Markup).Contains("events-workspace-navigation");

        await cut.Find("[aria-label='Close sidebar navigation']").ClickAsync(new MouseEventArgs());

        await Assert.That(closeCount).IsEqualTo(1);
    }

    [Test]
    public async Task Render_DedicatedSettingsWithOverlay_RendersNothingWithoutProvider()
    {
        _ctx.SetAuthenticatedUser(Guid.NewGuid(), "Test User");
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/settings");

        var entry = CreateOverlayDockPanelEntry();
        var cut = _ctx.Render<CascadingValue<DockPanelEntry>>(parameters => parameters
            .Add(p => p.Value, entry)
            .Add(p => p.IsFixed, true)
            .AddChildContent<WorkspaceNavigationHost>(child => child
                .Add(p => p.OnCloseRequested, EventCallback.Factory.Create(this, () => { }))));

        await Assert.That(cut.Markup).IsEmpty();
    }

    [Test]
    public async Task Render_DockedMode_DoesNotShowOverlayHeader()
    {
        _ctx.SetAnonymousUser();
        var cut = _ctx.Render<WorkspaceNavigationHost>(parameters => parameters
            .Add(p => p.OnCloseRequested, EventCallback.Factory.Create(this, () => { })));

        await Assert.That(cut.Markup).DoesNotContain("workspace-nav-host__overlay-header");
    }

    [Test]
    public async Task Render_NoProvider_RendersNothing()
    {
        _ctx.Services.AddScoped<IWorkspaceRegistry>(_ => new TestNoProviderRegistry());
        _ctx.SetAnonymousUser();
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/no-nav");

        var cut = _ctx.Render<WorkspaceNavigationHost>();

        await Assert.That(cut.Markup).IsEmpty();
    }

    private static DockPanelEntry CreateOverlayDockPanelEntry()
    {
        var id = new DockPanelId("test-workspace-nav");
        return new DockPanelEntry(
            new DockPanelDescriptor(
                id,
                DockScope.Shell,
                DockSide.Start,
                DockMode.Temporary,
                "Navigation",
                "Workspace navigation",
                280, 240, 360, 0, false, true, true),
            _ => { },
            new DockPanelState(id, true, DockMode.Temporary, 280, 0, true));
    }

    private sealed class TestNoProviderRegistry : IWorkspaceRegistry
    {
        public IReadOnlyList<WorkspaceDescriptor> Workspaces { get; } =
        [
            new(WorkspaceKey.Events, "workspace.events", Icons.Material.Filled.Explore, "/", false, null, typeof(EventsWorkspaceNavigation)),
            new(new WorkspaceKey("no-nav"), "workspace.no-nav", Icons.Material.Filled.BugReport, "/no-nav", false, null, null)
        ];
    }
}

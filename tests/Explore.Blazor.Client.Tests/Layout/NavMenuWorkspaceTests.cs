using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Components.Shell;
using Explore.Blazor.Client.Layout;
using Explore.Blazor.Client.Services;
using Explore.Blazor.Client.Services.Shell;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Layout;

public sealed class NavMenuWorkspaceTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();

    public NavMenuWorkspaceTests()
    {
        _ctx.AddShellStateMocks();
        _ctx.SetAuthenticatedUser(Guid.CreateVersion7(), "Studio User");
        NavMenuTestServices.Register(
            _ctx,
            eventCreationEligibility: new EventCreationEligibility
            {
                CanCreate = true,
                IsUserSubmissionMode = true
            });
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task EventsWorkspace_SearchesPublicEventsAndKeepsAddEventAction()
    {
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/events");
        var cut = RenderNavMenu();

        var search = cut.Find(".navbar__search input");
        await search.ChangeAsync(new ChangeEventArgs { Value = "community iftar" });
        await search.KeyUpAsync(new KeyboardEventArgs { Key = "Enter" });

        await Assert.That(new Uri(navigation.Uri).PathAndQuery).IsEqualTo("/events?q=community%20iftar");
        await Assert.That(cut.Markup).Contains("Add Event");
    }

    [Test]
    public async Task StudioWorkspace_SearchesManagedEventsAndShowsCreateActorContext()
    {
        var actor = new ManagedActorDto
        {
            ActorId = Guid.CreateVersion7(),
            ActorType = "Group",
            DisplayName = "Youth Circle"
        };
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/studio");
        var shellState = _ctx.Services.GetRequiredService<UiShellState>();
        shellState.ReconcileActiveActors([actor], actor.ActorId);
        var cut = RenderNavMenu();

        var search = cut.Find(".navbar__search input");
        await search.ChangeAsync(new ChangeEventArgs { Value = "workshop" });
        await search.KeyUpAsync(new KeyboardEventArgs { Key = "Enter" });

        await Assert.That(new Uri(navigation.Uri).PathAndQuery).IsEqualTo("/studio/events?q=workshop");
        await Assert.That(cut.Find("[data-testid='navbar-primary-action']").TextContent).Contains("Create");
        await Assert.That(cut.Find("[data-testid='navbar-acting-actor']").TextContent).Contains("Youth Circle");
        await Assert.That(cut.Find("[data-testid='navbar-acting-actor'] bdi").GetAttribute("dir")).IsEqualTo("auto");
        await Assert.That(cut.Find("[data-testid='navbar-primary-action']").GetAttribute("href")).IsEqualTo("/events/create");
    }

    [Test]
    public async Task StudioWorkspace_BlankSearchNavigatesToManagedEventsList()
    {
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/studio");
        var cut = RenderNavMenu();

        await cut.Find(".navbar__search input").KeyUpAsync(new KeyboardEventArgs { Key = "Enter" });

        await Assert.That(new Uri(navigation.Uri).AbsolutePath).IsEqualTo("/studio/events");
    }

    [Test]
    public async Task SettingsWorkspace_HidesGlobalEventSearchAndEventAction()
    {
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/settings");
        var cut = RenderNavMenu();

        await Assert.That(cut.FindAll(".navbar__search")).IsEmpty();
        await Assert.That(cut.FindAll("[data-testid='navbar-primary-action']")).IsEmpty();
    }

    [Test]
    public async Task PersonalSettingsEntries_ProfileAndRailClicksPreserveSameEventsOrigin()
    {
        const string originRoute = "/events?q=iftar";
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(originRoute);
        var shellState = _ctx.Services.GetRequiredService<UiShellState>();
        var navMenu = RenderNavMenu();

        navMenu.Find(".navbar__user-btn").Click();
        navMenu.Find("a.navbar__dropdown-item[href='/settings/personal']").Click();

        await Assert.That(new Uri(navigation.Uri).AbsolutePath).IsEqualTo("/settings/personal");
        await Assert.That(shellState.ActiveWorkspace).IsEqualTo(WorkspaceKey.Events);
        await Assert.That(shellState.PersonalSettingsReturnRoute).IsEqualTo(originRoute);

        navigation.NavigateTo(originRoute);
        var rail = _ctx.Render<AppWorkspaceRail>();

        rail.Find("a.app-workspace-rail__link[href='/settings/personal']").Click();

        await Assert.That(new Uri(navigation.Uri).AbsolutePath).IsEqualTo("/settings/personal");
        await Assert.That(shellState.ActiveWorkspace).IsEqualTo(WorkspaceKey.Events);
        await Assert.That(shellState.PersonalSettingsReturnRoute).IsEqualTo(originRoute);
    }

    [Test]
    public async Task ThemeQuickSwitcher_AppearanceClicksPreserveEventsOrigin()
    {
        const string originRoute = "/events?q=theme";
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(originRoute);
        var shellState = _ctx.Services.GetRequiredService<UiShellState>();
        var quickSwitcher = RenderThemeQuickSwitcher();
        var childContent = quickSwitcher.FindComponent<MudMenu>().Instance.ChildContent
            ?? throw new InvalidOperationException("ThemeQuickSwitcher menu content not found");
        var menuContent = _ctx.Render(childContent);

        menuContent.Find("a[href='/settings/personal/appearance?custom=true']").Click();

        await Assert.That(new Uri(navigation.Uri).PathAndQuery)
            .IsEqualTo("/settings/personal/appearance?custom=true");
        await Assert.That(shellState.PersonalSettingsReturnRoute).IsEqualTo(originRoute);

        navigation.NavigateTo(originRoute);
        menuContent.Find("a[href='/settings/personal/appearance']").Click();

        await Assert.That(new Uri(navigation.Uri).PathAndQuery)
            .IsEqualTo("/settings/personal/appearance");
        await Assert.That(shellState.PersonalSettingsReturnRoute).IsEqualTo(originRoute);
    }

    [Test]
    public async Task AnonymousSettingsWorkspace_HidesSubmissionEventAction()
    {
        using var context = new BlazorTestContext();
        context.AddShellStateMocks();
        context.SetAnonymousUser();
        NavMenuTestServices.Register(
            context,
            publicExperienceSettings: new PublicExperienceSettingsBuilder()
                .WithUserSubmittedEvents()
                .Build());
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/settings");

        var cut = RenderNavMenu(context);

        await Assert.That(cut.Markup).DoesNotContain("Sign in to add an event");
        await Assert.That(cut.FindAll(".navbar__create-event-btn")).IsEmpty();
    }

    private IRenderedComponent<NavMenu> RenderNavMenu() => RenderNavMenu(_ctx);

    private IRenderedComponent<ThemeQuickSwitcher> RenderThemeQuickSwitcher() =>
        _ctx.RenderMudComponent<ThemeQuickSwitcher>();

    private static IRenderedComponent<NavMenu> RenderNavMenu(BlazorTestContext context) =>
        context.RenderMudComponent<NavMenu>();
}

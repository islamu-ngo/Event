using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Components.Shell.Workspaces;
using Explore.Blazor.Client.Contracts.Services.Shell;
using Explore.Blazor.Client.Services.Shell;

namespace Explore.Blazor.Client.Tests.Components.Shell;

public sealed class StudioActorSwitcherTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly IUiShellContextService _shellContextService = Substitute.For<IUiShellContextService>();

    public StudioActorSwitcherTests()
    {
        _ctx.Services.AddSingleton(_shellContextService);
        _ctx.Services.AddScoped<IWorkspaceRegistry, WorkspaceRegistry>();
        _ctx.Services.AddScoped<WorkspaceRouteClassifier>();
        _ctx.Services.AddScoped<UiShellState>();
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task SingleActorRendersReadOnlyIdentity()
    {
        var actor = CreateActor("Organization", "Community Events");
        SetContext([actor]);

        var cut = _ctx.RenderMudComponent<StudioActorSwitcher>();

        cut.WaitForElement("[data-testid='studio-actor-identity']");
        await Assert.That(cut.Markup).Contains("Community Events");
        await Assert.That(cut.FindAll("select")).IsEmpty();
    }

    [Test]
    public async Task PinnedActorRendersReadOnlyIdentity()
    {
        var firstActor = CreateActor("Organization", "Primary Organization");
        var secondActor = CreateActor("Group", "Volunteer Group");
        SetContext([firstActor, secondActor], firstActor.ActorId);

        var cut = _ctx.RenderMudComponent<StudioActorSwitcher>();

        cut.WaitForElement("[data-testid='studio-actor-identity']");
        await Assert.That(cut.Markup).Contains("Primary Organization");
        await Assert.That(cut.Markup).Contains("Pinned");
        await Assert.That(cut.FindAll("select")).IsEmpty();
    }

    [Test]
    public async Task MultipleUnpinnedActorsCanSwitchSessionActor()
    {
        var firstActor = CreateActor("Organization", "Organization");
        var secondActor = CreateActor("Group", "Group");
        SetContext([firstActor, secondActor]);

        var cut = _ctx.RenderMudComponent<StudioActorSwitcher>();
        var select = cut.WaitForElement("[data-testid='studio-actor-switcher']");

        await select.ChangeAsync(new ChangeEventArgs { Value = secondActor.ActorId!.Value.ToString() });

        var state = _ctx.Services.GetRequiredService<UiShellState>();
        await Assert.That(state.ActiveActorId).IsEqualTo(secondActor.ActorId);
        await Assert.That(select.GetAttribute("aria-label")).IsNull();
        await Assert.That(cut.Markup).Contains("for=\"studio-actor-switcher\"");
    }

    private void SetContext(IReadOnlyList<ManagedActorDto> actors, Guid? pinnedActorId = null)
    {
        _shellContextService.GetCachedContextAsync(Arg.Any<CancellationToken>())
            .Returns(new UiShellContextDto
            {
                ManagedActors = actors.ToList(),
                PinnedActorId = pinnedActorId
            });
    }

    private static ManagedActorDto CreateActor(string type, string name) => new()
    {
        ActorId = Guid.CreateVersion7(),
        ActorType = type,
        DisplayName = name
    };
}

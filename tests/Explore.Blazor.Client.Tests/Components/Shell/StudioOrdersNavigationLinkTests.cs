using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Components.Shell.Workspaces;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Services.Shell;

namespace Explore.Blazor.Client.Tests.Components.Shell;

public sealed class StudioOrdersNavigationLinkTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly IStudioContextService _studioContextService;

    public StudioOrdersNavigationLinkTests()
    {
        _studioContextService = _ctx.AddMockService<IStudioContextService>();
        _ctx.Services.AddScoped<IWorkspaceRegistry, WorkspaceRegistry>();
        _ctx.Services.AddScoped<WorkspaceRouteClassifier>();
        _ctx.Services.AddScoped<UiShellState>();
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Render_UsesStudioContextOrderRelationAsSoleGate(bool hasRelation)
    {
        var context = new HalResourceOfStudioContextDto
        {
            _links = hasRelation
                ? new Dictionary<string, HalLink>
                {
                    ["view-registration-orders"] = new() { Href = "/api/studio/registration-orders", Method = "GET" }
                }
                : new Dictionary<string, HalLink>()
        };
        _studioContextService.GetContextAsync(null, Arg.Any<CancellationToken>()).Returns(context);

        var cut = _ctx.RenderMudComponent<StudioOrdersNavigationLink>();
        var expectedCount = hasRelation ? 1 : 0;

        cut.WaitForState(() => cut.FindAll("[data-testid='studio-orders-navigation-link']").Count == expectedCount);
        await Assert.That(cut.FindAll("[data-testid='studio-orders-navigation-link']").Count).IsEqualTo(expectedCount);
    }
}

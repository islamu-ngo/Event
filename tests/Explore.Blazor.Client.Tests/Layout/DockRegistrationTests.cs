using Explore.Blazor.Client.Extensions;
using Explore.Blazor.Client.Services.Docking;

namespace Explore.Blazor.Client.Tests.Layout;

public sealed class DockRegistrationTests
{
    [Test]
    public async Task AddSharedApplicationServices_RegistersDockLayoutStateAsScopedRegistry()
    {
        var services = new ServiceCollection();

        services.AddSharedApplicationServices();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dockState = scope.ServiceProvider.GetRequiredService<DockLayoutState>();
        var registry = scope.ServiceProvider.GetRequiredService<IDockPanelRegistry>();

        await Assert.That(ReferenceEquals(dockState, registry)).IsTrue();
    }
}

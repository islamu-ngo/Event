using Explore.Blazor.Client.Components;

namespace Explore.Blazor.Client.Tests.Components;

public sealed class InteractiveReadinessMarkerTests
{
    [Test]
    public async Task Render_AfterInteractiveLifecycle_ExposesReadyState()
    {
        using var context = new BlazorTestContext();

        var component = context.Render<InteractiveReadinessMarker>();

        await Assert.That(
                component.Find("[data-blazor-interactive]").GetAttribute("data-blazor-interactive"))
            .IsEqualTo("true");
    }
}

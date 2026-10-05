using System.Reflection;

namespace Explore.Blazor.Client.Tests.Security;

public sealed class HomeDiscoveryPrivacyTests
{
    [Test]
    public async Task GeneratedCompositeClientAcceptsOnlyCoarseAreaAndModeContext()
    {
        var method = typeof(IPublicExperienceClient).GetMethod("GetHomeDiscoveryAsync")!;
        var parameterNames = method.GetParameters().Select(parameter => parameter.Name).ToArray();

        await Assert.That(parameterNames).Contains("areaId");
        await Assert.That(parameterNames).Contains("mode");
        await Assert.That(parameterNames).DoesNotContain("latitude");
        await Assert.That(parameterNames).DoesNotContain("longitude");
        await Assert.That(parameterNames).DoesNotContain("origin");
    }

    [Test]
    public async Task PersistentHomeStateContainsCompositePayloadButNoBrowserPosition()
    {
        var componentType = typeof(Explore.Blazor.Client.Components.Discovery.HomeDiscoveryExperience);
        var persistentProperties = componentType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(property => property.GetCustomAttribute<PersistentStateAttribute>() is not null)
            .ToArray();

        await Assert.That(persistentProperties.Length).IsEqualTo(1);
        await Assert.That(persistentProperties[0].PropertyType).IsEqualTo(typeof(HomeDiscoveryDto));
        await Assert.That(persistentProperties.Select(property => property.PropertyType))
            .DoesNotContain(typeof(Explore.Blazor.Client.Contracts.Interop.HomeDiscoveryGeolocationResult));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PersistedCompositePayloadNeverReplacesCurrentDisclosure(bool unavailable)
    {
        var persisted = new HomeDiscoveryDto
        {
            Context = new HomeDiscoveryContextDto
            {
                Mode = HomeDiscoveryMode.Area,
                SelectedAreaDisplayName = "Previously public location"
            }
        };
        HomeDiscoveryDto? current = unavailable ? null : new HomeDiscoveryDto
        {
            Context = new HomeDiscoveryContextDto
            {
                Mode = HomeDiscoveryMode.Area,
                SelectedAreaDisplayName = "Current public location"
            }
        };
        using var context = new BlazorTestContext();
        var discoveryService = Substitute.For<IHomeDiscoveryService>();
        discoveryService.LoadAsync(null, null, Arg.Any<CancellationToken>()).Returns(Task.FromResult(current));
        context.Services.AddSingleton(discoveryService);
        context.Services.AddSingleton(Substitute.For<Explore.Blazor.Client.Contracts.Interop.IHomeDiscoveryGeolocation>());
        context.ComponentFactories.Add(new PersistedDiscoveryFactory(persisted));

        var cut = context.RenderMudComponent<Explore.Blazor.Client.Components.Discovery.HomeDiscoveryExperience>();

        await Assert.That(cut.Markup).DoesNotContain("Previously public location");
        if (unavailable)
            await Assert.That(cut.FindAll(".home-discovery__failure").Count).IsEqualTo(1);
        else
            await Assert.That(cut.Find("[data-testid='home-discovery-context-trigger']").TextContent)
                .Contains("Current public location");
    }

    [Test]
    public async Task PersistedCompositePayloadIsIgnoredWhenUrlModeDiffers()
    {
        var persisted = new HomeDiscoveryDto
        {
            Context = new HomeDiscoveryContextDto { Mode = HomeDiscoveryMode.All }
        };
        var expected = new HomeDiscoveryDto
        {
            Context = new HomeDiscoveryContextDto { Mode = HomeDiscoveryMode.Online }
        };
        var discoveryService = Substitute.For<IHomeDiscoveryService>();
        discoveryService.LoadAsync(null, "online", Arg.Any<CancellationToken>()).Returns(expected);
        using var context = new BlazorTestContext();
        context.Services.AddSingleton(discoveryService);
        context.Services.AddSingleton(Substitute.For<Explore.Blazor.Client.Contracts.Interop.IHomeDiscoveryGeolocation>());
        context.ComponentFactories.Add(new PersistedDiscoveryFactory(persisted));
        var cut = context.RenderMudComponent<Explore.Blazor.Client.Components.Discovery.HomeDiscoveryExperience>(
            p => p.Add(x => x.UrlMode, "online"));
        cut.Find("[data-testid='home-discovery-context-trigger']").Click();
        await Assert.That(cut.FindAll(".home-discovery__context-option[aria-current='true']").Count).IsEqualTo(1);
    }

    private sealed class PersistedDiscoveryFactory(HomeDiscoveryDto persisted) : Bunit.IComponentFactory
    {
        public bool CanCreate(Type componentType) =>
            componentType == typeof(Explore.Blazor.Client.Components.Discovery.HomeDiscoveryExperience);

        public Microsoft.AspNetCore.Components.IComponent Create(Type componentType) =>
            new Explore.Blazor.Client.Components.Discovery.HomeDiscoveryExperience { PersistedDiscovery = persisted };
    }
}

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
    public async Task PersistedCompositePayloadRestoresBeforeAnyServiceAccess()
    {
        var expected = new HomeDiscoveryDto
        {
            Context = new HomeDiscoveryContextDto
            {
                Mode = HomeDiscoveryMode.Area,
                SelectedAreaDisplayName = "Brussels"
            }
        };
        var component = new Explore.Blazor.Client.Components.Discovery.HomeDiscoveryExperience
        {
            PersistedDiscovery = expected
        };
        var translation = Substitute.For<ITranslationService>();
        translation.T(Arg.Any<string>(), Arg.Any<string?>())
            .Returns(call => call.ArgAt<string?>(1) ?? call.ArgAt<string>(0));
        component.GetType().GetProperty(
                "Translation",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(component, translation);
        var lifecycleMethod = component.GetType().GetMethod(
            "OnParametersSetAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        await (Task)lifecycleMethod.Invoke(component, null)!;

        var restoredField = component.GetType().GetField(
            "_discovery",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        await Assert.That(restoredField.GetValue(component)).IsSameReferenceAs(expected);
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
        var component = new Explore.Blazor.Client.Components.Discovery.HomeDiscoveryExperience
        {
            PersistedDiscovery = persisted,
            UrlMode = "online"
        };
        var translation = Substitute.For<ITranslationService>();
        translation.T(Arg.Any<string>(), Arg.Any<string?>())
            .Returns(call => call.ArgAt<string?>(1) ?? call.ArgAt<string>(0));
        component.GetType().GetProperty(
                "Translation",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(component, translation);
        component.GetType().GetProperty(
                "HomeDiscoveryService",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(component, discoveryService);
        var lifecycleMethod = component.GetType().GetMethod(
            "OnParametersSetAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        await (Task)lifecycleMethod.Invoke(component, null)!;

        var restoredField = component.GetType().GetField(
            "_discovery",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        await Assert.That(restoredField.GetValue(component)).IsSameReferenceAs(expected);
        await discoveryService.Received(1).LoadAsync(null, "online", Arg.Any<CancellationToken>());
    }

}

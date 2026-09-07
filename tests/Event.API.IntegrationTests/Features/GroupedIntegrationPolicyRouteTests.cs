using System.Reflection;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Microsoft.AspNetCore.Mvc;

namespace Event.Api.IntegrationTests.Features;

public sealed class GroupedIntegrationPolicyRouteTests
{
    [Test]
    [Arguments(typeof(ListmonkIntegrationSettingsController), nameof(ListmonkIntegrationSettingsController.UpdateSettings), "settings", RouteNames.UpdateListmonkIntegrationSettings)]
    [Arguments(typeof(LocalizationAdminController), nameof(LocalizationAdminController.UpdateGovernance), "governance", RouteNames.UpdateLocalizationGovernance)]
    [Arguments(typeof(ExternalApiKeyController), nameof(ExternalApiKeyController.Update), "{id}", RouteNames.UpdateExternalApiKey)]
    public async Task UpdateRoute_UsesStablePatchMetadata(Type controllerType, string methodName, string template, string routeName)
    {
        var action = controllerType.GetMethod(methodName)!;
        var patch = action.GetCustomAttribute<HttpPatchAttribute>();

        await Assert.That(patch).IsNotNull();
        await Assert.That(patch!.Template).IsEqualTo(template);
        await Assert.That(patch.Name).IsEqualTo(routeName);
        await Assert.That(action.GetCustomAttribute<HttpPutAttribute>()).IsNull();
    }
}

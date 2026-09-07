using System.Reflection;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;

namespace Event.Api.IntegrationTests.Features;

public sealed class AnalyticsRelayControllerTests
{
    [Test]
    public async Task RelayRoute_UsesStableAnonymousPublicIngestionMetadata()
    {
        var controllerType = typeof(AnalyticsRelayController);
        var action = controllerType.GetMethod(nameof(AnalyticsRelayController.Relay))!;
        var responseStatuses = action
            .GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute => attribute.StatusCode)
            .ToList();

        await Assert.That(controllerType.GetCustomAttribute<EndpointClassificationAttribute>()?.Class)
            .IsEqualTo(EndpointClass.Public);
        await Assert.That(controllerType.GetCustomAttribute<RouteAttribute>()?.Template)
            .IsEqualTo("api/a/t");
        await AssertRoute(action, typeof(HttpPostAttribute), null, RouteNames.RelayAnalyticsEvent);
        await Assert.That(action.GetCustomAttribute<AllowAnonymousAttribute>()).IsNotNull();
        await Assert.That(action.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName)
            .IsEqualTo(RateLimitingExtensions.AnalyticsRelayPolicy);
        await Assert.That(responseStatuses).Contains(StatusCodes.Status202Accepted);
        await Assert.That(responseStatuses).Contains(StatusCodes.Status400BadRequest);
    }

    private static async Task AssertRoute(MethodInfo method, Type attributeType, string? template, string routeName)
    {
        var attribute = method.GetCustomAttributes().Single(value => value.GetType() == attributeType) as HttpMethodAttribute;

        await Assert.That(attribute).IsNotNull();
        await Assert.That(attribute!.Template).IsEqualTo(template);
        await Assert.That(attribute.Name).IsEqualTo(routeName);
    }
}

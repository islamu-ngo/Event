using System.Reflection;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.Application.Authorization;
using Explore.Application.Features.EventAspects.Requests.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.OutputCaching;

namespace Event.Api.IntegrationTests.Features;

public class EventAspectControllerTests
{
    [Test]
    public async Task ControllerRoute_PreservesExistingEventAspectBaseRoute()
    {
        var route = typeof(EventAspectController).GetCustomAttribute<RouteAttribute>()!;
        var tags = typeof(EventAspectController).GetCustomAttribute<TagsAttribute>()!;

        await Assert.That(route.Template).IsEqualTo("api/event");
        await Assert.That(tags.Tags).Contains("Event");
    }

    [Test]
    public async Task ReadRoutes_PreservePublicCachedAspectContracts()
    {
        await AssertReadRoute(
            nameof(EventAspectController.GetIslamicAspect),
            "{id:guid}/aspects/islamic",
            RouteNames.GetEventIslamicAspect);

        await AssertReadRoute(
            nameof(EventAspectController.GetTechAspect),
            "{id:guid}/aspects/tech",
            RouteNames.GetEventTechAspect);
    }

    [Test]
    public async Task ManagedReadRoutes_UseAuthenticatedViewManagementContracts()
    {
        await AssertManagedReadRoute(
            nameof(EventAspectController.GetManagedIslamicAspect),
            "{id:guid}/management-aspects/islamic",
            RouteNames.GetManagedEventIslamicAspect,
            typeof(GetManagedEventIslamicAspectRequest));
        await AssertManagedReadRoute(
            nameof(EventAspectController.GetManagedTechAspect),
            "{id:guid}/management-aspects/tech",
            RouteNames.GetManagedEventTechAspect,
            typeof(GetManagedEventTechAspectRequest));
    }

    [Test]
    public async Task CreateAndUpdateRoutes_UseAuthenticatedAspectContracts()
    {
        await AssertWriteRoute<HttpPostAttribute>(
            nameof(EventAspectController.CreateIslamicAspect),
            "{id:guid}/aspects/islamic",
            RouteNames.CreateEventIslamicAspect);

        await AssertWriteRoute<HttpPatchAttribute>(
            nameof(EventAspectController.UpdateIslamicAspect),
            "{id:guid}/aspects/islamic",
            RouteNames.UpdateEventIslamicAspect);

        await AssertWriteRoute<HttpPostAttribute>(
            nameof(EventAspectController.CreateTechAspect),
            "{id:guid}/aspects/tech",
            RouteNames.CreateEventTechAspect);

        await AssertWriteRoute<HttpPatchAttribute>(
            nameof(EventAspectController.UpdateTechAspect),
            "{id:guid}/aspects/tech",
            RouteNames.UpdateEventTechAspect);
    }

    [Test]
    public async Task DeleteRoutes_PreserveAuthenticatedAspectContracts()
    {
        await AssertDeleteRoute(
            nameof(EventAspectController.DeleteIslamicAspect),
            "{id:guid}/aspects/islamic",
            RouteNames.DeleteEventIslamicAspect);

        await AssertDeleteRoute(
            nameof(EventAspectController.DeleteTechAspect),
            "{id:guid}/aspects/tech",
            RouteNames.DeleteEventTechAspect);
    }

    [Test]
    public async Task EventController_NoLongerOwnsAspectActions()
    {
        await Assert.That(typeof(EventController).GetMethod("GetIslamicAspect")).IsNull();
        await Assert.That(typeof(EventController).GetMethod("CreateIslamicAspect")).IsNull();
        await Assert.That(typeof(EventController).GetMethod("UpdateIslamicAspect")).IsNull();
        await Assert.That(typeof(EventController).GetMethod("DeleteIslamicAspect")).IsNull();
        await Assert.That(typeof(EventController).GetMethod("GetTechAspect")).IsNull();
        await Assert.That(typeof(EventController).GetMethod("CreateTechAspect")).IsNull();
        await Assert.That(typeof(EventController).GetMethod("UpdateTechAspect")).IsNull();
        await Assert.That(typeof(EventController).GetMethod("DeleteTechAspect")).IsNull();
    }

    private static async Task AssertReadRoute(string actionName, string template, string routeName)
    {
        var action = typeof(EventAspectController).GetMethod(actionName)!;
        var route = action.GetCustomAttribute<HttpGetAttribute>()!;
        var classification = action.GetCustomAttribute<EndpointClassificationAttribute>()!;

        await Assert.That(route.Template).IsEqualTo(template);
        await Assert.That(route.Name).IsEqualTo(routeName);
        await Assert.That(classification.Class).IsEqualTo(EndpointClass.Public);
        await Assert.That(action.GetCustomAttribute<AllowAnonymousAttribute>()).IsNotNull();
        await Assert.That(action.GetCustomAttribute<AuthorizeAttribute>()).IsNull();
        await Assert.That(action.GetCustomAttribute<OutputCacheAttribute>()?.PolicyName).IsEqualTo("DetailData");
        await AssertProducesProblem(action, StatusCodes.Status404NotFound);
    }

    private static async Task AssertManagedReadRoute(
        string actionName,
        string template,
        string routeName,
        Type requestType)
    {
        var action = typeof(EventAspectController).GetMethod(actionName)!;
        var route = action.GetCustomAttribute<HttpGetAttribute>()!;
        var authorization = requestType.GetCustomAttribute<AuthorizeResourceAttribute>()!;

        await Assert.That(route.Template).IsEqualTo(template);
        await Assert.That(route.Name).IsEqualTo(routeName);
        await Assert.That(action.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(action.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
        await Assert.That(action.GetCustomAttribute<OutputCacheAttribute>()).IsNull();
        await Assert.That(authorization.Resource).IsEqualTo(ResourceKinds.Event);
        await Assert.That(authorization.Action).IsEqualTo(AuthorizationActions.Events.ViewManagement);
    }

    private static async Task AssertWriteRoute<TAttribute>(string actionName, string template, string routeName)
        where TAttribute : HttpMethodAttribute
    {
        var action = typeof(EventAspectController).GetMethod(actionName)!;
        var route = action.GetCustomAttribute<TAttribute>()!;
        var classification = action.GetCustomAttribute<EndpointClassificationAttribute>()!;

        await Assert.That(route.Template).IsEqualTo(template);
        await Assert.That(route.Name).IsEqualTo(routeName);
        await Assert.That(classification.Class).IsEqualTo(EndpointClass.Authenticated);
        await Assert.That(action.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(action.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
        await Assert.That(action.GetCustomAttribute<OutputCacheAttribute>()).IsNull();
        await Assert.That(action.GetCustomAttribute<ConsumesAttribute>()?.ContentTypes.Contains("application/json")).IsTrue();
        await AssertProducesProblem(action, StatusCodes.Status400BadRequest, typeof(ValidationProblemDetails));
        await AssertProducesProblem(action, StatusCodes.Status401Unauthorized);
        await AssertProducesProblem(action, StatusCodes.Status403Forbidden);
        await AssertProducesProblem(action, StatusCodes.Status404NotFound);
    }

    private static async Task AssertDeleteRoute(string actionName, string template, string routeName)
    {
        var action = typeof(EventAspectController).GetMethod(actionName)!;
        var route = action.GetCustomAttribute<HttpDeleteAttribute>()!;
        var classification = action.GetCustomAttribute<EndpointClassificationAttribute>()!;

        await Assert.That(route.Template).IsEqualTo(template);
        await Assert.That(route.Name).IsEqualTo(routeName);
        await Assert.That(classification.Class).IsEqualTo(EndpointClass.Authenticated);
        await Assert.That(action.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(action.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
        await Assert.That(action.GetCustomAttribute<OutputCacheAttribute>()).IsNull();
        await Assert.That(action.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Any(attribute => attribute.StatusCode == StatusCodes.Status204NoContent)).IsTrue();
        await AssertProducesProblem(action, StatusCodes.Status401Unauthorized);
        await AssertProducesProblem(action, StatusCodes.Status404NotFound);
    }

    private static async Task AssertProducesProblem(
        MethodInfo action,
        int statusCode,
        Type? problemType = null)
    {
        problemType ??= typeof(ProblemDetails);

        await Assert.That(action.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Any(attribute => attribute.StatusCode == statusCode && attribute.Type == problemType)).IsTrue();
    }
}

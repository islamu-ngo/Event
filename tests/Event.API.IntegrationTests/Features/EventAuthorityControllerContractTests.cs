using System.Reflection;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Event.Api.IntegrationTests.Features;

public sealed class EventAuthorityControllerContractTests
{
    [Test]
    public async Task PublicActionRedirect_UsesStoredIdentifiersOnly()
    {
        var action = typeof(EventPublicActionController).GetMethod(
            nameof(EventPublicActionController.RedirectToAction),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            binder: null,
            [typeof(Guid), typeof(Guid), typeof(string), typeof(CancellationToken)],
            modifiers: null)!;
        var route = action.GetCustomAttribute<HttpGetAttribute>()!;
        var parameters = action.GetParameters();

        await Assert.That(route.Template).IsEqualTo("{actionId:guid}/redirect");
        await Assert.That(route.Name).IsEqualTo(RouteNames.RedirectEventPublicAction);
        await Assert.That(parameters.Select(parameter => parameter.Name))
            .IsEquivalentTo(["eventId", "actionId", "surface", "cancellationToken"]);
        await Assert.That(parameters.Any(parameter =>
            parameter.Name?.Contains("url", StringComparison.OrdinalIgnoreCase) == true
            || parameter.Name?.Contains("return", StringComparison.OrdinalIgnoreCase) == true)).IsFalse();
        await Assert.That(action.GetCustomAttribute<AllowAnonymousAttribute>()).IsNotNull();
        await Assert.That(action.GetCustomAttribute<EndpointClassificationAttribute>()!.Class)
            .IsEqualTo(EndpointClass.Public);
    }

    [Test]
    public async Task OrganizerClaimReads_AreAuthenticatedAndNoStore()
    {
        var readActions = new[]
        {
            nameof(EventOrganizerClaimController.GetAll),
            nameof(EventOrganizerClaimController.GetById),
            nameof(EventOrganizerClaimController.GetByClaimant)
        };

        foreach (var actionName in readActions)
        {
            var action = typeof(EventOrganizerClaimController).GetMethod(actionName)!;
            var responseCache = action.GetCustomAttribute<ResponseCacheAttribute>()!;

            await Assert.That(action.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
            await Assert.That(action.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
            await Assert.That(action.GetCustomAttribute<EndpointClassificationAttribute>()!.Class)
                .IsEqualTo(EndpointClass.Authenticated);
            await Assert.That(responseCache.NoStore).IsTrue();
            await Assert.That(responseCache.Location).IsEqualTo(ResponseCacheLocation.None);
        }
    }
}

// ABOUTME: API contract coverage for the anonymous composite public-home discovery endpoint.
// ABOUTME: Verifies stable routing, cache metadata, area/mode dispatch, and response typing.

using System.Reflection;
using System.Text.Json;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.PublicExperience;
using Explore.Application.Features.PublicExperience.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Models.PublicExperience;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.OutputCaching;
using NSubstitute;

namespace Explore.Api.IntegrationTests.Features;

public sealed class PublicExperienceHomeDiscoveryControllerTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly ILinkPolicy<EventDiscoveryItemDto> _linkPolicy =
        Substitute.For<ILinkPolicy<EventDiscoveryItemDto>>();
    private readonly IHateoasLinkGenerator _linkGenerator =
        Substitute.For<IHateoasLinkGenerator>();

    [Test]
    public async Task HomeDiscoveryRouteHasStableAnonymousCachedMetadata()
    {
        var action = typeof(PublicExperienceController)
            .GetMethod(nameof(PublicExperienceController.GetHomeDiscovery))!;
        var route = action.GetCustomAttribute<HttpGetAttribute>();

        await Assert.That(route).IsNotNull();
        await Assert.That(route!.Template).IsEqualTo("~/api/public-experience/home");
        await Assert.That(route.Name).IsEqualTo(RouteNames.GetHomeDiscovery);
        await Assert.That(action.GetCustomAttribute<AllowAnonymousAttribute>()).IsNotNull();
        await Assert.That(action.GetCustomAttribute<EndpointClassificationAttribute>()?.Class).IsEqualTo(EndpointClass.Public);
        await Assert.That(action.GetCustomAttribute<OutputCacheAttribute>()?.PolicyName).IsEqualTo("PublicHomeDiscovery");
    }

    [Test]
    public async Task HomeDiscoveryDispatchesAreaAndModeQuery()
    {
        var areaId = Guid.NewGuid();
        var expected = new HomeDiscoveryDto
        {
            Context = new HomeDiscoveryContextDto
            {
                Mode = HomeDiscoveryMode.Online,
                SelectedAreaId = areaId
            }
        };
        _mediator.Send(Arg.Any<GetHomeDiscoveryQuery>(), Arg.Any<CancellationToken>())
            .Returns(expected);
        var controller = CreateController();

        var action = await controller.GetHomeDiscovery(areaId, "online", CancellationToken.None);

        var ok = action.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        await Assert.That(ok!.Value).IsSameReferenceAs(expected);
        await _mediator.Received(1).Send(
            Arg.Is<GetHomeDiscoveryQuery>(query =>
                query != null && query.AreaId == areaId && query.Mode == "online"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HomeDiscoveryAddsSourceRelationToNestedFederatedItems()
    {
        var item = new EventDiscoveryItemDto
        {
            Source = "atproto",
            Federation = new EventFederationMetadataDto
            {
                AtprotoRecordId = Guid.NewGuid(),
                HasSourceLink = true
            }
        };
        var expected = new HomeDiscoveryDto { UpcomingInArea = [item] };
        _mediator.Send(Arg.Any<GetHomeDiscoveryQuery>(), Arg.Any<CancellationToken>())
            .Returns(expected);
        var definition = new LinkDefinition(
            "source",
            RouteNames.GetAtprotoEventSource,
            new { atprotoRecordId = item.Federation.AtprotoRecordId },
            "GET");
        _linkPolicy.GetLinks(item, Arg.Any<System.Security.Claims.ClaimsPrincipal?>())
            .Returns([definition]);
        _linkGenerator.GenerateLink(definition, Arg.Any<HttpContext>())
            .Returns(new HalLink
            {
                Href = $"/api/event-discovery/{item.Federation.AtprotoRecordId}/source",
                Method = "GET"
            });
        var controller = CreateController();

        var action = await controller.GetHomeDiscovery(cancellationToken: CancellationToken.None);
        var response = action.Result as OkObjectResult;
        await Assert.That(response).IsNotNull();
        var body = JsonSerializer.SerializeToElement((HomeDiscoveryDto)response!.Value!, JsonSerializerOptions.Web);
        var source = body.GetProperty("upcomingInArea")[0].GetProperty("_links").GetProperty("source");
        await Assert.That(source.GetProperty("href").GetString())
            .IsEqualTo($"/api/event-discovery/{item.Federation.AtprotoRecordId}/source");
        await Assert.That(source.GetProperty("method").GetString()).IsEqualTo("GET");
    }

    private PublicExperienceController CreateController() =>
        new(_mediator, _linkPolicy, _linkGenerator)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
}

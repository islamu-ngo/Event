using System.Reflection;
using System.Text.Json;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.PublicExperience;
using Explore.Application.Features.PublicExperience.Requests.Queries;
using Explore.Application.Features.Events.Discovery;
using Explore.Application.Hateoas;
using Explore.Application.Models.PublicExperience;
using Explore.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.OutputCaching;
using NSubstitute;

namespace Explore.Api.IntegrationTests.Features;

public sealed class PublicExperienceHomeDiscoveryControllerTests
{
    private readonly IQueryHandler<GetPublicExperienceSettingsQuery, PublicExperienceSettingsDto> _settingsHandler =
        Substitute.For<IQueryHandler<GetPublicExperienceSettingsQuery, PublicExperienceSettingsDto>>();
    private readonly IQueryHandler<GetPublicExperienceShellQuery, PublicExperienceShellDto> _shellHandler =
        Substitute.For<IQueryHandler<GetPublicExperienceShellQuery, PublicExperienceShellDto>>();
    private readonly IQueryHandler<GetHomeDiscoveryQuery, HomeDiscoveryDto> _homeDiscoveryHandler =
        Substitute.For<IQueryHandler<GetHomeDiscoveryQuery, HomeDiscoveryDto>>();
    private readonly ILinkPolicy<EventDiscoveryItemDto> _linkPolicy =
        Substitute.For<ILinkPolicy<EventDiscoveryItemDto>>();
    private readonly IHateoasLinkGenerator _linkGenerator =
        Substitute.For<IHateoasLinkGenerator>();

    [Test]
    public async Task HomeDiscoveryRouteHasStableAnonymousNoStoreMetadata()
    {
        var action = typeof(PublicExperienceController)
            .GetMethod(nameof(PublicExperienceController.GetHomeDiscovery))!;
        var route = action.GetCustomAttribute<HttpGetAttribute>();

        await Assert.That(route).IsNotNull();
        await Assert.That(route!.Template).IsEqualTo("~/api/public-experience/home");
        await Assert.That(route.Name).IsEqualTo(RouteNames.GetHomeDiscovery);
        await Assert.That(action.GetCustomAttribute<AllowAnonymousAttribute>()).IsNotNull();
        await Assert.That(action.GetCustomAttribute<EndpointClassificationAttribute>()?.Class).IsEqualTo(EndpointClass.Public);
        await Assert.That(action.GetCustomAttribute<OutputCacheAttribute>()).IsNull();
        await Assert.That(action.GetCustomAttribute<PrivateNoStoreAttribute>()).IsNotNull();
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
        GetHomeDiscoveryQuery? dispatched = null;
        _homeDiscoveryHandler.QueryAsync(Arg.Any<GetHomeDiscoveryQuery>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                dispatched = call.Arg<GetHomeDiscoveryQuery>();
                return expected;
            });
        var controller = CreateController();

        var action = await controller.GetHomeDiscovery(areaId, "online", CancellationToken.None);

        var ok = action.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        await Assert.That(ok!.Value).IsSameReferenceAs(expected);
        await Assert.That(dispatched).IsNotNull();
        await Assert.That(dispatched!.AreaId).IsEqualTo(areaId);
        await Assert.That(dispatched.Mode).IsEqualTo("online");
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
        _homeDiscoveryHandler.QueryAsync(Arg.Any<GetHomeDiscoveryQuery>(), Arg.Any<CancellationToken>())
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

    private PublicExperienceController CreateController()
    {
        var tenant = Substitute.For<ITenantContext>();
        Guid tenantId = Guid.CreateVersion7();
        tenant.TenantId.Returns(tenantId);
        var revision = new EventDiscoveryRevision { Id = Guid.CreateVersion7(), TenantId = tenantId };
        var identities = Substitute.For<IEventDiscoveryIdentityRepository>();
        identities.GetRevisionAsync(tenantId, Arg.Any<CancellationToken>()).Returns(revision);
        var disclosure = Substitute.For<IEventDiscoveryDisclosureRepository>();
        disclosure.AcquireCurrentAsync(tenantId, Arg.Any<CancellationToken>()).Returns(revision);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.ExecuteReadCommittedAsync(
                Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<bool>>>()(call.Arg<CancellationToken>()));
        return new(_settingsHandler, _shellHandler, _homeDiscoveryHandler, _linkPolicy, _linkGenerator,
            new EventDiscoveryResponseAuthority(unitOfWork, disclosure, identities, tenant, TimeProvider.System,
                Substitute.For<IEventDiscoveryResponseBoundaryRepository>()))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }
}

using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.API.Models;
using Explore.API.Services;
using Explore.API.Services.Calendar;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.PublicExperience;
using Explore.Application.Features.Federation.Atproto.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Notifications;
using Explore.Application.Notifications.Handlers;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed class AtprotoEventDiscoveryApiTests
{
    [Test]
    public async Task EventListUsesSourceAwareDiscoveryContract()
    {
        var mediator = Substitute.For<IMediator>();
        var assembler = Substitute.For<IResourceAssembler<EventDiscoveryItemDto>>();
        var page = PaginatedResult<EventDiscoveryItemDto>.Create(
            [new EventDiscoveryItemDto { Source = "atproto", FederatedEvent = Federated() }],
            1,
            1,
            20);
        var hal = new HalCollectionResource<EventDiscoveryItemDto>();
        mediator.Send(Arg.Any<GetPublicEventDiscoveryRequest>(), Arg.Any<CancellationToken>()).Returns(page);
        assembler.ToCollectionResource(
                page,
                RouteNames.GetEvents,
                Arg.Any<object?>(),
                Arg.Any<HttpContext>())
            .Returns(hal);
        EventController controller = Controller(mediator, assembler);

        ActionResult<HalCollectionResource<EventDiscoveryItemDto>> result = await controller.GetAll(
            new EventFilterRequest(),
            CancellationToken.None);

        var ok = result.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        await Assert.That(ok!.Value).IsSameReferenceAs(hal);
        await mediator.Received(1).Send(
            Arg.Is<GetPublicEventDiscoveryRequest>(request =>
                request != null
                && request.Criteria.PageNumber == 1
                && request.Criteria.PageSize == 20),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task FederatedSourceRedirectUsesOnlyResolvedInternalQueryTarget()
    {
        Guid recordId = Guid.CreateVersion7();
        var mediator = Substitute.For<IMediator>();
        mediator.Send(
                Arg.Is<GetAtprotoEventSourceQuery>(query => query != null && query.AtprotoRecordId == recordId),
                Arg.Any<CancellationToken>())
            .Returns("https://events.example/source");
        EventController controller = Controller(mediator, Substitute.For<IResourceAssembler<EventDiscoveryItemDto>>());

        IActionResult result = await controller.GetAtprotoEventSource(recordId, CancellationToken.None);

        await Assert.That(result).IsTypeOf<RedirectResult>();
        await Assert.That(((RedirectResult)result).Url).IsEqualTo("https://events.example/source");
    }

    [Test]
    public async Task MissingOrDisabledFederatedSourceReturnsGenericNotFound()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetAtprotoEventSourceQuery>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
        EventController controller = Controller(mediator, Substitute.For<IResourceAssembler<EventDiscoveryItemDto>>());

        IActionResult result = await controller.GetAtprotoEventSource(Guid.CreateVersion7(), CancellationToken.None);

        await Assert.That(result).IsTypeOf<ObjectResult>();
        await Assert.That(((ObjectResult)result).StatusCode).IsEqualTo(404);
    }

    [Test]
    public async Task FederatedHalExposesSourceOnlyWhenGovernedSourceExists()
    {
        var localPolicy = Substitute.For<ICollectionLinkPolicy<EventListDto>>();
        var policy = new EventDiscoveryLinkPolicy(localPolicy);
        Guid recordId = Guid.CreateVersion7();
        var item = new EventDiscoveryItemDto
        {
            Source = "atproto",
            FederatedEvent = Federated(),
            Federation = new EventFederationMetadataDto
            {
                AtprotoRecordId = recordId,
                HasSourceLink = true,
                Provenance = "atproto"
            }
        };

        LinkDefinition[] links = policy.GetItemLinks(item, null).ToArray();

        await Assert.That(links).HasSingleItem();
        LinkDefinition link = links.Single();
        await Assert.That(link.Rel).IsEqualTo("source");
        await Assert.That(link.RouteName).IsEqualTo(RouteNames.GetAtprotoEventSource);
        await Assert.That(link.Method).IsEqualTo("GET");
        localPolicy.DidNotReceiveWithAnyArgs().GetItemLinks(default!, default);
    }

    [Test]
    public async Task FederatedHalOmitsSourceWhenGovernedSourceIsUnavailable()
    {
        var localPolicy = Substitute.For<ICollectionLinkPolicy<EventListDto>>();
        var policy = new EventDiscoveryLinkPolicy(localPolicy);
        var item = new EventDiscoveryItemDto
        {
            Source = "atproto",
            FederatedEvent = Federated(),
            Federation = new EventFederationMetadataDto
            {
                AtprotoRecordId = Guid.CreateVersion7(),
                HasSourceLink = false,
                Provenance = "atproto"
            }
        };

        LinkDefinition[] links = policy.GetItemLinks(item, null).ToArray();

        await Assert.That(links).IsEmpty();
        localPolicy.DidNotReceiveWithAnyArgs().GetItemLinks(default!, default);
    }

    [Test]
    public async Task DiscoveryCacheInvalidatorEvictsAllDiscoveryTags()
    {
        var store = Substitute.For<IOutputCacheStore>();
        var invalidator = new AtprotoDiscoveryCacheInvalidator(store);

        await invalidator.InvalidateAsync(CancellationToken.None);

        await store.Received(5).EvictByTagAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await store.Received(1).EvictByTagAsync("event-discovery", Arg.Any<CancellationToken>());
        await store.Received(1).EvictByTagAsync("public-home-discovery", Arg.Any<CancellationToken>());
        await store.Received(1).EvictByTagAsync("list-data", Arg.Any<CancellationToken>());
        await store.Received(1).EvictByTagAsync("detail-data", Arg.Any<CancellationToken>());
        await store.Received(1).EvictByTagAsync("seo-sitemap", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AtprotoCapabilitySettingNotificationInvalidatesDiscoveryCache()
    {
        var resolver = Substitute.For<IHierarchicalSettingsResolver>();
        var invalidator = Substitute.For<IAtprotoDiscoveryCacheInvalidator>();
        var handler = new SettingCacheInvalidationHandler(resolver, [invalidator], []);

        await handler.Handle(new SettingChangedNotification(
            GovernanceSettingKeys.Federation.AtprotoEventsEnabled,
            "true",
            "false",
            SettingSource.TenantOverride,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            DateTime.UtcNow), CancellationToken.None);

        await invalidator.Received(1).InvalidateAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UnrelatedSettingNotificationDoesNotInvalidateDiscoveryCache()
    {
        var resolver = Substitute.For<IHierarchicalSettingsResolver>();
        var invalidator = Substitute.For<IAtprotoDiscoveryCacheInvalidator>();
        var handler = new SettingCacheInvalidationHandler(resolver, [invalidator], []);

        await handler.Handle(new SettingChangedNotification(
            GovernanceSettingKeys.LocationPrivacy.AllowHomeLocations,
            "true",
            "false",
            SettingSource.TenantOverride,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            DateTime.UtcNow), CancellationToken.None);

        await invalidator.DidNotReceiveWithAnyArgs().InvalidateAsync(default);
    }

    [Test]
    public async Task PublicRawAtprotoRecordControllerIsAbsent()
    {
        Type[] controllerTypes = typeof(EventController).Assembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type))
            .ToArray();

        await Assert.That(controllerTypes.Any(type => type.Name == "AtprotoRecordController")).IsFalse();
        await Assert.That(typeof(EventController).Assembly.GetType("Explore.API.Hateoas.Policies.AtprotoRecordDetailLinkPolicy"))
            .IsNull();
    }

    [Test]
    public async Task PublicDiscoveryContractsContainNoCredentialOrSessionMembers()
    {
        string[] propertyNames =
        [
            .. typeof(EventDiscoveryItemDto).GetProperties().Select(property => property.Name),
            .. typeof(FederatedEventDto).GetProperties().Select(property => property.Name),
            .. typeof(EventFederationMetadataDto).GetProperties().Select(property => property.Name)
        ];

        string[] forbiddenFragments =
        [
            "AccessToken",
            "RefreshToken",
            "Dpop",
            "PrivateKey",
            "ClientSecret",
            "Credential",
            "SessionEnvelope"
        ];

        await Assert.That(propertyNames.Any(name => forbiddenFragments.Any(fragment =>
            name.Contains(fragment, StringComparison.OrdinalIgnoreCase)))).IsFalse();
    }

    [Test]
    public async Task DiscoveryAndSourceEndpointsRemainAnonymousGets()
    {
        Type controllerType = typeof(EventController);
        string[] methods =
        [
            nameof(EventController.GetAll),
            nameof(EventController.GetAtprotoEventSource)
        ];

        foreach (string method in methods)
        {
            var methodInfo = controllerType.GetMethod(method)!;
            await Assert.That(methodInfo.GetCustomAttributes(typeof(AllowAnonymousAttribute), true)).IsNotEmpty();
            await Assert.That(methodInfo.GetCustomAttributes(typeof(HttpGetAttribute), true)).IsNotEmpty();
        }
    }

    [Test]
    public async Task EventListUsesDedicatedDiscoveryCachePolicy()
    {
        var attribute = typeof(EventController)
            .GetMethod(nameof(EventController.GetAll))!
            .GetCustomAttributes(typeof(OutputCacheAttribute), true)
            .Cast<OutputCacheAttribute>()
            .Single();

        await Assert.That(attribute.PolicyName).IsEqualTo("EventDiscovery");
    }

    private static EventController Controller(
        IMediator mediator,
        IResourceAssembler<EventDiscoveryItemDto> discoveryAssembler)
    {
        var controller = new EventController(
            mediator,
            Substitute.For<IResourceAssembler<EventDto, EventListDto>>(),
            discoveryAssembler)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        return controller;
    }

    private static FederatedEventDto Federated() => new()
    {
        Id = Guid.CreateVersion7(),
        Name = "Remote event",
        CreatedAtUtc = DateTimeOffset.UtcNow
    };
}

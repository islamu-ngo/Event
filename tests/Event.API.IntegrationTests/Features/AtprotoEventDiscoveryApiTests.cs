using Explore.API.Controllers;
using System.Net;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Hateoas.Assemblers;
using Explore.Application.Features.Events.Discovery;
using Explore.Application.Contracts.Persistence;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.API.Models;
using Explore.API.Services;
using Explore.API.Services.Calendar;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.PublicExperience;
using Explore.Application.Features.Events.OpenGraph;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Application.Features.Federation.Atproto.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Notifications;
using Explore.Application.Notifications.Handlers;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
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
        await using var factory = new NativeEventTagsFactory(relational: true);
        using var client = factory.CreateClient();
        var seed = await EventDiscoveryTraversalTests.SeedAsync(factory);
        using var response = await client.GetAsync(
            $"/api/Event?searchTerm={Uri.EscapeDataString(seed.Title)}&pageSize=2");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(json.RootElement.GetProperty("snapshotCount").GetInt32()).IsEqualTo(6);
        var item = json.RootElement.GetProperty("_embedded").GetProperty("items")[0];
        await Assert.That(item.GetProperty("source").GetString()).IsEqualTo("local");
        await Assert.That(item.GetProperty("event").GetProperty("id").GetGuid()).IsNotEqualTo(Guid.Empty);
    }

    [Test]
    public async Task FederatedSourceRedirectUsesOnlyResolvedInternalQueryTarget()
    {
        Guid recordId = Guid.CreateVersion7();
        var sourceHandler = Substitute.For<IQueryHandler<GetAtprotoEventSourceQuery, string?>>();
        sourceHandler.QueryAsync(
                Arg.Is<GetAtprotoEventSourceQuery>(query => query != null && query.AtprotoRecordId == recordId),
                Arg.Any<CancellationToken>())
            .Returns("https://events.example/source");
        EventController controller = Controller(sourceHandler: sourceHandler);

        IActionResult result = await controller.GetAtprotoEventSource(recordId, CancellationToken.None);

        await Assert.That(result).IsTypeOf<RedirectResult>();
        await Assert.That(((RedirectResult)result).Url).IsEqualTo("https://events.example/source");
    }

    [Test]
    public async Task MissingOrDisabledFederatedSourceReturnsGenericNotFound()
    {
        var sourceHandler = Substitute.For<IQueryHandler<GetAtprotoEventSourceQuery, string?>>();
        sourceHandler.QueryAsync(Arg.Any<GetAtprotoEventSourceQuery>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
        EventController controller = Controller(sourceHandler: sourceHandler);

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

        await handler.HandleAsync(new SettingChangedNotification(
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

        await handler.HandleAsync(new SettingChangedNotification(
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
    public async Task EventListCannotReusePublicCachedDiscoveryMembership()
    {
        await using var factory = new NativeEventTagsFactory(relational: true);
        using var client = factory.CreateClient();
        var seed = await EventDiscoveryTraversalTests.SeedAsync(factory);
        using var response = await client.GetAsync(
            "/api/Event?searchTerm=" + Uri.EscapeDataString(seed.Title));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(response.Headers.CacheControl?.Public == true).IsFalse();
    }

    private static EventController Controller(
        IQueryHandler<GetEventDiscoveryTraversalQuery, EventDiscoveryTraversalDto>? discoveryHandler = null,
        IQueryHandler<GetAtprotoEventSourceQuery, string?>? sourceHandler = null,
        IResourceAssembler<EventDiscoveryItemDto>? discoveryAssembler = null,
        EventDiscoveryResponseAuthority? authority = null)
    {
        var tenantId = Guid.CreateVersion7();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var revision = new Explore.Domain.EventDiscoveryRevision { Id = Guid.CreateVersion7(), TenantId = tenantId };
        var identities = Substitute.For<IEventDiscoveryIdentityRepository>();
        identities.GetRevisionAsync(tenantId, Arg.Any<CancellationToken>()).Returns(revision);
        var disclosure = Substitute.For<IEventDiscoveryDisclosureRepository>();
        disclosure.AcquireCurrentAsync(tenantId, Arg.Any<CancellationToken>()).Returns(revision);
        var unit = Substitute.For<IUnitOfWork>();
        unit.ExecuteReadCommittedAsync(Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var operation = call.Arg<Func<CancellationToken, Task<bool>>>();
                ArgumentNullException.ThrowIfNull(operation);
                return operation(call.Arg<CancellationToken>());
            });
        var controller = new EventController(
            Substitute.For<IQueryHandler<GetMyEventsRequest, PaginatedResult<EventListDto>>>(),
            Substitute.For<IQueryHandler<GetEventDetailsRequest, EventDto?>>(),
            Substitute.For<IQueryHandler<GetPublicEventDetailsRequest, EventDto?>>(),
            Substitute.For<IQueryHandler<GetPublicEventOpenGraphImageRequest, EventOpenGraphImageRenderResult?>>(),
            discoveryHandler ?? Substitute.For<IQueryHandler<GetEventDiscoveryTraversalQuery, EventDiscoveryTraversalDto>>(),
            sourceHandler ?? Substitute.For<IQueryHandler<GetAtprotoEventSourceQuery, string?>>(),
            Substitute.For<IResourceAssembler<EventDto, EventListDto>>(),
            discoveryAssembler ?? Substitute.For<IResourceAssembler<EventDiscoveryItemDto>>(),
            new EventDiscoveryTraversalResourceAssembler(
                Substitute.For<IResourceAssembler<EventDiscoveryItemDto>>(),
                Substitute.For<IHateoasLinkGenerator>()),
            authority ?? new EventDiscoveryResponseAuthority(
                unit, disclosure, identities, tenant, TimeProvider.System,
                Substitute.For<IEventDiscoveryResponseBoundaryRepository>()))
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

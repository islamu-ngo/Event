using System.Reflection;
using System.Security.Claims;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Controllers;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.LocationPrivacy;
using Explore.Application.DTOs.Location;
using Explore.Application.Features.EventLocations.Requests.Commands;
using Explore.Application.Features.EventLocations.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features.Hateoas;

[Category("EventLocationPrivacy")]
[Category("EventLocationHateoas")]
public sealed class EventLocationHateoasTests
{
    [Test]
    public async Task DisclosureMutation_PinsDirectRouteAndAuthorizationMetadata()
    {
        MethodInfo action = typeof(EventLocationController).GetMethod(
            nameof(EventLocationController.UpdateDisclosure))!;
        HttpPatchAttribute route = action.GetCustomAttribute<HttpPatchAttribute>()!;
        AuthorizeResourceAttribute authorization = typeof(UpdateEventLocationPolicyCommand)
            .GetCustomAttribute<AuthorizeResourceAttribute>()!;
        var eventId = Guid.CreateVersion7();
        ISecureRequest request = new UpdateEventLocationPolicyCommand { EventId = eventId };

        await Assert.That(route.Template)
            .IsEqualTo("{eventLocationId:guid}/disclosure");
        await Assert.That(route.Name).IsEqualTo(RouteNames.UpdateEventLocationDisclosure);
        await Assert.That(authorization.Resource).IsEqualTo(ResourceKinds.Event);
        await Assert.That(authorization.Action).IsEqualTo(AuthorizationActions.Update);
        await Assert.That(request.ResourceId).IsEqualTo(eventId.ToString("D"));
    }

    [Test]
    public async Task ManagementResponse_WhenDisclosureUpdateAllowed_ContainsAuthorizedEditLink()
    {
        var eventId = Guid.CreateVersion7();
        var eventLocationId = Guid.CreateVersion7();
        (object? payload, IReadOnlyList<AuthorizationRequest> checks) = await InvokeManagementAsync(
            eventId,
            eventLocationId,
            _ => true);

        await Assert.That(payload).IsTypeOf<HalResource<EventLocationManagementDto>>();
        var resource = (HalResource<EventLocationManagementDto>)payload!;
        await Assert.That(resource.Links.ContainsKey(LinkRelations.Edit)).IsTrue();
        await Assert.That(resource.Links[LinkRelations.Edit].Href)
            .IsEqualTo($"/api/events/{eventId:D}/locations/{eventLocationId:D}/disclosure");
        await Assert.That(resource.Links[LinkRelations.Edit].Method).IsEqualTo(HttpMethods.Patch);
        await AssertDirectMutationParityAsync(checks, eventId);
    }

    [Test]
    public async Task ManagementResponse_WhenDisclosureUpdateDenied_OmitsEditAndSpeculativeLinks()
    {
        var eventId = Guid.CreateVersion7();
        (object? payload, IReadOnlyList<AuthorizationRequest> checks) = await InvokeManagementAsync(
            eventId,
            Guid.CreateVersion7(),
            _ => false);

        await Assert.That(payload).IsTypeOf<HalResource<EventLocationManagementDto>>();
        var resource = (HalResource<EventLocationManagementDto>)payload!;
        await Assert.That(resource.Links.ContainsKey(LinkRelations.Edit)).IsFalse();
        await Assert.That(resource.Links.Keys.Any(rel =>
            rel.Contains("owner", StringComparison.OrdinalIgnoreCase)
            || rel.Contains("remedi", StringComparison.OrdinalIgnoreCase))).IsFalse();
        await AssertDirectMutationParityAsync(checks, eventId);
    }

    [Test]
    public async Task ManagementResponse_WhenAuthorizationProviderFails_OmitsEdit()
    {
        (object? payload, _) = await InvokeManagementAsync(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            _ => throw new InvalidOperationException("Provider unavailable"));

        await Assert.That(payload).IsTypeOf<HalResource<EventLocationManagementDto>>();
        var resource = (HalResource<EventLocationManagementDto>)payload!;
        await Assert.That(resource.Links.ContainsKey(LinkRelations.Edit)).IsFalse();
    }

    [Test]
    public async Task ManagementResponse_WhenRouteMetadataIsMissing_OmitsEditWithoutAuthorizationCall()
    {
        (object? payload, IReadOnlyList<AuthorizationRequest> checks) = await InvokeManagementAsync(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            _ => true,
            includeRouteMetadata: false);

        await Assert.That(payload).IsTypeOf<HalResource<EventLocationManagementDto>>();
        var resource = (HalResource<EventLocationManagementDto>)payload!;
        await Assert.That(resource.Links.ContainsKey(LinkRelations.Edit)).IsFalse();
        await Assert.That(checks).IsEmpty();
    }

    [Test]
    public async Task ManagementResponse_WhenAuthorizationMetadataIsMissing_OmitsEditWithoutAuthorizationCall()
    {
        (object? payload, IReadOnlyList<AuthorizationRequest> checks) = await InvokeManagementAsync(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            _ => true,
            includeAuthorizationMetadata: false);

        await Assert.That(payload).IsTypeOf<HalResource<EventLocationManagementDto>>();
        var resource = (HalResource<EventLocationManagementDto>)payload!;
        await Assert.That(resource.Links.ContainsKey(LinkRelations.Edit)).IsFalse();
        await Assert.That(checks).IsEmpty();
    }

    [Test]
    public async Task ManagementResponse_WhenEventRouteMetadataIsMalformed_OmitsEditWithoutAuthorizationCall()
    {
        (object? payload, IReadOnlyList<AuthorizationRequest> checks) = await InvokeManagementAsync(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            _ => true,
            routeEventId: "not-an-event-id");

        await Assert.That(payload).IsTypeOf<HalResource<EventLocationManagementDto>>();
        var resource = (HalResource<EventLocationManagementDto>)payload!;
        await Assert.That(resource.Links.ContainsKey(LinkRelations.Edit)).IsFalse();
        await Assert.That(checks).IsEmpty();
    }

    private static async Task<(object? Payload, IReadOnlyList<AuthorizationRequest> Checks)> InvokeManagementAsync(
        Guid eventId,
        Guid eventLocationId,
        Func<AuthorizationRequest, bool> decision,
        bool includeRouteMetadata = true,
        object? routeEventId = null,
        bool includeAuthorizationMetadata = true)
    {
        var checks = new List<AuthorizationRequest>();
        var authorizationProvider = new StubAuthorizationProvider
        {
            CheckPredicate = check =>
            {
                checks.Add(check);
                return decision(check);
            }
        };
        var evaluator = new HateoasAuthorizationEvaluator(
            authorizationProvider,
            Substitute.For<Explore.Application.Contracts.Persistence.IEventRepository>(),
            Substitute.For<ITenantContext>(),
            Substitute.For<Microsoft.Extensions.Logging.ILogger<HateoasAuthorizationEvaluator>>());
        var linkGenerator = Substitute.For<IHateoasLinkGenerator>();
        linkGenerator.GenerateLink(Arg.Any<LinkDefinition>(), Arg.Any<HttpContext>())
            .Returns(call => MaterializeLink(call.Arg<LinkDefinition>()));

        var tenantId = Guid.CreateVersion7();
        EventLocationManagementDto dto = CreateManagementDto(
            eventLocationId,
            eventId,
            tenantId,
            Guid.CreateVersion7(),
            includeAuthorizationMetadata);
        IMediator mediator = Substitute.For<IMediator>();
        mediator.Send(
                Arg.Any<GetManagementEventLocationRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(dto);

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString("D"))],
                "Test"))
        };
        if (includeRouteMetadata)
        {
            httpContext.Request.RouteValues["eventId"] = routeEventId ?? eventId;
            httpContext.Request.RouteValues["eventLocationId"] = eventLocationId;
        }

        var services = new ServiceCollection();
        services.AddSingleton(mediator);
        services.AddSingleton<IHateoasAuthorizationEvaluator>(evaluator);
        services.AddSingleton(linkGenerator);
        services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = httpContext });
        services.AddHateoasAssemblers();
        await using ServiceProvider provider = services.BuildServiceProvider();
        httpContext.RequestServices = provider;

        var controller = ActivatorUtilities.CreateInstance<EventLocationController>(provider);
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        var result = await controller.GetManagement(eventId, eventLocationId);
        object? payload = result.Result is OkObjectResult ok ? ok.Value : result.Value;
        return (payload, checks);
    }

    private static HalLink MaterializeLink(LinkDefinition definition)
    {
        if (!string.Equals(
                definition.RouteName,
                RouteNames.UpdateEventLocationDisclosure,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unexpected route {definition.RouteName}.");
        }

        Guid eventId = GetRouteValue<Guid>(definition.RouteValues, "eventId");
        Guid eventLocationId = GetRouteValue<Guid>(definition.RouteValues, "eventLocationId");
        return new HalLink
        {
            Href = $"/api/events/{eventId:D}/locations/{eventLocationId:D}/disclosure",
            Method = definition.Method,
            Title = definition.Title
        };
    }

    private static EventLocationManagementDto CreateManagementDto(
        Guid eventLocationId,
        Guid eventId,
        Guid tenantId,
        Guid actorId,
        bool includeAuthorizationMetadata) =>
        EventLocationManagementDto.FromDisclosureResult(
            EventLocationDisclosureResult.Suppressed(
                eventLocationId,
                EventLocationDisclosurePurpose.Management,
                EventLocationDisclosureState.ToBeAnnounced),
            new EventLocationDisclosurePolicyDto(
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                (int)LocationDisclosureAudienceEnum.Never,
                null),
            needsPrivacyReview: false,
            policyVersion: 1,
            Guid.CreateVersion7(),
            includeAuthorizationMetadata
                ? new AuthorizationRequest(
                    ResourceKinds.Event,
                    eventId.ToString("D"),
                    AuthorizationActions.Update,
                    new AuthorizationScope(TenantId: tenantId.ToString("D")),
                    new EventAuthorizationFacts(
                        tenantId,
                        eventId,
                        actorId,
                        UserId: null,
                        OrganizationId: null,
                        GroupId: null,
                        OrganizerActorId: null,
                        OrganizerUserId: null,
                        OrganizerOrganizationId: null,
                        OrganizerGroupId: null,
                        ProvenanceType: null,
                        SubmittedByUserId: null))
                : null);

    private static async Task AssertDirectMutationParityAsync(
        IReadOnlyList<AuthorizationRequest> checks,
        Guid eventId)
    {
        await Assert.That(checks).HasSingleItem();
        AuthorizationRequest check = checks[0];
        await Assert.That(check.ResourceKind).IsEqualTo(ResourceKinds.Event);
        await Assert.That(check.ResourceId).IsEqualTo(eventId.ToString("D"));
        await Assert.That(check.Action).IsEqualTo(AuthorizationActions.Update);
        await Assert.That(check.Facts).IsTypeOf<EventAuthorizationFacts>();
        var facts = (EventAuthorizationFacts)check.Facts!;
        await Assert.That(facts.EventId).IsEqualTo(eventId);
        await Assert.That(facts.TenantId).IsNotEqualTo(Guid.Empty);
        await Assert.That(facts.ActorId).IsNotEqualTo(Guid.Empty);
        // The scope the provider resolves policy with must name the same tenant the facts describe.
        await Assert.That(check.Scope?.TenantId).IsEqualTo(facts.TenantId.ToString("D"));
    }

    private static T GetRouteValue<T>(object? routeValues, string name)
    {
        object? value = routeValues?.GetType().GetProperty(name)?.GetValue(routeValues);
        return value is T typed ? typed : throw new InvalidOperationException($"Missing route value {name}.");
    }
}

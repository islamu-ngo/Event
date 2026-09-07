using System.Security.Claims;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.API.Hateoas.Resources;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.EventTemplate;
using Explore.Application.Features.EventSessionTemplateSync.Queries.GetEventSessionTemplateDiff;
using Explore.Application.Features.EventTemplateSync.Queries.GetEventTemplateDiff;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using TUnit.Assertions;
using TUnit.Core;
using EventTemplateDiffDto = Explore.Application.DTOs.EventTemplateSync.TemplateDiffDto;
using SessionTemplateDiffDto = Explore.Application.DTOs.EventSessionTemplateSync.TemplateDiffDto;

namespace Event.Api.IntegrationTests.Features.Hateoas;

public sealed class TemplateSyncHateoasTests
{
    [Test]
    public async Task EventTemplateCollectionLinks_ExposePermissionMetadataForCreateEditAndDelete()
    {
        var tenantId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var policy = new EventTemplateCollectionLinkPolicy();
        var dto = new EventTemplateListDto
        {
            Id = templateId,
            TenantId = tenantId,
            TemplateKey = "conference",
            DisplayName = "Conference",
            Version = 1,
            IsPublished = true,
            IsActive = true
        };

        var collectionLinks = policy.GetCollectionLinks(user: null).ToList();
        var create = collectionLinks.Single(link => link.Rel == LinkRelations.Create);

        await Assert.That(create.RouteName).IsEqualTo(RouteNames.CreateEventTemplate);
        await Assert.That(create.Method).IsEqualTo(HttpMethods.Post);
        await Assert.That(create.PermissionResourceKind).IsEqualTo(ResourceKinds.Tenant);
        await Assert.That(create.PermissionAction).IsEqualTo(AuthorizationActions.Create);

        var itemLinks = policy.GetItemLinks(dto, user: null).ToList();
        var edit = itemLinks.Single(link => link.Rel == LinkRelations.Edit);
        var delete = itemLinks.Single(link => link.Rel == LinkRelations.Delete);

        await Assert.That(edit.RouteName).IsEqualTo(RouteNames.UpdateEventTemplate);
        await Assert.That(edit.Method).IsEqualTo(HttpMethods.Patch);
        await Assert.That(edit.PermissionResourceKind).IsEqualTo(ResourceKinds.Tenant);
        await Assert.That(edit.PermissionAction).IsEqualTo(AuthorizationActions.Update);
        await Assert.That(edit.PermissionResourceId).IsEqualTo(tenantId.ToString());
        await Assert.That(edit.PermissionFacts).IsEqualTo(new TenantScopedAuthorizationFacts(tenantId));
        await Assert.That(GetRouteValue<Guid>(edit.RouteValues, "id")).IsEqualTo(templateId);

        await Assert.That(delete.RouteName).IsEqualTo(RouteNames.DeleteEventTemplate);
        await Assert.That(delete.Method).IsEqualTo(HttpMethods.Delete);
        await Assert.That(delete.PermissionResourceKind).IsEqualTo(ResourceKinds.Tenant);
        await Assert.That(delete.PermissionAction).IsEqualTo(AuthorizationActions.Delete);
        await Assert.That(delete.PermissionResourceId).IsEqualTo(tenantId.ToString());
        await Assert.That(delete.PermissionFacts).IsEqualTo(new TenantScopedAuthorizationFacts(tenantId));
        await Assert.That(GetRouteValue<Guid>(delete.RouteValues, "id")).IsEqualTo(templateId);
    }

    [Test]
    public async Task EventTemplateSyncLinks_ExposePermissionMetadataForDiffAndApply()
    {
        var eventId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var policy = new EventTemplateSyncLinkPolicy();

        var links = policy.GetLinks(
            new EventTemplateSyncResource(tenantId, eventId, 7, HasChanges: true),
            user: null).ToList();

        var diff = links.Single(link => link.Rel == "sync-diff");
        await Assert.That(diff.RouteName).IsEqualTo(RouteNames.GetEventTemplateSyncDiff);
        await Assert.That(diff.Method).IsEqualTo(HttpMethods.Get);
        await Assert.That(diff.RequiresAuth).IsTrue();
        await Assert.That(diff.PermissionResourceKind).IsEqualTo(ResourceKinds.CustomPropertyTemplate);
        await Assert.That(diff.PermissionAction).IsEqualTo(AuthorizationActions.CustomPropertyTemplates.SyncDiff);
        await Assert.That(diff.PermissionResourceId).IsEqualTo(eventId.ToString());
        await Assert.That(GetRouteValue<Guid>(diff.RouteValues, "eventId")).IsEqualTo(eventId);
        await Assert.That(GetRouteValue<int>(diff.RouteValues, "templateVersion")).IsEqualTo(7);
        // Template sync is tenant-administered: the event, session and version identify the target row and
        // travel in the route, not in the policy facts.
        await Assert.That(diff.PermissionFacts).IsEqualTo(new TenantScopedAuthorizationFacts(tenantId));

        var apply = links.Single(link => link.Rel == "sync-apply");
        await Assert.That(apply.RouteName).IsEqualTo(RouteNames.ApplyEventTemplateSync);
        await Assert.That(apply.Method).IsEqualTo(HttpMethods.Post);
        await Assert.That(apply.RequiresAuth).IsTrue();
        await Assert.That(apply.PermissionResourceKind).IsEqualTo(ResourceKinds.CustomPropertyTemplate);
        await Assert.That(apply.PermissionAction).IsEqualTo(AuthorizationActions.CustomPropertyTemplates.SyncApply);
        await Assert.That(apply.PermissionResourceId).IsEqualTo(eventId.ToString());
        await Assert.That(GetRouteValue<Guid>(apply.RouteValues, "eventId")).IsEqualTo(eventId);

        var history = links.Single(link => link.Rel == "sync-history");
        await Assert.That(history.RequiresAuth).IsTrue();
        await Assert.That(history.PermissionAction).IsEqualTo(AuthorizationActions.CustomPropertyTemplates.View);
    }

    [Test]
    public async Task EventSessionTemplateSyncLinks_ExposePermissionMetadataForDiffAndApply()
    {
        var sessionId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var policy = new EventSessionTemplateSyncLinkPolicy();

        var links = policy.GetLinks(
            new EventSessionTemplateSyncResource(tenantId, sessionId, 4, HasChanges: true),
            user: null).ToList();

        var diff = links.Single(link => link.Rel == "sync-diff");
        await Assert.That(diff.RouteName).IsEqualTo(RouteNames.GetEventSessionTemplateSyncDiff);
        await Assert.That(diff.Method).IsEqualTo(HttpMethods.Get);
        await Assert.That(diff.RequiresAuth).IsTrue();
        await Assert.That(diff.PermissionResourceKind).IsEqualTo(ResourceKinds.CustomPropertyTemplate);
        await Assert.That(diff.PermissionAction).IsEqualTo(AuthorizationActions.CustomPropertyTemplates.SyncDiff);
        await Assert.That(diff.PermissionResourceId).IsEqualTo(sessionId.ToString());
        await Assert.That(GetRouteValue<Guid>(diff.RouteValues, "sessionId")).IsEqualTo(sessionId);
        await Assert.That(GetRouteValue<int>(diff.RouteValues, "templateVersion")).IsEqualTo(4);
        // Template sync is tenant-administered: the event, session and version identify the target row and
        // travel in the route, not in the policy facts.
        await Assert.That(diff.PermissionFacts).IsEqualTo(new TenantScopedAuthorizationFacts(tenantId));

        var apply = links.Single(link => link.Rel == "sync-apply");
        await Assert.That(apply.RouteName).IsEqualTo(RouteNames.ApplyEventSessionTemplateSync);
        await Assert.That(apply.Method).IsEqualTo(HttpMethods.Post);
        await Assert.That(apply.RequiresAuth).IsTrue();
        await Assert.That(apply.PermissionResourceKind).IsEqualTo(ResourceKinds.CustomPropertyTemplate);
        await Assert.That(apply.PermissionAction).IsEqualTo(AuthorizationActions.CustomPropertyTemplates.SyncApply);
        await Assert.That(apply.PermissionResourceId).IsEqualTo(sessionId.ToString());
        await Assert.That(GetRouteValue<Guid>(apply.RouteValues, "sessionId")).IsEqualTo(sessionId);

        var history = links.Single(link => link.Rel == "sync-history");
        await Assert.That(history.RequiresAuth).IsTrue();
        await Assert.That(history.PermissionAction).IsEqualTo(AuthorizationActions.CustomPropertyTemplates.View);
    }

    [Test]
    public async Task EventTemplateSyncController_GetDiff_MaterializesOnlyAuthorizedLinks()
    {
        var eventId = Guid.NewGuid();
        var mediator = Substitute.For<IMediator>();
        var authorizationEvaluator = Substitute.For<IHateoasAuthorizationEvaluator>();
        var linkGenerator = Substitute.For<IHateoasLinkGenerator>();
        var linkPolicy = Substitute.For<ILinkPolicy<EventTemplateSyncResource>>();
        var definitions = CreateManualDefinitions();
        mediator.Send(Arg.Is<GetEventTemplateDiffQuery>(query => query.EventId == eventId), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(BaseCommandResponse.Success(CreateEventTemplateDiff())));
        linkPolicy.GetLinks(Arg.Any<EventTemplateSyncResource>(), Arg.Any<ClaimsPrincipal?>()).Returns(definitions);
        authorizationEvaluator.AreLinksAllowedAsync(Arg.Is<IReadOnlyList<LinkDefinition>>(links => LinksMatchManualDefinitions(links)), Arg.Any<ClaimsPrincipal?>(), Arg.Any<HttpContext>())
            .Returns(Task.FromResult<IReadOnlyList<bool>>([true, false, true]));
        linkGenerator.GenerateLink(Arg.Any<LinkDefinition>(), Arg.Any<HttpContext>())
            .Returns(call => new HalLink { Href = $"/{call.Arg<LinkDefinition>().Rel}" });
        var controller = CreateEventTemplateController(mediator, authorizationEvaluator, linkGenerator, linkPolicy);

        var result = await controller.GetDiff(eventId, 3, CancellationToken.None);

        var resource = ExtractHalResource(result);
        await Assert.That(resource.Links.ContainsKey("allowed-diff")).IsTrue();
        await Assert.That(resource.Links.ContainsKey("blocked-apply")).IsFalse();
        await Assert.That(resource.Links.ContainsKey("allowed-history")).IsTrue();
        await authorizationEvaluator.Received(1).AreLinksAllowedAsync(Arg.Is<IReadOnlyList<LinkDefinition>>(links => LinksMatchManualDefinitions(links)), Arg.Any<ClaimsPrincipal?>(), Arg.Any<HttpContext>());
        linkGenerator.DidNotReceive().GenerateLink(Arg.Is<LinkDefinition>(definition => definition.Rel == "blocked-apply"), Arg.Any<HttpContext>());
    }

    [Test]
    public async Task EventSessionTemplateSyncController_GetDiff_MaterializesOnlyAuthorizedLinks()
    {
        var sessionId = Guid.NewGuid();
        var mediator = Substitute.For<IMediator>();
        var authorizationEvaluator = Substitute.For<IHateoasAuthorizationEvaluator>();
        var linkGenerator = Substitute.For<IHateoasLinkGenerator>();
        var linkPolicy = Substitute.For<ILinkPolicy<EventSessionTemplateSyncResource>>();
        var definitions = CreateManualDefinitions();
        mediator.Send(Arg.Is<GetEventSessionTemplateDiffQuery>(query => query.EventSessionId == sessionId), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(BaseCommandResponse.Success(CreateSessionTemplateDiff())));
        linkPolicy.GetLinks(Arg.Any<EventSessionTemplateSyncResource>(), Arg.Any<ClaimsPrincipal?>()).Returns(definitions);
        authorizationEvaluator.AreLinksAllowedAsync(Arg.Is<IReadOnlyList<LinkDefinition>>(links => LinksMatchManualDefinitions(links)), Arg.Any<ClaimsPrincipal?>(), Arg.Any<HttpContext>())
            .Returns(Task.FromResult<IReadOnlyList<bool>>([true, false, true]));
        linkGenerator.GenerateLink(Arg.Any<LinkDefinition>(), Arg.Any<HttpContext>())
            .Returns(call => new HalLink { Href = $"/{call.Arg<LinkDefinition>().Rel}" });
        var controller = CreateEventSessionTemplateController(mediator, authorizationEvaluator, linkGenerator, linkPolicy);

        var result = await controller.GetDiff(sessionId, 3, CancellationToken.None);

        var resource = ExtractHalResource(result);
        await Assert.That(resource.Links.ContainsKey("allowed-diff")).IsTrue();
        await Assert.That(resource.Links.ContainsKey("blocked-apply")).IsFalse();
        await Assert.That(resource.Links.ContainsKey("allowed-history")).IsTrue();
        await authorizationEvaluator.Received(1).AreLinksAllowedAsync(Arg.Is<IReadOnlyList<LinkDefinition>>(links => LinksMatchManualDefinitions(links)), Arg.Any<ClaimsPrincipal?>(), Arg.Any<HttpContext>());
        linkGenerator.DidNotReceive().GenerateLink(Arg.Is<LinkDefinition>(definition => definition.Rel == "blocked-apply"), Arg.Any<HttpContext>());
    }

    private static IReadOnlyList<LinkDefinition> CreateManualDefinitions() =>
    [
        new("allowed-diff", "AllowedDiff", Method: HttpMethods.Get, RequiresAuth: true),
        new("blocked-apply", "BlockedApply", Method: HttpMethods.Post, RequiresAuth: true),
        new("allowed-history", "AllowedHistory", Method: HttpMethods.Get, RequiresAuth: true)
    ];

    private static bool LinksMatchManualDefinitions(IReadOnlyList<LinkDefinition> links) =>
        links.Count == 3 &&
        links[0].Rel == "allowed-diff" &&
        links[1].Rel == "blocked-apply" &&
        links[2].Rel == "allowed-history";

    private static EventTemplateSyncController CreateEventTemplateController(
        IMediator mediator,
        IHateoasAuthorizationEvaluator authorizationEvaluator,
        IHateoasLinkGenerator linkGenerator,
        ILinkPolicy<EventTemplateSyncResource> linkPolicy)
    {
        var controller = new EventTemplateSyncController(
            mediator,
            authorizationEvaluator,
            linkGenerator,
            linkPolicy,
            Substitute.For<ITenantContext>());
        controller.ControllerContext = new ControllerContext { HttpContext = CreateHttpContext() };
        return controller;
    }

    private static EventSessionTemplateSyncController CreateEventSessionTemplateController(
        IMediator mediator,
        IHateoasAuthorizationEvaluator authorizationEvaluator,
        IHateoasLinkGenerator linkGenerator,
        ILinkPolicy<EventSessionTemplateSyncResource> linkPolicy)
    {
        var controller = new EventSessionTemplateSyncController(
            mediator,
            authorizationEvaluator,
            linkGenerator,
            linkPolicy,
            Substitute.For<ITenantContext>());
        controller.ControllerContext = new ControllerContext { HttpContext = CreateHttpContext() };
        return controller;
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "Test"));
        return context;
    }

    private static HalResource<T> ExtractHalResource<T>(ActionResult<HalResource<T>> result) where T : class
    {
        var ok = result.Result as OkObjectResult;
        return (HalResource<T>)ok!.Value!;
    }

    private static EventTemplateDiffDto CreateEventTemplateDiff() => new(
        3,
        2,
        [],
        [],
        [],
        [],
        [],
        [],
        []);

    private static SessionTemplateDiffDto CreateSessionTemplateDiff() => new(
        3,
        2,
        [],
        [],
        [],
        [],
        [],
        [],
        []);

    private static T? GetRouteValue<T>(object? routeValues, string name)
    {
        if (routeValues is null)
            return default;

        var property = routeValues.GetType().GetProperty(name);
        var value = property?.GetValue(routeValues);
        return value is T typedValue ? typedValue : default;
    }

}

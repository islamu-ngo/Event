using System.Security.Claims;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Assemblers;
using Explore.API.Hateoas.Policies;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Filters;
using Explore.Application.Authorization;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.RegistrationProviders;
using Explore.Application.Hateoas;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features.Hateoas;

public sealed class RegistrationFormEventLinkPolicyTests
{
    [Test]
    public async Task ManagementEvent_AdvertisesTenantScopedRegistrationWorkflow()
    {
        Guid eventId = Guid.CreateVersion7();
        Guid tenantId = Guid.CreateVersion7();
        var dto = new EventDto
        {
            Id = eventId,
            TenantId = tenantId,
            Title = "Managed event",
            ActorId = Guid.CreateVersion7(),
            ActorDisplayName = "Organizer",
            ActorTypeId = (int)ActorTypeEnum.Organization,
            ActorTypeFullName = "Organization",
            EventStatusId = (int)EventStatusEnum.Draft,
            EventStatusFullName = "Draft",
            EventStatusMasterCode = "DRAFT",
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityTypeFullName = "Public",
            VisibilityTypeMasterCode = "PUBLIC",
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormatFullName = "In person",
            EventFormatMasterCode = "IN_PERSON",
            IsManagementView = true
        };

        LinkDefinition link = new EventDetailLinkPolicy()
            .GetLinks(dto, new ClaimsPrincipal(new ClaimsIdentity("test")))
            .Single(candidate => candidate.Rel == LinkRelations.ManageRegistrationWorkflow);
        var routeValues = new RouteValueDictionary(link.RouteValues);

        await Assert.That(link.RouteName).IsEqualTo(RouteNames.GetRegistrationWorkflow);
        await Assert.That(link.Method).IsEqualTo(HttpMethods.Get);
        await Assert.That(routeValues["eventId"]).IsEqualTo(eventId);
        await Assert.That(routeValues["purpose"]).IsEqualTo("registration");
        await Assert.That(link.PermissionAction).IsEqualTo(AuthorizationActions.Events.ManageRegistrationWorkflow);
        await Assert.That(link.PermissionResourceKind).IsEqualTo(ResourceKinds.Event);
        await Assert.That(link.PermissionResourceId).IsEqualTo(eventId.ToString());
        await Assert.That(link.PermissionScope?.TenantId).IsEqualTo(tenantId.ToString());
        await Assert.That(link.PermissionFacts).IsTypeOf<EventAuthorizationFacts>();
        var facts = (EventAuthorizationFacts)link.PermissionFacts!;
        await Assert.That(facts.TenantId).IsEqualTo(tenantId);
        await Assert.That(facts.EventId).IsEqualTo(eventId);
    }

    [Test]
    public async Task ManagementEvent_AdvertisesProviderHealthAndManageRelationsSeparately()
    {
        Guid eventId = Guid.CreateVersion7();
        Guid tenantId = Guid.CreateVersion7();
        var dto = new EventDto
        {
            Id = eventId,
            TenantId = tenantId,
            Title = "Managed event",
            ActorId = Guid.CreateVersion7(),
            ActorDisplayName = "Organizer",
            ActorTypeId = (int)ActorTypeEnum.Organization,
            ActorTypeFullName = "Organization",
            EventStatusId = (int)EventStatusEnum.Draft,
            EventStatusFullName = "Draft",
            EventStatusMasterCode = "DRAFT",
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityTypeFullName = "Public",
            VisibilityTypeMasterCode = "PUBLIC",
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormatFullName = "In person",
            EventFormatMasterCode = "IN_PERSON",
            IsManagementView = true
        };

        LinkDefinition health = new EventDetailLinkPolicy()
            .GetLinks(dto, new ClaimsPrincipal(new ClaimsIdentity("test")))
            .Single(candidate => candidate.Rel == LinkRelations.ViewRegistrationProviderHealth);
        LinkDefinition manage = new EventDetailLinkPolicy()
            .GetLinks(dto, new ClaimsPrincipal(new ClaimsIdentity("test")))
            .Single(candidate => candidate.Rel == LinkRelations.ManageRegistrationChannels);

        await Assert.That(health.RouteName).IsEqualTo(RouteNames.GetRegistrationProviderHealth);
        await Assert.That(health.PermissionAction).IsEqualTo(AuthorizationActions.Events.ViewRegistrationProviderHealth);
        await Assert.That(manage.RouteName).IsEqualTo(RouteNames.GetRegistrationProviderQueue);
        await Assert.That(manage.PermissionAction).IsEqualTo(AuthorizationActions.Events.ManageRegistrationChannels);
        await Assert.That(health.PermissionAction).IsNotEqualTo(manage.PermissionAction);

        var healthRoute = new RouteValueDictionary(health.RouteValues);
        var manageRoute = new RouteValueDictionary(manage.RouteValues);
        await Assert.That(healthRoute["tenantId"]).IsEqualTo(tenantId);
        await Assert.That(manageRoute["tenantId"]).IsEqualTo(tenantId);
    }

    [Test]
    public async Task ProviderManagementLinks_PropagateTenantQueryValue()
    {
        Guid eventId = Guid.CreateVersion7();
        Guid tenantId = Guid.CreateVersion7();
        var dto = new RegistrationProviderBindingHealthDto
        {
            TenantId = tenantId,
            EventId = eventId,
            BindingId = Guid.CreateVersion7(),
            ConnectionId = Guid.CreateVersion7()
        };

        LinkDefinition self = new RegistrationProviderHealthLinkPolicy()
            .GetLinks(dto, new ClaimsPrincipal(new ClaimsIdentity("test")))
            .Single(candidate => candidate.Rel == LinkRelations.Self);
        var routeValues = new RouteValueDictionary(self.RouteValues);

        await Assert.That(routeValues["eventId"]).IsEqualTo(eventId);
        await Assert.That(routeValues["tenantId"]).IsEqualTo(tenantId);
        await Assert.That(self.PermissionScope?.TenantId).IsEqualTo(tenantId.ToString());
    }

    [Test]
    public async Task ProviderQueueLinks_EmitUsableEffectActionsAndNoImpossibleSubmissionRetry()
    {
        Guid eventId = Guid.CreateVersion7();
        Guid tenantId = Guid.CreateVersion7();
        var effect = new RegistrationProviderParkedQueueItemDto
        {
            TenantId = tenantId,
            EventId = eventId,
            BindingId = Guid.CreateVersion7(),
            EffectOutboxId = Guid.CreateVersion7()
        };
        var submission = new RegistrationProviderParkedQueueItemDto
        {
            TenantId = tenantId,
            EventId = eventId,
            BindingId = effect.BindingId,
            SubmissionId = Guid.CreateVersion7()
        };
        var policy = new RegistrationProviderQueueCollectionLinkPolicy();

        LinkDefinition[] effectLinks = [.. policy.GetItemLinks(effect, new ClaimsPrincipal(new ClaimsIdentity("test")))];
        LinkDefinition[] submissionLinks = [.. policy.GetItemLinks(submission, new ClaimsPrincipal(new ClaimsIdentity("test")))];

        await Assert.That(effectLinks.Select(link => link.Rel)).IsEquivalentTo([LinkRelations.Retry, LinkRelations.Resolve]);
        await Assert.That(submissionLinks.Select(link => link.Rel)).IsEquivalentTo([LinkRelations.Resolve]);
        foreach (LinkDefinition link in effectLinks.Concat(submissionLinks))
        {
            var routeValues = new RouteValueDictionary(link.RouteValues);
            await Assert.That(routeValues["tenantId"]).IsEqualTo(tenantId);
            await Assert.That(routeValues["eventId"]).IsEqualTo(eventId);
        }
    }

    [Test]
    public async Task ProviderConnectionHal_HealthOnlyAuthorizationOmitsMutationAffordances()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        var dto = new RegistrationProviderConnectionDto { Id = Guid.CreateVersion7(), TenantId = tenantId, EventId = eventId, Name = "Forms" };
        IHateoasAuthorizationEvaluator evaluator = Substitute.For<IHateoasAuthorizationEvaluator>();
        evaluator.AreLinksAllowedAsync(Arg.Any<IReadOnlyList<LinkDefinition>>(), Arg.Any<ClaimsPrincipal?>(), Arg.Any<HttpContext>())
            .Returns(call => (IReadOnlyList<bool>)call.Arg<IReadOnlyList<LinkDefinition>>()
                .Select(link => link.PermissionAction == AuthorizationActions.Events.ViewRegistrationProviderHealth)
                .ToArray());
        IHateoasLinkGenerator generator = Substitute.For<IHateoasLinkGenerator>();
        generator.GenerateLink(Arg.Any<LinkDefinition>(), Arg.Any<HttpContext>())
            .Returns(call => new HalLink { Href = $"/{call.Arg<LinkDefinition>().Rel}" });
        var services = new ServiceCollection().AddSingleton(evaluator).BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        var assembler = new RegistrationProviderConnectionResourceAssembler(
            generator,
            new RegistrationProviderConnectionLinkPolicy(),
            new RegistrationProviderConnectionCollectionLinkPolicy());

        HalResource<RegistrationProviderConnectionDto> resource = await assembler.ToResource(dto, context);

        await Assert.That(resource.Links.ContainsKey(LinkRelations.Edit)).IsFalse();
        await Assert.That(resource.Links.ContainsKey(LinkRelations.Delete)).IsFalse();
        await Assert.That(resource.Links.ContainsKey(LinkRelations.Origins)).IsFalse();
    }

    [Test]
    public async Task ProviderCollectionHal_EmitsCanonicalMutationRelationsWithExactAuthorization()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        var context = new RegistrationProviderEventCollectionContext(tenantId, eventId);

        LinkDefinition connectionCreate = new RegistrationProviderConnectionCollectionLinkPolicy()
            .GetCollectionLinks(new ClaimsPrincipal(new ClaimsIdentity("test")), context)
            .Single(candidate => candidate.Rel == LinkRelations.ProviderCreate);
        LinkDefinition bindingCreate = new RegistrationProviderBindingCollectionLinkPolicy()
            .GetCollectionLinks(new ClaimsPrincipal(new ClaimsIdentity("test")), context)
            .Single(candidate => candidate.Rel == LinkRelations.ProviderCreate);
        LinkDefinition manualImport = new RegistrationProviderBindingCollectionLinkPolicy()
            .GetCollectionLinks(new ClaimsPrincipal(new ClaimsIdentity("test")), context)
            .Single(candidate => candidate.Rel == LinkRelations.ManualImport);

        await Assert.That(connectionCreate.RouteName).IsEqualTo(RouteNames.CreateRegistrationProviderConnection);
        await Assert.That(connectionCreate.Method).IsEqualTo(HttpMethods.Post);
        await Assert.That(connectionCreate.PermissionResourceKind).IsEqualTo(ResourceKinds.Tenant);
        await Assert.That(connectionCreate.PermissionAction).IsEqualTo(AuthorizationActions.Tenants.Update);
        await Assert.That(bindingCreate.RouteName).IsEqualTo(RouteNames.CreateRegistrationProviderBinding);
        await Assert.That(bindingCreate.PermissionResourceKind).IsEqualTo(ResourceKinds.Event);
        await Assert.That(bindingCreate.PermissionAction).IsEqualTo(AuthorizationActions.Events.ManageRegistrationChannels);
        await Assert.That(manualImport.RouteName).IsEqualTo(RouteNames.QueueManualRegistrationProviderImport);
        await Assert.That(manualImport.PermissionScope?.TenantId).IsEqualTo(tenantId.ToString("D"));
    }

    [Test]
    public async Task ProviderBindingHalEmitsMappingsOnlyForAuthorizedDraftBindings()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        Guid bindingId = Guid.CreateVersion7();
        var draft = new RegistrationProviderBindingDto { Id = bindingId, TenantId = tenantId, EventId = eventId, StateId = (int)RegistrationProviderBindingStateEnum.Draft };
        var published = new RegistrationProviderBindingDto { Id = Guid.CreateVersion7(), TenantId = tenantId, EventId = eventId, StateId = (int)RegistrationProviderBindingStateEnum.Published };
        IHateoasAuthorizationEvaluator evaluator = Substitute.For<IHateoasAuthorizationEvaluator>();
        evaluator.AreLinksAllowedAsync(Arg.Any<IReadOnlyList<LinkDefinition>>(), Arg.Any<ClaimsPrincipal?>(), Arg.Any<HttpContext>())
            .Returns(call => (IReadOnlyList<bool>)call.Arg<IReadOnlyList<LinkDefinition>>()
                .Select(link => link.PermissionAction == AuthorizationActions.Events.ManageRegistrationChannels)
                .ToArray());
        IHateoasLinkGenerator generator = Substitute.For<IHateoasLinkGenerator>();
        generator.GenerateLink(Arg.Any<LinkDefinition>(), Arg.Any<HttpContext>())
            .Returns(call => new HalLink { Href = $"/{call.Arg<LinkDefinition>().Rel}" });
        var services = new ServiceCollection().AddSingleton(evaluator).BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        var assembler = new RegistrationProviderBindingResourceAssembler(
            generator,
            new RegistrationProviderBindingLinkPolicy(),
            new RegistrationProviderBindingCollectionLinkPolicy());

        HalResource<RegistrationProviderBindingDto> draftResource = await assembler.ToResource(draft, context);
        HalResource<RegistrationProviderBindingDto> publishedResource = await assembler.ToResource(published, context);

        await Assert.That(draftResource.Links.ContainsKey(LinkRelations.Mappings)).IsTrue();
        await Assert.That(draftResource.Links[LinkRelations.Mappings].Href).IsEqualTo("/mappings");
        await Assert.That(publishedResource.Links.ContainsKey(LinkRelations.Mappings)).IsFalse();
    }

    [Test]
    public async Task ProviderBindingHalOmitsMappingsWhenManageAuthorizationDenies()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        var draft = new RegistrationProviderBindingDto { Id = Guid.CreateVersion7(), TenantId = tenantId, EventId = eventId, StateId = (int)RegistrationProviderBindingStateEnum.Draft };
        IHateoasAuthorizationEvaluator evaluator = Substitute.For<IHateoasAuthorizationEvaluator>();
        evaluator.AreLinksAllowedAsync(Arg.Any<IReadOnlyList<LinkDefinition>>(), Arg.Any<ClaimsPrincipal?>(), Arg.Any<HttpContext>())
            .Returns(call => (IReadOnlyList<bool>)call.Arg<IReadOnlyList<LinkDefinition>>().Select(_ => false).ToArray());
        IHateoasLinkGenerator generator = Substitute.For<IHateoasLinkGenerator>();
        generator.GenerateLink(Arg.Any<LinkDefinition>(), Arg.Any<HttpContext>()).Returns(new HalLink { Href = "/denied" });
        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().AddSingleton(evaluator).BuildServiceProvider() };
        var assembler = new RegistrationProviderBindingResourceAssembler(generator, new RegistrationProviderBindingLinkPolicy(), new RegistrationProviderBindingCollectionLinkPolicy());

        HalResource<RegistrationProviderBindingDto> resource = await assembler.ToResource(draft, context);

        await Assert.That(resource.Links.ContainsKey(LinkRelations.Mappings)).IsFalse();
    }

    [Test]
    public async Task ProviderMappingRouteAndDtosAreStructuredWithoutRawJsonArtifact()
    {
        var route = typeof(RegistrationProviderManagementController)
            .GetMethod(nameof(RegistrationProviderManagementController.ReplaceMappings))!
            .GetCustomAttributes(typeof(HttpPutAttribute), inherit: true)
            .Cast<HttpPutAttribute>()
            .Single();

        await Assert.That(route.Name).IsEqualTo(RouteNames.ReplaceRegistrationProviderMappings);
        await Assert.That(route.Template).IsEqualTo("bindings/{bindingId:guid}/mappings");
        await Assert.That(typeof(ReplaceRegistrationProviderMappingsRequestDto).GetProperty("MappingArtifact")).IsNull();
        await Assert.That(typeof(ReplaceRegistrationProviderMappingsRequestDto).GetProperty("AdditionalProperties")).IsNull();
        await Assert.That(typeof(RegistrationProviderBindingDto).GetProperty(nameof(RegistrationProviderBindingDto.FieldMappings))).IsNotNull();
        await Assert.That(typeof(RegistrationProviderBindingDto).GetProperty(nameof(RegistrationProviderBindingDto.OptionMappings))).IsNotNull();
    }

    [Test]
    public async Task ProviderChannelCollectionHal_IncludesWorkflowRequirementLineage()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        Guid workflowId = Guid.CreateVersion7();
        Guid requirementId = Guid.CreateVersion7();

        LinkDefinition create = new RegistrationChannelCollectionLinkPolicy()
            .GetCollectionLinks(new ClaimsPrincipal(new ClaimsIdentity("test")), new RegistrationProviderChannelCollectionContext(tenantId, eventId, workflowId, requirementId))
            .Single(candidate => candidate.Rel == LinkRelations.ProviderCreate);
        var route = new RouteValueDictionary(create.RouteValues);

        await Assert.That(create.RouteName).IsEqualTo(RouteNames.CreateRegistrationChannel);
        await Assert.That(route["tenantId"]).IsEqualTo(tenantId);
        await Assert.That(route["eventId"]).IsEqualTo(eventId);
        await Assert.That(route["workflowId"]).IsEqualTo(workflowId);
        await Assert.That(route["requirementId"]).IsEqualTo(requirementId);
        // Channel creation authorizes against the parent event; the workflow and requirement scope which
        // channels are listed and stay in the route.
        await Assert.That(create.PermissionFacts)
            .IsEqualTo(new EventScopedAuthorizationFacts(tenantId, eventId));
    }

    [Test]
    public async Task LaunchDescriptorHal_UsesExactDescriptorSelfRouteAndNoServerProxyLink()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        Guid workflowId = Guid.CreateVersion7();
        Guid requirementId = Guid.CreateVersion7();
        Guid channelId = Guid.CreateVersion7();
        Guid bindingId = Guid.CreateVersion7();
        LinkDefinition self = new RegistrationProviderLaunchDescriptorLinkPolicy()
            .GetLinks(new RegistrationProviderLaunchDescriptorDto
            {
                TenantId = tenantId,
                EventId = eventId,
                WorkflowId = workflowId,
                RequirementId = requirementId,
                ChannelId = channelId,
                BindingId = bindingId,
                Available = true,
                Url = "https://forms.example.org/launch"
            }, new ClaimsPrincipal(new ClaimsIdentity("test")))
            .Single(link => link.Rel == LinkRelations.Self);
        var route = new RouteValueDictionary(self.RouteValues);

        await Assert.That(self.RouteName).IsEqualTo(RouteNames.GetRegistrationProviderLaunchDescriptor);
        await Assert.That(route["tenantId"]).IsEqualTo(tenantId);
        await Assert.That(route["eventId"]).IsEqualTo(eventId);
        await Assert.That(route["workflowId"]).IsEqualTo(workflowId);
        await Assert.That(route["requirementId"]).IsEqualTo(requirementId);
        await Assert.That(route["channelId"]).IsEqualTo(channelId);
        await Assert.That(route["bindingId"]).IsEqualTo(bindingId);
        await Assert.That(self.PermissionAction).IsEqualTo(AuthorizationActions.Events.ManageRegistrationChannels);
    }

    [Test]
    public async Task ChannelHal_UsesExactChannelLaunchDescriptorRoute()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        Guid workflowId = Guid.CreateVersion7();
        Guid requirementId = Guid.CreateVersion7();
        Guid channelId = Guid.CreateVersion7();
        Guid bindingId = Guid.CreateVersion7();

        LinkDefinition link = new RegistrationChannelLinkPolicy()
            .GetLinks(new RegistrationChannelDto
            {
                TenantId = tenantId,
                EventId = eventId,
                RegistrationWorkflowId = workflowId,
                RegistrationRequirementId = requirementId,
                Id = channelId,
                IsNative = false,
                RegistrationProviderBindingId = bindingId
            }, new ClaimsPrincipal(new ClaimsIdentity("test")))
            .Single(candidate => candidate.Rel == "launch-descriptor");
        var route = new RouteValueDictionary(link.RouteValues);

        await Assert.That(route["tenantId"]).IsEqualTo(tenantId);
        await Assert.That(route["eventId"]).IsEqualTo(eventId);
        await Assert.That(route["workflowId"]).IsEqualTo(workflowId);
        await Assert.That(route["requirementId"]).IsEqualTo(requirementId);
        await Assert.That(route["channelId"]).IsEqualTo(channelId);
        await Assert.That(route["bindingId"]).IsEqualTo(bindingId);
    }

    [Test]
    public async Task LaunchDescriptorRouteContract_IncludesChannelLineageBeforeBinding()
    {
        var route = typeof(RegistrationProviderManagementController)
            .GetMethod(nameof(RegistrationProviderManagementController.GetLaunchDescriptor))!
            .GetCustomAttributes(typeof(HttpGetAttribute), inherit: true)
            .Cast<HttpGetAttribute>()
            .Single();

        await Assert.That(route.Name).IsEqualTo(RouteNames.GetRegistrationProviderLaunchDescriptor);
        await Assert.That(route.Template).IsEqualTo("workflows/{workflowId:guid}/requirements/{requirementId:guid}/channels/{channelId:guid}/bindings/{bindingId:guid}/launch-descriptor");
    }

    [Test]
    public async Task ProviderManagementController_IsAuthenticatedPrivateNoStoreSurface()
    {
        Type controller = typeof(RegistrationProviderManagementController);

        await Assert.That(controller.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)).HasSingleItem();
        await Assert.That(controller.GetCustomAttributes(typeof(RouteAttribute), inherit: true)
            .Cast<RouteAttribute>()
            .Single()
            .Template).IsEqualTo("api/tenants/{tenantId:guid}/events/{eventId:guid}/registration-providers");
        foreach (string actionName in new[] { "GetHealth", "GetQueue", "PollReconciliation", "QueueManualImport", "RetryQueueItem", "ResolveQueueItem" })
        {
            var method = controller.GetMethod(actionName)!;
            await Assert.That(method.GetCustomAttributes(typeof(PrivateNoStoreAttribute), inherit: true)).HasSingleItem();
            await Assert.That(method.GetCustomAttributes(typeof(ProducesResponseTypeAttribute), inherit: true)
                .Cast<ProducesResponseTypeAttribute>()
                .Select(attribute => attribute.StatusCode)).Contains(StatusCodes.Status401Unauthorized);
            await Assert.That(method.GetCustomAttributes(typeof(ProducesResponseTypeAttribute), inherit: true)
                .Cast<ProducesResponseTypeAttribute>()
                .Select(attribute => attribute.StatusCode)).Contains(StatusCodes.Status403Forbidden);
        }
    }
}

using System.Security.Claims;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Assemblers;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventRoleAssignment;
using Explore.Application.Features.EventRoleAssignments.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features.Hateoas;

public sealed class EventTeamHateoasTests
{
    [Test]
    public async Task GetTeam_DispatchesScopedQueryAndReturnsCanonicalHalCollection()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        EventTeamMemberDto member = Member(tenantId, eventId);
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetEventTeamListRequest>(), Arg.Any<CancellationToken>())
            .Returns([member]);
        var assembler = Substitute.For<IResourceAssembler<EventTeamMemberDto, EventTeamMemberDto>>();
        var collection = new HalCollectionResource<EventTeamMemberDto>();
        assembler.ToCollectionResource(
                Arg.Any<IEnumerable<EventTeamMemberDto>>(),
                RouteNames.GetEventTeam,
                Arg.Any<EventTeamCollectionAuthorizationContext>(),
                Arg.Any<HttpContext>())
            .Returns(collection);
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenantId);
        var controller = new EventTeamController(
            mediator,
            Substitute.For<IAdminContext>(),
            tenantContext,
            assembler)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        ActionResult<HalCollectionResource<EventTeamMemberDto>> result = await controller.GetTeam(eventId);

        await Assert.That((result.Result as OkObjectResult)?.Value).IsEqualTo(collection);
        _ = mediator.Received(1).Send(
            Arg.Is<GetEventTeamListRequest>(query =>
                query.TenantId == tenantId && query.EventId == eventId && !query.IncludeInactive),
            Arg.Any<CancellationToken>());
        _ = assembler.Received(1).ToCollectionResource(
            Arg.Is<IEnumerable<EventTeamMemberDto>>(items => items.Single() == member),
            RouteNames.GetEventTeam,
            Arg.Is<EventTeamCollectionAuthorizationContext>(context =>
                context.TenantId == tenantId && context.EventId == eventId),
            Arg.Any<HttpContext>());
    }

    [Test]
    public async Task CollectionResource_WhenManageTeamAllowed_ExposesAssignAndRevokeWithSameCapabilityContext()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        var checks = new List<AuthorizationRequest>();
        EventTeamMemberDto member = Member(tenantId, eventId);
        TestAssembler assembler = CreateAssembler(
            check =>
            {
                checks.Add(check);
                return IsManageTeamCheck(check, tenantId, eventId);
            },
            tenantId,
            eventId);

        var resource = await assembler.ToCollectionResource(
            [member],
            RouteNames.GetEventTeam,
            new EventTeamCollectionAuthorizationContext(tenantId, eventId),
            HttpContext());

        await Assert.That(LinkRelations.AssignEventRole).IsEqualTo("assign-event-role");
        await Assert.That(resource.Links.ContainsKey(LinkRelations.AssignEventRole)).IsTrue();
        await Assert.That(resource.Embedded!.Items.Single().Links.ContainsKey(LinkRelations.Revoke)).IsTrue();
        await Assert.That(checks.Count).IsEqualTo(2);
        await Assert.That(checks.All(check => IsManageTeamCheck(check, tenantId, eventId))).IsTrue();
    }

    [Test]
    public async Task CollectionResource_WhenManageTeamDenied_HidesAssignAndRevoke()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        TestAssembler assembler = CreateAssembler(_ => false, tenantId, eventId);

        var resource = await assembler.ToCollectionResource(
            [Member(tenantId, eventId)],
            RouteNames.GetEventTeam,
            new EventTeamCollectionAuthorizationContext(tenantId, eventId),
            HttpContext());

        await Assert.That(resource.Links.ContainsKey(LinkRelations.AssignEventRole)).IsFalse();
        await Assert.That(resource.Embedded!.Items.Single().Links.ContainsKey(LinkRelations.Revoke)).IsFalse();
    }

    [Test]
    public async Task Policies_WhenEventFactsAreMissing_EmitNoMutationCandidates()
    {
        var collectionPolicy = new EventTeamMemberCollectionLinkPolicy();
        EventTeamMemberDto member = Member(Guid.Empty, Guid.Empty);

        var collectionLinks = collectionPolicy.GetCollectionLinks(
            user: null,
            new EventTeamCollectionAuthorizationContext(Guid.Empty, Guid.Empty));
        var itemLinks = collectionPolicy.GetItemLinks(member, user: null);

        await Assert.That(collectionLinks).IsEmpty();
        await Assert.That(itemLinks).IsEmpty();
    }

    [Test]
    public async Task CollectionResource_WhenItemTenantDoesNotMatchRequest_HidesRevoke()
    {
        Guid requestTenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        TestAssembler assembler = CreateAssembler(
            check => IsManageTeamCheck(check, requestTenantId, eventId),
            requestTenantId,
            eventId);

        var resource = await assembler.ToCollectionResource(
            [Member(Guid.CreateVersion7(), eventId)],
            RouteNames.GetEventTeam,
            new EventTeamCollectionAuthorizationContext(requestTenantId, eventId),
            HttpContext());

        await Assert.That(resource.Links.ContainsKey(LinkRelations.AssignEventRole)).IsTrue();
        await Assert.That(resource.Embedded!.Items.Single().Links.ContainsKey(LinkRelations.Revoke)).IsFalse();
    }

    [Test]
    public async Task CollectionPolicy_SuppressesRevokeForOwnerAndInactiveAssignments()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        var policy = new EventTeamMemberCollectionLinkPolicy();
        EventTeamMemberDto owner = Member(tenantId, eventId, roleId: (int)RoleEnum.EventOwner);
        EventTeamMemberDto inactive = Member(tenantId, eventId, isEffective: false);

        await Assert.That(policy.GetItemLinks(owner, null)).IsEmpty();
        await Assert.That(policy.GetItemLinks(inactive, null)).IsEmpty();
    }

    [Test]
    public async Task EmptyCollection_UsesTrustedEventFactsForAssignAffordance()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        TestAssembler assembler = CreateAssembler(
            check => IsManageTeamCheck(check, tenantId, eventId),
            tenantId,
            eventId);

        var resource = await assembler.ToCollectionResource(
            [],
            RouteNames.GetEventTeam,
            new EventTeamCollectionAuthorizationContext(tenantId, eventId),
            HttpContext());

        await Assert.That(resource.Links.ContainsKey(LinkRelations.AssignEventRole)).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task EventTeamHal_MissingOrWrongTenantEventFailsClosed(bool wrongTenant)
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        IEventRepository repository = Substitute.For<IEventRepository>();
        ITenantContext tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenantId);
        IReadOnlyList<Explore.Domain.Event> targets = wrongTenant
            ? [AuthorizationEvent(Guid.CreateVersion7(), eventId)]
            : [];
        repository.GetAuthorizationTargetsByIdsAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(targets);
        IAuthorizationProvider provider = Substitute.For<IAuthorizationProvider>();
        var evaluator = new HateoasAuthorizationEvaluator(
            provider,
            repository,
            tenantContext,
            Substitute.For<ILogger<HateoasAuthorizationEvaluator>>());
        LinkDefinition link = new EventTeamMemberCollectionLinkPolicy()
            .GetCollectionLinks(null, new EventTeamCollectionAuthorizationContext(tenantId, eventId))
            .Single();

        IReadOnlyList<bool> allowed = await evaluator.AreLinksAllowedAsync([link], HttpContext().User, HttpContext());

        await Assert.That(allowed).IsEquivalentTo([false]);
        await provider.DidNotReceive().AuthorizeBatchAsync(
            Arg.Any<IReadOnlyList<AuthorizationRequest>>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task EventTeamMember_HiddenAuthorizationContextIsNotSerialized()
    {
        EventTeamMemberDto member = Member(Guid.CreateVersion7(), Guid.CreateVersion7());

        string json = JsonSerializer.Serialize(member);

        await Assert.That(json.Contains("\"tenantId\"", StringComparison.OrdinalIgnoreCase)).IsFalse();
        await Assert.That(json.Contains("\"eventId\"", StringComparison.OrdinalIgnoreCase)).IsFalse();
    }

    private static TestAssembler CreateAssembler(
        Func<AuthorizationRequest, bool> predicate,
        Guid tenantId,
        Guid eventId)
    {
        IEventRepository repository = Substitute.For<IEventRepository>();
        repository.GetAuthorizationTargetsByIdsAsync(
                Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(eventId)),
                Arg.Any<CancellationToken>())
            .Returns([AuthorizationEvent(tenantId, eventId)]);
        ITenantContext tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenantId);
        var evaluator = new HateoasAuthorizationEvaluator(
            new StubAuthorizationProvider { CheckPredicate = predicate },
            repository,
            tenantContext,
            Substitute.For<ILogger<HateoasAuthorizationEvaluator>>());
        var linkGenerator = Substitute.For<IHateoasLinkGenerator>();
        linkGenerator.GeneratePath(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<HttpContext>())
            .Returns(call => $"/{call.ArgAt<string>(0)}");
        linkGenerator.GenerateLink(Arg.Any<LinkDefinition>(), Arg.Any<HttpContext>())
            .Returns(call => new HalLink
            {
                Href = $"/{call.Arg<LinkDefinition>().Rel}",
                Method = call.Arg<LinkDefinition>().Method,
                Title = call.Arg<LinkDefinition>().Title
            });

        var assembler = new HalResourceAssembler<EventTeamMemberDto, EventTeamMemberDto>(
            linkGenerator,
            new EventTeamMemberDetailLinkPolicy(),
            new EventTeamMemberCollectionLinkPolicy());
        return new TestAssembler(assembler, evaluator);
    }

    private static DefaultHttpContext HttpContext()
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString("D"))],
            "Test"));
        return context;
    }

    private static EventTeamMemberDto Member(
        Guid tenantId,
        Guid eventId,
        int roleId = 42,
        bool isEffective = true) => new()
        {
            TenantId = tenantId,
            EventId = eventId,
            AssignmentId = Guid.CreateVersion7(),
            UserId = Guid.CreateVersion7(),
            UserEmail = "member@example.test",
            UserFullName = "Event Team Member",
            RoleId = roleId,
            RoleName = "Event Manager",
            RoleMasterCode = "EVENT_MANAGER",
            Status = EventRoleAssignmentStatus.Active,
            StartsAtUtc = DateTime.UtcNow.AddMinutes(-1),
            IsEffective = isEffective,
            CreatedAt = DateTime.UtcNow.AddMinutes(-2)
        };

    private static bool IsManageTeamCheck(AuthorizationRequest check, Guid tenantId, Guid eventId) =>
        check.ResourceKind == ResourceKinds.Event
        && check.Action == AuthorizationActions.Events.ManageTeam
        && check.ResourceId == eventId.ToString("D")
        && check.Scope?.TenantId == tenantId.ToString("D")
        && check.Facts is EventAuthorizationFacts facts
        && facts.TenantId == tenantId
        && facts.EventId == eventId;

    private static Explore.Domain.Event AuthorizationEvent(Guid tenantId, Guid eventId)
    {
        var actor = new Explore.Domain.Actor
        {
            Id = Guid.CreateVersion7(),
            Pii = new Explore.Domain.ActorPii { DisplayName = "Contributor" },
            ActorTypeId = 1,
            ActorType = null!,
            UserId = Guid.CreateVersion7()
        };
        return new Explore.Domain.Event
        {
            Id = eventId,
            TenantId = tenantId,
            Tenant = null!,
            Title = "Authorization target",
            ActorId = actor.Id,
            Actor = actor,
            EventStatus = null!,
            EventFormat = null!,
            VisibilityType = null!
        };
    }

    private sealed class TestAssembler(
        HalResourceAssembler<EventTeamMemberDto, EventTeamMemberDto> assembler,
        IHateoasAuthorizationEvaluator evaluator)
    {
        public Task<HalCollectionResource<EventTeamMemberDto>> ToCollectionResource(
            IEnumerable<EventTeamMemberDto> items,
            string routeName,
            object additionalRouteValues,
            DefaultHttpContext context)
        {
            var services = new ServiceCollection();
            services.AddSingleton(evaluator);
            context.RequestServices = services.BuildServiceProvider();
            return assembler.ToCollectionResource(items, routeName, additionalRouteValues, context);
        }
    }
}

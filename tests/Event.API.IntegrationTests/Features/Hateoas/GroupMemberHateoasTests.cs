using System.Security.Claims;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Assemblers;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.GroupMember;
using Explore.Application.Hateoas;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features.Hateoas;

public sealed class GroupMemberHateoasTests
{
    [Test]
    public async Task CollectionItemLinks_ShouldExposeEditAndDeleteAffordances()
    {
        var memberId = Guid.NewGuid();
        var dto = CreateMember(memberId);
        var policy = new GroupMemberCollectionLinkPolicy();

        var links = policy.GetItemLinks(dto, user: null).ToList();

        var editLink = links.Single(link => link.Rel == LinkRelations.Edit);
        await Assert.That(editLink.RouteName).IsEqualTo(RouteNames.UpdateGroupMember);
        await Assert.That(editLink.Method).IsEqualTo("PUT");
        await Assert.That(editLink.Title).IsEqualTo("Update membership");
        await Assert.That(editLink.RequiresAuth).IsTrue();
        await Assert.That(editLink.PermissionResourceKind).IsEqualTo(ResourceKinds.GroupMember);
        await Assert.That(editLink.PermissionAction).IsEqualTo(AuthorizationActions.Update);
        await Assert.That(GetRouteValue<Guid>(editLink.RouteValues, "id")).IsEqualTo(memberId);

        var deleteLink = links.Single(link => link.Rel == LinkRelations.Delete);
        await Assert.That(deleteLink.RouteName).IsEqualTo(RouteNames.DeleteGroupMember);
        await Assert.That(deleteLink.Method).IsEqualTo("DELETE");
        await Assert.That(deleteLink.Title).IsEqualTo("Remove member");
        await Assert.That(deleteLink.RequiresAuth).IsTrue();
        await Assert.That(deleteLink.PermissionResourceKind).IsEqualTo(ResourceKinds.GroupMember);
        await Assert.That(deleteLink.PermissionAction).IsEqualTo(AuthorizationActions.Delete);
        await Assert.That(GetRouteValue<Guid>(deleteLink.RouteValues, "id")).IsEqualTo(memberId);
    }

    [Test]
    public async Task CollectionPolicy_ShouldNotExposeUnscopedCreateAffordance()
    {
        var policy = new GroupMemberCollectionLinkPolicy();

        var links = policy.GetCollectionLinks(user: null).ToList();

        await Assert.That(links.Any(link => link.Rel == LinkRelations.Create)).IsFalse();
    }

    [Test]
    public async Task CollectionResource_GroupAdmin_ShouldExposeScopedCreateEditAndDeleteAffordances()
    {
        var groupId = Guid.NewGuid();
        var member = CreateMember(Guid.NewGuid(), groupId);
        var assembler = CreateAssembler(check => IsGroupMemberActionForGroup(check, groupId));
        var context = CreateHttpContext(authenticated: true, assembler.LinkGenerator);

        var resource = await assembler.ToCollectionResource([member], RouteNames.GetGroupMembers, new { groupId }, context);
        var item = resource.Embedded!.Items.Single();

        await Assert.That(resource.Links.ContainsKey(LinkRelations.Create)).IsTrue();
        await Assert.That(resource.Links[LinkRelations.Create].Href).IsEqualTo("/create");
        await Assert.That(item.Links.ContainsKey(LinkRelations.Edit)).IsTrue();
        await Assert.That(item.Links.ContainsKey(LinkRelations.Delete)).IsTrue();
    }

    [Test]
    public async Task CollectionResource_RegularMember_ShouldHideCreateEditAndDeleteAffordances()
    {
        var groupId = Guid.NewGuid();
        var member = CreateMember(Guid.NewGuid(), groupId);
        var assembler = CreateAssembler(_ => false);
        var context = CreateHttpContext(authenticated: true, assembler.LinkGenerator);

        var resource = await assembler.ToCollectionResource([member], RouteNames.GetGroupMembers, new { groupId }, context);
        var item = resource.Embedded!.Items.Single();

        await Assert.That(resource.Links.ContainsKey(LinkRelations.Create)).IsFalse();
        await Assert.That(item.Links.ContainsKey(LinkRelations.Edit)).IsFalse();
        await Assert.That(item.Links.ContainsKey(LinkRelations.Delete)).IsFalse();
    }

    [Test]
    public async Task CollectionResource_AuthenticatedNonMember_ShouldHideCreateEditAndDeleteAffordances()
    {
        var groupId = Guid.NewGuid();
        var member = CreateMember(Guid.NewGuid(), groupId);
        var assembler = CreateAssembler(_ => false);
        var context = CreateHttpContext(authenticated: true, assembler.LinkGenerator);

        var resource = await assembler.ToCollectionResource([member], RouteNames.GetGroupMembers, new { groupId }, context);
        var item = resource.Embedded!.Items.Single();

        await Assert.That(resource.Links.ContainsKey(LinkRelations.Create)).IsFalse();
        await Assert.That(item.Links.ContainsKey(LinkRelations.Edit)).IsFalse();
        await Assert.That(item.Links.ContainsKey(LinkRelations.Delete)).IsFalse();
    }

    [Test]
    public async Task CollectionResource_AnonymousUser_ShouldHideCreateEditAndDeleteAffordances()
    {
        var groupId = Guid.NewGuid();
        var member = CreateMember(Guid.NewGuid(), groupId);
        var assembler = CreateAssembler(_ => true);
        var context = CreateHttpContext(authenticated: false, assembler.LinkGenerator);

        var resource = await assembler.ToCollectionResource([member], RouteNames.GetGroupMembers, new { groupId }, context);
        var item = resource.Embedded!.Items.Single();

        await Assert.That(resource.Links.ContainsKey(LinkRelations.Create)).IsFalse();
        await Assert.That(item.Links.ContainsKey(LinkRelations.Edit)).IsFalse();
        await Assert.That(item.Links.ContainsKey(LinkRelations.Delete)).IsFalse();
    }

    private static bool IsGroupMemberActionForGroup(AuthorizationRequest check, Guid groupId)
    {
        if (check.ResourceKind != ResourceKinds.GroupMember)
        {
            return false;
        }

        if (check.Action is not (AuthorizationActions.Create or AuthorizationActions.Update or AuthorizationActions.Delete))
        {
            return false;
        }

        return check.ResourceId == groupId.ToString()
            || (check.Facts is GroupAuthorizationFacts groupFacts && groupFacts.GroupId == groupId)
            || (check.Facts is GroupMemberAuthorizationFacts memberFacts && memberFacts.GroupId == groupId);
    }

    private static TestAssembler CreateAssembler(Func<AuthorizationRequest, bool> predicate)
    {
        var authorizationProvider = new StubAuthorizationProvider { CheckPredicate = predicate };
        var evaluator = new HateoasAuthorizationEvaluator(
            authorizationProvider,
            Substitute.For<Explore.Application.Contracts.Persistence.IEventRepository>(),
            Substitute.For<ITenantContext>(),
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

        var assembler = new GroupMemberResourceAssembler(
            linkGenerator,
            new GroupMemberDetailLinkPolicy(),
            new GroupMemberCollectionLinkPolicy());

        return new TestAssembler(assembler, evaluator, linkGenerator);
    }

    private static DefaultHttpContext CreateHttpContext(bool authenticated, IHateoasLinkGenerator linkGenerator)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IHateoasLinkGenerator>());
        services.AddSingleton<IHateoasAuthorizationEvaluator>(sp => CurrentEvaluator.Value!);
        var serviceProvider = services.BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = serviceProvider };
        context.User = authenticated
            ? new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "Test"))
            : new ClaimsPrincipal(new ClaimsIdentity());
        return context;
    }

    private static readonly AsyncLocal<IHateoasAuthorizationEvaluator?> CurrentEvaluator = new();

    private static GroupMemberDto CreateMember(Guid memberId, Guid? groupId = null)
    {
        return new GroupMemberDto
        {
            Id = memberId,
            GroupId = groupId ?? Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            UserEmail = "member@example.com",
            UserFullName = "Group Member",
            RoleId = 2,
            RoleName = "Admin"
        };
    }

    private static T? GetRouteValue<T>(object? routeValues, string name)
    {
        if (routeValues is null)
        {
            return default;
        }

        var property = routeValues.GetType().GetProperty(name);
        var value = property?.GetValue(routeValues);
        return value is T typedValue ? typedValue : default;
    }

    private sealed class TestAssembler
    {
        private readonly GroupMemberResourceAssembler _assembler;
        public TestAssembler(
            GroupMemberResourceAssembler assembler,
            IHateoasAuthorizationEvaluator evaluator,
            IHateoasLinkGenerator linkGenerator)
        {
            _assembler = assembler;
            Evaluator = evaluator;
            LinkGenerator = linkGenerator;
        }

        public IHateoasAuthorizationEvaluator Evaluator { get; }
        public IHateoasLinkGenerator LinkGenerator { get; }

        public async Task<HalCollectionResource<GroupMemberDto>> ToCollectionResource(
            IEnumerable<GroupMemberDto> items,
            string routeName,
            object? additionalRouteValues,
            HttpContext context)
        {
            CurrentEvaluator.Value = Evaluator;
            try
            {
                return await _assembler.ToCollectionResource(items, routeName, additionalRouteValues, context);
            }
            finally
            {
                CurrentEvaluator.Value = null;
            }
        }
    }
}

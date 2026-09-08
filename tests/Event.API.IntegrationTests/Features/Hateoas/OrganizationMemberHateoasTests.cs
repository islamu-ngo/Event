using System.Security.Claims;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Assemblers;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.OrganizationMember;
using Explore.Application.Hateoas;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features.Hateoas;

public sealed class OrganizationMemberHateoasTests
{
    [Test]
    public async Task CollectionItemLinks_ShouldExposeEditAndDeleteAffordances()
    {
        var memberId = Guid.NewGuid();
        var dto = CreateMember(memberId);
        var policy = new OrganizationMemberCollectionLinkPolicy();

        var links = policy.GetItemLinks(dto, user: null).ToList();

        var editLink = links.Single(link => link.Rel == LinkRelations.Edit);
        await Assert.That(editLink.RouteName).IsEqualTo(RouteNames.UpdateOrganizationMemberRole);
        await Assert.That(editLink.Method).IsEqualTo("PUT");
        await Assert.That(editLink.RequiresAuth).IsTrue();
        await Assert.That(editLink.PermissionResourceKind).IsEqualTo(ResourceKinds.OrganizationMember);
        await Assert.That(editLink.PermissionAction).IsEqualTo(AuthorizationActions.Update);
        await Assert.That(GetRouteValue<Guid>(editLink.RouteValues, "id")).IsEqualTo(memberId);

        var deleteLink = links.Single(link => link.Rel == LinkRelations.Delete);
        await Assert.That(deleteLink.RouteName).IsEqualTo(RouteNames.DeleteOrganizationMember);
        await Assert.That(deleteLink.Method).IsEqualTo("DELETE");
        await Assert.That(deleteLink.RequiresAuth).IsTrue();
        await Assert.That(deleteLink.PermissionResourceKind).IsEqualTo(ResourceKinds.OrganizationMember);
        await Assert.That(deleteLink.PermissionAction).IsEqualTo(AuthorizationActions.Delete);
        await Assert.That(GetRouteValue<Guid>(deleteLink.RouteValues, "id")).IsEqualTo(memberId);
    }

    [Test]
    public async Task CollectionPolicy_ShouldNotExposeUnscopedCreateAffordance()
    {
        var policy = new OrganizationMemberCollectionLinkPolicy();

        var links = policy.GetCollectionLinks(user: null).ToList();

        await Assert.That(links.Any(link => link.Rel == LinkRelations.Create)).IsFalse();
    }

    [Test]
    public async Task CollectionResource_OrganizationAdmin_ShouldExposeScopedCreateEditAndDeleteAffordances()
    {
        var tenantId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        var member = CreateMember(Guid.NewGuid(), tenantId, organizationId);
        var assembler = CreateAssembler(check => IsOrganizationMemberActionForOrganization(check, tenantId, organizationId));
        var context = CreateHttpContext(authenticated: true);

        var resource = await assembler.ToCollectionResource(
            [member],
            RouteNames.GetOrganizationMembersByOrganization,
            new { organizationId, tenantId },
            context);
        var item = resource.Embedded!.Items.Single();

        await Assert.That(resource.Links.ContainsKey(LinkRelations.Create)).IsTrue();
        await Assert.That(item.Links.ContainsKey(LinkRelations.Edit)).IsTrue();
        await Assert.That(item.Links.ContainsKey(LinkRelations.Delete)).IsTrue();
    }

    [Test]
    public async Task CollectionResource_RegularMember_ShouldHideCreateEditAndDeleteAffordances()
    {
        var tenantId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        var member = CreateMember(Guid.NewGuid(), tenantId, organizationId);
        var assembler = CreateAssembler(_ => false);
        var context = CreateHttpContext(authenticated: true);

        var resource = await assembler.ToCollectionResource(
            [member],
            RouteNames.GetOrganizationMembersByOrganization,
            new { organizationId, tenantId },
            context);
        var item = resource.Embedded!.Items.Single();

        await Assert.That(resource.Links.ContainsKey(LinkRelations.Create)).IsFalse();
        await Assert.That(item.Links.ContainsKey(LinkRelations.Edit)).IsFalse();
        await Assert.That(item.Links.ContainsKey(LinkRelations.Delete)).IsFalse();
    }

    [Test]
    public async Task DetailPolicy_ShouldUseCurrentOrganizationMemberRoutes()
    {
        var member = CreateMember(Guid.NewGuid());
        var policy = new OrganizationMemberDetailLinkPolicy();

        var links = policy.GetLinks(member, user: null).ToList();

        await Assert.That(links.Single(link => link.Rel == LinkRelations.Self).RouteName).IsEqualTo(RouteNames.GetOrganizationMemberById);
        await Assert.That(links.Single(link => link.Rel == LinkRelations.Edit).RouteName).IsEqualTo(RouteNames.UpdateOrganizationMemberRole);
        await Assert.That(links.Single(link => link.Rel == LinkRelations.Delete).RouteName).IsEqualTo(RouteNames.DeleteOrganizationMember);
    }

    private static bool IsOrganizationMemberActionForOrganization(AuthorizationRequest check, Guid tenantId, Guid organizationId)
    {
        if (check.ResourceKind != ResourceKinds.OrganizationMember)
        {
            return false;
        }

        if (check.Action is not (AuthorizationActions.Create or AuthorizationActions.Update or AuthorizationActions.Delete))
        {
            return false;
        }

        if (check.Facts is OrganizationMemberAuthorizationFacts organizationMemberFacts
            && organizationMemberFacts.TenantId == tenantId
            && organizationMemberFacts.OrganizationId == organizationId)
        {
            return true;
        }

        return check.ResourceId == organizationId.ToString();
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

        var assembler = new OrganizationMemberResourceAssembler(
            linkGenerator,
            new OrganizationMemberDetailLinkPolicy(),
            new OrganizationMemberCollectionLinkPolicy());

        return new TestAssembler(assembler, evaluator);
    }

    private static DefaultHttpContext CreateHttpContext(bool authenticated)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHateoasAuthorizationEvaluator>(_ => CurrentEvaluator.Value!);
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.User = authenticated
            ? new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "Test"))
            : new ClaimsPrincipal(new ClaimsIdentity());
        return context;
    }

    private static readonly AsyncLocal<IHateoasAuthorizationEvaluator?> CurrentEvaluator = new();

    private static OrganizationMemberDto CreateMember(Guid memberId, Guid? tenantId = null, Guid? organizationId = null)
    {
        return new OrganizationMemberDto
        {
            Id = memberId,
            TenantId = tenantId ?? Guid.NewGuid(),
            OrganizationId = organizationId ?? Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            UserEmail = "member@example.com",
            UserFullName = "Organization Member",
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
        private readonly OrganizationMemberResourceAssembler _assembler;

        public TestAssembler(OrganizationMemberResourceAssembler assembler, IHateoasAuthorizationEvaluator evaluator)
        {
            _assembler = assembler;
            Evaluator = evaluator;
        }

        private IHateoasAuthorizationEvaluator Evaluator { get; }

        public async Task<HalCollectionResource<OrganizationMemberDto>> ToCollectionResource(
            IEnumerable<OrganizationMemberDto> items,
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

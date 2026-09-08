using System.Security.Claims;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.Footer;
using Explore.Application.Hateoas;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features.Hateoas;

public sealed class TenantFooterSettingsLinkPolicyTests
{
    [Test]
    public async Task GetLinks_WhenAuthorizedAndAllFourGovernedGroupsAreLocked_EmitsSelfAndPermissionBoundPatchEdit()
    {
        var tenantId = Guid.NewGuid();
        var dto = CreateAllLockedDto(tenantId, lockLinkGroups: false);

        var links = await GetAuthorizedLinksAsync(dto, isAuthorized: true);

        var self = links.Single(link => link.Rel == LinkRelations.Self);
        await Assert.That(self.RouteName).IsEqualTo(RouteNames.GetTenantFooterSettings);
        await Assert.That(self.Method).IsEqualTo("GET");
        var edit = links.Single(link => link.Rel == LinkRelations.Edit);
        await Assert.That(edit.RouteName).IsEqualTo(RouteNames.PatchTenantFooterSettings);
        await Assert.That(edit.Method).IsEqualTo("PATCH");
        await Assert.That(edit.PermissionResourceKind).IsEqualTo(ResourceKinds.Tenant);
        await Assert.That(edit.PermissionAction).IsEqualTo(AuthorizationActions.Update);
        await Assert.That(edit.PermissionResourceId).IsEqualTo(tenantId.ToString("D"));
        // Footer settings are tenant-administered; the setting group names the document, not an authority zone.
        await Assert.That(edit.PermissionFacts).IsEqualTo(new TenantScopedAuthorizationFacts(tenantId));
        await Assert.That(edit.PermissionScope!.TenantId).IsEqualTo(tenantId.ToString("D"));
        var manage = links.Single(link => link.Rel == "manage-link-groups");
        await Assert.That(manage.RouteName).IsEqualTo(RouteNames.GetFooterLinkGroups);
        await Assert.That(manage.Method).IsEqualTo("GET");
        await Assert.That(manage.PermissionResourceKind).IsEqualTo(ResourceKinds.Tenant);
        await Assert.That(manage.PermissionAction).IsEqualTo(AuthorizationActions.Update);
        await Assert.That(manage.PermissionScope!.TenantId).IsEqualTo(tenantId.ToString("D"));
    }

    [Test]
    public async Task GetLinks_WhenLinkGroupsAreLocked_OmitsManageRelationButKeepsEdit()
    {
        var dto = CreateAllLockedDto(Guid.NewGuid(), lockLinkGroups: true);

        var links = await GetAuthorizedLinksAsync(dto, isAuthorized: true);

        await Assert.That(links.Any(link => link.Rel == LinkRelations.Self)).IsTrue();
        await Assert.That(links.Any(link => link.Rel == LinkRelations.Edit)).IsTrue();
        await Assert.That(links.Any(link => link.Rel == "manage-link-groups")).IsFalse();
    }

    [Test]
    public async Task GetLinks_WhenUnauthorized_EmitsSelfOnly()
    {
        var dto = CreateAllLockedDto(Guid.NewGuid(), lockLinkGroups: false);

        var links = await GetAuthorizedLinksAsync(dto, isAuthorized: false);

        await Assert.That(links.Select(link => link.Rel)).IsEquivalentTo([LinkRelations.Self]);
    }

    private static TenantFooterSettingsDto CreateAllLockedDto(Guid tenantId, bool lockLinkGroups) => new()
    {
        TenantId = tenantId,
        LockTenantTemplate = true,
        LockTenantDescription = true,
        LockTenantLinkGroups = lockLinkGroups,
        LockTenantSocialLinks = true,
        LockTenantCopyright = true
    };

    private static async Task<LinkDefinition[]> GetAuthorizedLinksAsync(
        TenantFooterSettingsDto dto,
        bool isAuthorized)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", Guid.NewGuid().ToString("D"))],
            "Test"));
        var definitions = new TenantFooterSettingsLinkPolicy().GetLinks(dto, user).ToArray();
        var authorizationProvider = Substitute.For<IAuthorizationProvider>();
        authorizationProvider.AuthorizeBatchAsync(
                Arg.Any<IReadOnlyList<AuthorizationRequest>>(),
                Arg.Any<CancellationToken>())
            .Returns([
                isAuthorized
                    ? AuthorizationDecision.Allow(AuthorizationProviderMetadata.Local)
                    : AuthorizationDecision.Deny(AuthorizationProviderMetadata.Local)
            ]);
        var evaluator = new HateoasAuthorizationEvaluator(
            authorizationProvider,
            Substitute.For<Explore.Application.Contracts.Persistence.IEventRepository>(),
            Substitute.For<ITenantContext>(),
            Substitute.For<ILogger<HateoasAuthorizationEvaluator>>());
        var httpContext = new DefaultHttpContext { User = user };
        var allowed = await evaluator.AreLinksAllowedAsync(definitions, user, httpContext);

        return definitions.Where((_, index) => allowed[index]).ToArray();
    }
}

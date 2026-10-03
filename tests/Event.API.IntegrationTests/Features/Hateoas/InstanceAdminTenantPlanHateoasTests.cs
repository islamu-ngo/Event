using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.DTOs.InstanceAdmin;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Features.InstanceAdmin.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features.Hateoas;

public sealed class InstanceAdminTenantPlanHateoasTests
{
    [Test]
    public async Task DetailLinks_ExposePlanReadAuthorizationMetadata()
    {
        var policy = new InstanceTenantPlanDetailLinkPolicy();

        var links = policy.GetLinks(CreateDetail(), user: null).ToArray();

        var self = links.Single(link => link.Rel == LinkRelations.Self);
        await Assert.That(self.RouteName).IsEqualTo(RouteNames.GetInstanceAdminTenantPlanByKey);
        await Assert.That(self.Method).IsEqualTo("GET");
        await Assert.That(self.RequiresAuth).IsTrue();
        await Assert.That(self.PermissionResourceKind).IsEqualTo(ResourceKinds.InstanceSetting);
        await Assert.That(self.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.View);
        await Assert.That(self.PermissionResourceId).IsEqualTo(GetInstanceTenantPlanListQuery.SettingKey);
        await Assert.That(self.PermissionFacts).IsEqualTo(InstanceScopedAuthorizationFacts.Instance);

        var collection = links.Single(link => link.Rel == LinkRelations.Collection);
        await Assert.That(collection.RouteName).IsEqualTo(RouteNames.GetInstanceAdminTenantPlans);
        await Assert.That(collection.PermissionResourceKind).IsEqualTo(ResourceKinds.InstanceSetting);
        await Assert.That(collection.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.View);
        await Assert.That(collection.PermissionResourceId).IsEqualTo(GetInstanceTenantPlanListQuery.SettingKey);
    }

    [Test]
    public async Task CollectionLinks_ExposeCreateDraftAuthorizationMetadata()
    {
        var policy = new InstanceTenantPlanCollectionLinkPolicy();

        var itemLinks = policy.GetItemLinks(CreateListItem(), user: null).ToArray();
        var collectionLinks = policy.GetCollectionLinks(user: null).ToArray();

        var self = itemLinks.Single(link => link.Rel == LinkRelations.Self);
        await Assert.That(self.RouteName).IsEqualTo(RouteNames.GetInstanceAdminTenantPlanByKey);
        await Assert.That(self.PermissionResourceKind).IsEqualTo(ResourceKinds.InstanceSetting);
        await Assert.That(self.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.View);
        await Assert.That(self.PermissionFacts).IsEqualTo(InstanceScopedAuthorizationFacts.Instance);

        var create = collectionLinks.Single(link => link.Rel == LinkRelations.Create);
        await Assert.That(create.RouteName).IsEqualTo(RouteNames.CreateInstanceAdminTenantPlanDraft);
        await Assert.That(create.Method).IsEqualTo("POST");
        await Assert.That(create.RequiresAuth).IsTrue();
        await Assert.That(create.PermissionResourceKind).IsEqualTo(ResourceKinds.InstanceSetting);
        await Assert.That(create.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.Update);
        await Assert.That(create.PermissionResourceId).IsEqualTo(CreateInstanceTenantPlanDraftCommand.SettingKey);
        await Assert.That(create.PermissionFacts).IsEqualTo(InstanceScopedAuthorizationFacts.Instance);

        var validate = collectionLinks.Single(link => link.Rel == "validate");
        await Assert.That(validate.RouteName).IsEqualTo(RouteNames.ValidateInstanceAdminTenantPlanDraft);
        await Assert.That(validate.Method).IsEqualTo("POST");
        await Assert.That(validate.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.View);
        await Assert.That(validate.PermissionResourceId).IsEqualTo(ValidateInstanceTenantPlanDraftQuery.SettingKey);

        var previewDiff = collectionLinks.Single(link => link.Rel == "preview-diff");
        await Assert.That(previewDiff.RouteName).IsEqualTo(RouteNames.PreviewInstanceAdminTenantPlanDiff);
        await Assert.That(previewDiff.Method).IsEqualTo("POST");
        await Assert.That(previewDiff.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.View);
        await Assert.That(previewDiff.PermissionResourceId).IsEqualTo(PreviewInstanceTenantPlanDiffQuery.SettingKey);
    }

    [Test]
    public async Task DetailLinks_KeepVersionActionsOffTheRootResource()
    {
        var policy = new InstanceTenantPlanDetailLinkPolicy();
        var detail = CreateDetail();
        var links = policy.GetLinks(detail, user: null).ToArray();

        var createVersion = links.Single(link => link.Rel == "create-version-draft");
        await Assert.That(createVersion.RouteName).IsEqualTo(RouteNames.CreateInstanceAdminTenantPlanVersionDraft);
        await Assert.That(createVersion.Method).IsEqualTo("POST");
        await Assert.That(RouteValues(createVersion)["key"]).IsEqualTo("community");
        await Assert.That(createVersion.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.Update);
        await Assert.That(createVersion.PermissionResourceId).IsEqualTo(CreateInstanceTenantPlanVersionDraftCommand.SettingKey);
        await Assert.That(createVersion.PermissionFacts).IsEqualTo(InstanceScopedAuthorizationFacts.Instance);

        await Assert.That(links.Any(link => link.Rel == "update-version-draft")).IsFalse();
        await Assert.That(links.Any(link => link.Rel == LinkRelations.Publish)).IsFalse();
        await Assert.That(links.Any(link => link.Rel == LinkRelations.Archive)).IsFalse();
        await Assert.That(links.Any(link => link.Rel == "clone")).IsFalse();

        var validate = links.Single(link => link.Rel == "validate");
        await Assert.That(validate.RouteName).IsEqualTo(RouteNames.ValidateInstanceAdminTenantPlanDraft);
        await Assert.That(validate.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.View);
        await Assert.That(validate.PermissionResourceId).IsEqualTo(ValidateInstanceTenantPlanDraftQuery.SettingKey);

        var previewDiff = links.Single(link => link.Rel == "preview-diff");
        await Assert.That(previewDiff.RouteName).IsEqualTo(RouteNames.PreviewInstanceAdminTenantPlanDiff);
        await Assert.That(previewDiff.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.View);
        await Assert.That(previewDiff.PermissionResourceId).IsEqualTo(PreviewInstanceTenantPlanDiffQuery.SettingKey);
    }

    [Test]
    public async Task VersionLinks_AreStateQualifiedAndMaterializedPerVersion()
    {
        InstanceTenantPlanDetailDto detail = CreateDetail();
        var draft = new InstanceTenantPlanVersionDto
        {
            Id = Guid.NewGuid(),
            VersionNumber = 2,
            StatusId = (int)TenantPlanStatusEnum.Draft,
            StatusCode = "DRAFT"
        };
        detail = detail with { Versions = [draft, detail.Versions.Single()] };

        IHateoasAuthorizationEvaluator evaluator = Substitute.For<IHateoasAuthorizationEvaluator>();
        evaluator.AreLinksAllowedAsync(
                Arg.Any<IReadOnlyList<LinkDefinition>>(),
                Arg.Any<System.Security.Claims.ClaimsPrincipal?>(),
                Arg.Any<HttpContext>())
            .Returns(call => Task.FromResult<IReadOnlyList<bool>>(
                call.ArgAt<IReadOnlyList<LinkDefinition>>(0).Select(_ => true).ToArray()));
        IHateoasLinkGenerator linkGenerator = Substitute.For<IHateoasLinkGenerator>();
        linkGenerator.GenerateLink(Arg.Any<LinkDefinition>(), Arg.Any<HttpContext>())
            .Returns(call =>
            {
                LinkDefinition definition = call.ArgAt<LinkDefinition>(0);
                RouteValueDictionary values = RouteValues(definition);
                Guid? id = values.TryGetValue("versionId", out object? versionId)
                    ? (Guid)versionId!
                    : values.TryGetValue("sourceVersionId", out object? sourceVersionId)
                        ? (Guid)sourceVersionId!
                        : null;
                return new HalLink
                {
                    Href = id.HasValue
                        ? $"/plans/versions/{id.Value:D}/{definition.Rel}"
                        : $"/plans/{definition.Rel}",
                    Method = definition.Method,
                    Title = definition.Title
                };
            });
        var services = new ServiceCollection();
        services.AddSingleton(evaluator);
        using ServiceProvider provider = services.BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = provider };
        var assembler = new Explore.API.Hateoas.Assemblers.InstanceTenantPlanResourceAssembler(
            linkGenerator,
            new InstanceTenantPlanDetailLinkPolicy(),
            new InstanceTenantPlanCollectionLinkPolicy());

        HalResource<InstanceTenantPlanDetailDto> resource = await assembler.ToResource(detail, context);

        await Assert.That(resource.Links.Keys.Any(relation => relation is "update-version-draft" or "publish" or "archive" or "clone")).IsFalse();
        await Assert.That(draft.Links?.Keys).IsEquivalentTo(["publish", "update-version-draft"]);
        await Assert.That(draft.Links!.Values.All(link => link.Href.Contains(draft.Id.ToString("D"), StringComparison.Ordinal))).IsTrue();
        InstanceTenantPlanVersionDto published = detail.Versions.Single(version => version.StatusId == (int)TenantPlanStatusEnum.Published);
        await Assert.That(published.Links?.Keys).IsEquivalentTo(["archive", "clone"]);
        await Assert.That(published.Links!.Values.All(link => link.Href.Contains(published.Id.ToString("D"), StringComparison.Ordinal))).IsTrue();
    }

    private static InstanceTenantPlanDetailDto CreateDetail() => new()
    {
        Id = Guid.NewGuid(),
        Key = "community",
        DisplayName = "Community",
        Versions =
        [
            new InstanceTenantPlanVersionDto
            {
                Id = Guid.NewGuid(),
                VersionNumber = 1,
                StatusId = (int)TenantPlanStatusEnum.Published,
                StatusCode = "PUBLISHED",
                PriceAmount = 29m,
                CurrencyCode = "EUR",
                BillingPeriod = "monthly",
                IsActiveForProvisioning = true
            }
        ]
    };

    private static InstanceTenantPlanListItemDto CreateListItem() => new()
    {
        Id = Guid.NewGuid(),
        Key = "community",
        DisplayName = "Community",
        LatestVersionNumber = 1,
        PublishedVersionNumber = 1,
        PriceAmount = 29m,
        CurrencyCode = "EUR",
        BillingPeriod = "monthly",
        IsActiveForProvisioning = true
    };

    private static RouteValueDictionary RouteValues(LinkDefinition link) => new(link.RouteValues);
}

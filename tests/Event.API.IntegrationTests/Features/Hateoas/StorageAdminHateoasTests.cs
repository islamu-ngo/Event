using System.Security.Claims;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Assemblers;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Hateoas;
using Explore.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features.Hateoas;

public sealed class StorageAdminHateoasTests
{
    [Test]
    public async Task StorageObjectDetailLinks_ExposeContentAndPermissionBoundMutations()
    {
        var tenantId = Guid.CreateVersion7();
        var dto = CreateStorageObject(tenantId);
        var policy = new StorageObjectDetailLinkPolicy();

        var links = policy.GetLinks(dto, user: null).ToArray();

        await Assert.That(links.Single(link => link.Rel == LinkRelations.Self).RouteName)
            .IsEqualTo(RouteNames.GetStorageObjectById);
        var content = links.Single(link => link.Rel == "content");
        await Assert.That(content.RouteName).IsEqualTo(RouteNames.GetStorageObjectContent);
        await Assert.That(content.RequiresAuth).IsTrue();
        await Assert.That(content.PermissionResourceKind).IsEqualTo(ResourceKinds.StorageObject);
        await Assert.That(content.PermissionAction).IsEqualTo(AuthorizationActions.StorageObjects.Download);

        await Assert.That(links.Single(link => link.Rel == "public-image").RouteName)
            .IsEqualTo(RouteNames.GetPublicStorageObjectImage);

        var presigned = links.Single(link => link.Rel == "presigned-download");
        await Assert.That(presigned.RequiresAuth).IsTrue();
        await Assert.That(presigned.PermissionAction).IsEqualTo(AuthorizationActions.StorageObjects.PresignedDownload);

        var edit = links.Single(link => link.Rel == LinkRelations.Edit);
        await Assert.That(edit.RouteName).IsEqualTo(RouteNames.UpdateStorageObject);
        await Assert.That(edit.RequiresAuth).IsTrue();
        await Assert.That(edit.PermissionResourceKind).IsEqualTo(ResourceKinds.StorageObject);
        await Assert.That(edit.PermissionAction).IsEqualTo(AuthorizationActions.StorageObjects.Update);
        await Assert.That(edit.PermissionResourceId).IsEqualTo(dto.Id.ToString());
        // Storage-object mutations are decided on the persisted object, so its visibility and lifecycle
        // -- not the caller's claim about them -- are what reaches the provider.
        await Assert.That(edit.PermissionFacts).IsTypeOf<PersistedStorageObjectAuthorizationFacts>();

        var delete = links.Single(link => link.Rel == LinkRelations.Delete);
        await Assert.That(delete.RouteName).IsEqualTo(RouteNames.DeleteStorageObject);
        await Assert.That(delete.RequiresAuth).IsTrue();
        await Assert.That(delete.PermissionResourceKind).IsEqualTo(ResourceKinds.StorageObject);
        await Assert.That(delete.PermissionAction).IsEqualTo(AuthorizationActions.StorageObjects.Delete);
    }

    [Test]
    public async Task StorageObjectCollectionLinks_ExposeOnlyUploadSessionWriteAffordance()
    {
        var policy = new StorageObjectCollectionLinkPolicy();

        var links = policy.GetCollectionLinks(user: null).ToArray();

        await Assert.That(links.Any(link => link.Rel == LinkRelations.Create)).IsFalse();

        var uploadSession = links.Single(link => link.Rel == "create-upload-session");
        await Assert.That(uploadSession.RouteName).IsEqualTo(RouteNames.CreateStorageUploadSession);
        await Assert.That(uploadSession.RequiresAuth).IsTrue();
        await Assert.That(uploadSession.PermissionResourceKind).IsEqualTo(ResourceKinds.StorageObject);
        await Assert.That(uploadSession.PermissionAction).IsEqualTo(AuthorizationActions.StorageObjects.Create);
    }

    [Test]
    public async Task InstanceStorageSettingsLinks_ExposeAdminActionPermissionMetadata()
    {
        var policy = new InstanceStorageSettingsLinkPolicy();

        var links = policy.GetLinks(new InstanceStorageSettingsDto(), user: null).ToArray();

        var self = links.Single(link => link.Rel == LinkRelations.Self);
        await Assert.That(self.RouteName).IsEqualTo(RouteNames.GetInstanceStorageSettings);
        await Assert.That(self.PermissionResourceKind).IsEqualTo(ResourceKinds.InstanceSetting);
        await Assert.That(self.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.View);

        var edit = links.Single(link => link.Rel == LinkRelations.Edit);
        await Assert.That(edit.RouteName).IsEqualTo(RouteNames.UpdateInstanceStorageSettings);
        await Assert.That(edit.Method).IsEqualTo("PATCH");
        await Assert.That(edit.PermissionResourceKind).IsEqualTo(ResourceKinds.InstanceSetting);
        await Assert.That(edit.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.Update);

        var providerTest = links.Single(link => link.Rel == "provider-test");
        await Assert.That(providerTest.RouteName).IsEqualTo(RouteNames.TestInstanceStorageConnection);
        await Assert.That(providerTest.Method).IsEqualTo("POST");
        await Assert.That(providerTest.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.Update);

        var recalculate = links.Single(link => link.Rel == "recalculate-usage");
        await Assert.That(recalculate.RouteName).IsEqualTo(RouteNames.RecalculateInstanceStorageUsage);
        await Assert.That(recalculate.Method).IsEqualTo("POST");
        await Assert.That(recalculate.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.Update);
    }

    [Test]
    public async Task TenantStorageSettingsLinks_ExposeEditOnlyWhenDelegationAllowsOverrides()
    {
        var tenantId = Guid.CreateVersion7();
        var policy = new TenantStorageSettingsLinkPolicy();
        var editable = new TenantStorageSettingsDto
        {
            TenantId = tenantId,
            TenantOverridesAllowed = true,
            TenantStorageLocked = false,
            IsReadOnly = false
        };
        var locked = new TenantStorageSettingsDto
        {
            TenantId = tenantId,
            TenantOverridesAllowed = false,
            TenantStorageLocked = true,
            IsReadOnly = true
        };

        var editableLinks = policy.GetLinks(editable, user: null).ToArray();
        var lockedLinks = policy.GetLinks(locked, user: null).ToArray();

        var edit = editableLinks.Single(link => link.Rel == LinkRelations.Edit);
        await Assert.That(edit.RouteName).IsEqualTo(RouteNames.PatchTenantStorageSettings);
        await Assert.That(edit.Method).IsEqualTo("PATCH");
        await Assert.That(edit.PermissionResourceKind).IsEqualTo(ResourceKinds.TenantSetting);
        await Assert.That(edit.PermissionAction).IsEqualTo(AuthorizationActions.TenantSettings.Update);
        await Assert.That(edit.PermissionResourceId).IsEqualTo($"{tenantId}:storage");
        await Assert.That(edit.PermissionFacts)
            .IsEqualTo(new TenantSettingAuthorizationFacts(tenantId, "storage", IsLockedByInstance: false));
        await Assert.That(edit.PermissionScope!.TenantId).IsEqualTo(tenantId.ToString());

        var providerTest = editableLinks.Single(link => link.Rel == "provider-test");
        await Assert.That(providerTest.RouteName).IsEqualTo(RouteNames.TestTenantStorageConnection);
        await Assert.That(providerTest.Method).IsEqualTo("POST");
        await Assert.That(providerTest.PermissionAction).IsEqualTo(AuthorizationActions.TenantSettings.Update);

        await Assert.That(lockedLinks.Any(link => link.Rel == LinkRelations.Edit)).IsFalse();
        await Assert.That(lockedLinks.Any(link => link.Rel == "provider-test")).IsFalse();
        await Assert.That(lockedLinks.Single(link => link.Rel == LinkRelations.Self).PermissionAction)
            .IsEqualTo(AuthorizationActions.TenantSettings.View);
    }

    [Test]
    public async Task InstanceStorageSettingsResource_WhenInstanceSettingUpdateAllowed_MaterializesAdminAffordances()
    {
        var assembler = CreateInstanceAssembler(check =>
            check.ResourceKind == ResourceKinds.InstanceSetting &&
            check.ResourceId == "storage" &&
            check.Action is AuthorizationActions.InstanceSettings.View or AuthorizationActions.InstanceSettings.Update);
        var context = CreateHttpContext(authenticated: true, assembler.Evaluator);

        var resource = await assembler.Assembler.ToResource(new InstanceStorageSettingsDto(), context);

        await Assert.That(resource.Links.ContainsKey(LinkRelations.Self)).IsTrue();
        await Assert.That(resource.Links.ContainsKey(LinkRelations.Edit)).IsTrue();
        await Assert.That(resource.Links.ContainsKey("provider-test")).IsTrue();
        await Assert.That(resource.Links.ContainsKey("provider-test")).IsTrue();
        await Assert.That(resource.Links.ContainsKey("recalculate-usage")).IsTrue();
    }

    [Test]
    public async Task InstanceStorageSettingsResource_WhenInstanceSettingUpdateDenied_HidesAdminAffordances()
    {
        var assembler = CreateInstanceAssembler(check =>
            check.ResourceKind == ResourceKinds.InstanceSetting &&
            check.Action == AuthorizationActions.InstanceSettings.View);
        var context = CreateHttpContext(authenticated: true, assembler.Evaluator);

        var resource = await assembler.Assembler.ToResource(new InstanceStorageSettingsDto(), context);

        await Assert.That(resource.Links.ContainsKey(LinkRelations.Self)).IsTrue();
        await Assert.That(resource.Links.ContainsKey(LinkRelations.Edit)).IsFalse();
        await Assert.That(resource.Links.ContainsKey("provider-test")).IsFalse();
        await Assert.That(resource.Links.ContainsKey("provider-test")).IsFalse();
        await Assert.That(resource.Links.ContainsKey("recalculate-usage")).IsFalse();
    }

    [Test]
    public async Task TenantStorageSettingsResource_WhenTenantSettingUpdateAllowed_MaterializesEditAffordance()
    {
        var tenantId = Guid.CreateVersion7();
        var assembler = CreateTenantAssembler(check =>
            check.ResourceKind == ResourceKinds.TenantSetting &&
            check.ResourceId == $"{tenantId}:storage" &&
            check.Action is AuthorizationActions.TenantSettings.View or AuthorizationActions.TenantSettings.Update);
        var context = CreateHttpContext(authenticated: true, assembler.Evaluator);

        var resource = await assembler.Assembler.ToResource(CreateEditableTenantSettings(tenantId), context);

        await Assert.That(resource.Links.ContainsKey(LinkRelations.Self)).IsTrue();
        await Assert.That(resource.Links.ContainsKey(LinkRelations.Edit)).IsTrue();
    }

    [Test]
    public async Task TenantStorageSettingsResource_WhenDelegationLocked_HidesEditBeforeAuthorization()
    {
        var tenantId = Guid.CreateVersion7();
        var assembler = CreateTenantAssembler(_ => true);
        var context = CreateHttpContext(authenticated: true, assembler.Evaluator);

        var resource = await assembler.Assembler.ToResource(new TenantStorageSettingsDto
        {
            TenantId = tenantId,
            TenantOverridesAllowed = false,
            TenantStorageLocked = true,
            IsReadOnly = true
        }, context);

        await Assert.That(resource.Links.ContainsKey(LinkRelations.Self)).IsTrue();
        await Assert.That(resource.Links.ContainsKey(LinkRelations.Edit)).IsFalse();
    }

    private static StorageObjectDto CreateStorageObject(Guid tenantId) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            FileTypeId = 1,
            Uri = "/storage/test.png",
            Provider = StorageProviders.Local,
            FullName = "test.png",
            SafeDisplayName = "test.png",
            Extension = ".png",
            Size = 1024,
            Visibility = StorageObjectVisibilities.PublicImage,
            Purpose = StorageObjectPurposes.EventImage,
            LifecycleState = StorageObjectLifecycleStates.Active,
            TenantId = tenantId
        };

    private static TenantStorageSettingsDto CreateEditableTenantSettings(Guid tenantId) =>
        new()
        {
            TenantId = tenantId,
            TenantOverridesAllowed = true,
            TenantStorageLocked = false,
            IsReadOnly = false
        };

    private static InstanceAssemblerHarness CreateInstanceAssembler(Func<AuthorizationRequest, bool> predicate)
    {
        var evaluator = CreateEvaluator(predicate);
        var linkGenerator = CreateLinkGenerator();
        var assembler = new InstanceStorageSettingsResourceAssembler(
            linkGenerator,
            new InstanceStorageSettingsLinkPolicy(),
            new InstanceStorageSettingsCollectionLinkPolicy());

        return new InstanceAssemblerHarness(assembler, evaluator);
    }

    private static TenantAssemblerHarness CreateTenantAssembler(Func<AuthorizationRequest, bool> predicate)
    {
        var evaluator = CreateEvaluator(predicate);
        var linkGenerator = CreateLinkGenerator();
        var assembler = new TenantStorageSettingsResourceAssembler(
            linkGenerator,
            new TenantStorageSettingsLinkPolicy(),
            new TenantStorageSettingsCollectionLinkPolicy());

        return new TenantAssemblerHarness(assembler, evaluator);
    }

    private static IHateoasAuthorizationEvaluator CreateEvaluator(Func<AuthorizationRequest, bool> predicate)
    {
        var authorizationProvider = new StubAuthorizationProvider { CheckPredicate = predicate };
        return new HateoasAuthorizationEvaluator(
            authorizationProvider,
            Substitute.For<Explore.Application.Contracts.Persistence.IEventRepository>(),
            Substitute.For<ITenantContext>(),
            Substitute.For<ILogger<HateoasAuthorizationEvaluator>>());
    }

    private static IHateoasLinkGenerator CreateLinkGenerator()
    {
        var linkGenerator = Substitute.For<IHateoasLinkGenerator>();
        linkGenerator.GenerateLink(Arg.Any<LinkDefinition>(), Arg.Any<HttpContext>())
            .Returns(call => new HalLink
            {
                Href = $"/{call.Arg<LinkDefinition>().Rel}",
                Method = call.Arg<LinkDefinition>().Method,
                Title = call.Arg<LinkDefinition>().Title
            });

        return linkGenerator;
    }

    private static DefaultHttpContext CreateHttpContext(bool authenticated, IHateoasAuthorizationEvaluator evaluator)
    {
        var services = new ServiceCollection();
        services.AddSingleton(evaluator);

        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.User = authenticated
            ? new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "Test"))
            : new ClaimsPrincipal(new ClaimsIdentity());

        return context;
    }

    private sealed record InstanceAssemblerHarness(
        InstanceStorageSettingsResourceAssembler Assembler,
        IHateoasAuthorizationEvaluator Evaluator);

    private sealed record TenantAssemblerHarness(
        TenantStorageSettingsResourceAssembler Assembler,
        IHateoasAuthorizationEvaluator Evaluator);
}

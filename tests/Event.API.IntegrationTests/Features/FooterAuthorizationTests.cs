using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Footer;
using Explore.Application.Features.Footer.Handlers.Commands;
using Explore.Application.Models.Common;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Domain;
using Explore.Domain.Constants;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

public sealed class FooterAuthorizationTests
{
    [Test]
    public async Task GetSettings_WhenAuthenticated_ReturnsAuthoritativeHalResource()
    {
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider { AllowAll = true }
        };
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(HttpMethod.Get, "/api/footer/settings");

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        await Assert.That(root.GetProperty("tenantId").GetGuid()).IsNotEqualTo(Guid.Empty);
        await Assert.That(root.TryGetProperty("enabled", out _)).IsTrue();
        await Assert.That(root.TryGetProperty("template", out _)).IsTrue();
        await Assert.That(root.TryGetProperty("lockTenantTemplate", out _)).IsTrue();
        await Assert.That(root.TryGetProperty("lockTenantDescription", out _)).IsTrue();
        await Assert.That(root.TryGetProperty("lockTenantSocialLinks", out _)).IsTrue();
        await Assert.That(root.TryGetProperty("lockTenantCopyright", out _)).IsTrue();
        await Assert.That(root.TryGetProperty("linkGroups", out _)).IsFalse();
        var links = root.GetProperty("_links");
        await Assert.That(links.TryGetProperty("self", out _)).IsTrue();
        await Assert.That(links.GetProperty("edit").GetProperty("method").GetString()).IsEqualTo("PATCH");
    }

    [Test]
    public async Task GetSettings_WhenEditAuthorizationIsDenied_OmitsEditButKeepsSelf()
    {
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider { AllowAll = false }
        };
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(HttpMethod.Get, "/api/footer/settings");

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var links = body.RootElement.GetProperty("_links");
        await Assert.That(links.TryGetProperty("self", out _)).IsTrue();
        await Assert.That(links.TryGetProperty("edit", out _)).IsFalse();
    }

    [Test]
    public async Task PatchSettings_WhenAuthorizationProviderDenies_ReturnsForbidden()
    {
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider { AllowAll = false }
        };
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(HttpMethod.Patch, "/api/footer/settings");
        request.Content = JsonContent.Create(new PatchTenantFooterSettingsDto
        {
            General = new PatchTenantFooterGeneralDto
            {
                Enabled = OptionalUpdate<bool>.Set(true)
            }
        });

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task PutSettings_WhenAuthenticated_ReturnsMethodNotAllowed()
    {
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider { AllowAll = true }
        };
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(HttpMethod.Put, "/api/footer/settings");
        request.Content = JsonContent.Create(new { });

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MethodNotAllowed);
    }

    [Test]
    public async Task CreateLinkGroup_WhenAuthorizationProviderDenies_ReturnsForbidden()
    {
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider { AllowAll = false }
        };
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/footer/link-groups")
        {
            Content = JsonContent.Create(new { title = "Main" })
        };
        request.Headers.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(Guid.NewGuid()));

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task CreateLinkGroup_WhenMultiTenantLinkGroupsAreLocked_ReturnsForbiddenWithoutMutation()
    {
        var settingsResolver = CreateSettingsResolver(lockLinkGroups: true);
        var deploymentModeProvider = Substitute.For<IDeploymentModeProvider>();
        deploymentModeProvider.IsSingleTenantAsync(Arg.Any<CancellationToken>()).Returns(false);
        var groupRepository = Substitute.For<IFooterLinkGroupRepository>();
        await using var baseFactory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider { AllowAll = true }
        };
        await using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHierarchicalSettingsResolver>();
            services.AddScoped(_ => settingsResolver);
            services.RemoveAll<FooterLinkMutationGuard>();
            services.AddScoped(_ => new FooterLinkMutationGuard(settingsResolver, deploymentModeProvider));
            services.RemoveAll<IFooterLinkGroupRepository>();
            services.AddScoped(_ => groupRepository);
        }));
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/footer/link-groups");
        request.Headers.Add("X-Tenant-Slug", PlatformDefaults.DefaultTenantSlug);
        request.Content = JsonContent.Create(new { title = "Main" });

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await groupRepository.DidNotReceiveWithAnyArgs().GetMaxOrderAsync(default, default);
        await groupRepository.DidNotReceiveWithAnyArgs().Create(default!);
    }

    [Test]
    public async Task SingleTenant_WhenRawLinkGroupLockIsSet_EmitsManageRelationAndAllowsCreate()
    {
        var settingsResolver = CreateSettingsResolver(lockLinkGroups: true);
        var groupRepository = Substitute.For<IFooterLinkGroupRepository>();
        groupRepository.GetMaxOrderAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(0);
        groupRepository.Create(Arg.Any<TenantFooterLinkGroup>())
            .Returns(call =>
            {
                var group = call.Arg<TenantFooterLinkGroup>();
                group.Id = Guid.NewGuid();
                return group;
            });
        await using var baseFactory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider { AllowAll = true }
        };
        await using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHierarchicalSettingsResolver>();
            services.AddScoped(_ => settingsResolver);
            services.RemoveAll<IFooterLinkGroupRepository>();
            services.AddScoped(_ => groupRepository);
        }));
        using var client = factory.CreateClient();
        using var getRequest = CreateAuthenticatedRequest(HttpMethod.Get, "/api/footer/settings");

        var getResponse = await client.SendAsync(getRequest);
        using var body = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync());
        using var createRequest = CreateAuthenticatedRequest(HttpMethod.Post, "/api/footer/link-groups");
        createRequest.Content = JsonContent.Create(new { title = "Main" });
        var createResponse = await client.SendAsync(createRequest);

        await Assert.That(getResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body.RootElement.GetProperty("lockTenantLinkGroups").GetBoolean()).IsFalse();
        await Assert.That(body.RootElement.GetProperty("_links").TryGetProperty("manage-link-groups", out _)).IsTrue();
        await Assert.That(createResponse.StatusCode).IsEqualTo(HttpStatusCode.Created);
        await groupRepository.Received(1).Create(Arg.Is<TenantFooterLinkGroup>(group => group.Title == "Main"));
        await settingsResolver.Received(1).ResolveGroupAsync<FooterSettingGroup>(
            Arg.Any<SettingContext>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task LinkCrudAndReorderActions_KeepTheirExplicitMethodsRoutesAndNames()
    {
        await AssertRoute<Microsoft.AspNetCore.Mvc.HttpGetAttribute>(nameof(FooterController.GetLinkGroups), "link-groups", RouteNames.GetFooterLinkGroups);
        await AssertRoute<Microsoft.AspNetCore.Mvc.HttpGetAttribute>(nameof(FooterController.GetLinkGroupById), "link-groups/{id:guid}", RouteNames.GetFooterLinkGroupById);
        await AssertRoute<Microsoft.AspNetCore.Mvc.HttpPostAttribute>(nameof(FooterController.CreateLinkGroup), "link-groups", RouteNames.CreateFooterLinkGroup);
        await AssertRoute<Microsoft.AspNetCore.Mvc.HttpPatchAttribute>(nameof(FooterController.UpdateLinkGroup), "link-groups/{id:guid}", RouteNames.UpdateFooterLinkGroup);
        await AssertRoute<Microsoft.AspNetCore.Mvc.HttpDeleteAttribute>(nameof(FooterController.DeleteLinkGroup), "link-groups/{id:guid}", RouteNames.DeleteFooterLinkGroup);
        await AssertRoute<Microsoft.AspNetCore.Mvc.HttpPostAttribute>(nameof(FooterController.ReorderLinkGroups), "link-groups/reorder", RouteNames.ReorderFooterLinkGroups);
        await AssertRoute<Microsoft.AspNetCore.Mvc.HttpPostAttribute>(nameof(FooterController.CreateLink), "link-groups/{groupId:guid}/links", RouteNames.CreateFooterLink);
        await AssertRoute<Microsoft.AspNetCore.Mvc.HttpPatchAttribute>(nameof(FooterController.UpdateLink), "links/{id:guid}", RouteNames.UpdateFooterLink);
        await AssertRoute<Microsoft.AspNetCore.Mvc.HttpDeleteAttribute>(nameof(FooterController.DeleteLink), "links/{id:guid}", RouteNames.DeleteFooterLink);
    }

    private static HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(Guid.NewGuid()));
        return request;
    }

    private static IHierarchicalSettingsResolver CreateSettingsResolver(bool lockLinkGroups)
    {
        var settingsResolver = Substitute.For<IHierarchicalSettingsResolver>();
        var group = new FooterSettingGroup();
        group.Populate(new Dictionary<string, ResolvedSetting>
        {
            [GovernanceSettingKeys.Footer.LockTenantLinkGroups] = new()
            {
                Value = SettingValueSerializer.Serialize(lockLinkGroups)
            }
        });
        settingsResolver.ResolveGroupAsync<FooterSettingGroup>(
                Arg.Any<SettingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(group);
        return settingsResolver;
    }

    private static async Task AssertRoute<TAttribute>(string methodName, string template, string routeName)
        where TAttribute : HttpMethodAttribute
    {
        var method = typeof(FooterController).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        var attribute = method?.GetCustomAttribute<TAttribute>();

        await Assert.That(attribute).IsNotNull();
        await Assert.That(attribute!.Template).IsEqualTo(template);
        await Assert.That(attribute.Name).IsEqualTo(routeName);
    }
}

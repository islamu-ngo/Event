using System.Reflection;
using Explore.API.Controllers;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.InstanceAdmin;
using Explore.Blazor.Client.Contracts.Services.InstanceAdmin;
using Explore.Blazor.Client.Extensions;
using Explore.Blazor.Client.Pages.Admin;
using Explore.Blazor.Client.Pages.Admin.Components;
using Explore.Blazor.Client.Pages.Admin.Instance;
using Explore.Blazor.Client.Routing.InstanceAdmin;
using Explore.Blazor.Client.Services.InstanceAdmin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using RouteAttribute = Microsoft.AspNetCore.Mvc.RouteAttribute;

namespace Event.Architecture.Tests;

public sealed class UnifiedAdminClientArchitectureTests
{
    [Test]
    public async Task InstanceRoutes_MustStayUnderAdministrationRoots()
    {
        await Assert.That(InstanceAdminRoutes.Root).IsEqualTo("/admin/instance");
        await Assert.That(InstanceAdminRoutes.Overview).IsEqualTo("/settings/instance");
        await Assert.That(InstanceAdminRoutes.Tenants).IsEqualTo("/admin/instance/tenants");
        await Assert.That(InstanceAdminRoutes.Plans).IsEqualTo("/admin/instance/plans");
    }

    [Test]
    public async Task UnifiedAdministration_MustExposeAuthorizedClientComponents()
    {
        await Assert.That(typeof(IComponent).IsAssignableFrom(typeof(AdminSettingsPage))).IsTrue();
        await Assert.That(typeof(AdminSettingsPage).GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(typeof(IComponent).IsAssignableFrom(typeof(UnifiedAdminSettingsLayout))).IsTrue();
        await Assert.That(typeof(UnifiedAdminSettingsLayout).Assembly).IsEqualTo(typeof(AdminSettingsPage).Assembly);
    }

    [Test]
    public async Task InstanceClientRegistration_MustBeIdempotentAndShareScopedAdapters()
    {
        var services = new ServiceCollection();
        await Assert.That(services.AddInstanceAdminClient()).IsEqualTo(services);
        services.AddInstanceAdminClient();
        services.AddSharedApplicationServices();
        foreach (Type contract in new[]
        {
            typeof(IInstanceOverviewService), typeof(IInstanceTenantService),
            typeof(IInstanceDomainService), typeof(IInstanceOperationsService),
            typeof(IInstancePlanCatalogService), typeof(IInstanceTenantConfigurationService)
        })
        {
            ServiceDescriptor registration = services.Single(descriptor => descriptor.ServiceType == contract);
            await Assert.That(registration.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            await Assert.That(contract.IsAssignableFrom(typeof(InstanceAdminApiAdapter))).IsTrue();
        }

        Type[] transportDependencies = typeof(InstanceAdminApiAdapter).GetConstructors().Single()
            .GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        await Assert.That(transportDependencies).Contains(typeof(IInstanceAdminClient));
        await Assert.That(transportDependencies.All(dependency =>
                GeneratedEventApiClients.ClientTypes.Any(client => client.InterfaceType == dependency)))
            .IsTrue()
            .Because("the adapter's only backend boundary is the generated API transport");
    }

    [Test]
    public async Task InstanceServices_MustExposeGeneratedHalAndCommandContracts()
    {
        await Assert.That(typeof(IInstanceOverviewService)
                .GetMethod(nameof(IInstanceOverviewService.GetOverviewAsync))!.ReturnType)
            .IsEqualTo(typeof(Task<HalResourceOfInstanceOverviewDto>));
        await Assert.That(typeof(IInstanceTenantConfigurationService)
                .GetMethod(nameof(IInstanceTenantConfigurationService.GetEffectiveConfigurationAsync))!.ReturnType)
            .IsEqualTo(typeof(Task<HalResourceOfInstanceTenantEffectiveConfigurationDto>));
        await Assert.That(typeof(IInstancePlanCatalogService)
                .GetMethod(nameof(IInstancePlanCatalogService.GetPlansAsync))!.ReturnType)
            .IsEqualTo(typeof(Task<HalCollectionResourceOfInstanceTenantPlanListItemDto>));
        await Assert.That(typeof(IInstancePlanCatalogService)
                .GetMethod(nameof(IInstancePlanCatalogService.GetPlanAsync))!.ReturnType)
            .IsEqualTo(typeof(Task<HalResourceOfInstanceTenantPlanDetailDto>));

        foreach (Type service in new[]
        {
            typeof(IInstanceOverviewService), typeof(IInstanceTenantService),
            typeof(IInstanceDomainService), typeof(IInstanceOperationsService),
            typeof(IInstancePlanCatalogService), typeof(IInstanceTenantConfigurationService)
        })
        {
            foreach (MethodInfo method in service.GetMethods())
            {
                await Assert.That(method.ReturnType.IsGenericType
                        && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
                    .IsTrue();
                Type response = method.ReturnType.GenericTypeArguments.Single();
                await Assert.That(response.Assembly).IsEqualTo(typeof(HalLink).Assembly);
                await Assert.That(response.Namespace).IsEqualTo(typeof(HalLink).Namespace)
                    .Because("UI services return generated transport contracts, not local HAL or command mirrors");
                await Assert.That(method.GetParameters().Last().ParameterType).IsEqualTo(typeof(CancellationToken));
            }
        }

        foreach (MethodInfo method in typeof(IInstanceTenantConfigurationService).GetMethods()
                     .Where(method => method.Name != nameof(IInstanceTenantConfigurationService.GetEffectiveConfigurationAsync)))
        {
            await Assert.That(method.ReturnType).IsEqualTo(typeof(Task<BaseCommandResponseOfGuid>));
            await Assert.That(method.GetParameters().Last().ParameterType).IsEqualTo(typeof(CancellationToken));
        }
    }

    [Test]
    public async Task TenantConfiguration_MustInjectItsGeneratedContractService()
    {
        Type[] injected = typeof(InstanceTenantConfiguration).GetProperties(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(property => property.IsDefined(typeof(InjectAttribute)))
            .Select(property => property.PropertyType).ToArray();

        await Assert.That(injected).Contains(typeof(IInstanceTenantConfigurationService));
    }

    [Test]
    public async Task InstanceAdministration_MustExposeItsDedicatedApiRoutes()
    {
        foreach (var capability in new (Type Controller, string Operation, string Verb, string Path)[]
        {
            (typeof(InstanceAdminController), Explore.API.Hateoas.RouteNames.GetInstanceAdminOverview,
                "GET", "api/admin/instance/overview"),
            (typeof(InstanceTenantLifecycleController), Explore.API.Hateoas.RouteNames.CreateInstanceAdminTenant,
                "POST", "api/admin/instance/tenants"),
            (typeof(InstanceTenantPlanController), Explore.API.Hateoas.RouteNames.GetInstanceAdminTenantPlans,
                "GET", "api/admin/instance/plans"),
            (typeof(InstanceTenantConfigurationController), Explore.API.Hateoas.RouteNames.SetInstanceAdminTenantSetting,
                "PUT", "api/admin/instance/tenants/{tenantId:guid}/settings/{key}"),
            (typeof(InstanceDeploymentModeController), Explore.API.Hateoas.RouteNames.GetInstanceAdminDeploymentModeRunbook,
                "GET", "api/admin/instance/deployment-mode"),
            (typeof(InstanceDeploymentModeController), Explore.API.Hateoas.RouteNames.TransitionInstanceAdminDeploymentMode,
                "POST", "api/admin/instance/deployment-mode/transition")
        })
        {
            string root = capability.Controller.GetCustomAttribute<RouteAttribute>()!.Template;
            HttpMethodAttribute endpoint = capability.Controller.GetMethods()
                .SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>())
                .Single(attribute => attribute.Name == capability.Operation);

            await Assert.That($"{root}/{endpoint.Template}".TrimEnd('/')).IsEqualTo(capability.Path);
            await Assert.That(endpoint.HttpMethods).IsEquivalentTo([capability.Verb]);
            await Assert.That(capability.Controller.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        }
    }

    [Test]
    public async Task HalAffordances_MustFailClosedAndMatchResourceIdentity()
    {
        Guid resource = Guid.CreateVersion7();
        var links = new Dictionary<string, HalLink>
        {
            [InstanceAdminLinkRelations.Edit] = new() { Href = $"/api/admin/instance/tenants/{resource}/settings" }
        };

        await Assert.That(InstanceAdminHal.HasLink(null, InstanceAdminLinkRelations.Edit)).IsFalse();
        await Assert.That(InstanceAdminHal.HasLink(links, InstanceAdminLinkRelations.Delete)).IsFalse();
        await Assert.That(InstanceAdminHal.HasLink(links, InstanceAdminLinkRelations.Edit)).IsTrue();
        await Assert.That(InstanceAdminHal.HasLinkForResource(links, InstanceAdminLinkRelations.Edit, resource)).IsTrue();
        await Assert.That(InstanceAdminHal.HasLinkForResource(links, InstanceAdminLinkRelations.Edit, Guid.CreateVersion7())).IsFalse();
        links[InstanceAdminLinkRelations.Edit] = new HalLink { Href = $"/api/admin/instance/tenants?target={resource}" };
        await Assert.That(InstanceAdminHal.HasLinkForResource(links, InstanceAdminLinkRelations.Edit, resource)).IsFalse();
    }

    [Test]
    public async Task ExternalFleetManagement_MustKeepItsDedicatedApiRoute()
    {
        await Assert.That(typeof(ManagementController).GetCustomAttribute<RouteAttribute>()?.Template)
            .IsEqualTo("api/management");
        await Assert.That(typeof(ManagementController).GetConstructors().Single().GetParameters()
                .Any(parameter => parameter.ParameterType.GenericTypeArguments.Any(argument =>
                    argument == typeof(Explore.Application.Features.Management.Requests.Commands.TriggerManagedControlPlaneRegistrationCommand))))
            .IsTrue();
    }
}

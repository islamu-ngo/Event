namespace Explore.Blazor.Client.Tests.Pages.Admin;

using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.ControlPlane;
using Explore.Blazor.Client.Contracts.Interop;
using Explore.Blazor.Client.Contracts.Services.ControlPlane;
using Explore.Blazor.Client.Pages.Admin.Components;
using Explore.Blazor.Client.Services.ControlPlane;
using Microsoft.AspNetCore.Components;

public sealed class ConfigurationManifestImportAdministrationTests : IDisposable
{
    private readonly BlazorTestContext context = new();

    public void Dispose() => context.Dispose();

    [Test]
    public async Task InstanceImport_RendersOnlyWhenServerAdvertisesHalCapability()
    {
        IControlPlaneClient api = ConfigurationManifestImportAdministrationTests.InstanceApi(
            ControlPlaneLinkRelations.CreateConfigurationImportSession);
        ConfigurationManifestImportAdministrationTests.Register(context, api);

        var cut = context.Render<ConfigurationPortabilityWorkspace>(parameters =>
            parameters.Add(component => component.Scope, ConfigurationImportScope.Instance));
        cut.WaitForAssertion(() => cut.Find("#instance-configuration-portability-file"));

        await Assert.That(cut.Markup).Contains("Preview and apply");
        await Assert.That(cut.Markup).DoesNotContain("Apply selected configuration");
    }

    [Test]
    public async Task InstanceImport_CapabilityLossRemovesUploadEntryPoint()
    {
        Register(context, InstanceApi());

        var cut = context.Render<ConfigurationPortabilityWorkspace>(parameters =>
            parameters.Add(component => component.Scope, ConfigurationImportScope.Instance));
        cut.WaitForAssertion(() => cut.Find("#instance-configuration-portability-heading"));

        await Assert.That(cut.FindAll("#instance-configuration-portability-file"))
            .IsEmpty();
        await Assert.That(cut.Markup)
            .Contains("server did not advertise configuration import authority");
    }

    internal static IControlPlaneClient InstanceApi(params string[] relations)
    {
        IControlPlaneClient api = Substitute.For<IControlPlaneClient>();
        api.GetControlPlaneOverviewAsync(null, null, Arg.Any<CancellationToken>())
            .Returns(new HalResourceOfControlPlaneOverviewDto
            {
                _links = Links(relations)
            });
        return api;
    }

    internal static void Register(
        BlazorTestContext context,
        IControlPlaneClient? controlPlaneClient = null,
        ITenantOnboardingClient? tenantOnboardingClient = null)
    {
        var cpClient = controlPlaneClient ?? Substitute.For<IControlPlaneClient>();
        var toClient = tenantOnboardingClient ?? Substitute.For<ITenantOnboardingClient>();
        context.Services.AddSingleton(cpClient);
        context.Services.AddSingleton(toClient);
        context.Services.AddSingleton<IConfigurationPortabilityService>(provider =>
            new ConfigurationPortabilityService(
                cpClient,
                toClient,
                Substitute.For<IControl_Plane_ConfigurationClient>(),
                Substitute.For<ITenant_ConfigurationClient>(),
                Substitute.For<IConfigurationManifestExportService>(),
                Substitute.For<IBrowserActionInterop>(),
                provider.GetRequiredService<NavigationManager>()));
    }

    internal static Dictionary<string, HalLink> Links(params string[] relations) =>
        relations.ToDictionary(
            relation => relation,
            relation => new HalLink { Href = $"/api/{relation}", Method = "POST" },
            StringComparer.Ordinal);
}

public sealed class TenantConfigurationPortabilityAdministrationTests : IDisposable
{
    private readonly BlazorTestContext context = new();

    public void Dispose() => context.Dispose();

    [Test]
    public async Task TenantWorkspace_UsesCurrentTenantAndNeverOffersInstanceExport()
    {
        Guid tenantId = Guid.CreateVersion7();
        var toClient = Substitute.For<ITenantOnboardingClient>();
        toClient.GetTenantOnboardingStatusAsync(null, null, Arg.Any<CancellationToken>())
            .Returns(new HalResourceOfTenantOnboardingStatusDto
            {
                TenantId = tenantId,
                _links = ConfigurationManifestImportAdministrationTests.Links(
                    ControlPlaneLinkRelations.CreateConfigurationImportSession,
                    ControlPlaneLinkRelations.ExportTenantConfigurationPackage)
            });
        var cpClient = Substitute.For<IControlPlaneClient>();
        cpClient.GetControlPlaneTenantsAsync(null, null, Arg.Any<CancellationToken>())
            .Returns(new HalCollectionResourceOfControlPlaneTenantListItemDto
            {
                _links = new Dictionary<string, HalLink>(StringComparer.Ordinal)
            });
        ConfigurationManifestImportAdministrationTests.Register(context, cpClient, toClient);

        var cut = context.Render<ConfigurationPortabilityWorkspace>(parameters =>
            parameters.Add(component => component.Scope, ConfigurationImportScope.Tenant));
        cut.WaitForAssertion(() => cut.Find("#tenant-configuration-portability-file"));

        await Assert.That(cut.Markup).Contains("Download tenant package");
        await Assert.That(cut.Markup).DoesNotContain("Download portable");
        await Assert.That(cut.Markup).Contains("current route selects the target");
    }
}

public sealed class ConfigurationPortabilityAccessibilityTests : IDisposable
{
    private readonly BlazorTestContext context = new();

    public void Dispose() => context.Dispose();

    [Test]
    public async Task UploadSurface_HasLandmarkedHeadingsLabelAndDescribedHelp()
    {
        ConfigurationManifestImportAdministrationTests.Register(
            context,
            ConfigurationManifestImportAdministrationTests.InstanceApi(
                ControlPlaneLinkRelations.CreateConfigurationImportSession));

        var cut = context.Render<ConfigurationPortabilityWorkspace>(parameters =>
            parameters.Add(component => component.Scope, ConfigurationImportScope.Instance));
        cut.WaitForAssertion(() => cut.Find("#instance-configuration-portability-file"));
        var input = cut.Find("#instance-configuration-portability-file");

        await Assert.That(cut.Find("h2").TextContent)
            .Contains("Instance configuration portability");
        await Assert.That(cut.Find("h3").TextContent).Contains("Preview and apply");
        await Assert.That(cut.Find("label[for='instance-configuration-portability-file']"))
            .IsNotNull();
        await Assert.That(input.GetAttribute("aria-describedby"))
            .IsEqualTo("instance-configuration-portability-file-help");
        await Assert.That(cut.Markup).Contains("dir=\"auto\"");
    }
}

using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Instance;
using Explore.Application.Features.ControlPlane.Handlers.Queries;
using Explore.Application.Features.ControlPlane.Requests.Queries;
using Explore.Domain.Enums;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Application.UnitTests.Features.ControlPlane.Queries;

public sealed class GetControlPlaneDomainsQueryHandlerTests
{
    [Test]
    public async Task Handle_WhenHostsAreConfigured_ReturnsDnsChecklist()
    {
        var governanceService = Substitute.For<IInstanceGovernanceSettingService>();
        governanceService.ReadSettingsAsync().Returns(CreateSettings("events.example.org", allowCustomDomains: true));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PublicBaseUrl"] = "https://events.example.org",
                ["ControlPlane:PublicOrigin"] = "https://admin.example.org"
            })
            .Build();
        var handler = new GetControlPlaneDomainsQueryHandler(governanceService, configuration);

        var result = await handler.Handle(new GetControlPlaneDomainsQuery(), CancellationToken.None);

        await Assert.That(result.PublicPlatformHost).IsEqualTo("events.example.org");
        await Assert.That(result.WildcardTenantHost).IsEqualTo("*.events.example.org");
        await Assert.That(result.AdminHost).IsEqualTo("admin.example.org");
        await Assert.That(result.AllowTenantCustomDomains).IsTrue();

        var records = result.DnsRecords.ToArray();
        await Assert.That(records.Length).IsEqualTo(4);
        await Assert.That(records.Single(record => record.Purpose == "Public platform").Status).IsEqualTo("configured");
        await Assert.That(records.Single(record => record.Purpose == "Tenant subdomains").Name).IsEqualTo("*.events.example.org");
        await Assert.That(records.Single(record => record.Purpose == "Control plane").Name).IsEqualTo("admin.example.org");
        await Assert.That(records.Single(record => record.Purpose == "Custom tenant domains").Status).IsEqualTo("available");
    }

    [Test]
    public async Task Handle_WhenBaseDomainIsMissing_ReturnsWarnings()
    {
        var governanceService = Substitute.For<IInstanceGovernanceSettingService>();
        governanceService.ReadSettingsAsync().Returns(CreateSettings(instanceBaseDomain: string.Empty, allowCustomDomains: true));
        var configuration = new ConfigurationBuilder().Build();
        var handler = new GetControlPlaneDomainsQueryHandler(governanceService, configuration);

        var result = await handler.Handle(new GetControlPlaneDomainsQuery(), CancellationToken.None);

        await Assert.That(result.DnsRecords.Single(record => record.Purpose == "Public platform").Status)
            .IsEqualTo("missing_configuration");
        await Assert.That(result.DnsRecords.Single(record => record.Purpose == "Tenant subdomains").Status)
            .IsEqualTo("missing_configuration");
        await Assert.That(result.Warnings.Select(warning => warning.Code))
            .Contains("public_platform_host_missing");
        await Assert.That(result.Warnings.Select(warning => warning.Code))
            .Contains("wildcard_tenant_domain_missing");
    }

    [Test]
    public async Task Handle_WhenAdminOriginIsMissing_UsesPersistedAdminHost()
    {
        var governanceService = Substitute.For<IInstanceGovernanceSettingService>();
        governanceService.ReadSettingsAsync()
            .Returns(CreateSettings("events.example.org", allowCustomDomains: false, adminHost: "admin.events.example.org"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PublicBaseUrl"] = "https://events.example.org"
            })
            .Build();
        var handler = new GetControlPlaneDomainsQueryHandler(governanceService, configuration);

        var result = await handler.Handle(new GetControlPlaneDomainsQuery(), CancellationToken.None);

        await Assert.That(result.AdminHost).IsEqualTo("admin.events.example.org");
        await Assert.That(result.DnsRecords.Single(record => record.Purpose == "Control plane").Name)
            .IsEqualTo("admin.events.example.org");
        await Assert.That(result.Warnings.Select(warning => warning.Code))
            .DoesNotContain("control_plane_host_not_configured");
    }

    private static InstanceGovernanceSettings CreateSettings(
        string instanceBaseDomain,
        bool allowCustomDomains,
        string adminHost = "") => new()
        {
            DeploymentMode = new DeploymentModeDto { Mode = DeploymentMode.MultiTenant },
            Modules = new ModuleSettingsDto(),
            EventPolicy = new EventPolicyDto(),
            OrganizationPolicy = new OrganizationPolicyDto(),
            Branding = new BrandingSettingsDto(),
            Domains = new DomainSettingsDto
            {
                InstanceBaseDomain = instanceBaseDomain,
                AdminHost = adminHost,
                AllowTenantCustomDomains = allowCustomDomains
            },
            TenantDelegation = new TenantDelegationSettingsDto(),
            AdminPortal = new AdminPortalSettingsDto(),
            AiAssistant = new AiAssistantGovernanceSettingsDto(),
            Mcp = new McpGovernanceSettingsDto(),
            RenderPolicy = new RenderPolicySettingsDto()
        };
}

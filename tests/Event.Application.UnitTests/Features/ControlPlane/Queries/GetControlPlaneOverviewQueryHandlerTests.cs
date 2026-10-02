using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Instance;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.ControlPlane.Handlers.Queries;
using Explore.Application.Features.ControlPlane.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Application.UnitTests.Features.ControlPlane.Queries;

public sealed class GetControlPlaneOverviewQueryHandlerTests
{
    [Test]
    public async Task ConfiguredLocalAuthenticationIsReportedAsTheSelectedProvider()
    {
        var deployment = Substitute.For<IDeploymentModeProvider>();
        var tenants = Substitute.For<ITenantRepository>();
        var governance = Substitute.For<IInstanceGovernanceSettingService>();
        var authentication = Substitute.For<IAuthProviderConfigurationService>();
        var authorization = Substitute.For<IAuthorizationProviderConfigurationService>();
        var storage = Substitute.For<IInstanceStorageSettingService>();
        var smtp = Substitute.For<IInstanceSmtpSettingService>();
        var secrets = Substitute.For<ISecretAuthorityStatusReader>();

        deployment.GetCurrentModeAsync(Arg.Any<CancellationToken>()).Returns(DeploymentMode.MultiTenant);
        tenants.GetAll().Returns([]);
        governance.ReadSettingsAsync().Returns(new InstanceGovernanceSettings
        {
            DeploymentMode = new(),
            Modules = new(),
            EventPolicy = new(),
            OrganizationPolicy = new(),
            Branding = new(),
            Domains = new(),
            TenantDelegation = new(),
            AdminPortal = new(),
            AiAssistant = new(),
            Mcp = new(),
            RenderPolicy = new()
        });
        authentication.IsConfiguredAsync().Returns(true);
        authentication.ReadConfigurationAsync().Returns(new AuthProviderConfigurationDto
        {
            PrimaryProviderId = (int)AuthenticationProviderKind.Local
        });
        authorization.IsConfiguredAsync().Returns(true);
        authorization.ReadConfigurationAsync().Returns(new AuthorizationProviderConfigurationDto());
        storage.ReadSettingsAsync(Arg.Any<CancellationToken>()).Returns(new InstanceStorageSettingsDto());
        smtp.ReadSettingsAsync().Returns(new InstanceSmtpSettingsDto());
        secrets.ReadAsync(Arg.Any<CancellationToken>())
            .Returns(new SecretAuthorityStatusSnapshot("Environment", "configured", string.Empty));

        var handler = new GetControlPlaneOverviewQueryHandler(
            deployment, tenants, governance, authentication, authorization,
            storage, smtp, secrets, new ConfigurationBuilder().Build());

        var overview = await handler.QueryAsync(new GetControlPlaneOverviewQuery(), CancellationToken.None);
        var provider = overview.ProviderSummaries.Single(summary => summary.Key == "authentication");

        await Assert.That(provider.Configured).IsTrue();
        await Assert.That(provider.Message).IsEqualTo(nameof(AuthenticationProviderKind.Local));
        await Assert.That(overview.Warnings.Select(warning => warning.Code))
            .DoesNotContain("authentication_provider_missing");
    }
}

using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Instance;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceAdmin.Handlers.Queries;
using Explore.Application.Features.InstanceAdmin.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Application.UnitTests.Features.InstanceAdmin.Queries;

public sealed class GetInstanceOverviewQueryHandlerTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task ConfiguredLocalAuthenticationIsReportedAsTheSelectedProvider(
        bool bootstrapCompleted, bool profileAvailable)
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

        var profileReader = Substitute.For<IAuthenticationCredentialProfileReader>();
        Guid profileRevision = Guid.CreateVersion7();
        if (profileAvailable)
            profileReader.ReadCurrent().Returns(new AuthenticationCredentialProfile(
                AuthenticationCredentialCutover.CredentialAuthority.Environment, profileRevision));
        else
            profileReader.ReadCurrent().Returns(_ =>
                throw new InvalidOperationException("authentication_credential_profile_unavailable"));
        var bootstrapStates = Substitute.For<IInstanceBootstrapStateRepository>();
        DateTime now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);
        var bootstrap = InstanceBootstrapState.CreateInteractivePending(Guid.CreateVersion7(), DeploymentMode.MultiTenant, now);
        if (bootstrapCompleted)
            bootstrap.CompleteInteractive(Guid.CreateVersion7(), now);
        bootstrapStates.GetCurrent(Arg.Any<CancellationToken>()).Returns(bootstrap);
        var handler = new GetInstanceOverviewQueryHandler(
            deployment, tenants, governance, authentication, authorization,
            storage, smtp, secrets, new ConfigurationBuilder().Build(), profileReader, bootstrapStates);

        var overview = await handler.QueryAsync(new GetInstanceOverviewQuery(), CancellationToken.None);
        var provider = overview.ProviderSummaries.Single(summary => summary.Key == "authentication");

        await Assert.That(provider.Configured).IsTrue();
        await Assert.That(provider.Message).IsEqualTo(nameof(AuthenticationProviderKind.Local));
        await Assert.That(overview.Warnings.Select(warning => warning.Code))
            .DoesNotContain("authentication_provider_missing");
        await Assert.That(overview.AuthenticationCredentialProfileRevision)
            .IsEqualTo(bootstrapCompleted && profileAvailable ? profileRevision : (Guid?)null);
        await Assert.That(overview.AuthenticationCredentialAuthority)
            .IsEqualTo(bootstrapCompleted && profileAvailable ? "Environment" : null);
    }
}

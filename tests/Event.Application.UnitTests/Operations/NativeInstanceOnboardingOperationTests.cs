using Explore.Application;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Analytics;
using Explore.Application.DTOs.Instance;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using Explore.Application.Models;
using Explore.Application.Responses;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeInstanceOnboardingOperationTests
{
    private static readonly Type[] Requests =
    [
        // Commands (28)
        typeof(ApplyKeycloakRealmSyncCommand),
        typeof(BootstrapKeycloakRealmCommand),
        typeof(ClaimConfiguredInstanceAdministratorCommand),
        typeof(CompleteInstanceOnboardingCommand),
        typeof(CompleteLocalInstanceOnboardingCommand),
        typeof(RecalculateInstanceStorageUsageCommand),
        typeof(RotateKeycloakClientSecretCommand),
        typeof(SaveInstanceOnboardingProfileCommand),
        typeof(SyncAuthorizationPolicyPackageCommand),
        typeof(UpdateAnalyticsGovernanceSettingsCommand),
        typeof(UpdateAuthProviderConfigurationCommand),
        typeof(UpdateAuthProviderConfigurationDuringSetupCommand),
        typeof(UpdateAuthorizationProviderConfigurationCommand),
        typeof(UpdateAuthorizationProviderConfigurationDuringSetupCommand),
        typeof(UpdateInstanceSmtpSettingsCommand),
        typeof(UpdateInstanceStorageSettingsCommand),
        typeof(UpdateResolverConfigurationCommand),
        typeof(VerifyCerbosEndpointCommand),
        typeof(UpdateModuleSettingsCommand),
        typeof(UpdateEventPolicyCommand),
        typeof(UpdateOrganizationPolicyCommand),
        typeof(UpdateBrandingSettingsCommand),
        typeof(UpdateDomainSettingsCommand),
        typeof(UpdateTenantDelegationSettingsCommand),
        typeof(UpdateAdminPortalSettingsCommand),
        typeof(UpdateMcpGovernanceSettingsCommand),
        typeof(UpdateAiAssistantGovernanceSettingsCommand),
        typeof(UpdateRenderPolicySettingsCommand),

        // Queries (17)
        typeof(DownloadAuthorizationPolicyPackageQuery),
        typeof(GetActiveTenantCountQuery),
        typeof(GetAnalyticsGovernanceSettingsQuery),
        typeof(GetAuthProviderConfigurationQuery),
        typeof(GetAuthorizationPolicyPackageStatusQuery),
        typeof(GetAuthorizationProviderConfigurationQuery),
        typeof(GetInstanceGovernanceSettingsQuery),
        typeof(GetInstanceOnboardingStatusQuery),
        typeof(GetInstanceSmtpSettingsQuery),
        typeof(GetInstanceStorageSettingsQuery),
        typeof(GetOnboardingPreflightQuery),
        typeof(GetResolverConfigurationQuery),
        typeof(GetSystemOnboardingStatusQuery),
        typeof(PreviewKeycloakRealmSyncQuery),
        typeof(RunKeycloakRealmDoctorQuery),
        typeof(TestInstanceSmtpConnectionQuery),
        typeof(TestInstanceStorageProviderQuery)
    ];

    [Test]
    [Arguments(typeof(ApplyKeycloakRealmSyncCommand), typeof(ICommand<KeycloakRealmSyncPlanDto>))]
    [Arguments(typeof(BootstrapKeycloakRealmCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(ClaimConfiguredInstanceAdministratorCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(CompleteInstanceOnboardingCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(CompleteLocalInstanceOnboardingCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(RecalculateInstanceStorageUsageCommand), typeof(ICommand<InstanceStorageUsageDto>))]
    [Arguments(typeof(RotateKeycloakClientSecretCommand), typeof(ICommand<KeycloakClientSecretRotationResultDto>))]
    [Arguments(typeof(SaveInstanceOnboardingProfileCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(SyncAuthorizationPolicyPackageCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(UpdateAnalyticsGovernanceSettingsCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(UpdateAuthProviderConfigurationCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(UpdateAuthProviderConfigurationDuringSetupCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(UpdateAuthorizationProviderConfigurationCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(UpdateAuthorizationProviderConfigurationDuringSetupCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(UpdateInstanceSmtpSettingsCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(UpdateInstanceStorageSettingsCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(UpdateResolverConfigurationCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(VerifyCerbosEndpointCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(UpdateModuleSettingsCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(UpdateEventPolicyCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(UpdateOrganizationPolicyCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(UpdateBrandingSettingsCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(UpdateDomainSettingsCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(UpdateTenantDelegationSettingsCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(UpdateAdminPortalSettingsCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(UpdateMcpGovernanceSettingsCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(UpdateAiAssistantGovernanceSettingsCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(UpdateRenderPolicySettingsCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(DownloadAuthorizationPolicyPackageQuery), typeof(IQuery<PolicyPackageArchive>))]
    [Arguments(typeof(GetActiveTenantCountQuery), typeof(IQuery<int>))]
    [Arguments(typeof(GetAnalyticsGovernanceSettingsQuery), typeof(IQuery<AnalyticsGovernanceSettingsDto>))]
    [Arguments(typeof(GetAuthProviderConfigurationQuery), typeof(IQuery<AuthProviderConfigurationDto>))]
    [Arguments(typeof(GetAuthorizationPolicyPackageStatusQuery), typeof(IQuery<AuthorizationPolicyPackageStatusDto>))]
    [Arguments(typeof(GetAuthorizationProviderConfigurationQuery), typeof(IQuery<AuthorizationProviderConfigurationDto>))]
    [Arguments(typeof(GetInstanceGovernanceSettingsQuery), typeof(IQuery<InstanceGovernanceSettings>))]
    [Arguments(typeof(GetInstanceOnboardingStatusQuery), typeof(IQuery<InstanceOnboardingStatusDto>))]
    [Arguments(typeof(GetInstanceSmtpSettingsQuery), typeof(IQuery<InstanceSmtpSettingsDto>))]
    [Arguments(typeof(GetInstanceStorageSettingsQuery), typeof(IQuery<InstanceStorageSettingsDto>))]
    [Arguments(typeof(GetOnboardingPreflightQuery), typeof(IQuery<OnboardingPreflightDto>))]
    [Arguments(typeof(GetResolverConfigurationQuery), typeof(IQuery<ResolverConfigurationDto>))]
    [Arguments(typeof(GetSystemOnboardingStatusQuery), typeof(IQuery<SystemOnboardingStatusDto>))]
    [Arguments(typeof(PreviewKeycloakRealmSyncQuery), typeof(IQuery<KeycloakRealmSyncPlanDto>))]
    [Arguments(typeof(RunKeycloakRealmDoctorQuery), typeof(IQuery<KeycloakRealmDoctorResultDto>))]
    [Arguments(typeof(TestInstanceSmtpConnectionQuery), typeof(IQuery<EmailResult>))]
    [Arguments(typeof(TestInstanceStorageProviderQuery), typeof(IQuery<InstanceStorageProviderStatusDto>))]
    public async Task Requests_ExposeOnlyTheirExactNativeOperation(Type request, Type port)
    {
        await Assert.That(request.GetInterfaces()).Contains(port);
        await Assert.That(request.GetInterfaces().Any(type => type.Namespace == "MediatR")).IsFalse();
        await Assert.That(request.GetInterfaces().Count(type => type.IsGenericType
            && (type.GetGenericTypeDefinition() == typeof(ICommand<>)
                || type.GetGenericTypeDefinition() == typeof(IQuery<>)))).IsEqualTo(1);
    }

    [Test]
    public async Task Requests_HaveOneNativeShapeAndNoLegacyDispatchEscapeHatch()
    {
        await Assert.That(Requests.Length).IsEqualTo(45);

        foreach (var request in Requests)
        {
            var shapes = request.GetInterfaces().Where(type => type.IsGenericType &&
                (type.GetGenericTypeDefinition() == typeof(ICommand<>) ||
                 type.GetGenericTypeDefinition() == typeof(IQuery<>))).ToArray();
            await Assert.That(shapes.Length).IsEqualTo(1);
            await Assert.That(typeof(MediatR.IBaseRequest).IsAssignableFrom(request)).IsFalse();
        }
    }

    [Test]
    public async Task ApplicationComposition_RegistersEveryInstanceOnboardingOperationAsAScopedProtectedPort()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations(typeof(CompleteInstanceOnboardingCommand).Assembly.GetTypes());
        var ports = services.Where(descriptor => !descriptor.IsKeyedService &&
            OperationServicesRegistration.IsHandlerContract(descriptor.ServiceType) &&
            Requests.Contains(descriptor.ServiceType.GenericTypeArguments[0])).ToArray();
        await Assert.That(ports.Length).IsEqualTo(45);
        await Assert.That(ports.All(port => port.Lifetime == ServiceLifetime.Scoped)).IsTrue();
        await Assert.That(ports.All(port => port.ImplementationFactory is not null)).IsTrue();
    }
}

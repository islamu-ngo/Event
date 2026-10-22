using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Users.Requests.Commands;
using Explore.Application.Features.Localization.Requests.Commands;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.EmailDispatch.Requests.Commands;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Settings;
using Explore.Application.Features.Settings;
using Explore.Application.Features.TenantOnboarding.Requests.Commands;
using Explore.Application.Features.EventReporting.Requests.Commands;
using Explore.Application.Features.ManagedProviderProvisioning.Requests.Commands;
using Explore.Application.Features.Tenants.Requests.Commands;
using Explore.Domain.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Application.Operations;

internal static class PolicyMutationCommandScope
{
    internal static bool Handles(Type request) =>
        FooterOperationScope.Handles(request)
        || request == typeof(UpdateLocalizationGovernanceCommand)
        || request == typeof(UpdateUserLastActiveTenantCommand)
        || request == typeof(UpdateSettingCommand) || request == typeof(UpdateSettingBatchCommand)
        || request == typeof(ResetSettingCommand) || request == typeof(LockSettingCommand)
        || request == typeof(UnlockSettingCommand) || request == typeof(SetInstanceTenantSettingCommand)
        || request == typeof(LockInstanceTenantSettingCommand) || request == typeof(UnlockInstanceTenantSettingCommand)
        || request == typeof(ApplyInstanceTenantPlanAssignmentCommand)
        || request == typeof(DisableEmailDeliveryCommand)
        || request == typeof(PreviewEmailDeliveryDisableCommand)
        || request == typeof(UpdateInstanceSmtpSettingsCommand)
        || request == typeof(UpdateTenantDelegationSettingsCommand)
        || request == typeof(UpdateModuleSettingsCommand)
        || request == typeof(UpdateOrganizationPolicyCommand)
        || request == typeof(UpdateBrandingSettingsCommand)
        || request == typeof(UpdateDomainSettingsCommand)
        || request == typeof(UpdateAdminPortalSettingsCommand)
        || request == typeof(UpdateMcpGovernanceSettingsCommand)
        || request == typeof(UpdateAiAssistantGovernanceSettingsCommand)
        || request == typeof(UpdateRenderPolicySettingsCommand)
        || request == typeof(UpdateEventPolicyCommand)
        || request == typeof(EnsureManagedProviderClientProvisionedCommand)
        || request == typeof(CreateTenantCommand)
        || request == typeof(CompleteInstanceOnboardingCommand)
        || request == typeof(CompleteLocalInstanceOnboardingCommand)
        || request == typeof(CompleteTenantOnboardingCommand)
        || request == typeof(UpdateTenantReportingIntakePolicyCommand);

    internal static ValueTask<PolicyMutationScope?> SelectAsync(
        object request, IServiceProvider services, CancellationToken cancellationToken)
    {
        if (FooterOperationScope.Handles(request.GetType()))
            return ValueTask.FromResult<PolicyMutationScope?>(FooterOperationScope.Select(request, services.GetService<ITenantContextAccessor>()));
        cancellationToken.ThrowIfCancellationRequested();
        if (request is CreateTenantCommand)
        {
            if (services.GetRequiredService<IMachinePrincipalAccessor>().IsMachineCaller)
                throw new UnauthorizedAccessException("Human tenant creation cannot borrow machine authority.");
            return ValueTask.FromResult<PolicyMutationScope?>(PolicyMutationScope.TenantCreation);
        }
        if (request is EnsureManagedProviderClientProvisionedCommand provisioning)
        {
            if (provisioning.ManagementRequest is not null || provisioning.OperationId.HasValue
                || provisioning.ExpectedOutboxMessageId.HasValue
                || services.GetRequiredService<IMachinePrincipalAccessor>().IsMachineCaller)
                throw new UnauthorizedAccessException("Human provisioning cannot borrow managed worker authority.");
            // Select the control-plane owner before constructing its DATA and source graph.
            // Managed workers invoke their separate provisioner, not this human command path.
            return ValueTask.FromResult<PolicyMutationScope?>(PolicyMutationScope.Instance);
        }
        // A plan's complete footprint is observed on this one idle combined selection.
        // SMTP presence cannot promote a disposable preflight reader into another owner.
        return ValueTask.FromResult(Select(request, services.GetService<ITenantContextAccessor>()));
    }

    private static PolicyMutationScope? Select(object request, ITenantContextAccessor? tenantContext) => request switch
    {
        UpdateLocalizationGovernanceCommand => PolicyMutationScope.Instance,
        UpdateUserLastActiveTenantCommand => PolicyMutationScope.Instance,
        CompleteInstanceOnboardingCommand => PolicyMutationScope.Instance,
        CompleteLocalInstanceOnboardingCommand => PolicyMutationScope.Instance,
        UpdateEventPolicyCommand => PolicyMutationScope.MixedSettingsInstance,
        CompleteTenantOnboardingCommand => MixedSettingsScope(SettingScope.Tenant, tenantContext),
        UpdateTenantReportingIntakePolicyCommand command when command.TenantId == tenantContext?.TenantId =>
            MixedSettingsScope(SettingScope.Tenant, tenantContext),
        UpdateTenantReportingIntakePolicyCommand =>
            throw new UnauthorizedAccessException("Reporting requires its actual authenticated tenant selection."),
        UpdateSettingCommand command when EmailDeliverySettingKeys.Contains(command.Key) =>
            EmailScope(command.Scope, tenantContext),
        UpdateSettingBatchCommand command when MixedSettingsBatchMutation.RequiresCombinedSource(command) =>
            MixedSettingsScope(command.Scope, tenantContext),
        UpdateSettingBatchCommand command when command.Values.Keys.Any(EmailDeliverySettingKeys.Contains) =>
            EmailScope(command.Scope, tenantContext),
        ResetSettingCommand command when EmailDeliverySettingKeys.Contains(command.Key) =>
            EmailScope(command.Scope, tenantContext),
        LockSettingCommand command when EmailDeliverySettingKeys.Contains(command.Key) =>
            EmailScope(command.Scope, tenantContext),
        UnlockSettingCommand command when EmailDeliverySettingKeys.Contains(command.Key) =>
            EmailScope(command.Scope, tenantContext),
        SetInstanceTenantSettingCommand command when command.TenantId != Guid.Empty
            && EmailDeliverySettingKeys.Contains(command.Key) => PolicyMutationScope.EmailTenant(command.TenantId),
        LockInstanceTenantSettingCommand command when command.TenantId != Guid.Empty
            && EmailDeliverySettingKeys.Contains(command.Key) => PolicyMutationScope.EmailTenant(command.TenantId),
        UnlockInstanceTenantSettingCommand command when command.TenantId != Guid.Empty
            && EmailDeliverySettingKeys.Contains(command.Key) => PolicyMutationScope.EmailTenant(command.TenantId),
        DisableEmailDeliveryCommand { TenantId: null } => PolicyMutationScope.EmailInstance,
        DisableEmailDeliveryCommand command when command.TenantId == tenantContext?.TenantId =>
            EmailScope(SettingScope.Tenant, tenantContext),
        DisableEmailDeliveryCommand => throw new InvalidOperationException("Email mutation requires the authenticated tenant."),
        PreviewEmailDeliveryDisableCommand { TenantId: null } => PolicyMutationScope.EmailInstance,
        PreviewEmailDeliveryDisableCommand command when command.TenantId != Guid.Empty
            && command.TenantId == tenantContext?.TenantId => EmailScope(SettingScope.Tenant, tenantContext),
        PreviewEmailDeliveryDisableCommand => throw new InvalidOperationException("Email preview requires the authenticated tenant."),
        UpdateInstanceSmtpSettingsCommand => PolicyMutationScope.EmailInstance,
        UpdateModuleSettingsCommand or UpdateOrganizationPolicyCommand or UpdateBrandingSettingsCommand
            or UpdateDomainSettingsCommand or UpdateTenantDelegationSettingsCommand
            or UpdateAdminPortalSettingsCommand or UpdateMcpGovernanceSettingsCommand
            or UpdateAiAssistantGovernanceSettingsCommand or UpdateRenderPolicySettingsCommand =>
            PolicyMutationScope.MixedSettingsInstance,
        UpdateSettingCommand { Scope: SettingScope.Instance } => PolicyMutationScope.Instance,
        UpdateSettingBatchCommand { Scope: SettingScope.Instance } => PolicyMutationScope.Instance,
        ResetSettingCommand { Scope: SettingScope.Instance } => PolicyMutationScope.Instance,
        LockSettingCommand { Scope: SettingScope.Instance } => PolicyMutationScope.Instance,
        UnlockSettingCommand { Scope: SettingScope.Instance } => PolicyMutationScope.Instance,
        SetInstanceTenantSettingCommand { TenantId: var tenant } when tenant != Guid.Empty => new(tenant),
        LockInstanceTenantSettingCommand { TenantId: var tenant } when tenant != Guid.Empty => new(tenant),
        UnlockInstanceTenantSettingCommand { TenantId: var tenant } when tenant != Guid.Empty => new(tenant),
        ApplyInstanceTenantPlanAssignmentCommand { TenantId: var tenant } when tenant != Guid.Empty =>
            PolicyMutationScope.MixedSettingsTenant(tenant),
        _ => null
    };

    private static PolicyMutationScope? EmailScope(SettingScope scope, ITenantContextAccessor? tenantContext) =>
        scope switch
        {
            SettingScope.Instance => PolicyMutationScope.EmailInstance,
            SettingScope.Tenant => PolicyMutationScope.EmailTenant(
                tenantContext?.TenantId ?? throw new InvalidOperationException("Email mutation requires the authenticated tenant.")),
            _ => null
        };

    private static PolicyMutationScope MixedSettingsScope(SettingScope scope, ITenantContextAccessor? tenantContext) =>
        scope switch
        {
            SettingScope.Instance => PolicyMutationScope.MixedSettingsInstance,
            SettingScope.Tenant => PolicyMutationScope.MixedSettingsTenant(
                tenantContext?.TenantId ?? throw new InvalidOperationException("Mixed settings require the authenticated tenant.")),
            _ => throw new InvalidOperationException("Mixed settings require an instance or exact tenant administrator scope.")
        };
}

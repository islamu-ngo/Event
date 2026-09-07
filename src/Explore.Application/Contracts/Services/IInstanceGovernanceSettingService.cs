using Explore.Application.DTOs.Instance;
using Explore.Application.Notifications;
using Explore.Application.Settings;

namespace Explore.Application.Contracts.Services;

public interface IInstanceGovernanceSettingService
{
    Task<InstanceGovernanceSettings> ReadSettingsAsync();

    Task<InstanceGovernanceSettings> ReadEffectiveSettingsForTenantAsync(Guid tenantId);

    Task<InstanceGovernanceSettingApplyResult> ApplySettingsAsync(
        Guid? defaultTenantId,
        InstanceGovernanceSettings settings,
        Guid? actorUserId,
        CancellationToken cancellationToken = default);

    Task ApplyModuleSettingsAsync(
        Guid? defaultTenantId,
        ModuleSettingsDto modules,
        Guid? actorUserId,
        CancellationToken cancellationToken = default);
    Task ApplyModuleSettingsPatchAsync(
        Guid? defaultTenantId,
        PatchModuleSettingsDto patch,
        ModuleSettingsDto modules,
        Guid? actorUserId,
        CancellationToken cancellationToken = default);

    Task<PublicationPolicyMutationResult> ApplyEventPolicyAsync(
        EventPolicyDto eventPolicy,
        Guid? actorUserId,
        CancellationToken cancellationToken = default);
    Task<PublicationPolicyMutationResult> ApplyEventPolicyPatchAsync(
        PatchEventPolicyDto patch,
        EventPolicyDto eventPolicy,
        Guid? actorUserId,
        CancellationToken cancellationToken = default);

    Task ApplyOrganizationPolicyAsync(OrganizationPolicyDto orgPolicy, Guid? actorUserId);
    Task ApplyOrganizationPolicyPatchAsync(
        PatchOrganizationPolicyDto patch,
        OrganizationPolicyDto orgPolicy,
        Guid? actorUserId);

    Task ApplyBrandingSettingsAsync(BrandingSettingsDto branding, Guid? actorUserId);
    Task<IReadOnlyList<SettingChangedNotification>> ApplyBrandingSettingsPatchAsync(
        PatchBrandingSettingsDto patch,
        BrandingSettingsDto branding,
        Guid? actorUserId,
        CancellationToken cancellationToken = default);

    Task ApplyDomainSettingsAsync(DomainSettingsDto domains, Guid? actorUserId);
    Task<IReadOnlyList<SettingChangedNotification>> ApplyDomainSettingsPatchAsync(
        PatchDomainSettingsDto patch,
        DomainSettingsDto domains,
        Guid? actorUserId,
        CancellationToken cancellationToken = default);

    Task ApplyTenantDelegationSettingsAsync(TenantDelegationSettingsDto delegation, Guid? actorUserId);
    Task<IReadOnlyList<SettingChangedNotification>> ApplyTenantDelegationSettingsPatchAsync(
        bool isMultiTenant,
        PatchTenantDelegationSettingsDto patch,
        TenantDelegationSettingsDto delegation,
        Guid? actorUserId,
        CancellationToken cancellationToken = default);

    Task ApplyAdminPortalSettingsAsync(AdminPortalSettingsDto adminPortal, Guid? actorUserId);
    Task ApplyAdminPortalSettingsPatchAsync(
        PatchAdminPortalSettingsDto patch,
        AdminPortalSettingsDto adminPortal,
        Guid? actorUserId);

    Task ApplyAiAssistantGovernanceSettingsAsync(AiAssistantGovernanceSettingsDto aiAssistant, Guid? actorUserId);
    Task<IReadOnlyList<SettingChangedNotification>> ApplyAiAssistantGovernanceSettingsPatchAsync(
        PatchAiAssistantGovernanceSettingsDto patch,
        AiAssistantGovernanceSettingsDto aiAssistant,
        Guid? actorUserId,
        CancellationToken cancellationToken = default);

    Task ApplyMcpGovernanceSettingsAsync(McpGovernanceSettingsDto mcp, Guid? actorUserId);
    Task<IReadOnlyList<SettingChangedNotification>> ApplyMcpGovernanceSettingsPatchAsync(
        PatchMcpGovernanceSettingsDto patch,
        McpGovernanceSettingsDto mcp,
        Guid? actorUserId,
        CancellationToken cancellationToken = default);

    Task ApplyRenderPolicySettingsAsync(RenderPolicySettingsDto renderPolicy, Guid? actorUserId);
    Task ApplyRenderPolicySettingsPatchAsync(
        PatchRenderPolicySettingsDto patch,
        RenderPolicySettingsDto renderPolicy,
        Guid? actorUserId);
}

public sealed record InstanceGovernanceSettingApplyResult(
    IReadOnlyList<LocationPrivacyGovernanceMutationResult> LocationPrivacyMutations,
    IReadOnlyList<SettingChangedNotification> DeferredNotifications);

using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Explore.Application.DTOs.Instance;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Models.Common;
using Explore.Domain.Constants;

namespace Explore.Application.Features.Settings;

public sealed partial class TypedSettingsOriginal
{
    internal TypedSettingsOriginal(UpdateModuleSettingsCommand command) : this(new GovernanceSettingsOriginal(command)) { }
    internal TypedSettingsOriginal(UpdateOrganizationPolicyCommand command) : this(new GovernanceSettingsOriginal(command)) { }
    internal TypedSettingsOriginal(UpdateBrandingSettingsCommand command) : this(new GovernanceSettingsOriginal(command)) { }
    internal TypedSettingsOriginal(UpdateDomainSettingsCommand command) : this(new GovernanceSettingsOriginal(command)) { }
    internal TypedSettingsOriginal(UpdateTenantDelegationSettingsCommand command) : this(new GovernanceSettingsOriginal(command)) { }
    internal TypedSettingsOriginal(UpdateAdminPortalSettingsCommand command) : this(new GovernanceSettingsOriginal(command)) { }
    internal TypedSettingsOriginal(UpdateMcpGovernanceSettingsCommand command) : this(new GovernanceSettingsOriginal(command)) { }
    internal TypedSettingsOriginal(UpdateAiAssistantGovernanceSettingsCommand command) : this(new GovernanceSettingsOriginal(command)) { }
    internal TypedSettingsOriginal(UpdateRenderPolicySettingsCommand command) : this(new GovernanceSettingsOriginal(command)) { }

    private TypedSettingsOriginal(GovernanceSettingsOriginal governance)
    {
        Governance = governance;
        Actor = governance.Actor;
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GovernanceSettingsOriginal? Governance { get; }
}

/// <summary>
/// Closed original governance actions. Supplied collections are captured before observation;
/// neither a scalar command nor a caller-provided effect list can construct this original.
/// </summary>
public sealed class GovernanceSettingsOriginal
{
    internal GovernanceSettingsOriginal(object command)
    {
        switch (command)
        {
            case UpdateModuleSettingsCommand value:
                Modules = value with { Patch = value.Patch with { } };
                Actor = value.UserId;
                Producer = "governance-modules";
                break;
            case UpdateOrganizationPolicyCommand value:
                Organization = value with { Patch = value.Patch with { } };
                Actor = value.UserId;
                Producer = "governance-organization";
                break;
            case UpdateBrandingSettingsCommand value:
                Branding = value with { Patch = value.Patch with { } };
                Actor = value.UserId;
                Producer = "governance-branding";
                break;
            case UpdateDomainSettingsCommand value:
                Domains = value with { Patch = value.Patch with { } };
                Actor = value.UserId;
                Producer = "governance-domains";
                break;
            case UpdateTenantDelegationSettingsCommand value:
                Delegation = value with { Patch = value.Patch with { } };
                Actor = value.UserId;
                Producer = "governance-delegation";
                break;
            case UpdateAdminPortalSettingsCommand value:
                AdminPortal = value with { Patch = value.Patch with { } };
                Actor = value.UserId;
                Producer = "governance-admin-portal";
                break;
            case UpdateMcpGovernanceSettingsCommand value:
                Mcp = value with { Patch = value.Patch with { } };
                Actor = value.UserId;
                Producer = "governance-mcp";
                break;
            case UpdateAiAssistantGovernanceSettingsCommand value:
                Ai = value with
                {
                    Patch = value.Patch with
                    {
                        ProviderConfiguration = value.Patch.ProviderConfiguration.HasValue
                            ? OptionalUpdate<AiAssistantProviderConfigurationWriteDto>.Set(
                                value.Patch.ProviderConfiguration.Value is { } provider
                                    ? provider with { AllowedModelIds = provider.AllowedModelIds.ToImmutableArray() }
                                    : null!)
                            : OptionalUpdate<AiAssistantProviderConfigurationWriteDto>.Unspecified()
                    }
                };
                Actor = value.UserId;
                Producer = "governance-ai";
                break;
            case UpdateRenderPolicySettingsCommand value:
                RenderPolicy = value with { Patch = value.Patch with { } };
                Actor = value.UserId;
                Producer = "governance-render-policy";
                break;
            default:
                throw new UnauthorizedAccessException("An exact original governance PATCH is required.");
        }
    }

    public UpdateModuleSettingsCommand? Modules { get; }
    public UpdateOrganizationPolicyCommand? Organization { get; }
    public UpdateBrandingSettingsCommand? Branding { get; }
    public UpdateDomainSettingsCommand? Domains { get; }
    public UpdateTenantDelegationSettingsCommand? Delegation { get; }
    public UpdateAdminPortalSettingsCommand? AdminPortal { get; }
    public UpdateMcpGovernanceSettingsCommand? Mcp { get; }
    public UpdateAiAssistantGovernanceSettingsCommand? Ai { get; }
    public UpdateRenderPolicySettingsCommand? RenderPolicy { get; }
    public Guid Actor { get; }
    public string Producer { get; }

    public bool SuppliesValue(string key)
    {
        if (Branding is { Patch: var branding })
            return key switch
            {
                GovernanceSettingKeys.Branding.DisplayName => branding.DefaultBrandDisplayName.HasValue,
                GovernanceSettingKeys.Branding.LogoUrl => branding.DefaultBrandLogoUrl.HasValue,
                GovernanceSettingKeys.Branding.FaviconUrl => branding.DefaultBrandFaviconUrl.HasValue,
                GovernanceSettingKeys.Branding.CustomCssUrl => branding.DefaultBrandCustomCssUrl.HasValue,
                _ => false
            };
        if (Domains is not null && key is GovernanceSettingKeys.Domains.TenantSubdomain or GovernanceSettingKeys.Domains.TenantCustomDomain)
            return false;
        if (Delegation is { Patch: var delegation } && key == GovernanceSettingKeys.Routing.DefaultPublicHomePage)
            return delegation.DefaultPublicHomePage.HasValue;
        if (Mcp is { Patch: var mcp })
            return key switch
            {
                GovernanceSettingKeys.Mcp.Enabled => mcp.Enabled.HasValue,
                GovernanceSettingKeys.Mcp.EnableLegacySse => mcp.EnableLegacySse.HasValue,
                _ => true
            };
        if (Ai is { Patch: var ai })
            return key switch
            {
                GovernanceSettingKeys.AiAssistant.Enabled => ai.Enabled.HasValue,
                GovernanceSettingKeys.AiAssistant.AllowAnonymousAccess => ai.AllowAnonymousAccess.HasValue,
                GovernanceSettingKeys.AiAssistant.ToolProposalsEnabled => ai.ToolProposalsEnabled.HasValue,
                GovernanceSettingKeys.TenantDelegation.LockAiAssistant => true,
                _ => ai.ProviderConfiguration.HasValue
            };
        return true;
    }
}

using System.Collections.Immutable;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Infrastructure.Ai;
using Explore.Application.DTOs.Instance.Validators;
using Explore.Application.Features.Settings;
using Explore.Application.Models.Common;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;

namespace Explore.Application.Services;

public partial class InstanceGovernanceSettingService
{
    /// <summary>
    /// Compiles only the supplied members of an exact original PATCH against acquired rows.
    /// Lock-only writes preserve stored bytes, including values that normalize differently.
    /// This is input compilation, not permission to write: the selected store owns admission.
    /// </summary>
    public static ImmutableArray<TypedSettingsEffect> CompileGovernanceOriginal(
        TypedSettingsOriginal original, IReadOnlyDictionary<string, SystemSetting> system)
    {
        var governance = original.Governance
            ?? throw new UnauthorizedAccessException("The original governance PATCH is required.");
        var resolved = SettingRegistry.All
            .Select(definition => HierarchicalSettingMerge.Resolve(definition.Key, system))
            .OfType<ResolvedSetting>().ToDictionary(value => value.Key, StringComparer.Ordinal);
        var settings = MapOriginalSettings(resolved);
        var effects = ImmutableArray.CreateBuilder<TypedSettingsEffect>();

        void Value<T>(string key, OptionalUpdate<T> input, Func<T, string>? normalize = null)
        {
            if (input.HasValue)
                effects.Add(new(key, normalize is null ? SettingValueSerializer.Serialize(input.Value) : normalize(input.Value!), false));
        }
        void Pair(string key, bool supplied, string value, OptionalUpdate<bool> locked, bool currentLock)
        {
            if (!supplied && !locked.HasValue) return;
            effects.Add(new(key, supplied ? value : system.GetValueOrDefault(key)?.Value ?? value,
                locked.HasValue ? locked.Value : currentLock));
        }

        if (governance.Modules is { Patch: var module })
        {
            Value(GovernanceSettingKeys.Modules.IslamicEnabled, module.EnableIslamicModule);
            Value(GovernanceSettingKeys.Modules.TechEnabled, module.EnableTechModule);
        }
        else if (governance.Organization is { Patch: var organization })
        {
            Value(GovernanceSettingKeys.Organizations.VerificationRequired, organization.RequireOrganizationVerification);
            Value(GovernanceSettingKeys.Organizations.TenantCanOmitVerification, organization.AllowTenantToOmitVerification);
            Value(GovernanceSettingKeys.Organizations.SelfRegistrationEnabled, organization.AllowOrganizationSelfRegistration);
            Value(GovernanceSettingKeys.Groups.SelfRegistrationEnabled, organization.AllowGroupSelfRegistration);
        }
        else if (governance.Branding is { Patch: var branding })
        {
            var current = settings.Branding;
            Pair(GovernanceSettingKeys.Branding.DisplayName, branding.DefaultBrandDisplayName.HasValue,
                SettingValueSerializer.Serialize(NormalizeRequiredDisplayName(branding.DefaultBrandDisplayName.HasValue
                    ? branding.DefaultBrandDisplayName.Value : current.DefaultBrandDisplayName)),
                branding.LockTenantBrandDisplayName, current.LockTenantBrandDisplayName);
            Pair(GovernanceSettingKeys.Branding.LogoUrl, branding.DefaultBrandLogoUrl.HasValue,
                SettingValueSerializer.Serialize(NormalizeOptionalUrl(branding.DefaultBrandLogoUrl.HasValue
                    ? branding.DefaultBrandLogoUrl.Value : current.DefaultBrandLogoUrl)),
                branding.LockTenantBrandLogoUrl, current.LockTenantBrandLogoUrl);
            Pair(GovernanceSettingKeys.Branding.FaviconUrl, branding.DefaultBrandFaviconUrl.HasValue,
                SettingValueSerializer.Serialize(NormalizeOptionalUrl(branding.DefaultBrandFaviconUrl.HasValue
                    ? branding.DefaultBrandFaviconUrl.Value : current.DefaultBrandFaviconUrl)),
                branding.LockTenantBrandFaviconUrl, current.LockTenantBrandFaviconUrl);
            Pair(GovernanceSettingKeys.Branding.CustomCssUrl, branding.DefaultBrandCustomCssUrl.HasValue,
                SettingValueSerializer.Serialize(NormalizeOptionalUrl(branding.DefaultBrandCustomCssUrl.HasValue
                    ? branding.DefaultBrandCustomCssUrl.Value : current.DefaultBrandCustomCssUrl)),
                branding.LockTenantBrandCustomCssUrl, current.LockTenantBrandCustomCssUrl);
        }
        else if (governance.Domains is { Patch: var domain })
        {
            Value(GovernanceSettingKeys.Domains.InstanceBaseDomain, domain.InstanceBaseDomain,
                value => SettingValueSerializer.Serialize(NormalizeOptionalHost(value)));
            Value(GovernanceSettingKeys.Domains.AdminHost, domain.AdminHost,
                value => SettingValueSerializer.Serialize(NormalizeOptionalHost(value)));
            Value(GovernanceSettingKeys.Domains.AllowTenantCustomDomain, domain.AllowTenantCustomDomains);
            Pair(GovernanceSettingKeys.Domains.TenantSubdomain, false, "\"\"", domain.LockTenantSubdomain, false);
            Pair(GovernanceSettingKeys.Domains.TenantCustomDomain, false, "\"\"", domain.LockTenantCustomDomain, false);
        }
        else if (governance.Delegation is { Patch: var delegation })
        {
            bool multiTenant = settings.DeploymentMode.Mode == DeploymentMode.MultiTenant;
            Value(GovernanceSettingKeys.Tenants.SelfServiceRegistration, delegation.AllowTenantSelfServiceRegistration,
                value => SettingValueSerializer.Serialize(multiTenant && value));
            Value(GovernanceSettingKeys.Tenants.WhiteLabelingEnabled, delegation.AllowTenantWhiteLabeling,
                value => SettingValueSerializer.Serialize(multiTenant && value));
            Pair(GovernanceSettingKeys.Routing.DefaultPublicHomePage, delegation.DefaultPublicHomePage.HasValue,
                SettingValueSerializer.Serialize(NormalizeHomePage(delegation.DefaultPublicHomePage.HasValue
                    ? delegation.DefaultPublicHomePage.Value : settings.TenantDelegation.DefaultPublicHomePage)),
                delegation.LockTenantHomePagePreference, settings.TenantDelegation.LockTenantHomePagePreference);
            Value(GovernanceSettingKeys.TenantDelegation.LockSmtp, delegation.LockTenantSmtp);
            Value(GovernanceSettingKeys.TenantDelegation.LockStorage, delegation.LockTenantStorage);
            Value(GovernanceSettingKeys.TenantDelegation.LockAnalytics, delegation.LockTenantAnalytics);
            Value(GovernanceSettingKeys.TenantDelegation.LockAiAssistant, delegation.LockTenantAiAssistant);
        }
        else if (governance.AdminPortal is { Patch: var portal })
        {
            Value(GovernanceSettingKeys.AdminPortal.Enabled, portal.Enabled);
            Value(GovernanceSettingKeys.AdminPortal.PublicUrl, portal.PublicUrl,
                value => SettingValueSerializer.Serialize(NormalizeOptionalUrl(value)));
            Value(GovernanceSettingKeys.AdminPortal.AllowTenantAdminAccess, portal.AllowTenantAdminAccess);
        }
        else if (governance.Mcp is { Patch: var mcp })
        {
            Pair(GovernanceSettingKeys.Mcp.Enabled, mcp.Enabled.HasValue,
                SettingValueSerializer.Serialize(mcp.Enabled.HasValue ? mcp.Enabled.Value : settings.Mcp.Enabled),
                mcp.LockTenantMcp, settings.Mcp.LockTenantMcp);
            Pair(GovernanceSettingKeys.Mcp.EnableLegacySse, mcp.EnableLegacySse.HasValue,
                SettingValueSerializer.Serialize(mcp.EnableLegacySse.HasValue ? mcp.EnableLegacySse.Value : settings.Mcp.EnableLegacySse),
                mcp.LockTenantMcpLegacySse, settings.Mcp.LockTenantMcpLegacySse);
            Value(GovernanceSettingKeys.TenantDelegation.LockMcp, mcp.LockTenantMcp);
            Value(GovernanceSettingKeys.TenantDelegation.LockMcpLegacySse, mcp.LockTenantMcpLegacySse);
        }
        else if (governance.Ai is { Patch: var ai })
        {
            var current = settings.AiAssistant;
            if (ai.Enabled.HasValue) current.Enabled = ai.Enabled.Value;
            if (ai.ProviderConfiguration.HasValue)
            {
                var provider = ai.ProviderConfiguration.Value
                    ?? throw new Exceptions.ValidationException(new FluentValidation.Results.ValidationResult(
                        [new FluentValidation.Results.ValidationFailure(nameof(ai.ProviderConfiguration), "AI provider configuration must be complete.")]));
                current.Provider = provider.Provider;
                current.EndpointUrl = provider.EndpointUrl;
                current.CredentialScope = provider.CredentialScope;
                current.ModelId = provider.ModelId;
                current.AllowedModelIds = provider.AllowedModelIds;
            }
            if (ai.ProviderConfiguration.HasValue || ai.Enabled is { HasValue: true, Value: true })
                ValidateAiAssistantCredentialScope(current);
            string providerName = NormalizeAiAssistantProvider(current.Provider, current.Enabled);
            bool compatible = providerName is AiProviderDefaults.ProviderOpenAiCompatible or AiProviderDefaults.ProviderAnthropicCompatible;
            bool external = compatible || providerName is AiProviderDefaults.ProviderOpenAi or AiProviderDefaults.ProviderAnthropic;
            string model = external ? current.ModelId?.Trim() ?? string.Empty : string.Empty;
            var modelIds = external ? NormalizeAiModelIds([model], current.AllowedModelIds) : [];
            Pair(GovernanceSettingKeys.AiAssistant.Enabled, ai.Enabled.HasValue,
                SettingValueSerializer.Serialize(current.Enabled), ai.LockTenantAiAssistant, current.LockTenantAiAssistant);
            Pair(GovernanceSettingKeys.AiAssistant.Provider, ai.ProviderConfiguration.HasValue,
                SettingValueSerializer.Serialize(providerName), ai.LockTenantAiAssistant, current.LockTenantAiAssistant);
            Pair(GovernanceSettingKeys.AiAssistant.EndpointUrl, ai.ProviderConfiguration.HasValue,
                SettingValueSerializer.Serialize(compatible ? NormalizeOptionalUrl(current.EndpointUrl) : string.Empty),
                ai.LockTenantAiAssistant, current.LockTenantAiAssistant);
            Pair(GovernanceSettingKeys.AiAssistant.CredentialScope, ai.ProviderConfiguration.HasValue,
                SettingValueSerializer.Serialize(ai.ProviderConfiguration.HasValue
                    ? external ? current.CredentialScope : "none" : current.CredentialScope),
                ai.LockTenantAiAssistant, current.LockTenantAiAssistant);
            Pair(GovernanceSettingKeys.AiAssistant.ModelId, ai.ProviderConfiguration.HasValue,
                SettingValueSerializer.Serialize(model), ai.LockTenantAiAssistant, current.LockTenantAiAssistant);
            Pair(GovernanceSettingKeys.AiAssistant.AllowedModelIds, ai.ProviderConfiguration.HasValue,
                SettingValueSerializer.Serialize(modelIds), ai.LockTenantAiAssistant, current.LockTenantAiAssistant);
            Pair(GovernanceSettingKeys.AiAssistant.AllowAnonymousAccess, ai.AllowAnonymousAccess.HasValue,
                SettingValueSerializer.Serialize(ai.AllowAnonymousAccess.HasValue ? ai.AllowAnonymousAccess.Value : current.AllowAnonymousAccess),
                ai.LockTenantAiAssistant, current.LockTenantAiAssistant);
            Pair(GovernanceSettingKeys.AiAssistant.ToolProposalsEnabled, ai.ToolProposalsEnabled.HasValue,
                SettingValueSerializer.Serialize(ai.ToolProposalsEnabled.HasValue ? ai.ToolProposalsEnabled.Value : current.ToolProposalsEnabled),
                ai.LockTenantAiAssistant, current.LockTenantAiAssistant);
            Value(GovernanceSettingKeys.TenantDelegation.LockAiAssistant, ai.LockTenantAiAssistant);
        }
        else if (governance.RenderPolicy is { Patch: var patch })
        {
            var policy = settings.RenderPolicy;
            if (patch.RenderPolicyPreset.HasValue) policy.RenderPolicyPreset = patch.RenderPolicyPreset.Value ?? string.Empty;
            if (patch.EnableAdvancedRenderPolicyOverrides.HasValue) policy.EnableAdvancedRenderPolicyOverrides = patch.EnableAdvancedRenderPolicyOverrides.Value;
            if (patch.GlobalRenderMode.HasValue) policy.GlobalRenderMode = patch.GlobalRenderMode.Value ?? string.Empty;
            if (patch.GlobalPrerenderEnabled.HasValue) policy.GlobalPrerenderEnabled = patch.GlobalPrerenderEnabled.Value;
            if (patch.PublicSeoRenderMode.HasValue) policy.PublicSeoRenderMode = patch.PublicSeoRenderMode.Value ?? string.Empty;
            if (patch.PublicSeoPrerenderEnabled.HasValue) policy.PublicSeoPrerenderEnabled = patch.PublicSeoPrerenderEnabled.Value;
            if (patch.OperationalRenderMode.HasValue) policy.OperationalRenderMode = patch.OperationalRenderMode.Value ?? string.Empty;
            if (patch.OperationalPrerenderEnabled.HasValue) policy.OperationalPrerenderEnabled = patch.OperationalPrerenderEnabled.Value;
            if (patch.AdminRenderMode.HasValue) policy.AdminRenderMode = patch.AdminRenderMode.Value ?? string.Empty;
            if (patch.AdminPrerenderEnabled.HasValue) policy.AdminPrerenderEnabled = patch.AdminPrerenderEnabled.Value;
            if (patch.OnboardingRenderMode.HasValue) policy.OnboardingRenderMode = patch.OnboardingRenderMode.Value ?? string.Empty;
            if (patch.OnboardingPrerenderEnabled.HasValue) policy.OnboardingPrerenderEnabled = patch.OnboardingPrerenderEnabled.Value;
            if (patch.AllowTenantRenderPolicyOverride.HasValue) policy.AllowTenantRenderPolicyOverride = patch.AllowTenantRenderPolicyOverride.Value;
            if (patch.LockTenantPublicSeoRenderPolicy.HasValue) policy.LockTenantPublicSeoRenderPolicy = patch.LockTenantPublicSeoRenderPolicy.Value;
            if (patch.LockTenantOperationalRenderPolicy.HasValue) policy.LockTenantOperationalRenderPolicy = patch.LockTenantOperationalRenderPolicy.Value;
            if (patch.LockTenantAdminRenderPolicy.HasValue) policy.LockTenantAdminRenderPolicy = patch.LockTenantAdminRenderPolicy.Value;
            var validation = new RenderPolicySettingsDtoValidator().Validate(policy);
            if (!validation.IsValid) throw new Exceptions.ValidationException(validation);
            NormalizeRenderPolicySettings(policy);
            Value(GovernanceSettingKeys.Routing.RenderPolicy.Preset, patch.RenderPolicyPreset, _ => SettingValueSerializer.Serialize(policy.RenderPolicyPreset));
            Value(GovernanceSettingKeys.Routing.RenderPolicy.AdvancedEnabled, patch.EnableAdvancedRenderPolicyOverrides, _ => SettingValueSerializer.Serialize(policy.EnableAdvancedRenderPolicyOverrides));
            Value(GovernanceSettingKeys.Routing.RenderPolicy.Fallback.RenderMode, patch.GlobalRenderMode, _ => SettingValueSerializer.Serialize(policy.GlobalRenderMode));
            Value(GovernanceSettingKeys.Routing.RenderPolicy.Fallback.PrerenderEnabled, patch.GlobalPrerenderEnabled, _ => SettingValueSerializer.Serialize(policy.GlobalPrerenderEnabled));
            Value(GovernanceSettingKeys.Routing.RenderPolicy.PublicSeo.RenderMode, patch.PublicSeoRenderMode, _ => SettingValueSerializer.Serialize(policy.PublicSeoRenderMode));
            Value(GovernanceSettingKeys.Routing.RenderPolicy.PublicSeo.PrerenderEnabled, patch.PublicSeoPrerenderEnabled, _ => SettingValueSerializer.Serialize(policy.PublicSeoPrerenderEnabled));
            Value(GovernanceSettingKeys.Routing.RenderPolicy.Operational.RenderMode, patch.OperationalRenderMode, _ => SettingValueSerializer.Serialize(policy.OperationalRenderMode));
            Value(GovernanceSettingKeys.Routing.RenderPolicy.Operational.PrerenderEnabled, patch.OperationalPrerenderEnabled, _ => SettingValueSerializer.Serialize(policy.OperationalPrerenderEnabled));
            Value(GovernanceSettingKeys.Routing.RenderPolicy.Admin.RenderMode, patch.AdminRenderMode, _ => SettingValueSerializer.Serialize(policy.AdminRenderMode));
            Value(GovernanceSettingKeys.Routing.RenderPolicy.Admin.PrerenderEnabled, patch.AdminPrerenderEnabled, _ => SettingValueSerializer.Serialize(policy.AdminPrerenderEnabled));
            Value(GovernanceSettingKeys.Routing.RenderPolicy.Onboarding.RenderMode, patch.OnboardingRenderMode, _ => SettingValueSerializer.Serialize(policy.OnboardingRenderMode));
            Value(GovernanceSettingKeys.Routing.RenderPolicy.Onboarding.PrerenderEnabled, patch.OnboardingPrerenderEnabled, _ => SettingValueSerializer.Serialize(policy.OnboardingPrerenderEnabled));
            Value(GovernanceSettingKeys.Routing.RenderPolicy.AllowTenantOverride, patch.AllowTenantRenderPolicyOverride);
            Value(GovernanceSettingKeys.Routing.RenderPolicy.LockTenantPublicSeo, patch.LockTenantPublicSeoRenderPolicy);
            Value(GovernanceSettingKeys.Routing.RenderPolicy.LockTenantOperational, patch.LockTenantOperationalRenderPolicy);
            Value(GovernanceSettingKeys.Routing.RenderPolicy.LockTenantAdmin, patch.LockTenantAdminRenderPolicy);
        }
        else throw new UnauthorizedAccessException("The exact governance original is missing.");
        return effects.ToImmutable();
    }
}

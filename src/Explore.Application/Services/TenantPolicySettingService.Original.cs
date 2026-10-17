using System.Collections.Immutable;
using System.Text.Json;
using Explore.Application.DTOs.TenantPolicy;
using Explore.Application.Features.Settings;
using Explore.Domain;
using Explore.Domain.Constants;

namespace Explore.Application.Services;

public partial class TenantPolicySettingService
{
    public static ImmutableArray<string> OriginalReadKeys => [.. TenantPolicySettingKeys];

    public static ImmutableArray<string> OriginalWriteKeys =>
    [
        GovernanceSettingKeys.Events.RequireApproval,
        GovernanceSettingKeys.Events.UserSubmissionEnabled,
        GovernanceSettingKeys.Events.OrganizationSubmissionEnabled,
        GovernanceSettingKeys.Events.GroupSubmissionEnabled,
        GovernanceSettingKeys.Events.CardClickOpensDetailPage,
        GovernanceSettingKeys.Organizations.VerificationRequired,
        GovernanceSettingKeys.Organizations.SelfRegistrationEnabled,
        GovernanceSettingKeys.Groups.SelfRegistrationEnabled,
        GovernanceSettingKeys.Routing.DefaultPublicHomePage,
        GovernanceSettingKeys.Domains.TenantSubdomain,
        GovernanceSettingKeys.Domains.TenantCustomDomain,
        GovernanceSettingKeys.PublicExperience.AnnouncementBarEnabled,
        GovernanceSettingKeys.PublicExperience.AnnouncementBarMessage,
        GovernanceSettingKeys.PublicExperience.AnnouncementBarLinkText,
        GovernanceSettingKeys.PublicExperience.AnnouncementBarLinkUrl,
        GovernanceSettingKeys.PublicExperience.AnnouncementBarRevision,
        GovernanceSettingKeys.Policies.CommunityGuidelinesContent,
        GovernanceSettingKeys.Routing.RenderPolicy.Preset,
        GovernanceSettingKeys.Routing.RenderPolicy.AdvancedEnabled,
        GovernanceSettingKeys.Routing.RenderPolicy.Fallback.RenderMode,
        GovernanceSettingKeys.Routing.RenderPolicy.Fallback.PrerenderEnabled,
        GovernanceSettingKeys.Routing.RenderPolicy.PublicSeo.RenderMode,
        GovernanceSettingKeys.Routing.RenderPolicy.PublicSeo.PrerenderEnabled,
        GovernanceSettingKeys.Routing.RenderPolicy.Operational.RenderMode,
        GovernanceSettingKeys.Routing.RenderPolicy.Operational.PrerenderEnabled,
        GovernanceSettingKeys.Routing.RenderPolicy.Admin.RenderMode,
        GovernanceSettingKeys.Routing.RenderPolicy.Admin.PrerenderEnabled,
        GovernanceSettingKeys.AiAssistant.Enabled,
        GovernanceSettingKeys.AiAssistant.Provider,
        GovernanceSettingKeys.AiAssistant.EndpointUrl,
        GovernanceSettingKeys.AiAssistant.CredentialScope,
        GovernanceSettingKeys.AiAssistant.ModelId,
        GovernanceSettingKeys.AiAssistant.AllowedModelIds,
        GovernanceSettingKeys.AiAssistant.AllowAnonymousAccess,
        GovernanceSettingKeys.Mcp.Enabled,
        GovernanceSettingKeys.Mcp.EnableLegacySse
    ];

    /// <summary>
    /// Compiles the original onboarding policy only from its acquired ceilings, tenant slug,
    /// and revision. The result is data; it cannot acquire or complete a persistence owner.
    /// </summary>
    public static ImmutableArray<TypedSettingsEffect> CompileOriginal(
        UpdateTenantPolicyRequest settings, IReadOnlyDictionary<string, SystemSetting> system,
        string? tenantSlug, string? announcementRevision)
    {
        var effects = ImmutableArray.CreateBuilder<TypedSettingsEffect>();
        SystemSetting? Row(string key) => system.GetValueOrDefault(key);
        bool Can(string key, bool allowed = true) => allowed && Row(key)?.IsLocked != true;
        bool Boolean(string key, bool fallback) => DeserializeBoolean(Row(key)?.Value, fallback);
        void Set(string key, string? value, bool allowed = true) =>
            effects.Add(new(key, Can(key, allowed) ? value : null));
        void Bool(string key, bool value, bool allowed = true) =>
            Set(key, JsonSerializer.Serialize(value), allowed);
        void Text(string key, string? value, bool allowed = true) =>
            Set(key, string.IsNullOrWhiteSpace(value) ? null : JsonSerializer.Serialize(value.Trim()), allowed);

        bool multi = DeserializeString(Row(GovernanceSettingKeys.Deployment.Mode)?.Value, "SingleTenant")
            .Equals("MultiTenant", StringComparison.OrdinalIgnoreCase);
        bool ai = !multi || !Boolean(GovernanceSettingKeys.TenantDelegation.LockAiAssistant, true);
        string provider = Can(GovernanceSettingKeys.AiAssistant.Provider, ai)
            ? NormalizeAiAssistantProvider(settings.AiAssistantProvider, settings.AiAssistantEnabled)
            : DeserializeString(Row(GovernanceSettingKeys.AiAssistant.Provider)?.Value, "none");
        bool official = provider is "openai" or "anthropic";
        bool compatible = provider is "openai-compatible" or "anthropic-compatible";
        bool external = official || compatible;
        var models = external ? NormalizeAiModelIds([settings.AiAssistantModelId], settings.AiAssistantAllowedModelIds) : [];
        if (ai)
        {
            string Effective(string key, string value, string fallback) =>
                Can(key) ? value : DeserializeString(Row(key)?.Value, fallback);
            var effective = settings with
            {
                AiAssistantEnabled = Can(GovernanceSettingKeys.AiAssistant.Enabled)
                    ? settings.AiAssistantEnabled : Boolean(GovernanceSettingKeys.AiAssistant.Enabled, false),
                AiAssistantProvider = Effective(GovernanceSettingKeys.AiAssistant.Provider, provider, "none"),
                AiAssistantEndpointUrl = Effective(GovernanceSettingKeys.AiAssistant.EndpointUrl, settings.AiAssistantEndpointUrl, string.Empty),
                AiAssistantCredentialScope = Effective(GovernanceSettingKeys.AiAssistant.CredentialScope, settings.AiAssistantCredentialScope, "none"),
                AiAssistantModelId = Effective(GovernanceSettingKeys.AiAssistant.ModelId, settings.AiAssistantModelId, string.Empty)
            };
            ValidateAiAssistantSettings(effective, effective.AiAssistantProvider,
                Can(GovernanceSettingKeys.AiAssistant.Provider)
                || Can(GovernanceSettingKeys.AiAssistant.EndpointUrl) && compatible
                    && !string.IsNullOrWhiteSpace(settings.AiAssistantEndpointUrl));
        }

        Bool(GovernanceSettingKeys.Events.RequireApproval, settings.RequireEventApproval);
        Bool(GovernanceSettingKeys.Events.UserSubmissionEnabled, settings.AllowUserSubmittedEvents);
        Bool(GovernanceSettingKeys.Events.OrganizationSubmissionEnabled, settings.AllowOrganizationSubmittedEvents);
        Bool(GovernanceSettingKeys.Events.GroupSubmissionEnabled, settings.AllowGroupSubmittedEvents);
        Bool(GovernanceSettingKeys.Events.CardClickOpensDetailPage, settings.EventCardClickOpensDetailPage);
        bool omitVerification = Boolean(GovernanceSettingKeys.Organizations.TenantCanOmitVerification, false)
            && Can(GovernanceSettingKeys.Organizations.VerificationRequired);
        Bool(GovernanceSettingKeys.Organizations.VerificationRequired,
            omitVerification ? settings.RequireOrganizationVerification
                : Boolean(GovernanceSettingKeys.Organizations.VerificationRequired, true), omitVerification);
        Bool(GovernanceSettingKeys.Organizations.SelfRegistrationEnabled, settings.AllowOrganizationSelfRegistration);
        Bool(GovernanceSettingKeys.Groups.SelfRegistrationEnabled, settings.AllowGroupSelfRegistration);
        Text(GovernanceSettingKeys.Routing.DefaultPublicHomePage, NormalizeHomePage(settings.PreferredHomePage));
        Text(GovernanceSettingKeys.Domains.TenantSubdomain,
            NormalizeSubdomain(settings.Subdomain) ?? NormalizeSubdomain(tenantSlug) ?? "default");
        Text(GovernanceSettingKeys.Domains.TenantCustomDomain, NormalizeOptionalHost(settings.CustomDomain),
            Boolean(GovernanceSettingKeys.Domains.AllowTenantCustomDomain, true));
        Bool(GovernanceSettingKeys.PublicExperience.AnnouncementBarEnabled, settings.AnnouncementBarEnabled);
        Text(GovernanceSettingKeys.PublicExperience.AnnouncementBarMessage, settings.AnnouncementBarMessage);
        Text(GovernanceSettingKeys.PublicExperience.AnnouncementBarLinkText, settings.AnnouncementBarLinkText);
        Text(GovernanceSettingKeys.PublicExperience.AnnouncementBarLinkUrl, settings.AnnouncementBarLinkUrl);
        if (settings.ForceAnnouncementBarRedisplay)
            Set(GovernanceSettingKeys.PublicExperience.AnnouncementBarRevision,
                JsonSerializer.Serialize(DeserializeInteger(announcementRevision, 0) + 1));
        Text(GovernanceSettingKeys.Policies.CommunityGuidelinesContent, settings.CommunityGuidelinesContent);

        bool render = !multi || Boolean(GovernanceSettingKeys.Routing.RenderPolicy.AllowTenantOverride, false);
        bool seo = render && (!multi || !Boolean(GovernanceSettingKeys.Routing.RenderPolicy.LockTenantPublicSeo, false));
        bool operational = render && (!multi || !Boolean(GovernanceSettingKeys.Routing.RenderPolicy.LockTenantOperational, false));
        bool admin = render && (!multi || !Boolean(GovernanceSettingKeys.Routing.RenderPolicy.LockTenantAdmin, false));
        Text(GovernanceSettingKeys.Routing.RenderPolicy.Preset, settings.RenderPolicyPreset, render);
        Bool(GovernanceSettingKeys.Routing.RenderPolicy.AdvancedEnabled, settings.EnableAdvancedRenderPolicyOverrides, render);
        Text(GovernanceSettingKeys.Routing.RenderPolicy.Fallback.RenderMode, settings.GlobalRenderMode, render);
        Bool(GovernanceSettingKeys.Routing.RenderPolicy.Fallback.PrerenderEnabled, settings.GlobalPrerenderEnabled, render);
        Text(GovernanceSettingKeys.Routing.RenderPolicy.PublicSeo.RenderMode, settings.PublicSeoRenderMode, seo);
        Bool(GovernanceSettingKeys.Routing.RenderPolicy.PublicSeo.PrerenderEnabled, settings.PublicSeoPrerenderEnabled, seo);
        Text(GovernanceSettingKeys.Routing.RenderPolicy.Operational.RenderMode, settings.OperationalRenderMode, operational);
        Bool(GovernanceSettingKeys.Routing.RenderPolicy.Operational.PrerenderEnabled, settings.OperationalPrerenderEnabled, operational);
        Text(GovernanceSettingKeys.Routing.RenderPolicy.Admin.RenderMode, settings.AdminRenderMode, admin);
        Bool(GovernanceSettingKeys.Routing.RenderPolicy.Admin.PrerenderEnabled, settings.AdminPrerenderEnabled, admin);
        Bool(GovernanceSettingKeys.AiAssistant.Enabled, settings.AiAssistantEnabled, ai);
        Text(GovernanceSettingKeys.AiAssistant.Provider, provider, ai);
        Text(GovernanceSettingKeys.AiAssistant.EndpointUrl, compatible ? settings.AiAssistantEndpointUrl : string.Empty, ai);
        Text(GovernanceSettingKeys.AiAssistant.CredentialScope, settings.AiAssistantCredentialScope, ai);
        Text(GovernanceSettingKeys.AiAssistant.ModelId, external ? settings.AiAssistantModelId : string.Empty, ai);
        Set(GovernanceSettingKeys.AiAssistant.AllowedModelIds,
            models.Count == 0 ? null : JsonSerializer.Serialize(models), ai && external);
        Bool(GovernanceSettingKeys.AiAssistant.AllowAnonymousAccess, settings.AiAssistantAllowAnonymousAccess, ai);
        Bool(GovernanceSettingKeys.Mcp.Enabled, settings.McpEnabled,
            !multi || !Boolean(GovernanceSettingKeys.TenantDelegation.LockMcp, true));
        Bool(GovernanceSettingKeys.Mcp.EnableLegacySse, settings.McpEnableLegacySse,
            !multi || !Boolean(GovernanceSettingKeys.TenantDelegation.LockMcpLegacySse, true));
        return effects.ToImmutable();
    }
}

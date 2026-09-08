using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.TenantPolicy;
using Explore.Application.Exceptions;
using Explore.Application.Notifications;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using FluentValidation.Results;

namespace Explore.Application.Services;

public partial class TenantPolicySettingService
{
    private static readonly string[] TenantPolicySettingKeys =
    [
        .. PublicationPolicySettingKeys.All,
        GovernanceSettingKeys.AiAssistant.AllowAnonymousAccess,
        GovernanceSettingKeys.AiAssistant.AllowedModelIds,
        GovernanceSettingKeys.AiAssistant.ApiKey,
        GovernanceSettingKeys.AiAssistant.Enabled,
        GovernanceSettingKeys.AiAssistant.EndpointUrl,
        GovernanceSettingKeys.AiAssistant.ModelId,
        GovernanceSettingKeys.AiAssistant.Provider,
        GovernanceSettingKeys.Deployment.Mode,
        GovernanceSettingKeys.Domains.AllowTenantCustomDomain,
        GovernanceSettingKeys.Domains.TenantCustomDomain,
        GovernanceSettingKeys.Domains.TenantSubdomain,
        GovernanceSettingKeys.Events.CardClickOpensDetailPage,
        GovernanceSettingKeys.Groups.SelfRegistrationEnabled,
        GovernanceSettingKeys.Mcp.EnableLegacySse,
        GovernanceSettingKeys.Mcp.Enabled,
        GovernanceSettingKeys.Organizations.SelfRegistrationEnabled,
        GovernanceSettingKeys.Organizations.TenantCanOmitVerification,
        GovernanceSettingKeys.Organizations.VerificationRequired,
        GovernanceSettingKeys.Policies.CommunityGuidelinesContent,
        GovernanceSettingKeys.PublicExperience.AnnouncementBarEnabled,
        GovernanceSettingKeys.PublicExperience.AnnouncementBarLinkText,
        GovernanceSettingKeys.PublicExperience.AnnouncementBarLinkUrl,
        GovernanceSettingKeys.PublicExperience.AnnouncementBarMessage,
        GovernanceSettingKeys.PublicExperience.AnnouncementBarRevision,
        GovernanceSettingKeys.Routing.DefaultPublicHomePage,
        GovernanceSettingKeys.Routing.RenderPolicy.Admin.PrerenderEnabled,
        GovernanceSettingKeys.Routing.RenderPolicy.Admin.RenderMode,
        GovernanceSettingKeys.Routing.RenderPolicy.AdvancedEnabled,
        GovernanceSettingKeys.Routing.RenderPolicy.AllowTenantOverride,
        GovernanceSettingKeys.Routing.RenderPolicy.Fallback.PrerenderEnabled,
        GovernanceSettingKeys.Routing.RenderPolicy.Fallback.RenderMode,
        GovernanceSettingKeys.Routing.RenderPolicy.LockTenantAdmin,
        GovernanceSettingKeys.Routing.RenderPolicy.LockTenantOperational,
        GovernanceSettingKeys.Routing.RenderPolicy.LockTenantPublicSeo,
        GovernanceSettingKeys.Routing.RenderPolicy.Operational.PrerenderEnabled,
        GovernanceSettingKeys.Routing.RenderPolicy.Operational.RenderMode,
        GovernanceSettingKeys.Routing.RenderPolicy.Preset,
        GovernanceSettingKeys.Routing.RenderPolicy.PublicSeo.PrerenderEnabled,
        GovernanceSettingKeys.Routing.RenderPolicy.PublicSeo.RenderMode,
        GovernanceSettingKeys.TenantDelegation.LockAiAssistant,
        GovernanceSettingKeys.TenantDelegation.LockMcp,
        GovernanceSettingKeys.TenantDelegation.LockMcpLegacySse
    ];

    public Task<IReadOnlyList<SettingChangedNotification>> ApplyTenantSettingsAsync(
        Guid tenantId,
        Guid? actorUserId,
        UpdateTenantPolicyRequest settings,
        CancellationToken cancellationToken = default)
    {
        return _mutationLock.ExecuteManyAsync(
            TenantPolicySettingKeys,
            token => ApplyTenantSettingsInsideLocksAsync(tenantId, actorUserId, settings, token),
            cancellationToken);
    }

    private async Task<IReadOnlyList<SettingChangedNotification>> ApplyTenantSettingsInsideLocksAsync(
        Guid tenantId,
        Guid? actorUserId,
        UpdateTenantPolicyRequest settings,
        CancellationToken cancellationToken)
    {
        var notifications = new List<SettingChangedNotification>();
        Dictionary<string, SystemSetting> systemSettings = (await _systemSettingRepository
                .GetAllSettings(cancellationToken: cancellationToken))
            .Where(setting => TenantPolicySettingKeys.Contains(setting.SettingKey, StringComparer.Ordinal))
            .ToDictionary(setting => setting.SettingKey, StringComparer.Ordinal);

        bool CanOverride(string key, bool allowed) =>
            allowed && systemSettings.GetValueOrDefault(key)?.IsLocked != true;

        Task SetBooleanTenantOverrideAsync(Guid id, string key, bool value, bool allowed, Guid? actor) =>
            SetBooleanTenantOverrideCoreAsync(
                id, key, value, CanOverride(key, allowed), actor, notifications, cancellationToken);
        Task SetStringTenantOverrideAsync(Guid id, string key, string? value, bool allowed, Guid? actor) =>
            SetStringTenantOverrideCoreAsync(
                id, key, value, CanOverride(key, allowed), actor, notifications, cancellationToken);
        Task SetStringListTenantOverrideAsync(Guid id, string key, IReadOnlyList<string> values, bool allowed, Guid? actor) =>
            SetStringListTenantOverrideCoreAsync(
                id, key, values, CanOverride(key, allowed), actor, notifications, cancellationToken);
        async Task UpsertTenantOverrideAsync(Guid id, string key, string value, Guid? actor)
        {
            if (!CanOverride(key, true))
            {
                await RemoveTenantOverrideCoreAsync(
                    id, key, actor, notifications, cancellationToken);
                return;
            }

            await UpsertTenantOverrideCoreAsync(
                id, key, value, actor, notifications, cancellationToken);
        }

        var eventCardClickSetting = systemSettings.GetValueOrDefault(GovernanceSettingKeys.Events.CardClickOpensDetailPage);
        var requireVerificationSetting = systemSettings.GetValueOrDefault(GovernanceSettingKeys.Organizations.VerificationRequired);
        var canOmitVerificationSetting = systemSettings.GetValueOrDefault(GovernanceSettingKeys.Organizations.TenantCanOmitVerification);
        var orgSelfRegSetting = systemSettings.GetValueOrDefault(GovernanceSettingKeys.Organizations.SelfRegistrationEnabled);
        var groupSelfRegSetting = systemSettings.GetValueOrDefault(GovernanceSettingKeys.Groups.SelfRegistrationEnabled);
        var deploymentModeSetting = systemSettings.GetValueOrDefault(GovernanceSettingKeys.Deployment.Mode);
        var homePageSetting = systemSettings.GetValueOrDefault(GovernanceSettingKeys.Routing.DefaultPublicHomePage);
        var allowCustomDomainSetting = systemSettings.GetValueOrDefault(GovernanceSettingKeys.Domains.AllowTenantCustomDomain);
        var subdomainSetting = systemSettings.GetValueOrDefault(GovernanceSettingKeys.Domains.TenantSubdomain);
        var customDomainSetting = systemSettings.GetValueOrDefault(GovernanceSettingKeys.Domains.TenantCustomDomain);
        var communityGuidelinesSetting = systemSettings.GetValueOrDefault(GovernanceSettingKeys.Policies.CommunityGuidelinesContent);
        var allowTenantRenderOverrideSetting = systemSettings.GetValueOrDefault(GovernanceSettingKeys.Routing.RenderPolicy.AllowTenantOverride);
        var lockPublicSeoSetting = systemSettings.GetValueOrDefault(GovernanceSettingKeys.Routing.RenderPolicy.LockTenantPublicSeo);
        var lockOperationalSetting = systemSettings.GetValueOrDefault(GovernanceSettingKeys.Routing.RenderPolicy.LockTenantOperational);
        var lockAdminSetting = systemSettings.GetValueOrDefault(GovernanceSettingKeys.Routing.RenderPolicy.LockTenantAdmin);
        var lockAiAssistantSetting = systemSettings.GetValueOrDefault(GovernanceSettingKeys.TenantDelegation.LockAiAssistant);
        var lockMcpSetting = systemSettings.GetValueOrDefault(GovernanceSettingKeys.TenantDelegation.LockMcp);
        var lockMcpLegacySseSetting = systemSettings.GetValueOrDefault(GovernanceSettingKeys.TenantDelegation.LockMcpLegacySse);
        var isMultiTenant = DeserializeString(deploymentModeSetting?.Value, "SingleTenant").Equals("MultiTenant", StringComparison.OrdinalIgnoreCase);
        var canOverrideAiAssistant = !isMultiTenant || !DeserializeBoolean(lockAiAssistantSetting?.Value, true);
        var aiAssistantProvider = NormalizeAiAssistantProvider(settings.AiAssistantProvider, settings.AiAssistantEnabled);
        var usesOfficialProvider = aiAssistantProvider is "openai" or "anthropic";
        var usesCompatibleProvider = aiAssistantProvider is "openai-compatible" or "anthropic-compatible";
        var usesExternalProvider = usesOfficialProvider || usesCompatibleProvider;
        var aiAssistantAllowedModelIds = usesExternalProvider
            ? NormalizeAiModelIds([settings.AiAssistantModelId], settings.AiAssistantAllowedModelIds)
            : [];

        if (canOverrideAiAssistant)
        {
            ValidateAiAssistantSettings(settings, aiAssistantProvider);
        }

        var occurredAtUtc = DateTime.UtcNow;
        PublicationPolicyMutationResult publicationPolicyResult = await _publicationPolicyMutationBoundary.ApplyTenantAsync(
            new PublicationPolicyTenantMutationRequest(
                tenantId,
                actorUserId ?? Guid.Empty,
                occurredAtUtc,
                [
                    new PublicationPolicySettingMutation(
                        GovernanceSettingKeys.Events.RequireApproval,
                        PublicationPolicyMutationKind.Set,
                        JsonSerializer.Serialize(settings.RequireEventApproval),
                        tenantId,
                        IsLocked: null),
                    new PublicationPolicySettingMutation(
                        GovernanceSettingKeys.Events.UserSubmissionEnabled,
                        PublicationPolicyMutationKind.Set,
                        JsonSerializer.Serialize(settings.AllowUserSubmittedEvents),
                        tenantId,
                        IsLocked: null),
                    new PublicationPolicySettingMutation(
                        GovernanceSettingKeys.Events.OrganizationSubmissionEnabled,
                        PublicationPolicyMutationKind.Set,
                        JsonSerializer.Serialize(settings.AllowOrganizationSubmittedEvents),
                        tenantId,
                        IsLocked: null),
                    new PublicationPolicySettingMutation(
                        GovernanceSettingKeys.Events.GroupSubmissionEnabled,
                        PublicationPolicyMutationKind.Set,
                        JsonSerializer.Serialize(settings.AllowGroupSubmittedEvents),
                        tenantId,
                        IsLocked: null)
                ],
                PublicationPolicyLockedSystemBehavior.RemoveOverride),
            cancellationToken);
        if (!publicationPolicyResult.Success)
        {
            var failureCode = string.IsNullOrWhiteSpace(publicationPolicyResult.FailureCode)
                ? "event_reporting_intake_policy_invalid"
                : publicationPolicyResult.FailureCode;
            throw new FluentValidation.ValidationException([
                new ValidationFailure(nameof(UpdateTenantPolicyRequest), string.Empty)
                {
                    ErrorCode = failureCode
                }
            ]);
        }

        notifications.AddRange(publicationPolicyResult.DeferredNotifications);

        var tenant = await _tenantRepository.GetByIdAsNoTrackingAsync(tenantId, cancellationToken);
        var fallbackSubdomain = NormalizeSubdomain(tenant?.Slug) ?? "default";

        await SetBooleanTenantOverrideAsync(
            tenantId,
            GovernanceSettingKeys.Events.CardClickOpensDetailPage,
            settings.EventCardClickOpensDetailPage,
            eventCardClickSetting?.IsLocked != true,
            actorUserId);

        var canTenantOmitVerification = DeserializeBoolean(canOmitVerificationSetting?.Value, false)
            && requireVerificationSetting?.IsLocked != true;
        var effectiveRequireVerification = canTenantOmitVerification
            ? settings.RequireOrganizationVerification
            : DeserializeBoolean(requireVerificationSetting?.Value, true);

        await SetBooleanTenantOverrideAsync(
            tenantId,
            GovernanceSettingKeys.Organizations.VerificationRequired,
            effectiveRequireVerification,
            canTenantOmitVerification,
            actorUserId);

        await SetBooleanTenantOverrideAsync(
            tenantId,
            GovernanceSettingKeys.Organizations.SelfRegistrationEnabled,
            settings.AllowOrganizationSelfRegistration,
            orgSelfRegSetting?.IsLocked != true,
            actorUserId);

        await SetBooleanTenantOverrideAsync(
            tenantId,
            GovernanceSettingKeys.Groups.SelfRegistrationEnabled,
            settings.AllowGroupSelfRegistration,
            groupSelfRegSetting?.IsLocked != true,
            actorUserId);

        await SetStringTenantOverrideAsync(
            tenantId,
            GovernanceSettingKeys.Routing.DefaultPublicHomePage,
            NormalizeHomePage(settings.PreferredHomePage),
            homePageSetting?.IsLocked != true,
            actorUserId);

        await SetStringTenantOverrideAsync(
            tenantId,
            GovernanceSettingKeys.Domains.TenantSubdomain,
            NormalizeSubdomain(settings.Subdomain) ?? fallbackSubdomain,
            subdomainSetting?.IsLocked != true,
            actorUserId);

        await SetStringTenantOverrideAsync(
            tenantId,
            GovernanceSettingKeys.Domains.TenantCustomDomain,
            NormalizeOptionalHost(settings.CustomDomain),
            customDomainSetting?.IsLocked != true && DeserializeBoolean(allowCustomDomainSetting?.Value, true),
            actorUserId);

        await SetBooleanTenantOverrideAsync(
            tenantId,
            GovernanceSettingKeys.PublicExperience.AnnouncementBarEnabled,
            settings.AnnouncementBarEnabled,
            true,
            actorUserId);

        await SetStringTenantOverrideAsync(
            tenantId,
            GovernanceSettingKeys.PublicExperience.AnnouncementBarMessage,
            settings.AnnouncementBarMessage,
            true,
            actorUserId);

        await SetStringTenantOverrideAsync(
            tenantId,
            GovernanceSettingKeys.PublicExperience.AnnouncementBarLinkText,
            settings.AnnouncementBarLinkText,
            true,
            actorUserId);

        await SetStringTenantOverrideAsync(
            tenantId,
            GovernanceSettingKeys.PublicExperience.AnnouncementBarLinkUrl,
            settings.AnnouncementBarLinkUrl,
            true,
            actorUserId);

        if (settings.ForceAnnouncementBarRedisplay)
        {
            var revisionSetting = await _tenantSettingRepository.GetByTenantAndKey(
                tenantId,
                GovernanceSettingKeys.PublicExperience.AnnouncementBarRevision,
                cancellationToken);
            var nextRevision = DeserializeInteger(revisionSetting?.Value, 0) + 1;

            await UpsertTenantOverrideAsync(
                tenantId,
                GovernanceSettingKeys.PublicExperience.AnnouncementBarRevision,
                JsonSerializer.Serialize(nextRevision),
                actorUserId);
        }

        await SetStringTenantOverrideAsync(
            tenantId,
            GovernanceSettingKeys.Policies.CommunityGuidelinesContent,
            settings.CommunityGuidelinesContent,
            communityGuidelinesSetting?.IsLocked != true,
            actorUserId);

        var allowTenantRenderOverride = !isMultiTenant || DeserializeBoolean(allowTenantRenderOverrideSetting?.Value, false);
        var canOverridePublicSeo = allowTenantRenderOverride && (!isMultiTenant || !DeserializeBoolean(lockPublicSeoSetting?.Value, false));
        var canOverrideOperational = allowTenantRenderOverride && (!isMultiTenant || !DeserializeBoolean(lockOperationalSetting?.Value, false));
        var canOverrideAdmin = allowTenantRenderOverride && (!isMultiTenant || !DeserializeBoolean(lockAdminSetting?.Value, false));

        await SetStringTenantOverrideAsync(
            tenantId,
            GovernanceSettingKeys.Routing.RenderPolicy.Preset,
            settings.RenderPolicyPreset,
            allowTenantRenderOverride,
            actorUserId);

        await SetBooleanTenantOverrideAsync(
            tenantId,
            GovernanceSettingKeys.Routing.RenderPolicy.AdvancedEnabled,
            settings.EnableAdvancedRenderPolicyOverrides,
            allowTenantRenderOverride,
            actorUserId);

        await SetStringTenantOverrideAsync(
            tenantId,
            GovernanceSettingKeys.Routing.RenderPolicy.Fallback.RenderMode,
            settings.GlobalRenderMode,
            allowTenantRenderOverride,
            actorUserId);

        await SetBooleanTenantOverrideAsync(
            tenantId,
            GovernanceSettingKeys.Routing.RenderPolicy.Fallback.PrerenderEnabled,
            settings.GlobalPrerenderEnabled,
            allowTenantRenderOverride,
            actorUserId);

        await SetStringTenantOverrideAsync(
            tenantId,
            GovernanceSettingKeys.Routing.RenderPolicy.PublicSeo.RenderMode,
            settings.PublicSeoRenderMode,
            canOverridePublicSeo,
            actorUserId);

        await SetBooleanTenantOverrideAsync(
            tenantId,
            GovernanceSettingKeys.Routing.RenderPolicy.PublicSeo.PrerenderEnabled,
            settings.PublicSeoPrerenderEnabled,
            canOverridePublicSeo,
            actorUserId);

        await SetStringTenantOverrideAsync(
            tenantId,
            GovernanceSettingKeys.Routing.RenderPolicy.Operational.RenderMode,
            settings.OperationalRenderMode,
            canOverrideOperational,
            actorUserId);

        await SetBooleanTenantOverrideAsync(
            tenantId,
            GovernanceSettingKeys.Routing.RenderPolicy.Operational.PrerenderEnabled,
            settings.OperationalPrerenderEnabled,
            canOverrideOperational,
            actorUserId);

        await SetStringTenantOverrideAsync(
            tenantId,
            GovernanceSettingKeys.Routing.RenderPolicy.Admin.RenderMode,
            settings.AdminRenderMode,
            canOverrideAdmin,
            actorUserId);

        await SetBooleanTenantOverrideAsync(
            tenantId,
            GovernanceSettingKeys.Routing.RenderPolicy.Admin.PrerenderEnabled,
            settings.AdminPrerenderEnabled,
            canOverrideAdmin,
            actorUserId);

        await SetBooleanTenantOverrideAsync(
            tenantId,
            GovernanceSettingKeys.AiAssistant.Enabled,
            settings.AiAssistantEnabled,
            canOverrideAiAssistant,
            actorUserId);

        await SetStringTenantOverrideAsync(
            tenantId,
            GovernanceSettingKeys.AiAssistant.Provider,
            aiAssistantProvider,
            canOverrideAiAssistant,
            actorUserId);

        await SetStringTenantOverrideAsync(
            tenantId,
            GovernanceSettingKeys.AiAssistant.EndpointUrl,
            usesCompatibleProvider ? settings.AiAssistantEndpointUrl : string.Empty,
            canOverrideAiAssistant,
            actorUserId);

        await SetStringTenantOverrideAsync(
            tenantId,
            GovernanceSettingKeys.AiAssistant.ApiKey,
            usesExternalProvider ? settings.AiAssistantApiKey : string.Empty,
            canOverrideAiAssistant,
            actorUserId);

        await SetStringTenantOverrideAsync(
            tenantId,
            GovernanceSettingKeys.AiAssistant.ModelId,
            usesExternalProvider ? settings.AiAssistantModelId : string.Empty,
            canOverrideAiAssistant,
            actorUserId);

        await SetStringListTenantOverrideAsync(
            tenantId,
            GovernanceSettingKeys.AiAssistant.AllowedModelIds,
            aiAssistantAllowedModelIds,
            canOverrideAiAssistant && usesExternalProvider,
            actorUserId);

        await SetBooleanTenantOverrideAsync(
            tenantId,
            GovernanceSettingKeys.AiAssistant.AllowAnonymousAccess,
            settings.AiAssistantAllowAnonymousAccess,
            canOverrideAiAssistant,
            actorUserId);

        var canOverrideMcp = !isMultiTenant || !DeserializeBoolean(lockMcpSetting?.Value, true);
        var canOverrideMcpLegacySse = !isMultiTenant || !DeserializeBoolean(lockMcpLegacySseSetting?.Value, true);

        await SetBooleanTenantOverrideAsync(
            tenantId,
            GovernanceSettingKeys.Mcp.Enabled,
            settings.McpEnabled,
            canOverrideMcp,
            actorUserId);

        await SetBooleanTenantOverrideAsync(
            tenantId,
            GovernanceSettingKeys.Mcp.EnableLegacySse,
            settings.McpEnableLegacySse,
            canOverrideMcpLegacySse,
            actorUserId);

        return notifications;
    }

    private async Task SetBooleanTenantOverrideCoreAsync(
        Guid tenantId,
        string settingKey,
        bool value,
        bool allowTenantOverride,
        Guid? actorUserId,
        ICollection<SettingChangedNotification> notifications,
        CancellationToken cancellationToken)
    {
        if (!allowTenantOverride)
        {
            await RemoveTenantOverrideCoreAsync(
                tenantId, settingKey, actorUserId, notifications, cancellationToken);
            return;
        }

        await UpsertTenantOverrideCoreAsync(
            tenantId,
            settingKey,
            JsonSerializer.Serialize(value),
            actorUserId,
            notifications,
            cancellationToken);
    }

    private async Task SetStringTenantOverrideCoreAsync(
        Guid tenantId,
        string settingKey,
        string? value,
        bool allowTenantOverride,
        Guid? actorUserId,
        ICollection<SettingChangedNotification> notifications,
        CancellationToken cancellationToken)
    {
        if (!allowTenantOverride || string.IsNullOrWhiteSpace(value))
        {
            await RemoveTenantOverrideCoreAsync(
                tenantId, settingKey, actorUserId, notifications, cancellationToken);
            return;
        }

        await UpsertTenantOverrideCoreAsync(
            tenantId,
            settingKey,
            JsonSerializer.Serialize(value.Trim()),
            actorUserId,
            notifications,
            cancellationToken);
    }

    private async Task SetStringListTenantOverrideCoreAsync(
        Guid tenantId,
        string settingKey,
        IReadOnlyList<string> values,
        bool allowTenantOverride,
        Guid? actorUserId,
        ICollection<SettingChangedNotification> notifications,
        CancellationToken cancellationToken)
    {
        var normalizedValues = NormalizeAiModelIds(values);

        if (!allowTenantOverride || normalizedValues.Count == 0)
        {
            await RemoveTenantOverrideCoreAsync(
                tenantId, settingKey, actorUserId, notifications, cancellationToken);
            return;
        }

        await UpsertTenantOverrideCoreAsync(
            tenantId,
            settingKey,
            JsonSerializer.Serialize(normalizedValues),
            actorUserId,
            notifications,
            cancellationToken);
    }

    private static void ValidateAiAssistantSettings(UpdateTenantPolicyRequest settings, string provider)
    {
        if (!settings.AiAssistantEnabled)
        {
            return;
        }

        var failures = new List<ValidationFailure>();
        if (provider is not "fake" and not "openai" and not "openai-compatible" and not "anthropic" and not "anthropic-compatible")
        {
            failures.Add(new ValidationFailure(
                nameof(settings.AiAssistantProvider),
                "AI Assistant provider must be OpenAI, OpenAI-compatible, Anthropic, Anthropic-compatible, or Fake."));
        }

        if (provider is "openai" or "anthropic")
        {
            if (string.IsNullOrWhiteSpace(settings.AiAssistantApiKey))
            {
                failures.Add(new ValidationFailure(
                    nameof(settings.AiAssistantApiKey),
                    "AI Assistant API key is required for official AI providers."));
            }

            if (string.IsNullOrWhiteSpace(settings.AiAssistantModelId))
            {
                failures.Add(new ValidationFailure(
                    nameof(settings.AiAssistantModelId),
                    "AI Assistant model ID is required for official AI providers."));
            }
        }

        if (provider is "openai-compatible" or "anthropic-compatible")
        {
            if (!HasAbsoluteHttpUrl(settings.AiAssistantEndpointUrl))
            {
                failures.Add(new ValidationFailure(
                    nameof(settings.AiAssistantEndpointUrl),
                    "AI Assistant endpoint URL must be an absolute HTTP or HTTPS URL."));
            }

            if (string.IsNullOrWhiteSpace(settings.AiAssistantModelId))
            {
                failures.Add(new ValidationFailure(
                    nameof(settings.AiAssistantModelId),
                    "AI Assistant model ID is required for OpenAI-compatible or Anthropic-compatible providers."));
            }
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(new ValidationResult(failures));
        }
    }

    private static string NormalizeAiAssistantProvider(string? provider, bool enabled)
    {
        var normalized = provider?.Trim().ToLowerInvariant() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(normalized) || (enabled && normalized == "none"))
        {
            return enabled ? "openai" : "none";
        }

        if (!enabled && normalized is not "none" and not "fake" and not "openai" and not "openai-compatible" and not "anthropic" and not "anthropic-compatible")
        {
            return "none";
        }

        return normalized;
    }

    private static bool HasAbsoluteHttpUrl(string? value)
    {
        return Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private async Task UpsertTenantOverrideCoreAsync(
        Guid tenantId,
        string settingKey,
        string value,
        Guid? actorUserId,
        ICollection<SettingChangedNotification> notifications,
        CancellationToken cancellationToken)
    {
        var existing = await _tenantSettingRepository.GetByTenantAndKey(tenantId, settingKey, cancellationToken);
        var oldValue = existing?.Value;

        await _tenantSettingRepository.SetValueAsync(
            tenantId,
            settingKey,
            value,
            cancellationToken,
            actorUserId);

        notifications.Add(new SettingChangedNotification(
            settingKey, oldValue, value, SettingSource.TenantOverride, tenantId, actorUserId, DateTime.UtcNow));
    }

    private async Task RemoveTenantOverrideCoreAsync(
        Guid tenantId,
        string settingKey,
        Guid? actorUserId,
        ICollection<SettingChangedNotification> notifications,
        CancellationToken cancellationToken)
    {
        TenantSetting? existing = await _tenantSettingRepository.GetByTenantAndKey(
            tenantId,
            settingKey,
            cancellationToken);
        if (existing is null || !await _tenantSettingRepository.RemoveOverrideAsync(
                tenantId,
                settingKey,
                cancellationToken))
        {
            return;
        }

        notifications.Add(new SettingChangedNotification(
            settingKey,
            existing.Value,
            null,
            SettingSource.TenantOverride,
            tenantId,
            actorUserId,
            DateTime.UtcNow));
    }
}

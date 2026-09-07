using System.Globalization;
using Explore.Blazor.Client.Clients;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.Client.Services;

public interface ITenantPublicExperienceAdminService
{
    Task<TenantPublicExperienceAdminModel> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task ApplySingleTenantPolicySettingsAsync(TenantPolicySettingsDto model, CancellationToken cancellationToken = default);
    Task ApplyAnnouncementBarSettingsAsync(TenantPolicySettingsDto model, CancellationToken cancellationToken = default);
    Task<PublicExperienceAdminSaveResult> SaveAsync(TenantPublicExperienceAdminModel model, CancellationToken cancellationToken = default);
    Task<PublicExperienceAdminSaveResult> SaveSingleTenantPolicySettingsAsync(TenantPolicySettingsDto model, CancellationToken cancellationToken = default);
    Task<PublicExperienceAdminSaveResult> SaveAnnouncementBarAsync(TenantPolicySettingsDto model, bool forceRedisplay, CancellationToken cancellationToken = default);
}

public sealed class TenantPublicExperienceAdminService(
    ISettingsClient apiClient,
    IPublicExperienceService publicExperienceService,
    ILogger<TenantPublicExperienceAdminService> logger) : ITenantPublicExperienceAdminService
{
    private const string Category = "PublicExperience";
    private const string ModeKey = "public_experience.mode";
    private const string EventCatalogLabelKey = "public_experience.event_catalog_label";
    private const string PrimaryOrganizationIdKey = "public_experience.primary_organization_id";
    private const string HomeBlocksKey = "public_experience.home_blocks";
    private const string CtasKey = "public_experience.ctas";
    private const string EventSectionPresetsKey = "public_experience.event_section_presets";
    private const string AnnouncementBarEnabledKey = "public_experience.announcement_bar.enabled";
    private const string AnnouncementBarMessageKey = "public_experience.announcement_bar.message";
    private const string AnnouncementBarLinkTextKey = "public_experience.announcement_bar.link_text";
    private const string AnnouncementBarLinkUrlKey = "public_experience.announcement_bar.link_url";
    private const string AnnouncementBarRevisionKey = "public_experience.announcement_bar.revision";

    private const string EventsCategory = "Events";
    private const string OrganizationsCategory = "Organizations";
    private const string GroupsCategory = "Groups";
    private const string AiAssistantCategory = "AiAssistant";
    private const string EventsUserSubmissionEnabledKey = "events.user_submission_enabled";
    private const string EventsOrganizationSubmissionEnabledKey = "events.organization_submission_enabled";
    private const string EventsGroupSubmissionEnabledKey = "events.group_submission_enabled";
    private const string EventsRequireApprovalKey = "events.require_approval";
    private const string EventsCardClickOpensDetailPageKey = "events.card_click_opens_detail_page";
    private const string OrganizationsVerificationRequiredKey = "organizations.verification_required";
    private const string OrganizationsSelfRegistrationEnabledKey = "organizations.self_registration_enabled";
    private const string GroupsSelfRegistrationEnabledKey = "groups.self_registration_enabled";
    private const string AiAssistantEnabledKey = "ai_assistant.enabled";
    private const string AiAssistantProviderKey = "ai_assistant.provider";
    private const string AiAssistantEndpointUrlKey = "ai_assistant.endpoint_url";
    private const string AiAssistantApiKeyKey = "ai_assistant.api_key";
    private const string AiAssistantModelIdKey = "ai_assistant.model_id";
    private const string AiAssistantAllowAnonymousAccessKey = "ai_assistant.allow_anonymous_access";
    private const string AiProviderNone = "none";

    private const string DefaultMode = "DiscoveryCentric";
    private const string DefaultEventCatalogLabel = "Events";
    private const string DefaultHomeBlocksJson = "{\"schemaVersion\":1,\"blocks\":[]}";
    private const string DefaultCtasJson = "{\"schemaVersion\":1,\"ctas\":[]}";
    private const string DefaultEventSectionPresetsJson = "{\"schemaVersion\":1,\"presets\":[]}";

    public async Task<TenantPublicExperienceAdminModel> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            SettingGroupResponseDto response = await apiClient.GetTenantScopedSettingsAsync(
                Category,
                cancellationToken: cancellationToken);

            Dictionary<string, EffectiveSettingDto> settings = response.Settings
                .Where(setting => !string.IsNullOrWhiteSpace(setting.Key))
                .ToDictionary(setting => setting.Key, StringComparer.OrdinalIgnoreCase);

            return new TenantPublicExperienceAdminModel
            {
                Mode = GetString(settings, ModeKey, DefaultMode),
                EventCatalogLabel = GetString(settings, EventCatalogLabelKey, DefaultEventCatalogLabel),
                PrimaryOrganizationId = GetGuid(settings, PrimaryOrganizationIdKey),
                HomeBlocksJson = GetString(settings, HomeBlocksKey, DefaultHomeBlocksJson),
                CtasJson = GetString(settings, CtasKey, DefaultCtasJson),
                EventSectionPresetsJson = GetString(settings, EventSectionPresetsKey, DefaultEventSectionPresetsJson),
                CanEditMode = CanEdit(settings, ModeKey),
                CanEditEventCatalogLabel = CanEdit(settings, EventCatalogLabelKey),
                CanEditPrimaryOrganization = CanEdit(settings, PrimaryOrganizationIdKey),
                CanEditHomeBlocks = CanEdit(settings, HomeBlocksKey),
                CanEditCtas = CanEdit(settings, CtasKey),
                CanEditEventSectionPresets = CanEdit(settings, EventSectionPresetsKey)
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load tenant public experience settings.");
            return new TenantPublicExperienceAdminModel();
        }
    }

    public async Task ApplySingleTenantPolicySettingsAsync(
        TenantPolicySettingsDto model,
        CancellationToken cancellationToken = default)
    {
        model.CanTenantOmitVerification = true;
        model.CanOverrideEventCardClickBehavior = true;
        model.CanOverrideAiAssistant = true;

        try
        {
            Dictionary<string, EffectiveSettingDto> eventSettings = await GetTenantSettingsDictionaryAsync(
                EventsCategory,
                cancellationToken);
            Dictionary<string, EffectiveSettingDto> organizationSettings = await GetTenantSettingsDictionaryAsync(
                OrganizationsCategory,
                cancellationToken);
            Dictionary<string, EffectiveSettingDto> groupSettings = await GetTenantSettingsDictionaryAsync(
                GroupsCategory,
                cancellationToken);
            Dictionary<string, EffectiveSettingDto> aiAssistantSettings = await GetTenantSettingsDictionaryAsync(
                AiAssistantCategory,
                cancellationToken);

            model.AllowUserSubmittedEvents = GetBoolean(
                eventSettings,
                EventsUserSubmissionEnabledKey,
                model.AllowUserSubmittedEvents ?? true);
            model.AllowOrganizationSubmittedEvents = GetBoolean(
                eventSettings,
                EventsOrganizationSubmissionEnabledKey,
                model.AllowOrganizationSubmittedEvents ?? true);
            model.AllowGroupSubmittedEvents = GetBoolean(
                eventSettings,
                EventsGroupSubmissionEnabledKey,
                model.AllowGroupSubmittedEvents ?? true);
            model.RequireEventApproval = GetBoolean(
                eventSettings,
                EventsRequireApprovalKey,
                model.RequireEventApproval == true);
            model.EventCardClickOpensDetailPage = GetBoolean(
                eventSettings,
                EventsCardClickOpensDetailPageKey,
                model.EventCardClickOpensDetailPage == true);
            model.RequireOrganizationVerification = GetBoolean(
                organizationSettings,
                OrganizationsVerificationRequiredKey,
                model.RequireOrganizationVerification ?? true);
            model.AllowOrganizationSelfRegistration = GetBoolean(
                organizationSettings,
                OrganizationsSelfRegistrationEnabledKey,
                model.AllowOrganizationSelfRegistration ?? true);
            model.AllowGroupSelfRegistration = GetBoolean(
                groupSettings,
                GroupsSelfRegistrationEnabledKey,
                model.AllowGroupSelfRegistration ?? true);
            model.AiAssistantEnabled = GetBoolean(
                aiAssistantSettings,
                AiAssistantEnabledKey,
                model.AiAssistantEnabled == true);
            model.AiAssistantProvider = GetString(
                aiAssistantSettings,
                AiAssistantProviderKey,
                model.AiAssistantProvider ?? AiProviderNone);
            model.AiAssistantEndpointUrl = GetString(
                aiAssistantSettings,
                AiAssistantEndpointUrlKey,
                model.AiAssistantEndpointUrl ?? string.Empty);
            model.AiAssistantApiKey = GetString(
                aiAssistantSettings,
                AiAssistantApiKeyKey,
                model.AiAssistantApiKey ?? string.Empty);
            model.AiAssistantModelId = GetString(
                aiAssistantSettings,
                AiAssistantModelIdKey,
                model.AiAssistantModelId ?? string.Empty);
            model.AiAssistantAllowAnonymousAccess = GetBoolean(
                aiAssistantSettings,
                AiAssistantAllowAnonymousAccessKey,
                model.AiAssistantAllowAnonymousAccess == true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load single-tenant policy settings.");
        }
    }

    public async Task ApplyAnnouncementBarSettingsAsync(
        TenantPolicySettingsDto model,
        CancellationToken cancellationToken = default)
    {
        try
        {
            SettingGroupResponseDto response = await apiClient.GetTenantScopedSettingsAsync(
                Category,
                cancellationToken: cancellationToken);

            Dictionary<string, EffectiveSettingDto> settings = response.Settings
                .Where(setting => !string.IsNullOrWhiteSpace(setting.Key))
                .ToDictionary(setting => setting.Key, StringComparer.OrdinalIgnoreCase);

            model.AnnouncementBarEnabled = GetBoolean(settings, AnnouncementBarEnabledKey, false);
            model.AnnouncementBarMessage = GetString(settings, AnnouncementBarMessageKey, string.Empty);
            model.AnnouncementBarLinkText = GetString(settings, AnnouncementBarLinkTextKey, string.Empty);
            model.AnnouncementBarLinkUrl = GetString(settings, AnnouncementBarLinkUrlKey, string.Empty);
            model.AnnouncementBarRevision = GetInteger(settings, AnnouncementBarRevisionKey, 0);

            await ApplyPublicAnnouncementFallbackAsync(model);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load tenant announcement bar settings.");
            await ApplyPublicAnnouncementFallbackAsync(model);
        }
    }

    private async Task ApplyPublicAnnouncementFallbackAsync(TenantPolicySettingsDto model)
    {
        if (model.AnnouncementBarEnabled == true || !string.IsNullOrWhiteSpace(model.AnnouncementBarMessage))
        {
            return;
        }

        PublicExperienceSettingsDto? settings = await publicExperienceService.GetSettingsAsync();
        if (settings?.AnnouncementBarEnabled != true
            || string.IsNullOrWhiteSpace(settings.AnnouncementBarMessage))
        {
            return;
        }

        model.AnnouncementBarEnabled = true;
        model.AnnouncementBarMessage = settings.AnnouncementBarMessage.Trim();
        model.AnnouncementBarLinkText = settings.AnnouncementBarLinkText?.Trim() ?? string.Empty;
        model.AnnouncementBarLinkUrl = settings.AnnouncementBarLinkUrl?.Trim() ?? string.Empty;
        model.AnnouncementBarRevision = settings.AnnouncementBarRevision ?? 0;
    }

    public async Task<PublicExperienceAdminSaveResult> SaveAsync(
        TenantPublicExperienceAdminModel model,
        CancellationToken cancellationToken = default)
    {
        Dictionary<string, string> values = BuildEditableValues(model);

        if (values.Count == 0)
        {
            return PublicExperienceAdminSaveResult.Failed("No public experience settings are editable.");
        }

        try
        {
            BatchUpdateResponseDto response = await apiClient.UpdateTenantSettingsBatchAsync(
                Category,
                new UpdateSettingBatchDto
                {
                    Values = values,
                    Mode = (BatchUpdateMode)1 // Strict admin save: reject the batch if any key is locked or invalid.
                },
                cancellationToken: cancellationToken);

            return response.Success == true
                ? PublicExperienceAdminSaveResult.Successful()
                : PublicExperienceAdminSaveResult.Failed(BuildFailureMessage(response));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save tenant public experience settings.");
            return PublicExperienceAdminSaveResult.Failed("Failed to save public experience settings.");
        }
    }

    public async Task<PublicExperienceAdminSaveResult> SaveSingleTenantPolicySettingsAsync(
        TenantPolicySettingsDto model,
        CancellationToken cancellationToken = default)
    {
        try
        {
            PublicExperienceAdminSaveResult eventsResult = await SaveBatchAsync(
                EventsCategory,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [EventsUserSubmissionEnabledKey] = FormatBoolean(model.AllowUserSubmittedEvents ?? true),
                    [EventsOrganizationSubmissionEnabledKey] = FormatBoolean(model.AllowOrganizationSubmittedEvents ?? true),
                    [EventsGroupSubmissionEnabledKey] = FormatBoolean(model.AllowGroupSubmittedEvents ?? true),
                    [EventsRequireApprovalKey] = FormatBoolean(model.RequireEventApproval == true),
                    [EventsCardClickOpensDetailPageKey] = FormatBoolean(model.EventCardClickOpensDetailPage == true)
                },
                cancellationToken);
            if (!eventsResult.Success)
            {
                return eventsResult;
            }

            PublicExperienceAdminSaveResult organizationsResult = await SaveBatchAsync(
                OrganizationsCategory,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [OrganizationsVerificationRequiredKey] = FormatBoolean(model.RequireOrganizationVerification ?? true),
                    [OrganizationsSelfRegistrationEnabledKey] = FormatBoolean(model.AllowOrganizationSelfRegistration ?? true)
                },
                cancellationToken);
            if (!organizationsResult.Success)
            {
                return organizationsResult;
            }

            PublicExperienceAdminSaveResult groupsResult = await SaveBatchAsync(
                GroupsCategory,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [GroupsSelfRegistrationEnabledKey] = FormatBoolean(model.AllowGroupSelfRegistration ?? true)
                },
                cancellationToken);
            return groupsResult;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save single-tenant policy settings.");
            return PublicExperienceAdminSaveResult.Failed("Failed to save policy settings.");
        }
    }

    public async Task<PublicExperienceAdminSaveResult> SaveAnnouncementBarAsync(
        TenantPolicySettingsDto model,
        bool forceRedisplay,
        CancellationToken cancellationToken = default)
    {
        int revision = model.AnnouncementBarRevision ?? 0;
        if (forceRedisplay)
        {
            revision++;
        }
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase)
        {
            [AnnouncementBarEnabledKey] = FormatBoolean(model.AnnouncementBarEnabled == true),
            [AnnouncementBarMessageKey] = model.AnnouncementBarMessage?.Trim() ?? string.Empty,
            [AnnouncementBarLinkTextKey] = model.AnnouncementBarLinkText?.Trim() ?? string.Empty,
            [AnnouncementBarLinkUrlKey] = model.AnnouncementBarLinkUrl?.Trim() ?? string.Empty,
            [AnnouncementBarRevisionKey] = revision.ToString(CultureInfo.InvariantCulture)
        };

        try
        {
            PublicExperienceAdminSaveResult result = await SaveBatchAsync(Category, values, cancellationToken);
            if (result.Success)
            {
                model.AnnouncementBarRevision = revision;
            }

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save tenant announcement bar settings.");
            return PublicExperienceAdminSaveResult.Failed("Failed to save announcement bar settings.");
        }
    }

    private async Task<Dictionary<string, EffectiveSettingDto>> GetTenantSettingsDictionaryAsync(
        string category,
        CancellationToken cancellationToken)
    {
        SettingGroupResponseDto response = await apiClient.GetTenantScopedSettingsAsync(
            category,
            cancellationToken: cancellationToken);

        return response.Settings
            .Where(setting => !string.IsNullOrWhiteSpace(setting.Key))
            .ToDictionary(setting => setting.Key, StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> BuildEditableValues(TenantPublicExperienceAdminModel model)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);

        if (model.CanEditMode)
        {
            values[ModeKey] = string.IsNullOrWhiteSpace(model.Mode) ? DefaultMode : model.Mode;
        }

        if (model.CanEditEventCatalogLabel)
        {
            values[EventCatalogLabelKey] = string.IsNullOrWhiteSpace(model.EventCatalogLabel)
                ? DefaultEventCatalogLabel
                : model.EventCatalogLabel.Trim();
        }

        if (model.CanEditPrimaryOrganization)
        {
            values[PrimaryOrganizationIdKey] = model.PrimaryOrganizationId?.ToString("D") ?? string.Empty;
        }

        if (model.CanEditHomeBlocks)
        {
            values[HomeBlocksKey] = NormalizeJson(model.HomeBlocksJson, DefaultHomeBlocksJson);
        }

        if (model.CanEditCtas)
        {
            values[CtasKey] = NormalizeJson(model.CtasJson, DefaultCtasJson);
        }

        if (model.CanEditEventSectionPresets)
        {
            values[EventSectionPresetsKey] = NormalizeJson(model.EventSectionPresetsJson, DefaultEventSectionPresetsJson);
        }

        return values;
    }

    private async Task<PublicExperienceAdminSaveResult> SaveBatchAsync(
        string category,
        Dictionary<string, string> values,
        CancellationToken cancellationToken)
    {
        BatchUpdateResponseDto response = await apiClient.UpdateTenantSettingsBatchAsync(
            category,
            new UpdateSettingBatchDto
            {
                Values = values,
                Mode = (BatchUpdateMode)1
            },
            cancellationToken: cancellationToken);

        return response.Success == true
            ? PublicExperienceAdminSaveResult.Successful()
            : PublicExperienceAdminSaveResult.Failed(BuildFailureMessage(response));
    }

    private static string FormatBoolean(bool value) => value ? "true" : "false";

    private static string GetString(
        IReadOnlyDictionary<string, EffectiveSettingDto> settings,
        string key,
        string fallback)
    {
        if (!settings.TryGetValue(key, out EffectiveSettingDto? setting) || string.IsNullOrWhiteSpace(setting.Value))
        {
            return fallback;
        }

        try
        {
            var deserialized = System.Text.Json.JsonSerializer.Deserialize<string>(setting.Value);
            return string.IsNullOrWhiteSpace(deserialized) ? fallback : deserialized;
        }
        catch
        {
            return setting.Value.Trim('"');
        }
    }

    private static bool GetBoolean(
        IReadOnlyDictionary<string, EffectiveSettingDto> settings,
        string key,
        bool fallback)
    {
        return settings.TryGetValue(key, out EffectiveSettingDto? setting)
            && bool.TryParse(setting.Value?.Trim('"'), out var value)
                ? value
                : fallback;
    }

    private static int GetInteger(
        IReadOnlyDictionary<string, EffectiveSettingDto> settings,
        string key,
        int fallback)
    {
        return settings.TryGetValue(key, out EffectiveSettingDto? setting)
            && int.TryParse(setting.Value?.Trim('"'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : fallback;
    }

    private static Guid? GetGuid(IReadOnlyDictionary<string, EffectiveSettingDto> settings, string key)
    {
        return settings.TryGetValue(key, out EffectiveSettingDto? setting)
            && Guid.TryParse(setting.Value, out Guid value)
                ? value
                : null;
    }

    private static bool CanEdit(IReadOnlyDictionary<string, EffectiveSettingDto> settings, string key)
    {
        return !settings.TryGetValue(key, out EffectiveSettingDto? setting) || setting.CanEdit != false;
    }

    private static string NormalizeJson(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string BuildFailureMessage(BatchUpdateResponseDto response)
    {
        if (!string.IsNullOrWhiteSpace(response.Message))
        {
            return response.Message;
        }

        string[] skipped = response.Results
            .Where(result => result.Applied != true && !string.IsNullOrWhiteSpace(result.SkipReason))
            .Select(result => $"{result.Key}: {result.SkipReason}")
            .ToArray();

        return skipped.Length == 0
            ? "Public experience settings were not saved."
            : string.Join("; ", skipped);
    }
}

public sealed class TenantPublicExperienceAdminModel
{
    public string Mode { get; set; } = "DiscoveryCentric";
    public string EventCatalogLabel { get; set; } = "Events";
    public Guid? PrimaryOrganizationId { get; set; }
    public string HomeBlocksJson { get; set; } = "{\"schemaVersion\":1,\"blocks\":[]}";
    public string CtasJson { get; set; } = "{\"schemaVersion\":1,\"ctas\":[]}";
    public string EventSectionPresetsJson { get; set; } = "{\"schemaVersion\":1,\"presets\":[]}";
    public bool CanEditMode { get; set; } = true;
    public bool CanEditEventCatalogLabel { get; set; } = true;
    public bool CanEditPrimaryOrganization { get; set; } = true;
    public bool CanEditHomeBlocks { get; set; } = true;
    public bool CanEditCtas { get; set; } = true;
    public bool CanEditEventSectionPresets { get; set; } = true;

    public bool CanEditAny => CanEditMode
        || CanEditEventCatalogLabel
        || CanEditPrimaryOrganization
        || CanEditHomeBlocks
        || CanEditCtas
        || CanEditEventSectionPresets;
}

public sealed record PublicExperienceAdminSaveResult(bool Success, string Message)
{
    public static PublicExperienceAdminSaveResult Successful() => new(true, string.Empty);
    public static PublicExperienceAdminSaveResult Failed(string message) => new(false, message);
}

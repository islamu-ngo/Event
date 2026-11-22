using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services;

namespace Explore.Blazor.Client.Services;

public interface ITenantPublicExperienceAdminService
{
    Task<TenantPublicExperienceAdminModel> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task ApplySingleTenantPolicySettingsAsync(TenantPolicySettingsDto model, CancellationToken cancellationToken = default);
    Task<AnnouncementDraft?> GetAnnouncementAsync(CancellationToken cancellationToken = default);
    bool CanSaveAnnouncement(AnnouncementDraft? model);
    Task<bool> ReconcileAnnouncementAsync(AnnouncementDraft model, CancellationToken cancellationToken = default);
    Task<PublicExperienceAdminSaveResult> SaveAsync(TenantPublicExperienceAdminModel model, CancellationToken cancellationToken = default);
    Task<PublicExperienceAdminSaveResult> SaveSingleTenantPolicySettingsAsync(TenantPolicySettingsDto model, CancellationToken cancellationToken = default);
    Task<PublicExperienceAdminSaveResult> SaveAnnouncementBarAsync(AnnouncementDraft model, bool forceRedisplay, CancellationToken cancellationToken = default);
    Task<PublicExperienceAdminSaveResult> RemoveAnnouncementOverrideAsync(AnnouncementDraft model, CancellationToken cancellationToken = default);
}

public sealed class TenantPublicExperienceAdminService(
    ISettingsClient apiClient,
    IAnnouncementClientService announcements,
    ILogger<TenantPublicExperienceAdminService> logger) : ITenantPublicExperienceAdminService
{
    private const string Category = "PublicExperience";
    private const string TenantCategory = "Tenant";
    private const string UserRegistrationEnabledKey = "tenants.user_registration_enabled";
    private const string ModeKey = "public_experience.mode";
    private const string VisitorAccessModeKey = "public_experience.visitor_access_mode";
    private const string EventCatalogLabelKey = "public_experience.event_catalog_label";
    private const string PrimaryOrganizationIdKey = "public_experience.primary_organization_id";
    private const string HomeBlocksKey = "public_experience.home_blocks";
    private const string CtasKey = "public_experience.ctas";
    private const string EventSectionPresetsKey = "public_experience.event_section_presets";

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
    private const string AiAssistantCredentialScopeKey = "ai_assistant.credential_scope";
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
            HalResourceOfSettingGroupResponseDto response = await apiClient.GetTenantScopedSettingsAsync(
                Category,
                cancellationToken: cancellationToken);

            var model = MapSettings(response);
            try
            {
                var enrollment = await apiClient.GetTenantScopedSettingsAsync(TenantCategory,
                    cancellationToken: cancellationToken);
                var setting = enrollment.Settings.SingleOrDefault(setting => setting.Key == UserRegistrationEnabledKey);
                model.UserRegistrationEnabled = setting is null
                    ? true : System.Text.Json.JsonSerializer.Deserialize<bool>(setting.Value);
                model.CanEditUserRegistration = setting?.CanEdit == true
                    && enrollment._links?.ContainsKey("edit") == true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Tenant enrollment settings could not be loaded.");
                model.UserRegistrationEnabled = null;
                model.CanEditUserRegistration = false;
            }
            return model;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load tenant public experience settings.");
            return TenantPublicExperienceAdminModel.Unavailable();
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
            model.AiAssistantCredentialScope = GetString(
                aiAssistantSettings,
                AiAssistantCredentialScopeKey,
                "none");
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

    public async Task<AnnouncementDraft?> GetAnnouncementAsync(CancellationToken cancellationToken = default)
    {
        var source = await announcements.LoadSourceAsync(cancellationToken: cancellationToken);
        if (source is null)
            return null;
        var draft = new AnnouncementDraft();
        ApplyAnnouncementSource(draft, source);
        return draft;
    }

    public bool CanSaveAnnouncement(AnnouncementDraft? model) =>
        model?.Original is { } source && announcements.CanPublish(source);

    public async Task<bool> ReconcileAnnouncementAsync(
        AnnouncementDraft model, CancellationToken cancellationToken = default)
    {
        var source = await announcements.LoadSourceAsync(cancellationToken: cancellationToken);
        if (source is null)
            return false;
        model.Original = source;
        return true;
    }

    public async Task<PublicExperienceAdminSaveResult> SaveAsync(
        TenantPublicExperienceAdminModel model,
        CancellationToken cancellationToken = default)
    {
        if (!model.IsAvailable || model.VisitorAccessMode is null)
        {
            MarkUnavailable(model);
            return PublicExperienceAdminSaveResult.Unavailable();
        }

        Dictionary<string, string> values = BuildEditableValues(model);

        bool saveEnrollment = model.CanEditUserRegistration && model.UserRegistrationEnabled.HasValue;
        if (values.Count == 0 && !saveEnrollment)
        {
            return PublicExperienceAdminSaveResult.Failed("No public experience settings are editable.");
        }

        bool publicSettingsSaved = false;
        try
        {
            if (values.Count > 0)
            {
                var publicResult = await SaveBatchAsync(Category, values, cancellationToken);
                if (!publicResult.Success)
                    return await ReloadAfterRejectedSaveAsync(model, publicResult.Message, cancellationToken);
                publicSettingsSaved = true;
            }

            if (saveEnrollment)
            {
                var enrollmentResult = await SaveBatchAsync(TenantCategory,
                    new Dictionary<string, string>
                    {
                        [UserRegistrationEnabledKey] = model.UserRegistrationEnabled == true ? "true" : "false"
                    }, cancellationToken);
                if (!enrollmentResult.Success)
                    return await ReloadAfterRejectedSaveAsync(model,
                        "Public experience values may already be saved. Enrollment settings were rejected: " + enrollmentResult.Message,
                        cancellationToken);
            }
            return PublicExperienceAdminSaveResult.Successful();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (ApiException ex) when (ex.StatusCode == 409)
        {
            logger.LogWarning("Tenant visitor access policy save was rejected with a conflict.");
            return await ReloadAfterRejectedSaveAsync(
                model,
                (publicSettingsSaved ? "Public experience values may already be saved. " : string.Empty)
                    + "Visitor access settings changed or conflict with current event participation.",
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save tenant public experience settings.");
            return await ReloadAfterRejectedSaveAsync(model,
                (publicSettingsSaved ? "Public experience values may already be saved. " : string.Empty)
                    + "Public experience settings could not be saved. Reload the authoritative settings before retrying.",
                cancellationToken);
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
        AnnouncementDraft model,
        bool forceRedisplay,
        CancellationToken cancellationToken = default)
    {
        if (!CanSaveAnnouncement(model) || model.Original is not { } source)
            return PublicExperienceAdminSaveResult.Failed("Reload the original announcement source before saving.");
        bool enabled = model.Enabled;
        string message = model.Message.Trim(), linkText = model.LinkText.Trim(), linkUrl = model.LinkUrl.Trim();
        var result = await announcements.PublishAsync(source, new()
        {
            Kind = "publish", Enabled = enabled, Redisplay = forceRedisplay,
            Material = new() { Message = message, LinkText = linkText, LinkUrl = linkUrl }
        }, cancellationToken);
        if (!result.Success || result.Source is not { } fresh)
            return PublicExperienceAdminSaveResult.Failed(result.Message ?? "Reload announcement state before saving again.");
        if (model.Enabled == enabled && model.Message.Trim() == message
            && model.LinkText.Trim() == linkText && model.LinkUrl.Trim() == linkUrl)
            ApplyAnnouncementSource(model, fresh);
        else
            model.Original = fresh;
        return PublicExperienceAdminSaveResult.Successful();
    }

    public async Task<PublicExperienceAdminSaveResult> RemoveAnnouncementOverrideAsync(
        AnnouncementDraft model, CancellationToken cancellationToken = default)
    {
        if (!CanSaveAnnouncement(model) || model.Original is not { } source)
            return PublicExperienceAdminSaveResult.Failed("Reload the original announcement source before removing its override.");
        bool enabled = model.Enabled;
        string message = model.Message, linkText = model.LinkText, linkUrl = model.LinkUrl;
        var result = await announcements.PublishAsync(source, new() { Kind = "remove-override" }, cancellationToken);
        if (!result.Success || result.Source is not { } fresh)
            return PublicExperienceAdminSaveResult.Failed(result.Message ?? "The announcement override removal was not confirmed.");
        if (model.Enabled == enabled && model.Message == message && model.LinkText == linkText && model.LinkUrl == linkUrl)
            ApplyAnnouncementSource(model, fresh);
        else
            model.Original = fresh;
        return PublicExperienceAdminSaveResult.Successful();
    }

    private static void ApplyAnnouncementSource(AnnouncementDraft model, HalResourceOfAnnouncementSourceResource source)
    {
        model.Original = source;
        model.Enabled = source.Source.Group?.Enabled == true;
        model.Message = source.Source.Group?.Publication?.Material.Message ?? string.Empty;
        model.LinkText = source.Source.Group?.Publication?.Material.LinkText ?? string.Empty;
        model.LinkUrl = source.Source.Group?.Publication?.Material.LinkUrl ?? string.Empty;
    }

    private async Task<Dictionary<string, EffectiveSettingDto>> GetTenantSettingsDictionaryAsync(
        string category,
        CancellationToken cancellationToken)
    {
        HalResourceOfSettingGroupResponseDto response = await apiClient.GetTenantScopedSettingsAsync(
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

        if (model.CanEditVisitorAccessMode && model.VisitorAccessMode is { } visitorAccessMode)
        {
            values[VisitorAccessModeKey] = visitorAccessMode.ToString();
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

    private async Task<PublicExperienceAdminSaveResult> ReloadAfterRejectedSaveAsync(
        TenantPublicExperienceAdminModel model,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        bool restored = await ReloadAuthoritativeAfterFailureAsync(model, cancellationToken);
        return restored
            ? PublicExperienceAdminSaveResult.Failed(
                $"{failureMessage} The latest settings were restored.",
                authoritativeStateRestored: true)
            : PublicExperienceAdminSaveResult.Failed(
                $"{failureMessage} The latest settings could not be reloaded; editing remains unavailable.");
    }

    private async Task<bool> ReloadAuthoritativeAfterFailureAsync(
        TenantPublicExperienceAdminModel model,
        CancellationToken cancellationToken)
    {
        try
        {
            TenantPublicExperienceAdminModel authoritative = await GetSettingsAsync(cancellationToken);
            if (!authoritative.IsAvailable)
            {
                MarkUnavailable(model);
                return false;
            }

            CopySettings(authoritative, model);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to reload authoritative tenant visitor access settings after a rejected save.");
            MarkUnavailable(model);
            return false;
        }
    }

    private static TenantPublicExperienceAdminModel MapSettings(HalResourceOfSettingGroupResponseDto response)
    {
        Dictionary<string, EffectiveSettingDto> settings = response.Settings
            .Where(setting => !string.IsNullOrWhiteSpace(setting.Key))
            .ToDictionary(setting => setting.Key, StringComparer.OrdinalIgnoreCase);

        bool visitorAccessAvailable = TryGetVisitorAccessMode(settings, out VisitorAccessMode visitorAccessMode);
        return new TenantPublicExperienceAdminModel
        {
            IsAvailable = visitorAccessAvailable,
            Mode = GetString(settings, ModeKey, DefaultMode),
            VisitorAccessMode = visitorAccessAvailable ? visitorAccessMode : null,
            EventCatalogLabel = GetString(settings, EventCatalogLabelKey, DefaultEventCatalogLabel),
            PrimaryOrganizationId = GetGuid(settings, PrimaryOrganizationIdKey),
            HomeBlocksJson = GetString(settings, HomeBlocksKey, DefaultHomeBlocksJson),
            CtasJson = GetString(settings, CtasKey, DefaultCtasJson),
            EventSectionPresetsJson = GetString(settings, EventSectionPresetsKey, DefaultEventSectionPresetsJson),
            CanEditMode = CanEdit(settings, ModeKey),
            CanEditVisitorAccessMode = CanEdit(settings, VisitorAccessModeKey),
            CanEditEventCatalogLabel = CanEdit(settings, EventCatalogLabelKey),
            CanEditPrimaryOrganization = CanEdit(settings, PrimaryOrganizationIdKey),
            CanEditHomeBlocks = CanEdit(settings, HomeBlocksKey),
            CanEditCtas = CanEdit(settings, CtasKey),
            CanEditEventSectionPresets = CanEdit(settings, EventSectionPresetsKey)
        };
    }

    private static void CopySettings(
        TenantPublicExperienceAdminModel source,
        TenantPublicExperienceAdminModel target)
    {
        target.IsAvailable = source.IsAvailable;
        target.Mode = source.Mode;
        target.VisitorAccessMode = source.VisitorAccessMode;
        target.UserRegistrationEnabled = source.UserRegistrationEnabled;
        target.CanEditUserRegistration = source.CanEditUserRegistration;
        target.EventCatalogLabel = source.EventCatalogLabel;
        target.PrimaryOrganizationId = source.PrimaryOrganizationId;
        target.HomeBlocksJson = source.HomeBlocksJson;
        target.CtasJson = source.CtasJson;
        target.EventSectionPresetsJson = source.EventSectionPresetsJson;
        target.CanEditMode = source.CanEditMode;
        target.CanEditVisitorAccessMode = source.CanEditVisitorAccessMode;
        target.CanEditEventCatalogLabel = source.CanEditEventCatalogLabel;
        target.CanEditPrimaryOrganization = source.CanEditPrimaryOrganization;
        target.CanEditHomeBlocks = source.CanEditHomeBlocks;
        target.CanEditCtas = source.CanEditCtas;
        target.CanEditEventSectionPresets = source.CanEditEventSectionPresets;
    }

    private static void MarkUnavailable(TenantPublicExperienceAdminModel model)
    {
        model.IsAvailable = false;
        model.CanEditMode = false;
        model.CanEditVisitorAccessMode = false;
        model.CanEditUserRegistration = false;
        model.CanEditEventCatalogLabel = false;
        model.CanEditPrimaryOrganization = false;
        model.CanEditHomeBlocks = false;
        model.CanEditCtas = false;
        model.CanEditEventSectionPresets = false;
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

    private static bool TryGetVisitorAccessMode(
        IReadOnlyDictionary<string, EffectiveSettingDto> settings,
        out VisitorAccessMode mode)
    {
        mode = VisitorAccessMode.FullRegistrationAndAuth;
        if (!settings.TryGetValue(VisitorAccessModeKey, out EffectiveSettingDto? setting)
            || string.IsNullOrWhiteSpace(setting.Value))
        {
            return false;
        }

        string value = GetString(settings, VisitorAccessModeKey, string.Empty);
        return Enum.TryParse(value, ignoreCase: false, out mode) && Enum.IsDefined(mode);
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
    public bool IsAvailable { get; set; }
    public string Mode { get; set; } = "DiscoveryCentric";
    public VisitorAccessMode? VisitorAccessMode { get; set; }
    public bool? UserRegistrationEnabled { get; set; }
    public bool CanEditUserRegistration { get; set; }
    public string EventCatalogLabel { get; set; } = "Events";
    public Guid? PrimaryOrganizationId { get; set; }
    public string HomeBlocksJson { get; set; } = "{\"schemaVersion\":1,\"blocks\":[]}";
    public string CtasJson { get; set; } = "{\"schemaVersion\":1,\"ctas\":[]}";
    public string EventSectionPresetsJson { get; set; } = "{\"schemaVersion\":1,\"presets\":[]}";
    public bool CanEditMode { get; set; }
    public bool CanEditVisitorAccessMode { get; set; }
    public bool CanEditEventCatalogLabel { get; set; }
    public bool CanEditPrimaryOrganization { get; set; }
    public bool CanEditHomeBlocks { get; set; }
    public bool CanEditCtas { get; set; }
    public bool CanEditEventSectionPresets { get; set; }

    public bool CanEditAny => IsAvailable && (CanEditMode
        || CanEditVisitorAccessMode
        || CanEditUserRegistration
        || CanEditEventCatalogLabel
        || CanEditPrimaryOrganization
        || CanEditHomeBlocks
        || CanEditCtas
        || CanEditEventSectionPresets);

    public static TenantPublicExperienceAdminModel Unavailable() => new();
}

public sealed record PublicExperienceAdminSaveResult(
    bool Success,
    string Message,
    bool AuthoritativeStateRestored = false)
{
    public static PublicExperienceAdminSaveResult Successful() => new(true, string.Empty);
    public static PublicExperienceAdminSaveResult Failed(
        string message,
        bool authoritativeStateRestored = false) =>
        new(false, message, authoritativeStateRestored);
    public static PublicExperienceAdminSaveResult Unavailable() =>
        Failed("Public experience settings are unavailable. Reload them before saving.");
}

using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Helpers;

namespace Explore.Blazor.Client.Services;

public interface ITenantOnboardingService
{
    Task<TenantOnboardingStatusDto?> GetStatusAsync();
    Task<TenantPolicySettingsDto> GetSettingsAsync();
    Task<TenantPolicySettingsDto?> GetManagementSettingsAsync(
        CancellationToken cancellationToken = default);
    Task<SettingGroupResponseDto?> GetTenantSettingsAsync(
        string category,
        CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> UpdateTenantSettingAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> CompleteAsync(TenantPolicySettingsDto settings);
    Task<IReadOnlyList<AiAssistantModelDto>> GetAiModelsAsync(string endpointUrl, string? apiKey);
}

public class TenantOnboardingService : ITenantOnboardingService
{
    public const string DirectoryOperatorIdentityUnavailableCode =
        "tenant_directory_operator_identity_unavailable";

    private readonly ITenantOnboardingClient _api;
    private readonly ISettingsClient _settingsClient;
    private readonly IAiAssistantClient _aiAssistantClient;
    private readonly ITenantDirectoryOperatorIdentityAdminService _directoryOperatorIdentity;
    private readonly ILogger<TenantOnboardingService> _logger;

    public TenantOnboardingService(
        ITenantOnboardingClient api,
        ISettingsClient settingsClient,
        IAiAssistantClient aiAssistantClient,
        ITenantDirectoryOperatorIdentityAdminService directoryOperatorIdentity,
        ILogger<TenantOnboardingService> logger)
    {
        _api = api;
        _settingsClient = settingsClient;
        _aiAssistantClient = aiAssistantClient;
        _directoryOperatorIdentity = directoryOperatorIdentity;
        _logger = logger;
    }

    public async Task<TenantOnboardingStatusDto?> GetStatusAsync()
    {
        try
        {
            var resource = await _api.GetTenantOnboardingStatusAsync(cancellationToken: CancellationToken.None);
            return resource.ToDto();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch tenant onboarding status.");
            return null;
        }
    }

    public async Task<TenantPolicySettingsDto> GetSettingsAsync()
    {
        try
        {
            return await _api.GetTenantOnboardingPolicySettingsAsync(cancellationToken: CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch tenant policy settings.");
            return DefaultSettings();
        }
    }

    public async Task<TenantPolicySettingsDto?> GetManagementSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _api.GetTenantOnboardingPolicySettingsAsync(
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Loading tenant management settings was cancelled.");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch tenant management settings.");
            return null;
        }
    }

    public async Task<SettingGroupResponseDto?> GetTenantSettingsAsync(
        string category,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _settingsClient.GetTenantScopedSettingsAsync(
                category,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Loading tenant setting category {Category} was cancelled.", category);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch tenant setting category {Category}.", category);
            return null;
        }
    }

    public Task<BaseCommandResponseOfGuid> UpdateTenantSettingAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default) =>
        SendCommandAsync(
            () => _settingsClient.UpdateTenantSettingAsync(
                key,
                new UpdateSettingValueDto { Value = value },
                cancellationToken: cancellationToken),
            cancellationToken);

    public Task<BaseCommandResponseOfGuid> CompleteAsync(TenantPolicySettingsDto settings) =>
        SendCommandAsync(async () =>
        {
            TenantDirectoryOperatorIdentityAdminModel identity =
                await _directoryOperatorIdentity.GetAsync(CancellationToken.None);
            if (identity.MessageCode != TenantDirectoryOperatorIdentityAdminMessageCode.None)
            {
                return new BaseCommandResponseOfGuid
                {
                    Success = false,
                    Message = DirectoryOperatorIdentityUnavailableCode
                };
            }

            return await _api.CompleteTenantOnboardingAsync(
                new CompleteTenantOnboardingRequest
                {
                    Settings = ToRequest(settings, false),
                    DirectoryOperatorIdentity = new TenantDirectoryOperatorIdentityInputDto
                    {
                        PublicName = identity.PublicName,
                        LegalName = identity.LegalName,
                        OperatorKindCode = identity.OperatorKindCode,
                        JurisdictionCountryCode = identity.JurisdictionCountryCode,
                        RegistrationIdentifier = identity.RegistrationIdentifier,
                        PublicContactEmail = identity.PublicContactEmail,
                        LegalNoticeUrl = identity.LegalNoticeUrl,
                        TermsUrl = identity.TermsUrl,
                        PrivacyUrl = identity.PrivacyUrl
                    },
                    ExpectedDirectoryOperatorIdentityConcurrencyStamp = identity.ConcurrencyStamp
                },
                cancellationToken: CancellationToken.None);
        });

    public async Task<IReadOnlyList<AiAssistantModelDto>> GetAiModelsAsync(string endpointUrl, string? apiKey)
    {
        try
        {
            var response = await _aiAssistantClient.GetAiAssistantModelsAsync(new AiAssistantModelDiscoveryRequestDto
            {
                EndpointUrl = endpointUrl,
                ApiKey = apiKey
            }, cancellationToken: CancellationToken.None);

            return response.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover AI assistant models.");
            return [];
        }
    }

    private async Task<BaseCommandResponseOfGuid> SendCommandAsync(
        Func<Task<BaseCommandResponseOfGuid>> sendFunc,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await sendFunc();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Tenant onboarding request was cancelled.");
            return new BaseCommandResponseOfGuid
            {
                Success = false,
                Message = "Request cancelled."
            };
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "Tenant onboarding endpoint returned status {StatusCode}.", ex.StatusCode);
            return new BaseCommandResponseOfGuid
            {
                Success = false,
                Message = $"Request failed with status {ex.StatusCode}."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to call tenant onboarding endpoint.");
            return new BaseCommandResponseOfGuid
            {
                Success = false,
                Message = "Request failed."
            };
        }
    }

    private static TenantPolicySettingsDto DefaultSettings() => new()
    {
        AllowUserSubmittedEvents = true,
        AllowOrganizationSubmittedEvents = true,
        AllowGroupSubmittedEvents = true,
        AllowOrganizationSelfRegistration = true,
        AllowGroupSelfRegistration = true,
        RequireOrganizationVerification = true,
        PreferredHomePage = "EventList",
        CanOverrideHomePagePreference = true,
        CanOverrideSubdomain = true,
        CanOverrideCustomDomain = true,
        CanOverrideEventCardClickBehavior = true,
        CanOverrideCommunityGuidelines = true,
        AiAssistantProvider = "none"
    };

    private static UpdateTenantPolicyRequest ToRequest(
        TenantPolicySettingsDto settings,
        bool forceAnnouncementBarRedisplay) => new()
        {
            AllowUserSubmittedEvents = settings.AllowUserSubmittedEvents,
            AllowOrganizationSubmittedEvents = settings.AllowOrganizationSubmittedEvents,
            AllowGroupSubmittedEvents = settings.AllowGroupSubmittedEvents,
            AllowOrganizationSelfRegistration = settings.AllowOrganizationSelfRegistration,
            AllowGroupSelfRegistration = settings.AllowGroupSelfRegistration,
            EventCardClickOpensDetailPage = settings.EventCardClickOpensDetailPage,
            RequireEventApproval = settings.RequireEventApproval,
            RequireOrganizationVerification = settings.RequireOrganizationVerification,
            PreferredHomePage = settings.PreferredHomePage,
            Subdomain = settings.Subdomain,
            CustomDomain = settings.CustomDomain,
            AnnouncementBarEnabled = settings.AnnouncementBarEnabled,
            AnnouncementBarMessage = settings.AnnouncementBarMessage,
            AnnouncementBarLinkText = settings.AnnouncementBarLinkText,
            AnnouncementBarLinkUrl = settings.AnnouncementBarLinkUrl,
            ForceAnnouncementBarRedisplay = forceAnnouncementBarRedisplay,
            CommunityGuidelinesContent = settings.CommunityGuidelinesContent,
            RenderPolicyPreset = settings.RenderPolicyPreset,
            EnableAdvancedRenderPolicyOverrides = settings.EnableAdvancedRenderPolicyOverrides,
            GlobalRenderMode = settings.GlobalRenderMode,
            GlobalPrerenderEnabled = settings.GlobalPrerenderEnabled,
            PublicSeoRenderMode = settings.PublicSeoRenderMode,
            PublicSeoPrerenderEnabled = settings.PublicSeoPrerenderEnabled,
            OperationalRenderMode = settings.OperationalRenderMode,
            OperationalPrerenderEnabled = settings.OperationalPrerenderEnabled,
            AdminRenderMode = settings.AdminRenderMode,
            AdminPrerenderEnabled = settings.AdminPrerenderEnabled,
            AiAssistantEnabled = settings.AiAssistantEnabled,
            AiAssistantProvider = settings.AiAssistantProvider,
            AiAssistantEndpointUrl = settings.AiAssistantEndpointUrl,
            AiAssistantApiKey = settings.AiAssistantApiKey,
            AiAssistantModelId = settings.AiAssistantModelId,
            AiAssistantAllowedModelIds = settings.AiAssistantAllowedModelIds,
            AiAssistantAllowAnonymousAccess = settings.AiAssistantAllowAnonymousAccess,
            McpEnabled = settings.McpEnabled,
            McpEnableLegacySse = settings.McpEnableLegacySse
        };
}

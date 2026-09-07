// ABOUTME: Unit tests for tenant-admin public experience settings mapping and save behavior.
// ABOUTME: Verifies generic setting endpoints are wrapped safely for post-onboarding public UX controls.

namespace Explore.Blazor.Client.Tests.Services;

public class TenantPublicExperienceAdminServiceTests
{
    private const string Category = "PublicExperience";

    private readonly ISettingsClient _apiClient;
    private readonly IPublicExperienceService _publicExperienceService;
    private readonly ILogger<TenantPublicExperienceAdminService> _logger;
    private readonly TenantPublicExperienceAdminService _service;

    public TenantPublicExperienceAdminServiceTests()
    {
        _apiClient = Substitute.For<ISettingsClient>();
        _publicExperienceService = Substitute.For<IPublicExperienceService>();
        _logger = Substitute.For<ILogger<TenantPublicExperienceAdminService>>();
        _service = new TenantPublicExperienceAdminService(_apiClient, _publicExperienceService, _logger);
    }

    [Test]
    public async Task GetSettingsAsync_MapsPublicExperienceSettings_WhenApiReturnsCategory()
    {
        // Arrange
        Guid organizationId = Guid.NewGuid();
        var response = new HalResourceOfSettingGroupResponseDto
        {
            Category = Category,
            Settings =
            [
                Setting("public_experience.mode", "OrganizationCentric"),
                Setting("public_experience.event_catalog_label", "Programs"),
                Setting("public_experience.primary_organization_id", organizationId.ToString("D"), canEdit: false),
                Setting("public_experience.home_blocks", "{\"schemaVersion\":1,\"blocks\":[{\"id\":\"hero\"}]}"),
                Setting("public_experience.ctas", "{\"schemaVersion\":1,\"ctas\":[]}"),
                Setting("public_experience.event_section_presets", "{\"schemaVersion\":1,\"presets\":[]}")
            ]
        };

        _apiClient.GetTenantScopedSettingsAsync(
                Category,
                null,
                null,
                Arg.Any<CancellationToken>())
            .Returns(response);

        // Act
        TenantPublicExperienceAdminModel result = await _service.GetSettingsAsync();

        // Assert
        await Assert.That(result.Mode).IsEqualTo("OrganizationCentric");
        await Assert.That(result.EventCatalogLabel).IsEqualTo("Programs");
        await Assert.That(result.PrimaryOrganizationId).IsEqualTo(organizationId);
        await Assert.That(result.HomeBlocksJson).Contains("hero");
        await Assert.That(result.CanEditPrimaryOrganization).IsFalse();
        await Assert.That(result.CanEditMode).IsTrue();
    }

    [Test]
    public async Task GetSettingsAsync_ReturnsConservativeDefaults_WhenApiThrows()
    {
        // Arrange
        _apiClient.GetTenantScopedSettingsAsync(
                Category,
                null,
                null,
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("settings unavailable"));

        // Act
        TenantPublicExperienceAdminModel result = await _service.GetSettingsAsync();

        // Assert
        await Assert.That(result.Mode).IsEqualTo("DiscoveryCentric");
        await Assert.That(result.EventCatalogLabel).IsEqualTo("Events");
        await Assert.That(result.PrimaryOrganizationId).IsNull();
        await Assert.That(result.HomeBlocksJson).IsEqualTo("{\"schemaVersion\":1,\"blocks\":[]}");
        await Assert.That(result.CanEditAny).IsTrue();
    }

    [Test]
    public async Task ApplySingleTenantPolicySettingsAsync_MapsPolicyAndAiAssistantTenantSettings()
    {
        // Arrange
        var model = new TenantPolicySettingsDto();

        _apiClient.GetTenantScopedSettingsAsync(
                "Events",
                null,
                null,
                Arg.Any<CancellationToken>())
            .Returns(new HalResourceOfSettingGroupResponseDto
            {
                Category = "Events",
                Settings =
                [
                    Setting("events.user_submission_enabled", "false"),
                    Setting("events.organization_submission_enabled", "true"),
                    Setting("events.group_submission_enabled", "false"),
                    Setting("events.require_approval", "true"),
                    Setting("events.card_click_opens_detail_page", "true")
                ]
            });
        _apiClient.GetTenantScopedSettingsAsync(
                "Organizations",
                null,
                null,
                Arg.Any<CancellationToken>())
            .Returns(new HalResourceOfSettingGroupResponseDto
            {
                Category = "Organizations",
                Settings =
                [
                    Setting("organizations.verification_required", "false"),
                    Setting("organizations.self_registration_enabled", "false")
                ]
            });
        _apiClient.GetTenantScopedSettingsAsync(
                "Groups",
                null,
                null,
                Arg.Any<CancellationToken>())
            .Returns(new HalResourceOfSettingGroupResponseDto
            {
                Category = "Groups",
                Settings =
                [
                    Setting("groups.self_registration_enabled", "false")
                ]
            });
        _apiClient.GetTenantScopedSettingsAsync(
                "AiAssistant",
                null,
                null,
                Arg.Any<CancellationToken>())
            .Returns(new HalResourceOfSettingGroupResponseDto
            {
                Category = "AiAssistant",
                Settings =
                [
                    Setting("ai_assistant.enabled", "true"),
                    Setting("ai_assistant.provider", "\"openai-compatible\""),
                    Setting("ai_assistant.endpoint_url", "\"https://ai.example.test\""),
                    Setting("ai_assistant.api_key", "\"secret-ref\""),
                    Setting("ai_assistant.model_id", "\"gpt-test\""),
                    Setting("ai_assistant.allow_anonymous_access", "true")
                ]
            });

        // Act
        await _service.ApplySingleTenantPolicySettingsAsync(model);

        // Assert
        await Assert.That(model.AllowUserSubmittedEvents).IsFalse();
        await Assert.That(model.AllowOrganizationSubmittedEvents).IsTrue();
        await Assert.That(model.AllowGroupSubmittedEvents).IsFalse();
        await Assert.That(model.RequireEventApproval).IsTrue();
        await Assert.That(model.EventCardClickOpensDetailPage).IsTrue();
        await Assert.That(model.RequireOrganizationVerification).IsFalse();
        await Assert.That(model.AllowOrganizationSelfRegistration).IsFalse();
        await Assert.That(model.AllowGroupSelfRegistration).IsFalse();
        await Assert.That(model.AiAssistantEnabled).IsTrue();
        await Assert.That(model.AiAssistantProvider).IsEqualTo("openai-compatible");
        await Assert.That(model.AiAssistantEndpointUrl).IsEqualTo("https://ai.example.test");
        await Assert.That(model.AiAssistantApiKey).IsEqualTo("secret-ref");
        await Assert.That(model.AiAssistantModelId).IsEqualTo("gpt-test");
        await Assert.That(model.AiAssistantAllowAnonymousAccess).IsTrue();
        await Assert.That(model.CanTenantOmitVerification).IsTrue();
        await Assert.That(model.CanOverrideEventCardClickBehavior).IsTrue();
        await Assert.That(model.CanOverrideAiAssistant).IsTrue();
    }

    [Test]
    public async Task SaveAsync_UsesStrictBatchAndNormalizesEditableValues()
    {
        // Arrange
        var model = new TenantPublicExperienceAdminModel
        {
            Mode = "OrganizationCentric",
            EventCatalogLabel = "  Programs  ",
            PrimaryOrganizationId = null,
            HomeBlocksJson = " ",
            CtasJson = "{\"schemaVersion\":1,\"ctas\":[{\"id\":\"join\"}]}",
            EventSectionPresetsJson = "\n{\"schemaVersion\":1,\"presets\":[]}\n"
        };

        UpdateSettingBatchDto? captured = null;
        _apiClient.UpdateTenantSettingsBatchAsync(
                Category,
                Arg.Do<UpdateSettingBatchDto>(body => captured = body),
                null,
                null,
                Arg.Any<CancellationToken>())
            .Returns(new BatchUpdateResponseDto { Success = true });

        // Act
        PublicExperienceAdminSaveResult result = await _service.SaveAsync(model);

        // Assert
        await Assert.That(result.Success).IsTrue();
        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.Mode).IsEqualTo(BatchUpdateMode.Strict);
        await Assert.That(captured.Values["public_experience.mode"]).IsEqualTo("OrganizationCentric");
        await Assert.That(captured.Values["public_experience.event_catalog_label"]).IsEqualTo("Programs");
        await Assert.That(captured.Values["public_experience.primary_organization_id"]).IsEqualTo(string.Empty);
        await Assert.That(captured.Values["public_experience.home_blocks"]).IsEqualTo("{\"schemaVersion\":1,\"blocks\":[]}");
        await Assert.That(captured.Values["public_experience.ctas"]).Contains("join");
        await Assert.That(captured.Values["public_experience.event_section_presets"]).IsEqualTo("{\"schemaVersion\":1,\"presets\":[]}");
    }

    [Test]
    public async Task SaveAsync_ReturnsFailureMessage_WhenStrictBatchFails()
    {
        // Arrange
        var response = new BatchUpdateResponseDto
        {
            Success = false,
            Results =
            [
                new SettingUpdateResultDto
                {
                    Key = "public_experience.mode",
                    Applied = false,
                    SkipReason = "Locked"
                }
            ]
        };

        _apiClient.UpdateTenantSettingsBatchAsync(
                Category,
                Arg.Any<UpdateSettingBatchDto>(),
                null,
                null,
                Arg.Any<CancellationToken>())
            .Returns(response);

        // Act
        PublicExperienceAdminSaveResult result = await _service.SaveAsync(new TenantPublicExperienceAdminModel());

        // Assert
        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Message).Contains("public_experience.mode: Locked");
    }



    [Test]
    public async Task SaveAsync_OmitsLockedFields_WhenSomeSettingsAreNotEditable()
    {
        // Arrange
        Guid organizationId = Guid.NewGuid();
        var model = new TenantPublicExperienceAdminModel
        {
            Mode = "OrganizationCentric",
            EventCatalogLabel = "Programs",
            PrimaryOrganizationId = organizationId,
            HomeBlocksJson = """{"schemaVersion":1,"blocks":[{"id":"hero"}]}""",
            CtasJson = """{"schemaVersion":1,"ctas":[]}""",
            EventSectionPresetsJson = """{"schemaVersion":1,"presets":[]}""",
            CanEditMode = true,
            CanEditEventCatalogLabel = false,
            CanEditPrimaryOrganization = true,
            CanEditHomeBlocks = false,
            CanEditCtas = true,
            CanEditEventSectionPresets = false
        };

        UpdateSettingBatchDto? captured = null;
        _apiClient.UpdateTenantSettingsBatchAsync(
                Category,
                Arg.Do<UpdateSettingBatchDto>(body => captured = body),
                null,
                null,
                Arg.Any<CancellationToken>())
            .Returns(new BatchUpdateResponseDto { Success = true });

        // Act
        PublicExperienceAdminSaveResult result = await _service.SaveAsync(model);

        // Assert
        await Assert.That(result.Success).IsTrue();
        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.Values.ContainsKey("public_experience.mode")).IsTrue();
        await Assert.That(captured.Values.ContainsKey("public_experience.primary_organization_id")).IsTrue();
        await Assert.That(captured.Values.ContainsKey("public_experience.ctas")).IsTrue();
        await Assert.That(captured.Values.ContainsKey("public_experience.event_catalog_label")).IsFalse();
        await Assert.That(captured.Values.ContainsKey("public_experience.home_blocks")).IsFalse();
        await Assert.That(captured.Values.ContainsKey("public_experience.event_section_presets")).IsFalse();
        await Assert.That(captured.Values["public_experience.primary_organization_id"]).IsEqualTo(organizationId.ToString("D"));
    }

    [Test]
    public async Task SaveAsync_DoesNotCallApi_WhenNoSettingsAreEditable()
    {
        // Arrange
        var model = new TenantPublicExperienceAdminModel
        {
            CanEditMode = false,
            CanEditEventCatalogLabel = false,
            CanEditPrimaryOrganization = false,
            CanEditHomeBlocks = false,
            CanEditCtas = false,
            CanEditEventSectionPresets = false
        };

        // Act
        PublicExperienceAdminSaveResult result = await _service.SaveAsync(model);

        // Assert
        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Message).Contains("No public experience settings are editable");
        await _apiClient.DidNotReceiveWithAnyArgs().UpdateTenantSettingsBatchAsync(
            default!,
            default!,
            default,
            default,
            default);
    }

    [Test]
    public async Task SaveSingleTenantPolicySettingsAsync_DoesNotSaveAiAssistantCategory()
    {
        // Arrange
        var model = new TenantPolicySettingsDto
        {
            AllowUserSubmittedEvents = true,
            AllowOrganizationSubmittedEvents = true,
            AllowGroupSubmittedEvents = false,
            RequireEventApproval = true,
            EventCardClickOpensDetailPage = true,
            RequireOrganizationVerification = true,
            AllowOrganizationSelfRegistration = false,
            AllowGroupSelfRegistration = true,
            CanOverrideAiAssistant = false,
            AiAssistantEnabled = true,
            AiAssistantProvider = "openai-compatible",
            AiAssistantEndpointUrl = "https://ai.example.test",
            AiAssistantApiKey = "secret-ref",
            AiAssistantModelId = "gpt-test",
            AiAssistantAllowAnonymousAccess = false
        };

        var categories = new List<string>();
        var batches = new List<UpdateSettingBatchDto>();

        _apiClient.UpdateTenantSettingsBatchAsync(
                Arg.Do<string>(category => categories.Add(category)),
                Arg.Do<UpdateSettingBatchDto>(body => batches.Add(body)),
                null,
                null,
                Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.ArgAt<string>(0) == "AiAssistant"
                ? new BatchUpdateResponseDto { Success = false, Message = "Locked by instance administrator" }
                : new BatchUpdateResponseDto { Success = true });

        // Act
        PublicExperienceAdminSaveResult result = await _service.SaveSingleTenantPolicySettingsAsync(model);

        // Assert
        await Assert.That(result.Success).IsTrue();
        await Assert.That(categories).IsEquivalentTo(["Events", "Organizations", "Groups"]);
        await Assert.That(batches.Count).IsEqualTo(3);
        await Assert.That(batches.All(batch => batch.Mode == BatchUpdateMode.Strict)).IsTrue();
        await Assert.That(batches[0].Values["events.user_submission_enabled"]).IsEqualTo("true");
        await Assert.That(batches[0].Values["events.group_submission_enabled"]).IsEqualTo("false");
        await Assert.That(batches[0].Values["events.card_click_opens_detail_page"]).IsEqualTo("true");
        await Assert.That(batches[1].Values["organizations.verification_required"]).IsEqualTo("true");
        await Assert.That(batches[2].Values["groups.self_registration_enabled"]).IsEqualTo("true");
    }

    [Test]
    public async Task SaveSingleTenantPolicySettingsAsync_IgnoresAiAssistantConfigWithoutApiKey()
    {
        // Arrange
        var model = new TenantPolicySettingsDto
        {
            AllowUserSubmittedEvents = true,
            AllowOrganizationSubmittedEvents = true,
            AllowGroupSubmittedEvents = true,
            RequireOrganizationVerification = true,
            AiAssistantEnabled = true,
            AiAssistantProvider = "openai-compatible",
            AiAssistantEndpointUrl = "http://127.0.0.1:1337/v1",
            AiAssistantApiKey = string.Empty,
            AiAssistantModelId = "Gemma-4-E2B-Uncensored-HauhauCS-Aggressive-Q8_K_P"
        };

        var categories = new List<string>();
        var batches = new List<UpdateSettingBatchDto>();
        _apiClient.UpdateTenantSettingsBatchAsync(
                Arg.Do<string>(category => categories.Add(category)),
                Arg.Do<UpdateSettingBatchDto>(body => batches.Add(body)),
                null,
                null,
                Arg.Any<CancellationToken>())
            .Returns(new BatchUpdateResponseDto { Success = true });

        // Act
        PublicExperienceAdminSaveResult result = await _service.SaveSingleTenantPolicySettingsAsync(model);

        // Assert
        await Assert.That(result.Success).IsTrue();
        await Assert.That(categories).IsEquivalentTo(["Events", "Organizations", "Groups"]);
        await Assert.That(batches.Count).IsEqualTo(3);
    }

    [Test]
    public async Task SaveSingleTenantPolicySettingsAsync_DoesNotValidateIncompleteAiAssistantConfig()
    {
        // Arrange
        var model = new TenantPolicySettingsDto
        {
            AiAssistantEnabled = true,
            AiAssistantProvider = "openai-compatible",
            AiAssistantEndpointUrl = "https://ai.example.test",
            AiAssistantApiKey = "secret-ref",
            AiAssistantModelId = string.Empty
        };

        var categories = new List<string>();
        _apiClient.UpdateTenantSettingsBatchAsync(
                Arg.Do<string>(category => categories.Add(category)),
                Arg.Any<UpdateSettingBatchDto>(),
                null,
                null,
                Arg.Any<CancellationToken>())
            .Returns(new BatchUpdateResponseDto { Success = true });

        // Act
        PublicExperienceAdminSaveResult result = await _service.SaveSingleTenantPolicySettingsAsync(model);

        // Assert
        await Assert.That(result.Success).IsTrue();
        await Assert.That(categories).IsEquivalentTo(["Events", "Organizations", "Groups"]);
    }

    [Test]
    public async Task SaveSingleTenantPolicySettingsAsync_DoesNotValidateStaleAiAssistantProvider()
    {
        // Arrange
        var model = new TenantPolicySettingsDto
        {
            AiAssistantEnabled = true,
            AiAssistantProvider = "fake",
            AiAssistantEndpointUrl = "https://stale.example.test",
            AiAssistantApiKey = "stale-secret",
            AiAssistantModelId = "stale-model",
            AiAssistantAllowAnonymousAccess = true
        };

        var categories = new List<string>();
        _apiClient.UpdateTenantSettingsBatchAsync(
                Arg.Do<string>(category => categories.Add(category)),
                Arg.Any<UpdateSettingBatchDto>(),
                null,
                null,
                Arg.Any<CancellationToken>())
            .Returns(new BatchUpdateResponseDto { Success = true });

        // Act
        PublicExperienceAdminSaveResult result = await _service.SaveSingleTenantPolicySettingsAsync(model);

        // Assert
        await Assert.That(result.Success).IsTrue();
        await Assert.That(categories).IsEquivalentTo(["Events", "Organizations", "Groups"]);
    }

    [Test]
    public async Task SaveAnnouncementBarAsync_IncrementsRevision_WhenForceRedisplayRequested()
    {
        // Arrange
        var model = new TenantPolicySettingsDto
        {
            AnnouncementBarEnabled = true,
            AnnouncementBarMessage = "New update",
            AnnouncementBarLinkText = "Learn more",
            AnnouncementBarLinkUrl = "https://example.test/update",
            AnnouncementBarRevision = 7
        };

        string? capturedCategory = null;
        UpdateSettingBatchDto? captured = null;
        _apiClient.UpdateTenantSettingsBatchAsync(
                Arg.Do<string>(category => capturedCategory = category),
                Arg.Do<UpdateSettingBatchDto>(body => captured = body),
                null,
                null,
                Arg.Any<CancellationToken>())
            .Returns(new BatchUpdateResponseDto { Success = true });

        // Act
        PublicExperienceAdminSaveResult result = await _service.SaveAnnouncementBarAsync(model, forceRedisplay: true);

        // Assert
        await Assert.That(result.Success).IsTrue();
        await Assert.That(capturedCategory).IsEqualTo(Category);
        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.Mode).IsEqualTo(BatchUpdateMode.Strict);
        await Assert.That(captured.Values["public_experience.announcement_bar.enabled"]).IsEqualTo("true");
        await Assert.That(captured.Values["public_experience.announcement_bar.message"]).IsEqualTo("New update");
        await Assert.That(captured.Values["public_experience.announcement_bar.revision"]).IsEqualTo("8");
        await Assert.That(model.AnnouncementBarRevision).IsEqualTo(8);
    }

    private static EffectiveSettingDto Setting(string key, string value, bool? canEdit = true)
    {
        return new EffectiveSettingDto
        {
            Key = key,
            Value = value,
            CanEdit = canEdit,
            SettingValueTypeCode = "String",
            SettingValueTypeName = "String"
        };
    }
}

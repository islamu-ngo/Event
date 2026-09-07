using Explore.Blazor.Client.Helpers;

namespace Explore.Blazor.Client.Tests.Services;

public class TenantOnboardingServiceTests
{
    private readonly ITenantOnboardingClient _api;
    private readonly ISettingsClient _settingsClient;
    private readonly IAiAssistantClient _aiAssistantClient;
    private readonly ITenantDirectoryOperatorIdentityAdminService _directoryOperatorIdentity;
    private readonly TenantOnboardingService _service;

    public TenantOnboardingServiceTests()
    {
        _api = Substitute.For<ITenantOnboardingClient>();
        _settingsClient = Substitute.For<ISettingsClient>();
        _aiAssistantClient = Substitute.For<IAiAssistantClient>();
        _directoryOperatorIdentity = Substitute.For<ITenantDirectoryOperatorIdentityAdminService>();
        _directoryOperatorIdentity.GetAsync(Arg.Any<CancellationToken>())
            .Returns(CompleteIdentity());
        _service = new TenantOnboardingService(
            _api,
            _settingsClient,
            _aiAssistantClient,
            _directoryOperatorIdentity,
            Substitute.For<ILogger<TenantOnboardingService>>());
    }

    [Test]
    public async Task GetStatusAsync_ReturnsMappedStatus_WhenApiSucceeds()
    {
        var tenantId = Guid.NewGuid();
        _api.GetTenantOnboardingStatusAsync(
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new HalResourceOfTenantOnboardingStatusDto
            {
                IsCompleted = true,
                IsAuthenticated = true,
                IsCurrentUserTenantAdministrator = true,
                IsCurrentUserPlatformAdministrator = false,
                TenantId = tenantId,
                _links = new Dictionary<string, HalLink>
                {
                    ["manage-tenant-settings"] = new HalLink { Href = "/api/tenant-onboarding/policy-settings" }
                }
            });

        var result = await _service.GetStatusAsync();

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.IsCompleted).IsTrue();
        await Assert.That(result.TenantId).IsEqualTo(tenantId);
        await Assert.That(result.HasHalLink("manage-tenant-settings")).IsTrue();
        await _api.Received(1).GetTenantOnboardingStatusAsync(
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetStatusAsync_ReturnsNull_WhenApiThrows()
    {
        _api.GetTenantOnboardingStatusAsync(
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<HalResourceOfTenantOnboardingStatusDto>>(_ => throw new HttpRequestException("boom"));

        var result = await _service.GetStatusAsync();

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetSettingsAsync_ReturnsMappedSettings_WhenApiSucceeds()
    {
        _api.GetTenantOnboardingPolicySettingsAsync(
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new TenantPolicySettingsDto
            {
                AllowUserSubmittedEvents = false,
                RequireEventApproval = true,
                PreferredHomePage = "Dashboard"
            });

        var result = await _service.GetSettingsAsync();

        await Assert.That(result.RequireEventApproval).IsTrue();
        await Assert.That(result.PreferredHomePage).IsEqualTo("Dashboard");
    }

    [Test]
    public async Task GetSettingsAsync_ReturnsDefaultSettings_WhenApiThrows()
    {
        _api.GetTenantOnboardingPolicySettingsAsync(
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<TenantPolicySettingsDto>>(_ => throw new HttpRequestException("boom"));

        var result = await _service.GetSettingsAsync();

        await Assert.That(result).IsNotNull();
        await Assert.That(result.PreferredHomePage).IsEqualTo("EventList");
    }

    [Test]
    public async Task GetManagementSettingsAsync_ReturnsNull_WhenApiThrows()
    {
        _api.GetTenantOnboardingPolicySettingsAsync(
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<TenantPolicySettingsDto>>(_ => throw new HttpRequestException("boom"));

        TenantPolicySettingsDto? result = await _service.GetManagementSettingsAsync();

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetTenantSettingsAsync_ForwardsExactCategory()
    {
        _settingsClient.GetTenantScopedSettingsAsync(
                "Events",
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new SettingGroupResponseDto { Category = "Events" });

        var result = await _service.GetTenantSettingsAsync("Events");

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Category).IsEqualTo("Events");
        await _settingsClient.Received(1).GetTenantScopedSettingsAsync(
            "Events",
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateTenantSettingAsync_ForwardsExactKeyAndValue()
    {
        _settingsClient.UpdateTenantSettingAsync(
                "events.user_submission_enabled",
                Arg.Is<UpdateSettingValueDto>(body => body != null && body.Value == "true"),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true });

        var result = await _service.UpdateTenantSettingAsync(
            "events.user_submission_enabled",
            "true");

        await Assert.That(result.Success).IsTrue();
        await _settingsClient.Received(1).UpdateTenantSettingAsync(
            "events.user_submission_enabled",
            Arg.Is<UpdateSettingValueDto>(body => body != null && body.Value == "true"),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateTenantSettingAsync_WhenCancelled_ReturnsFailure()
    {
        _settingsClient.UpdateTenantSettingAsync(
                Arg.Any<string>(),
                Arg.Any<UpdateSettingValueDto>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<BaseCommandResponseOfGuid>>(_ => throw new OperationCanceledException());

        var result = await _service.UpdateTenantSettingAsync(
            "events.user_submission_enabled",
            "true",
            new CancellationToken(canceled: true));

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Message).IsEqualTo("Request cancelled.");
    }

    [Test]
    public async Task CompleteAsync_ForwardsMappedSettingsAndReturnsSuccess()
    {
        _api.CompleteTenantOnboardingAsync(
                Arg.Any<CompleteTenantOnboardingRequest>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid
            {
                Success = true,
                Message = "OK"
            });
        var settings = new TenantPolicySettingsDto
        {
            PreferredHomePage = "Dashboard",
            RequireEventApproval = true
        };

        var result = await _service.CompleteAsync(settings);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Message).IsEqualTo("OK");
        await _api.Received(1).CompleteTenantOnboardingAsync(
            Arg.Is<CompleteTenantOnboardingRequest>(request =>
                request.Settings.PreferredHomePage == "Dashboard"
                && request.Settings.RequireEventApproval == true
                && request.DirectoryOperatorIdentity.LegalName == "Community Events ASBL"
                && request.ExpectedDirectoryOperatorIdentityConcurrencyStamp ==
                    Guid.Parse("018e4e5c-7f00-7000-8000-000000000202")),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CompleteAsync_ReturnsFailure_WhenApiThrows()
    {
        _api.CompleteTenantOnboardingAsync(
                Arg.Any<CompleteTenantOnboardingRequest>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<BaseCommandResponseOfGuid>>(_ => throw new HttpRequestException("network failed"));

        var result = await _service.CompleteAsync(new TenantPolicySettingsDto());

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Message).IsEqualTo("Request failed.");
        await Assert.That(result.Errors).IsNull();
    }

    [Test]
    public async Task CompleteAsync_FailsClosedWhenDirectoryIdentityIsUnavailable()
    {
        _directoryOperatorIdentity.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new TenantDirectoryOperatorIdentityAdminModel
            {
                MessageCode = TenantDirectoryOperatorIdentityAdminMessageCode.LoadFailed
            });

        BaseCommandResponseOfGuid result =
            await _service.CompleteAsync(new TenantPolicySettingsDto());

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Message)
            .IsEqualTo(TenantOnboardingService.DirectoryOperatorIdentityUnavailableCode);
        await _api.DidNotReceiveWithAnyArgs().CompleteTenantOnboardingAsync(default!);
    }

    private static TenantDirectoryOperatorIdentityAdminModel CompleteIdentity() => new()
    {
        ConcurrencyStamp = Guid.Parse("018e4e5c-7f00-7000-8000-000000000202"),
        PublicName = "Community Events",
        LegalName = "Community Events ASBL",
        OperatorKindCode = "registered_organization",
        JurisdictionCountryCode = "BE",
        RegistrationIdentifier = "BE 0123.456.789",
        PublicContactEmail = "contact@example.test",
        LegalNoticeUrl = "https://example.test/legal",
        TermsUrl = "https://example.test/terms",
        PrivacyUrl = "https://example.test/privacy"
    };
}

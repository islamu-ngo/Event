using System.Text.Json;
using Explore.Blazor.Client.Contracts.Services.Shell;
using Explore.Blazor.Client.Contracts.Services.PaidEventPolicies;
using Explore.Blazor.Client.Pages.Admin.Tenant;
using Explore.Blazor.Client.Pages.Admin.Tenant.Components;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Admin;

public class TenantAdminSettingsRedirectTests : IDisposable
{
    private readonly BlazorTestContext _ctx;
    private readonly IInstanceOnboardingService _onboardingService;
    private readonly ITenantOnboardingService _tenantOnboardingService;
    private readonly ITenantBrandingSettingsAdminService _tenantBrandingSettingsAdminService;
    private readonly ITenantDirectoryOperatorIdentityAdminService _tenantDirectoryOperatorIdentityAdminService;
    private readonly ITenantPublicExperienceAdminService _publicExperienceAdminService;
    private readonly ITenantStorageSettingsAdminService _tenantStorageSettingsAdminService;
    private readonly IPaidEventPolicyService _paidEventPolicyService;
    private readonly BunitNavigationManager _nav;
    private readonly Guid _tenantId = Guid.CreateVersion7();

    public TenantAdminSettingsRedirectTests()
    {
        _ctx = new BlazorTestContext();
        _ctx.AddShellStateMocks();
        _ctx.SetAuthenticatedUser(Guid.NewGuid(), "Instance Admin", "admin@example.com");

        _onboardingService = _ctx.AddMockService<IInstanceOnboardingService>();
        _tenantOnboardingService = _ctx.AddMockService<ITenantOnboardingService>();
        _publicExperienceAdminService = _ctx.AddMockService<ITenantPublicExperienceAdminService>();
        _tenantStorageSettingsAdminService = _ctx.AddMockService<ITenantStorageSettingsAdminService>();
        _tenantBrandingSettingsAdminService = _ctx.AddMockService<ITenantBrandingSettingsAdminService>();
        _tenantDirectoryOperatorIdentityAdminService =
            _ctx.AddMockService<ITenantDirectoryOperatorIdentityAdminService>();
        _paidEventPolicyService = _ctx.AddMockService<IPaidEventPolicyService>();
        _ctx.AddMockService<IOrganizationService>();
        _ctx.AddMockService<IUiShellContextService>();
        _ctx.AddMockService<IShellPreferencesService>();
        _publicExperienceAdminService.GetSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(new TenantPublicExperienceAdminModel());
        _tenantStorageSettingsAdminService.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new HalResourceOfTenantStorageSettingsDto());
        _tenantDirectoryOperatorIdentityAdminService.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new TenantDirectoryOperatorIdentityAdminModel
            {
                Exists = true
            });

        _nav = _ctx.Services.GetRequiredService<BunitNavigationManager>();
        _nav.NavigateTo("/settings/admin");
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task TenantAdminSettings_SingleTenantMode_DoesNotRedirectTenantAdministrator()
    {
        _onboardingService.GetStatusAsync()
            .Returns(new InstanceOnboardingStatusDto { SelectedDeploymentMode = nameof(DeploymentMode.SingleTenant) });

        _ctx.Render<TenantAdminSettings>();

        await Assert.That(_nav.Uri).EndsWith("/settings/admin");
    }

    [Test]
    public async Task TenantAdminSettings_MultiTenantMode_DoesNotRedirect()
    {
        _onboardingService.GetStatusAsync()
            .Returns(new InstanceOnboardingStatusDto { SelectedDeploymentMode = nameof(DeploymentMode.MultiTenant) });

        _ctx.Render<TenantAdminSettings>();

        await Assert.That(_nav.Uri).EndsWith("/settings/admin");
    }

    [Test]
    public async Task TenantAdminSettingsLayout_RoleMetadataWithoutManageHal_FailsClosed()
    {
        _tenantOnboardingService.GetStatusAsync()
            .Returns(new TenantOnboardingStatusDto
            {
                IsAuthenticated = true,
                IsCurrentUserTenantAdministrator = true,
                IsCurrentUserPlatformAdministrator = true,
                TenantId = Guid.NewGuid()
            });
        var cut = RenderLayout();
        cut.WaitForState(() => cut.Markup.Contains(
            "You do not have tenant administrator permissions",
            StringComparison.Ordinal));

        await Assert.That(cut.Markup).DoesNotContain("Event & Organization Policies", StringComparison.Ordinal);
        await _tenantOnboardingService.DidNotReceive().GetSettingsAsync();
    }

    [Test]
    public async Task TenantAdminSettingsLayout_PoliciesSection_DoesNotExposeBroadSave()
    {
        var tenantStatus = new TenantOnboardingStatusDto();
        tenantStatus.AdditionalProperties["_links"] = JsonSerializer.SerializeToElement(
            new Dictionary<string, object>
            {
                ["manage-tenant-settings"] = new { href = "/api/tenant-onboarding/policy-settings" }
            });
        _tenantOnboardingService.GetStatusAsync().Returns(tenantStatus);
        _tenantOnboardingService.GetManagementSettingsAsync().Returns(new TenantPolicySettingsDto());
        _onboardingService.GetStatusAsync().Returns(new InstanceOnboardingStatusDto
        {
            SelectedDeploymentMode = nameof(DeploymentMode.MultiTenant)
        });
        _tenantBrandingSettingsAdminService.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new TenantBrandingSettingsAdminModel());

        var cut = RenderLayout();
        cut.WaitForState(() => cut.Markup.Contains("Event & Organization Policies", StringComparison.Ordinal));

        await Assert.That(cut.Markup).DoesNotContain("Save Tenant Settings", StringComparison.Ordinal);
    }

    [Test]
    public async Task TenantAdminSettingsLayout_PaidEventsUsesAuthoritativeTenantId()
    {
        ConfigureAuthorizedManagement(CreateManagementModel());
        _paidEventPolicyService.GetTenantAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(
            new HalResourceOfTenantPaidEventPolicyConfigurationDto
            {
                ActiveInstanceCeiling = PaidPolicy(),
                EffectivePolicy = PaidPolicy(),
                Authority = new PaidEventPolicyAuthorityDto
                {
                    InstancePolicyVersion = 1,
                    EffectiveValuesInherited = true,
                    HasTenantNarrowing = false,
                    ManifestOwnedFields = ["allowedCurrencyCodes"],
                    SovereignLockedFields = ["providerCredentials", "saleControl"]
                }
            });
        var cut = RenderLayout();

        await NavigateTo(cut, "Paid Events");

        cut.WaitForElement("[data-testid='tenant-paid-policy-section']");
        await _paidEventPolicyService.Received(1).GetTenantAsync(_tenantId, Arg.Any<CancellationToken>());
        await Assert.That(cut.Markup).DoesNotContain("Save Tenant Settings", StringComparison.Ordinal);
    }

    [Test]
    public async Task TenantAdminSettingsLayout_BrandingAndStorage_DoNotExposeBroadSave()
    {
        var management = CreateManagementModel();
        management.CanOverrideStorage = true;
        ConfigureAuthorizedManagement(management);
        _tenantBrandingSettingsAdminService.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new TenantBrandingSettingsAdminModel
            {
                Exists = true,
                CanReplace = true,
                CanChangeDisplayName = true,
                ConcurrencyStamp = Guid.NewGuid()
            });
        _tenantStorageSettingsAdminService.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new HalResourceOfTenantStorageSettingsDto
            {
                TenantOverridesAllowed = true,
                TenantStorageLocked = false,
                IsReadOnly = false,
                _links = new Dictionary<string, HalLink>
                {
                    ["edit"] = new() { Href = "/api/tenant/settings/storage", Method = "PATCH" }
                }
            }.InitializeForEditing());
        var cut = RenderLayout();

        await NavigateTo(cut, "Branding");
        await Assert.That(cut.Markup).Contains("Brand display name", StringComparison.Ordinal);
        await Assert.That(cut.Markup).DoesNotContain("Save Tenant Settings", StringComparison.Ordinal);

        await NavigateTo(cut, "Object Storage");
        await Assert.That(cut.Markup).Contains("Tenant Storage Provider", StringComparison.Ordinal);
        await Assert.That(cut.Markup).DoesNotContain("Save Tenant Settings", StringComparison.Ordinal);
    }

    [Test]
    public async Task TenantAdminSettingsLayout_PublicExperience_KeepsExplicitSaveAffordance()
    {
        ConfigureAuthorizedManagement(CreateManagementModel());
        var cut = RenderLayout();

        await NavigateTo(cut, "Public Experience");

        await Assert.That(cut.Markup).Contains("Save Public Experience", StringComparison.Ordinal);
    }

    [Test]
    public async Task TenantAdminSettingsLayout_RemainingSparseSections_DoNotExposeBroadSave()
    {
        ConfigureAuthorizedManagement(CreateManagementModel());
        var cut = RenderLayout();

        foreach (string section in new[] { "Render Policy", "Domain", "MCP Adapter", "Community Guidelines" })
        {
            await NavigateTo(cut, section);
            await Assert.That(cut.Markup).DoesNotContain("Save Tenant Settings", StringComparison.Ordinal);
        }

    }

    [Test]
    public async Task TenantAdminSettingsLayout_WhenManagementSettingsLoadFails_RendersAlertWithoutWritableSurface()
    {
        ConfigureAuthorizedManagement(settings: null);

        var cut = RenderLayout();
        cut.WaitForState(() => cut.Markup.Contains("Tenant settings could not be loaded", StringComparison.Ordinal));

        await Assert.That(cut.FindAll("[role='alert']").Any(element =>
            element.TextContent.Contains("Tenant settings could not be loaded", StringComparison.Ordinal))).IsTrue();
        await Assert.That(cut.Markup).DoesNotContain("Event & Organization Policies", StringComparison.Ordinal);
        await Assert.That(cut.Markup).DoesNotContain("Save Tenant Settings", StringComparison.Ordinal);
    }

    private void ConfigureAuthorizedManagement(TenantPolicySettingsDto? settings)
    {
        var tenantStatus = new TenantOnboardingStatusDto { TenantId = _tenantId };
        tenantStatus.AdditionalProperties["_links"] = JsonSerializer.SerializeToElement(
            new Dictionary<string, object>
            {
                ["manage-tenant-settings"] = new { href = "/api/tenant-onboarding/policy-settings" }
            });
        _tenantOnboardingService.GetStatusAsync().Returns(tenantStatus);
        _tenantOnboardingService.GetManagementSettingsAsync().Returns(settings);
        _tenantOnboardingService.GetTenantSettingsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => CreatePolicyCategory(call.ArgAt<string>(0)));
        _onboardingService.GetStatusAsync().Returns(new InstanceOnboardingStatusDto
        {
            SelectedDeploymentMode = nameof(DeploymentMode.MultiTenant)
        });
        _tenantBrandingSettingsAdminService.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new TenantBrandingSettingsAdminModel());
    }

    private IRenderedComponent<TenantAdminSettingsLayout> RenderLayout() =>
        _ctx.RenderMudComponent<TenantAdminSettingsLayout>();

    private static TenantPolicySettingsDto CreateManagementModel() => new()
    {
        AllowUserSubmittedEvents = false,
        AllowOrganizationSubmittedEvents = false,
        AllowGroupSubmittedEvents = false,
        CanOverrideMcp = true
    };

    private static PaidEventPolicyDto PaidPolicy() => new()
    {
        IsPaymentsEnabled = true,
        AllowedOrganizerKindIds = [2],
        AllowedCurrencyCodes = ["EUR"],
        DefaultCurrencyCode = "EUR",
        RefundProtectionIds = [1, 2, 3, 4, 5, 6, 7],
        CurrencyRiskLimits =
        [
            new PaidEventPolicyCurrencyRiskLimitDto
            {
                CurrencyCode = "EUR",
                PerEventSalesCeilingMinor = 500_000,
                RollingOrganizerSalesCeilingMinor = 1_000_000,
                HighValueReviewThresholdMinor = 250_000
            }
        ]
    };

    private static Task NavigateTo(
        IRenderedComponent<TenantAdminSettingsLayout> cut,
        string section)
    {
        IRenderedComponent<MudListItem<string>> item =
            cut.FindComponents<MudListItem<string>>().Single(component =>
                string.Equals(
                    component.Instance.Text,
                    section,
                    StringComparison.OrdinalIgnoreCase));
        return cut.InvokeAsync(() =>
            item.Instance.OnClick.InvokeAsync(new MouseEventArgs()));
    }

    private static SettingGroupResponseDto CreatePolicyCategory(string category) => category switch
    {
        "Events" => new SettingGroupResponseDto
        {
            Category = category,
            Settings =
            [
                EditableBoolean("events.user_submission_enabled"),
                EditableBoolean("events.organization_submission_enabled"),
                EditableBoolean("events.group_submission_enabled"),
                EditableBoolean("events.require_approval"),
                EditableBoolean("events.card_click_opens_detail_page")
            ]
        },
        "Organizations" => new SettingGroupResponseDto
        {
            Category = category,
            Settings =
            [
                EditableBoolean("organizations.verification_required"),
                EditableBoolean("organizations.self_registration_enabled")
            ]
        },
        "Groups" => new SettingGroupResponseDto
        {
            Category = category,
            Settings = [EditableBoolean("groups.self_registration_enabled")]
        },
        _ => new SettingGroupResponseDto { Category = category }
    };

    private static EffectiveSettingDto EditableBoolean(string key) => new()
    {
        Key = key,
        Value = "false",
        CanEdit = true,
        Source = SettingSource.TenantOverride,
        IsLocked = false
    };
}

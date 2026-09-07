// ABOUTME: bUnit tests verifying lock toggle visibility in instance section components.
// ABOUTME: Ensures lock toggles are hidden in single-tenant mode and visible in multi-tenant mode.

using Explore.Blazor.Client.Models.Analytics;
using Explore.Blazor.Client.Pages.Admin.Instance.Components;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Admin;

public class InstanceSectionLockToggleTests : IDisposable
{
    private readonly BlazorTestContext _ctx;
    private readonly ITenantOnboardingService _tenantOnboardingService;

    public InstanceSectionLockToggleTests()
    {
        _ctx = new BlazorTestContext();
        _ctx.SetAuthenticatedUser(Guid.NewGuid(), "Instance Admin", "admin@example.com");
        _ctx.Services.AddSingleton(TimeProvider.System);
        _ctx.AddMockService<IEmailDeliveryAdminService>().GetInstanceAsync(Arg.Any<CancellationToken>())
            .Returns(new HalResourceOfInstanceSmtpSettingsDto { DeliveryEnabled = false });
        _ctx.AddMockService<IInstanceOnboardingService>();
        _tenantOnboardingService = _ctx.AddMockService<ITenantOnboardingService>();
        _ctx.AddMockService<ITenantOnboardingService>();
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task StorageSection_SingleTenant_NoLockToggle()
    {
        var cut = _ctx.RenderMudComponent<InstanceStorageSection>(parameters => parameters
            .Add(component => component.Model, new HalResourceOfInstanceStorageSettingsDto())
            .Add(component => component.IsSingleTenant, true)
            .Add(component => component.LockForTenants, false));

        await Assert.That(cut.Markup).DoesNotContain("Lock storage settings", StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public async Task SmtpSection_SingleTenant_NoLockToggle()
    {
        var cut = _ctx.RenderMudComponent<InstanceSmtpSection>(parameters => parameters
            .Add(component => component.Model, new HalResourceOfInstanceSmtpSettingsDto())
            .Add(component => component.IsSingleTenant, true)
            .Add(component => component.LockForTenants, false));

        await Assert.That(cut.Markup).DoesNotContain("Lock SMTP settings", StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public async Task AnalyticsSection_SingleTenant_NoLockToggle()
    {
        var cut = _ctx.RenderMudComponent<InstanceAnalyticsPrivacySection>(parameters => parameters
            .Add(component => component.Model, new AnalyticsGovernanceSettingsDto())
            .Add(component => component.IsSingleTenant, true)
            .Add(component => component.LockForTenants, false));

        await Assert.That(cut.Markup).DoesNotContain("Lock analytics settings", StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public async Task StorageSection_MultiTenant_HasLockToggle()
    {
        var cut = _ctx.RenderMudComponent<InstanceStorageSection>(parameters => parameters
            .Add(component => component.Model, new HalResourceOfInstanceStorageSettingsDto())
            .Add(component => component.IsSingleTenant, false)
            .Add(component => component.LockForTenants, false));

        await Assert.That(cut.Markup).Contains("Lock storage settings", StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public async Task StorageSection_LockToggle_SavesImmediatelyAndAnnouncesSuccess()
    {
        bool? captured = null;
        var model = new HalResourceOfInstanceStorageSettingsDto
        {
            _links = new Dictionary<string, HalLink> { ["edit"] = new() { Href = "/storage", Method = "PATCH" } }
        };
        var cut = _ctx.RenderMudComponent<InstanceStorageSection>(parameters => parameters
            .Add(component => component.Model, model)
            .Add(component => component.IsSingleTenant, false)
            .Add(component => component.LockForTenants, false)
            .Add(component => component.SaveLockAsync, value =>
            {
                captured = value;
                return Task.FromResult(true);
            }));

        await cut.InvokeAsync(() => cut.FindComponents<MudSwitch<bool>>().Last().Instance.ValueChanged.InvokeAsync(true));

        await Assert.That(captured).IsTrue();
        await Assert.That(cut.Find("[role='status']").TextContent).Contains("Storage tenant lock saved.");
    }

    [Test]
    public async Task SmtpSection_LockToggle_WhenSaveFails_AnnouncesAuthoritativeRestore()
    {
        var cut = _ctx.RenderMudComponent<InstanceSmtpSection>(parameters => parameters
            .Add(component => component.Model, new HalResourceOfInstanceSmtpSettingsDto())
            .Add(component => component.IsSingleTenant, false)
            .Add(component => component.LockForTenants, false)
            .Add(component => component.SaveLockAsync, _ => Task.FromResult(false)));

        await cut.InvokeAsync(() => cut.FindComponents<MudSwitch<bool>>().Last().Instance.ValueChanged.InvokeAsync(true));

        await Assert.That(cut.Find("[role='alert']").TextContent).Contains("latest value was restored");
    }

    [Test]
    public async Task ModulesSection_ToggleSendsOnePropertyAndAnnouncesSavedState()
    {
        ModuleSettingsDto? captured = null;
        var cut = _ctx.RenderMudComponent<InstanceModulesSection>(parameters => parameters
            .Add(component => component.Model, new ModuleSettingsDto())
            .Add(component => component.EventPolicy, new EventPolicyDto())
            .Add(component => component.OrganizationPolicy, new OrganizationPolicyDto())
            .Add(component => component.SaveModuleSettingsAsync, patch =>
            {
                captured = patch;
                return Task.FromResult(new BaseCommandResponseOfGuid { Success = true });
            })
            .Add(component => component.SaveEventPolicyAsync, _ =>
                Task.FromResult(new BaseCommandResponseOfGuid { Success = true }))
            .Add(component => component.SaveOrganizationPolicyAsync, _ =>
                Task.FromResult(new BaseCommandResponseOfGuid { Success = true })));

        await cut.InvokeAsync(() =>
            cut.FindComponents<MudSwitch<bool>>()[0].Instance.ValueChanged.InvokeAsync(true));

        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.EnableIslamicModule).IsTrue();
        await Assert.That(captured.EnableTechModule).IsNull();
        await Assert.That(cut.Markup).Contains("Module settings saved.", StringComparison.Ordinal);
    }

    [Test]
    public async Task BrandingSection_ToggleSendsOnePropertyAndAnnouncesSavedState()
    {
        BrandingSettingsDto? captured = null;
        var cut = _ctx.RenderMudComponent<InstanceBrandingSection>(parameters => parameters
            .Add(component => component.Model, new BrandingSettingsDto())
            .Add(component => component.IsSingleTenant, false)
            .Add(component => component.SaveBrandingAsync, patch =>
            {
                captured = patch;
                return Task.FromResult(new BaseCommandResponseOfGuid { Success = true });
            }));

        await cut.InvokeAsync(() =>
            cut.FindComponents<MudSwitch<bool>>()[0].Instance.ValueChanged.InvokeAsync(true));

        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.LockTenantBrandDisplayName).IsTrue();
        await Assert.That(captured.LockTenantBrandLogoUrl).IsNull();
        await Assert.That(cut.Markup).Contains("Branding settings saved.", StringComparison.Ordinal);
    }

    [Test]
    public async Task DomainSection_ToggleSendsOnePropertyAndAnnouncesSavedState()
    {
        DomainSettingsDto? captured = null;
        var cut = _ctx.RenderMudComponent<InstanceDomainSection>(parameters => parameters
            .Add(component => component.Model, new DomainSettingsDto())
            .Add(component => component.SaveDomainAsync, patch =>
            {
                captured = patch;
                return Task.FromResult(new BaseCommandResponseOfGuid { Success = true });
            }));

        await cut.InvokeAsync(() =>
            cut.FindComponents<MudSwitch<bool>>()[0].Instance.ValueChanged.InvokeAsync(true));

        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.AllowTenantCustomDomains).IsTrue();
        await Assert.That(captured.LockTenantSubdomain).IsNull();
        await Assert.That(cut.Markup).Contains("Domain settings saved.", StringComparison.Ordinal);
    }

    [Test]
    public async Task AnalyticsSection_ToggleSendsOnePropertyAndAnnouncesSavedState()
    {
        AnalyticsGovernanceSettingsDto? captured = null;
        var cut = _ctx.RenderMudComponent<InstanceAnalyticsPrivacySection>(parameters => parameters
            .Add(component => component.Model, new AnalyticsGovernanceSettingsDto())
            .Add(component => component.IsSingleTenant, true)
            .Add(component => component.SaveAnalyticsAsync, patch =>
            {
                captured = patch;
                return Task.FromResult(new BaseCommandResponseOfGuid { Success = true });
            })
            .Add(component => component.SaveDelegationAsync, _ =>
                Task.FromResult(new BaseCommandResponseOfGuid { Success = true })));

        await cut.InvokeAsync(() =>
            cut.FindComponents<MudSwitch<bool>>()[0].Instance.ValueChanged.InvokeAsync(true));

        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.GlobalDisableClientTracking).IsTrue();
        await Assert.That(captured.CookieConsentEnabled).IsNull();
        await Assert.That(cut.Markup).Contains("Analytics settings saved.", StringComparison.Ordinal);
    }

    [Test]
    public async Task AiSection_ToggleSendsOnePropertyAndAnnouncesSavedState()
    {
        AiAssistantGovernanceSettingsDto? captured = null;
        var cut = _ctx.RenderMudComponent<InstanceAiSection>(parameters => parameters
            .Add(component => component.AiAssistant, new AiAssistantGovernanceSettingsDto())
            .Add(component => component.SaveAiAssistantAsync, patch =>
            {
                captured = patch;
                return Task.FromResult(new BaseCommandResponseOfGuid { Success = true });
            })
            .Add(component => component.SaveAiProviderConfigurationAsync, _ =>
                Task.FromResult(new BaseCommandResponseOfGuid { Success = true })));

        await cut.InvokeAsync(() =>
            cut.FindComponents<MudSwitch<bool>>()[0].Instance.ValueChanged.InvokeAsync(true));

        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.Enabled).IsTrue();
        await Assert.That(captured.Provider).IsNull();
        await Assert.That(cut.Find("[role='status']").TextContent).Contains("AI Assistant setting saved.", StringComparison.Ordinal);
    }

    [Test]
    public async Task AiSection_ProviderFieldsRemainExplicitAndSaveAsOneCoupledPatch()
    {
        AiAssistantProviderConfigurationWriteDto? captured = null;
        var model = new AiAssistantGovernanceSettingsDto
        {
            Enabled = true,
            Provider = "openai-compatible",
            EndpointUrl = "https://ai.example.test/v1",
            ModelId = "model-a",
            AllowedModelIds = ["model-a", "model-b"]
        };
        var cut = _ctx.RenderMudComponent<InstanceAiSection>(parameters => parameters
            .Add(component => component.AiAssistant, model)
            .Add(component => component.SaveAiAssistantAsync, _ =>
                Task.FromResult(new BaseCommandResponseOfGuid { Success = true }))
            .Add(component => component.SaveAiProviderConfigurationAsync, patch =>
            {
                captured = patch;
                return Task.FromResult(new BaseCommandResponseOfGuid { Success = true });
            }));

        await Assert.That(captured).IsNull();
        var apiKeyField = cut.FindComponents<MudTextField<string>>().Single(field =>
            field.Instance.Label?.Contains("API key", StringComparison.Ordinal) == true);
        await cut.InvokeAsync(() => apiKeyField.Instance.ValueChanged.InvokeAsync("replacement-key"));
        cut.FindAll("button").Single(button =>
            button.TextContent.Contains("Save provider configuration", StringComparison.Ordinal)).Click();

        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.Provider).IsEqualTo("openai-compatible");
        await Assert.That(captured.EndpointUrl).IsEqualTo("https://ai.example.test/v1");
        await Assert.That(captured.ApiKey).IsEqualTo("replacement-key");
        await Assert.That(captured.ModelId).IsEqualTo("model-a");
        await Assert.That(captured.AllowedModelIds).IsEquivalentTo(["model-a", "model-b"]);
        await Assert.That(cut.Find("[role='status']").TextContent).Contains("AI provider configuration saved.", StringComparison.Ordinal);
    }

    [Test]
    public async Task BrandingSection_TextSavesOnBlurAsOneProperty()
    {
        BrandingSettingsDto? captured = null;
        var cut = _ctx.RenderMudComponent<InstanceBrandingSection>(parameters => parameters
            .Add(component => component.Model, new BrandingSettingsDto())
            .Add(component => component.IsSingleTenant, true)
            .Add(component => component.SaveBrandingAsync, patch =>
            {
                captured = patch;
                return Task.FromResult(new BaseCommandResponseOfGuid { Success = true });
            }));
        var field = cut.FindComponents<MudTextField<string>>()
            .Single(component => component.Instance.Label == "Brand Display Name");

        await cut.InvokeAsync(() => field.Instance.ValueChanged.InvokeAsync("Authoritative Brand"));
        await Assert.That(captured).IsNull();
        field.Find("input").Blur();

        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.DefaultBrandDisplayName).IsEqualTo("Authoritative Brand");
        await Assert.That(captured.DefaultBrandLogoUrl).IsNull();
    }

    [Test]
    public async Task FooterGovernanceSection_ToggleSendsOnePropertyAndAnnouncesSavedState()
    {
        FooterGovernanceSettingsDto? captured = null;
        var cut = _ctx.RenderMudComponent<InstanceFooterGovernanceSection>(parameters => parameters
            .Add(component => component.Model, new FooterGovernanceSettingsDto())
            .Add(component => component.SaveFooterGovernanceAsync, patch =>
            {
                captured = patch;
                return Task.FromResult(new BaseCommandResponseOfGuid { Success = true });
            }));

        await cut.InvokeAsync(() =>
            cut.FindComponents<MudSwitch<bool>>()[0].Instance.ValueChanged.InvokeAsync(true));

        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.LockTenantTemplate).IsTrue();
        await Assert.That(captured.LockTenantLinkGroups).IsNull();
        await Assert.That(cut.Find("[role='status']").TextContent).Contains("Footer governance saved.", StringComparison.Ordinal);
    }

    [Test]
    public async Task AiSection_EndpointChangeIgnoresStaleModelDiscoveryResponse()
    {
        var response = new TaskCompletionSource<IReadOnlyList<AiAssistantModelDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _tenantOnboardingService.GetAiModelsAsync("https://first.example.test/v1", string.Empty)
            .Returns(response.Task);
        var model = new AiAssistantGovernanceSettingsDto
        {
            Enabled = true,
            EndpointUrl = "https://first.example.test/v1"
        };
        var cut = _ctx.RenderMudComponent<InstanceAiSection>(parameters => parameters
            .Add(component => component.AiAssistant, model)
            .Add(component => component.SaveAiAssistantAsync, _ =>
                Task.FromResult(new BaseCommandResponseOfGuid { Success = true })));
        cut.FindAll("button").Single(button =>
            button.TextContent.Contains("Load models", StringComparison.Ordinal)).Click();
        var endpointField = cut.FindComponents<MudTextField<string>>()
            .Single(field => field.Instance.Label == "Endpoint URL");

        await cut.InvokeAsync(() => endpointField.Instance.ValueChanged.InvokeAsync("https://second.example.test/v1"));
        response.SetResult([new AiAssistantModelDto { Id = "stale-model" }]);
        cut.WaitForAssertion(() =>
        {
            var loadButton = cut.FindAll("button").Single(button =>
                button.TextContent.Contains("Load models", StringComparison.Ordinal));
            if (loadButton.HasAttribute("disabled")
                || cut.Markup.Contains("stale-model", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The stale model-discovery request has not settled safely.");
            }
        });

        await Assert.That(model.EndpointUrl).IsEqualTo("https://second.example.test/v1");
        await Assert.That(string.IsNullOrEmpty(model.ModelId)).IsTrue();
        await Assert.That(cut.Markup).DoesNotContain("stale-model", StringComparison.Ordinal);
        await Assert.That(cut.FindAll("button").Single(button =>
            button.TextContent.Contains("Load models", StringComparison.Ordinal)).HasAttribute("disabled")).IsFalse();
    }

}

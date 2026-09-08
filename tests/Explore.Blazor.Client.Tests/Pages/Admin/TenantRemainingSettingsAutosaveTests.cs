using Explore.Blazor.Client.Pages.Admin.Tenant.Components;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Admin;

public sealed class TenantRemainingSettingsAutosaveTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly ITenantOnboardingService _tenantOnboardingService;

    public TenantRemainingSettingsAutosaveTests()
    {
        _tenantOnboardingService = _ctx.AddMockService<ITenantOnboardingService>();
        _tenantOnboardingService.UpdateTenantSettingAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true });
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task RenderPolicyPreset_WhenChanged_WritesOnlyExactKey()
    {
        var model = EditableRenderPolicy();
        IRenderedComponent<TenantRenderPolicySection> cut = RenderPolicy(model);
        MudSelect<string> preset = cut.FindComponents<MudSelect<string>>()
            .Single(component => component.Instance.Label == "Render Policy Preset")
            .Instance;

        await cut.InvokeAsync(() => preset.ValueChanged.InvokeAsync("SeoBalanced"));

        await _tenantOnboardingService.Received(1).UpdateTenantSettingAsync(
            "routing.render_policy.preset",
            "SeoBalanced",
            Arg.Any<CancellationToken>());
        await Assert.That(model.RenderPolicyPreset).IsEqualTo("SeoBalanced");
        await Assert.That(cut.Find("[role='status']").TextContent).Contains("Render policy preset saved.");
    }

    [Test]
    public async Task RenderPolicyPreset_WhilePending_DisablesControlsAndSuppressesRepeat()
    {
        var pending = new TaskCompletionSource<BaseCommandResponseOfGuid>();
        _tenantOnboardingService.UpdateTenantSettingAsync(
                "routing.render_policy.preset",
                "SeoBalanced",
                Arg.Any<CancellationToken>())
            .Returns(pending.Task);
        var model = EditableRenderPolicy();
        IRenderedComponent<TenantRenderPolicySection> cut = RenderPolicy(model);
        MudSelect<string> preset = cut.FindComponents<MudSelect<string>>()
            .Single(component => component.Instance.Label == "Render Policy Preset")
            .Instance;

        Task firstWrite = cut.InvokeAsync(() => preset.ValueChanged.InvokeAsync("SeoBalanced"));
        cut.WaitForState(() => cut.FindComponents<MudSelect<string>>().All(component => component.Instance.Disabled));
        await cut.InvokeAsync(() => preset.ValueChanged.InvokeAsync("AllPrerendered"));

        await _tenantOnboardingService.Received(1).UpdateTenantSettingAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        pending.SetResult(new BaseCommandResponseOfGuid { Success = true });
        await firstWrite;
    }

    [Test]
    public async Task DomainSubdomain_WhenBlurSaveFails_RestoresAuthoritativeValue()
    {
        var model = EditableDomain(subdomain: "draft");
        _tenantOnboardingService.UpdateTenantSettingAsync(
                "domains.tenant_subdomain",
                "changed",
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = false });
        _tenantOnboardingService.GetManagementSettingsAsync()
            .Returns(EditableDomain(subdomain: "authoritative"));
        IRenderedComponent<TenantDomainSection> cut = RenderDomain(model);
        IRenderedComponent<MudTextField<string>> field = cut.FindComponents<MudTextField<string>>()
            .Single(component => component.Instance.Label == "Subdomain");

        await cut.InvokeAsync(() => field.Instance.ValueChanged.InvokeAsync("changed"));
        await Assert.That(model.Subdomain).IsEqualTo("changed");
        field.Find("input").Blur();
        cut.WaitForState(() => model.Subdomain == "authoritative");

        await _tenantOnboardingService.Received(1).UpdateTenantSettingAsync(
            "domains.tenant_subdomain",
            "changed",
            Arg.Any<CancellationToken>());
        await Assert.That(cut.Find("[role='alert']").TextContent).Contains("latest value was restored");
    }

    [Test]
    public async Task DomainControls_WhenServerLocksOverrides_AreDisabled()
    {
        var model = EditableDomain(subdomain: "tenant");
        model.CanOverrideHomePagePreference = false;
        model.CanOverrideSubdomain = false;
        model.CanOverrideCustomDomain = false;

        IRenderedComponent<TenantDomainSection> cut = RenderDomain(model);

        await Assert.That(cut.FindComponents<MudSelect<string>>().Single().Instance.Disabled).IsTrue();
        await Assert.That(cut.FindComponents<MudTextField<string>>().All(component => component.Instance.Disabled)).IsTrue();
        await _tenantOnboardingService.DidNotReceive().UpdateTenantSettingAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    private IRenderedComponent<TenantRenderPolicySection> RenderPolicy(TenantPolicySettingsDto model) =>
        _ctx.RenderMudComponent<TenantRenderPolicySection>(parameters =>
            parameters.Add(component => component.Model, model));

    private IRenderedComponent<TenantDomainSection> RenderDomain(TenantPolicySettingsDto model) =>
        _ctx.RenderMudComponent<TenantDomainSection>(parameters =>
            parameters.Add(component => component.Model, model));

    private static TenantPolicySettingsDto EditableRenderPolicy() => new()
    {
        RenderPolicyPreset = "AllInteractiveServer",
        CanOverrideRenderPolicy = true,
        CanOverridePublicSeoRenderPolicy = true,
        CanOverrideOperationalRenderPolicy = true,
        CanOverrideAdminRenderPolicy = true
    };

    private static TenantPolicySettingsDto EditableDomain(string subdomain) => new()
    {
        PreferredHomePage = "EventList",
        Subdomain = subdomain,
        CustomDomain = "tenant.example.test",
        InstanceBaseDomain = "example.test",
        CanOverrideHomePagePreference = true,
        CanOverrideSubdomain = true,
        CanOverrideCustomDomain = true
    };

}

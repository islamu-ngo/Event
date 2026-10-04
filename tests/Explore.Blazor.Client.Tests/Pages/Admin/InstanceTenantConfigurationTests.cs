using Explore.Blazor.Client.Contracts.InstanceAdmin;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Contracts.Services.InstanceAdmin;
using Explore.Blazor.Client.Pages.Admin.Instance;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Explore.Blazor.Client.Tests.Pages.Admin;

public sealed class InstanceTenantConfigurationTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly IInstanceTenantConfigurationService _configurationService =
        Substitute.For<IInstanceTenantConfigurationService>();
    private readonly IInstancePlanCatalogService _planCatalog =
        Substitute.For<IInstancePlanCatalogService>();
    private readonly IAccessibilityFocusService _focusService = Substitute.For<IAccessibilityFocusService>();
    private readonly IAccessibilityAnnouncerService _announcer = Substitute.For<IAccessibilityAnnouncerService>();

    public InstanceTenantConfigurationTests()
    {
        _ctx.Services.RemoveAll<IInstanceTenantConfigurationService>();
        _ctx.Services.RemoveAll<IInstancePlanCatalogService>();
        _ctx.Services.RemoveAll<IAccessibilityFocusService>();
        _ctx.Services.RemoveAll<IAccessibilityAnnouncerService>();
        _ctx.Services.AddSingleton(_configurationService);
        _ctx.Services.AddSingleton(_planCatalog);
        _ctx.Services.AddSingleton(_focusService);
        _ctx.Services.AddSingleton(_announcer);
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task Page_WithoutSwitchPlanLink_DoesNotLoadPlanCatalog()
    {
        ReturnConfiguration();

        var cut = RenderPage();

        cut.WaitForAssertion(() => cut.Find("h1").TextContent.Equals("Tenant configuration", StringComparison.Ordinal));
        await Assert.That(_ctx.Services.GetRequiredService<IInstancePlanCatalogService>())
            .IsSameReferenceAs(_planCatalog);
        await _planCatalog.DidNotReceive().GetPlansAsync(Arg.Any<CancellationToken>());
        await _configurationService.Received(1).GetEffectiveConfigurationAsync(_tenantId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task LoadingThenEmpty_RendersAccessibleStates()
    {
        var pending = new TaskCompletionSource<HalResourceOfInstanceTenantEffectiveConfigurationDto>();
        _configurationService.GetEffectiveConfigurationAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(pending.Task);

        var cut = RenderPage();

        await Assert.That(cut.Find("[role='status']").TextContent).Contains("Loading tenant configuration");
        pending.SetResult(Configuration());
        cut.WaitForAssertion(() => cut.Markup.Contains("No effective settings", StringComparison.Ordinal));
    }

    [Test]
    public async Task ThrownLoad_RendersSafeErrorWithoutRawException()
    {
        _configurationService.GetEffectiveConfigurationAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns<Task<HalResourceOfInstanceTenantEffectiveConfigurationDto>>(_ =>
                throw new InvalidOperationException("raw provider credential"));

        var cut = RenderPage();

        cut.WaitForAssertion(() => cut.Find("[role='alert']"));
        await Assert.That(cut.Markup).Contains("Tenant configuration is currently unavailable.");
        await Assert.That(cut.Markup).DoesNotContain("raw provider credential");
    }

    [Test]
    public async Task SensitiveSetting_MasksValueAndSuppressesActions()
    {
        ReturnConfiguration(Setting(
            "smtp.password",
            "raw-secret-value",
            isSensitive: true,
            links: [InstanceAdminLinkRelations.Override, InstanceAdminLinkRelations.Lock]));

        var cut = RenderPage();

        cut.WaitForAssertion(() => cut.Markup.Contains("smtp.password", StringComparison.Ordinal));
        await Assert.That(cut.Markup).Contains("••••••••");
        await Assert.That(cut.Markup).DoesNotContain("raw-secret-value");
        await Assert.That(cut.Markup).DoesNotContain("Override smtp.password");
        await Assert.That(cut.Markup).DoesNotContain("Lock smtp.password");
    }

    [Test]
    public async Task PerSettingLinks_RenderOnlyTheirOwnActions()
    {
        ReturnConfiguration(
            Setting("feature.override", "off", links: InstanceAdminLinkRelations.Override),
            Setting("feature.lock", "on", links: InstanceAdminLinkRelations.Lock),
            Setting("feature.unlock", "on", isLocked: true, links: InstanceAdminLinkRelations.Unlock),
            Setting("feature.readonly", "on"));

        var cut = RenderPage();

        cut.WaitForAssertion(() => cut.Find("button[aria-label='Override feature.override']"));
        await Assert.That(cut.FindAll("button[aria-label^='Override ']").Count).IsEqualTo(1);
        await Assert.That(cut.Find("button[aria-label='Lock feature.lock']")).IsNotNull();
        await Assert.That(cut.FindAll("button[aria-label^='Lock ']").Count).IsEqualTo(1);
        await Assert.That(cut.Find("button[aria-label='Unlock feature.unlock']")).IsNotNull();
        await Assert.That(cut.FindAll("button[aria-label^='Unlock ']").Count).IsEqualTo(1);
        await Assert.That(cut.Markup).DoesNotContain("feature.readonly']");
    }

    [Test]
    public async Task EffectiveConfiguration_RendersLockSourceAndReadOnlyPlanAssignment()
    {
        var assignment = new InstanceTenantPlanAssignmentDto
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            PlanId = Guid.NewGuid(),
            PlanKey = "community",
            PlanVersionId = Guid.NewGuid(),
            VersionNumber = 3,
            StatusId = 1,
            StatusCode = "Active",
            AssignedAt = DateTimeOffset.Parse("2026-07-01T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            AssignedByUserId = Guid.NewGuid()
        };
        _configurationService.GetEffectiveConfigurationAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new HalResourceOfInstanceTenantEffectiveConfigurationDto
            {
                TenantId = _tenantId,
                PlanAssignment = assignment,
                Settings = [Setting("feature.locked", "on", isLocked: true)],
                Quotas = []
            });

        var cut = RenderPage();

        cut.WaitForAssertion(() => cut.Find("section[aria-labelledby='tenant-plan-assignment-title']"));
        await Assert.That(cut.Markup).Contains("community");
        await Assert.That(cut.Markup).Contains("v3");
        await Assert.That(cut.Markup).Contains("Active");
        await Assert.That(cut.Find("[data-setting-key='feature.locked'] dd[data-lock-source]").TextContent)
            .IsEqualTo("Tenant");
        await Assert.That(_ctx.Services.GetRequiredService<IInstancePlanCatalogService>())
            .IsSameReferenceAs(_planCatalog);
        await _planCatalog.DidNotReceive().GetPlansAsync(Arg.Any<CancellationToken>());
        await Assert.That(cut.Markup).DoesNotContain("Apply assignment");
        await Assert.That(cut.Markup).DoesNotContain("Rollback assignment");
        await Assert.That(cut.Find(".instance-tenant-config__header code").GetAttribute("dir")).IsEqualTo("ltr");
        foreach (var fact in cut.FindAll(".instance-tenant-config__assignment-facts dd"))
        {
            await Assert.That(fact.GetAttribute("dir")).IsEqualTo("ltr");
        }

        var setting = cut.Find("[data-setting-key='feature.locked']");
        await Assert.That(setting.QuerySelector("h3 code")?.GetAttribute("dir")).IsEqualTo("ltr");
        await Assert.That(setting.QuerySelector("dd[data-lock-source]")?.GetAttribute("dir")).IsEqualTo("ltr");
    }

    [Test]
    public async Task OverrideSuccess_SubmitsTrimmedValueReloadsRestoresFocusAndAnnounces()
    {
        ReturnConfiguration(Setting("ai.max_daily_messages", "500", links: InstanceAdminLinkRelations.Override));
        _configurationService.SetSettingAsync(
                _tenantId,
                "ai.max_daily_messages",
                "1000",
                Arg.Any<CancellationToken>())
            .Returns(CommandResult(true, "Setting overridden."));
        var cut = RenderPage();
        cut.WaitForAssertion(() => cut.Find("button[aria-label='Override ai.max_daily_messages']"));

        cut.Find("button[aria-label='Override ai.max_daily_messages']").Click();
        await _focusService.Received(1).SaveFocusAsync();
        cut.WaitForAssertion(() => _focusService.Received(1).FocusByIdAsync("instance-tenant-config-edit-value"));
        cut.Find("input[aria-label='Edit value for ai.max_daily_messages']").Change("  1000  ");
        cut.Find("button[aria-label='Save override for ai.max_daily_messages']").Click();

        cut.WaitForAssertion(() => cut.Find("[role='status'][aria-live='polite']").TextContent.Contains("Setting overridden.", StringComparison.Ordinal));
        await _configurationService.Received(1).SetSettingAsync(
            _tenantId,
            "ai.max_daily_messages",
            "1000",
            Arg.Any<CancellationToken>());
        await _configurationService.Received(2).GetEffectiveConfigurationAsync(_tenantId, Arg.Any<CancellationToken>());
        await _focusService.Received(1).RestoreFocusAsync("#instance-tenant-config-actions-ai-max_daily_messages");
        await _announcer.Received(1).AnnouncePoliteAsync("Setting overridden.");
    }

    [Test]
    public async Task StringOverride_DisplaysAndSavesPlainValueWithoutStorageQuotes()
    {
        const string key = "email.smtp_host";
        const string value = "smtp.example.test";
        ReturnConfiguration(Setting(key, value, links: InstanceAdminLinkRelations.Override));
        _configurationService.SetSettingAsync(_tenantId, key, value, Arg.Any<CancellationToken>())
            .Returns(CommandResult(true, "Setting overridden."));
        var cut = RenderPage();
        cut.WaitForAssertion(() => cut.Find($"button[aria-label='Override {key}']"));

        await Assert.That(cut.Find($"[data-setting-key='{key}'] dd").TextContent)
            .IsEqualTo(value);
        cut.Find($"button[aria-label='Override {key}']").Click();
        await Assert.That(cut.Find($"input[aria-label='Edit value for {key}']").GetAttribute("value"))
            .IsEqualTo(value);
        cut.Find($"button[aria-label='Save override for {key}']").Click();

        cut.WaitForAssertion(() => cut.Find("[role='status']").TextContent.Contains("Setting overridden.", StringComparison.Ordinal));
        await _configurationService.Received(1).SetSettingAsync(
            _tenantId,
            key,
            value,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task OverrideFailure_KeepsEditorDoesNotReloadAndAnnouncesAssertively()
    {
        ReturnConfiguration(Setting("ai.max_daily_messages", "500", links: InstanceAdminLinkRelations.Override));
        _configurationService.SetSettingAsync(
                _tenantId,
                "ai.max_daily_messages",
                "invalid",
                Arg.Any<CancellationToken>())
            .Returns(CommandResult(false, "The setting value is invalid.", "validation_failed"));
        var cut = RenderPage();
        cut.WaitForAssertion(() => cut.Find("button[aria-label='Override ai.max_daily_messages']"));

        cut.Find("button[aria-label='Override ai.max_daily_messages']").Click();
        cut.Find("input[aria-label='Edit value for ai.max_daily_messages']").Change("invalid");
        cut.Find("button[aria-label='Save override for ai.max_daily_messages']").Click();

        cut.WaitForAssertion(() => cut.Find("[role='alert'][aria-live='assertive']").TextContent.Contains("invalid", StringComparison.Ordinal));
        await _configurationService.Received(1).GetEffectiveConfigurationAsync(_tenantId, Arg.Any<CancellationToken>());
        await Assert.That(cut.Find("input[aria-label='Edit value for ai.max_daily_messages']")).IsNotNull();
        await _focusService.DidNotReceive().RestoreFocusAsync(Arg.Any<string?>());
        await _announcer.Received(1).AnnounceAssertiveAsync("The setting value is invalid.");
    }

    [Test]
    public async Task LockSuccess_CallsServiceReloadsAndRestoresFocus()
    {
        ReturnConfiguration(Setting("feature.lock", "on", links: InstanceAdminLinkRelations.Lock));
        _configurationService.LockSettingAsync(_tenantId, "feature.lock", Arg.Any<CancellationToken>())
            .Returns(CommandResult(true, "Setting locked."));
        var cut = RenderPage();
        cut.WaitForAssertion(() => cut.Find("button[aria-label='Lock feature.lock']"));

        cut.Find("button[aria-label='Lock feature.lock']").Click();

        cut.WaitForAssertion(() => cut.Find("[role='status']").TextContent.Contains("Setting locked.", StringComparison.Ordinal));
        await _configurationService.Received(1).LockSettingAsync(_tenantId, "feature.lock", Arg.Any<CancellationToken>());
        await _configurationService.Received(2).GetEffectiveConfigurationAsync(_tenantId, Arg.Any<CancellationToken>());
        await _focusService.Received(1).SaveFocusAsync();
        await _focusService.Received(1).RestoreFocusAsync("#instance-tenant-config-actions-feature-lock");
        await _announcer.Received(1).AnnouncePoliteAsync("Setting locked.");
        await Assert.That(cut.Find("[role='status'][aria-live='polite']").ParentElement?.ClassList.Contains("instance-tenant-config"))
            .IsTrue();
    }

    [Test]
    public async Task MixedDirectionContent_IsolatesDescriptionsTechnicalFactsAndQuotaValues()
    {
        var setting = Setting("email.smtp_host", "smtp.example.test") with
        {
            Description = "خادم SMTP الأساسي."
        };
        _configurationService.GetEffectiveConfigurationAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new HalResourceOfInstanceTenantEffectiveConfigurationDto
            {
                TenantId = _tenantId,
                Settings = [setting],
                Quotas =
                [
                    new InstanceTenantQuotaUsageDto
                    {
                        Key = "storage.bytes",
                        Limit = 10_000,
                        Used = 7_500,
                        Reserved = 0,
                        Quarantined = 0,
                        Available = 2_500,
                        ObjectCount = 12,
                        Provider = "s3",
                        Source = "TenantPlan"
                    }
                ]
            });

        var cut = RenderPage();
        cut.WaitForAssertion(() => cut.Find("[data-setting-key='email.smtp_host']"));

        var row = cut.Find("[data-setting-key='email.smtp_host']");
        await Assert.That(row.QuerySelector("h3 code")?.GetAttribute("dir")).IsEqualTo("ltr");
        await Assert.That(row.QuerySelector("p")?.GetAttribute("dir")).IsEqualTo("auto");
        await Assert.That(row.QuerySelector("dd span")?.GetAttribute("dir")).IsEqualTo("auto");
        await Assert.That(row.QuerySelector("dd[data-value-source]")?.GetAttribute("dir")).IsEqualTo("ltr");
        await Assert.That(cut.Find(".instance-tenant-config__table-wrap").GetAttribute("dir")).IsEqualTo("ltr");
    }

    [Test]
    public async Task UnlockFailure_CallsServiceWithoutReloadAndAnnouncesAssertively()
    {
        ReturnConfiguration(Setting("feature.unlock", "on", isLocked: true, links: InstanceAdminLinkRelations.Unlock));
        _configurationService.UnlockSettingAsync(_tenantId, "feature.unlock", Arg.Any<CancellationToken>())
            .Returns(CommandResult(false, "Setting cannot be unlocked.", "conflict"));
        var cut = RenderPage();
        cut.WaitForAssertion(() => cut.Find("button[aria-label='Unlock feature.unlock']"));

        cut.Find("button[aria-label='Unlock feature.unlock']").Click();

        cut.WaitForAssertion(() => cut.Find("[role='alert']").TextContent.Contains("cannot be unlocked", StringComparison.Ordinal));
        await _configurationService.Received(1).UnlockSettingAsync(_tenantId, "feature.unlock", Arg.Any<CancellationToken>());
        await _configurationService.Received(1).GetEffectiveConfigurationAsync(_tenantId, Arg.Any<CancellationToken>());
        await _focusService.DidNotReceive().RestoreFocusAsync(Arg.Any<string?>());
        await _announcer.Received(1).AnnounceAssertiveAsync("Setting cannot be unlocked.");
    }

    private IRenderedComponent<InstanceTenantConfiguration> RenderPage() =>
        _ctx.RenderMudComponent<InstanceTenantConfiguration>(parameters => parameters.Add(p => p.TenantId, _tenantId));

    private void ReturnConfiguration(params InstanceTenantEffectiveSettingDto[] settings) =>
        _configurationService.GetEffectiveConfigurationAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(Configuration(settings));

    private HalResourceOfInstanceTenantEffectiveConfigurationDto Configuration(params InstanceTenantEffectiveSettingDto[] settings) =>
        new()
        {
            TenantId = _tenantId,
            Settings = settings,
            Quotas = []
        };

    private static InstanceTenantEffectiveSettingDto Setting(
        string key,
        string value,
        bool isLocked = false,
        bool isSensitive = false,
        params string[] links) => new()
        {
            Key = key,
            Category = "General",
            Value = value,
            SettingValueTypeId = 1,
            SettingValueTypeCode = "String",
            SettingValueTypeName = "String",
            ValueSource = isLocked ? "TenantLocked" : "TenantOverride",
            IsLocked = isLocked,
            LockSource = isLocked ? "Tenant" : null,
            IsSensitive = isSensitive,
            AllowedValues = [],
            _links = Links(links)
        };

    private static BaseCommandResponseOfGuid CommandResult(bool success, string message, string? failureCode = null) => new()
    {
        Success = success,
        Message = message,
        FailureCode = failureCode
    };

    private static Dictionary<string, HalLink> Links(params string[] relations) =>
        relations.ToDictionary(
            relation => relation,
            relation => new HalLink
            {
                Href = $"/api/admin/instance/tenants/settings/{relation}",
                Method = "POST"
            },
            StringComparer.OrdinalIgnoreCase);
}

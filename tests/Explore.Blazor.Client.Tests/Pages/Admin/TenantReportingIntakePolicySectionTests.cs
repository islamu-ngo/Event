using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Contracts.Services.Reporting;
using Explore.Blazor.Client.Pages.Admin.Tenant.Components;
using AngleSharp.Html.Dom;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Explore.Blazor.Client.Tests.Pages.Admin;

public sealed class TenantReportingIntakePolicySectionTests : IDisposable
{
    private const string ToggleLabel = "Accept reports about published content";

    private readonly BlazorTestContext _ctx = new();
    private readonly ITenantReportingIntakePolicyService _policyService =
        Substitute.For<ITenantReportingIntakePolicyService>();
    private readonly IAccessibilityAnnouncerService _announcer =
        Substitute.For<IAccessibilityAnnouncerService>();
    private readonly ITranslationService _translations =
        Substitute.For<ITranslationService>();

    public TenantReportingIntakePolicySectionTests()
    {
        _ctx.Services.AddSingleton(_policyService);
        _ctx.Services.RemoveAll<IAccessibilityAnnouncerService>();
        _ctx.Services.AddSingleton(_announcer);
        _ctx.Services.RemoveAll<ITranslationService>();
        _ctx.Services.AddSingleton(_translations);
        _announcer.AnnouncePoliteAsync(Arg.Any<string>()).Returns(Task.CompletedTask);
        _announcer.AnnounceAssertiveAsync(Arg.Any<string>()).Returns(Task.CompletedTask);
        _translations.T(Arg.Any<string>(), Arg.Any<string?>())
            .Returns(call => call.ArgAt<string?>(1) ?? call.ArgAt<string>(0));
    }

    [Test]
    public async Task EnabledPolicy_WhenServerAllowsDisable_RendersEditableCheckedSwitch()
    {
        _policyService.GetAsync(Arg.Any<CancellationToken>())
            .Returns(CreatePolicy(enabled: true, canDisable: true, canEdit: true));

        var cut = _ctx.Render<TenantReportingIntakePolicySection>();

        cut.WaitForState(() => Toggle(cut).HasAttribute("disabled") is false);
        await Assert.That(Toggle(cut).IsChecked).IsTrue();
        await Assert.That(Toggle(cut).GetAttribute("aria-describedby"))
            .IsEqualTo("reporting-intake-distinction reporting-intake-contacts");
        await Assert.That(cut.Markup).Contains("Tenant override");
        await Assert.That(cut.Markup).Contains("External reporting providers are configured separately");
        await Assert.That(cut.Markup).Contains("Correction, legal, and copyright requests remain available");
        _translations.Received().T("tenant.reportingIntake.heading", "Reporting intake");
    }

    [Test]
    public async Task EnabledPolicy_WhenPublicationSafetyBlocksDisable_RendersServerReasonAndDisablesSwitch()
    {
        const string reason = "Published content still requires a reporting intake path.";
        _policyService.GetAsync(Arg.Any<CancellationToken>())
            .Returns(CreatePolicy(
                enabled: true,
                canDisable: false,
                canEdit: true,
                reason: reason));

        var cut = _ctx.Render<TenantReportingIntakePolicySection>();

        cut.WaitForState(() => cut.Markup.Contains(reason, StringComparison.Ordinal));
        await Assert.That(Toggle(cut).HasAttribute("disabled")).IsTrue();
        await Assert.That(Toggle(cut).GetAttribute("aria-describedby"))
            .Contains("reporting-intake-authority");
        await Assert.That(cut.Find("[role='status']").TextContent).Contains(reason);
    }

    [Test]
    public async Task InstanceLockedPolicy_DisablesSwitchAndExplainsAuthoritativeLock()
    {
        _policyService.GetAsync(Arg.Any<CancellationToken>())
            .Returns(CreatePolicy(
                enabled: true,
                canDisable: true,
                canEdit: false,
                isLocked: true));

        var cut = _ctx.Render<TenantReportingIntakePolicySection>();

        cut.WaitForState(() => cut.Markup.Contains("Locked by the instance", StringComparison.Ordinal));
        await Assert.That(Toggle(cut).HasAttribute("disabled")).IsTrue();
    }

    [Test]
    public async Task PolicyWithoutEditLink_DisablesSwitchWithoutInspectingLocalAuthorization()
    {
        _policyService.GetAsync(Arg.Any<CancellationToken>())
            .Returns(CreatePolicy(enabled: false, canDisable: true, canEdit: false));

        var cut = _ctx.Render<TenantReportingIntakePolicySection>();

        cut.WaitForState(() => cut.Markup.Contains("read-only", StringComparison.OrdinalIgnoreCase));
        await Assert.That(Toggle(cut).HasAttribute("disabled")).IsTrue();
    }

    [Test]
    public async Task LoadFailure_RendersAlertAndAnnouncesSafeMessage()
    {
        _policyService.GetAsync(Arg.Any<CancellationToken>())
            .Returns<Task<HalResourceOfTenantReportingIntakePolicyDto>>(
                _ => throw new InvalidOperationException("sensitive downstream detail"));

        var cut = _ctx.Render<TenantReportingIntakePolicySection>();

        cut.WaitForState(() => cut.FindAll("[role='alert']").Count == 1);
        await Assert.That(cut.Find("[role='alert']").TextContent).Contains("could not be loaded");
        await Assert.That(cut.Markup).DoesNotContain("sensitive downstream detail");
        await _announcer.Received(1).AnnounceAssertiveAsync(
            Arg.Is<string>(message => message.Contains("could not be loaded", StringComparison.OrdinalIgnoreCase)));
    }

    [Test]
    public async Task ExpiredSession_RendersReauthenticationAlertInsteadOfRetryAdvice()
    {
        _policyService.GetAsync(Arg.Any<CancellationToken>())
            .Returns<Task<HalResourceOfTenantReportingIntakePolicyDto>>(
                _ => throw new ApiException(
                    "Unauthorized",
                    401,
                    "sensitive downstream detail",
                    new Dictionary<string, IEnumerable<string>>(),
                    null));

        var cut = _ctx.Render<TenantReportingIntakePolicySection>();

        cut.WaitForState(() => cut.Markup.Contains("session has expired", StringComparison.OrdinalIgnoreCase));
        await Assert.That(cut.Find("[role='alert']").TextContent).Contains("Sign in again");
        await Assert.That(cut.Markup).DoesNotContain("Try again");
        await Assert.That(cut.Markup).DoesNotContain("sensitive downstream detail");
        await _announcer.Received(1).AnnounceAssertiveAsync(
            Arg.Is<string>(message => message.Contains("Sign in again", StringComparison.Ordinal)));
    }

    [Test]
    public async Task RejectedUpdate_WhenAccessIsForbidden_ReloadsPolicyAndRendersAccessDeniedAlert()
    {
        _policyService.GetAsync(Arg.Any<CancellationToken>())
            .Returns(
                CreatePolicy(enabled: true, canDisable: true, canEdit: true),
                CreatePolicy(enabled: true, canDisable: true, canEdit: false));
        _policyService.UpdateAsync(false, Arg.Any<CancellationToken>())
            .Returns<Task<BaseCommandResponseOfGuid>>(
                _ => throw new ApiException(
                    "Forbidden",
                    403,
                    "sensitive downstream detail",
                    new Dictionary<string, IEnumerable<string>>(),
                    null));
        var cut = _ctx.Render<TenantReportingIntakePolicySection>();
        cut.WaitForState(() => Toggle(cut).HasAttribute("disabled") is false);

        await cut.InvokeAsync(() => Toggle(cut).Change(false));

        cut.WaitForState(() => cut.Markup.Contains("no longer have permission", StringComparison.OrdinalIgnoreCase));
        await Assert.That(Toggle(cut).IsChecked).IsTrue();
        await Assert.That(Toggle(cut).HasAttribute("disabled")).IsTrue();
        await Assert.That(cut.Markup).DoesNotContain("sensitive downstream detail");
        await _policyService.Received(2).GetAsync(Arg.Any<CancellationToken>());
        await _announcer.Received(1).AnnounceAssertiveAsync(
            Arg.Is<string>(message => message.Contains("no longer have permission", StringComparison.OrdinalIgnoreCase)));
    }

    [Test]
    public async Task SuccessfulUpdate_ReloadsAuthoritativePolicyAndAnnouncesCompletion()
    {
        _policyService.GetAsync(Arg.Any<CancellationToken>())
            .Returns(
                CreatePolicy(enabled: true, canDisable: true, canEdit: true),
                CreatePolicy(enabled: false, canDisable: true, canEdit: true));
        _policyService.UpdateAsync(false, Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid());
        var cut = _ctx.Render<TenantReportingIntakePolicySection>();
        cut.WaitForState(() => Toggle(cut).HasAttribute("disabled") is false);

        await cut.InvokeAsync(() => Toggle(cut).Change(false));

        cut.WaitForState(() => Toggle(cut).IsChecked is false);
        await _policyService.Received(1).UpdateAsync(false, Arg.Any<CancellationToken>());
        await _policyService.Received(2).GetAsync(Arg.Any<CancellationToken>());
        await _announcer.Received(1).AnnouncePoliteAsync(
            Arg.Is<string>(message => message.Contains("updated", StringComparison.OrdinalIgnoreCase)));
    }

    [Test]
    public async Task UpdateFailure_RestoresAuthoritativePolicyAndRendersSafeAlert()
    {
        _policyService.GetAsync(Arg.Any<CancellationToken>())
            .Returns(
                CreatePolicy(enabled: true, canDisable: true, canEdit: true),
                CreatePolicy(enabled: true, canDisable: true, canEdit: true));
        _policyService.UpdateAsync(false, Arg.Any<CancellationToken>())
            .Returns<Task<BaseCommandResponseOfGuid>>(
                _ => throw new InvalidOperationException("sensitive downstream detail"));
        var cut = _ctx.Render<TenantReportingIntakePolicySection>();
        cut.WaitForState(() => Toggle(cut).HasAttribute("disabled") is false);

        await cut.InvokeAsync(() => Toggle(cut).Change(false));

        cut.WaitForState(() => cut.FindAll("[role='alert']").Count == 1);
        await Assert.That(Toggle(cut).IsChecked).IsTrue();
        await Assert.That(cut.Find("[role='alert']").TextContent).Contains("could not be updated");
        await Assert.That(cut.Markup).DoesNotContain("sensitive downstream detail");
        await _announcer.Received(1).AnnounceAssertiveAsync(
            Arg.Is<string>(message => message.Contains("could not be updated", StringComparison.OrdinalIgnoreCase)));
    }

    private static HalResourceOfTenantReportingIntakePolicyDto CreatePolicy(
        bool enabled,
        bool canDisable,
        bool canEdit,
        bool isLocked = false,
        string? reason = null)
    {
        return new HalResourceOfTenantReportingIntakePolicyDto
        {
            Enabled = enabled,
            Source = SettingSource.TenantOverride,
            IsLockedByInstance = isLocked,
            CanDisable = canDisable,
            Reason = reason,
            _links = canEdit
                ? new Dictionary<string, HalLink>
                {
                    ["edit"] = new() { Href = "/api/v1/tenant/reporting-intake-policy" }
                }
                : new Dictionary<string, HalLink>()
        };
    }

    private static IHtmlInputElement Toggle(IRenderedComponent<TenantReportingIntakePolicySection> cut)
    {
        var renderedLabel = cut.FindAll("label").Single(element =>
            element.TextContent.Contains(ToggleLabel, StringComparison.OrdinalIgnoreCase));
        return (IHtmlInputElement)(renderedLabel.QuerySelector("input")
            ?? throw new InvalidOperationException("Reporting intake switch input was not found."));
    }

    public void Dispose()
    {
        _ctx.Dispose();
    }
}

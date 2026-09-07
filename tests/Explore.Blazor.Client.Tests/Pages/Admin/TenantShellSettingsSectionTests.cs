// ABOUTME: Tests tenant workspace-shell governance controls and server-authoritative editability.
// ABOUTME: Proves D8 settings write exact values while locked settings remain disabled with reasons.

using Explore.Blazor.Client.Contracts.Services.Shell;
using Explore.Blazor.Client.Pages.Admin.Tenant.Components;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Admin;

public sealed class TenantShellSettingsSectionTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly ITenantShellSettingsService _settingsService =
        Substitute.For<ITenantShellSettingsService>();

    public TenantShellSettingsSectionTests()
    {
        _ctx.Services.AddSingleton(_settingsService);
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task ShellSettings_WhenEditable_RenderAllControlsAndWriteExactValue()
    {
        _settingsService.GetAsync(Arg.Any<CancellationToken>()).Returns(CreateSettings(canEdit: true));
        _settingsService.UpdateAsync(
                TenantShellSettingsSection.DefaultNavModeEventsKey,
                "Collapsed",
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true });

        var cut = _ctx.RenderMudComponent<TenantShellSettingsSection>();
        cut.WaitForState(() => cut.Markup.Contains("Events navigation mode", StringComparison.Ordinal));

        await Assert.That(cut.Markup).Contains("Public workspace rail");
        await Assert.That(cut.Markup).Contains("Studio navigation mode");
        await Assert.That(cut.Markup).Contains("AI navigation mode");
        await Assert.That(cut.Markup).Contains("Allow personal navigation overrides");
        await Assert.That(cut.Markup).Contains("Organizer default workspace");

        var select = cut.FindComponents<MudSelect<string>>()
            .Single(component => component.Instance.Label == "Events navigation mode");
        await cut.InvokeAsync(() => select.Instance.ValueChanged.InvokeAsync("Collapsed"));

        await _settingsService.Received(1).UpdateAsync(
            TenantShellSettingsSection.DefaultNavModeEventsKey,
            "Collapsed",
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ShellSettings_WhenLocked_DisableControlsAndShowServerReason()
    {
        const string reason = "Locked by the instance shell policy.";
        _settingsService.GetAsync(Arg.Any<CancellationToken>()).Returns(CreateSettings(
            canEdit: false,
            reason: reason,
            source: SettingSource.SystemLocked));

        var cut = _ctx.RenderMudComponent<TenantShellSettingsSection>();
        cut.WaitForState(() => cut.Markup.Contains("Events navigation mode", StringComparison.Ordinal));

        var selects = cut.FindComponents<MudSelect<string>>();
        var switches = cut.FindComponents<MudSwitch<bool>>();

        await Assert.That(selects.All(component => component.Instance.Disabled)).IsTrue();
        await Assert.That(switches.All(component => component.Instance.Disabled)).IsTrue();
        await Assert.That(cut.FindAll("[role='status']").Count).IsEqualTo(6);
        await Assert.That(cut.Markup).Contains(reason, StringComparison.Ordinal);
        await _settingsService.DidNotReceive().UpdateAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    private static HalResourceOfSettingGroupResponseDto CreateSettings(
        bool canEdit,
        string? reason = null,
        SettingSource source = SettingSource.TenantOverride) => new()
        {
            Category = TenantShellSettingsSection.Category,
            Settings = TenantShellSettingsSection.SettingKeys.Select(key => new EffectiveSettingDto
            {
                Key = key,
                Value = key switch
                {
                    TenantShellSettingsSection.AllowUserNavOverrideKey => "true",
                    TenantShellSettingsSection.OrganizerDefaultWorkspaceKey => "\"Events\"",
                    TenantShellSettingsSection.RailPublicVisibilityKey => "\"AuthenticatedOnly\"",
                    _ => "\"Docked\""
                },
                CanEdit = canEdit,
                Source = source,
                IsLocked = !canEdit,
                IsLockable = true,
                Reason = reason
            }).ToList()
        };
}

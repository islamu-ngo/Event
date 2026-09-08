using System.Text.Json;
using AngleSharp.Dom;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Contracts.Services.Federation;
using Explore.Blazor.Client.Pages.Admin.Tenant.Components;
using Explore.Blazor.Client.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Admin;

public sealed class TenantPoliciesSectionTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly IAtprotoFederationSettingsService _settingsService =
        Substitute.For<IAtprotoFederationSettingsService>();
    private readonly ITenantOnboardingService _tenantOnboardingService =
        Substitute.For<ITenantOnboardingService>();
    private readonly IAccessibilityAnnouncerService _announcer =
        Substitute.For<IAccessibilityAnnouncerService>();

    public TenantPoliciesSectionTests()
    {
        _ctx.Services.AddSingleton(_settingsService);
        _ctx.Services.RemoveAll<IAccessibilityAnnouncerService>();
        _ctx.Services.AddSingleton(_tenantOnboardingService);
        _ctx.Services.AddSingleton(_announcer);
        _announcer.AnnouncePoliteAsync(Arg.Any<string>()).Returns(Task.CompletedTask);
        _announcer.AnnounceAssertiveAsync(Arg.Any<string>()).Returns(Task.CompletedTask);
        _tenantOnboardingService.GetStatusAsync().Returns(CreateTenantStatus());
        _tenantOnboardingService.GetTenantSettingsAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(call => CreatePolicyCategory(call.ArgAt<string>(0)));
        _tenantOnboardingService.UpdateTenantSettingAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true });
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task PolicySwitches_WhenEditable_WriteAllEightExactKeysOnce()
    {
        var cut = RenderComponent();
        cut.WaitForState(() => PolicyInput(cut, "Allow users to submit events").HasAttribute("disabled") == false);

        var mappings = new[]
        {
            ("Allow users to submit events", "events.user_submission_enabled"),
            ("Allow organizations to submit events", "events.organization_submission_enabled"),
            ("Allow groups to submit events", "events.group_submission_enabled"),
            ("Require approval before publication", "events.require_approval"),
            ("Event card click opens detail page", "events.card_click_opens_detail_page"),
            ("Require organization verification", "organizations.verification_required"),
            ("Allow organization self-registration", "organizations.self_registration_enabled"),
            ("Allow group self-registration", "groups.self_registration_enabled")
        };

        foreach ((string label, string key) in mappings)
        {
            PolicyInput(cut, label).Change(true);
            await _tenantOnboardingService.Received(1).UpdateTenantSettingAsync(
                key,
                "true",
                Arg.Any<CancellationToken>());
        }

        await _tenantOnboardingService.Received(8).UpdateTenantSettingAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PolicySwitch_WhenPending_DisablesOnlyActiveKeyAndSuppressesRapidRepeat()
    {
        var pending = new TaskCompletionSource<BaseCommandResponseOfGuid>();
        var pendingStates = new List<bool>();
        _tenantOnboardingService.UpdateTenantSettingAsync(
                "events.user_submission_enabled",
                "true",
                Arg.Any<CancellationToken>())
            .Returns(pending.Task);
        var model = new TenantPolicySettingsDto();
        var cut = RenderComponent(model: model, pendingChanged: pendingStates.Add);
        cut.WaitForState(() => PolicyInput(cut, "Allow users to submit events").HasAttribute("disabled") == false);

        Task firstChange = cut.InvokeAsync(() => PolicyInput(cut, "Allow users to submit events").Change(true));
        cut.WaitForState(() => PolicyInput(cut, "Allow users to submit events").HasAttribute("disabled"));

        await Assert.That(model.AllowUserSubmittedEvents).IsFalse();
        await Assert.That(pendingStates).IsEquivalentTo([true]);
        await Assert.That(PolicyInput(cut, "Allow organizations to submit events").HasAttribute("disabled")).IsFalse();
        var activeSwitch = cut.FindComponents<MudSwitch<bool?>>().Single(component =>
            component.Markup.Contains("Allow users to submit events", StringComparison.Ordinal));
        await cut.InvokeAsync(() => activeSwitch.Instance.ValueChanged.InvokeAsync(false));
        await _tenantOnboardingService.Received(1).UpdateTenantSettingAsync(
            "events.user_submission_enabled",
            "true",
            Arg.Any<CancellationToken>());

        pending.SetResult(new BaseCommandResponseOfGuid { Success = true });
        await firstChange;
        cut.WaitForState(() => PolicyInput(cut, "Allow users to submit events").HasAttribute("disabled") == false);
        await Assert.That(model.AllowUserSubmittedEvents).IsTrue();
        await Assert.That(pendingStates).IsEquivalentTo([true, false]);
    }

    [Test]
    public async Task PolicySwitch_WhenSaveSucceeds_ShowsAndAnnouncesConfirmedStatus()
    {
        var model = new TenantPolicySettingsDto();
        var cut = RenderComponent(model: model);
        cut.WaitForState(() => PolicyInput(cut, "Allow users to submit events").HasAttribute("disabled") == false);

        PolicyInput(cut, "Allow users to submit events").Change(true);
        cut.WaitForState(() => cut.FindAll("[role='status']").Any(element =>
            element.TextContent.Contains("Allow users to submit events saved.", StringComparison.Ordinal)));

        await Assert.That(model.AllowUserSubmittedEvents).IsTrue();
        await _announcer.Received(1).AnnouncePoliteAsync("Allow users to submit events saved.");
        await _announcer.DidNotReceive().AnnounceAssertiveAsync(Arg.Any<string>());
    }

    [Test]
    public async Task PolicySwitch_WhenApiRejects_ReloadsCategoryAndRestoresConfirmedValue()
    {
        HalResourceOfSettingGroupResponseDto initial = CreatePolicyCategory("Events");
        HalResourceOfSettingGroupResponseDto reloaded = CreatePolicyCategory("Events");
        _tenantOnboardingService.GetTenantSettingsAsync("Events", Arg.Any<CancellationToken>())
            .Returns(initial, reloaded);
        _tenantOnboardingService.UpdateTenantSettingAsync(
                "events.user_submission_enabled",
                "true",
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = false, Message = "Saved" });
        var model = new TenantPolicySettingsDto();
        var pendingTransitions = new List<(bool Pending, bool? ModelValue)>();
        var cut = RenderComponent(
            model: model,
            pendingChanged: pending => pendingTransitions.Add((pending, model.AllowUserSubmittedEvents)));
        cut.WaitForState(() => PolicyInput(cut, "Allow users to submit events").HasAttribute("disabled") == false);

        PolicyInput(cut, "Allow users to submit events").Change(true);
        cut.WaitForState(() => cut.FindAll("[role='alert']").Any(element =>
            element.TextContent.Contains("could not be saved", StringComparison.OrdinalIgnoreCase)));

        await Assert.That(model.AllowUserSubmittedEvents).IsFalse();
        (bool Pending, bool? ModelValue)[] expectedTransitions = [(true, false), (false, false)];
        await Assert.That(pendingTransitions).IsEquivalentTo(expectedTransitions);
        await _tenantOnboardingService.Received(2).GetTenantSettingsAsync("Events", Arg.Any<CancellationToken>());
        await _announcer.Received(1).AnnounceAssertiveAsync(
            "Allow users to submit events could not be saved. The latest setting value was restored.");
        await _announcer.DidNotReceive().AnnouncePoliteAsync(Arg.Any<string>());
    }

    [Test]
    public async Task PolicySwitch_WhenTransportFails_ReloadsCategoryAndRestoresConfirmedValue()
    {
        _tenantOnboardingService.GetTenantSettingsAsync("Events", Arg.Any<CancellationToken>())
            .Returns(CreatePolicyCategory("Events"), (HalResourceOfSettingGroupResponseDto?)null);
        _tenantOnboardingService.UpdateTenantSettingAsync(
                "events.user_submission_enabled",
                "true",
                Arg.Any<CancellationToken>())
            .Returns<Task<BaseCommandResponseOfGuid>>(_ => throw new HttpRequestException("credential canary"));
        var model = new TenantPolicySettingsDto();
        var cut = RenderComponent(model: model);
        cut.WaitForState(() => PolicyInput(cut, "Allow users to submit events").HasAttribute("disabled") == false);

        PolicyInput(cut, "Allow users to submit events").Change(true);
        cut.WaitForState(() => cut.FindAll("[role='alert']").Any());

        await Assert.That(model.AllowUserSubmittedEvents).IsFalse();
        await Assert.That(cut.Markup).DoesNotContain("credential canary", StringComparison.Ordinal);
        await _tenantOnboardingService.Received(2).GetTenantSettingsAsync("Events", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PolicySwitch_WhenSiblingReloadIsStale_DoesNotOverwritePendingKey()
    {
        _tenantOnboardingService.GetTenantSettingsAsync("Events", Arg.Any<CancellationToken>())
            .Returns(CreatePolicyCategory("Events"), CreatePolicyCategory("Events"));
        var organizationPending = new TaskCompletionSource<BaseCommandResponseOfGuid>();
        _tenantOnboardingService.UpdateTenantSettingAsync(
                "events.organization_submission_enabled",
                "true",
                Arg.Any<CancellationToken>())
            .Returns(organizationPending.Task);
        _tenantOnboardingService.UpdateTenantSettingAsync(
                "events.user_submission_enabled",
                "true",
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = false });
        var pendingCleared = new TaskCompletionSource();
        var model = new TenantPolicySettingsDto();
        var cut = RenderComponent(
            model: model,
            pendingChanged: pending =>
            {
                if (!pending)
                {
                    pendingCleared.TrySetResult();
                }
            });
        cut.WaitForState(() => PolicyInput(cut, "Allow organizations to submit events").HasAttribute("disabled") == false);

        _ = cut.InvokeAsync(() =>
            PolicyInput(cut, "Allow organizations to submit events").Change(true));
        cut.WaitForState(() => PolicyInput(cut, "Allow organizations to submit events").HasAttribute("disabled"));
        await Assert.That(model.AllowOrganizationSubmittedEvents).IsFalse();
        PolicyInput(cut, "Allow users to submit events").Change(true);

        await Assert.That(model.AllowOrganizationSubmittedEvents).IsFalse();
        organizationPending.SetResult(new BaseCommandResponseOfGuid { Success = true });
        await pendingCleared.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(model.AllowOrganizationSubmittedEvents).IsTrue();
    }

    [Test]
    public async Task PolicySwitch_WhenDisposedWhilePending_SynchronizesParentWithoutStaleAnnouncement()
    {
        var pending = new TaskCompletionSource<BaseCommandResponseOfGuid>();
        var pendingStates = new List<bool>();
        var pendingCleared = new TaskCompletionSource();
        _tenantOnboardingService.UpdateTenantSettingAsync(
                "events.user_submission_enabled",
                "true",
                Arg.Any<CancellationToken>())
            .Returns(pending.Task);
        var model = new TenantPolicySettingsDto();
        var cut = RenderComponent(
            model: model,
            pendingChanged: state =>
            {
                pendingStates.Add(state);
                if (!state)
                {
                    pendingCleared.TrySetResult();
                }
            });
        cut.WaitForState(() => PolicyInput(cut, "Allow users to submit events").HasAttribute("disabled") == false);

        _ = cut.InvokeAsync(() => PolicyInput(cut, "Allow users to submit events").Change(true));
        cut.WaitForState(() => pendingStates.Contains(true));
        cut.Instance.Dispose();
        cut.Dispose();
        pending.SetResult(new BaseCommandResponseOfGuid { Success = true });
        await pendingCleared.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(model.AllowUserSubmittedEvents).IsTrue();
        await Assert.That(pendingStates).IsEquivalentTo([true, false]);
        await _announcer.DidNotReceive().AnnouncePoliteAsync(Arg.Any<string>());
        await _announcer.DidNotReceive().AnnounceAssertiveAsync(Arg.Any<string>());
    }

    [Test]
    public async Task PolicySwitch_WithoutManageTenantSettingsAffordance_IsDisabled()
    {
        _tenantOnboardingService.GetStatusAsync().Returns(CreateTenantStatus(includeManageAffordance: false));

        var cut = RenderComponent();
        cut.WaitForState(() => cut.Markup.Contains("Allow users to submit events", StringComparison.Ordinal));

        await Assert.That(PolicyInput(cut, "Allow users to submit events").HasAttribute("disabled")).IsTrue();
        await Assert.That(cut.Markup).Contains("The server has not granted tenant settings management access.");
        await _tenantOnboardingService.DidNotReceive().UpdateTenantSettingAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PolicySwitch_WhenMetadataIsLocked_IsDisabledAndShowsReason()
    {
        const string reason = "Locked by instance policy.";
        HalResourceOfSettingGroupResponseDto events = CreatePolicyCategory("Events");
        EffectiveSettingDto setting = events.Settings.Single(item => item.Key == "events.user_submission_enabled");
        setting.CanEdit = false;
        setting.Reason = reason;
        _tenantOnboardingService.GetTenantSettingsAsync("Events", Arg.Any<CancellationToken>()).Returns(events);

        var cut = RenderComponent();
        cut.WaitForState(() => cut.Markup.Contains(reason, StringComparison.Ordinal));

        await Assert.That(PolicyInput(cut, "Allow users to submit events").HasAttribute("disabled")).IsTrue();
        await Assert.That(cut.FindAll("[role='status']").Any(element => element.TextContent.Contains(reason, StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task PolicySwitch_WhenCategoryMetadataIsMissing_FailsClosed()
    {
        _tenantOnboardingService.GetTenantSettingsAsync("Events", Arg.Any<CancellationToken>())
            .Returns(new HalResourceOfSettingGroupResponseDto { Category = "Organizations" });

        var cut = RenderComponent();
        cut.WaitForState(() => cut.Markup.Contains("Allow users to submit events", StringComparison.Ordinal));

        await Assert.That(PolicyInput(cut, "Allow users to submit events").HasAttribute("disabled")).IsTrue();
    }

    [Test]
    public async Task PolicySwitch_WhenKeyMetadataIsMissing_FailsClosed()
    {
        HalResourceOfSettingGroupResponseDto events = CreatePolicyCategory("Events");
        events.Settings = events.Settings
            .Where(setting => setting.Key != "events.user_submission_enabled")
            .ToList();
        _tenantOnboardingService.GetTenantSettingsAsync("Events", Arg.Any<CancellationToken>()).Returns(events);

        var cut = RenderComponent();
        cut.WaitForState(() => cut.Markup.Contains("Allow users to submit events", StringComparison.Ordinal));

        await Assert.That(PolicyInput(cut, "Allow users to submit events").HasAttribute("disabled")).IsTrue();
    }

    [Test]
    public async Task PolicySwitch_WhenCanEditMetadataIsMissing_FailsClosed()
    {
        HalResourceOfSettingGroupResponseDto events = CreatePolicyCategory("Events");
        events.Settings.Single(setting => setting.Key == "events.user_submission_enabled").CanEdit = null;
        _tenantOnboardingService.GetTenantSettingsAsync("Events", Arg.Any<CancellationToken>()).Returns(events);

        var cut = RenderComponent();
        cut.WaitForState(() => cut.Markup.Contains("Allow users to submit events", StringComparison.Ordinal));

        await Assert.That(PolicyInput(cut, "Allow users to submit events").HasAttribute("disabled")).IsTrue();
    }

    [Test]
    public async Task PolicySwitch_WhenBooleanMetadataIsMalformed_FailsClosed()
    {
        HalResourceOfSettingGroupResponseDto events = CreatePolicyCategory("Events");
        events.Settings.Single(setting => setting.Key == "events.user_submission_enabled").Value = "not-a-boolean";
        _tenantOnboardingService.GetTenantSettingsAsync("Events", Arg.Any<CancellationToken>()).Returns(events);

        var cut = RenderComponent();
        cut.WaitForState(() => cut.Markup.Contains("Allow users to submit events", StringComparison.Ordinal));

        await Assert.That(PolicyInput(cut, "Allow users to submit events").HasAttribute("disabled")).IsTrue();
        await Assert.That(cut.Markup).Contains("Setting metadata is unavailable.");
    }

    [Test]
    public async Task AtprotoEvents_WhenEditable_UpdatesCombinedCapabilitySetting()
    {
        _settingsService.GetTenantAsync(Arg.Any<CancellationToken>())
            .Returns(CreateSettings(eventsCanEdit: true));
        _settingsService.UpdateTenantAsync(
                "federation.atproto_events_enabled",
                "true",
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true });

        var cut = RenderComponent();
        cut.WaitForState(() => cut.Markup.Contains("Enable AT Protocol events", StringComparison.OrdinalIgnoreCase));

        var eventsLabel = cut.FindAll("label").Single(label =>
            label.TextContent.Contains("Enable AT Protocol events", StringComparison.OrdinalIgnoreCase));
        (eventsLabel.QuerySelector("input") ?? throw new InvalidOperationException("AT Protocol events switch input not found."))
            .Change(true);

        await _settingsService.Received(1).UpdateTenantAsync(
            "federation.atproto_events_enabled",
            "true",
            Arg.Any<CancellationToken>());
        var capabilityCopy = cut.FindAll("p")
            .Single(element => element.TextContent.Contains("public inbound discovery", StringComparison.OrdinalIgnoreCase))
            .TextContent;
        await Assert.That(capabilityCopy).Contains("public inbound discovery works without AT\u00A0Protocol authentication", StringComparison.OrdinalIgnoreCase);
        await Assert.That(capabilityCopy).Contains("Publishing and RSVP synchronization require AT\u00A0Protocol authentication", StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public async Task AtprotoEvents_WhenLocked_DisablesControlAndDoesNotWrite()
    {
        _settingsService.GetTenantAsync(Arg.Any<CancellationToken>())
            .Returns(CreateSettings(
                eventsCanEdit: false,
                reason: "Locked by the instance administrator.",
                source: SettingSource.SystemLocked));

        var cut = RenderComponent();
        cut.WaitForState(() => cut.Markup.Contains("Enable AT Protocol events", StringComparison.OrdinalIgnoreCase));
        var eventsLabel = cut.FindAll("label").Single(label =>
            label.TextContent.Contains("Enable AT Protocol events", StringComparison.OrdinalIgnoreCase));
        var atprotoSwitch = eventsLabel.QuerySelector("input")
            ?? throw new InvalidOperationException("AT Protocol events switch input not found.");

        await Assert.That(atprotoSwitch.HasAttribute("disabled")).IsTrue();
        await Assert.That(cut.Markup).Contains("Source: System locked", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).Contains("Lock: Locked", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).Contains("Locked by the instance administrator.");
        await Assert.That(cut.FindAll("[role='status']").Any(element =>
            element.TextContent.Contains("Locked by the instance administrator.", StringComparison.Ordinal))).IsTrue();
        await _settingsService.DidNotReceive().UpdateTenantAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AtprotoEvents_WhenLoadFails_RendersSafeAccessibleAlert()
    {
        _settingsService.GetTenantAsync(Arg.Any<CancellationToken>())
            .Returns<Task<HalResourceOfSettingGroupResponseDto>>(_ =>
                throw new HttpRequestException("provider credential canary"));

        var cut = RenderComponent();
        cut.WaitForState(() => cut.Markup.Contains(
            "AT Protocol event settings are temporarily unavailable.",
            StringComparison.Ordinal));
        var alert = cut.Find("[role='alert']");

        await Assert.That(alert.TextContent).Contains("temporarily unavailable", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).DoesNotContain("provider credential canary", StringComparison.Ordinal);
        await Assert.That(cut.FindAll("button").Any(button =>
            button.TextContent.Contains("Retry AT Protocol settings", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task AtprotoEvents_WhenApplicationSessionExpired_RequestsApplicationSignIn()
    {
        _settingsService.GetTenantAsync(Arg.Any<CancellationToken>())
            .Returns<Task<HalResourceOfSettingGroupResponseDto>>(_ =>
                throw new ApiException(
                    "Unauthorized",
                    401,
                    response: null,
                    new Dictionary<string, IEnumerable<string>>(),
                    innerException: null));

        var cut = RenderComponent();
        cut.WaitForState(() => cut.Markup.Contains(
            "Your application session expired.",
            StringComparison.Ordinal));
        var alert = cut.Find("[role='alert']");

        await Assert.That(alert.TextContent).Contains("Sign in again", StringComparison.OrdinalIgnoreCase);
        await Assert.That(alert.TextContent).DoesNotContain("AT Protocol authentication", StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public async Task AtprotoEvents_WhenInstanceScopeIsSelected_UsesInstanceHalAffordances()
    {
        _settingsService.GetInstanceAsync(Arg.Any<CancellationToken>())
            .Returns(CreateInstanceSettings("update-federation.atproto_events_enabled"));
        _settingsService.UpdateInstanceAsync(
                "federation.atproto_events_enabled",
                "true",
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true });

        var cut = RenderComponent(useInstanceScope: true);
        cut.WaitForState(() => cut.Markup.Contains("Enable AT Protocol events", StringComparison.OrdinalIgnoreCase));

        var eventsLabel = cut.FindAll("label").Single(label =>
            label.TextContent.Contains("Enable AT Protocol events", StringComparison.OrdinalIgnoreCase));
        (eventsLabel.QuerySelector("input") ?? throw new InvalidOperationException("AT Protocol events switch input not found."))
            .Change(true);

        await _settingsService.Received(1).UpdateInstanceAsync(
            "federation.atproto_events_enabled",
            "true",
            Arg.Any<CancellationToken>());
        await _settingsService.DidNotReceive().UpdateTenantAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await Assert.That(cut.Markup).DoesNotContain("Locked by the instance administrator.", StringComparison.Ordinal);
    }

    [Test]
    public async Task AtprotoEvents_WhenInstanceUnlockAffordanceExists_UnlocksTenantOverride()
    {
        _settingsService.GetInstanceAsync(Arg.Any<CancellationToken>())
            .Returns(CreateInstanceSettings("unlock-federation.atproto_events_enabled"));
        _settingsService.SetInstanceLockAsync(
                "federation.atproto_events_enabled",
                false,
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true });

        var cut = RenderComponent(useInstanceScope: true);
        cut.WaitForState(() => cut.Markup.Contains("Unlock tenant override", StringComparison.OrdinalIgnoreCase));

        cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Unlock tenant override", StringComparison.OrdinalIgnoreCase))
            .Click();

        await _settingsService.Received(1).SetInstanceLockAsync(
            "federation.atproto_events_enabled",
            false,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task BackfillControls_WhenEditable_SubmitPlainTenantValues()
    {
        const string enabledKey = "federation.atproto_events_backfill_enabled";
        const string modeKey = "federation.atproto_events_backfill_mode";
        var settings = CreateSettings(eventsCanEdit: true);
        settings.Settings.Add(new EffectiveSettingDto
        {
            Key = enabledKey,
            Value = "false",
            CanEdit = true,
            Source = SettingSource.TenantOverride,
            IsLocked = false,
            IsLockable = true
        });
        settings.Settings.Add(new EffectiveSettingDto
        {
            Key = modeKey,
            Value = "\"downtime_only\"",
            CanEdit = true,
            Source = SettingSource.TenantOverride,
            IsLocked = false,
            IsLockable = true
        });
        _settingsService.GetTenantAsync(Arg.Any<CancellationToken>()).Returns(settings);
        _settingsService.UpdateTenantAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true });

        var cut = RenderComponent();
        cut.WaitForState(() => cut.Markup.Contains("Enable inbound event recovery", StringComparison.Ordinal));

        var backfillLabel = cut.FindAll("label").Single(label =>
            label.TextContent.Contains("Enable inbound event recovery", StringComparison.Ordinal));
        (backfillLabel.QuerySelector("input") ?? throw new InvalidOperationException("Backfill switch input not found."))
            .Change(true);
        var modeSelect = cut.FindComponents<MudSelect<string>>()
            .Single(component => component.Instance.Label == "Inbound recovery mode");
        await cut.InvokeAsync(() => modeSelect.Instance.ValueChanged.InvokeAsync("full"));

        await _settingsService.Received(1).UpdateTenantAsync(
            enabledKey,
            "true",
            Arg.Any<CancellationToken>());
        await _settingsService.Received(1).UpdateTenantAsync(
            modeKey,
            "full",
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task BackfillControls_WhenLocked_AreDisabledAndShowReason()
    {
        const string reason = "Inbound recovery is locked by instance policy until the receiver checkpoint has been reviewed by an administrator.";
        var settings = CreateSettings(eventsCanEdit: true);
        settings.Settings.Add(new EffectiveSettingDto
        {
            Key = "federation.atproto_events_backfill_enabled",
            Value = "false",
            CanEdit = false,
            Source = SettingSource.SystemLocked,
            IsLocked = true,
            IsLockable = true,
            Reason = reason
        });
        settings.Settings.Add(new EffectiveSettingDto
        {
            Key = "federation.atproto_events_backfill_mode",
            Value = "\"downtime_only\"",
            CanEdit = false,
            Source = SettingSource.SystemLocked,
            IsLocked = true,
            IsLockable = true,
            Reason = reason
        });
        _settingsService.GetTenantAsync(Arg.Any<CancellationToken>()).Returns(settings);

        var cut = RenderComponent();
        cut.WaitForState(() => cut.Markup.Contains("Enable inbound event recovery", StringComparison.Ordinal));

        var backfillLabel = cut.FindAll("label").Single(label =>
            label.TextContent.Contains("Enable inbound event recovery", StringComparison.Ordinal));
        var backfillInput = backfillLabel.QuerySelector("input")
            ?? throw new InvalidOperationException("Backfill switch input not found.");
        var modeSelect = cut.FindComponents<MudSelect<string>>()
            .Single(component => component.Instance.Label == "Inbound recovery mode");

        await Assert.That(backfillInput.HasAttribute("disabled")).IsTrue();
        await Assert.That(modeSelect.Instance.Disabled).IsTrue();
        await Assert.That(cut.Markup).Contains("Source: System locked", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).Contains(reason, StringComparison.Ordinal);
        await _settingsService.DidNotReceive().UpdateTenantAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ValidationProfile_WhenEditable_SubmitsPlainTenantCode()
    {
        const string key = "federation.atproto_event_validation_profile";
        _settingsService.GetTenantAsync(Arg.Any<CancellationToken>())
            .Returns(CreateSettings(eventsCanEdit: true));
        _settingsService.UpdateTenantAsync(
                key,
                "community_lexicon",
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true });

        var cut = RenderComponent();
        cut.WaitForState(() => cut.Markup.Contains("Event creation validation", StringComparison.Ordinal));
        var profileSelect = cut.FindComponents<MudSelect<string>>()
            .Single(component => component.Instance.Label == "Event creation validation");
        await cut.InvokeAsync(() => profileSelect.Instance.ValueChanged.InvokeAsync("community_lexicon"));

        await _settingsService.Received(1).UpdateTenantAsync(
            key,
            "community_lexicon",
            Arg.Any<CancellationToken>());
    }

    private IRenderedComponent<TenantPoliciesSection> RenderComponent(
        bool useInstanceScope = false,
        TenantPolicySettingsDto? model = null,
        Action<bool>? pendingChanged = null) =>
        _ctx.RenderMudComponent<TenantPoliciesSection>(parameters => parameters
            .Add(component => component.Model, model ?? new TenantPolicySettingsDto())
            .Add(component => component.UseInstanceScope, useInstanceScope)
            .Add(component => component.PendingChanged,
                EventCallback.Factory.Create(this, pendingChanged ?? (_ => { }))));

    private static IElement PolicyInput(IRenderedComponent<TenantPoliciesSection> cut, string label)
    {
        var renderedLabel = cut.FindAll("label").Single(element =>
            element.TextContent.Contains(label, StringComparison.OrdinalIgnoreCase));
        return renderedLabel.QuerySelector("input")
            ?? throw new InvalidOperationException($"Policy switch input '{label}' not found.");
    }

    private static TenantOnboardingStatusDto CreateTenantStatus(bool includeManageAffordance = true)
    {
        var status = new TenantOnboardingStatusDto
        {
            IsAuthenticated = true,
            IsCurrentUserTenantAdministrator = true,
            TenantId = Guid.NewGuid()
        };
        if (includeManageAffordance)
        {
            status.AdditionalProperties["_links"] = JsonSerializer.SerializeToElement(
                new Dictionary<string, object>
                {
                    ["manage-tenant-settings"] = new { href = "/api/tenant-onboarding/policy-settings" }
                });
        }

        return status;
    }

    private static HalResourceOfSettingGroupResponseDto CreatePolicyCategory(string category) => category switch
    {
        "Events" => new HalResourceOfSettingGroupResponseDto
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
        "Organizations" => new HalResourceOfSettingGroupResponseDto
        {
            Category = category,
            Settings =
            [
                EditableBoolean("organizations.verification_required"),
                EditableBoolean("organizations.self_registration_enabled")
            ]
        },
        "Groups" => new HalResourceOfSettingGroupResponseDto
        {
            Category = category,
            Settings = [EditableBoolean("groups.self_registration_enabled")]
        },
        _ => new HalResourceOfSettingGroupResponseDto { Category = category }
    };

    private static EffectiveSettingDto EditableBoolean(string key) => new()
    {
        Key = key,
        Value = "false",
        CanEdit = true,
        Source = SettingSource.TenantOverride,
        IsLocked = false
    };

    private static HalResourceOfSettingGroupResponseDto CreateSettings(
        bool eventsCanEdit,
        string? reason = null,
        SettingSource source = SettingSource.TenantOverride) =>
        new()
        {
            Category = "AtprotoFederation",
            Settings =
            [
                new EffectiveSettingDto
                {
                    Key = "federation.atproto_events_enabled",
                    Value = "false",
                    CanEdit = eventsCanEdit,
                    Source = source,
                    IsLocked = source is SettingSource.SystemLocked or SettingSource.TenantLocked,
                    Reason = reason
                },
                new EffectiveSettingDto
                {
                    Key = "federation.atproto_event_validation_profile",
                    Value = "\"platform\"",
                    CanEdit = eventsCanEdit,
                    Source = source,
                    IsLocked = source is SettingSource.SystemLocked or SettingSource.TenantLocked,
                    Reason = reason
                }
            ]
        };

    private static HalResourceOfSettingGroupResponseDto CreateInstanceSettings(params string[] relations) =>
        new()
        {
            Category = "AtprotoFederation",
            Settings =
            [
                new EffectiveSettingDto
                {
                    Key = "federation.atproto_events_enabled",
                    Value = "false",
                    CanEdit = true,
                    Source = SettingSource.SystemLocked,
                    IsLocked = true,
                    IsLockable = true
                },
                new EffectiveSettingDto
                {
                    Key = "federation.atproto_event_validation_profile",
                    Value = "\"platform\"",
                    CanEdit = true,
                    Source = SettingSource.SystemLocked,
                    IsLocked = true,
                    IsLockable = true
                }
            ],
            _links = relations.ToDictionary(
                relation => relation,
                relation => new HalLink { Href = $"/api/settings/{relation}" },
                StringComparer.Ordinal)
        };
}

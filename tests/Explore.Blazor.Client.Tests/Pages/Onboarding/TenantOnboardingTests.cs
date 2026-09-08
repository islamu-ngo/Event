using System.Reflection;
using System.Text.Json;
using AngleSharp.Dom;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Models.Responses;
using Explore.Blazor.Client.Pages.Onboarding;
using Explore.Blazor.Client.Routing.ControlPlane;

namespace Explore.Blazor.Client.Tests.Pages.Onboarding;

public sealed class TenantOnboardingTests : IDisposable
{
    private readonly BlazorTestContext _ctx;
    private readonly ITenantOnboardingService _tenantOnboardingService;
    private readonly IUserService _userService;
    private readonly IAccessibilityAnnouncerService _announcer;

    public TenantOnboardingTests()
    {
        _ctx = new BlazorTestContext();
        _ctx.SetAuthenticatedUser(Guid.NewGuid(), "Tenant Admin");

        _tenantOnboardingService = Substitute.For<ITenantOnboardingService>();
        _userService = Substitute.For<IUserService>();
        _announcer = Substitute.For<IAccessibilityAnnouncerService>();
        ITranslationService translations = Substitute.For<ITranslationService>();
        translations.T(Arg.Any<string>(), Arg.Any<string?>())
            .Returns(call => call.ArgAt<string?>(1) ?? call.ArgAt<string>(0));

        _ctx.Services.AddSingleton(_tenantOnboardingService);
        _ctx.Services.AddSingleton(_userService);
        _ctx.Services.AddSingleton(_announcer);
        _ctx.Services.AddSingleton(translations);

        _userService.SyncUserAsync().Returns(new BaseCommandResponseOfGuid { Success = true });
        _tenantOnboardingService.GetSettingsAsync().Returns(CreateSettingsModel());
        _tenantOnboardingService.CompleteAsync(Arg.Any<TenantPolicySettingsDto>())
            .Returns(new BaseCommandResponseOfGuid { Success = true, Message = "ok" });
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task MissingStatus_FailsClosedWithoutLoadingOrCompletingSettings()
    {
        _tenantOnboardingService.GetStatusAsync().Returns((TenantOnboardingStatusDto?)null);

        IRenderedComponent<TenantOnboarding> cut = Render();

        cut.WaitForAssertion(() => RequireAlertContains(cut, "authoritative tenant onboarding status"));
        _ = _tenantOnboardingService.DidNotReceive().GetSettingsAsync();
        _ = _tenantOnboardingService.DidNotReceive().CompleteAsync(Arg.Any<TenantPolicySettingsDto>());
        _ = _announcer.Received(1).AnnounceAssertiveAsync(
            "Unable to load the authoritative tenant onboarding status.");
        await Assert.That(cut.FindAll("h1").Count).IsEqualTo(1);
    }

    [Test]
    public async Task MissingTenantIdentifier_FailsClosedWithoutSettingsCall()
    {
        _tenantOnboardingService.GetStatusAsync().Returns(new TenantOnboardingStatusDto
        {
            IsAuthenticated = true,
            IsCurrentUserTenantAdministrator = true
        });

        IRenderedComponent<TenantOnboarding> cut = Render();

        cut.WaitForAssertion(() => RequireAlertContains(cut, "No trusted tenant context"));
        _ = _tenantOnboardingService.DidNotReceive().GetSettingsAsync();
        _ = _tenantOnboardingService.DidNotReceive().CompleteAsync(Arg.Any<TenantPolicySettingsDto>());
        await Assert.That(cut.Markup).DoesNotContain("Required tenant settings");
    }

    [Test]
    public async Task EmptyTenantIdentifier_FailsClosedWithoutSettingsCall()
    {
        _tenantOnboardingService.GetStatusAsync().Returns(CreateStatus(Guid.Empty));

        IRenderedComponent<TenantOnboarding> cut = Render();

        cut.WaitForAssertion(() => RequireAlertContains(cut, "No trusted tenant context"));
        _ = _tenantOnboardingService.DidNotReceive().GetSettingsAsync();
        _ = _tenantOnboardingService.DidNotReceive().CompleteAsync(Arg.Any<TenantPolicySettingsDto>());
        await Assert.That(cut.Markup).DoesNotContain("Required tenant settings");
    }

    [Test]
    public async Task UnauthenticatedServerStatus_FailsClosed()
    {
        _tenantOnboardingService.GetStatusAsync().Returns(CreateStatus(
            Guid.NewGuid(),
            isAuthenticated: false));

        IRenderedComponent<TenantOnboarding> cut = Render();

        cut.WaitForAssertion(() => RequireAlertContains(cut, "authenticated session could not be verified"));
        _ = _tenantOnboardingService.DidNotReceive().GetSettingsAsync();
        await Assert.That(cut.Markup).DoesNotContain("Complete tenant onboarding");
    }

    [Test]
    public async Task UnauthorizedServerStatus_FailsClosedWithoutUsingLocalRoles()
    {
        _tenantOnboardingService.GetStatusAsync().Returns(CreateStatus(
            Guid.NewGuid(),
            isTenantAdministrator: false,
            isPlatformAdministrator: false));

        IRenderedComponent<TenantOnboarding> cut = Render();

        cut.WaitForAssertion(() => RequireAlertContains(cut, "not authorized to manage onboarding"));
        _ = _tenantOnboardingService.DidNotReceive().GetSettingsAsync();
        _ = _tenantOnboardingService.DidNotReceive().CompleteAsync(Arg.Any<TenantPolicySettingsDto>());
        await Assert.That(cut.Markup).DoesNotContain("Required tenant settings");
    }

    [Test]
    public async Task MissingCompletionAffordance_FailsClosedWithoutLoadingSettings()
    {
        _tenantOnboardingService.GetStatusAsync().Returns(CreateStatus(
            Guid.NewGuid(),
            includeAffordances: false));

        IRenderedComponent<TenantOnboarding> cut = Render();

        cut.WaitForAssertion(() => RequireAlertContains(cut, "did not expose permission to complete onboarding"));
        _ = _tenantOnboardingService.DidNotReceive().GetSettingsAsync();
        _ = _tenantOnboardingService.DidNotReceive().CompleteAsync(Arg.Any<TenantPolicySettingsDto>());
        await Assert.That(cut.Markup).DoesNotContain("Required tenant settings");
    }

    [Test]
    public void TenantAdministrator_RendersSingleColumnTasksAndLockedPolicyInputs()
    {
        Guid tenantId = Guid.NewGuid();
        _tenantOnboardingService.GetStatusAsync().Returns(CreateStatus(tenantId));
        _tenantOnboardingService.GetSettingsAsync().Returns(CreateSettingsModel(allOverridesAllowed: false));

        IRenderedComponent<TenantOnboarding> cut = Render();

        cut.WaitForAssertion(() =>
        {
            RequireContains(cut.Markup, "Trusted tenant context");
            RequireContains(cut.Markup, tenantId.ToString("D"));
            RequireContains(cut.Markup, "Confirm trusted tenant context");
            RequireContains(cut.Markup, "Set tenant policies and experience");
            RequireContains(cut.Markup, "Configure branding and domains after launch");
            RequireContains(cut.Markup, "Required");
            RequireContains(cut.Markup, "Optional");
            RequireContains(cut.Markup, "locked by the platform administrator");

            if (cut.FindAll("h1").Count != 1)
            {
                throw new InvalidOperationException("Tenant onboarding must render exactly one h1.");
            }

            if (cut.FindAll("input[disabled]").Count < 4)
            {
                throw new InvalidOperationException("Server-locked policy inputs must remain disabled.");
            }
        });

        _ = _tenantOnboardingService.Received(1).GetSettingsAsync();
    }

    [Test]
    public async Task AlreadyCompletedTenantAdministrator_ShowsCanonicalHandoffChoicesWithoutSettingsCall()
    {
        _tenantOnboardingService.GetStatusAsync().Returns(CreateStatus(
            Guid.NewGuid(),
            isCompleted: true));

        IRenderedComponent<TenantOnboarding> cut = Render();

        cut.WaitForAssertion(() =>
        {
            RequireContains(cut.Markup, "Tenant onboarding is complete");
            RequireContains(cut.Markup, "Open tenant settings");
            RequireContains(cut.Markup, "href=\"/settings/admin\"");
            RequireContains(cut.Markup, "href=\"/events\"");

            if (cut.FindAll("[role='status']").Count == 0)
            {
                throw new InvalidOperationException("Completion must be exposed as an accessible status.");
            }
        });

        _ = _tenantOnboardingService.DidNotReceive().GetSettingsAsync();
        await Assert.That(cut.Markup).DoesNotContain("Complete tenant onboarding</button>");
    }

    [Test]
    public async Task PlatformAdministratorOnly_CanCompleteAndUsesControlPlaneHandoff()
    {
        Guid tenantId = Guid.NewGuid();
        _tenantOnboardingService.GetStatusAsync().Returns(
            CreateStatus(
                tenantId,
                isTenantAdministrator: false,
                isPlatformAdministrator: true),
            CreateStatus(
                tenantId,
                isCompleted: true,
                isTenantAdministrator: false,
                isPlatformAdministrator: true));

        IRenderedComponent<TenantOnboarding> cut = Render();
        cut.WaitForAssertion(() => RequireContains(cut.Markup, "Complete tenant onboarding"));

        ClickButton(cut, "Complete tenant onboarding");

        cut.WaitForAssertion(() =>
        {
            RequireContains(cut.Markup, "Tenant onboarding is complete");
            RequireContains(cut.Markup, "Open control plane");
            RequireContains(cut.Markup, $"href=\"{ControlPlaneRoutes.Overview}\"");
            RequireContains(cut.Markup, "href=\"/events\"");
            RequireDoesNotContain(cut.Markup, "/settings/admin");
        });

        _ = _tenantOnboardingService.Received(1).CompleteAsync(Arg.Any<TenantPolicySettingsDto>());
        _ = _tenantOnboardingService.Received(2).GetStatusAsync();
        await Assert.That(cut.Markup).DoesNotContain("Required tenant settings");
    }

    [Test]
    public async Task SuccessfulCompletion_RefetchesStatusBeforeShowingCompletion()
    {
        Guid tenantId = Guid.NewGuid();
        _tenantOnboardingService.GetStatusAsync().Returns(
            CreateStatus(tenantId),
            CreateStatus(tenantId, isCompleted: true));

        IRenderedComponent<TenantOnboarding> cut = Render();
        cut.WaitForAssertion(() => RequireContains(cut.Markup, "Complete tenant onboarding"));

        ClickButton(cut, "Complete tenant onboarding");

        cut.WaitForAssertion(() => RequireContains(cut.Markup, "Tenant onboarding is complete"));
        _ = _tenantOnboardingService.Received(2).GetStatusAsync();
        _ = _tenantOnboardingService.Received(1).GetSettingsAsync();
        _ = _tenantOnboardingService.Received(1).CompleteAsync(Arg.Is<TenantPolicySettingsDto>(
            settings => settings.PreferredHomePage == "EventList"));
        _ = _announcer.Received(1).AnnouncePoliteAsync(
            "Tenant onboarding completed and confirmed by the server.");
        await Assert.That(cut.FindAll("[role='status']").Count).IsGreaterThan(0);
    }

    [Test]
    public async Task SuccessfulCompletion_WithDifferentTenantContext_FailsClosed()
    {
        Guid submittedTenantId = Guid.NewGuid();
        Guid differentTenantId = Guid.NewGuid();
        const string safeMessage = "The completion request succeeded, but the server has not confirmed completion. Refresh and try again.";
        _tenantOnboardingService.GetStatusAsync().Returns(
            CreateStatus(submittedTenantId),
            CreateStatus(differentTenantId, isCompleted: true));

        IRenderedComponent<TenantOnboarding> cut = Render();
        cut.WaitForAssertion(() => RequireContains(cut.Markup, "Complete tenant onboarding"));

        ClickButton(cut, "Complete tenant onboarding");

        cut.WaitForAssertion(() =>
        {
            RequireAlertContains(cut, safeMessage);
            RequireDoesNotContain(cut.Markup, "Tenant onboarding is complete");
            RequireDoesNotContain(cut.Markup, "/settings/admin");
            RequireDoesNotContain(cut.Markup, ControlPlaneRoutes.Overview);
        });

        _ = _tenantOnboardingService.Received(2).GetStatusAsync();
        _ = _tenantOnboardingService.Received(1).CompleteAsync(Arg.Any<TenantPolicySettingsDto>());
        _ = _announcer.Received(1).AnnounceAssertiveAsync(safeMessage);
        _ = _announcer.DidNotReceive().AnnouncePoliteAsync(
            "Tenant onboarding completed and confirmed by the server.");
        await Assert.That(cut.FindAll("[role='alert']").Count).IsGreaterThan(0);
    }

    [Test]
    public async Task FailedCompletion_StaysOnPageAndAnnouncesSafeResponseMessage()
    {
        const string safeMessage = "The tenant policy request was rejected.";
        _tenantOnboardingService.GetStatusAsync().Returns(CreateStatus(Guid.NewGuid()));
        _tenantOnboardingService.CompleteAsync(Arg.Any<TenantPolicySettingsDto>())
            .Returns(new BaseCommandResponseOfGuid { Success = false, Message = safeMessage });

        IRenderedComponent<TenantOnboarding> cut = Render();
        cut.WaitForAssertion(() => RequireContains(cut.Markup, "Complete tenant onboarding"));

        ClickButton(cut, "Complete tenant onboarding");

        cut.WaitForAssertion(() =>
        {
            RequireAlertContains(cut, safeMessage);
            RequireContains(cut.Markup, "Required tenant settings");
            RequireContains(cut.Markup, "Complete tenant onboarding");
        });

        _ = _tenantOnboardingService.Received(1).GetStatusAsync();
        _ = _announcer.Received(1).AnnounceAssertiveAsync(safeMessage);
        await Assert.That(cut.Markup).DoesNotContain("Tenant onboarding is complete");
    }

    [Test]
    public async Task DirectoryIdentityFailureCode_IsTranslatedBeforeRenderingAndAnnouncement()
    {
        const string code = TenantOnboardingService.DirectoryOperatorIdentityUnavailableCode;
        _tenantOnboardingService.GetStatusAsync().Returns(CreateStatus(Guid.NewGuid()));
        _tenantOnboardingService.CompleteAsync(Arg.Any<TenantPolicySettingsDto>())
            .Returns(new BaseCommandResponseOfGuid { Success = false, Message = code });

        IRenderedComponent<TenantOnboarding> cut = Render();
        cut.WaitForAssertion(() => RequireContains(cut.Markup, "Complete tenant onboarding"));

        ClickButton(cut, "Complete tenant onboarding");

        cut.WaitForAssertion(() =>
        {
            RequireDoesNotContain(cut.Markup, code);
            if (!cut.FindAll("[role='alert']").Any(alert => !string.IsNullOrWhiteSpace(alert.TextContent)))
            {
                throw new InvalidOperationException("Expected a translated non-empty onboarding alert.");
            }
        });
        _ = _announcer.Received(1).AnnounceAssertiveAsync(
            Arg.Is<string>(message =>
                !string.IsNullOrWhiteSpace(message)
                && !string.Equals(message, code, StringComparison.Ordinal)));
    }

    [Test]
    public async Task Refresh_RefetchesStatusAndSettingsAndDeduplicatesOverlap()
    {
        Guid tenantId = Guid.NewGuid();
        _tenantOnboardingService.GetStatusAsync().Returns(CreateStatus(tenantId));
        IRenderedComponent<TenantOnboarding> cut = Render();
        cut.WaitForAssertion(() => RequireContains(cut.Markup, "Required tenant settings"));

        _tenantOnboardingService.ClearReceivedCalls();
        _announcer.ClearReceivedCalls();
        var statusGate = new TaskCompletionSource<TenantOnboardingStatusDto?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _tenantOnboardingService.GetStatusAsync().Returns(statusGate.Task);

        MethodInfo refreshMethod = typeof(TenantOnboarding).GetMethod(
            "RefreshAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Tenant onboarding refresh method was not found.");

        Task? firstRefresh = null;
        Task? overlappingRefresh = null;
        await cut.InvokeAsync(() =>
        {
            firstRefresh = (Task)refreshMethod.Invoke(cut.Instance, null)!;
            overlappingRefresh = (Task)refreshMethod.Invoke(cut.Instance, null)!;
        });

        _ = _tenantOnboardingService.Received(1).GetStatusAsync();
        _ = _tenantOnboardingService.DidNotReceive().GetSettingsAsync();

        statusGate.SetResult(CreateStatus(tenantId));
        await Task.WhenAll(firstRefresh!, overlappingRefresh!);

        _ = _tenantOnboardingService.Received(1).GetStatusAsync();
        _ = _tenantOnboardingService.Received(1).GetSettingsAsync();
        _ = _announcer.Received(1).AnnouncePoliteAsync("Tenant onboarding refreshed.");
        await Assert.That(cut.Markup).Contains("Refresh");
    }

    [Test]
    public async Task DisposedPage_IgnoresLateRefreshResult()
    {
        Guid tenantId = Guid.NewGuid();
        _tenantOnboardingService.GetStatusAsync().Returns(CreateStatus(tenantId));
        IRenderedComponent<TenantOnboarding> cut = Render();
        cut.WaitForAssertion(() => RequireContains(cut.Markup, "Required tenant settings"));

        _tenantOnboardingService.ClearReceivedCalls();
        _announcer.ClearReceivedCalls();
        var statusGate = new TaskCompletionSource<TenantOnboardingStatusDto?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _tenantOnboardingService.GetStatusAsync().Returns(statusGate.Task);
        MethodInfo refreshMethod = typeof(TenantOnboarding).GetMethod(
            "RefreshAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Tenant onboarding refresh method was not found.");

        Task? refresh = null;
        await cut.InvokeAsync(() =>
        {
            refresh = (Task)refreshMethod.Invoke(cut.Instance, null)!;
        });
        cut.Instance.Dispose();
        cut.Dispose();
        statusGate.SetResult(CreateStatus(tenantId));
        await refresh!;

        _ = _tenantOnboardingService.Received(1).GetStatusAsync();
        _ = _tenantOnboardingService.DidNotReceive().GetSettingsAsync();
        _ = _announcer.DidNotReceive().AnnouncePoliteAsync(Arg.Any<string>());
    }

    private IRenderedComponent<TenantOnboarding> Render() =>
        _ctx.RenderMudComponent<TenantOnboarding>();

    private static TenantOnboardingStatusDto CreateStatus(
        Guid tenantId,
        bool isCompleted = false,
        bool isAuthenticated = true,
        bool isTenantAdministrator = true,
        bool isPlatformAdministrator = false,
        bool includeAffordances = true)
    {
        var status = new TenantOnboardingStatusDto
        {
            IsCompleted = isCompleted,
            IsAuthenticated = isAuthenticated,
            IsCurrentUserTenantAdministrator = isTenantAdministrator,
            IsCurrentUserPlatformAdministrator = isPlatformAdministrator,
            TenantId = tenantId
        };

        if (!includeAffordances || !isAuthenticated || tenantId == Guid.Empty)
        {
            return status;
        }

        var relations = new List<string>();
        if (isTenantAdministrator)
        {
            relations.Add("manage-tenant-settings");
            if (!isCompleted)
            {
                relations.Add("complete");
            }
        }

        if (isPlatformAdministrator)
        {
            relations.Add("manage-control-plane");
            if (!isCompleted && !isTenantAdministrator)
            {
                relations.Add("complete");
            }
        }

        status.AdditionalProperties["_links"] = JsonSerializer.SerializeToElement(
            relations.ToDictionary(relation => relation, _ => new { href = "/" }));
        return status;
    }

    private static TenantPolicySettingsDto CreateSettingsModel(bool allOverridesAllowed = true) =>
        new()
        {
            AllowUserSubmittedEvents = true,
            RequireEventApproval = true,
            RequireOrganizationVerification = true,
            EventCardClickOpensDetailPage = true,
            PreferredHomePage = "EventList",
            InstanceBaseDomain = "events.example.test",
            CanOverrideHomePagePreference = allOverridesAllowed,
            CanOverrideEventCardClickBehavior = allOverridesAllowed,
            CanOverrideSubdomain = allOverridesAllowed,
            CanOverrideCustomDomain = allOverridesAllowed,
            CanTenantOmitVerification = allOverridesAllowed
        };

    private static void ClickButton(IRenderedComponent<TenantOnboarding> cut, string text)
    {
        IElement? button = cut.FindAll("button")
            .FirstOrDefault(element => element.TextContent.Contains(text, StringComparison.OrdinalIgnoreCase));

        if (button is null)
        {
            throw new InvalidOperationException($"Button containing '{text}' was not found.");
        }

        button.Click();
    }

    private static void RequireAlertContains(IRenderedComponent<TenantOnboarding> cut, string expected)
    {
        IElement? alert = cut.FindAll("[role='alert']")
            .FirstOrDefault(element => element.TextContent.Contains(expected, StringComparison.OrdinalIgnoreCase));

        if (alert is null)
        {
            throw new InvalidOperationException($"Expected alert containing '{expected}' was not rendered.");
        }
    }

    private static void RequireContains(string value, string expected)
    {
        if (!value.Contains(expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Expected markup to contain '{expected}'.");
        }
    }

    private static void RequireDoesNotContain(string value, string unexpected)
    {
        if (value.Contains(unexpected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Expected markup not to contain '{unexpected}'.");
        }
    }
}

using System.Text.Json;
using AngleSharp.Dom;
using Explore.Blazor.Client.Models.Responses;
using Explore.Blazor.Client.Pages.Onboarding;
using Explore.Blazor.Client.Pages.Onboarding.Components;
using Explore.Blazor.Client.Routing.ControlPlane;
using Microsoft.AspNetCore.Components.Web;

namespace Explore.Blazor.Client.Tests.Pages.Onboarding;

public class InstanceOnboardingTests : IDisposable
{
    private readonly BlazorTestContext _ctx;
    private readonly IInstanceOnboardingService _instanceOnboardingService;
    private readonly IUserService _userService;
    private readonly IBffAuthApi _bffAuthApi;
    private readonly IInstanceOperatorIdentityAdminService _operatorIdentityAdminService;
    private string _currentDeploymentMode = "SingleTenant";
    private HalResourceOfInstanceOnboardingJourneyDto? _journey;
    private bool _identityReady = true;

    public InstanceOnboardingTests()
    {
        _ctx = new BlazorTestContext();
        _ctx.SetAuthenticatedUser(Guid.NewGuid(), "Setup Admin");

        _instanceOnboardingService = Substitute.For<IInstanceOnboardingService>();
        _userService = Substitute.For<IUserService>();

        _ctx.Services.AddSingleton(_instanceOnboardingService);
        _ctx.Services.AddSingleton(_userService);
        _ctx.Services.AddSingleton(Substitute.For<ILogger<InstanceOnboarding>>());

        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(new OkHttpHandler())
        {
            BaseAddress = new Uri("https://localhost/")
        });
        _ctx.Services.AddSingleton(httpClientFactory);
        _bffAuthApi = _ctx.Services.GetRequiredService<IBffAuthApi>();
        _operatorIdentityAdminService = _ctx.Services.GetRequiredService<IInstanceOperatorIdentityAdminService>();

        _userService.SyncUserAsync().Returns(new BaseCommandResponseOfGuid { Success = true });
        _userService.GetCurrentUserAsync().Returns(new UserDto
        {
            Email = "setup-admin@example.com",
            FirstName = "Setup",
            LastName = "Admin"
        });

        _instanceOnboardingService.CompleteAsync(Arg.Any<CompleteInstanceOnboardingRequest>())
            .Returns(_ =>
            {
                _journey!.Bootstrap = CreateStatus(isCompleted: true, _currentDeploymentMode);
                _instanceOnboardingService.GetStatusAsync().Returns(_journey.Bootstrap);
                _journey._links = JsonSerializer.Deserialize<Dictionary<string, HalLink>>(
                    ((JsonElement)_journey.Bootstrap.AdditionalProperties["_links"]).GetRawText());
                return new BaseCommandResponseOfGuid
                {
                    Success = true,
                    Message = "ok"
                };
            });
        _instanceOnboardingService.RefreshAuthSessionAsync().Returns(true);
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task PrivateCompletion_DoesNotRequireLegalIdentity()
    {
        _identityReady = false;
        var cut = RenderForDeploymentMode("SingleTenant");

        await Assert.That(cut.FindAll("#operator-legal-name").Count).IsEqualTo(0);
        await Assert.That(cut.Find("button[type='submit']").HasAttribute("disabled")).IsFalse();
    }

    [Test]
    public async Task Refresh_UsesOnlyTheJourneySnapshotAndRefreshesSavedProfile()
    {
        var journey = new HalResourceOfInstanceOnboardingJourneyDto
        {
            State = "Available", Generation = "first",
            Bootstrap = CreateStatus(false, "MultiTenant"),
            Profile = new SelfHostOnboardingProfileDto { SiteName = "Journey site" },
            Authentication = new OnboardingProviderReadinessDto { State = "Ready" },
            Authorization = new OnboardingProviderReadinessDto { State = "Ready" },
            Preflight = CreatePreflight("MultiTenant"),
            _links = new Dictionary<string, HalLink> { ["save-profile"] = new() { Href = "/api/instanceonboarding/profile", Method = "PATCH" } }
        };
        var requested = new List<string>();
        _instanceOnboardingService.GetJourneyAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            requested.Add("journey");
            return Task.FromResult<HalResourceOfInstanceOnboardingJourneyDto?>(journey);
        });
        _instanceOnboardingService.SaveProfileAsync(Arg.Any<SelfHostOnboardingProfileDto>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            requested.Add("save-profile");
            var saved = call.Arg<SelfHostOnboardingProfileDto>();
            journey.Profile = new SelfHostOnboardingProfileDto { SiteName = saved.SiteName, CanonicalUrl = saved.CanonicalUrl };
            journey.Generation = "after-save";
            return Task.FromResult(new BaseCommandResponseOfGuid { Success = true });
        });
        SetupBffJsModule(true);
        var cut = _ctx.RenderMudComponent<InstanceOnboarding>();
        await cut.InvokeAsync(() => Task.CompletedTask);
        await Assert.That(cut.FindAll("input").Any(input => input.GetAttribute("value") == "Journey site")).IsTrue();
        await cut.InvokeAsync(() => cut.Instance.RefreshAsync());
        await cut.Find("button[data-testid='save-onboarding-profile']").ClickAsync(new MouseEventArgs());
        await Assert.That(requested).IsEquivalentTo(new[] { "journey", "journey", "save-profile", "journey" });
        await _instanceOnboardingService.DidNotReceive().GetStatusAsync();
        await _instanceOnboardingService.DidNotReceive().GetSystemOnboardingStatusAsync();
        await _instanceOnboardingService.DidNotReceive().GetBrandingSettingsAsync();
        await _instanceOnboardingService.DidNotReceive().GetOnboardingPreflightAsync();
        await _instanceOnboardingService.DidNotReceive().GetAuthProviderConfiguredStateAsync();
        await _instanceOnboardingService.DidNotReceive().GetAuthorizationProviderConfiguredStateAsync();
        await _operatorIdentityAdminService.DidNotReceive().GetAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Workspace_HasOneH1AndContinuesWithoutStandaloneOverview()
    {
        var cut = RenderForDeploymentMode("SingleTenant");

        Require(cut.FindComponents<PageTitle>().Count == 1, "Expected exactly one PageTitle component.");
        Require(cut.FindComponents<OnboardingWorkspace>().Count == 1, "Expected the shared onboarding workspace.");
        Require(cut.FindAll("h1").Count == 1, "Expected exactly one h1.");
        RequireContains(cut.Find("h1").TextContent, "Site profile and launch");
        RequireNotContains(cut.Markup, "Setup Overview");
        RequireNotContains(cut.Markup, "instance-onboarding__header");
        RequireContains(cut.Markup, "Instance onboarding progress");
        RequireNotContains(cut.Markup, "Launch Recap");
        RequireNotContains(cut.Markup, "Administration Access");
        RequireNotContains(cut.Markup, "Dedicated Admin Host");

        await Task.CompletedTask;
    }

    [Test]
    public async Task SingleTenant_RendersRequiredTasksWithNativeFragmentActions()
    {
        var cut = RenderForDeploymentMode("SingleTenant");

        RequireContains(cut.Markup, "Site profile");
        RequireContains(cut.Markup, "Authentication provider");
        RequireContains(cut.Markup, "Authorization provider");
        RequireContains(cut.Markup, "Launch readiness");
        RequireContains(cut.Markup, "Required");
        RequireContains(cut.Markup, "Instance operator scope");
        RequireContains(cut.Markup, "Server-evaluated instance scope");
        Require(cut.FindAll("a").Any(link => link.GetAttribute("href") == "#site-profile"), "Expected site profile fragment action.");
        Require(cut.FindAll("a").Any(link => link.GetAttribute("href") == "#launch-readiness"), "Expected readiness fragment action.");
        RequireNotContains(cut.Markup, "First tenant");

        await Task.CompletedTask;
    }

    [Test]
    public async Task MultiTenant_RendersOptionalFirstTenantAndReadOnlyDeploymentContext()
    {
        var cut = RenderForDeploymentMode("MultiTenant");

        RequireContains(cut.Markup, "Multi tenant");
        RequireContains(cut.Markup, "First tenant");
        RequireContains(cut.Markup, "Optional");
        RequireContains(cut.Markup, "never blocks instance launch");
        RequireContains(cut.Markup, "Tenant scope");
        RequireNotContains(cut.Markup, "Embedded admin area");
        RequireNotContains(cut.Markup, "Dedicated admin hostname");

        await Task.CompletedTask;
    }

    [Test]
    public async Task ConfiguredAuthenticationProvider_KeepsManagementActionWithoutImplyingIncompleteState()
    {
        var cut = RenderForDeploymentMode(
            "SingleTenant",
            authenticationConfigured: true,
            authorizationConfigured: true);

        RequireContains(cut.Markup, "Authentication provider");
        RequireContains(cut.Markup, "Manage authentication");
        Require(cut.FindAll("a").Any(link => link.GetAttribute("href") == "/onboarding/auth-provider"),
            "Configured authentication must retain access to realm management.");
        RequireNotContains(cut.Markup, "Configure authentication");
        Require(!cut.Find("button[type='submit']").HasAttribute("disabled"),
            "A completed authentication task must not block launch.");

        await Task.CompletedTask;
    }

    [Test]
    public async Task CompletedInstance_KeepsAuthenticationRealmManagementInAdminProviderSettings()
    {
        var cut = RenderForDeploymentMode("SingleTenant");

        await cut.Find("form").SubmitAsync();
        Require(FindLink(cut, "/settings/instance?section=auth-providers") is not null,
            "Completed setup must retain the HAL-authorized provider management route.");

        await Task.CompletedTask;
    }

    [Test]
    public async Task ProviderAndPreflightState_MapToActionRequiredAndBlockedTasks()
    {
        var preflight = CreatePreflight(
            "SingleTenant",
            blockingStatus: "Fail",
            blockingMessage: "Setup secret is missing or invalid.");
        var cut = RenderForDeploymentMode(
            "SingleTenant",
            preflight,
            authenticationConfigured: false,
            authorizationConfigured: true);

        RequireContains(cut.Markup, "Configure authentication");
        RequireContains(cut.Markup, "/onboarding/auth-provider");
        RequireContains(cut.Markup, "Action required");
        RequireContains(cut.Markup, "Setup secret is missing or invalid.");
        RequireContains(cut.Markup, "Blocked");
        RequireNotContains(cut.Markup, "Configure authorization");

        await Task.CompletedTask;
    }

    [Test]
    public async Task RequiredBlocker_DisablesCompletion()
    {
        var cut = RenderForDeploymentMode(
            "SingleTenant",
            CreatePreflight("SingleTenant", blockingStatus: "Fail"));

        Require(cut.Find("button[type='submit']").HasAttribute("disabled"), "Expected blocker to disable completion.");
        await _instanceOnboardingService.DidNotReceive()
            .CompleteAsync(Arg.Any<CompleteInstanceOnboardingRequest>());
    }

    [Test]
    public async Task OrdinaryWarning_IsNonBlockingAndSingleTenantUsesPrivateHandoff()
    {
        var cut = RenderForDeploymentMode("SingleTenant");

        Require(!cut.Find("button[type='submit']").HasAttribute("disabled"), "Ordinary warning must remain non-blocking.");
        await cut.Find("form").SubmitAsync();
        Require(FindLink(cut, "/settings/instance?section=getting-started") is not null, "Expected private handoff.");
        Require(FindLink(cut, "/events") is null, "Completion must not require public access.");

        await _instanceOnboardingService.Received(1)
            .CompleteAsync(Arg.Any<CompleteInstanceOnboardingRequest>());
    }

    [Test]
    public async Task SeriousWarning_RequiresAcknowledgementBeforeCompletion()
    {
        var preflight = CreatePreflight(
            "SingleTenant",
            warningSeverity: "Critical",
            warningMessage: "The site will be publicly accessible.");
        var cut = RenderForDeploymentMode("SingleTenant", preflight);

        RequireContains(cut.Markup, "Required acknowledgement");
        Require(cut.Find("button[type='submit']").HasAttribute("disabled"), "Serious warning must require acknowledgement.");
        await cut.Find("input[type='checkbox']").ChangeAsync(new ChangeEventArgs { Value = true });
        Require(!cut.Find("button[type='submit']").HasAttribute("disabled"), "Acknowledgement should enable completion.");
        await cut.Find("form").SubmitAsync();

        await _instanceOnboardingService.Received(1)
            .CompleteAsync(Arg.Any<CompleteInstanceOnboardingRequest>());
    }

    [Test]
    public async Task MultiTenantCompletion_UsesControlPlaneHandoffAndLeavesAdminFieldsAtDefaults()
    {
        var requestDefaults = new CompleteInstanceOnboardingRequest();
        var cut = RenderForDeploymentMode("MultiTenant");

        await cut.Find("form").SubmitAsync();
        Require(FindLink(cut, "/settings/instance?section=getting-started") is not null, "Expected private handoff.");
        Require(FindLink(cut, ControlPlaneRoutes.Tenants) is not null, "Expected optional tenant handoff.");

        await _instanceOnboardingService.Received(1).CompleteAsync(
            Arg.Is<CompleteInstanceOnboardingRequest>(request =>
                request != null
                && request.SiteProfile != null
                && request.SiteProfile.SiteName == "ISLAMU Explore"
                && request.ExpectedJourneyGeneration == "fixture"
                && request.AdministrationAccessMode == "Embedded"
                && request.AdminHost == requestDefaults.AdminHost));
    }

    [Test]
    public async Task MultiTenantFirstTenant_RemainsOptionalAndDoesNotBlockLaunch()
    {
        var cut = RenderForDeploymentMode("MultiTenant");

        RequireContains(cut.Markup, "Post-launch");
        Require(!cut.Find("button[type='submit']").HasAttribute("disabled"), "Optional tenant task must not block completion.");
        await cut.Find("form").SubmitAsync();

        await _instanceOnboardingService.Received(1)
            .CompleteAsync(Arg.Any<CompleteInstanceOnboardingRequest>());
    }

    [Test]
    public async Task Completion_RefreshesBffAuthBeforeAndAfterMutation()
    {
        var cut = RenderForDeploymentMode("SingleTenant");

        await cut.Find("form").SubmitAsync();

        await _instanceOnboardingService.Received(2).RefreshAuthSessionAsync();
        await _instanceOnboardingService.Received(1)
            .CompleteAsync(Arg.Any<CompleteInstanceOnboardingRequest>());
        await Assert.That(_ctx.JSInterop.Invocations.Count(invocation => invocation.Identifier == "syncSetupSecret"))
            .IsEqualTo(2);
    }

    [Test]
    public async Task CompletionFailure_RendersOnlySafeServiceResponseDetails()
    {
        _instanceOnboardingService.CompleteAsync(Arg.Any<CompleteInstanceOnboardingRequest>())
            .Returns(new BaseCommandResponseOfGuid
            {
                Success = false,
                Message = "Invalid onboarding request.",
                Errors = ["A required launch check failed."]
            });
        var cut = RenderForDeploymentMode("SingleTenant");

        await cut.Find("form").SubmitAsync();
        RequireContains(cut.Find("[role='alert']").TextContent, "Invalid onboarding request.");
        RequireContains(cut.Find("[role='alert']").TextContent, "A required launch check failed.");

        await Task.CompletedTask;
    }

    [Test]
    [Arguments(400)]
    [Arguments(403)]
    [Arguments(410)]
    public async Task InvalidSetupSecret_DeletesBffStateAndRedirectsForReauthentication(int statusCode)
    {
        var nav = _ctx.Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();
        nav.NavigateTo("/onboarding/instance?section=launch");
        var cut = RenderForDeploymentMode(
            "SingleTenant",
            syncOk: false,
            syncFailureStatus: statusCode);

        cut.WaitForAssertion(() =>
            Require(
                nav.Uri.EndsWith("/setup?returnUrl=%2Fonboarding%2Finstance%3Fsection%3Dlaunch", StringComparison.Ordinal),
                $"Unexpected setup re-authentication URL: '{nav.Uri}'."));

        await _bffAuthApi.Received(1).DeleteSetupSecretAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task MissingAuthoritativeState_RendersSafeUnavailableStateAndBlocksCompletion()
    {
        var cut = RenderForDeploymentMode(
            "SingleTenant",
            statusAvailable: false,
            systemStatusAvailable: false,
            preflightAvailable: false);

        RequireContains(cut.Find("[role='alert']").TextContent, "authoritative setup state is unavailable");
        RequireContains(cut.Markup, "Unavailable");
        RequireContains(cut.Markup, "Launch readiness is unavailable");
        Require(!cut.FindAll("a").Any(link => link.GetAttribute("href") == "/onboarding/auth-provider"),
            "Missing authoritative state must not expose provider actions.");
        Require(cut.Find("button[type='submit']").HasAttribute("disabled"), "Missing state must disable completion.");
        await _instanceOnboardingService.DidNotReceive()
            .CompleteAsync(Arg.Any<CompleteInstanceOnboardingRequest>());
    }

    [Test]
    public async Task ProviderStatusFailure_FailsClosedWithoutExposingProviderOrCompletionActions()
    {
        var cut = RenderForDeploymentMode(
            "SingleTenant",
            authenticationConfigured: null,
            authorizationConfigured: true);

        RequireContains(cut.Find("[role='alert']").TextContent, "authoritative setup state is unavailable");
        RequireContains(cut.Markup, "Status unavailable");
        Require(!cut.FindAll("a").Any(link => link.GetAttribute("href") == "/onboarding/auth-provider"),
            "Unavailable provider status must not expose a setup action.");
        Require(cut.Find("button[type='submit']").HasAttribute("disabled"),
            "Unavailable provider status must disable completion.");
        await _instanceOnboardingService.DidNotReceive()
            .CompleteAsync(Arg.Any<CompleteInstanceOnboardingRequest>());
    }

    [Test]
    public async Task InitialLoad_UsesOneJourneyRequest()
    {
        RenderForDeploymentMode("SingleTenant");

        await AssertAuthoritativeCallCountAsync(1);
    }

    [Test]
    public async Task ExplicitRefresh_UsesOneAdditionalJourneyRequest()
    {
        var cut = RenderForDeploymentMode("SingleTenant");

        await cut.Find("button[aria-label='Refresh setup status']").ClickAsync(new MouseEventArgs());

        await AssertAuthoritativeCallCountAsync(2);
    }

    [Test]
    public async Task OverlappingRefreshes_ShareOneInFlightAuthoritativeCallSet()
    {
        var cut = RenderForDeploymentMode("SingleTenant");
        var statusGate = new TaskCompletionSource<HalResourceOfInstanceOnboardingJourneyDto?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _instanceOnboardingService.GetJourneyAsync(Arg.Any<CancellationToken>()).Returns(_ => statusGate.Task);

        var firstRefresh = cut.Instance.RefreshAsync();
        var overlappingRefresh = cut.Instance.RefreshAsync();

        Require(ReferenceEquals(firstRefresh, overlappingRefresh), "Overlapping refreshes should share one task.");
        await _instanceOnboardingService.Received(2).GetJourneyAsync(Arg.Any<CancellationToken>());

        statusGate.SetResult(_journey);
        await Task.WhenAll(firstRefresh, overlappingRefresh).WaitAsync(TimeSpan.FromSeconds(5));

        await AssertAuthoritativeCallCountAsync(2);
    }

    [Test]
    public async Task OperatorIdentityIncomplete_DoesNotBlockPrivateCompletion()
    {
        _identityReady = false;

        var cut = RenderForDeploymentMode("SingleTenant");

        Require(!cut.Find("button[type='submit']").HasAttribute("disabled"),
            "An incomplete legal identity must not block private administration.");
        await Task.CompletedTask;
    }

    [Test]
    public async Task SingleTenant_CompletionDoesNotCopyLegalFactsIntoDirectoryIdentity()
    {
        var cut = RenderForDeploymentMode("SingleTenant");

        await Assert.That(cut.FindAll("[data-testid='copy-to-directory-identity']").Count).IsEqualTo(0);
        await Assert.That(cut.FindAll("#operator-public-name").Count).IsEqualTo(0);
        await Assert.That(cut.FindAll("#operator-legal-name").Count).IsEqualTo(0);
    }

    private IRenderedComponent<InstanceOnboarding> RenderForDeploymentMode(
        string deploymentMode,
        OnboardingPreflightDto? preflight = null,
        bool? authenticationConfigured = true,
        bool? authorizationConfigured = true,
        bool syncOk = true,
        bool statusAvailable = true,
        bool systemStatusAvailable = true,
        bool preflightAvailable = true,
        int syncFailureStatus = 400)
    {
        _currentDeploymentMode = deploymentMode;
        var status = CreateStatus(false, deploymentMode);
        _journey = statusAvailable && systemStatusAvailable && preflightAvailable
            && authenticationConfigured.HasValue && authorizationConfigured.HasValue ? new()
        {
            State = "Available", Generation = "fixture", Bootstrap = status,
            Profile = new() { SiteName = "ISLAMU Explore" },
            Authentication = new() { State = authenticationConfigured == true ? "Ready" : "ActionRequired" },
            Authorization = new() { State = authorizationConfigured == true ? "Ready" : "ActionRequired" },
            Preflight = preflight ?? CreatePreflight(deploymentMode),
            OperatorIdentity = new()
            {
                PublicName = "ISLAMU Explore", LegalName = "ISLAMU Explore", OperatorKindCode = "individual",
                PaidCommerce = new() { IsReady = _identityReady, ReasonCodes = [] },
                PublicDisclosure = new() { IsReady = true, ReasonCodes = [] }
            },
            _links = JsonSerializer.Deserialize<Dictionary<string, HalLink>>(((JsonElement)status.AdditionalProperties["_links"]).GetRawText())
        } : null;
        if (_journey is not null) _journey._links!["update-operator-identity"] = new() { Href = "/api/instance-operator-identity", Method = "PUT" };
        _instanceOnboardingService.GetJourneyAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(_journey));

        SetupBffJsModule(syncOk, syncFailureStatus);

        var cut = _ctx.RenderMudComponent<InstanceOnboarding>();
        cut.WaitForAssertion(() =>
        {
            RequireContains(cut.Markup, "Launch readiness");
            Require(!cut.Find("button[aria-label='Refresh setup status']").HasAttribute("disabled"), "Refresh should be enabled after load.");
        });

        return cut;
    }

    private void SetupBffJsModule(bool syncOk, int syncFailureStatus = 400)
    {
        var module = _ctx.JSInterop.SetupModule("/js/bff.js");
        module.Setup<BffMutationResult>("syncSetupSecret", _ => true)
            .SetResult(new BffMutationResult
            {
                Ok = syncOk,
                Status = syncOk ? 200 : syncFailureStatus,
                Error = syncOk ? null : "Sync failed."
            });
    }

    private static OnboardingPreflightDto CreatePreflight(
        string deploymentMode,
        string blockingStatus = "Pass",
        string blockingMessage = "Setup secret is active.",
        string warningSeverity = "Warning",
        string warningMessage = "SMTP can be configured after launch.") =>
        new()
        {
            DeploymentMode = deploymentMode,
            IsReadyToLaunch = string.Equals(blockingStatus, "Pass", StringComparison.OrdinalIgnoreCase),
            BlockingChecks =
            [
                new OnboardingPreflightCheckDto
                {
                    Code = "setup_secret",
                    Name = "Setup Secret",
                    Severity = "Blocking",
                    Status = blockingStatus,
                    Message = blockingMessage
                }
            ],
            WarningChecks =
            [
                new OnboardingPreflightCheckDto
                {
                    Code = "smtp",
                    Name = "SMTP",
                    Severity = warningSeverity,
                    Status = "Warning",
                    Message = warningMessage
                }
            ]
        };

    private static InstanceOnboardingStatusDto CreateStatus(bool isCompleted, string deploymentMode)
    {
        var relations = new List<string>
        {
            "manage-authentication",
            "manage-authorization"
        };
        if (isCompleted && string.Equals(deploymentMode, "MultiTenant", StringComparison.OrdinalIgnoreCase))
        {
            relations.Add("manage-tenants");
        }
        else if (!isCompleted)
        {
            relations.Add("complete");
        }

        var status = new InstanceOnboardingStatusDto
        {
            IsCompleted = isCompleted,
            IsAuthenticated = true,
            IsCurrentUserInstanceAdmin = isCompleted,
            SelectedDeploymentMode = deploymentMode
        };
        status.AdditionalProperties["_links"] = JsonSerializer.SerializeToElement(
            relations.ToDictionary(relation => relation, _ => new { href = "/" }));
        return status;
    }

    private async Task AssertAuthoritativeCallCountAsync(int count)
    {
        await _instanceOnboardingService.Received(count).GetJourneyAsync(Arg.Any<CancellationToken>());
        await _instanceOnboardingService.DidNotReceive().GetStatusAsync();
        await _instanceOnboardingService.DidNotReceive().GetSystemOnboardingStatusAsync();
        await _instanceOnboardingService.DidNotReceive().GetBrandingSettingsAsync();
        await _instanceOnboardingService.DidNotReceive().GetAuthProviderConfiguredStateAsync();
        await _instanceOnboardingService.DidNotReceive().GetAuthorizationProviderConfiguredStateAsync();
        await _instanceOnboardingService.DidNotReceive().GetOnboardingPreflightAsync();
    }

    private static IElement FindButton(IRenderedComponent<InstanceOnboarding> cut, string text) =>
        cut.FindAll("button").Single(button =>
            button.TextContent.Contains(text, StringComparison.OrdinalIgnoreCase));

    private static IElement? FindLink(IRenderedComponent<InstanceOnboarding> cut, string href) =>
        cut.FindAll("a").FirstOrDefault(link => link.GetAttribute("href") == href);

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireContains(string actual, string expected)
    {
        if (!actual.Contains(expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Expected content to contain '{expected}'.");
        }
    }

    private static void RequireNotContains(string actual, string expected)
    {
        if (actual.Contains(expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Expected content not to contain '{expected}'.");
        }
    }

    private sealed class OkHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}


using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Bunit.TestDoubles;
using Explore.Blazor.Client.Pages.Onboarding;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Refit;

namespace Explore.Blazor.Client.Tests.Pages.Onboarding;

public sealed class LocalInstanceOnboardingTests
{
    [Test]
    public async Task LocalSetupRendersWithoutOrdinaryUserHydration()
    {
        using var fixture = new Fixture();
        var cut = fixture.Context.RenderMudComponent<InstanceOnboarding>();
        await Assert.That(cut.FindAll("input[autocomplete=username][type=text]").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("input[autocomplete=new-password]").Count).IsEqualTo(1);
        await Assert.That(fixture.Transport.OrdinarySessionWork).IsFalse();
        await Assert.That(cut.FindAll("#operator-public-name, #operator-legal-name, #operator-public-contact-email")).IsEmpty();
    }

    [Test]
    public async Task RecoveredLocalOperationSendsExplicitLegalIdentityAndNoCredentialEmail()
    {
        using var fixture = new Fixture();
        var cut = fixture.Context.RenderMudComponent<InstanceOnboarding>();
        Fill(cut);
        await cut.InvokeAsync(() => cut.FindComponent<EditForm>().Instance.OnValidSubmit.InvokeAsync());
        await Assert.That(fixture.Transport.Submitted).IsNotNull();
        var request = fixture.Transport.Submitted!;
        await Assert.That(request.OperationId).IsEqualTo(fixture.Transport.PendingOperationId);
        await Assert.That(request.Email).IsNull();
        await Assert.That(request.Settings!.SiteProfile!.Locale).IsEqualTo("en");
        await Assert.That(request.Settings.SiteProfile.TimeZone).IsEqualTo("UTC");
        await Assert.That(request.Settings.DirectoryOperatorIdentity).IsNull();
        await Assert.That(request.Settings.ExpectedJourneyGeneration).IsEqualTo("local-fixture");
        await Assert.That(request.Username).IsEqualTo("instance-operator");
        await Assert.That(fixture.Transport.OrdinarySessionWork).IsFalse();
        await Assert.That(cut.FindAll("input[type=password]")).IsEmpty();
        await Assert.That(cut.FindAll("a").Any(link => link.GetAttribute("href") == "/login?provider=local&returnUrl=%2Fsettings%2Finstance%3Fsection%3Dgetting-started")).IsTrue();
    }

    [Test]
    public async Task FailureClearsPasswordWithoutReplacingReservedOperation()
    {
        using var fixture = new Fixture();
        fixture.Transport.FailCompletion = true;
        var cut = fixture.Context.RenderMudComponent<InstanceOnboarding>();
        Fill(cut);
        await cut.InvokeAsync(() => cut.FindComponent<EditForm>().Instance.OnValidSubmit.InvokeAsync());
        await Assert.That(fixture.Transport.Submitted!.OperationId).IsEqualTo(fixture.Transport.PendingOperationId);
        await Assert.That(cut.Find("input[autocomplete=new-password]").GetAttribute("value") ?? "").IsEqualTo("");
        await Assert.That(fixture.Transport.OrdinarySessionWork).IsFalse();
    }

    [Test]
    public async Task MissingCompletionHalCannotSubmitLocalCredentials()
    {
        using var fixture = new Fixture();
        fixture.Transport.AllowCompletion = false;
        var cut = fixture.Context.RenderMudComponent<InstanceOnboarding>();
        await Assert.That(cut.Find("button[type=submit]").HasAttribute("disabled")).IsTrue();
        await cut.InvokeAsync(() => cut.FindComponent<EditForm>().Instance.OnValidSubmit.InvokeAsync());
        await Assert.That(fixture.Transport.Submitted).IsNull();
    }

    [Test]
    public async Task CancelIgnoresLateCompletionAndClearsTransientPassword()
    {
        using var fixture = new Fixture();
        fixture.Transport.PauseCompletion = true;
        var cut = fixture.Context.RenderMudComponent<InstanceOnboarding>();
        Fill(cut);
        Task submission = cut.InvokeAsync(() => cut.FindComponent<EditForm>().Instance.OnValidSubmit.InvokeAsync());
        await fixture.Transport.CompletionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cut.FindAll("button").Single(button => button.TextContent.Contains("Cancel Local setup", StringComparison.Ordinal))
            .ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        fixture.Transport.CompletionRelease.TrySetResult();
        await submission.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(cut.Find("input[autocomplete=new-password]").GetAttribute("value") ?? "").IsEqualTo("");
        await Assert.That(cut.FindAll("a[href^='/login?provider=local']")).IsEmpty();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ResponseLossRefreshUsesOnlyTerminalPublicStatus(bool terminalStatus)
    {
        using var fixture = new Fixture();
        fixture.Transport.LoseCompletionResponse = true;
        fixture.Transport.PublicStatusCompleted = terminalStatus;
        var forgotten = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Context.JSInterop.SetupModule("/js/bff.js")
            .Setup<Explore.Blazor.Client.Models.Responses.BffMutationResult>("deleteSetupSecret", invocation =>
            {
                fixture.Transport.SetupAuthorityPresent = false;
                forgotten.TrySetResult();
                return true;
            }).SetResult(new() { Ok = true, Status = 200 });
        var cut = fixture.Context.RenderMudComponent<InstanceOnboarding>();
        Fill(cut);
        var committed = fixture.Transport.CompletionCommitted.Task;
        var submission = cut.InvokeAsync(() => cut.FindComponent<EditForm>().Instance.OnValidSubmit.InvokeAsync());
        await committed.WaitAsync(TimeSpan.FromSeconds(5));
        await submission.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(cut.Find("input[autocomplete=new-password]").GetAttribute("value") ?? "").IsEqualTo("");
        await cut.Find("button[aria-label='Refresh setup status']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        await Assert.That(fixture.Transport.PublicStatusReads).IsEqualTo(1);
        await Assert.That(fixture.Transport.CompletionPosts).IsEqualTo(1);
        await Assert.That(fixture.Transport.Accounts).IsEqualTo(1);
        await Assert.That(fixture.Transport.Administrators).IsEqualTo(1);
        await Assert.That(cut.FindAll("input[type=password]")).IsEmpty();
        await Assert.That(cut.FindAll("a[href^='/login?provider=local']").Count).IsEqualTo(terminalStatus ? 1 : 0);
        if (terminalStatus) await forgotten.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(fixture.Transport.SetupAuthorityPresent).IsEqualTo(!terminalStatus);
        await Assert.That(fixture.Transport.OrdinarySessionWork).IsFalse();
    }

    private static void Fill(IRenderedComponent<InstanceOnboarding> cut)
    {
        cut.Find("input[autocomplete=username]").Change("instance-operator");
        cut.Find("input[autocomplete=new-password]").Change($"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(24))}");
    }

    private sealed class Fixture : IDisposable
    {
        internal BlazorTestContext Context { get; } = new();
        internal Transport Transport { get; } = new();
        private readonly HttpClient _client;
        internal Fixture()
        {
            _client = new HttpClient(Transport) { BaseAddress = new Uri("https://example.test") };
            var users = Substitute.For<IUserService>();
            users.SyncUserAsync().Returns<Task<BaseCommandResponseOfGuid>>(_ => { Transport.OrdinarySessionWork = true; throw new InvalidOperationException("Unexpected Local user hydration."); });
            users.GetCurrentUserAsync().Returns<Task<UserDto?>>(_ => { Transport.OrdinarySessionWork = true; throw new InvalidOperationException("Unexpected Local user hydration."); });
            Context.Services.AddSingleton(users);
            var auth = RestService.For<IBffAuthApi>(_client);
            Context.Services.AddSingleton<IInstanceOnboardingService>(services => new InstanceOnboardingService(
                new InstanceAuthenticationSettingsClient(_client), new InstanceKeycloakOperationsClient(_client),
                new InstanceAuthorizationSettingsClient(_client),
                new InstanceGovernanceSettingsClient(_client), new InstanceMessagingSettingsClient(_client),
                new InstanceOnboardingClient(_client), new InstancePresentationSettingsClient(_client),
                new InstanceStorageSettingsClient(_client), new SystemClient(_client), new TenantClient(_client),
                auth, services.GetRequiredService<ILogger<InstanceOnboardingService>>(),
                services.GetRequiredService<NavigationManager>()));
            Context.Services.GetRequiredService<BunitNavigationManager>().NavigateTo("/onboarding/instance");
        }
        public void Dispose() { Context.Dispose(); _client.Dispose(); }
    }

    private sealed class Transport : HttpMessageHandler
    {
        internal Guid PendingOperationId { get; } = Guid.CreateVersion7();
        internal bool OrdinarySessionWork { get; set; }
        internal bool AllowCompletion { get; set; } = true;
        internal bool FailCompletion { get; set; }
        internal bool PauseCompletion { get; set; }
        internal bool LoseCompletionResponse { get; set; }
        internal bool PublicStatusCompleted { get; set; } = true;
        internal bool SetupAuthorityPresent { get; set; } = true;
        internal int CompletionPosts { get; private set; }
        internal int PublicStatusReads { get; private set; }
        internal int Accounts { get; private set; }
        internal int Administrators { get; private set; }
        internal TaskCompletionSource CompletionCommitted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource CompletionStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource CompletionRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CompleteLocalInstanceOnboardingRequestDto? Submitted { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath.ToLowerInvariant();
            if (path == "/api/instanceonboarding/status")
            {
                PublicStatusReads++;
                return Json(new
                {
                    isCompleted = PublicStatusCompleted,
                    provider = "Local",
                    isAuthenticated = false,
                    state = PublicStatusCompleted ? "Completed" : "InteractivePending",
                    selectedDeploymentMode = "SingleTenant"
                });
            }
            if (path == "/api/instanceonboarding/journey" && CompletionCommitted.Task.IsCompleted)
                return new HttpResponseMessage(HttpStatusCode.Gone) { Content = JsonContent.Create(new { status = 410 }) };
            if (path == "/api/instanceonboarding/journey") return Json(new
            {
                state = "Available",
                generation = "local-fixture",
                bootstrap = new
                {
                    isCompleted = false,
                    provider = "Local",
                    state = "InteractivePending",
                    isAuthenticated = true,
                    selectedDeploymentMode = "SingleTenant",
                    pendingOperationId = PendingOperationId
                },
                profile = new { siteName = "Native Local Site" },
                authentication = new { provider = "Local", state = "Ready" },
                authorization = new { provider = "Local", state = "Ready" },
                preflight = new { isReadyToLaunch = true, blockingChecks = Array.Empty<object>(), warningChecks = Array.Empty<object>() },
                _links = AllowCompletion ? new Dictionary<string, object> { ["complete-local"] = new { href = "/api/instanceonboarding/complete-local", method = "POST" } } : []
            });
            if (path == "/api/instanceonboarding/complete-local")
            {
                CompletionPosts++;
                Submitted = await request.Content!.ReadFromJsonAsync<CompleteLocalInstanceOnboardingRequestDto>(cancellationToken);
                if (LoseCompletionResponse)
                {
                    Accounts++;
                    Administrators++;
                    CompletionCommitted.TrySetResult();
                    throw new HttpRequestException("Completion response lost after commit.");
                }
                CompletionStarted.TrySetResult();
                if (PauseCompletion) await CompletionRelease.Task.WaitAsync(cancellationToken);
                return FailCompletion
                    ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = JsonContent.Create(new { title = "Setup rejected", status = 400 }) }
                    : Json(new { success = true, id = PendingOperationId });
            }
            OrdinarySessionWork = true;
            throw new InvalidOperationException("Unexpected ordinary-session request during Local setup.");
        }
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}

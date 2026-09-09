
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
        await Assert.That(cut.FindAll("#operator-public-name, #operator-legal-name, #operator-public-contact-email").Count).IsEqualTo(3);
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
        await Assert.That(request.Settings!.DirectoryOperatorIdentity!.PublicContactEmail).IsEqualTo("directory@example.test");
        await Assert.That(request.Username).IsEqualTo("instance-operator");
        await Assert.That(fixture.Transport.OrdinarySessionWork).IsFalse();
        await Assert.That(cut.FindAll("input[type=password]")).IsEmpty();
        await Assert.That(cut.FindAll("a").Any(link => link.GetAttribute("href") == "/login?provider=local")).IsTrue();
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
        await Assert.That(cut.FindAll("a[href='/login?provider=local']")).IsEmpty();
    }

    private static void Fill(IRenderedComponent<InstanceOnboarding> cut)
    {
        cut.Find("input[autocomplete=username]").Change("instance-operator");
        cut.Find("input[autocomplete=new-password]").Change($"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(24))}");
        cut.Find("#operator-public-name").Change("Directory operator");
        cut.Find("#operator-legal-name").Change("Directory operator ASBL");
        cut.Find("#operator-kind-code").Change("registered_organization");
        cut.Find("#operator-jurisdiction-country-code").Change("BE");
        cut.Find("#operator-public-contact-email").Change("directory@example.test");
        cut.Find("#operator-legal-notice-url").Change("https://example.test/legal");
        cut.Find("#operator-privacy-url").Change("https://example.test/privacy");
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
                new InstanceAuthenticationSettingsClient(_client), new InstanceAuthorizationSettingsClient(_client),
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
        internal TaskCompletionSource CompletionStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource CompletionRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CompleteLocalInstanceOnboardingRequestDto? Submitted { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath.ToLowerInvariant();
            if (path == "/api/instanceonboarding/status") return Json(new
            {
                isCompleted = false, provider = "Local", state = "InteractivePending", isAuthenticated = true,
                selectedDeploymentMode = "SingleTenant", pendingOperationId = PendingOperationId,
                _links = AllowCompletion ? new Dictionary<string, object> { ["complete-local"] = new { href = "/api/instanceonboarding/complete-local", method = "POST" } } : []
            });
            if (path == "/api/system/onboarding-status") return Json(new { requiresOnboarding = true, deploymentMode = "SingleTenant" });
            if (path == "/api/instance/settings/branding") return Json(new { defaultBrandDisplayName = "Native Local Site" });
            if (path.EndsWith("/status", StringComparison.Ordinal)) return Json(new { configured = true });
            if (path == "/api/system/onboarding-preflight") return Json(new { isReadyToLaunch = true, blockingChecks = Array.Empty<object>(), warningChecks = Array.Empty<object>() });
            if (path == "/api/instanceonboarding/complete-local")
            {
                Submitted = await request.Content!.ReadFromJsonAsync<CompleteLocalInstanceOnboardingRequestDto>(cancellationToken);
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

using System.Text.Encodings.Web;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Constants;
using Explore.Blazor.Services;
using Explore.Blazor.Services.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Explore.Blazor.IntegrationTests.Endpoints;

public sealed class ConfiguredAdministratorRoutingTests
{
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(5);

    [Test]
    public async Task InteractivePendingRoutesRootAndAnonymousAuthEntryToSetupWithoutLoopingSetup()
    {
        await using var app = CreateFactory(PendingInteractive());
        using var client = CreateClient(app.Factory);

        using var root = await client.GetAsync("/");
        using var challenge = await client.GetAsync("/auth/challenge?provider=keycloak&returnUrl=/dashboard");
        using var setup = await client.GetAsync("/setup");

        await Assert.That(root.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(root.Headers.Location?.OriginalString).IsEqualTo("/setup");
        await Assert.That(challenge.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(challenge.Headers.Location?.OriginalString).IsEqualTo("/setup");
        await Assert.That(setup.StatusCode).IsNotEqualTo(HttpStatusCode.Redirect);
    }

    [Test]
    public async Task ConfiguredKeycloakPendingAllowsOnlyOneExactChallengeAndNeverRendersSetup()
    {
        await using var app = CreateFactory(PendingConfigured("Keycloak"));
        using var client = CreateClient(app.Factory);

        using var setup = await client.GetAsync("/setup");
        await Assert.That(setup.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(setup.Headers.Location?.OriginalString).IsEqualTo("/");

        using var root = await client.GetAsync(setup.Headers.Location);
        await Assert.That(root.Headers.Location?.OriginalString).IsNotEqualTo("/setup");

        using var exact = await client.GetAsync(
            "/auth/challenge?provider=keycloak&returnUrl=/admin%2Ftenants%3Ftab%3Dpending");
        var challenge = await app.Recorder.Challenged.Task.WaitAsync(EventTimeout);

        await Assert.That(exact.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(exact.Headers.Location?.OriginalString)
            .IsEqualTo("/recorded-keycloak?returnUrl=%2Fadmin%2Ftenants%3Ftab%3Dpending");
        await Assert.That(challenge.Scheme).IsEqualTo(AuthSchemeNames.Keycloak);
        await Assert.That(challenge.ReturnUrl).IsEqualTo("/admin/tenants?tab=pending");
        await AssertBrowserTokenBoundaryAsync(exact);

        string[] denied =
        [
            "/login",
            "/auth/login?provider=keycloak",
            "/auth/challenge",
            "/auth/challenge?provider=atproto",
            "/auth/challenge?provider=unknown",
            "/auth/challenge?provider=keycloak&provider=keycloak"
        ];
        foreach (var path in denied)
        {
            using var response = await client.GetAsync(path);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden).Because(path);
            await Assert.That(response.Headers.Location).IsNull().Because(path);
        }

        await Assert.That(app.Recorder.ChallengeCount).IsEqualTo(1);
    }

    [Test]
    public async Task ConfiguredChallengeValidatesReturnUrlBeforeProviderDispatch()
    {
        await using var app = CreateFactory(PendingConfigured("Keycloak"));
        using var client = CreateClient(app.Factory);

        using var response = await client.GetAsync(
            "/auth/challenge?provider=keycloak&returnUrl=https%3A%2F%2Fevil.example%2Fsteal");
        var challenge = await app.Recorder.Challenged.Task.WaitAsync(EventTimeout);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(challenge.ReturnUrl).IsEqualTo("/");
        await Assert.That(response.Headers.Location?.OriginalString).IsEqualTo("/recorded-keycloak?returnUrl=%2F");
        await Assert.That(response.Headers.Location?.OriginalString).DoesNotContain("evil.example");
        await AssertBrowserTokenBoundaryAsync(response);
    }

    [Test]
    public async Task UnknownOrInvalidOnboardingStateFailsClosedWithoutRedirectLoopOrChallenge()
    {
        foreach (var status in new[]
                 {
                     BffOnboardingStatus.Unknown,
                     new BffOnboardingStatus(
                         false,
                         "Pending",
                         "ConfiguredAdministrator",
                         "Unknown",
                         1,
                         BffOnboardingDisposition.Closed)
                 })
        {
            await using var app = CreateFactory(status);
            using var client = CreateClient(app.Factory);

            foreach (var path in new[] { "/", "/setup", "/auth/challenge?provider=keycloak" })
            {
                using var response = await client.GetAsync(path);
                await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable).Because(path);
                await Assert.That(response.Headers.Location).IsNull().Because(path);
            }

            await Assert.That(app.Recorder.ChallengeCount).IsEqualTo(0);
        }
    }

    private static async Task AssertBrowserTokenBoundaryAsync(HttpResponseMessage response)
    {
        var headers = string.Join('\n', response.Headers.SelectMany(header => header.Value));
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(headers).DoesNotContain("access-token");
        await Assert.That(headers).DoesNotContain("refresh-token");
        await Assert.That(body).DoesNotContain("access-token");
        await Assert.That(response.Headers.TryGetValues("Set-Cookie", out var cookies)
            && cookies.Any(cookie => cookie.StartsWith(".AspNetCore.Cookies=", StringComparison.Ordinal))).IsFalse();
    }

    private static BffOnboardingStatus PendingInteractive() => new(
        false,
        "Pending",
        "Interactive",
        null,
        1,
        BffOnboardingDisposition.InteractivePending);

    private static BffOnboardingStatus PendingConfigured(string provider) => new(
        false,
        "Pending",
        "ConfiguredAdministrator",
        provider,
        1,
        BffOnboardingDisposition.ConfiguredAdministratorPending);

    private static TestApplication CreateFactory(BffOnboardingStatus status)
    {
        var recorder = new ChallengeRecorder();
        var factory = new BlazorBffWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IBffOnboardingStatusProvider>();
                var onboarding = Substitute.For<IBffOnboardingStatusProvider>();
                onboarding.GetStatusAsync(Arg.Any<CancellationToken>()).Returns(status);
                services.AddSingleton(onboarding);

                services.RemoveAll<IInstanceOnboardingClient>();
                var onboardingClient = Substitute.For<IInstanceOnboardingClient>();
                onboardingClient.GetInstanceOnboardingStatusAsync(
                        Arg.Any<string?>(),
                        Arg.Any<string?>(),
                        Arg.Any<CancellationToken>())
                    .Returns(Task.FromResult(new HalResourceOfInstanceOnboardingStatusDto
                    {
                        IsCompleted = status.IsCompleted,
                        State = status.State,
                        Mode = status.Mode,
                        Generation = status.Generation,
                        SelectedDeploymentMode = "SingleTenant"
                    }));
                services.AddSingleton(onboardingClient);

                services.RemoveAll<IBffProviderReadinessService>();
                var readiness = Substitute.For<IBffProviderReadinessService>();
                readiness.ResolveProviderScheme(Arg.Any<string?>()).Returns(call =>
                    string.Equals(call.Arg<string?>(), "keycloak", StringComparison.OrdinalIgnoreCase)
                        ? AuthSchemeNames.Keycloak
                        : string.Equals(call.Arg<string?>(), "atproto", StringComparison.OrdinalIgnoreCase)
                            ? AuthSchemeNames.Atproto
                            : null);
                readiness.IsProviderReadyAsync(AuthSchemeNames.Keycloak, Arg.Any<CancellationToken>()).Returns(true);
                readiness.IsProviderReadyAsync(AuthSchemeNames.Atproto, Arg.Any<CancellationToken>()).Returns(true);
                services.AddSingleton(readiness);

                services.AddSingleton(recorder);
                services.AddAuthentication()
                    .AddScheme<AuthenticationSchemeOptions, RecordingChallengeHandler>(
                        AuthSchemeNames.Keycloak,
                        _ => { });
            });
        });
        return new(factory, recorder);
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://events.example.com"),
            HandleCookies = true
        });

    private sealed record TestApplication(
        WebApplicationFactory<Program> Factory,
        ChallengeRecorder Recorder) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Factory.DisposeAsync();
    }

    private sealed class ChallengeRecorder
    {
        private int _challengeCount;

        public TaskCompletionSource<ChallengeEvent> Challenged { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ChallengeCount => Volatile.Read(ref _challengeCount);

        public void Record(string scheme, string returnUrl)
        {
            Interlocked.Increment(ref _challengeCount);
            Challenged.TrySetResult(new(scheme, returnUrl));
        }
    }

    private sealed record ChallengeEvent(string Scheme, string ReturnUrl);

    private sealed class RecordingChallengeHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ChallengeRecorder recorder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(AuthenticateResult.NoResult());

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            var returnUrl = properties.RedirectUri ?? "/";
            recorder.Record(Scheme.Name, returnUrl);
            Response.Redirect($"/recorded-keycloak?returnUrl={Uri.EscapeDataString(returnUrl)}");
            return Task.CompletedTask;
        }
    }
}

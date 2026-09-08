
using System.Net;
using System.Text;
using System.Text.Json;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Models;
using Explore.Blazor.Client.Pages.Registration;
using Explore.Blazor.Client.Services;
using Explore.Blazor.Client.Services.Http;
using Explore.Blazor.Client.Services.Shell;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging.Abstractions;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class AnonymousRegistrationBrowserFlowTests
{
    [Test]
    [Arguments(false, 503)]
    [Arguments(true, 503)]
    [Arguments(false, 401)]
    public async Task SubmittedFailure_RetriesOriginalBodyKeyProofAndScopeEvenAfterExpiry(bool unknownResponse, int status)
    {
        using var flow = new Flow();
        flow.Transport.UnknownResponse = unknownResponse;
        flow.Transport.FailureStatus = (HttpStatusCode)status;
        flow.Transport.FailuresRemaining = 1;
        var request = flow.Request();
        await Assert.That(await flow.Service.StartGuestAsync(flow.EventId, request)).IsNull();
        await Assert.That(flow.Service.PendingGuestEventId).IsEqualTo(flow.EventId);
        await Assert.That(flow.Context.Services.GetRequiredService<NavigationManager>().Uri.Contains("/login", StringComparison.Ordinal)).IsFalse();
        request.Lines!.Single().Quantity = 9;
        await Assert.That(await flow.Service.StartGuestAsync(flow.EventId, request)).IsNull();
        await Assert.That(await flow.Service.RetryGuestAsync(Guid.CreateVersion7())).IsNull();
        flow.Clock.Advance(TimeSpan.FromMinutes(3));
        var recovered = await flow.Service.RetryGuestAsync(flow.EventId);
        await Assert.That(recovered?.Id).IsEqualTo(flow.Transport.OrderId);
        await Assert.That(flow.Transport.Issues.Count).IsEqualTo(1);
        await Assert.That(flow.Transport.Starts.Count).IsEqualTo(2);
        var issue = flow.Transport.Issues.Single();
        foreach (var start in flow.Transport.Starts)
        {
            await Assert.That(start.Body == issue.Body).IsTrue();
            await Assert.That(start.Key == issue.Key).IsTrue();
            await Assert.That(start.Challenge == flow.Transport.Envelope).IsTrue();
            await Assert.That(start.Proof == flow.Solver.Nonce).IsTrue();
            await Assert.That(start.Capability is null && start.AttemptCapability is null).IsTrue();
            await Assert.That(start.Csrf == flow.Csrf).IsTrue();
            await Assert.That(start.ProofCount).IsEqualTo(1);
        }
        await Assert.That(issue.Capability is null && issue.AttemptCapability is null).IsTrue();
        await Assert.That(issue.Csrf == flow.Csrf).IsTrue();
        await Assert.That(flow.Service.PendingGuestEventId).IsNull();
        await Assert.That(flow.Capabilities.TryGet(flow.EventId, flow.Transport.OrderId, out _)).IsTrue();
    }

    [Test]
    public async Task AuthenticatedRegistration401StillUsesNormalLoginRedirect()
    {
        using var flow = new Flow();
        flow.Transport.FailuresRemaining = 1;
        flow.Transport.FailureStatus = HttpStatusCode.Unauthorized;
        await flow.Service.StartAuthenticatedAsync(flow.EventId, flow.Request());
        await Assert.That(flow.Context.Services.GetRequiredService<NavigationManager>().Uri.Contains("/login", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task CancelBeforeSubmit_CreatesNoAllocationAndExplicitStartGetsNewAuthority()
    {
        using var flow = new Flow();
        flow.Solver.Pause = true;
        using var cancellation = new CancellationTokenSource();
        var operation = flow.Service.StartGuestAsync(flow.EventId, flow.Request(), cancellation.Token);
        await flow.Solver.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await operation;
        await Assert.That(flow.Transport.Starts.Count).IsEqualTo(0);
        await Assert.That(flow.Service.PendingGuestEventId).IsNull();
        await Assert.That(flow.Service.GuestStartPhase).IsEqualTo(GuestRegistrationStartPhase.Cancelled);
        flow.Solver.Pause = false;
        await flow.Service.StartGuestAsync(flow.EventId, flow.Request());
        await Assert.That(flow.Transport.Issues.Count).IsEqualTo(2);
        await Assert.That(flow.Transport.Issues[0].Key != flow.Transport.Issues[1].Key).IsTrue();
        await Assert.That(flow.Transport.Starts.Count).IsEqualTo(1);
    }

    [Test]
    public async Task CancelAfterSend_IsUncertainAndRetryDoesNotIssueAgain()
    {
        using var flow = new Flow();
        flow.Transport.PauseStart = true;
        using var cancellation = new CancellationTokenSource();
        var operation = flow.Service.StartGuestAsync(flow.EventId, flow.Request(), cancellation.Token);
        await flow.Transport.StartEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await operation;
        await Assert.That(flow.Service.GuestStartPhase).IsEqualTo(GuestRegistrationStartPhase.Uncertain);
        await Assert.That(flow.Service.PendingGuestEventId).IsEqualTo(flow.EventId);
        flow.Transport.PauseStart = false;
        await flow.Service.RetryGuestAsync(flow.EventId);
        await Assert.That(flow.Transport.Issues.Count).IsEqualTo(1);
        await Assert.That(flow.Transport.Starts.Count).IsEqualTo(2);
        await Assert.That(flow.Transport.Starts[0].Key == flow.Transport.Starts[1].Key).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ChangedBodyOrPrincipalDuringProofCannotSubmitOrReuseThatProof(bool changePrincipal)
    {
        using var flow = new Flow();
        flow.Solver.Pause = true;
        var request = flow.Request();
        var operation = flow.Service.StartGuestAsync(flow.EventId, request);
        await flow.Solver.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (changePrincipal) flow.Context.SetAuthenticatedUser(Guid.CreateVersion7(), "Changed session");
        else request.PlatformContributionBasisPoints = 75;
        flow.Solver.Completion.SetResult(flow.Solver.Nonce);
        await operation;
        await Assert.That(flow.Transport.Starts.Count).IsEqualTo(0);
        await Assert.That(flow.Service.GuestStartPhase).IsEqualTo(GuestRegistrationStartPhase.IntentChanged);
        flow.Context.SetAuthenticatedUser(Guid.CreateVersion7(), "Changed session");
        await flow.Service.StartGuestAsync(flow.EventId, flow.Request());
        await Assert.That(flow.Transport.Issues.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ExpiredBeforeSubmitRequiresExplicitNewChallengeAndDoesNotAllocate()
    {
        using var flow = new Flow();
        flow.Solver.Pause = true;
        var operation = flow.Service.StartGuestAsync(flow.EventId, flow.Request());
        await flow.Solver.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        flow.Clock.Advance(TimeSpan.FromMinutes(3));
        flow.Solver.Completion.SetResult(flow.Solver.Nonce);
        await operation;
        await Assert.That(flow.Service.GuestStartPhase).IsEqualTo(GuestRegistrationStartPhase.Expired);
        await Assert.That(flow.Transport.Starts.Count).IsEqualTo(0);
        flow.Solver.Pause = false;
        await flow.Service.StartGuestAsync(flow.EventId, flow.Request());
        await Assert.That(flow.Transport.Issues.Count).IsEqualTo(2);
        await Assert.That(flow.Transport.Starts.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ExplicitRetriesAreBoundedAndNeverReplaceUnresolvedIntent()
    {
        using var flow = new Flow();
        flow.Transport.FailuresRemaining = 10;
        await flow.Service.StartGuestAsync(flow.EventId, flow.Request());
        for (var attempt = 0; attempt < 4; attempt++) await flow.Service.RetryGuestAsync(flow.EventId);
        await Assert.That(flow.Transport.Starts.Count).IsEqualTo(3);
        await Assert.That(flow.Transport.Issues.Count).IsEqualTo(1);
        await Assert.That(flow.Service.GuestStartPhase).IsEqualTo(GuestRegistrationStartPhase.RetryExhausted);
        await Assert.That(flow.Service.PendingGuestEventId).IsEqualTo(flow.EventId);
    }

    [Test]
    public async Task RenderedProgressLocksFieldsRejectsDuplicateSubmitAndCancellationUnlocksWithoutSecrets()
    {
        using var flow = new Flow();
        flow.Solver.Pause = true;
        var cut = flow.Context.RenderMudComponent<TicketSelection>(p => p.Add(c => c.EventId, flow.EventId));
        cut.Find("input").Change("1");
        var click = cut.Find("[data-testid='ticket-reservation-action']").ClickAsync(new());
        await flow.Solver.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cut.InvokeAsync(() => { });
        await Assert.That(cut.Find("fieldset").HasAttribute("disabled")).IsTrue();
        await Assert.That(cut.Find("[data-testid='ticket-reservation-action']").GetAttribute("aria-busy")).IsEqualTo("true");
        await Assert.That(cut.Find("[data-testid='ticket-challenge-status']").GetAttribute("aria-live")).IsEqualTo("polite");
        await Assert.That(cut.Markup.Contains(flow.Transport.Envelope, StringComparison.Ordinal)).IsFalse();
        await Assert.That(cut.Markup.Contains(flow.Solver.Nonce, StringComparison.Ordinal)).IsFalse();
        await CaptureAsync(flow.Context, cut.Markup, "solving");
        await Assert.That(await flow.Service.StartGuestAsync(flow.EventId, flow.Request())).IsNull();
        await cut.Find("[data-testid='ticket-challenge-cancel']").ClickAsync(new());
        await click;
        await Assert.That(cut.Find("fieldset").HasAttribute("disabled")).IsFalse();
        await Assert.That(flow.Transport.Issues.Count).IsEqualTo(1);
        await Assert.That(flow.Transport.Starts.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RenderedUnknownResponseLocksEditsAndRetryNavigatesToOriginalGuestOrder()
    {
        using var flow = new Flow();
        flow.Transport.FailuresRemaining = 1;
        var cut = flow.Context.RenderMudComponent<TicketSelection>(p => p.Add(c => c.EventId, flow.EventId));
        cut.Find("input").Change("1");
        await cut.Find("[data-testid='ticket-reservation-action']").ClickAsync(new());
        await Assert.That(cut.Find("fieldset").HasAttribute("disabled")).IsTrue();
        await Assert.That(cut.FindAll("[data-testid='ticket-reservation-uncertain']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-testid='ticket-challenge-cancel']").Count).IsEqualTo(0);
        await CaptureAsync(flow.Context, cut.Markup, "uncertain");
        await cut.Find("[data-testid='ticket-reservation-action']").ClickAsync(new());
        await Assert.That(flow.Context.Services.GetRequiredService<NavigationManager>().Uri)
            .EndsWith($"/registration/guest/events/{flow.EventId}/orders/{flow.Transport.OrderId}");
        await Assert.That(flow.Transport.Issues.Count).IsEqualTo(1);
        await Assert.That(flow.Transport.Starts.Count).IsEqualTo(2);
    }

    private static async Task CaptureAsync(BlazorTestContext context, string markup, string state)
    {
        var output = Environment.GetEnvironmentVariable("P08_VISUAL_QA_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Explore.slnx"))) root = root.Parent;
        if (root is null) throw new DirectoryNotFoundException();
        var version = typeof(MudBlazor.MudButton).Assembly.GetName().Version!;
        var packages = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        var mudCss = await File.ReadAllTextAsync(Path.Combine(packages, "mudblazor", $"{version.Major}.{version.Minor}.{version.Build}", "staticwebassets", "MudBlazor.min.css"));
        var tokens = await File.ReadAllTextAsync(Path.Combine(root.FullName, "src/Explore.Blazor/wwwroot/css/tokens.css"));
        var css = await File.ReadAllTextAsync(Path.Combine(root.FullName, "src/Explore.Blazor.Client/Pages/Registration/TicketSelection.razor.css"));
        var theme = context.Render<MudBlazor.MudThemeProvider>();
        Directory.CreateDirectory(output);
        foreach (var direction in new[] { "ltr", "rtl" })
        {
            await File.WriteAllTextAsync(Path.Combine(output, $"ticket-challenge-{state}-{direction}.html"), $$"""
                <!doctype html><html lang="en" dir="{{direction}}"><head><meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1"><title>Ticket challenge visual QA</title>
                <style>{{mudCss}}</style><style>{{tokens}}</style><style>{{css}}</style>
                </head><body>{{theme.Markup}}<main>{{markup}}</main></body></html>
                """);
        }
    }

    private sealed class Flow : IDisposable
    {
        public BlazorTestContext Context { get; } = new();
        public Guid EventId { get; } = Guid.CreateVersion7();
        public Guid CatalogId { get; } = Guid.CreateVersion7();
        public Guid TicketId { get; } = Guid.CreateVersion7();
        public string Csrf { get; } = Guid.NewGuid().ToString("N");
        public TestClock Clock { get; } = new();
        public ControlledSolver Solver { get; } = new();
        public GuestRegistrationOrderCapabilityStore Capabilities { get; } = new();
        public RecordingTransport Transport { get; }
        public RegistrationOrderService Service { get; }
        private readonly HttpClient _http;
        private readonly BffAntiforgeryMessageHandler _pipeline;

        public Flow()
        {
            Context.SetAnonymousUser();
            Context.JSInterop.SetupVoid("Blazor._internal.NavigationLock.enableNavigationPrompt", _ => true);
            Context.JSInterop.SetupVoid("Blazor._internal.NavigationLock.disableNavigationPrompt", _ => true);
            Context.JSInterop.SetupModule("/js/bff.js").Setup<string>("getCookie", "XSRF-TOKEN").SetResult(Csrf);
            Transport = new RecordingTransport(Clock, new RegistrationCheckoutCompositionDto
            {
                EventId = EventId, TicketCatalogVersionId = CatalogId,
                TicketTypes = [new RegistrationCheckoutTicketTypeDto { Id = TicketId, Name = "Admission", TicketPricingModeCode = "FREE" }]
            });
            var behavior = new EventApiBehaviorMessageHandler { InnerHandler = Transport };
            _pipeline = new BffAntiforgeryMessageHandler(Context.JSInterop.JSRuntime) { InnerHandler = behavior };
            _http = new HttpClient(_pipeline, disposeHandler: false) { BaseAddress = new Uri("http://localhost/") };
            // A previous checkout must not accidentally become the scope of a new guest intent.
            _http.DefaultRequestHeaders.Add("X-Registration-Order-Capability", Guid.NewGuid().ToString("N"));
            _http.DefaultRequestHeaders.Add("X-Registration-Attempt-Capability", Guid.NewGuid().ToString("N"));
            Context.Services.AddScoped<IWorkspaceRegistry, WorkspaceRegistry>();
            Context.Services.AddScoped<WorkspaceRouteClassifier>();
            Context.Services.AddScoped<UiShellState>();
            Context.Services.AddScoped<IRegistrationOrderService>(provider => new RegistrationOrderService(
                new RegistrationOrderClient(_http), new AuthenticatedRegistrationOrderClient(_http), new GuestRegistrationOrderClient(_http),
                Substitute.For<IEventService>(), provider.GetRequiredService<UiShellState>(), Capabilities,
                NullLogger<RegistrationOrderService>.Instance, new AnonymousRegistrationChallengeClient(_http), Solver,
                provider.GetRequiredService<NavigationManager>(), Clock, provider.GetRequiredService<AuthenticationStateProvider>(),
                new GuestRegistrationStatusClient(_http)));
            Service = (RegistrationOrderService)Context.Services.GetRequiredService<IRegistrationOrderService>();
            behavior.InnerHandler = new BffUnauthorizedHandler(Context.Services.GetRequiredService<NavigationManager>(), NullLogger<BffUnauthorizedHandler>.Instance)
            {
                InnerHandler = Transport
            };
        }

        public StartRegistrationOrderRequest Request() => new()
        {
            TicketCatalogVersionId = CatalogId, BookingPartyType = 1, PlatformContributionBasisPoints = 25,
            Lines = [new RegistrationOrderLineSelection { TicketTypeId = TicketId, Quantity = 2, ChosenUnitPriceMinor = 300 }]
        };
        public void Dispose() { _http.Dispose(); _pipeline.Dispose(); Context.Dispose(); }
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now += amount;
    }

    private sealed class ControlledSolver : IAnonymousRegistrationChallengeSolver
    {
        public string Nonce { get; } = "0000000000000000";
        public bool Pause { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<string> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<string> SolveAsync(HalResourceOfAnonymousRegistrationChallengeDto challenge, Action<int> progress, CancellationToken cancellationToken)
        {
            progress(1024);
            Entered.TrySetResult();
            return Pause ? await Completion.Task.WaitAsync(cancellationToken) : Nonce;
        }
    }

    private sealed class Captured
    {
        public required string Body { get; init; }
        public string? Key { get; init; }
        public string? Challenge { get; init; }
        public string? Proof { get; init; }
        public int ProofCount { get; init; }
        public string? Capability { get; init; }
        public string? AttemptCapability { get; init; }
        public string? Csrf { get; init; }
    }

    private sealed class RecordingTransport(TestClock clock, RegistrationCheckoutCompositionDto composition) : HttpMessageHandler
    {
        public string Envelope { get; } = Guid.NewGuid().ToString("N");
        public Guid OrderId { get; } = Guid.CreateVersion7();
        public int FailuresRemaining { get; set; }
        public bool UnknownResponse { get; set; }
        public HttpStatusCode FailureStatus { get; set; } = HttpStatusCode.ServiceUnavailable;
        public bool PauseStart { get; set; }
        public List<Captured> Issues { get; } = [];
        public List<Captured> Starts { get; } = [];
        public TaskCompletionSource StartEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _neverDelivered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get) return Json(HttpStatusCode.OK, composition);
            var captured = new Captured
            {
                Body = await request.Content!.ReadAsStringAsync(cancellationToken), Key = Header(request, "Idempotency-Key"),
                Challenge = Header(request, "X-Registration-Challenge"), Proof = Header(request, "X-Registration-Proof"),
                ProofCount = request.Headers.TryGetValues("X-Registration-Proof", out var proofs) ? proofs.Count() : 0,
                Capability = Header(request, "X-Registration-Order-Capability"), AttemptCapability = Header(request, "X-Registration-Attempt-Capability"),
                Csrf = Header(request, "X-CSRF-TOKEN")
            };
            if (request.RequestUri!.AbsolutePath.EndsWith("/guest-registration-challenges", StringComparison.Ordinal))
            {
                Issues.Add(captured);
                return Json(HttpStatusCode.OK, new { protectedChallenge = Envelope, expiresAt = clock.GetUtcNow().AddMinutes(2), difficulty = 18, version = 1 });
            }
            Starts.Add(captured);
            StartEntered.TrySetResult();
            if (PauseStart) await _neverDelivered.Task.WaitAsync(cancellationToken);
            if (FailuresRemaining-- > 0)
            {
                if (UnknownResponse) throw new HttpRequestException();
                return Json(FailureStatus, new { status = (int)FailureStatus });
            }
            var response = Json(HttpStatusCode.Created, new { id = OrderId, success = true });
            response.Headers.Add("X-Registration-Order-Capability", Guid.NewGuid().ToString("N"));
            return response;
        }
        private static string? Header(HttpRequestMessage request, string name) => request.Headers.TryGetValues(name, out var values) ? values.Single() : null;
        private static HttpResponseMessage Json(HttpStatusCode status, object value) => new(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
        };
    }
}

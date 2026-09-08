// ABOUTME: Exercises private bookmark restoration through real generated clients and rendered status UI.
// ABOUTME: Uses exact transport/render signals to guard scope, checkout independence, HAL, and explicit save outcomes.

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Blazor.Client.Components.Registration;
using Explore.Blazor.Client.Components.Registration.FormRenderer;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Pages.Registration;
using Explore.Blazor.Client.Services.Http;
using Explore.Blazor.Client.Services.Shell;
using Explore.Blazor.Client.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace Explore.Blazor.Client.Tests.Pages.Registration;

public sealed class GuestRegistrationStatusTests
{
    private static string Secret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Test]
    public async Task FreshScopeRestoresExactBookmarkAndReadsStatusAfterGeneralCheckoutExpiry()
    {
        string token = Secret();
        using var flow = new Flow(token);
        // The general checkout route is expired, but private status has an independent live window.
        flow.Capabilities.RestoreBookmark(flow.EventId, flow.OrderId, token);
        var capability = flow.Capability;
        await Assert.That(await flow.Service.GetGuestAsync(flow.EventId, flow.OrderId, capability)).IsNull();
        flow.Capabilities.Remove(flow.EventId, flow.OrderId);
        var cut = flow.Render();
        await flow.CompleteAsync(cut);
        await Assert.That(flow.Transport.StatusCapability == token).IsTrue();
        await Assert.That(flow.Transport.StatusPath).IsEqualTo($"/api/events/{flow.EventId}/guest-registration-orders/{flow.OrderId}/status");
        await Assert.That(flow.Transport.StatusBody).IsEqualTo(string.Empty);
        await Assert.That(flow.Capabilities.TryGet(flow.EventId, flow.OrderId, out _)).IsTrue();
        await Assert.That(flow.Capabilities.TryGet(flow.EventId, Guid.CreateVersion7(), out _)).IsFalse();
        await Assert.That(new GuestRegistrationOrderCapabilityStore().TryGet(flow.EventId, flow.OrderId, out _)).IsFalse();
        await Assert.That(cut.Find("[data-testid=registration-status]").GetAttribute("data-status-id")).IsEqualTo("9");
        await Assert.That(cut.Find("[data-testid=event-status]").GetAttribute("data-status-id")).IsEqualTo("3");
        await Assert.That(DateTimeOffset.Parse(cut.Find("[data-testid=status-access-until] time").GetAttribute("datetime")!,
            System.Globalization.CultureInfo.InvariantCulture)).IsEqualTo(new DateTimeOffset(2026, 10, 10, 18, 0, 0, TimeSpan.Zero));
        await Assert.That(flow.Transport.GeneralReads).IsEqualTo(1);
        await Assert.That(cut.Markup).DoesNotContain(token);
        await Assert.That(flow.Navigation.Uri).DoesNotContain(token);
        await Assert.That(cut.FindAll("input, textarea, a[href*='#']").Count).IsEqualTo(0);
        await Assert.That(cut.FindAll("button").Count).IsEqualTo(3); // refresh, copy, download; no P10 action or placeholder
        await WriteEvidenceAsync(cut.Markup, "authorized");
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("invalid")]
    public async Task MissingOrMalformedBookmarkCannotFetchOrRecoverIdentity(string? captured)
    {
        using var flow = new Flow(captured);
        if (captured is not null) flow.Capabilities.RestoreBookmark(flow.EventId, flow.OrderId, Secret());
        var cut = flow.Render();
        await Assert.That(cut.FindAll("[role=alert]").Count).IsEqualTo(1);
        await Assert.That(flow.Transport.StatusEntered.Task.IsCompleted).IsFalse();
        await Assert.That(cut.FindAll("button, form, input, a").Count).IsEqualTo(0);
        await Assert.That(flow.Capabilities.TryGet(flow.EventId, flow.OrderId, out _)).IsFalse();
    }

    [Test]
    [Arguments(404)]
    [Arguments(503)]
    public async Task DeniedOrUnavailableStatusHasGenericAbsenceWithoutActions(int status)
    {
        using var flow = new Flow(Secret());
        var cut = flow.Render();
        await flow.CompleteAsync(cut, status);
        await Assert.That(cut.FindAll("[role=alert]").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("button, form, input, a").Count).IsEqualTo(0);
        await Assert.That(cut.Markup).DoesNotContain(flow.Token!);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task CalendarUsesOnlyPublicHalExportAndNeverCarriesCapability(bool available)
    {
        using var flow = new Flow(Secret());
        flow.Transport.Calendar = available;
        var cut = flow.Render();
        await flow.CompleteAsync(cut);
        await Assert.That(cut.FindAll("[data-testid=registration-status]").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("a").Count).IsEqualTo(available ? 1 : 0);
        if (available)
        {
            var link = cut.Find("a");
            await Assert.That(link.GetAttribute("href")).IsEqualTo($"http://localhost/api/event/{flow.EventId}/calendar");
            await Assert.That(link.GetAttribute("referrerpolicy")).IsEqualTo("no-referrer");
            await Assert.That(link.HasAttribute("download")).IsTrue();
            await Assert.That(link.OuterHtml).DoesNotContain(flow.Token!);
        }
    }

    [Test]
    [Arguments(false, true, "copied")]
    [Arguments(false, false, "failed")]
    [Arguments(true, true, "download-started")]
    [Arguments(true, false, "failed")]
    public async Task SaveIsExplicitReportsActualResultAndFocusesFeedback(bool download, bool success, string outcome)
    {
        using var flow = new Flow(Secret());
        var save = flow.Context.JSInterop.Setup<bool>("guestRegistrationStatus.save", _ => true);
        save.SetResult(success);
        var cut = flow.Render();
        await flow.CompleteAsync(cut);
        await Assert.That(save.Invocations.Count).IsEqualTo(0);
        var focused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        flow.Context.Services.GetRequiredService<IAccessibilityFocusService>().FocusAsync("#guest-status-save-result")
            .Returns(_ => { focused.TrySetResult(); return Task.CompletedTask; });
        await cut.FindAll("button")[download ? 2 : 1].ClickAsync(new MouseEventArgs());
        await focused.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(cut.Find("#guest-status-save-result").GetAttribute("data-save-result")).IsEqualTo(outcome);
        await Assert.That(cut.Find("#guest-status-save-result").GetAttribute("tabindex")).IsEqualTo("-1");
        await Assert.That(save.Invocations.Count).IsEqualTo(1);
        await Assert.That(save.Invocations.Single().Arguments[3]?.ToString() == flow.Token).IsTrue();
        await Assert.That(cut.Markup).DoesNotContain(flow.Token!);
        if (!success) await WriteEvidenceAsync(cut.Markup, download ? "download-failed" : "copy-failed");
    }

    [Test]
    public async Task PostConfirmationHalKeepsStatusEntryWhenGeneralCheckoutReadExpires()
    {
        using var flow = new Flow(null);
        flow.Capabilities.RestoreBookmark(flow.EventId, flow.OrderId, Secret());
        flow.Transport.CheckoutAvailable = true;
        flow.Context.ComponentFactories.AddStub<RegistrationParticipantEditor>();
        flow.Context.ComponentFactories.AddStub<TicketPurchaseGovernancePanel>();
        var cut = flow.Context.RenderMudComponent<GuestOrderRecovery>(parameters => parameters
            .Add(page => page.EventId, flow.EventId).Add(page => page.OrderId, flow.OrderId));
        await Assert.That(cut.FindAll("#guest-order-private-status").Count).IsEqualTo(0);
        await cut.FindAll("button").Single().ClickAsync(new MouseEventArgs());
        await Assert.That(flow.Transport.Confirmed).IsTrue();
        await Assert.That(flow.Transport.GeneralReads).IsEqualTo(2);
        await Assert.That(cut.FindAll("#guest-order-private-status").Count).IsEqualTo(1);
        await cut.FindAll("button").Single().ClickAsync(new MouseEventArgs());
        await Assert.That(flow.Navigation.Uri).IsEqualTo($"http://localhost/registration/guest/events/{flow.EventId}/orders/{flow.OrderId}/status");
        await Assert.That(flow.Navigation.Uri).DoesNotContain(flow.Capability.Value);
    }

    [Test]
    public async Task PrivateInitialLandingDoesNotInitializeAnalyticsOrFetchSettings()
    {
        using var context = new BlazorTestContext();
        var settings = context.AddMockService<IPublicExperienceService>();
        var analytics = context.AddMockService<IAnalyticsInterop>();
        context.Services.AddSingleton(new CookieConsentStateService());
        context.AddMockService<ICookieConsentInterop>();
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"/nested/registration/guest/events/{Guid.CreateVersion7()}/orders/{Guid.CreateVersion7()}/status");
        context.Render<AnalyticsInitializer>();
        // No bootstrap call can start a third-party provider or retain an initial private URL.
        await Assert.That(settings.ReceivedCalls().Any()).IsFalse();
        await Assert.That(analytics.ReceivedCalls().Any()).IsFalse();
    }

    private static async Task WriteEvidenceAsync(string markup, string state)
    {
        if (Environment.GetEnvironmentVariable("GUEST_STATUS_EVIDENCE") is { Length: > 0 } output)
            await VisualEvidenceDocument.WriteAsync(Path.Combine(output, $"status-{state}.html"), "Private registration status", markup);
    }

    private sealed class Flow : IDisposable
    {
        public BlazorTestContext Context { get; } = new();
        public Guid EventId { get; } = Guid.CreateVersion7();
        public Guid OrderId { get; } = Guid.CreateVersion7();
        public string? Token { get; }
        public NavigationManager Navigation => Context.Services.GetRequiredService<NavigationManager>();
        public GuestRegistrationOrderCapabilityStore Capabilities { get; } = new();
        public RegistrationOrderService Service { get; }
        public Transport Transport { get; }
        private readonly HttpClient _http;
        private readonly EventApiBehaviorMessageHandler _pipeline;
        public GuestRegistrationOrderCapability Capability => Capabilities.TryGet(EventId, OrderId, out var capability) ? capability! : throw new InvalidOperationException();

        public Flow(string? token)
        {
            Token = token;
            Context.SetAnonymousUser();
            Context.JSInterop.Setup<string?>("guestRegistrationStatus.take", _ => true).SetResult(token);
            Transport = new Transport(EventId, OrderId);
            _pipeline = new EventApiBehaviorMessageHandler { InnerHandler = Transport };
            _http = new HttpClient(_pipeline, disposeHandler: false) { BaseAddress = new Uri("http://localhost/") };
            Context.AddMockService<IRegistrationPaymentService>();
            Context.Services.AddSingleton<IGuestRegistrationOrderCapabilityStore>(Capabilities);
            Context.Services.AddScoped<IWorkspaceRegistry, WorkspaceRegistry>();
            Context.Services.AddScoped<WorkspaceRouteClassifier>();
            Context.Services.AddScoped<UiShellState>();
            Context.Services.AddScoped<IRegistrationOrderService>(provider => new RegistrationOrderService(
                new RegistrationOrderClient(_http), new AuthenticatedRegistrationOrderClient(_http), new GuestRegistrationOrderClient(_http),
                Substitute.For<IEventService>(), provider.GetRequiredService<UiShellState>(), Capabilities, NullLogger<RegistrationOrderService>.Instance,
                Substitute.For<IAnonymousRegistrationChallengeClient>(), Substitute.For<IAnonymousRegistrationChallengeSolver>(),
                Navigation, TimeProvider.System, provider.GetRequiredService<AuthenticationStateProvider>(), new GuestRegistrationStatusClient(_http)));
            Service = (RegistrationOrderService)Context.Services.GetRequiredService<IRegistrationOrderService>();
            Navigation.NavigateTo($"/registration/guest/events/{EventId}/orders/{OrderId}/status");
        }
        public IRenderedComponent<GuestRegistrationStatus> Render() => Context.RenderMudComponent<GuestRegistrationStatus>(parameters => parameters
            .Add(page => page.EventId, EventId).Add(page => page.OrderId, OrderId));

        public async Task CompleteAsync(IRenderedComponent<GuestRegistrationStatus> cut, int status = 200)
        {
            await Transport.StatusEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            cut.OnMarkupUpdated += (_, _) =>
            {
                if (cut.FindAll(status == 200 ? "[data-testid=registration-status]" : "[role=alert]").Count == 1) rendered.TrySetResult();
            };
            Transport.StatusReply.SetResult(status);
            await rendered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        public void Dispose() { Context.Dispose(); _http.Dispose(); _pipeline.Dispose(); }
    }

    private sealed class Transport(Guid eventId, Guid orderId) : HttpMessageHandler
    {
        public TaskCompletionSource StatusEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<int> StatusReply { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? StatusCapability { get; private set; }
        public string? StatusPath { get; private set; }
        public string? StatusBody { get; private set; }
        public bool Calendar { get; set; }
        public int GeneralReads { get; private set; }
        public bool CheckoutAvailable { get; set; }
        public bool Confirmed { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            int status = 200;
            object body;
            if (path.EndsWith("/status", StringComparison.Ordinal))
            {
                StatusCapability = request.Headers.GetValues("X-Registration-Order-Capability").Single();
                StatusPath = request.RequestUri.PathAndQuery;
                StatusBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
                StatusEntered.TrySetResult();
                status = await StatusReply.Task.WaitAsync(cancellationToken);
                var links = new Dictionary<string, object> { ["self"] = new { href = path, method = "GET" } };
                if (Calendar) links["calendar"] = new { href = $"https://api.internal/api/event/{eventId}/calendar", method = "GET" };
                body = new { eventId, orderId, eventStatusId = 3, registrationOrderStatusId = 9,
                    confirmedAt = "2026-09-01T12:00:00Z", cancelledAt = (string?)null, lastSessionEndUtc = "2026-09-10T18:00:00Z",
                    statusAccessUntil = "2026-10-10T18:00:00Z", _links = links };
            }
            else if (request.Method == HttpMethod.Post && path.EndsWith("/finalize", StringComparison.Ordinal))
            {
                Confirmed = true;
                CheckoutAvailable = false;
                body = new { order = new { id = orderId, eventId, statusCode = "CONFIRMED" },
                    _links = new Dictionary<string, object> { ["guest-status"] = new { href = path + "/status", method = "GET" } } };
            }
            else
            {
                GeneralReads++;
                status = CheckoutAvailable ? 200 : 404;
                body = new { id = orderId, eventId, statusCode = "READY_FOR_CHECKOUT", lines = Array.Empty<object>(),
                    _links = new Dictionary<string, object> { ["finalize"] = new { href = path + "/finalize", method = "POST" } } };
            }
            return new HttpResponseMessage((HttpStatusCode)status) { RequestMessage = request,
                Content = new StringContent(status == 200 ? JsonSerializer.Serialize(body) : "{}", Encoding.UTF8,
                    status == 200 ? "application/hal+json" : "application/problem+json") };
        }
    }
}

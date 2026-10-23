using System.Net;
using System.Text;
using Explore.Blazor.Client.Services.Shell;
using Microsoft.Extensions.Logging.Abstractions;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class AuthenticatedRegistrationCreationRecoveryTests
{
    [Test]
    public async Task UnknownResponseRetainsOriginalBodyAndOperationKeyAndRejectsChangedIntent()
    {
        using var flow = new Flow();
        flow.Transport.FailFirst = true;
        var request = flow.Request();
        await Assert.That(await flow.Service.StartAuthenticatedAsync(flow.EventId, request)).IsNull();
        request.Lines!.Single().Quantity = 2;
        await Assert.That(await flow.Service.StartAuthenticatedAsync(flow.EventId, request)).IsNull();
        await Assert.That(flow.Transport.Captured.Count).IsEqualTo(1);
        var recovered = await flow.Service.StartAuthenticatedAsync(flow.EventId, flow.Request());
        await Assert.That(recovered!.Id).IsEqualTo(flow.Transport.OrderId);
        await Assert.That(flow.Transport.Captured.Count).IsEqualTo(2);
        await Assert.That(flow.Transport.Captured[0]).IsEqualTo(flow.Transport.Captured[1]);
        await Assert.That(string.IsNullOrWhiteSpace(flow.Transport.Captured[0].Key)).IsFalse();
    }

    [Test]
    public async Task CancellationAfterSendKeepsKeyForExactRetry()
    {
        using var flow = new Flow();
        flow.Transport.Hold = true;
        using var cancellation = new CancellationTokenSource();
        var first = flow.Service.StartAuthenticatedAsync(flow.EventId, flow.Request(), cancellation.Token);
        await flow.Transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        bool cancelled = false;
        try { await first; }
        catch (OperationCanceledException) { cancelled = true; }
        await Assert.That(cancelled).IsTrue();
        flow.Transport.Hold = false;
        var retry = await flow.Service.StartAuthenticatedAsync(flow.EventId, flow.Request());
        await Assert.That(retry!.Id).IsEqualTo(flow.Transport.OrderId);
        await Assert.That(flow.Transport.Captured[0]).IsEqualTo(flow.Transport.Captured[1]);
    }

    [Test]
    public async Task ConcurrentSubmitCannotCreateSecondIntentWhileResponseIsUncertain()
    {
        using var flow = new Flow();
        flow.Transport.Hold = true;
        var first = flow.Service.StartAuthenticatedAsync(flow.EventId, flow.Request());
        await flow.Transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await Assert.That(await flow.Service.StartAuthenticatedAsync(flow.EventId, flow.Request())).IsNull();
            await Assert.That(flow.Transport.Captured.Count).IsEqualTo(1);
        }
        finally
        {
            flow.Transport.Release.TrySetResult();
        }
        await Assert.That((await first)!.Id).IsEqualTo(flow.Transport.OrderId);
    }

    [Test]
    public async Task AcknowledgedCreationAllowsDistinctNewIntentWithNewKey()
    {
        using var flow = new Flow();
        await flow.Service.StartAuthenticatedAsync(flow.EventId, flow.Request());
        await flow.Service.StartAuthenticatedAsync(flow.EventId, flow.Request());
        await Assert.That(flow.Transport.Captured.Count).IsEqualTo(2);
        await Assert.That(flow.Transport.Captured[0].Key == flow.Transport.Captured[1].Key).IsFalse();
    }

    private sealed class Flow : IDisposable
    {
        private readonly BlazorTestContext _context = new();
        private readonly HttpClient _http;
        internal Guid EventId { get; } = Guid.CreateVersion7();
        private Guid CatalogId { get; } = Guid.CreateVersion7();
        private Guid TicketId { get; } = Guid.CreateVersion7();
        internal Transport Transport { get; } = new();
        internal RegistrationOrderService Service { get; }

        internal Flow()
        {
            _context.Services.AddScoped<IWorkspaceRegistry, WorkspaceRegistry>();
            _context.Services.AddScoped<WorkspaceRouteClassifier>();
            _context.Services.AddScoped<UiShellState>();
            _http = new HttpClient(Transport) { BaseAddress = new Uri("http://localhost/") };
            Service = new RegistrationOrderService(
                new RegistrationOrderClient(_http),
                new AuthenticatedRegistrationOrderClient(_http),
                new GuestRegistrationOrderClient(_http),
                Substitute.For<IEventService>(),
                _context.Services.GetRequiredService<UiShellState>(),
                new GuestRegistrationOrderCapabilityStore(),
                NullLogger<RegistrationOrderService>.Instance,
                new AnonymousRegistrationChallengeClient(_http),
                Substitute.For<IAnonymousRegistrationChallengeSolver>(),
                _context.Services.GetRequiredService<NavigationManager>(),
                TimeProvider.System,
                _context.Services.GetRequiredService<AuthenticationStateProvider>(),
                new GuestRegistrationStatusClient(_http));
        }

        internal StartRegistrationOrderRequest Request() => new()
        {
            TicketCatalogVersionId = CatalogId,
            BookingPartyType = 1,
            Lines = [new RegistrationOrderLineSelection { TicketTypeId = TicketId, Quantity = 1 }]
        };

        public void Dispose()
        {
            _http.Dispose();
            _context.Dispose();
        }
    }

    private sealed record Captured(string? Key, string Body);

    private sealed class Transport : HttpMessageHandler
    {
        internal Guid OrderId { get; } = Guid.CreateVersion7();
        internal bool FailFirst { get; set; }
        internal bool Hold { get; set; }
        internal List<Captured> Captured { get; } = [];
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Captured.Add(new(
                request.Headers.TryGetValues("Idempotency-Key", out var values) ? values.Single() : null,
                await request.Content!.ReadAsStringAsync(cancellationToken)));
            Entered.TrySetResult();
            if (Hold) await Release.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            bool fail = FailFirst && Captured.Count == 1;
            return new HttpResponseMessage(fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Created)
            {
                RequestMessage = request,
                Content = new StringContent(
                    fail ? "{\"status\":503}" : $$"""{"id":"{{OrderId}}","success":true}""",
                    Encoding.UTF8, fail ? "application/problem+json" : "application/json")
            };
        }
    }
}

using System.Net;
using System.Security.Claims;
using System.Threading.Channels;
using Explore.Blazor.IntegrationTests.Fixtures;
using Explore.Blazor.Services;
using Explore.ServiceDefaults;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Explore.Blazor.IntegrationTests;

public sealed class EarlyAdmissionBoundaryTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    [Test]
    public async Task SaturationPreventsNativeCookieWorkEvenForLoopbackOrForgedInternalHeaders()
    {
        await using var host = await AdmissionHost.CreateAsync();
        await Assert.That(host.App.Services.GetRequiredService<EarlyRequestAdmission>().IsEnabled).IsTrue();
        Task<HttpResponseMessage>[] admitted = Enumerable.Range(0, 32)
            .Select(_ => host.SendPrivateAsync()).ToArray();
        try
        {
            for (int i = 0; i < admitted.Length; i++)
                await host.Entered.Reader.ReadAsync().AsTask().WaitAsync(Deadline);
            foreach (string? forwarding in new string?[] { null, "invalid-forwarding-canary" })
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "/private");
                request.Headers.Add("Cookie", "invalid-cookie-canary");
                request.Headers.Add("X-Event-Internal", "true");
                if (forwarding is not null) request.Headers.Add("X-Forwarded-For", forwarding);
                using var rejected = await host.Client.SendAsync(request).WaitAsync(Deadline);
                await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
            }
            await Assert.That(host.Validations).IsEqualTo(32);
            await Assert.That(host.Challenges).IsEqualTo(0);
        }
        finally
        {
            host.Release.TrySetResult();
            foreach (var response in await Task.WhenAll(admitted).WaitAsync(Deadline)) response.Dispose();
        }
        using var control = await host.SendPrivateAsync().WaitAsync(Deadline);
        await Assert.That(control.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(host.Validations).IsEqualTo(33);
    }

    [Test]
    public async Task NativeDispatcherHasFiniteSubgroupAndReleasesCanceledWork()
    {
        await using var host = await AdmissionHost.CreateAsync();
        Task<int>[] admitted = Enumerable.Range(0, 8).Select(_ => host.DispatchAsync()).ToArray();
        try
        {
            for (int i = 0; i < admitted.Length; i++)
                await host.Entered.Reader.ReadAsync().AsTask().WaitAsync(Deadline);
            await Assert.That(await host.DispatchAsync().WaitAsync(Deadline)).IsEqualTo(429);
            await Assert.That(host.Validations).IsEqualTo(8);
        }
        finally
        {
            host.Release.TrySetResult();
            await Task.WhenAll(admitted).WaitAsync(Deadline);
        }
        await Assert.That(await host.DispatchAsync().WaitAsync(Deadline)).IsEqualTo(200);

        await using var canceledHost = await AdmissionHost.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        Task<int> running = canceledHost.DispatchAsync(cancellation.Token);
        await canceledHost.Entered.Reader.ReadAsync().AsTask().WaitAsync(Deadline);
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => running.WaitAsync(Deadline));
        canceledHost.Release.TrySetResult();
        await Assert.That(await canceledHost.DispatchAsync().WaitAsync(Deadline)).IsEqualTo(200);
    }

    [Test]
    public async Task LoopbackPeerBudgetCannotBecomeAnInternalTransportIdentity()
    {
        await using var host = await AdmissionHost.CreateAsync();
        for (int i = 0; i < 600; i++)
        {
            using var response = await host.Client.GetAsync("/fast").WaitAsync(Deadline);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
        using var forged = new HttpRequestMessage(HttpMethod.Get, "/fast");
        forged.Headers.Add("X-Event-Internal", "true");
        using var rejected = await host.Client.SendAsync(forged).WaitAsync(Deadline);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        host.Release.TrySetResult();
        await Assert.That(await host.DispatchAsync().WaitAsync(Deadline)).IsEqualTo(200);
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        using var refreshed = await host.Client.GetAsync("/fast").WaitAsync(Deadline);
        await Assert.That(refreshed.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task TestingDisableCannotBeSelectedByProduction()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.Configuration["EarlyAdmission:DisableInTesting"] = "true";
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            builder.Services.AddEarlyRequestAdmission(builder.Configuration, builder.Environment);
            return Task.CompletedTask;
        });
    }

    private sealed class AdmissionHost(WebApplication app, ManualClock clock) : IAsyncDisposable
    {
        private const string Scheme = "native-admission-cookie";
        private readonly InProcessEventApiDispatcher _dispatcher = new();
        private string _cookie = null!;
        public WebApplication App { get; } = app;
        public ManualClock Clock { get; } = clock;
        public HttpClient Client { get; private set; } = null!;
        public Channel<int> Entered { get; } = Channel.CreateBounded<int>(256);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Validations;
        public int Challenges;

        public static async Task<AdmissionHost> CreateAsync()
        {
            var clock = new ManualClock();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton<TimeProvider>(clock);
            builder.Services.AddEarlyRequestAdmission(builder.Configuration, builder.Environment);
            builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
            AdmissionHost? host = null;
            builder.Services.AddAuthentication(Scheme).AddCookie(Scheme, options =>
            {
                options.TimeProvider = clock;
                options.Events.OnValidatePrincipal = async context =>
                {
                    int count = Interlocked.Increment(ref host!.Validations);
                    await host.Entered.Writer.WriteAsync(count, context.HttpContext.RequestAborted);
                    await host.Release.Task.WaitAsync(context.HttpContext.RequestAborted);
                };
                options.Events.OnRedirectToLogin = context =>
                {
                    Interlocked.Increment(ref host!.Challenges);
                    context.Response.StatusCode = 401;
                    return Task.CompletedTask;
                };
            });
            builder.Services.AddAuthorization();
            builder.Services.AddTestBffSessionAuthority(new TestBffSessionAuthority(clock), Scheme);
            var app = builder.Build();
            host = new(app, clock);
            app.Use((context, next) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Loopback;
                return next(context);
            });
            app.UseEarlyRequestAdmission();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapGet("/private", () => "authorized").RequireAuthorization();
            app.MapGet("/fast", () => "ready");
            await app.StartAsync();
            host.Client = app.GetTestClient();
            host.Client.BaseAddress = new Uri("https://localhost");
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString("N"))],
                CookieAuthenticationDefaults.AuthenticationScheme));
            var ticket = new AuthenticationTicket(principal, new AuthenticationProperties
            {
                IssuedUtc = clock.GetUtcNow(),
                ExpiresUtc = clock.GetUtcNow().AddHours(1)
            }, Scheme);
            host._cookie = await TestBffSessionAuthorityRegistration.IssueFreshCookieAsync(app.Services, ticket, Scheme);
            var native = new ApplicationBuilder(app.Services);
            native.UseAuthentication();
            native.Run(context =>
            {
                context.Response.StatusCode = context.User.Identity?.IsAuthenticated == true ? 200 : 401;
                return Task.CompletedTask;
            });
            host._dispatcher.Bind(native.Build());
            return host;
        }

        public async Task<HttpResponseMessage> SendPrivateAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/private");
            request.Headers.Add("Cookie", _cookie);
            return await Client.SendAsync(request);
        }

        public async Task<int> DispatchAsync(CancellationToken cancellationToken = default)
        {
            await using var scope = App.Services.CreateAsyncScope();
            var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider, RequestAborted = cancellationToken };
            context.Request.Headers.Cookie = _cookie;
            _dispatcher.MarkRequest(context);
            await _dispatcher.DispatchAsync(context);
            return context.Response.StatusCode;
        }

        public async ValueTask DisposeAsync()
        {
            Release.TrySetResult();
            Client.Dispose();
            await App.DisposeAsync();
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private readonly DateTimeOffset _start = DateTimeOffset.UtcNow;
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);
        public override DateTimeOffset GetUtcNow() => _start.AddTicks(GetTimestamp());
        public void Advance(TimeSpan duration) => Interlocked.Add(ref _ticks, duration.Ticks);
    }
}

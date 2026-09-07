using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Explore.Blazor.Extensions;
using Explore.Blazor.Hosting;
using Explore.Blazor.Services;
using Explore.Blazor.Services.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Event.Standalone.IntegrationTests;

public sealed class InProcessEventApiTransportTests
{
    [Test]
    public async Task HandlerCopiesRequestAndResponseWithoutCookieOrAmbientPrincipal()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        await using var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        var ambient = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "ambient")], "Cookie"))
        };
        ambient.Request.Headers.Cookie = "session=secret";
        accessor.HttpContext = ambient;
        var dispatcher = new InProcessEventApiDispatcher();
        dispatcher.BindEndpointSelector(_ => Task.CompletedTask);
        dispatcher.Bind(async context =>
        {
            await Assert.That(context.Request.Method).IsEqualTo("POST");
            await Assert.That(context.Request.Path).IsEqualTo(new PathString("/api/probe"));
            await Assert.That(context.Request.QueryString.Value).IsEqualTo("?value=1");
            await Assert.That(context.Request.Headers["X-Probe"].ToString()).IsEqualTo("exact");
            await Assert.That(context.Request.Headers.ContainsKey("Cookie")).IsFalse();
            await Assert.That(context.User.Identity?.IsAuthenticated == true).IsFalse();
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, false, 1024, true);
            await Assert.That(await reader.ReadToEndAsync(context.RequestAborted)).IsEqualTo("payload");
            context.Response.StatusCode = StatusCodes.Status201Created;
            context.Response.Headers["X-Result"] = "preserved";
            await context.Response.WriteAsync("response", context.RequestAborted);
        });
        using var handler = new InProcessEventApiHttpMessageHandler(
            dispatcher,
            provider.GetRequiredService<IServiceScopeFactory>(),
            accessor);
        using var client = new HttpClient(handler) { BaseAddress = InProcessEventApiDispatcher.InternalBaseAddress };
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/probe?value=1")
        {
            Content = new StringContent("payload", Encoding.UTF8, "text/plain")
        };
        request.Headers.Add("X-Probe", "exact");
        request.Headers.Add("Cookie", "attacker=secret");

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
        await Assert.That(response.Headers.GetValues("X-Result").Single()).IsEqualTo("preserved");
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("response");
        await Assert.That(accessor.HttpContext).IsSameReferenceAs(ambient);
    }

    [Test]
    public async Task CancellationAbortsDispatch()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        await using var provider = services.BuildServiceProvider();
        var dispatcher = new InProcessEventApiDispatcher();
        dispatcher.BindEndpointSelector(_ => Task.CompletedTask);
        dispatcher.Bind(context => Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted));
        using var handler = new InProcessEventApiHttpMessageHandler(
            dispatcher,
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IHttpContextAccessor>());
        using var client = new HttpClient(handler) { BaseAddress = InProcessEventApiDispatcher.InternalBaseAddress };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        async Task Act() => _ = await client.GetAsync("api/probe", cancellation.Token);

        await Assert.That(Act)
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task ResponseCallbacksRunLifoAroundDispatch()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        await using var provider = services.BuildServiceProvider();
        var callbacks = new List<string>();
        var dispatcher = new InProcessEventApiDispatcher();
        dispatcher.BindEndpointSelector(_ => Task.CompletedTask);
        dispatcher.Bind(async context =>
        {
            callbacks.Add(context.Response.HasStarted ? "started-too-early" : "not-started");
            context.Response.OnStarting(() =>
            {
                callbacks.Add("starting-1");
                context.Response.Headers["X-Started"] = "yes";
                return Task.CompletedTask;
            });
            context.Response.OnStarting(() =>
            {
                callbacks.Add("starting-2");
                return Task.CompletedTask;
            });
            context.Response.OnCompleted(() =>
            {
                callbacks.Add("completed-1");
                return Task.CompletedTask;
            });
            context.Response.OnCompleted(() =>
            {
                callbacks.Add("completed-2");
                return Task.CompletedTask;
            });
            context.Response.OnCompleted(() =>
            {
                callbacks.Add("completed-error");
                throw new InvalidOperationException("completion failed");
            });
            await context.Response.WriteAsync("ok", context.RequestAborted);
            callbacks.Add(context.Response.HasStarted ? "started" : "not-started-after-write");
            try
            {
                context.Response.StatusCode = StatusCodes.Status202Accepted;
            }
            catch (InvalidOperationException)
            {
                callbacks.Add("status-locked");
            }
        });
        using var handler = new InProcessEventApiHttpMessageHandler(
            dispatcher,
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IHttpContextAccessor>());
        using var client = new HttpClient(handler) { BaseAddress = InProcessEventApiDispatcher.InternalBaseAddress };

        using var response = await client.GetAsync("api/callbacks");
        _ = await response.Content.ReadAsStringAsync();

        await Assert.That(response.Headers.GetValues("X-Started").Single()).IsEqualTo("yes");
        await Assert.That(callbacks).IsEquivalentTo([
            "not-started", "starting-2", "starting-1", "started", "status-locked",
            "completed-error", "completed-2", "completed-1"]);
    }

    [Test]
    public async Task StartingFailureRejectsResponseAndStillRunsCompletionCallbacks()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        await using var provider = services.BuildServiceProvider();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = new InProcessEventApiDispatcher();
        dispatcher.BindEndpointSelector(_ => Task.CompletedTask);
        dispatcher.Bind(async context =>
        {
            context.Response.OnCompleted(() =>
            {
                completed.TrySetResult();
                return Task.CompletedTask;
            });
            context.Response.OnStarting(() => throw new InvalidOperationException("starting failed"));
            await context.Response.WriteAsync("never returned", context.RequestAborted);
        });
        using var handler = new InProcessEventApiHttpMessageHandler(
            dispatcher,
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IHttpContextAccessor>());
        using var client = new HttpClient(handler) { BaseAddress = InProcessEventApiDispatcher.InternalBaseAddress };
        async Task Act() => _ = await client.GetAsync("api/callback-failure");

        await Assert.That(Act).Throws<InvalidOperationException>();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task RequestBodyStreamsWithoutEagerFullBuffering()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        await using var provider = services.BuildServiceProvider();
        var releaseContent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstChunkRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = new InProcessEventApiDispatcher();
        dispatcher.BindEndpointSelector(_ => Task.CompletedTask);
        dispatcher.Bind(async context =>
        {
            var buffer = new byte[5];
            await context.Request.Body.ReadExactlyAsync(buffer, context.RequestAborted);
            await Assert.That(Encoding.UTF8.GetString(buffer)).IsEqualTo("first");
            await Assert.That(context.Request.ContentLength).IsEqualTo(12);
            await Assert.That(context.Request.ContentType).IsEqualTo("text/plain");
            firstChunkRead.TrySetResult();
            await context.Response.WriteAsync("accepted", context.RequestAborted);
        });
        using var handler = new InProcessEventApiHttpMessageHandler(
            dispatcher,
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IHttpContextAccessor>());
        using var client = new HttpClient(handler) { BaseAddress = InProcessEventApiDispatcher.InternalBaseAddress };
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/upload")
        {
            Content = new GatedContent(releaseContent.Task)
        };

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        await firstChunkRead.Task.WaitAsync(TimeSpan.FromSeconds(2));
        releaseContent.TrySetResult();

        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("accepted");
    }

    [Test]
    public async Task RequestBodyHonorsEndpointSizeCeilingWhileStreaming()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        await using var provider = services.BuildServiceProvider();
        var dispatcher = new InProcessEventApiDispatcher();
        dispatcher.BindEndpointSelector(_ => Task.CompletedTask);
        dispatcher.Bind(async context =>
        {
            context.Features.GetRequiredFeature<IHttpMaxRequestBodySizeFeature>().MaxRequestBodySize = 5;
            await context.Request.Body.CopyToAsync(Stream.Null, context.RequestAborted);
        });
        using var handler = new InProcessEventApiHttpMessageHandler(
            dispatcher,
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IHttpContextAccessor>());
        using var client = new HttpClient(handler) { BaseAddress = InProcessEventApiDispatcher.InternalBaseAddress };
        async Task Act() => _ = await client.PostAsync(
            "api/upload",
            new StringContent("too-large", Encoding.UTF8, "text/plain"));

        await Assert.That(Act).Throws<BadHttpRequestException>();
    }

    [Test]
    public async Task ResponseStreamsBeforeProducerCompletesAndDisposalCancelsIt()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        await using var provider = services.BuildServiceProvider();
        var producerStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = new InProcessEventApiDispatcher();
        dispatcher.BindEndpointSelector(_ => Task.CompletedTask);
        dispatcher.Bind(async context =>
        {
            context.Response.OnCompleted(() =>
            {
                completed.TrySetResult();
                return Task.CompletedTask;
            });
            try
            {
                await context.Response.WriteAsync("first", context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
                await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                producerStopped.TrySetResult();
            }
        });
        using var handler = new InProcessEventApiHttpMessageHandler(
            dispatcher,
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IHttpContextAccessor>());
        using var client = new HttpClient(handler) { BaseAddress = InProcessEventApiDispatcher.InternalBaseAddress };

        using var response = await client.GetAsync("api/stream", HttpCompletionOption.ResponseHeadersRead);
        await using var stream = await response.Content.ReadAsStreamAsync();
        var buffer = new byte[5];
        await stream.ReadExactlyAsync(buffer);
        await Assert.That(Encoding.UTF8.GetString(buffer)).IsEqualTo("first");

        response.Dispose();

        await producerStopped.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task NamedClientsPreserveAdminAndAtprotoPathsAndHeaders()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddApiHttpClients(
            new ConfigurationBuilder().Build(),
            new TestWebHostEnvironment(),
            BlazorHostProfile.Combined);
        await using var provider = services.BuildServiceProvider();
        var requests = new List<(string Path, string? Authorization, string? Assertion, string? Tenant)>();
        var dispatcher = provider.GetRequiredService<InProcessEventApiDispatcher>();
        dispatcher.BindEndpointSelector(_ => Task.CompletedTask);
        dispatcher.Bind(context =>
        {
            requests.Add((
                context.Request.Path.Value!,
                context.Request.Headers.Authorization.ToString(),
                context.Request.Headers[AtprotoBootstrapAssertionService.SessionBridgeHeaderName].ToString(),
                context.Request.Headers["X-Tenant-Slug"].ToString()));
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        });
        var clientFactory = provider.GetRequiredService<IHttpClientFactory>();
        using var admin = clientFactory.CreateClient("AdminAuthority");
        using var adminRequest = new HttpRequestMessage(HttpMethod.Get, "/api/user/admin-authority");
        adminRequest.Headers.Authorization = new("Bearer", "admin-token");
        using var atproto = clientFactory.CreateClient(ApiBackedOAuthSessionStore.HttpClientName);
        using var atprotoRequest = new HttpRequestMessage(HttpMethod.Get, AtprotoBootstrapAssertionService.SessionBridgePath);
        atprotoRequest.Headers.Authorization = new("Bearer", "atproto-token");
        atprotoRequest.Headers.Add(AtprotoBootstrapAssertionService.SessionBridgeHeaderName, "bridge-assertion");
        atprotoRequest.Headers.Add("X-Tenant-Slug", "tenant-one");

        using var adminResponse = await admin.SendAsync(adminRequest);
        using var atprotoResponse = await atproto.SendAsync(atprotoRequest);

        await Assert.That(adminResponse.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(atprotoResponse.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(requests.Count).IsEqualTo(2);
        await Assert.That(requests[0].Path).IsEqualTo("/api/user/admin-authority");
        await Assert.That(requests[0].Authorization).IsEqualTo("Bearer admin-token");
        await Assert.That(requests[1].Path).IsEqualTo(AtprotoBootstrapAssertionService.SessionBridgePath);
        await Assert.That(requests[1].Authorization).IsEqualTo("Bearer atproto-token");
        await Assert.That(requests[1].Assertion).IsEqualTo("bridge-assertion");
        await Assert.That(requests[1].Tenant).IsEqualTo("tenant-one");
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Event.Standalone.IntegrationTests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class GatedContent : HttpContent
    {
        private readonly Task _release;

        public GatedContent(Task release)
        {
            _release = release;
            Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            Headers.ContentLength = 12;
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            await stream.WriteAsync("first"u8.ToArray());
            await stream.FlushAsync();
            await _release;
            await stream.WriteAsync("-second"u8.ToArray());
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 12;
            return true;
        }
    }
}

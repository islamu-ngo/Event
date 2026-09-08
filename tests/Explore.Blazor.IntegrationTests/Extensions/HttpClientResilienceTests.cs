using Explore.Blazor.Client.Clients;
using Explore.Blazor.Extensions;
using Explore.Blazor.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Explore.Blazor.IntegrationTests.Extensions;

public sealed class HttpClientResilienceTests
{
    [Test]
    public async Task BffSelfClient_WhenRefreshRedirectsToLogin_DoesNotMaskExpiredApplicationSession()
    {
        await using var bff = await RedirectingBffApp.StartAsync();
        var services = CreateServices(bff.BaseAddress);
        services.AddSingleton(Substitute.For<IBffAuthCookieStore>());
        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("BffSelfClient");
        client.BaseAddress = new Uri(bff.BaseAddress);

        using var response = await client.PostAsync("/bff/auth/refresh-session/internal", content: null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(bff.LoginCallCount).IsEqualTo(0);
    }

    [Test]
    public async Task BffClient_PostAiMessage_WhenApiTakesMoreThanFourSeconds_DoesNotTimeoutOrRetryUnsafeRequest()
    {
        await using var api = await DelayedApiApp.StartAsync(TimeSpan.FromSeconds(5));
        var services = CreateServices(api.BaseAddress);
        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(nameof(IAiAssistantClient));

        using var response = await client.PostAsJsonAsync(
            $"/api/ai/assistant/conversations/{Guid.CreateVersion7()}/messages",
            new { content = "hi", idempotencyKey = $"test-{Guid.CreateVersion7()}" });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        await Assert.That(api.CallCount).IsEqualTo(1);
    }

    private static ServiceCollection CreateServices(string apiBaseAddress)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ExploreApi:BaseUrl"] = apiBaseAddress
            })
            .Build();

        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Development);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton(Substitute.For<ICircuitAccessTokenService>());
        services.AddSingleton(Substitute.For<ICircuitUserContext>());
        services.AddSingleton(Substitute.For<ICircuitTokenStore>());
        services.AddSingleton(Substitute.For<ITenantRouteContextAccessor>());
        services.AddSingleton(Substitute.For<ISetupSecretResolver>());
        services.AddSingleton(CreateSupportAccessSessionStore());
        services.AddApiHttpClients(configuration, environment);
        return services;
    }

    private static IBffSupportAccessSessionStore CreateSupportAccessSessionStore()
    {
        var store = Substitute.For<IBffSupportAccessSessionStore>();
        store.ResolveCurrentAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(BffSupportAccessStoreResult.Failed("session_not_found")));
        return store;
    }

    private sealed class DelayedApiApp(WebApplication app) : IAsyncDisposable
    {
        private readonly WebApplication _app = app;
        private int _callCount;

        public string BaseAddress { get; private set; } = string.Empty;

        public int CallCount => _callCount;

        public static async Task<DelayedApiApp> StartAsync(TimeSpan delay)
        {
            var wrapper = new DelayedApiApp(CreateApp());
            wrapper._app.MapPost(
                "/api/ai/assistant/conversations/{conversationId:guid}/messages",
                async (Guid conversationId) =>
                {
                    Interlocked.Increment(ref wrapper._callCount);
                    // Elapsed latency is the contract under test: this request must
                    // outlive the historical four-second timeout without retrying.
                    await Task.Delay(delay);
                    return Results.Accepted(
                        $"/api/ai/assistant/conversations/{conversationId}/runs/{Guid.CreateVersion7()}",
                        new BaseCommandResponseOfGuid
                        {
                            Success = true,
                            Id = Guid.CreateVersion7(),
                            Message = "AI message sent."
                        });
                });

            await wrapper._app.StartAsync();
            var addresses = wrapper._app.Services
                .GetRequiredService<IServer>()
                .Features
                .Get<IServerAddressesFeature>();
            wrapper.BaseAddress = addresses!.Addresses.Single();
            return wrapper;
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        private static WebApplication CreateApp()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });
            builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            return builder.Build();
        }
    }

    private sealed class RedirectingBffApp(WebApplication app) : IAsyncDisposable
    {
        private readonly WebApplication _app = app;
        private int _loginCallCount;

        public string BaseAddress { get; private set; } = string.Empty;

        public int LoginCallCount => _loginCallCount;

        public static async Task<RedirectingBffApp> StartAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });
            builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            var wrapper = new RedirectingBffApp(builder.Build());
            wrapper._app.MapPost("/bff/auth/refresh-session/internal", () => Results.Redirect("/login"));
            wrapper._app.MapGet("/login", () =>
            {
                Interlocked.Increment(ref wrapper._loginCallCount);
                return Results.Ok();
            });

            await wrapper._app.StartAsync();
            var addresses = wrapper._app.Services
                .GetRequiredService<IServer>()
                .Features
                .Get<IServerAddressesFeature>();
            wrapper.BaseAddress = addresses!.Addresses.Single();
            return wrapper;
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}

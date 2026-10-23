using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Event.Standalone.Hosting;
using Explore.API.BackgroundServices;
using Explore.Application.Contracts.Infrastructure;
using Explore.Blazor.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Quartz;
using Quartz.Listener;

namespace Event.Standalone.IntegrationTests.Fixtures;

/// <summary>
/// Runs the Standalone entrypoint, including native startup and hosted workers, under
/// the existing isolated SQLite/local-identity deployment authority. No routes or workers
/// are synthesized. The scheduler uses its supported memory store; outbound HTTP is rejected.
/// </summary>
internal sealed class NonTestingStandaloneWebApplicationFactory : WebApplicationFactory<StandaloneHostMarker>
{
    private readonly NativeEmailOptionalStandaloneFixture _deployment;
    private readonly bool _schedulerEnabled;

    public QueueLifecycleProbe Queue { get; } = new();
    public SchedulerLifecycleSignals Scheduler { get; } = new();
    public ConcurrentQueue<Uri?> OutboundRequests { get; } = new();

    public NonTestingStandaloneWebApplicationFactory(
        NativeEmailOptionalStandaloneFixture deployment, bool schedulerEnabled)
    {
        _deployment = deployment;
        _schedulerEnabled = schedulerEnabled;
        // Minimal-host registration reads these options before the late WAF configuration
        // callback. Bind the controlled profile before Program creates its builder.
        deployment.SetEnvironment("Scheduler__Quartz__Enabled", schedulerEnabled.ToString());
        deployment.SetEnvironment("Scheduler__Quartz__UsePersistentStore", "false");
        deployment.SetEnvironment("Scheduler__Quartz__ValidateSchemaOnStartup", "false");
        deployment.SetEnvironment("Scheduler__Quartz__SchedulerName", $"activation-{Guid.CreateVersion7():N}");
        deployment.SetEnvironment("Scheduler__Quartz__StatusEndpointEnabled", schedulerEnabled.ToString());
        deployment.SetEnvironment("Scheduler__Quartz__DashboardEnabled", schedulerEnabled.ToString());
        deployment.SetEnvironment("WebhookDeliveryProcessor__Enabled", "false");
        deployment.SetEnvironment("IntegrationSyncProcessor__Enabled", "false");
        deployment.SetEnvironment("Atproto__PdsSync__Enabled", "false");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Staging");
        builder.UseStaticWebAssets();
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailConnectionTester>();
            services.AddSingleton<IEmailConnectionTester>(_deployment.Transport);
            services.AddSingleton<IHttpMessageHandlerBuilderFilter>(
                new RejectOutboundHttpFilter(OutboundRequests));

            // Decorate the actual channel, not the worker or its business handler. A pending
            // read and its cancellation are observable without enqueuing artificial business work.
            services.RemoveAll<IAiAssistantRunQueue>();
            services.AddSingleton<IAiAssistantRunQueue>(Queue);
            if (_schedulerEnabled)
            {
                services.AddSingleton(Scheduler);
                services.AddQuartz(quartz => quartz.AddSchedulerListener<SchedulerLifecycleProbe>());
            }
        });
    }

    internal sealed class QueueLifecycleProbe : IAiAssistantRunQueue
    {
        private readonly AiAssistantRunQueue _queue = new();
        private readonly TaskCompletionSource _reading = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Reading => _reading.Task;
        public Task Cancelled => _cancelled.Task;

        public ValueTask EnqueueAsync(AiAssistantRunQueueItem item, CancellationToken cancellationToken) =>
            _queue.EnqueueAsync(item, cancellationToken);

        public async IAsyncEnumerable<AiAssistantRunQueueItem> ReadAllAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await using var reader = _queue.ReadAllAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
            while (true)
            {
                bool available;
                try
                {
                    ValueTask<bool> pending = reader.MoveNextAsync();
                    _reading.TrySetResult();
                    available = await pending;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    _cancelled.TrySetResult();
                    throw;
                }

                if (!available)
                    yield break;
                yield return reader.Current;
            }
        }
    }

    internal sealed class SchedulerLifecycleSignals
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _shutdown = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;
        public Task Shutdown => _shutdown.Task;

        public void MarkStarted() => _started.TrySetResult();
        public void MarkShutdown() => _shutdown.TrySetResult();
    }

    // Quartz constructs listeners through DI instead of reusing a registered listener instance.
    // The factory and whichever listener instance Quartz constructs share these exact signals.
    internal sealed class SchedulerLifecycleProbe(SchedulerLifecycleSignals signals) : SchedulerListenerSupport
    {
        public override Task SchedulerStarted(CancellationToken cancellationToken = default)
        {
            signals.MarkStarted();
            return Task.CompletedTask;
        }

        public override Task SchedulerShutdown(CancellationToken cancellationToken = default)
        {
            signals.MarkShutdown();
            return Task.CompletedTask;
        }
    }

    private sealed class RejectOutboundHttpFilter(ConcurrentQueue<Uri?> requests)
        : IHttpMessageHandlerBuilderFilter
    {
        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) =>
            builder =>
            {
                next(builder);
                if (builder.PrimaryHandler is not InProcessEventApiHttpMessageHandler)
                    builder.PrimaryHandler = new RejectOutboundHttpHandler(requests);
            };
    }

    private sealed class RejectOutboundHttpHandler(ConcurrentQueue<Uri?> requests) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            requests.Enqueue(request.RequestUri);
            throw new InvalidOperationException("This controlled Standalone profile forbids outbound HTTP.");
        }
    }
}

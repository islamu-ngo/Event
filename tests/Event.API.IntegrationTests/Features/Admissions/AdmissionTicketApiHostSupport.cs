using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using Event.Api.IntegrationTests.Fixtures;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Event.Api.IntegrationTests.Features;

internal sealed class AdmissionApiFactory : AuthenticatedWebApplicationFactory
{
    private readonly AdmissionApiScenario scenario;
    private readonly CapturingLoggerProvider? logs;
    private readonly bool enableRecoveryRateLimit;

    internal AdmissionApiFactory(
        AdmissionApiScenario scenario,
        CapturingLoggerProvider? logs = null,
        bool enableRecoveryRateLimit = false)
    {
        this.scenario = scenario;
        this.logs = logs;
        this.enableRecoveryRateLimit = enableRecoveryRateLimit;
        AuthorizationProviderOverride = new StubAuthorizationProvider { AllowAll = true };
        AdditionalConfiguration["RateLimiting:DisableInTesting"] = (!enableRecoveryRateLimit).ToString();
        AdditionalConfiguration["RateLimiting:AdmissionTicketRecovery:PermitLimit"] = "1";
        AdditionalConfiguration["RateLimiting:AdmissionTicketRecovery:WindowSeconds"] = "60";
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        if (logs is not null)
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMediator>();
            services.RemoveAll<ISender>();
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(scenario.Clock);
            services.AddSingleton(scenario);
            services.AddSingleton(AdmissionApiRequestContracts.Resolve());
            services.AddSingleton<AdmissionScenarioDispatcher>();
            services.AddSingleton<IMediator>(provider => new AdmissionScenarioMediator(
                provider.GetRequiredService<AdmissionScenarioDispatcher>()));
            services.AddSingleton<ISender>(provider => provider.GetRequiredService<IMediator>());
            if (enableRecoveryRateLimit)
            {
                services.PostConfigure<RateLimiterOptions>(options =>
                {
                    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                        _ => RateLimitPartition.GetFixedWindowLimiter(
                            "admission-ticket-recovery-test",
                            _ => new FixedWindowRateLimiterOptions
                            {
                                PermitLimit = 1,
                                Window = TimeSpan.FromMinutes(1),
                                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                                QueueLimit = 0,
                                AutoReplenishment = true
                            }));
                });
            }
        });
    }
}

internal sealed class AdmissionScenarioMediator(AdmissionScenarioDispatcher dispatcher) : IMediator
{
    public Task<TResponse> Send<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult((TResponse)dispatcher.Dispatch(request, typeof(TResponse))!);

    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest => throw Unsupported(request!.GetType().AssemblyQualifiedName);

    public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
        throw Unsupported(request.GetType().AssemblyQualifiedName);

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamRequest<TResponse> request,
        CancellationToken cancellationToken = default) => throw Unsupported(request.GetType().AssemblyQualifiedName);

    public IAsyncEnumerable<object?> CreateStream(
        object request,
        CancellationToken cancellationToken = default) => throw Unsupported(request.GetType().AssemblyQualifiedName);

    public Task Publish(object notification, CancellationToken cancellationToken = default) =>
        throw Unsupported(notification.GetType().AssemblyQualifiedName);

    public Task Publish<TNotification>(
        TNotification notification,
        CancellationToken cancellationToken = default)
        where TNotification : INotification => throw Unsupported(notification.GetType().AssemblyQualifiedName);

    private static InvalidOperationException Unsupported(string? identity) =>
        new($"Admission API test mediator received unsupported exact type '{identity ?? "<unknown>"}'.");
}

internal sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(utcNow);
}

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> messages = new();
    internal IReadOnlyCollection<string> Messages => messages;
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(messages);
    public void Dispose() { }

    private sealed class CapturingLogger(ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            messages.Enqueue(formatter(state, exception));
            if (state is IEnumerable<KeyValuePair<string, object?>> fields)
                foreach ((string key, object? value) in fields)
                    messages.Enqueue($"{key}={Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)}");
        }
    }
}

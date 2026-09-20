using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.AdmissionTickets;
using Explore.Application.Features.AdmissionTickets.Handlers;
using Explore.Application.Features.AdmissionTickets.Handlers.Commands;
using Explore.Application.Features.AdmissionTickets.Requests.Commands;
using Explore.Application.Features.AdmissionTickets.Requests.Queries;
using Explore.Application.Operations;
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
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(scenario.Clock);
            services.AddSingleton(scenario);
            services.AddSingleton(AdmissionApiRequestContracts.Resolve());
            services.AddSingleton<AdmissionScenarioDispatcher>();

            var catalog = services.SingleOrDefault(d => d.ServiceType == typeof(NativeOperationCatalog))?.ImplementationInstance as NativeOperationCatalog;
            Type[] prodHandlers =
            [
                typeof(GetCurrentAdmissionTicketsQueryHandler),
                typeof(GetCurrentAdmissionTicketQueryHandler),
                typeof(ReissueCurrentAdmissionTicketQrCommandHandler),
                typeof(ReissueCurrentAdmissionTicketPrintCommandHandler),
                typeof(RequestAdmissionTicketRecoveryCommandHandler),
                typeof(RedeemAdmissionTicketRecoveryCommandHandler)
            ];

            if (catalog is not null)
            {
                foreach (var prod in prodHandlers)
                {
                    var entries = catalog.Registrations.Where(r => r.Implementation == prod).ToArray();
                    foreach (var entry in entries)
                    {
                        catalog.Registrations.Remove(entry);
                        services.Remove(entry.PublicDescriptor);
                        services.Remove(entry.ConcreteDescriptor);
                    }
                }
            }

            services.AddNativeOperations([
                typeof(GetCurrentAdmissionTicketsQuery),
                typeof(AdmissionScenarioGetTicketsHandler),
                typeof(GetCurrentAdmissionTicketQuery),
                typeof(AdmissionScenarioGetTicketHandler),
                typeof(ReissueCurrentAdmissionTicketQrCommand),
                typeof(AdmissionScenarioReissueQrHandler),
                typeof(ReissueCurrentAdmissionTicketPrintCommand),
                typeof(AdmissionScenarioReissuePrintHandler),
                typeof(RequestAdmissionTicketRecoveryCommand),
                typeof(AdmissionScenarioRequestRecoveryHandler),
                typeof(RedeemAdmissionTicketRecoveryCommand),
                typeof(AdmissionScenarioRedeemRecoveryHandler)
            ]);
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

internal sealed class AdmissionScenarioGetTicketsHandler(AdmissionScenarioDispatcher dispatcher)
    : IQueryHandler<GetCurrentAdmissionTicketsQuery, IReadOnlyList<AdmissionTicketDto>>
{
    public Task<IReadOnlyList<AdmissionTicketDto>> QueryAsync(
        GetCurrentAdmissionTicketsQuery query,
        CancellationToken cancellationToken = default) =>
        Task.FromResult((IReadOnlyList<AdmissionTicketDto>)dispatcher.Dispatch(query, typeof(IReadOnlyList<AdmissionTicketDto>))!);
}

internal sealed class AdmissionScenarioGetTicketHandler(AdmissionScenarioDispatcher dispatcher)
    : IQueryHandler<GetCurrentAdmissionTicketQuery, AdmissionTicketDto>
{
    public Task<AdmissionTicketDto> QueryAsync(
        GetCurrentAdmissionTicketQuery query,
        CancellationToken cancellationToken = default) =>
        Task.FromResult((AdmissionTicketDto)dispatcher.Dispatch(query, typeof(AdmissionTicketDto))!);
}

internal sealed class AdmissionScenarioReissueQrHandler(AdmissionScenarioDispatcher dispatcher)
    : ICommandHandler<ReissueCurrentAdmissionTicketQrCommand, AdmissionTicketQrDeliveryDto>
{
    public Task<AdmissionTicketQrDeliveryDto> ExecuteAsync(
        ReissueCurrentAdmissionTicketQrCommand command,
        CancellationToken cancellationToken = default) =>
        Task.FromResult((AdmissionTicketQrDeliveryDto)dispatcher.Dispatch(command, typeof(AdmissionTicketQrDeliveryDto))!);
}

internal sealed class AdmissionScenarioReissuePrintHandler(AdmissionScenarioDispatcher dispatcher)
    : ICommandHandler<ReissueCurrentAdmissionTicketPrintCommand, AdmissionTicketPrintDeliveryDto>
{
    public Task<AdmissionTicketPrintDeliveryDto> ExecuteAsync(
        ReissueCurrentAdmissionTicketPrintCommand command,
        CancellationToken cancellationToken = default) =>
        Task.FromResult((AdmissionTicketPrintDeliveryDto)dispatcher.Dispatch(command, typeof(AdmissionTicketPrintDeliveryDto))!);
}

internal sealed class AdmissionScenarioRequestRecoveryHandler(AdmissionScenarioDispatcher dispatcher)
    : ICommandHandler<RequestAdmissionTicketRecoveryCommand, AdmissionTicketRecoveryRequestResultDto>
{
    public Task<AdmissionTicketRecoveryRequestResultDto> ExecuteAsync(
        RequestAdmissionTicketRecoveryCommand command,
        CancellationToken cancellationToken = default) =>
        Task.FromResult((AdmissionTicketRecoveryRequestResultDto)dispatcher.Dispatch(command, typeof(AdmissionTicketRecoveryRequestResultDto))!);
}

internal sealed class AdmissionScenarioRedeemRecoveryHandler(AdmissionScenarioDispatcher dispatcher)
    : ICommandHandler<RedeemAdmissionTicketRecoveryCommand, AdmissionTicketRecoveryConsumeResultDto>
{
    public Task<AdmissionTicketRecoveryConsumeResultDto> ExecuteAsync(
        RedeemAdmissionTicketRecoveryCommand command,
        CancellationToken cancellationToken = default) =>
        Task.FromResult((AdmissionTicketRecoveryConsumeResultDto)dispatcher.Dispatch(command, typeof(AdmissionTicketRecoveryConsumeResultDto))!);
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

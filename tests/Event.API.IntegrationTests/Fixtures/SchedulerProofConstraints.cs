using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Event.Api.IntegrationTests.Fixtures;

/// <summary>
/// A production host builds exactly one scheduler and never tears its container down mid-life, so Quartz's
/// process-wide state is invisible there. A test process builds many, and two pieces of that state leak
/// between them:
/// <list type="bullet">
/// <item><description>
/// <c>DBConnectionManager</c> is a singleton keyed by ADO data-source name, and every scheduler defaults to
/// the same name — so disposing one container shuts the shared provider down under the others. Each
/// scheduler test therefore passes its own <c>dataSourceName</c>.
/// </description></item>
/// <item><description>
/// <c>LogProvider</c> caches the first container's <see cref="ILoggerFactory"/> statically. Once that
/// container is disposed every later scheduler dies with <c>ObjectDisposedException: LoggerFactory</c>
/// while resolving its own services — see <see cref="AddSchedulerProofLogging"/>.
/// </description></item>
/// </list>
/// </summary>
public static class SchedulerProofConstraints
{
    /// <summary>
    /// Constraint key shared by every test that starts a live scheduler, so they serialize against each
    /// other while the rest of the suite still runs in parallel.
    /// </summary>
    public const string LiveScheduler = "live-quartz-scheduler";

    /// <summary>
    /// Registers a logger factory that survives container disposal. Quartz hands its statically cached
    /// provider the factory from whichever container initialized it first; a disposable factory therefore
    /// turns a finished test into a failure in an unrelated later one.
    /// <see cref="NullLoggerFactory"/> has a no-op <c>Dispose</c>, which also keeps scheduler chatter out of
    /// test output as the suite's pristine-output rule requires.
    /// </summary>
    public static IServiceCollection AddSchedulerProofLogging(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddLogging();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        return services;
    }
}

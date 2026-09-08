using System.Collections;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Tests.Geocoding;

internal sealed class GeocodingReadinessContractHost : IDisposable
{
    private const string ProbeTypeName = "Explore.Infrastructure.Geocoding.GeocodingReadinessProbe";
    private readonly HttpClient _client;
    private readonly object _probe;

    private GeocodingReadinessContractHost(
        PhotonScriptedHttpHandler handler,
        PhotonManualTimeProvider timeProvider,
        object options,
        string environmentName)
    {
        Handler = handler;
        TimeProvider = timeProvider;
        Options = options;
        _client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://photon.operator.example/"),
            Timeout = Timeout.InfiniteTimeSpan
        };
        Type probeType = typeof(Explore.Infrastructure.InfrastructureServicesRegistration).Assembly.GetType(ProbeTypeName)
            ?? throw Red($"Missing production type '{ProbeTypeName}'.");
        ConstructorInfo constructor = probeType.GetConstructors()
            .OrderByDescending(candidate => candidate.GetParameters().Length)
            .FirstOrDefault()
            ?? throw Red("GeocodingReadinessProbe must have a public constructor.");
        var environment = new ReadinessHostEnvironment(environmentName);
        _probe = constructor.Invoke(constructor.GetParameters()
            .Select(parameter => Resolve(parameter.ParameterType, options, environment))
            .ToArray());
    }

    public PhotonScriptedHttpHandler Handler { get; }
    public PhotonManualTimeProvider TimeProvider { get; }
    public object Options { get; }

    public static GeocodingReadinessContractHost None(PhotonScriptedHttpHandler handler) => new(
        handler,
        NewTimeProvider(),
        PhotonDeploymentContractHost.CreateOptions(),
        Environments.Production);

    public static GeocodingReadinessContractHost Photon(
        PhotonScriptedHttpHandler handler,
        int readinessTimeoutMilliseconds = 2_000)
    {
        object options = PhotonDeploymentContractHost.CreateProductionPhotonOptions();
        PhotonDeploymentContractHost.SetRequired(
            options,
            readinessTimeoutMilliseconds,
            "ReadinessTimeoutMilliseconds");
        return new(handler, NewTimeProvider(), options, Environments.Development);
    }

    public async Task<ReadinessView> ProbeAsync(CancellationToken cancellationToken = default)
    {
        MethodInfo method = _probe.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(candidate => candidate.Name is "ProbeAsync" or "CheckAsync" or "CheckHealthAsync")
            .OrderByDescending(candidate => candidate.GetParameters().Length)
            .FirstOrDefault(candidate => candidate.GetParameters().Any(
                parameter => parameter.ParameterType == typeof(CancellationToken)))
            ?? throw Red("GeocodingReadinessProbe must expose ProbeAsync(CancellationToken).");
        object?[] arguments = method.GetParameters().Select(parameter => parameter.ParameterType switch
        {
            Type type when type == typeof(CancellationToken) => (object)cancellationToken,
            Type type when type == typeof(HealthCheckContext) => new HealthCheckContext(),
            _ => throw Red($"Unsupported readiness method parameter '{parameter.ParameterType.FullName}'.")
        }).ToArray();

        object? invocation;
        try
        {
            invocation = method.Invoke(_probe, arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }

        if (invocation is not Task task)
        {
            throw Red("Readiness probe must return Task<T>.");
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
            throw;
        }

        object? raw = task.GetType().GetProperty("Result")?.GetValue(task);
        return ReadinessView.From(raw);
    }

    public void Dispose() => _client.Dispose();

    private object Resolve(Type type, object options, IHostEnvironment environment)
    {
        if (type == typeof(HttpClient)) return _client;
        if (type == typeof(IHttpClientFactory)) return new SingleClientFactory(_client);
        if (type == typeof(TimeProvider)) return TimeProvider;
        if (type == typeof(IHostEnvironment)) return environment;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IOptions<>))
        {
            return Activator.CreateInstance(typeof(OptionsWrapper<>).MakeGenericType(type.GenericTypeArguments[0]), options)!;
        }
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ILogger<>))
        {
            Type nullLoggerType = typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>).MakeGenericType(
                type.GenericTypeArguments[0]);
            return nullLoggerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        }

        throw Red($"Unsupported readiness constructor dependency '{type.FullName}'.");
    }

    private static PhotonManualTimeProvider NewTimeProvider() =>
        new(new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero));

    private static InvalidOperationException Red(string reason) =>
        new($"RED - missing geocoding readiness behavior: {reason}");

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class ReadinessHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "GeocodingReadinessContractTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}

internal sealed record ReadinessView(string Status, string ObservableText, IReadOnlyCollection<string> DataKeys)
{
    public static ReadinessView From(object? raw)
    {
        if (raw is null)
        {
            throw new InvalidOperationException("RED - readiness probe returned no disposition.");
        }

        if (raw is HealthCheckResult health)
        {
            return new(
                health.Status.ToString(),
                string.Join('|', health.Description, Flatten(health.Data)),
                health.Data.Keys.ToArray());
        }

        Type type = raw.GetType();
        object? status = new[] { "Status", "State", "Category", "Disposition" }
            .Select(type.GetProperty)
            .FirstOrDefault(property => property is not null)
            ?.GetValue(raw);
        if (status is null)
        {
            throw new InvalidOperationException("RED - readiness disposition must expose a bounded status category.");
        }

        string observable = string.Join('|', type.GetProperties()
            .Where(property => property.GetIndexParameters().Length == 0)
            .Select(property => property.GetValue(raw)?.ToString()));
        string[] keys = type.GetProperties().Select(property => property.Name).ToArray();
        return new(status.ToString()!, observable, keys);
    }

    private static string Flatten(IReadOnlyDictionary<string, object> data) => string.Join(
        '|',
        data.Select(item => $"{item.Key}={FlattenValue(item.Value)}"));

    private static string FlattenValue(object value) => value is IEnumerable values and not string
        ? string.Join(',', values.Cast<object>())
        : value.ToString() ?? string.Empty;
}

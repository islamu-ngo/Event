using Explore.ServiceDefaults.Compliance;
using Explore.ServiceDefaults;
using Explore.ServiceDefaults.HealthChecks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ServiceDiscovery;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

// Adds common .NET Aspire services: service discovery, resilience, health checks, and OpenTelemetry.
// This project should be referenced by each service project in your solution.
// To learn more about using this project, see https://aka.ms/dotnet/aspire/service-defaults
public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    /// <summary>Reads this executable's explicit owning-build selection, rather than a framework default.</summary>
    public static (string RuntimeManifestPath, string EndpointManifestPath) ReadAssuranceManifestPaths(
        System.Reflection.Assembly owningAssembly)
    {
        var metadata = owningAssembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .Cast<System.Reflection.AssemblyMetadataAttribute>().ToArray();
        string ReadFileName(string key)
        {
            var entries = metadata.Where(item => item.Key == key).ToArray();
            if (entries.Length != 1 || entries[0].Value is not { Length: > 0 and <= 255 } name
                || name != Path.GetFileName(name) || name.Contains('\\', StringComparison.Ordinal) || name.Contains(':', StringComparison.Ordinal)
                || name is "." or ".." || name.Any(char.IsControl))
                throw new InvalidOperationException("managed_served_assets_metadata_rejected");
            return name;
        }
        return (Path.Combine(AppContext.BaseDirectory, ReadFileName("ISLAMU.Assurance.RuntimeManifestFileName")),
            Path.Combine(AppContext.BaseDirectory, ReadFileName("ISLAMU.Assurance.EndpointManifestFileName")));
    }

    /// <summary>Applies explicit inputs before Build. The caller owns the lease until after application disposal.</summary>
    public static async Task<AssuranceStaticAssetSelection> SelectAssuranceStaticAssetsAsync(
        this WebApplicationBuilder builder, string runtimeManifestPath, string endpointManifestPath,
        CancellationToken cancellationToken = default)
    {
        var selection = new AssuranceStaticAssetSelection(builder.Environment);
        try
        {
            if (!OperatingSystem.IsLinux() || !builder.Environment.IsEnvironment("Assurance"))
                throw new InvalidDataException();
            if (builder.Services.Any(descriptor => descriptor.ServiceType == typeof(AssuranceStaticAssetSelection)))
                throw new InvalidDataException();
            string? root = builder.Configuration["ISLAMU_TEST_ROOT"];
            string? owner = builder.Configuration["ISLAMU_TEST_RUN_ID"];
            if (root is null || !PrivateManagedHostRootObserver.Canonical(root)
                || !Guid.TryParseExact(owner, "N", out _) || Path.GetFileName(root) != owner)
                throw new InvalidDataException();
            PrivateManagedHostRootObserver.NoLinks(root);
            string marker = Path.Combine(root, ".islamu-test-owner");
            string user = (await PrivateManagedHostRootObserver.FileIdentityAsync(
                "/proc/" + Environment.ProcessId, true, cancellationToken))[2];
            string[] directory = await PrivateManagedHostRootObserver.FileIdentityAsync(root, false, cancellationToken);
            string[] markerIdentity = await PrivateManagedHostRootObserver.FileIdentityAsync(marker, false, cancellationToken);
            if (directory[0] != "directory" || directory[1] != "700" || directory[2] != user
                || markerIdentity[0] != "regular file" || markerIdentity[1] != "600"
                || markerIdentity[2] != user || markerIdentity[3] != "1" || markerIdentity[6] != "32")
                throw new InvalidDataException();
            await using (var markerStream = new FileStream(marker, new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            }))
            {
                byte[] bytes = new byte[33];
                int count = await markerStream.ReadAtLeastAsync(bytes, bytes.Length, throwOnEndOfStream: false, cancellationToken);
                string descriptor = $"/proc/{Environment.ProcessId}/fd/{markerStream.SafeFileHandle.DangerousGetHandle()}";
                if (count != 32 || System.Text.Encoding.UTF8.GetString(bytes, 0, count) != owner
                    || !(await PrivateManagedHostRootObserver.FileIdentityAsync(descriptor, true, cancellationToken)).SequenceEqual(markerIdentity)
                    || !(await PrivateManagedHostRootObserver.FileIdentityAsync(marker, false, cancellationToken)).SequenceEqual(markerIdentity))
                    throw new InvalidDataException();
            }
            selection.Runtime = await AssuranceStaticAssetSelection.CaptureFileAsync(runtimeManifestPath, cancellationToken);
            selection.Endpoint = await AssuranceStaticAssetSelection.CaptureFileAsync(endpointManifestPath, cancellationToken);
            if (runtimeManifestPath == endpointManifestPath) throw new InvalidDataException();
            selection.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [WebHostDefaults.StaticWebAssetsKey] = runtimeManifestPath
            });
            StaticWebAssetsLoader.UseStaticWebAssets(builder.Environment, selection.Configuration);
            selection.CaptureOwnedProvider();
            await selection.VerifyFilesAsync(cancellationToken);
            PrivateManagedHostRootObserver.NoLinks(root);
            if (!(await PrivateManagedHostRootObserver.FileIdentityAsync(root, false, cancellationToken)).Take(6).SequenceEqual(directory.Take(6))
                || !(await PrivateManagedHostRootObserver.FileIdentityAsync(marker, false, cancellationToken)).SequenceEqual(markerIdentity))
                throw new InvalidDataException();
            builder.Services.AddSingleton(selection);
            return selection;
        }
        catch (Exception failure)
        {
            selection.PrivateFailures.Add(failure);
            selection.CaptureOwnedProvider();
            var rejection = new InvalidOperationException("managed_served_assets_selection_rejected");
            try { selection.Dispose(); }
            catch (Exception cleanup)
            {
                selection.PrivateFailures.Add(cleanup);
                throw new AggregateException(rejection, cleanup);
            }
            throw rejection;
        }
    }

    /// <summary>Maps the same explicit input selected before Build; ordinary hosts retain their existing selection.</summary>
    public static void MapSelectedStaticAssets(this WebApplication app)
    {
        var selection = app.Services.GetService<AssuranceStaticAssetSelection>();
        if (selection is null)
        {
            if (app.Environment.IsEnvironment("Assurance")
                && (app.Configuration["ISLAMU_TEST_ROOT"] is not null || app.Configuration["ISLAMU_TEST_RUN_ID"] is not null))
                throw new InvalidOperationException("managed_served_assets_selection_rejected");
            app.MapStaticAssets();
            return;
        }
        selection.Map(app);
    }

    /// <summary>A Program-owned startup selection; registration in DI borrows this instance.</summary>
    public sealed class AssuranceStaticAssetSelection : IDisposable
    {
        private readonly IWebHostEnvironment environment;
        private readonly Microsoft.Extensions.FileProviders.IFileProvider originalProvider;
        private Microsoft.Extensions.FileProviders.IFileProvider? selectedProvider;
        private IDisposable? ownedProvider;
        private bool mapped;
        private int disposed;
        internal readonly ConfigurationManager Configuration = new();
        internal readonly List<Exception> PrivateFailures = [];
        internal ManifestInput? Runtime;
        internal ManifestInput? Endpoint;

        internal AssuranceStaticAssetSelection(IWebHostEnvironment environment)
        {
            this.environment = environment;
            originalProvider = environment.WebRootFileProvider;
        }

        internal void CaptureOwnedProvider()
        {
            if (!ReferenceEquals(originalProvider, environment.WebRootFileProvider))
            {
                selectedProvider = environment.WebRootFileProvider;
                ownedProvider = environment.WebRootFileProvider as IDisposable;
            }
        }

        internal sealed record ManifestInput(string Path, string Sha256, string Identity, long Modified);

        internal static async Task<ManifestInput> CaptureFileAsync(string path, CancellationToken cancellationToken)
        {
            if (!PrivateManagedHostRootObserver.Canonical(path)) throw new InvalidDataException();
            PrivateManagedHostRootObserver.NoLinks(path);
            var before = new FileInfo(path);
            long stamp = before.LastWriteTimeUtc.Ticks;
            if (!before.Exists || before.Length is <= 0 or > 33554432) throw new InvalidDataException();
            string[] identity = await PrivateManagedHostRootObserver.FileIdentityAsync(path, false, cancellationToken);
            await using var stream = new FileStream(path, new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            });
            string descriptor = $"/proc/{Environment.ProcessId}/fd/{stream.SafeFileHandle.DangerousGetHandle()}";
            if (!(await PrivateManagedHostRootObserver.FileIdentityAsync(descriptor, true, cancellationToken)).SequenceEqual(identity))
                throw new InvalidDataException();
            using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
            byte[] buffer = new byte[65536];
            int length = 0;
            while (true)
            {
                int count = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, 33554433 - length)), cancellationToken);
                if (count == 0) break;
                length += count;
                if (length > 33554432) throw new InvalidDataException();
                hash.AppendData(buffer, 0, count);
            }
            PrivateManagedHostRootObserver.NoLinks(path);
            var after = new FileInfo(path);
            if (!after.Exists || length == 0 || after.Length != length || stream.Length != length || after.LastWriteTimeUtc.Ticks != stamp
                || identity[0] != "regular file" || identity[3] != "1"
                || !(await PrivateManagedHostRootObserver.FileIdentityAsync(descriptor, true, cancellationToken)).SequenceEqual(identity)
                || !(await PrivateManagedHostRootObserver.FileIdentityAsync(path, false, cancellationToken)).SequenceEqual(identity))
                throw new InvalidDataException();
            return new(path, Convert.ToHexStringLower(hash.GetHashAndReset()),
                string.Join('|', identity), stamp);
        }

        internal async Task VerifyFilesAsync(CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref disposed) != 0 || Runtime is null || Endpoint is null
                || await CaptureFileAsync(Runtime.Path, cancellationToken) != Runtime
                || await CaptureFileAsync(Endpoint.Path, cancellationToken) != Endpoint)
                throw new InvalidDataException();
        }

        internal void Map(WebApplication app)
        {
            try
            {
                VerifyFilesAsync(CancellationToken.None).GetAwaiter().GetResult();
                app.MapStaticAssets(Endpoint!.Path);
                VerifyFilesAsync(CancellationToken.None).GetAwaiter().GetResult();
                mapped = true;
            }
            catch (Exception failure)
            {
                PrivateFailures.Add(failure);
                throw new InvalidOperationException("managed_served_assets_selection_rejected");
            }
        }

        internal async Task<object> ObserveAsync(CancellationToken cancellationToken)
        {
            if (!mapped) throw new InvalidDataException();
            await VerifyFilesAsync(cancellationToken);
            return new
            {
                RuntimeManifestPath = Runtime!.Path,
                RuntimeManifestSha256 = Runtime.Sha256,
                EndpointManifestPath = Endpoint!.Path,
                EndpointManifestSha256 = Endpoint.Sha256
            };
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            var failures = new List<Exception>();
            if (ReferenceEquals(environment.WebRootFileProvider, selectedProvider))
                environment.WebRootFileProvider = originalProvider;
            try { ownedProvider?.Dispose(); }
            catch (Exception failure) { PrivateFailures.Add(failure); failures.Add(new InvalidOperationException("managed_served_assets_cleanup_rejected")); }
            try { ((IDisposable)Configuration).Dispose(); }
            catch (Exception failure) { PrivateFailures.Add(failure); failures.Add(new InvalidOperationException("managed_served_assets_cleanup_rejected")); }
            if (failures.Count != 0) throw new AggregateException(failures);
        }
    }

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.TryAddSingleton<HostProcessSignalSubscriptions>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, PrivateManagedHostRootObserver>());
        builder.Services.AddEarlyRequestAdmission(builder.Configuration, builder.Environment);
        builder.ConfigureRedaction();

        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        // NOTE: We intentionally do NOT register global resilience/service-discovery handlers via
        // ConfigureHttpClientDefaults here. Doing so causes Microsoft.Extensions.Http.Resilience
        // to STACK a second ResilienceHandler on top of any per-client AddStandardResilienceHandler
        // call, resulting in double timeouts and spurious BrokenCircuitException cascades.
        //
        // Evidence: dotnet/extensions issues #4814, #5021, #5695. Official guidance is to scope
        // resilience per HttpClient (see docs/ARCHITECTURE.md and Explore.Blazor/Extensions/HttpClientExtensions.cs).
        //
        // If a consumer wants opt-in global resilience, add it in that project explicitly.

        // Uncomment the following to restrict the allowed schemes for service discovery.
        // builder.Services.Configure<ServiceDiscoveryOptions>(options =>
        // {
        //     options.AllowedSchemes = ["https"];
        // });

        return builder;
    }

    public static TBuilder ConfigureRedaction<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddRedaction(redaction =>
        {
            redaction.SetRedactor<StarRedactor>(DataTaxonomy.PiiData);
            redaction.SetRedactor<StarRedactor>(DataTaxonomy.SensitiveData);
            redaction.SetFallbackRedactor<StarRedactor>();
        });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter("Explore.Business")
                    .AddMeter("Explore.Mcp")
                    .AddMeter("Explore.Translation")
                    .AddMeter("Explore.Projections")
                    .AddMeter("Explore.EventLocationPrivacy")
                    .AddMeter("Explore.AdmissionCheckIn")
                    .AddPrometheusExporter();
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddProcessor(new EventResourceTraceProcessor())
                    .AddSource("Explore.Authorization")
                    .AddSource("Explore.Ai.Provider")
                    .AddSource("Explore.Hateoas.Authorization")
                    .AddSource("Explore.Mcp")
                    .AddSource("Quartz")
                    .AddAspNetCoreInstrumentation(tracing =>
                        // Exclude health check requests from tracing
                        tracing.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                            && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath)
                    )
                    // Uncomment the following line to enable gRPC instrumentation (requires the OpenTelemetry.Instrumentation.GrpcNetClient package)
                    //.AddGrpcClientInstrumentation()
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        // Uncomment the following lines to enable the Azure Monitor exporter (requires the Azure.Monitor.OpenTelemetry.AspNetCore package)
        //if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        //{
        //    builder.Services.AddOpenTelemetry()
        //       .UseAzureMonitor();
        //}

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            // Add a default liveness check to ensure app is responsive
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static TBuilder AddOidcDiscoveryReadinessCheck<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHttpClient(OidcDiscoveryHealthCheck.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
        });

        builder.Services.AddHealthChecks()
            .AddCheck<OidcDiscoveryHealthCheck>(
                "oidc-discovery",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready", "oidc", "infrastructure"]);

        return builder;
    }

    public static TBuilder AddDistributedCacheReadinessCheck<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck<DistributedCacheHealthCheck>(
                "distributed-cache",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready", "cache", "infrastructure"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Health check endpoints for container orchestration (Coolify, K8s, etc.)
        // Returns: 200 OK when healthy, 503 Service Unavailable when unhealthy
        // Required for zero-downtime rolling deployments

        var healthCheckOptions = new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("ready"),
            ResponseWriter = HealthCheckResponseWriter.WriteAsync,
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status200OK,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
            }
        };

        // Only readiness checks must pass for app to be considered ready to accept traffic.
        app.MapHealthChecks(HealthEndpointPath, healthCheckOptions).RequirePrivateOperationalListener();

        // Only health checks tagged with the "live" tag must pass for app to be considered alive
        app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live"),
            ResponseWriter = (context, _) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                return Task.CompletedTask;
            },
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status200OK,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
            }
        });

        app.MapPrometheusScrapingEndpoint("/metrics").RequirePrivateOperationalListener();

        return app;
    }

    private sealed class PrivateManagedHostRootObserver(Microsoft.Extensions.Configuration.IConfiguration configuration,
        IHostEnvironment environment, IServiceProvider services)
        : IHostedService, IDisposable
    {
        private FileStream? observation;
        private int stopped;
        private readonly List<Exception> privateFailures = [];

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (environment.EnvironmentName != "Assurance") return;
            if (configuration["ISLAMU_TEST_ROOT"] is null && configuration["ISLAMU_TEST_RUN_ID"] is null)
                return;
            if (observation is not null) return;
            FileStream? pending = null;
            try
            {
                if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
                if (Volatile.Read(ref stopped) != 0) throw new InvalidDataException();
                string? root = configuration["ISLAMU_TEST_ROOT"];
                string? owner = configuration["ISLAMU_TEST_RUN_ID"];
                if (!Guid.TryParseExact(owner, "N", out _) || root is null || !Canonical(root)
                    || Path.GetFileName(root) != owner)
                    throw new InvalidDataException();
                NoLinks(root);
                string marker = Path.Combine(root, ".islamu-test-owner");
                string currentUser = (await FileIdentityAsync("/proc/" + Environment.ProcessId, true, cancellationToken))[2];
                string[] rootIdentity = await FileIdentityAsync(root, false, cancellationToken);
                string[] markerIdentity = await FileIdentityAsync(marker, false, cancellationToken);
                if (rootIdentity[0] != "directory" || rootIdentity[1] != "700" || rootIdentity[2] != currentUser
                    || markerIdentity[0] != "regular file" || markerIdentity[1] != "600"
                    || markerIdentity[2] != currentUser || markerIdentity[3] != "1" || markerIdentity[6] != "32")
                    throw new InvalidDataException();
                using (var markerStream = new FileStream(marker, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    byte[] bytes = new byte[32];
                    await markerStream.ReadExactlyAsync(bytes, cancellationToken);
                    if (markerStream.ReadByte() != -1 || System.Text.Encoding.UTF8.GetString(bytes) != owner)
                        throw new InvalidDataException();
                }
                string contentRoot = environment.ContentRootPath;
                if (!Canonical(contentRoot) || !Directory.Exists(contentRoot)) throw new InvalidDataException();
                NoLinks(contentRoot);
                string applicationName = environment.ApplicationName;
                string? entryAssembly = System.Reflection.Assembly.GetEntryAssembly()?.Location;
                if (string.IsNullOrWhiteSpace(applicationName) || applicationName.Length > 512
                    || applicationName.Any(char.IsControl) || string.IsNullOrWhiteSpace(entryAssembly)
                    || entryAssembly.Length > 4096 || !Canonical(entryAssembly) || !File.Exists(entryAssembly))
                    throw new InvalidDataException();
                NoLinks(entryAssembly);
                var webEnvironment = services.GetService<IWebHostEnvironment>();
                int? operationsTargetPort = webEnvironment is null ? null
                    : configuration.GetValue<int?>("OperationalEndpoints:Port");
                if (operationsTargetPort is < 1 or > 65535) throw new InvalidDataException();
                int? httpTargetPort = webEnvironment is null ? null
                    : ReadHttpTargetPort(configuration[WebHostDefaults.ServerUrlsKey], operationsTargetPort);
                string? webRoot = webEnvironment?.WebRootPath;
                if (!string.IsNullOrEmpty(webRoot))
                {
                    if (webRoot.Length > 4096 || !Canonical(webRoot)) throw new InvalidDataException();
                    NoLinks(webRoot);
                }
                ulong start = ProcessStart();
                byte[] record = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
                {
                    Schema = 1,
                    RunId = owner,
                    Pid = Environment.ProcessId,
                    Start = start,
                    ContentRoot = contentRoot,
                    ApplicationName = applicationName,
                    EntryAssembly = entryAssembly,
                    IsWebHost = webEnvironment is not null,
                    WebRoot = webRoot,
                    OperationsTargetPort = operationsTargetPort,
                    HttpTargetPort = httpTargetPort,
                    ServedAssets = services.GetService<AssuranceStaticAssetSelection>() is { } selection
                        ? await selection.ObserveAsync(cancellationToken) : null
                });
                if (record.Length > 16384) throw new InvalidDataException();
                string temporary = Path.Combine(root, ".host-root-" + Guid.NewGuid().ToString("N"));
                string published = Path.Combine(root, $"managed-host-root-{Environment.ProcessId}-{start}.json");
                pending = new FileStream(temporary, new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.ReadWrite,
                    Share = FileShare.Read,
                    UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
                });
                await pending.WriteAsync(record, cancellationToken);
                await pending.FlushAsync(cancellationToken);
                string descriptor = $"/proc/{Environment.ProcessId}/fd/{pending.SafeFileHandle.DangerousGetHandle()}";
                string[] before = await FileIdentityAsync(descriptor, true, cancellationToken);
                NoLinks(root);
                if (!(await FileIdentityAsync(root, false, cancellationToken)).Take(6).SequenceEqual(rootIdentity.Take(6))
                    || !(await FileIdentityAsync(marker, false, cancellationToken)).SequenceEqual(markerIdentity)
                    || ProcessStart() != start)
                    throw new InvalidDataException();
                File.Move(temporary, published, overwrite: false);
                string[] after = await FileIdentityAsync(published, false, cancellationToken);
                if (!after.SequenceEqual(before) || after[0] != "regular file" || after[1] != "600"
                    || after[2] != currentUser || after[3] != "1"
                    || after[6] != record.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    || !(await FileIdentityAsync(descriptor, true, cancellationToken)).SequenceEqual(after))
                    throw new InvalidDataException();
                pending.Position = 0;
                byte[] readback = new byte[record.Length];
                await pending.ReadExactlyAsync(readback, cancellationToken);
                NoLinks(root);
                if (pending.ReadByte() != -1 || !readback.AsSpan().SequenceEqual(record)
                    || ProcessStart() != start
                    || !(await FileIdentityAsync(root, false, cancellationToken)).Take(6).SequenceEqual(rootIdentity.Take(6))
                    || !(await FileIdentityAsync(marker, false, cancellationToken)).SequenceEqual(markerIdentity)
                    || !(await FileIdentityAsync(descriptor, true, cancellationToken)).SequenceEqual(after))
                    throw new InvalidDataException();
                observation = pending;
                pending = null;
                if (Volatile.Read(ref stopped) != 0)
                {
                    Dispose();
                    throw new InvalidDataException();
                }
                return;
            }
            catch (Exception failure)
            {
                privateFailures.Add(failure);
                try { pending?.Dispose(); }
                catch (Exception cleanupFailure) { privateFailures.Add(cleanupFailure); }
            }
            // Host logging must never render private paths from the original failure chain.
            throw new InvalidOperationException("managed_host_root_observation_rejected");
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Dispose();
            return Task.CompletedTask;
        }

        private static int? ReadHttpTargetPort(string? bindings, int? operationsPort)
        {
            if (string.IsNullOrWhiteSpace(bindings) || bindings.Length > 8192) return null;
            string[] values = bindings.Split(';');
            if (values.Length > 16) return null;
            var ports = new HashSet<int>();
            foreach (string binding in values)
            {
                // Wildcard authorities are binding selectors; only their explicitly configured port is retained.
                string value = binding.Trim();
                if (value.StartsWith("http://*:", StringComparison.OrdinalIgnoreCase)
                    || value.StartsWith("http://+:", StringComparison.OrdinalIgnoreCase))
                    value = "http://localhost:" + value[9..];
                if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
                    || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0
                    || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0
                    || uri.Port is < 1 or > 65535) return null;
                if (uri.Scheme == "http" && uri.Port != operationsPort) ports.Add(uri.Port);
            }
            return ports.Count == 1 ? ports.Single() : null;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref stopped, 1);
            FileStream? owned = Interlocked.Exchange(ref observation, null);
            try { owned?.Dispose(); }
            catch (Exception failure)
            {
                privateFailures.Add(failure);
                throw new InvalidOperationException("managed_host_root_observation_rejected");
            }
        }

        internal static bool Canonical(string path) => Path.IsPathFullyQualified(path)
            && Path.GetFullPath(path) == path && !path.EndsWith(Path.DirectorySeparatorChar);

        internal static void NoLinks(string path)
        {
            for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
            {
                FileSystemInfo item = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
                if (item.LinkTarget is not null) throw new InvalidDataException();
                if (current == Path.GetPathRoot(current)) break;
            }
        }

        private static ulong ProcessStart()
        {
            string value = File.ReadAllText("/proc/self/stat");
            if (value.Length > 16384) throw new InvalidDataException();
            string[] fields = value[(value.LastIndexOf(')') + 2)..].Split(' ');
            return ulong.TryParse(fields[19], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out ulong start) && start > 0
                ? start : throw new InvalidDataException();
        }

        internal static async Task<string[]> FileIdentityAsync(string path, bool follow, CancellationToken cancellationToken)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            using var process = new System.Diagnostics.Process();
            process.StartInfo = new System.Diagnostics.ProcessStartInfo("stat")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            process.StartInfo.Environment["LC_ALL"] = "C";
            if (follow) process.StartInfo.ArgumentList.Add("--dereference");
            process.StartInfo.ArgumentList.Add("--format=%F|%a|%u|%h|%d|%i|%s");
            process.StartInfo.ArgumentList.Add("--");
            process.StartInfo.ArgumentList.Add(path);
            if (!process.Start()) throw new InvalidDataException();
            Task<string> output = BoundedTextAsync(process.StandardOutput, deadline.Token);
            Task<string> error = BoundedTextAsync(process.StandardError, deadline.Token);
            var failures = new List<Exception>();
            string? value = null;
            try
            {
                await process.WaitForExitAsync(deadline.Token);
                value = await output.WaitAsync(deadline.Token);
                string problem = await error.WaitAsync(deadline.Token);
                if (process.ExitCode != 0 || problem.Length != 0) throw new InvalidDataException();
            }
            catch (Exception failure) { failures.Add(failure); }
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception failure) { failures.Add(failure); }
            try { await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None); }
            catch (Exception failure) { failures.Add(failure); }
            try { await Task.WhenAll(output, error).WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None); }
            catch (Exception failure) { failures.Add(failure); }
            bool outputCompleted = output.IsCompleted;
            bool errorCompleted = error.IsCompleted;
            if (outputCompleted)
            {
                try { await output; }
                catch (Exception failure)
                {
                    if (!failures.Any(existing => ReferenceEquals(existing, failure))) failures.Add(failure);
                }
            }
            if (errorCompleted)
            {
                try { await error; }
                catch (Exception failure)
                {
                    if (!failures.Any(existing => ReferenceEquals(existing, failure))) failures.Add(failure);
                }
            }
            if (failures.Count != 0) throw new AggregateException(failures);
            string[] fields = value!.TrimEnd('\n').Split('|');
            if (fields.Length != 7 || fields.Skip(1).Any(field => field.Length is < 1 or > 20
                || !field.All(char.IsAsciiDigit))) throw new InvalidDataException();
            return fields;
        }

        private static async Task<string> BoundedTextAsync(StreamReader reader, CancellationToken cancellationToken)
        {
            char[] buffer = new char[513];
            int length = 0;
            while (length < buffer.Length)
            {
                int read = await reader.ReadAsync(buffer.AsMemory(length), cancellationToken);
                if (read == 0) return new string(buffer, 0, length);
                length += read;
            }
            throw new InvalidDataException();
        }
    }
}

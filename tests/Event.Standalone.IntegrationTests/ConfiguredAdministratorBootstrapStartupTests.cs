// ABOUTME: Proves configured-administrator preparation is a synchronous pre-HTTP startup gate in both host topologies.
// ABOUTME: Uses exact runtime signals to reject duplicate manifest ownership, retries, scheduling, and premature authority.

using System.Collections.Concurrent;
using System.Net;
using Explore.API.Hosting;
using Explore.API.Scheduling;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Infrastructure.ConfigurationManifest;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Event.Standalone.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Quartz;

namespace Event.Standalone.IntegrationTests;

[NotInParallel]
public sealed class ConfiguredAdministratorBootstrapStartupTests
{
    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(10);

    [Test]
    public async Task SplitWaitsForMigrationAndManifestThenPreparesExactlyOnceBeforeHttp()
    {
        var timeline = new StartupTimeline();
        timeline.Record("migration-dependency");
        timeline.Record("manifest-dependency");

        await using WebApplication app = BuildSplitHost(timeline);
        using var shutdown = new CancellationTokenSource();
        var state = new ApiHostCompositionState(
            IsOpenApiGeneration: false,
            UseQuartzScheduler: false,
            HttpsRedirectionEnabled: false,
            OwnsDevelopmentMigrations: true);

        await app.RunApiHostStartupAsync(state, shutdown, () => { });
        app.MapGet("/ready", () =>
        {
            timeline.Record("http");
            return Results.Ok();
        });
        app.Lifetime.ApplicationStarted.Register(() => timeline.Record("http-ready"));

        using var startupTimeout = new CancellationTokenSource(SignalTimeout);
        await app.StartAsync(startupTimeout.Token);
        await timeline.WaitForAsync("http-ready", SignalTimeout);
        using HttpClient client = app.GetTestClient();
        using HttpResponseMessage response = await client.GetAsync("/ready", startupTimeout.Token);
        await timeline.WaitForAsync("http", SignalTimeout);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(timeline.Events).IsEquivalentTo(
            ["migration-dependency", "manifest-dependency", "preparation", "bootstrap-created", "setup-authority", "http-ready", "http"],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(timeline.Count("preparation")).IsEqualTo(1);
        await Assert.That(app.Services.GetRequiredService<ManifestProbe>().RunCount).IsEqualTo(0);

        await app.StopAsync(startupTimeout.Token);
    }

    [Test]
    public async Task StandaloneRunsMigrationSeedManifestAndPreparationExactlyOnceBeforeHttp()
    {
        var timeline = new StartupTimeline();
        await using var factory = new StartupFactory(timeline, ConfiguredValues());

        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });
        using var requestTimeout = new CancellationTokenSource(SignalTimeout);
        using HttpResponseMessage response = await client.GetAsync("/alive", requestTimeout.Token);
        await timeline.WaitForAsync("http", SignalTimeout);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(timeline.Events).IsEquivalentTo(
            ["migration-seed", "manifest", "preparation", "bootstrap-created", "setup-authority", "token-authority", "cookie-authority", "http"],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(timeline.Count("migration-seed")).IsEqualTo(1);
        await Assert.That(timeline.Count("manifest")).IsEqualTo(1);
        await Assert.That(timeline.Count("preparation")).IsEqualTo(1);
        await Assert.That(timeline.Count("bootstrap-created")).IsEqualTo(1);
    }

    [Test]
    public async Task InteractiveModePreparationIsAStartupNoOp()
    {
        var timeline = new StartupTimeline();
        await using var factory = new StartupFactory(timeline, InteractiveValues());

        using HttpClient client = factory.CreateClient();
        using var requestTimeout = new CancellationTokenSource(SignalTimeout);
        using HttpResponseMessage response = await client.GetAsync("/alive", requestTimeout.Token);
        await timeline.WaitForAsync("http", SignalTimeout);

        var repository = factory.Services.GetRequiredService<BootstrapRepositoryProbe>();
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(timeline.Events).IsEquivalentTo(
            ["migration-seed", "manifest", "preparation", "setup-authority", "token-authority", "cookie-authority", "http"],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(repository.CreateCount).IsEqualTo(0);
        await Assert.That(repository.UpdateCount).IsEqualTo(0);
        await Assert.That(repository.DeleteCount).IsEqualTo(0);
    }

    [Test]
    public async Task PreparationFailureBlocksSetupCookieTokenAndHttpAuthorityWithoutRetry()
    {
        var timeline = new StartupTimeline();
        await using var factory = new StartupFactory(timeline, new Dictionary<string, string?>
        {
            ["INSTANCE_BOOTSTRAP_MODE"] = "invalid"
        });

        Exception? failure = null;
        try
        {
            _ = factory.CreateClient();
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        await Assert.That(failure).IsNotNull();
        await Assert.That(FlattenMessages(failure!)).Contains("instance_bootstrap_mode_invalid");
        await Assert.That(timeline.Events).IsEquivalentTo(
            ["migration-seed", "manifest", "preparation"],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(timeline.Count("manifest")).IsEqualTo(1);
        await Assert.That(timeline.Count("preparation")).IsEqualTo(1);
        await Assert.That(timeline.Count("setup-authority")).IsEqualTo(0);
        await Assert.That(timeline.Count("token-authority")).IsEqualTo(0);
        await Assert.That(timeline.Count("cookie-authority")).IsEqualTo(0);
        await Assert.That(timeline.Count("http")).IsEqualTo(0);
    }

    [Test]
    public async Task PreparationOwnsNoHostedWorkerSchedulerOrManifestRunnerContract()
    {
        Type preparation = typeof(ConfiguredAdministratorBootstrapStartupRunner);

        await Assert.That(typeof(IHostedService).IsAssignableFrom(preparation)).IsFalse();
        await Assert.That(typeof(BackgroundService).IsAssignableFrom(preparation)).IsFalse();
        await Assert.That(typeof(IJob).IsAssignableFrom(preparation)).IsFalse();
        await Assert.That(typeof(IConfigurationManifestStartupRunner).IsAssignableFrom(preparation)).IsFalse();
        await Assert.That(typeof(IConfigurationManifestPostMigrationSequence).IsAssignableFrom(preparation)).IsFalse();
    }

    private static WebApplication BuildSplitHost(StartupTimeline timeline)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Staging"
        });
        builder.WebHost.UseTestServer();
        builder.AddServiceDefaults();
        builder.Configuration.AddInMemoryCollection(BaseValues(ConfiguredValues()));
        builder.Services.AddLogging();
        builder.Services.AddSingleton<IInstanceOperatorIdentity, OperatorIdentityProbe>();
        builder.Services.AddSingleton(timeline);
        builder.Services.AddSingleton<BootstrapRepositoryProbe>();
        builder.Services.AddSingleton<IInstanceBootstrapStateRepository>(services =>
            services.GetRequiredService<BootstrapRepositoryProbe>());
        builder.Services.AddSingleton<IUnitOfWork, UnitOfWorkProbe>();
        builder.Services.AddSingleton<ManifestProbe>();
        builder.Services.AddSingleton<IConfigurationManifestStartupRunner>(services =>
            services.GetRequiredService<ManifestProbe>());
        builder.Services.AddSingleton<ISetupSecretProvider, SetupAuthorityProbe>();
        builder.Services.AddSingleton<IPrivacyErasureReplayService, PrivacyReplayProbe>();
        builder.Services.AddSingleton(TimeProvider.System);

        return builder.Build();
    }

    private static Dictionary<string, string?> BaseValues(
        IReadOnlyDictionary<string, string?> bootstrapValues)
    {
        var values = new Dictionary<string, string?>(bootstrapValues, StringComparer.Ordinal)
        {
            ["Deployment:Mode"] = "SingleTenant",
            ["Keycloak:Authority"] = "https://authority.example.test",
            ["Instance:OperatorIdentity:OperatorId"] = "0198e2a4-5340-7f89-8abc-b8bdf43e0ea8",
            ["Instance:OperatorIdentity:PublicName"] = "Startup Test Operator",
            ["Instance:OperatorIdentity:LegalName"] = "Startup Test Operator ASBL",
            ["Instance:OperatorIdentity:IsOfficialInstance"] = "false",
            ["Instance:OperatorIdentity:OfficialOrigin"] = "https://standalone.example.test",
            ["Instance:OperatorIdentity:OperatorKindCode"] = "registered_organization",
            ["Instance:OperatorIdentity:JurisdictionCountryCode"] = "BE",
            ["Instance:OperatorIdentity:RegistrationIdentifier"] = "BE 0123.456.789",
            ["Instance:OperatorIdentity:PublicContactEmail"] = "contact@standalone.example.test",
            ["Instance:OperatorIdentity:WebsiteUrl"] = "https://standalone.example.test",
            ["Instance:OperatorIdentity:LegalNoticeUrl"] = "https://standalone.example.test/legal",
            ["Instance:OperatorIdentity:TermsUrl"] = "https://standalone.example.test/terms",
            ["Instance:OperatorIdentity:PrivacyUrl"] = "https://standalone.example.test/privacy"
        };
        return values;
    }

    private static Dictionary<string, string?> ConfiguredValues() =>
        new Dictionary<string, string?>
        {
            ["INSTANCE_BOOTSTRAP_MODE"] = "ConfiguredAdministrator",
            ["INSTANCE_BOOTSTRAP_ADMIN_PROVIDER"] = "keycloak",
            ["INSTANCE_BOOTSTRAP_ADMIN_SUBJECT"] = "configured-admin",
            ["INSTANCE_BOOTSTRAP_BINDING_GENERATION"] = "1",
            ["INSTANCE_BOOTSTRAP_ADMIN_EMAIL"] = "admin@example.test"
        };

    private static Dictionary<string, string?> InteractiveValues() =>
        new Dictionary<string, string?>
        {
            ["INSTANCE_BOOTSTRAP_MODE"] = "Interactive"
        };

    private static string FlattenMessages(Exception exception)
    {
        var messages = new List<string>();
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(" | ", messages);
    }

    private sealed class StartupFactory : WebApplicationFactory<StandaloneHostMarker>
    {
        private readonly StartupTimeline _timeline;
        private readonly IReadOnlyDictionary<string, string?> _bootstrapValues;
        private readonly EnvironmentVariableScope _environment = new(new Dictionary<string, string?>
        {
            ["SECRET_PROVIDER"] = "Environment",
            ["Database__Provider"] = "PostgreSql",
            ["Database__Host"] = "postgres.example.test",
            ["Database__Database"] = "event_test",
            ["Database__Runtime__Database"] = "event_test",
            ["Database__Migrator__Database"] = "event_test",
            ["Database__Runtime__Username"] = "event_test",
            ["Database__Runtime__Password"] = "runtime-password"
        });

        public StartupFactory(
            StartupTimeline timeline,
            IReadOnlyDictionary<string, string?> bootstrapValues)
        {
            _timeline = timeline;
            _bootstrapValues = bootstrapValues;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Staging");
            builder.UseSetting("HttpsRedirection:Enabled", "false");
            builder.UseSetting("SecretProvider:Provider", "Environment");
            builder.UseSetting("Database:Provider", "PostgreSql");
            builder.UseSetting("Database:Host", "postgres.example.test");
            builder.UseSetting("Database:Database", "event_test");
            builder.UseSetting("Database:Runtime:Database", "event_test");
            builder.UseSetting("Database:Migrator:Database", "event_test");
            builder.UseSetting("Database:Runtime:Username", "event_test");
            builder.UseSetting("Database:Runtime:Password", "runtime-password");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(BaseValues(_bootstrapValues)));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<QuartzSchemaInitializer>();
                services.RemoveAll<IConfigurationManifestPostMigrationSequence>();
                services.AddSingleton<IConfigurationManifestPostMigrationSequence>(
                    new StandaloneSequenceProbe(_timeline));
                services.RemoveAll<IInstanceBootstrapStateRepository>();
                services.AddSingleton<BootstrapRepositoryProbe>();
                services.AddSingleton<IInstanceBootstrapStateRepository>(provider =>
                    provider.GetRequiredService<BootstrapRepositoryProbe>());
                services.RemoveAll<IUnitOfWork>();
                services.AddSingleton<IUnitOfWork, UnitOfWorkProbe>();
                services.RemoveAll<ISetupSecretProvider>();
                services.AddSingleton<ISetupSecretProvider, SetupAuthorityProbe>();
                services.RemoveAll<IPrivacyErasureReplayService>();
                services.AddSingleton<IPrivacyErasureReplayService, PrivacyReplayProbe>();
                services.AddSingleton(_timeline);
                services.AddSingleton<IStartupFilter, HttpAuthorityStartupFilter>();

                services.RemoveAll<IHostedService>();
                services.AddHostedService<TokenCookieAuthorityStartupProbe>();
            });
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            _environment.Dispose();
        }

    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> _originalValues;

        public EnvironmentVariableScope(IReadOnlyDictionary<string, string?> values)
        {
            _originalValues = values.Keys.ToDictionary(
                static key => key,
                Environment.GetEnvironmentVariable,
                StringComparer.Ordinal);
            foreach ((string key, string? value) in values)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }

        public void Dispose()
        {
            foreach ((string key, string? value) in _originalValues)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    private sealed class StandaloneSequenceProbe(StartupTimeline timeline)
        : IConfigurationManifestPostMigrationSequence
    {
        public Task RunAsync(
            Func<CancellationToken, Task> migrateAndSeed,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(migrateAndSeed);
            cancellationToken.ThrowIfCancellationRequested();
            timeline.Record("migration-seed");
            timeline.Record("manifest");
            return Task.CompletedTask;
        }
    }

    private sealed class ManifestProbe : IConfigurationManifestStartupRunner
    {
        private int _runCount;
        public int RunCount => Volatile.Read(ref _runCount);

        public Task RunAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _runCount);
            return Task.CompletedTask;
        }
    }

    private sealed class UnitOfWorkProbe(StartupTimeline timeline) : IUnitOfWork
    {
        public Task ExecuteInTransactionAsync(
            Func<CancellationToken, Task> operation,
            CancellationToken ct = default) => operation(ct);

        public Task<T> ExecuteInTransactionAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken ct = default) => operation(ct);

        public Task<T> ExecuteSerializableAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken ct = default)
        {
            timeline.Record("preparation");
            return operation(ct);
        }
    }

    private sealed class BootstrapRepositoryProbe(StartupTimeline timeline)
        : IInstanceBootstrapStateRepository
    {
        private int _createCount;
        private int _updateCount;
        private int _deleteCount;

        public int CreateCount => Volatile.Read(ref _createCount);
        public int UpdateCount => Volatile.Read(ref _updateCount);
        public int DeleteCount => Volatile.Read(ref _deleteCount);

        public Task<InstanceBootstrapState?> GetCurrent(CancellationToken cancellationToken = default) =>
            Task.FromResult<InstanceBootstrapState?>(null);

        public Task<InstanceBootstrapState?> GetCurrentForUpdate(CancellationToken cancellationToken = default) =>
            Task.FromResult<InstanceBootstrapState?>(null);

        public Task<InstanceBootstrapState?> GetById(Guid id) =>
            Task.FromResult<InstanceBootstrapState?>(null);

        public Task<IReadOnlyList<InstanceBootstrapState>> GetAll() =>
            Task.FromResult<IReadOnlyList<InstanceBootstrapState>>([]);

        public Task<(IReadOnlyList<InstanceBootstrapState> Items, int TotalCount)> GetAllPaged(
            int pageNumber,
            int pageSize) =>
            Task.FromResult<(IReadOnlyList<InstanceBootstrapState>, int)>(([], 0));

        public Task<bool> Exists(Guid id) => Task.FromResult(false);

        public Task<InstanceBootstrapState> Create(InstanceBootstrapState entity)
        {
            Interlocked.Increment(ref _createCount);
            timeline.Record("bootstrap-created");
            return Task.FromResult(entity);
        }

        public Task Update(InstanceBootstrapState entity)
        {
            Interlocked.Increment(ref _updateCount);
            return Task.CompletedTask;
        }

        public Task Delete(InstanceBootstrapState entity)
        {
            Interlocked.Increment(ref _deleteCount);
            return Task.CompletedTask;
        }
    }

    private sealed class SetupAuthorityProbe(StartupTimeline timeline) : ISetupSecretProvider
    {
        public bool IsSetupModeActive => false;
        public bool IsSetupSecretRequired => true;
        public bool IsFromEnvironmentVariable => false;

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            timeline.Record("setup-authority");
            return Task.CompletedTask;
        }

        public bool ValidateSecret(string? secret) => false;
        public void Lock() { }
    }

    private sealed class TokenCookieAuthorityStartupProbe(StartupTimeline timeline) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            timeline.Record("token-authority");
            timeline.Record("cookie-authority");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class PrivacyReplayProbe : IPrivacyErasureReplayService
    {
        public Task ReplayAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class HttpAuthorityStartupFilter(StartupTimeline timeline) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, following) =>
            {
                timeline.Record("http");
                await following();
            });
            next(app);
        };
    }

    private sealed class OperatorIdentityProbe : IInstanceOperatorIdentity
    {
        public Guid OperatorId => Guid.Parse("0198e2a4-5340-7f89-8abc-b8bdf43e0ea8");
        public string PublicName => "Startup Test Operator";
        public string LegalName => "Startup Test Operator ASBL";
        public bool IsOfficialInstance => false;
        public string OfficialOrigin => "https://standalone.example.test";
        public string OperatorKindCode => "registered_organization";
        public string JurisdictionCountryCode => "BE";
        public string RegistrationIdentifier => "BE 0123.456.789";
        public string PublicContactEmail => "contact@standalone.example.test";
        public string WebsiteUrl => "https://standalone.example.test";
        public string LegalNoticeUrl => "https://standalone.example.test/legal";
        public string TermsUrl => "https://standalone.example.test/terms";
        public string PrivacyUrl => "https://standalone.example.test/privacy";
    }

    private sealed class StartupTimeline
    {
        private readonly ConcurrentQueue<string> _events = [];
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _signals =
            new(StringComparer.Ordinal);

        public string[] Events => _events.ToArray();

        public void Record(string value)
        {
            _events.Enqueue(value);
            _signals.GetOrAdd(value, static _ => NewSignal()).TrySetResult();
        }

        public int Count(string value) =>
            _events.Count(item => string.Equals(item, value, StringComparison.Ordinal));

        public Task WaitForAsync(string value, TimeSpan timeout) =>
            _signals.GetOrAdd(value, static _ => NewSignal()).Task.WaitAsync(timeout);

        private static TaskCompletionSource NewSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

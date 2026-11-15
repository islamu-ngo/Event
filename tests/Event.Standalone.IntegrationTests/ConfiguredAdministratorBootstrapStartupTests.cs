using System.Collections.Concurrent;
using System.Net;
using Event.Testing.DataProtection;
using Explore.API.Hosting;
using Explore.API.Scheduling;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Domain;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.ServiceDefaults.Secrets;
using Event.Standalone.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
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
    [Arguments(false)]
    [Arguments(true)]
    public async Task SplitWaitsForMigrationThenRunsManifestAndPreparationExactlyOnceBeforeHttp(bool manifestFails)
    {
        await using var timeline = new StartupTimeline();
        timeline.Record("migration-dependency");

        var dataProtection = new ExternalDataProtectionTestAuthority("api").Input;
        using var environment = new EnvironmentVariableScope(WithDatabaseAuthorities(new Dictionary<string, string?>(ConfiguredValues())
        {
            ["SecretProvider__Provider"] = "Environment",
            ["EVENT_DP_API_RING"] = dataProtection.Ring,
            ["EVENT_DP_API_ADMISSION"] = dataProtection.Admission,
            ["EVENT_DP_API_ADMISSION_SHA256"] = dataProtection.AdmissionSha256,
            ["EVENT_DP_API_MINIMUM_GENERATION"] = "1",
            ["Keycloak__Authority"] = "https://authority.example.test",
            ["Database__Provider"] = "PostgreSql",
            ["Database__Host"] = "postgres.example.test",
            ["Database__Database"] = "event_test",
            ["Database__Runtime__Database"] = "event_test",
            ["Database__Migrator__Database"] = "event_test",
            ["Database__Runtime__Username"] = "event_test",
            ["Database__Global__Username"] = "event_test_global",
            ["Database__Global__Password"] =
                Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
            ["Database__Runtime__Password"] =
                Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
        }));
        await using var authority = await SecretAuthorityBootstrap.OpenAsync(
            new ConfigurationBuilder().AddEnvironmentVariables().Build(), "Staging", SecretConsumerRole.Api,
            [new("/runtime/api/authentication/keycloak", "KEYCLOAK_ENDPOINT", "KEYCLOAK_ENDPOINT")],
            new SecretAuthorityLoginAttempt());
        await authority.StartAuthenticationMaintenanceAsync();
        await using WebApplication app = await BuildSplitHostAsync(timeline, authority);
        using var shutdown = new CancellationTokenSource();
        var state = new ApiHostCompositionState(
            IsOpenApiGeneration: false,
            UseQuartzScheduler: false,
            HttpsRedirectionEnabled: false,
            OwnsDevelopmentMigrations: false,
            OwnsConfigurationManifestStartup: true);

        var manifest = app.Services.GetRequiredService<ManifestProbe>();
        if (manifestFails)
        {
            var failure = new InvalidOperationException("Injected manifest startup failure.");
            manifest.Failure = failure;
            var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                app.RunApiHostStartupAsync(state, shutdown, () => { }));
            await Assert.That(thrown).IsSameReferenceAs(failure);
            await Assert.That(timeline.Events).IsEquivalentTo(
                ["migration-dependency", "manifest"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
            await Assert.That(manifest.RunCount).IsEqualTo(1);
            return;
        }
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
            ["migration-dependency", "manifest", "preparation", "bootstrap-created", "setup-authority", "http-ready", "http"],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(timeline.Count("preparation")).IsEqualTo(1);
        await Assert.That(app.Services.GetRequiredService<ManifestProbe>().RunCount).IsEqualTo(1);

        await app.StopAsync(startupTimeout.Token);
    }

    [Test]
    public async Task StandaloneRunsManifestAndPreparationAfterExternalMigrationWithoutMigratorCredentials()
    {
        await using var timeline = new StartupTimeline();
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
        await Assert.That(factory.Services.GetRequiredService<IConfiguration>()["Database:Migrator:Password"]).IsNull();
        await Assert.ThrowsAsync<Microsoft.Extensions.Options.OptionsValidationException>(() =>
        {
            Explore.Persistence.Database.PrimaryDatabaseConfiguration.BindMigrator(
                factory.Services.GetRequiredService<IConfiguration>());
            return Task.CompletedTask;
        });
        await Assert.That(timeline.Events).IsEquivalentTo(
            ["migration-seed", "manifest", "preparation", "bootstrap-created", "setup-authority", "token-authority", "cookie-authority", "http"],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(timeline.Count("migration-seed")).IsEqualTo(1);
        await Assert.That(timeline.Count("manifest")).IsEqualTo(1);
        await Assert.That(timeline.Count("preparation")).IsEqualTo(1);
        await Assert.That(timeline.Count("bootstrap-created")).IsEqualTo(1);
    }

    [Test]
    public async Task StandaloneManifestFailureAfterExternalMigrationBlocksPreparationAndHttp()
    {
        await using var timeline = new StartupTimeline();
        var failure = new InvalidOperationException("Injected manifest startup failure.");
        await using var factory = new StartupFactory(timeline, ConfiguredValues(), manifestFailure: failure);

        Exception? thrown = null;
        try
        {
            _ = factory.CreateClient();
        }
        catch (Exception exception)
        {
            thrown = exception;
        }

        await Assert.That(thrown).IsNotNull();
        await Assert.That(FlattenMessages(thrown!)).Contains(failure.Message);
        await Assert.That(timeline.Events).IsEquivalentTo(
            ["migration-seed", "manifest"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(timeline.Count("manifest")).IsEqualTo(1);
        await Assert.That(timeline.Count("preparation")).IsEqualTo(0);
        await Assert.That(timeline.Count("setup-authority")).IsEqualTo(0);
        await Assert.That(timeline.Count("token-authority")).IsEqualTo(0);
        await Assert.That(timeline.Count("cookie-authority")).IsEqualTo(0);
        await Assert.That(timeline.Count("http")).IsEqualTo(0);
    }

    [Test]
    public async Task InteractiveModePreparationIsAStartupNoOp()
    {
        await using var timeline = new StartupTimeline();
        await using var factory = new StartupFactory(timeline, InteractiveValues());

        using HttpClient client = factory.CreateClient();
        using var requestTimeout = new CancellationTokenSource(SignalTimeout);
        using HttpResponseMessage response = await client.GetAsync("/alive", requestTimeout.Token);
        await timeline.WaitForAsync("http", SignalTimeout);

        await using var inspection = factory.Services.CreateAsyncScope();
        var repository = inspection.ServiceProvider.GetRequiredService<BootstrapRepositoryProbe>();
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(timeline.Events).IsEquivalentTo(
            ["migration-seed", "manifest", "preparation", "setup-authority", "token-authority", "cookie-authority", "http"],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(repository.CreateCount).IsEqualTo(0);
        await Assert.That(repository.UpdateCount).IsEqualTo(0);
        await Assert.That(repository.DeleteCount).IsEqualTo(0);
        await using var database = await timeline.Database.CreateDbContextAsync();
        await Assert.That(await new InstanceBootstrapStateRepository(database).GetCurrent()).IsNull();
    }

    [Test]
    public async Task StandaloneBootsCleanlyWithoutConfiguredOperatorIdentity()
    {
        await using var timeline = new StartupTimeline();
        await using var factory = new StartupFactory(timeline, ConfiguredValues(), includeOperatorIdentity: false);

        using HttpClient client = factory.CreateClient();
        using var requestTimeout = new CancellationTokenSource(SignalTimeout);
        using HttpResponseMessage response = await client.GetAsync("/alive", requestTimeout.Token);
        await timeline.WaitForAsync("http", SignalTimeout);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task PreparationFailureBlocksSetupCookieTokenAndHttpAuthorityWithoutRetry()
    {
        await using var timeline = new StartupTimeline();
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

    private static async Task<WebApplication> BuildSplitHostAsync(
        StartupTimeline timeline, SecretAuthorityAcquisition authority)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Staging"
        });
        builder.WebHost.UseTestServer();
        builder.AddServiceDefaults();
        builder.Configuration.AddInMemoryCollection(BaseValues(ConfiguredValues()));
        await builder.PrepareApiHostSecretsAsync(authority, SecretConsumerRole.Api);
        builder.AddApiHostServices(static () => false);
        builder.Services.RemoveAll<IHostedService>();
        builder.Services.RemoveAll<IInstanceOperatorIdentity>();
        builder.Services.RemoveAll<IInstanceBootstrapStateRepository>();
        builder.Services.RemoveAll<IUnitOfWork>();
        builder.Services.RemoveAll<IConfigurationManifestStartupRunner>();
        builder.Services.RemoveAll<ISetupSecretProvider>();
        builder.Services.RemoveAll<IPrivacyErasureReplayService>();
        builder.Services.AddSingleton<IInstanceOperatorIdentity, OperatorIdentityProbe>();
        builder.Services.AddSingleton(timeline);
        builder.Services.AddScoped<BootstrapRepositoryProbe>();
        builder.Services.AddScoped<IInstanceBootstrapStateRepository>(services =>
            services.GetRequiredService<BootstrapRepositoryProbe>());
        builder.Services.AddScoped<IUnitOfWork, UnitOfWorkProbe>();
        builder.Services.RemoveAll<IGlobalExploreDbContextFactory>();
        builder.Services.AddSingleton<IGlobalExploreDbContextFactory>(timeline.Database);
        builder.Services.AddSingleton<ManifestProbe>();
        builder.Services.AddSingleton<IConfigurationManifestStartupRunner>(services =>
            services.GetRequiredService<ManifestProbe>());
        builder.Services.AddSingleton<ISetupSecretProvider, SetupAuthorityProbe>();
        builder.Services.AddSingleton<IPrivacyErasureReplayService, PrivacyReplayProbe>();
        builder.Services.AddSingleton(TimeProvider.System);

        return builder.Build();
    }

    private static Dictionary<string, string?> BaseValues(
        IReadOnlyDictionary<string, string?> bootstrapValues,
        bool includeOperatorIdentity = true)
    {
        var values = new Dictionary<string, string?>(bootstrapValues, StringComparer.Ordinal)
        {
            ["Deployment:Mode"] = "SingleTenant",
            ["Keycloak:Authority"] = "https://authority.example.test"
        };
        if (includeOperatorIdentity)
        {
            values["Instance:OperatorIdentity:OperatorId"] = "0198e2a4-5340-7f89-8abc-b8bdf43e0ea8";
            values["Instance:OperatorIdentity:PublicName"] = "Startup Test Operator";
            values["Instance:OperatorIdentity:LegalName"] = "Startup Test Operator ASBL";
            values["Instance:OperatorIdentity:IsOfficialInstance"] = "false";
            values["Instance:OperatorIdentity:OfficialOrigin"] = "https://standalone.example.test";
            values["Instance:OperatorIdentity:OperatorKindCode"] = "registered_organization";
            values["Instance:OperatorIdentity:JurisdictionCountryCode"] = "BE";
            values["Instance:OperatorIdentity:RegistrationIdentifier"] = "BE 0123.456.789";
            values["Instance:OperatorIdentity:PublicContactEmail"] = "contact@standalone.example.test";
            values["Instance:OperatorIdentity:WebsiteUrl"] = "https://standalone.example.test";
            values["Instance:OperatorIdentity:LegalNoticeUrl"] = "https://standalone.example.test/legal";
            values["Instance:OperatorIdentity:TermsUrl"] = "https://standalone.example.test/terms";
            values["Instance:OperatorIdentity:PrivacyUrl"] = "https://standalone.example.test/privacy";
        }
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

    private static Dictionary<string, string?> WithDatabaseAuthorities(Dictionary<string, string?> values)
    {
        foreach (var role in Enum.GetValues<Explore.Persistence.Database.PrimaryDatabaseRole>())
        {
            values.TryAdd($"Database__{role}__Username", $"event_test_{role}");
            if (role != Explore.Persistence.Database.PrimaryDatabaseRole.Migrator)
                values.TryAdd($"Database__{role}__Password",
                    Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
        }
        values["Database__Migrator__Password"] = null;
        values["DATABASE_MIGRATOR_PASSWORD"] = null;
        return values;
    }

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
        private readonly bool _includeOperatorIdentity;
        private readonly Exception? _manifestFailure;
        private readonly EnvironmentVariableScope _environment;

        public StartupFactory(
            StartupTimeline timeline,
            IReadOnlyDictionary<string, string?> bootstrapValues,
            bool includeOperatorIdentity = true,
            Exception? manifestFailure = null)
        {
            var dataProtection = new ExternalDataProtectionTestAuthority("combined").Input;
            _environment = new(WithDatabaseAuthorities(new Dictionary<string, string?>(bootstrapValues)
            {
                ["SECRET_PROVIDER"] = "Environment",
                ["EVENT_DP_COMBINED_RING"] = dataProtection.Ring,
                ["EVENT_DP_COMBINED_ADMISSION"] = dataProtection.Admission,
                ["EVENT_DP_COMBINED_ADMISSION_SHA256"] = dataProtection.AdmissionSha256,
                ["EVENT_DP_COMBINED_MINIMUM_GENERATION"] = "1",
                ["Database__Provider"] = "PostgreSql",
                ["Database__Host"] = "postgres.example.test",
                ["Database__Database"] = "event_test",
                ["Database__Runtime__Database"] = "event_test",
                ["Database__Migrator__Database"] = "event_test",
                ["Database__Migrator__Password"] = null,
                ["DATABASE_MIGRATOR_PASSWORD"] = null,
                ["Database__Runtime__Username"] = "event_test",
                ["Database__Global__Username"] = "event_test_global",
                ["Database__Global__Password"] =
                    Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
                ["Database__Runtime__Password"] =
                    Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
            }));
            _timeline = timeline;
            _bootstrapValues = bootstrapValues;
            _includeOperatorIdentity = includeOperatorIdentity;
            _manifestFailure = manifestFailure;
            // The external migration dependency has already completed before host startup.
            _timeline.Record("migration-seed");
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
            builder.UseSetting("Database:Runtime:Password",
                Environment.GetEnvironmentVariable("Database__Runtime__Password")!);
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(BaseValues(_bootstrapValues, _includeOperatorIdentity)));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<QuartzSchemaInitializer>();
                services.RemoveAll<IConfigurationManifestPostMigrationSequence>();
                services.AddSingleton<IConfigurationManifestPostMigrationSequence>(
                    new StandaloneSequenceProbe(_timeline));
                services.RemoveAll<IConfigurationManifestStartupRunner>();
                services.AddSingleton<IConfigurationManifestStartupRunner>(
                    new ManifestProbe(_timeline) { Failure = _manifestFailure });
                services.RemoveAll<IInstanceBootstrapStateRepository>();
                services.AddScoped<BootstrapRepositoryProbe>();
                services.AddScoped<IInstanceBootstrapStateRepository>(provider =>
                    provider.GetRequiredService<BootstrapRepositoryProbe>());
                services.RemoveAll<IUnitOfWork>();
                services.AddScoped<IUnitOfWork, UnitOfWorkProbe>();
                services.RemoveAll<IGlobalExploreDbContextFactory>();
                services.AddSingleton<IGlobalExploreDbContextFactory>(_timeline.Database);
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
        public async Task RunAsync(
            Func<CancellationToken, Task> migrateAndSeed,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(migrateAndSeed);
            cancellationToken.ThrowIfCancellationRequested();
            await migrateAndSeed(cancellationToken);
            timeline.Record("manifest");
        }
    }

    private sealed class ManifestProbe(StartupTimeline timeline) : IConfigurationManifestStartupRunner
    {
        private int _runCount;
        public int RunCount => Volatile.Read(ref _runCount);
        internal Exception? Failure { get; set; }

        public Task RunAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _runCount);
            timeline.Record("manifest");
            if (Failure is { } failure)
                throw failure;
            return Task.CompletedTask;
        }
    }

    private sealed class UnitOfWorkProbe(ExploreDbContext database, StartupTimeline timeline) : IUnitOfWork
    {
        private readonly IUnitOfWork _inner = new EfCoreUnitOfWork(database);

        public Task<T> ExecuteReadCommittedAsync<T>(
            Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) =>
            _inner.ExecuteReadCommittedAsync(operation, ct);

        public Task ExecuteInTransactionAsync(
            Func<CancellationToken, Task> operation,
            CancellationToken ct = default) => _inner.ExecuteInTransactionAsync(operation, ct);

        public Task<T> ExecuteInTransactionAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken ct = default) => _inner.ExecuteInTransactionAsync(operation, ct);

        public Task<T> ExecuteSerializableAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken ct = default) => _inner.ExecuteSerializableAsync(operation, ct);

        public Task<T> ExecuteBootstrapConvergenceAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken ct = default)
        {
            timeline.Record("preparation");
            return _inner.ExecuteBootstrapConvergenceAsync(operation, ct);
        }
    }

    private sealed class BootstrapRepositoryProbe(ExploreDbContext database, StartupTimeline timeline)
        : IInstanceBootstrapStateRepository
    {
        private readonly InstanceBootstrapStateRepository _inner = new(database);

        public int CreateCount => timeline.Count("bootstrap-created");
        public int UpdateCount => timeline.Count("bootstrap-updated");
        public int DeleteCount => timeline.Count("bootstrap-deleted");

        public Task<InstanceBootstrapState?> GetCurrent(CancellationToken cancellationToken = default) =>
            _inner.GetCurrent(cancellationToken);

        public Task<InstanceBootstrapState?> GetCurrentForUpdate(CancellationToken cancellationToken = default) =>
            _inner.GetCurrentForUpdate(cancellationToken);

        public Task<InstanceBootstrapState?> GetById(Guid id) =>
            _inner.GetById(id);

        public Task<IReadOnlyList<InstanceBootstrapState>> GetAll() =>
            _inner.GetAll();

        public Task<(IReadOnlyList<InstanceBootstrapState> Items, int TotalCount)> GetAllPaged(
            int pageNumber,
            int pageSize) =>
            _inner.GetAllPaged(pageNumber, pageSize);

        public Task<bool> Exists(Guid id) => _inner.Exists(id);

        public async Task<InstanceBootstrapState> Create(InstanceBootstrapState entity)
        {
            var created = await _inner.Create(entity);
            timeline.Record("bootstrap-created");
            return created;
        }

        public async Task Update(InstanceBootstrapState entity)
        {
            await _inner.Update(entity);
            timeline.Record("bootstrap-updated");
        }

        public async Task Delete(InstanceBootstrapState entity)
        {
            await _inner.Delete(entity);
            timeline.Record("bootstrap-deleted");
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

    private sealed class StartupDatabase : IGlobalExploreDbContextFactory, IAsyncDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("bootstrap-startup-").FullName;
        private readonly PrimaryDatabaseConnectionOptions _options;
        private readonly GlobalExploreDbContextFactory _factory;

        public StartupDatabase()
        {
            _options = new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Global,
                Provider = PrimaryDatabaseProvider.Sqlite,
                Database = Path.Combine(_root, "bootstrap.db")
            };
            _factory = new GlobalExploreDbContextFactory(_options);
        }

        public async Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            var database = await _factory.CreateDbContextAsync(cancellationToken);
            try
            {
                await database.Database.EnsureCreatedAsync(cancellationToken);
                return database;
            }
            catch
            {
                await database.DisposeAsync();
                throw;
            }
        }

        public ValueTask DisposeAsync()
        {
            using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                PrimaryDatabaseConfiguration.BuildConnectionString(_options).ConnectionString);
            Microsoft.Data.Sqlite.SqliteConnection.ClearPool(connection);
            Directory.Delete(_root, recursive: true);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StartupTimeline : IAsyncDisposable
    {
        private readonly ConcurrentQueue<string> _events = [];
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _signals =
            new(StringComparer.Ordinal);

        public StartupDatabase Database { get; } = new();
        public string[] Events => _events.ToArray();

        public ValueTask DisposeAsync() => Database.DisposeAsync();

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

using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Event.Testing.DataProtection;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Infrastructure.Services;
using Explore.Persistence.Schema;
using Explore.Secrets.Database;
using Explore.ServiceDefaults.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Event.Benchmarks.Api;

internal sealed class PostgreSqlApiBenchmarkHostFactory : WebApplicationFactory<global::Program>
{
    private readonly ExternalDataProtectionTestAuthority _dataProtection = new("api");
    private readonly BenchmarkPostgreSqlAuthority _database;
    private readonly Dictionary<string, string?> _settings;
    private readonly ConfigurationRoot _configuration;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"benchmark-native-{Guid.CreateVersion7():N}");
    private readonly byte[] _blocklist;
    private bool _ownsRoot;

    internal string BootstrapIdentifier { get; } = $"benchmark-{Guid.CreateVersion7():N}@example.invalid";
    internal string BootstrapPassword => RequiredEnvironment("INSTANCE_BOOTSTRAP_LOCAL_PASSWORD");
    internal string ReplacementPassword => RequiredEnvironment("ISLAMU_TEST_REPLACEMENT_PASSWORD");

    internal PostgreSqlApiBenchmarkHostFactory(BenchmarkPostgreSqlAuthority database)
    {
        _database = database;
        _blocklist = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Version = 1,
            Revision = Guid.CreateVersion7().ToString("N"),
            Source = "Project-owned native benchmark password policy",
            License = "Project-owned generated test data",
            Passwords = new[] { Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) }
        });
        string blocklistPath = Path.Combine(_root, "password-blocklist.json");
        _settings = new Dictionary<string, string?>
        {
            ["SecretProvider:Provider"] = "Environment",
            ["Testing:UseRealDatabase"] = "true",
            ["Testing:ApplyMigrations"] = "false",
            ["Testing:SkipJwtAuthorityWarmup"] = "true",
            ["Authentication:Provider"] = "local",
            ["Authorization:Provider"] = "local",
            ["AUTHENTICATION_PROVIDER"] = "Local",
            ["AUTHORIZATION_PROVIDER"] = "Local",
            ["Authentication:Local:PasswordBlocklistPath"] = blocklistPath,
            ["Authentication:Local:PasswordBlocklistSha256"] = Convert.ToHexString(SHA256.HashData(_blocklist)),
            ["AUTHENTICATION_LOCAL_PASSWORD_BLOCKLIST_PATH"] = blocklistPath,
            ["AUTHENTICATION_LOCAL_PASSWORD_BLOCKLIST_SHA256"] = Convert.ToHexString(SHA256.HashData(_blocklist)),
            ["INSTANCE_BOOTSTRAP_MODE"] = "ConfiguredAdministrator",
            ["INSTANCE_BOOTSTRAP_ADMIN_PROVIDER"] = "local",
            ["INSTANCE_BOOTSTRAP_ADMIN_SUBJECT"] = Guid.CreateVersion7().ToString("D"),
            ["INSTANCE_BOOTSTRAP_ADMIN_EMAIL"] = BootstrapIdentifier,
            ["INSTANCE_BOOTSTRAP_BINDING_GENERATION"] = "1",
            ["Deployment:Mode"] = "MultiTenant",
            ["DEPLOYMENT_MODE"] = "MultiTenant",
            ["IdentityDatabase:Topology"] = "colocated",
            ["IDENTITY_DATABASE_TOPOLOGY"] = "colocated",
            ["PrivacyErasure:Authority:Topology"] = "EmbeddedSqlite",
            ["PrivacyErasureAuthorityEmbedded:Path"] = Path.Combine(_root, "privacy.db"),
            ["PrivacyErasureAuthorityEmbedded:WriterReplicaCount"] = "1",
            ["SecretRefresh:Enabled"] = "false",
            ["PdsSync:Enabled"] = "false",
            ["OutboxProcessor:Enabled"] = "false",
            ["EmailDispatchProcessor:Enabled"] = "false",
            ["EmailDispatchRabbitMq:Enabled"] = "false",
            ["PublicBaseUrl"] = "https://benchmark.event.local"
        };
        foreach (var role in Enum.GetValues<PrimaryDatabaseRole>())
        {
            var options = database.ForRole(role);
            string prefix = $"Database:{role}:";
            _settings[prefix + "Provider"] = options.Provider.ToString();
            _settings[prefix + "Host"] = options.Host;
            _settings[prefix + "Port"] = options.Port?.ToString(CultureInfo.InvariantCulture);
            _settings[prefix + "Database"] = options.Database;
            _settings[prefix + "Schema"] = options.Schema;
            _settings[prefix + "Username"] = options.Username;
            _settings[prefix + "Password"] = options.Password;
            _settings[prefix + "TlsMode"] = options.TlsMode.ToString();
        }
        _configuration = (ConfigurationRoot)new ConfigurationBuilder()
            .AddConfiguration(database.Configuration).AddInMemoryCollection(_settings).Build();
    }

    internal async Task InitializeDatabaseAsync(CancellationToken cancellationToken)
    {
        if (Directory.Exists(_root))
            throw new InvalidOperationException("benchmark_native_root_already_exists");
        Directory.CreateDirectory(_root);
        _ownsRoot = true;
        await File.WriteAllBytesAsync(Path.Combine(_root, "password-blocklist.json"), _blocklist, cancellationToken);
        _ = BootstrapPassword;
        _ = ReplacementPassword;
        _ = RequiredEnvironment("AUTHENTICATION_LOCAL_JWT_KEY");
        await ExploreDatabaseMigrator.MigrateAndSeedAsync(new BenchmarkDatabaseEnvironment(), _configuration,
            _database.Migrator, NullLogger.Instance, seedDevelopmentData: false, cancellationToken);
    }

    internal HttpClient CreateAnonymousClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = false,
        BaseAddress = new Uri("https://benchmark.event.local")
    });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSolutionRelativeContentRoot("src/Explore.API", "Explore.slnx");
        foreach (var setting in _settings)
            builder.UseSetting(setting.Key, setting.Value);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddConfiguration(_configuration));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDistributedCache>();
            services.AddDistributedMemoryCache();
            services.RemoveAll<IOutputCacheStore>();
            services.AddSingleton<IOutputCacheStore, BenchmarkNoOpOutputCacheStore>();
            services.RemoveAll<IHostedService>();
            services.AddHostedService<NativeBenchmarkStartup>();
            services.RemoveAll<IExternalDataProtectionSource>();
            services.AddSingleton<IExternalDataProtectionSource>(_dataProtection);
            services.RemoveAll<ILoggerProvider>();
            services.RemoveAll<ILoggerFactory>();
            services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        });
    }

    public override async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        try { await base.DisposeAsync(); }
        catch (Exception failure) { failures.Add(failure); }
        try { _configuration.Dispose(); }
        catch (Exception failure) { failures.Add(failure); }
        if (_ownsRoot)
        {
            try { Directory.Delete(_root, recursive: true); _ownsRoot = false; }
            catch (Exception failure) { failures.Add(failure); }
        }
        if (failures.Count != 0)
            throw new AggregateException("benchmark_native_host_cleanup_failed", failures);
    }

    private static string RequiredEnvironment(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException("benchmark_native_local_authority_missing") : value;
    }

    private sealed class NativeBenchmarkStartup(IServiceProvider services) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            using var scope = services.CreateScope();
            await using (var selected = await scope.ServiceProvider
                .GetRequiredService<IPolicyMutationExecutionScopeFactory>()
                .CreateAsync(PolicyMutationScope.Instance, cancellationToken))
            {
                var provider = ActivatorUtilities.CreateInstance<ConfiguredAdministratorBootstrapProvider>(selected.Services);
                var runner = ActivatorUtilities.CreateInstance<ConfiguredAdministratorBootstrapStartupRunner>(selected.Services, provider);
                await runner.PrepareAsync(cancellationToken);
                await selected.Services.GetRequiredService<LocalAdministratorBootstrapRunner>().RunAsync(cancellationToken);
            }
            await scope.ServiceProvider.GetRequiredService<ILookupDataCache>().RefreshAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class BenchmarkDatabaseEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = typeof(global::Program).Assembly.GetName().Name!;
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

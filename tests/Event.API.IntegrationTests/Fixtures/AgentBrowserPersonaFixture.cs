using System.Net;
using System.Data.Common;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.API.Hosting;
using Explore.Application.Constants;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Schema;
using Explore.Persistence.Seed;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Event.Api.IntegrationTests.Fixtures;

internal sealed partial class AgentBrowserPersonaFixture : IAsyncDisposable
{
    private static readonly PostgreSqlContainer Container = new PostgreSqlBuilder(
        "postgis/postgis:18-3.6-alpine@sha256:ffcf0c4b904e41b9779f8098007fb5a9484025319c18c70cf8e1bcebb742b9b7")
        .WithDatabase("islamu_event_agent").WithUsername("postgres")
        .WithPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(32))).Build();
    private static readonly string MigrationAuthorityDirectory = Path.Combine(
        Path.GetTempPath(), $"agent-browser-migration-{Guid.NewGuid():N}");
    private readonly TestSecrets _secrets = new();
    private readonly string _erasureDirectory = Path.Combine(
        Path.GetTempPath(), $"agent-browser-erasure-{Guid.NewGuid():N}");
    private static readonly Lazy<Task<TestDatabaseReset>> Initialization = new(InitializeDatabaseAsync);
    private readonly BoundaryFault _fault = new();
    private NativeFactory? _factory;
    private HttpClient? _client;
    private IConfigurationRoot _configuration = null!;
    private readonly string _replacementPassword = NewPassword();
    private string _finalPassword = null!;
    private static CancellationToken Token => TestContext.Current!.Execution.CancellationToken;

    internal IServiceProvider Services => _factory!.Services;

    internal static async Task<AgentBrowserPersonaFixture> CreateAsync(bool enableReset = false,
        Action<IServiceCollection>? configure = null, string? resetPipeName = null)
    {
        var fixture = new AgentBrowserPersonaFixture();
        try
        {
            Directory.CreateDirectory(fixture._erasureDirectory);
            await (await Initialization.Value).ResetAsync();
            await using (var database = fixture.CreateDatabase())
            {
                var agentRouting = await database.Set<SystemSetting>().SingleOrDefaultAsync(row =>
                    row.Id == AgentBrowserPersonaCatalog.Id(324), Token);
                if (agentRouting is not null)
                    database.Set<SystemSetting>().Remove(agentRouting);
                var baseDomain = await database.Set<SystemSetting>().SingleAsync(row =>
                    row.SettingKey == GovernanceSettingKeys.Domains.InstanceBaseDomain, Token);
                baseDomain.Value = "\"\"";
                var storageProvider = await database.Set<SystemSetting>().SingleAsync(row =>
                    row.SettingKey == GovernanceSettingKeys.Storage.Provider, Token);
                storageProvider.Value = $"\"{StorageProviders.Local}\"";
                await database.SaveChangesAsync(Token);
                await LookupTableSeeder.SeedAsync(database, Token);
            }
            var values = new Dictionary<string, string?>
            {
                ["AGENT_BROWSER_SEED_ENABLED"] = "true",
                ["ISLAMU_ASPIRE_MODE"] = "AgentBrowser",
                ["Hosting:Topology"] = "Split",
                ["IdentityDatabase:Topology"] = "colocated",
                ["Authentication:Provider"] = "local",
                ["Authorization:Provider"] = "local",
                ["SecretProvider:Provider"] = "Environment",
                ["Authentication:Local:JwtKey"] = fixture._secrets.Values[SecretDefinitionRegistry.Keys.Authentication.LocalJwtKey],
                ["CONFIGURATION_MANIFEST_MODE"] = "Off",
                ["PrivacyErasure:Authority:Topology"] = "EmbeddedSqlite",
                ["PrivacyErasureAuthorityEmbedded:Path"] = Path.Combine(fixture._erasureDirectory, "authority.db"),
                ["WEBHOOKS_PROVIDER"] = "Local",
                ["INSTANCE_BOOTSTRAP_MODE"] = "ConfiguredAdministrator",
                ["INSTANCE_BOOTSTRAP_ADMIN_PROVIDER"] = "local",
                ["INSTANCE_BOOTSTRAP_ADMIN_SUBJECT"] = AgentBrowserPersonaCatalog.Administrator.SubjectId.ToString("D"),
                ["INSTANCE_BOOTSTRAP_BINDING_GENERATION"] = "1",
                ["INSTANCE_BOOTSTRAP_ADMIN_EMAIL"] = AgentBrowserPersonaCatalog.Administrator.Email,
                ["INSTANCE_BOOTSTRAP_ADMIN_FIRST_NAME"] = AgentBrowserPersonaCatalog.Administrator.Name,
                ["INSTANCE_BOOTSTRAP_ADMIN_LAST_NAME"] = "Agent",
                ["Deployment:Mode"] = "MultiTenant",
                ["Testing:SkipJwtAuthorityWarmup"] = "true",
                ["SETUP_SECRET"] = NewPassword(),
                ["SETUP_SECRET_FILE"] = Path.Combine(Path.GetTempPath(), $"agent-setup-{Guid.CreateVersion7():N}"),
                ["OutboxProcessor:Enabled"] = "false",
                ["EmailDispatchProcessor:Enabled"] = "false"
            };
            if (resetPipeName is not null) values["AgentBrowser:ResetPipeName"] = resetPipeName;
            TestDatabaseConfiguration.AddPostgreSql(values, Container.GetConnectionString());
            fixture._configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
            fixture._finalPassword = fixture._secrets.Values[SecretDefinitionRegistry.Keys.Authentication.AgentBrowserPersonaPassword];
            fixture._factory = new NativeFactory(Container.GetConnectionString(), values, fixture._secrets, fixture._fault, enableReset, configure);
            fixture._client = fixture._factory.CreateClient();
            fixture._client.BaseAddress = new Uri("http://default.localhost");
            fixture._client.DefaultRequestHeaders.Add(TenantHeaderNames.TenantSlug, "default");
            return fixture;
        }
        catch { await fixture.DisposeAsync(); throw; }
    }

    internal ExploreDbContext CreateDatabase()
    {
        var db = new ExploreDbContext(new DbContextOptionsBuilder<ExploreDbContext>()
            .UseNpgsql(Container.GetConnectionString()).UseSnakeCaseNamingConvention().Options);
        db.EnableTenantFilterBypass(TenantFilterBypassReasons.DatabaseSeeding);
        return db;
    }

    internal Task RunAsync() => AgentBrowserPersonaStartup.RunAsync(_factory!.Services, _configuration,
        new HostingEnvironment { EnvironmentName = "Development" }, Token);

    internal async Task MigrateAsync()
    {
        await using var connection = new NpgsqlConnection(Container.GetConnectionString());
        await connection.OpenAsync(Token);
        await using var history = new NpgsqlCommand(
            "SELECT COUNT(*) FROM islamu_event.\"__EFMigrationsHistory\"", connection);
        if ((long)(await history.ExecuteScalarAsync(Token))! == 0)
            throw new InvalidOperationException("agent_browser_fixture_migration_history_missing");

        await using var database = CreateDatabase();
        await ExploreDatabaseMigrator.MigrateAndSeedAsync(database, new HostingEnvironment { EnvironmentName = "Development" },
            _configuration, PrimaryDatabaseConfiguration.BindRuntime(_configuration) with { Role = PrimaryDatabaseRole.Migrator }, NullLogger.Instance, Token);
    }

    internal void InterruptAt(string boundary) => _fault.Boundary = boundary;
    internal bool InterruptionObserved => _fault.Observed;

    private static async Task<TestDatabaseReset> InitializeDatabaseAsync()
    {
        await Container.StartAsync(Token);
        Directory.CreateDirectory(MigrationAuthorityDirectory);
        var values = new Dictionary<string, string?>
        {
            ["AGENT_BROWSER_SEED_ENABLED"] = "true",
            ["ISLAMU_ASPIRE_MODE"] = "AgentBrowser",
            ["Hosting:Topology"] = "Split",
            ["IdentityDatabase:Topology"] = "colocated",
            ["Authentication:Provider"] = "local",
            ["Authorization:Provider"] = "local",
            ["CONFIGURATION_MANIFEST_MODE"] = "Off",
            ["PrivacyErasure:Authority:Topology"] = "EmbeddedSqlite",
            ["PrivacyErasureAuthorityEmbedded:Path"] = Path.Combine(MigrationAuthorityDirectory, "authority.db"),
            ["WEBHOOKS_PROVIDER"] = "Local"
        };
        TestDatabaseConfiguration.AddPostgreSql(values, Container.GetConnectionString());
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        await using var database = new ExploreDbContext(new DbContextOptionsBuilder<ExploreDbContext>()
            .UseNpgsql(Container.GetConnectionString()).UseSnakeCaseNamingConvention().Options);
        await ExploreDatabaseMigrator.MigrateAndSeedAsync(database, new HostingEnvironment { EnvironmentName = "Development" },
            configuration, PrimaryDatabaseConfiguration.BindRuntime(configuration) with { Role = PrimaryDatabaseRole.Migrator }, NullLogger.Instance, Token);
        await Assert.That(await database.Users.AnyAsync(Token)).IsFalse();
        await Assert.That(await database.Tenants.AnyAsync(Token)).IsFalse();
        await Assert.That(await database.PlatformUserRoles.AnyAsync(Token)).IsFalse();
        return await TestDatabaseReset.CreateAsync(Container.GetConnectionString());
    }

    internal static async ValueTask DisposeDatabaseAsync()
    {
        await Container.DisposeAsync();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(MigrationAuthorityDirectory))
            Directory.Delete(MigrationAuthorityDirectory, recursive: true);
    }

    internal async Task AssertReadyAsync()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var credentials = scope.ServiceProvider.GetRequiredService<ILocalCredentialAdministration>();
        var page = await credentials.ListAsync(new LocalIdentityListRequest(1, 10), Token);
        await Assert.That(page.TotalCount).IsEqualTo(6);
        foreach (var identity in page.Items)
        {
            await Assert.That(identity.HasExactBinding).IsTrue();
            await Assert.That(identity.CredentialState).IsEqualTo(LocalCredentialState.Ready);
            var operation = await credentials.ReadOperationAsync(identity.CurrentOperationId!.Value, Token);
            await Assert.That(operation!.Receipt.Stage).IsEqualTo(LocalCredentialOperationStage.Replaced);
        }
        await using var database = CreateDatabase();
        await Assert.That(await database.PlatformUserRoles.CountAsync(Token)).IsEqualTo(1);
        await Assert.That(await database.EventRoleAssignments.CountAsync(Token)).IsEqualTo(3);
        await Assert.That(await database.TenantUserRoleGrants.CountAsync(Token)).IsEqualTo(5);
    }

    internal async Task ChangePasswordProfileAndRevokeGrantAsync()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
        var user = (await manager.FindByIdAsync(AgentBrowserPersonaCatalog.Attendee.SubjectId.ToString("D")))!;
        await Assert.That((await manager.ChangePasswordAsync(user, _finalPassword, _replacementPassword)).Succeeded).IsTrue();
        user.FirstName = "Changed by operator";
        await Assert.That((await manager.UpdateAsync(user)).Succeeded).IsTrue();
        await using var database = CreateDatabase();
        var grant = await database.TenantUserRoleGrants.SingleAsync(row => row.RoleId == (int)RoleEnum.TenantAdmin, Token);
        grant.RevokedAt = DateTime.UtcNow;
        await database.SaveChangesAsync(Token);
    }

    internal async Task AssertChangesPreservedAsync()
    {
        await using var database = CreateDatabase();
        await Assert.That((await database.TenantUserRoleGrants.SingleAsync(row => row.RoleId == (int)RoleEnum.TenantAdmin, Token)).RevokedAt).IsNotNull();
        await using var scope = _factory!.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
        var user = (await manager.FindByIdAsync(AgentBrowserPersonaCatalog.Attendee.SubjectId.ToString("D")))!;
        await Assert.That(user.FirstName).IsEqualTo("Changed by operator");
        await Assert.That(await manager.CheckPasswordAsync(user, _replacementPassword)).IsTrue();
        await Assert.That(await manager.CheckPasswordAsync(user, _finalPassword)).IsFalse();
        using var response = await _client!.PostAsJsonAsync("/api/auth/local/login",
            new LocalAuthRequestDto(user.UserName!, _replacementPassword), Token);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"agent_browser_replay_login_http_{(int)response.StatusCode}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        await Assert.That(body.RootElement.GetProperty("success").GetBoolean()).IsTrue();
    }

    internal void RemoveInitializationSecrets()
    {
        _secrets.Values.Remove(SecretDefinitionRegistry.Keys.Authentication.AgentBrowserPersonaPassword);
        _secrets.Values.Remove(SecretDefinitionRegistry.Keys.Authentication.LocalBootstrapPassword);
    }

    internal void BreakSecret(string fault)
    {
        string key = fault switch
        {
            "persona" => SecretDefinitionRegistry.Keys.Authentication.AgentBrowserPersonaPassword,
            "bootstrap" => SecretDefinitionRegistry.Keys.Authentication.LocalBootstrapPassword,
            _ => SecretDefinitionRegistry.Keys.Authentication.LocalJwtKey
        };
        if (fault == "malformed-signing") _secrets.Values[key] = Guid.CreateVersion7().ToString("D");
        else if (fault == "mismatched-signing") _secrets.Values[key] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        else _secrets.Values.Remove(key);
    }

    internal async Task InsertForeignUserAsync()
    {
        await using var database = CreateDatabase();
        database.Users.Add(new User { Id = Guid.CreateVersion7(), Pii = new UserPii { Email = "foreign@example.test", FirstName = "Foreign", LastName = "User" }, CreatedAt = DateTime.UtcNow });
        await database.SaveChangesAsync(Token);
    }

    internal async Task<NpgsqlConnection> AcquireProvisioningLockAsync()
    {
        var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(Container.GetConnectionString())
        {
            Pooling = false // Disposal releases the session lock now, not on a later pooled checkout.
        }.ConnectionString);
        await connection.OpenAsync(Token);
        await using var command = new NpgsqlCommand("SELECT pg_advisory_lock(@key)", connection);
        command.Parameters.AddWithValue("key", AgentBrowserPersonaStartup.AdvisoryLockKey);
        await command.ExecuteScalarAsync(Token);
        return connection;
    }

    internal async Task<string> DatabasePreservationFingerprintAsync()
    {
        await using var connection = new NpgsqlConnection(Container.GetConnectionString());
        await connection.OpenAsync(Token);
        var fingerprints = new List<string>();
        using var identifiers = new NpgsqlCommandBuilder();
        foreach (string table in new[] { "__EFMigrationsHistory", "__EFDataProtectionMigrationsHistory",
            "roles", "role_permissions", "permissions", "authentication_providers",
            "module_definitions", "ui_theme_presets" })
        {
            string identifier = identifiers.QuoteIdentifier(table);
            await using var command = new NpgsqlCommand($"SELECT md5(string_agg(payload, '|' ORDER BY payload)) FROM (SELECT to_jsonb(t)::text AS payload FROM islamu_event.{identifier} t) s", connection);
            fingerprints.Add((string)(await command.ExecuteScalarAsync(Token))!);
        }
        return string.Join(":", fingerprints);
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null) await _factory.DisposeAsync();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_erasureDirectory))
            Directory.Delete(_erasureDirectory, recursive: true);
    }

    private static string NewPassword() => $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}";

    private sealed class NativeFactory(string connection, Dictionary<string, string?> settings, TestSecrets secrets, BoundaryFault fault,
        bool enableReset, Action<IServiceCollection>? configure) : CustomWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Authentication:Provider", "local");
            builder.UseSetting("SecretProvider:Provider", "Environment");
            builder.UseSetting("Authentication:Local:JwtKey", settings["Authentication:Local:JwtKey"]);
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                // Host in Testing without automatic provisioning; invoke the same startup adapter explicitly
                // under its Development admission contract, and exercise its issued credentials over real HTTP.
                var host = new Dictionary<string, string?>(settings) { ["AGENT_BROWSER_SEED_ENABLED"] = "false", ["ISLAMU_ASPIRE_MODE"] = "" };
                configuration.AddInMemoryCollection(host);
            });
            builder.ConfigureTestServices(services =>
            {
                services.RemoveExploreDbContextRegistrations();
                services.AddDbContextFactory<ExploreDbContext>((provider, options) => options.UseNpgsql(
                        connection, npgsql => npgsql.EnableRetryOnFailure())
                    .UseSnakeCaseNamingConvention().AddInterceptors(fault).AddInterceptors(provider.GetServices<IInterceptor>()));
                services.AddScoped(provider =>
                {
                    var database = provider.GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContext();
                    database.TenantContext = provider.GetRequiredService<ITenantContext>();
                    database.CurrentUserService = provider.GetRequiredService<ICurrentUserService>();
                    return database;
                });
                services.RemoveAll<ISecretResolver>();
                services.AddSingleton<ISecretResolver>(secrets);
                if (enableReset)
                {
                    var admission = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
                    services.AddSingleton(provider => new AgentBrowserResetCoordinator(provider, admission,
                        new HostingEnvironment { EnvironmentName = "Development" },
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentBrowserResetCoordinator>.Instance));
                    services.AddSingleton<IAgentBrowserWorkAdmission>(provider => provider.GetRequiredService<AgentBrowserResetCoordinator>());
                    services.AddHostedService(provider => provider.GetRequiredService<AgentBrowserResetCoordinator>());
                    services.Configure<Microsoft.AspNetCore.OutputCaching.OutputCacheOptions>(options =>
                        options.AddBasePolicy(policy => policy.Tag("agent-database")));
                }
                configure?.Invoke(services);
            });
        }
    }

    internal sealed class InjectedBoundaryFailure : Exception;

    private sealed class BoundaryFault : DbTransactionInterceptor
    {
        internal string? Boundary { get; set; }
        internal bool Observed { get; private set; }

        public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (Boundary is null || Observed) return;
            await using var database = new ExploreDbContext(new DbContextOptionsBuilder<ExploreDbContext>()
                .UseNpgsql(Container.GetConnectionString()).UseSnakeCaseNamingConvention().Options);
            database.EnableTenantFilterBypass(TenantFilterBypassReasons.DatabaseSeeding);
            var operation = await database.Set<LocalIdentityCredentialOperation>().AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == AgentBrowserPersonaCatalog.Attendee.OperationId, cancellationToken);
            bool user = await database.Users.AnyAsync(row => row.Id == AgentBrowserPersonaCatalog.Attendee.SubjectId, cancellationToken);
            bool reached = Boundary switch
            {
                "marker" => await database.InstanceBootstrapStates.AnyAsync(cancellationToken)
                    && !await database.Tenants.AnyAsync(cancellationToken),
                "receipt" => operation?.Stage == LocalCredentialOperationStage.ProvisioningPending && !user,
                "graph" => operation?.Stage == LocalCredentialOperationStage.ProvisioningPending && user,
                "activation" => operation?.Stage == LocalCredentialOperationStage.ChangeRequired,
                "replacement" => operation?.Stage == LocalCredentialOperationStage.Replaced,
                _ => false
            };
            if (!reached) return;
            Observed = true;
            throw new InjectedBoundaryFailure();
        }
    }

    private sealed class TestSecrets : ISecretResolver
    {
        internal Dictionary<string, string> Values { get; } = new()
        {
            [SecretDefinitionRegistry.Keys.Authentication.LocalJwtKey] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
            [SecretDefinitionRegistry.Keys.Authentication.LocalBootstrapPassword] = NewPassword(),
            [SecretDefinitionRegistry.Keys.Authentication.AgentBrowserPersonaPassword] = NewPassword()
        };
        public Task<SecretResolutionResult> ResolveAsync(string settingKey, Guid? tenantId, CancellationToken cancellationToken = default) => Task.FromResult(
            Values.TryGetValue(settingKey, out string? value)
                ? SecretResolutionResult.Resolved(new ResolvedSecret(settingKey, value, SecretSourceType.EnvironmentVariable, SecretScope.Instance, null, DateTimeOffset.UtcNow))
                : SecretResolutionResult.Unconfigured);
        public Task<SecretResolutionResult> ResolveQualifiedAsync(string settingKey, SecretScope scope, Guid? scopeId, string qualifier, CancellationToken cancellationToken = default) => ResolveAsync(settingKey, scopeId, cancellationToken);
        public Task<SecretResolutionResult> ResolveTenantBindingAsync(Guid tenantId, Guid bindingId, CancellationToken cancellationToken = default) => Task.FromResult(SecretResolutionResult.Unconfigured);
        public Task InvalidateAsync(string settingKey, SecretScope scope, Guid? scopeId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

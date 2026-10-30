using System.Security.Cryptography;
using Explore.API.Authentication;
using Explore.API.Configuration;
using Explore.API.Middleware;
using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Services;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure;
using Explore.Infrastructure.Identity;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Explore.Persistence.Seed;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace Event.Api.IntegrationTests.Features;

[ClassDataSource<PreTenantAuthenticationDatabase>(Shared = SharedType.PerClass)]
[NotInParallel("PersistenceDb")]
public sealed class PreTenantAuthenticationLeaseTests(PreTenantAuthenticationDatabase database)
{
    [Test]
    [Arguments(false, false, false)]
    [Arguments(true, false, false)]
    [Arguments(false, true, false)]
    [Arguments(true, true, false)]
    [Arguments(false, true, true)]
    [Arguments(true, true, true)]
    public async Task NativeAuthenticationBindsTenantBeforeAcquiringItsOrdinaryRequestLease(
        bool enrichClaims, bool initialRouting, bool resolveRouting)
    {
        Guid tenantId = Guid.CreateVersion7();
        string role = $"pretenant_runtime_{Guid.CreateVersion7():N}";
        string globalRole = $"pretenant_global_{Guid.CreateVersion7():N}";
        string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        string globalPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        string credential = ApiKeyHashing.CreateSecret();
        string settingKey = $"pretenant.{tenantId:N}";
        string schema;
        string table;
        string resolverTable;
        await using (var seed = database.Context())
        {
            seed.Tenants.Add(new Tenant
            {
                Id = tenantId,
                FullName = settingKey,
                Slug = $"pretenant-{tenantId:N}",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!
            });
            seed.TenantSettingOverrides.Add(new TenantSetting
            {
                TenantId = tenantId,
                Tenant = null!,
                SettingKey = settingKey,
                Value = "{}"
            });
            await seed.SaveChangesAsync();
            var entity = seed.Model.FindEntityType(typeof(TenantSetting))!;
            schema = entity.GetSchema()!;
            table = $"\"{schema}\".\"{entity.GetTableName()}\"";
            var resolverEntity = seed.Model.FindEntityType(typeof(SystemSetting))!;
            resolverTable = $"\"{resolverEntity.GetSchema()}\".\"{resolverEntity.GetTableName()}\"";
        }
        await using var administration = new NpgsqlConnection(database.ConnectionString);
        await administration.OpenAsync();
        await using var command = administration.CreateCommand();
        command.CommandText = $"""
            CREATE ROLE "{role}" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD '{password}';
            CREATE ROLE "{globalRole}" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD '{globalPassword}';
            GRANT USAGE ON SCHEMA "{schema}" TO "{role}";
            GRANT SELECT ON {table} TO "{role}";
            {(resolveRouting ? $"GRANT SELECT ON {resolverTable} TO \"{role}\";" : "")}
            """;
        await command.ExecuteNonQueryAsync();
        var connection = new NpgsqlConnectionStringBuilder(database.ConnectionString)
        {
            Username = role,
            Password = password
        };
        try
        {
            var runtimeOptions = new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Runtime,
                Provider = PrimaryDatabaseProvider.PostgreSql,
                Host = connection.Host,
                Port = connection.Port,
                Database = connection.Database,
                Username = role,
                Password = password,
                Schema = schema,
                TlsMode = PrimaryDatabaseTlsMode.Disabled
            };
            await using (var control = await new TenantExploreDbContextFactory(runtimeOptions)
                .CreateDbContextAsync(tenantId))
            {
                await control.Database.OpenConnectionAsync();
                await using var authority = control.Database.GetDbConnection().CreateCommand();
                authority.CommandText = "SELECT current_user, rolsuper, rolbypassrls FROM pg_catalog.pg_roles WHERE rolname = current_user";
                await using (var reader = await authority.ExecuteReaderAsync())
                {
                    await Assert.That(await reader.ReadAsync()).IsTrue();
                    await Assert.That(reader.GetString(0)).IsEqualTo(role);
                    await Assert.That(reader.GetBoolean(1)).IsFalse();
                    await Assert.That(reader.GetBoolean(2)).IsFalse();
                }
                await Assert.That(await control.TenantSettingOverrides
                    .AnyAsync(row => row.TenantId == tenantId && row.SettingKey == settingKey)).IsTrue();
            }
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:Provider"] = "PostgreSql",
                    ["Database:Host"] = connection.Host,
                    ["Database:Port"] = connection.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["Database:Database"] = connection.Database,
                    ["Database:Schema"] = schema,
                    ["Database:Runtime:Username"] = role,
                    ["Database:Runtime:Password"] = password,
                    ["Database:Global:Username"] = globalRole,
                    ["Database:Global:Password"] = globalPassword,
                    ["Database:TlsMode"] = "Disabled",
                    ["PrivacyErasure:Authority:Topology"] = "CoLocated"
                }).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddMetrics();
            services.AddSingleton<BusinessMetrics>();
            services.AddHttpContextAccessor();
            services.AddProblemDetails();
            services.AddSingleton(configuration);
            services.ConfigurePersistenceServices(configuration, skipLookupCacheInitializer: true, environmentName: "Testing");
            services.Configure<DeploymentSettings>(options => options.Mode = DeploymentMode.MultiTenant);
            services.AddScoped<ITenantContextAccessor, TenantContextAccessor>();
            services.AddScoped<ITenantResolverService, TenantResolverService>();
            services.AddScoped<ITenantContext, TenantContext>();
            services.AddScoped<IAdminContext, AdminContext>();
            if (initialRouting)
            {
                services.AddMemoryCache();
                services.AddDistributedMemoryCache();
                services.AddScoped<IResolverConfigService, ResolverConfigService>();
                services.AddScoped<ITenantSlugCache, TenantSlugCache>();
                services.AddSingleton<IDeploymentModeProvider, DeploymentModeProvider>();
                services.AddScoped<ITenantLifecycleAccessService, TenantLifecycleAccessService>();
                if (resolveRouting)
                {
                    var mode = Substitute.For<IDeploymentModeProvider>();
                    mode.IsSingleTenantAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(false));
                    services.AddSingleton(mode);
                }
            }
            if (enrichClaims)
                services.AddTransient<IClaimsTransformation, AdminClaimsTransformation>();
            services.AddAuthentication(ApiAuthenticationSchemeNames.ApiKey)
                .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
                    ApiAuthenticationSchemeNames.ApiKey,
                    options => options.Clients.Add(new ApiKeyClientDescriptor
                    {
                        KeyId = "configured-native-control",
                        TenantId = tenantId,
                        OwnerType = nameof(ExternalApiKeyOwnerType.Tenant),
                        OwnerId = tenantId.ToString("D"),
                        Scopes = ["events:read"],
                        SecretHash = ApiKeyHashing.ComputeHash(credential)
                    }));
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            http.Request.Path = initialRouting && !resolveRouting
                ? "/api/System/auth-context-probe" : "/api/auth-context-probe";
            http.Request.Headers[ApiAuthenticationHeaderNames.ApiKey] = credential;
            scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = http;
            var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
            await Assert.That(tenant.IsResolved).IsFalse();
            bool reachedOrdinaryRead = false;
            var postAuthentication = new ApiTenantPostAuthenticationMiddleware(async context =>
            {
                var requestDatabase = context.RequestServices.GetRequiredService<ExploreDbContext>();
                Guid[] visible = await requestDatabase.TenantSettingOverrides
                    .Where(row => row.SettingKey == settingKey)
                    .Select(row => row.TenantId).ToArrayAsync();
                await Assert.That(visible).IsEquivalentTo([tenantId]);
                reachedOrdinaryRead = true;
            }, NullLogger<ApiTenantPostAuthenticationMiddleware>.Instance);
            var authentication = new AuthenticationMiddleware(
                context => postAuthentication.InvokeAsync(
                    context,
                    tenant,
                    scope.ServiceProvider.GetRequiredService<IProblemDetailsService>(),
                    scope.ServiceProvider.GetRequiredService<BusinessMetrics>(),
                    Options.Create(new McpAdapterSettings())),
                scope.ServiceProvider.GetRequiredService<IAuthenticationSchemeProvider>());

            if (initialRouting)
            {
                await new ApiTenantResolutionMiddleware(authentication.Invoke,
                    Options.Create(new DeploymentSettings { Mode = DeploymentMode.MultiTenant }))
                    .InvokeAsync(http,
                        tenant,
                        scope.ServiceProvider.GetRequiredService<IProblemDetailsService>(),
                        scope.ServiceProvider.GetRequiredService<IDeploymentModeProvider>(),
                        Options.Create(new McpAdapterSettings()),
                        scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>());
            }
            else
            {
                await authentication.Invoke(http);
            }

            await Assert.That(http.User.Identity!.IsAuthenticated).IsTrue();
            await Assert.That(tenant.TenantId).IsEqualTo(tenantId);
            await Assert.That(reachedOrdinaryRead).IsTrue();
        }
        finally
        {
            using var pool = new NpgsqlConnection(connection.ConnectionString);
            NpgsqlConnection.ClearPool(pool);
            command.CommandText = $"""
                REVOKE SELECT ON {table} FROM "{role}";
                {(resolveRouting ? $"REVOKE SELECT ON {resolverTable} FROM \"{role}\";" : "")}
                REVOKE USAGE ON SCHEMA "{schema}" FROM "{role}";
                DROP ROLE "{role}";
                DROP ROLE "{globalRole}";
                """;
            await command.ExecuteNonQueryAsync();
        }
    }
}

public sealed class PreTenantAuthenticationDatabase : TUnit.Core.Interfaces.IAsyncInitializer, IAsyncDisposable
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine")
        .WithPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(32))).Build();

    public string ConnectionString => _container.GetConnectionString();

    public ExploreDbContext Context() => new(new DbContextOptionsBuilder<ExploreDbContext>()
        .UseNpgsql(ConnectionString).UseSnakeCaseNamingConvention().Options);

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var context = Context();
        await ExploreDatabaseMigrator.MigrateAsync(context, new ConfigurationBuilder().Build());
        await PostgresModelConstraintApplier.ApplyAsync(context);
        await PostgresTenantRowLevelSecurityModel.ApplyAsync(context);
        await LookupTableSeeder.SeedAsync(context);
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}

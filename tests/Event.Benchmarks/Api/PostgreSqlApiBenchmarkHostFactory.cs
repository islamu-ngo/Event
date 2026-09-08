using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Domain.Constants;
using Explore.Persistence;
using Explore.Persistence.Schema;
using Explore.Persistence.Seed;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Event.Benchmarks.Api;

internal sealed class PostgreSqlApiBenchmarkHostFactory(string connectionString) : WebApplicationFactory<global::Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            var benchmarkConfig = new Dictionary<string, string?>
            {
                ["Keycloak:Authority"] = "https://auth.benchmark.invalid",
                ["Keycloak:Realm"] = "ISLAMU",
                ["Keycloak:Audience"] = "islamu-event-api",
                ["Keycloak:RequireHttpsMetadata"] = "false",
                ["Keycloak:MetadataAddress"] = "https://auth.benchmark.invalid/.well-known/openid-configuration",
                ["Testing:UseRealDatabase"] = "true",
                ["Testing:ApplyMigrations"] = "false",
                ["Testing:SkipJwtAuthorityWarmup"] = "true",
                ["SecretRefresh:Enabled"] = "false",
                ["PdsSync:Enabled"] = "false",
                ["OutboxProcessor:Enabled"] = "false",
                ["EmailDispatchProcessor:Enabled"] = "false",
                ["S3Settings:Region"] = "us-east-1",
                ["S3Settings:BucketName"] = "benchmark-bucket",
                ["S3Settings:AccessKeyId"] = "benchmark-key",
                ["S3Settings:SecretAccessKey"] = "benchmark-secret",
                ["S3Settings:Endpoint"] = "https://s3.benchmark.invalid",
                ["Deployment:Mode"] = "SingleTenant",
                ["Deployment:DefaultTenantId"] = PlatformDefaults.DefaultTenantId.ToString(),
                ["PublicBaseUrl"] = "https://benchmark.event.local"
            };

            BenchmarkDatabaseConfiguration.AddPostgreSql(benchmarkConfig, connectionString);

            config.AddInMemoryCollection(benchmarkConfig);
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveExploreDbContextRegistrations();

            services.AddDbContext<ExploreDbContext>(options =>
            {
                options.UseNpgsql(connectionString);
                options.UseSnakeCaseNamingConvention();
                options.ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
            });

            services.RemoveAll<IDistributedCache>();
            services.AddDistributedMemoryCache();
            services.RemoveAll<IOutputCacheStore>();
            services.AddSingleton<IOutputCacheStore, BenchmarkNoOpOutputCacheStore>();
            services.AddHostedService<PostgreSqlBenchmarkSeedingHostedService>();
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveProductHostedServices();

            services.AddAuthentication(options =>
            {
                options.DefaultScheme = ApiBenchmarkAuthHandler.SchemeName;
                options.DefaultAuthenticateScheme = ApiBenchmarkAuthHandler.SchemeName;
                options.DefaultChallengeScheme = ApiBenchmarkAuthHandler.SchemeName;
                options.DefaultForbidScheme = ApiBenchmarkAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, ApiBenchmarkAuthHandler>(
                ApiBenchmarkAuthHandler.SchemeName,
                _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultScheme = ApiBenchmarkAuthHandler.SchemeName;
                options.DefaultAuthenticateScheme = ApiBenchmarkAuthHandler.SchemeName;
                options.DefaultChallengeScheme = ApiBenchmarkAuthHandler.SchemeName;
                options.DefaultForbidScheme = ApiBenchmarkAuthHandler.SchemeName;
            });

            services.RemoveAll<IAuthorizationProvider>();
            services.AddSingleton<IAuthorizationProvider>(ApiBenchmarkAuthorizationProvider.AllowAll);
            services.RemoveAll<ISetupSecretProvider>();
            services.AddSingleton<ISetupSecretProvider, BenchmarkSetupSecretProvider>();
        });
    }

    private sealed class PostgreSqlBenchmarkSeedingHostedService(
        IServiceProvider serviceProvider,
        IHostEnvironment environment) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            using var scope = serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();

            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
            await PostgresModelConstraintApplier.ApplyAsync(dbContext, cancellationToken);
            await DatabaseSeeder.SeedAsync(dbContext, environment, cancellationToken: cancellationToken);
            await BenchmarkApiSeedData.SeedAsync(dbContext, cancellationToken);

            var lookupCache = scope.ServiceProvider.GetService<ILookupDataCache>();
            if (lookupCache is not null)
            {
                await lookupCache.RefreshAsync(cancellationToken);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

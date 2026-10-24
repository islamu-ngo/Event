using Event.Testing.DataProtection;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Domain.Constants;
using Explore.Persistence;
using Explore.Persistence.Seed;
using Explore.ServiceDefaults.DataProtection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Event.Benchmarks.Api;

internal sealed class ApiBenchmarkHostFactory : WebApplicationFactory<global::Program>
{
    private readonly string _databaseName = $"BenchmarkApiHost_{Guid.NewGuid():N}";
    private readonly ExternalDataProtectionTestAuthority _dataProtection = new("api");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSolutionRelativeContentRoot("src/Explore.API", "Explore.slnx");
        builder.UseSetting("SecretProvider:Provider", "Environment");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SecretProvider:Provider"] = "Environment",
            ["Authentication:Provider"] = "keycloak",
            ["Keycloak:Authority"] = "https://auth.benchmark.invalid",
            ["Keycloak:Realm"] = "benchmark",
            ["Keycloak:Audience"] = "islamu-event-api",
            ["Keycloak:RequireHttpsMetadata"] = "false",
            ["Keycloak:MetadataAddress"] = "https://auth.benchmark.invalid/.well-known/openid-configuration",
            ["Testing:SkipJwtAuthorityWarmup"] = "true",
            ["SecretRefresh:Enabled"] = "false",
            ["PdsSync:Enabled"] = "false",
            ["OutboxProcessor:Enabled"] = "false",
            ["EmailDispatchProcessor:Enabled"] = "false",
            ["Deployment:Mode"] = "SingleTenant",
            ["Deployment:DefaultTenantId"] = PlatformDefaults.DefaultTenantId.ToString(),
            ["PublicBaseUrl"] = "https://benchmark.event.local"
        }));

        builder.ConfigureServices(services =>
        {
            services.RemoveExploreDbContextRegistrations();
            services.AddDbContextFactory<ExploreDbContext>(options =>
            {
                options.UseInMemoryDatabase(_databaseName);
                options.ConfigureWarnings(warnings => warnings.Ignore(
                    Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning));
            });
            services.AddTestContextOwners();
            services.RemoveAll<IDistributedCache>();
            services.AddDistributedMemoryCache();
            services.AddHostedService<BenchmarkSeedingHostedService>();
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveProductHostedServices();
            services.RemoveAll<IExternalDataProtectionSource>();
            services.AddSingleton<IExternalDataProtectionSource>(_dataProtection);
            services.AddAuthentication(options =>
            {
                options.DefaultScheme = ApiBenchmarkAuthHandler.SchemeName;
                options.DefaultAuthenticateScheme = ApiBenchmarkAuthHandler.SchemeName;
                options.DefaultChallengeScheme = ApiBenchmarkAuthHandler.SchemeName;
                options.DefaultForbidScheme = ApiBenchmarkAuthHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, ApiBenchmarkAuthHandler>(ApiBenchmarkAuthHandler.SchemeName, _ => { });
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

    private sealed class BenchmarkSeedingHostedService(IServiceProvider serviceProvider, IHostEnvironment environment) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            using var scope = serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            if (!dbContext.Database.IsInMemory())
                throw new InvalidOperationException("benchmark_inmemory_request_provider_required");
            await DatabaseSeeder.SeedAsync(dbContext, environment, NullLogger.Instance, cancellationToken: cancellationToken);
            await BenchmarkApiSeedData.SeedAsync(dbContext, cancellationToken);
            if (await dbContext.Events.CountAsync(evt => evt.Slug != null && evt.Slug.StartsWith("benchmark-api-event-"), cancellationToken) != 24)
                throw new InvalidOperationException("benchmark_inmemory_dataset_invalid");
            var lookupCache = scope.ServiceProvider.GetService<ILookupDataCache>();
            if (lookupCache is not null)
                await lookupCache.RefreshAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

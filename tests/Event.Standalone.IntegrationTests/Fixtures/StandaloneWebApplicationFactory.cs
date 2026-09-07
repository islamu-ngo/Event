using System.Collections.Concurrent;
using System.Net;
using Event.Web.BffHosting.Abstractions;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Infrastructure;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Services;
using Explore.Domain.Constants;
using Explore.Persistence;
using Explore.Infrastructure.ConfigurationManifest;
using Event.Standalone.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Standalone.IntegrationTests.Fixtures;

public sealed class StandaloneWebApplicationFactory : WebApplicationFactory<StandaloneHostMarker>
{
    private readonly string _runtimePassword =
        Guid.CreateVersion7().ToString("N");
    private readonly IReadOnlyDictionary<string, string?>? _configurationOverrides;
    private readonly IConfigurationManifestStartupRunner? _startupRunner;

    public StandaloneWebApplicationFactory(
        IReadOnlyDictionary<string, string?>? configurationOverrides = null,
        IConfigurationManifestStartupRunner? startupRunner = null)
    {
        _configurationOverrides = configurationOverrides;
        _startupRunner = startupRunner;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseStaticWebAssets();
        builder.UseSetting("HttpsRedirection:Enabled", "false");
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "PostgreSql",
                ["Database:Host"] = "postgres.example.test",
                ["Database:Database"] = "event_test",
                ["Database:Runtime:Database"] = "event_test",
                ["Database:Migrator:Database"] = "event_test",
                ["Database:Runtime:Username"] = "event_test",
                ["Database:Runtime:Password"] = _runtimePassword,
                ["ExploreApi:BaseUrl"] = "http://127.0.0.1:7039/",
                ["Keycloak:Authority"] = "https://auth.example.com",
                ["Keycloak:Realm"] = "explore",
                ["Keycloak:Audience"] = "islamu-event-api",
                ["Keycloak:RequireHttpsMetadata"] = "false",
                ["Deployment:Mode"] = "SingleTenant",
                ["Deployment:DefaultTenantId"] = PlatformDefaults.DefaultTenantId.ToString(),
                ["Mcp:Enabled"] = "false",
                ["HttpsRedirection:Enabled"] = "false",
                ["Testing:SkipJwtAuthorityWarmup"] = "true",
                ["ForwardedHeadersTrust:TrustLoopbackProxy"] = "true",
                ["Bff:AdminHosts:0"] = "admin.proxy.test",
                ["Bff:AdminHostAllowedIpRanges:0"] = "203.0.113.0/24",
                ["Instance:OperatorIdentity:OperatorId"] =
                    "0198e2a4-5340-7f89-8abc-b8bdf43e0ea8",
                ["Instance:OperatorIdentity:PublicName"] =
                    "Standalone Test Operator",
                ["Instance:OperatorIdentity:LegalName"] =
                    "Standalone Test Operator ASBL",
                ["Instance:OperatorIdentity:IsOfficialInstance"] = "false",
                ["Instance:OperatorIdentity:OfficialOrigin"] =
                    "https://standalone.example.test",
                ["Instance:OperatorIdentity:OperatorKindCode"] =
                    "registered_organization",
                ["Instance:OperatorIdentity:JurisdictionCountryCode"] = "BE",
                ["Instance:OperatorIdentity:RegistrationIdentifier"] =
                    "BE 0123.456.789",
                ["Instance:OperatorIdentity:PublicContactEmail"] =
                    "contact@standalone.example.test",
                ["Instance:OperatorIdentity:WebsiteUrl"] =
                    "https://standalone.example.test",
                ["Instance:OperatorIdentity:LegalNoticeUrl"] =
                    "https://standalone.example.test/legal",
                ["Instance:OperatorIdentity:TermsUrl"] =
                    "https://standalone.example.test/terms",
                ["Instance:OperatorIdentity:PrivacyUrl"] =
                    "https://standalone.example.test/privacy"
            }));
        if (_configurationOverrides is not null)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(_configurationOverrides));
        }

        builder.ConfigureServices(services =>
        {
            AddInMemoryExploreDbContext(services);
            services.RemoveAll<IDistributedCache>();
            services.AddDistributedMemoryCache();
            services.RemoveAll<ISetupSecretProvider>();
            services.AddSingleton<ISetupSecretProvider, TestSetupSecretProvider>();
            if (_startupRunner is not null)
            {
                services.RemoveAll<IConfigurationManifestStartupRunner>();
                services.AddSingleton(_startupRunner);
            }
            services.RemoveAll<IDynamicAuthSchemeManager>();
            services.AddSingleton<DynamicAuthInitializationProbe>();
            services.AddSingleton<IDynamicAuthSchemeManager>(services =>
                services.GetRequiredService<DynamicAuthInitializationProbe>());
            services.RemoveAll<IEventBffTenantHintProvider>();
            services.AddSingleton<ForwardedRequestProbe>();
            services.AddSingleton<IEventBffTenantHintProvider>(serviceProvider =>
                serviceProvider.GetRequiredService<ForwardedRequestProbe>());

            for (var index = services.Count - 1; index >= 0; index--)
            {
                var implementationName = services[index].ImplementationType?.FullName;
                if (implementationName is "OpenFeature.Hosting.HostedFeatureLifecycleService"
                    or "Explore.API.Authentication.JwtAuthorityWarmupHostedService")
                {
                    services.RemoveAt(index);
                }
            }
        });
    }

    private static void AddInMemoryExploreDbContext(IServiceCollection services)
    {
        services.AddDbContextFactory<ExploreDbContext>(options =>
        {
            options.UseInMemoryDatabase("StandaloneGraphTests");
            options.ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning));
        });
        services.AddScoped(serviceProvider =>
        {
            var factory = serviceProvider.GetRequiredService<IDbContextFactory<ExploreDbContext>>();
            var context = factory.CreateDbContext();
            context.ClearTenantFilterBypass();
            context.TenantContext = serviceProvider.GetService<ITenantContext>();
            context.CurrentUserService = serviceProvider.GetService<ICurrentUserService>();
            return context;
        });
    }

    private sealed class TestSetupSecretProvider : ISetupSecretProvider
    {
        public bool IsSetupModeActive => false;
        public bool IsSetupSecretRequired => true;
        public bool IsFromEnvironmentVariable => false;
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public bool ValidateSecret(string? secret) => false;
        public void Lock() { }
    }

}

public sealed class ConfigurationManifestStartupProbe
    : IConfigurationManifestStartupRunner
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

public sealed class DynamicAuthInitializationProbe(IServiceScopeFactory scopeFactory) : IDynamicAuthSchemeManager
{
    private int _initializationCount;

    public int InitializationCount => Volatile.Read(ref _initializationCount);

    public async Task InitializeAsync()
    {
        Interlocked.Increment(ref _initializationCount);
        await Task.Yield();
        await using var scope = scopeFactory.CreateAsyncScope();
        var apiClient = scope.ServiceProvider.GetRequiredService<IEventTypeClient>();
        _ = await apiClient.GetEventTypesAsync(cancellationToken: CancellationToken.None);
    }

    public Task RefreshSchemesAsync(string? setupSecret = null) => Task.CompletedTask;

    public Task<IReadOnlyList<string>> GetRegisteredProviderSchemesAsync() =>
        Task.FromResult<IReadOnlyList<string>>([]);

    public string GetActivePrimaryProvider() => "local";
}

public sealed class ForwardedRequestProbe : IEventBffTenantHintProvider
{
    private readonly ConcurrentQueue<ForwardedRequestObservation> _observations = [];

    public IReadOnlyCollection<ForwardedRequestObservation> Observations => _observations.ToArray();

    public string? ResolveTenantSlug(HttpContext httpContext)
    {
        _observations.Enqueue(new ForwardedRequestObservation(
            httpContext.Request.Scheme,
            httpContext.Request.Host.Value ?? string.Empty,
            httpContext.Connection.RemoteIpAddress));
        return null;
    }
}

public sealed record ForwardedRequestObservation(
    string Scheme,
    string Host,
    IPAddress? RemoteIpAddress);

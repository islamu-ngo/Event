using System.Security.Cryptography;
using System.Data.Common;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Event.Persistence.IntegrationTests.Fixtures;
using Event.Testing.DataProtection;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Contracts.Secrets;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Explore.ServiceDefaults.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Authentication;

/// <summary>
/// Local Identity and its application binding live in the same PostgreSQL database.
/// Credential issuance uses the fixture's migrator authority. Human mutation selection
/// uses the enrolled Runtime login and production scope factory, never that issuer.
/// </summary>
internal sealed class TenantBrandingNativeAuthenticationHost(
    PostgreSqlContainerFixture postgres, Guid tenantId, IInterceptor[] ownerInterceptors,
    TimeProvider? clock = null, Action<IServiceCollection>? configureServices = null)
    : CustomWebApplicationFactory
{
    private readonly SigningSecret secrets = new();
    private readonly Dictionary<string, string?> previousEnvironment = [];
    private readonly string passwordBlocklistPath = Path.Combine(
        Path.GetTempPath(), $"branding-password-policy-{Guid.CreateVersion7():N}.json");
    internal TestPrivacyIdentityFenceKeyProvider PrivacyKeys { get; } = new();
    internal Exception? RequestFailure { get; private set; }
    private bool runtimeRequestContexts;

    internal void UseRuntimeRequestContexts() => runtimeRequestContexts = true;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        byte[] passwordPolicy = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Version = 1,
            Revision = Guid.CreateVersion7().ToString("N"),
            Source = "Runtime-generated branding fixture authority",
            License = "Project-owned test data",
            Passwords = new[] { Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) }
        });
        File.WriteAllBytes(passwordBlocklistPath, passwordPolicy);
        var native = new Dictionary<string, string?>
        {
            ["SecretProvider:Provider"] = "Environment",
            ["PrivacyErasure:Authority:Topology"] = "CoLocated",
            ["IdentityDatabase:Topology"] = "Colocated",
            ["Authentication:Provider"] = "local",
            ["Authorization:Provider"] = "local",
            ["Authentication:Local:PasswordBlocklistPath"] = passwordBlocklistPath,
            ["Authentication:Local:PasswordBlocklistSha256"] = Convert.ToHexString(SHA256.HashData(passwordPolicy))
        };
        TestDatabaseConfiguration.AddPostgreSql(native, postgres.ConnectionString);
        native["Database:Schema"] = postgres.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime).Schema;
        foreach (var role in Enum.GetValues<PrimaryDatabaseRole>())
        {
            var options = postgres.GetNativeQueueOptions(role);
            native[$"Database:{role}:Username"] = options.Username;
            native[$"Database:{role}:Password"] = options.Password;
            native[$"Database:{role}:TlsMode"] = options.TlsMode.ToString();
        }
        foreach (var pair in native)
        {
            builder.UseSetting(pair.Key, pair.Value);
            string name = pair.Key.Replace(":", "__", StringComparison.Ordinal);
            previousEnvironment.TryAdd(name, Environment.GetEnvironmentVariable(name));
            Environment.SetEnvironmentVariable(name, pair.Value);
        }
        base.ConfigureWebHost(builder);
        builder.UseSetting("Authentication:Provider", "local");
        builder.UseSetting("IdentityDatabase:Topology", "colocated");
        builder.UseSetting("SecretProvider:Provider", "Environment");
        builder.UseSetting("Authentication:Local:JwtKey", secrets.Key);
        builder.UseSetting("PrivacyErasure:Authority:Topology", "CoLocated");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["Authentication:Provider"] = "local",
                ["IdentityDatabase:Topology"] = "colocated",
                ["Authorization:Provider"] = "local",
                ["SecretProvider:Provider"] = "Environment",
                ["Authentication:Local:JwtKey"] = secrets.Key,
                ["Deployment:DefaultTenantId"] = tenantId.ToString("D"),
                ["PrivacyErasure:Authority:Topology"] = "CoLocated",
                ["PrivacyErasureAuthorityEmbedded:Path"] = PrivacyKeys.AuthorityPath,
                ["Testing:SkipJwtAuthorityWarmup"] = "true",
                ["RateLimiting:DisableInTesting"] = "true",
                ["OutboxProcessor:Enabled"] = "false",
                ["EmailDispatchProcessor:Enabled"] = "false"
            };
            foreach (var pair in native)
                settings[pair.Key] = pair.Value;
            configuration.AddInMemoryCollection(settings);
        });
        builder.ConfigureTestServices(services =>
        {
            services.Insert(0, ServiceDescriptor.Singleton<IExceptionHandler>(new FailureObserver(this)));
            services.RemoveAll<IExternalDataProtectionSource>();
            services.AddSingleton<IExternalDataProtectionSource>(new ExternalDataProtectionTestAuthority("api", clock));
            if (clock is not null)
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
            }
            services.RemoveExploreDbContextRegistrations();
            services.AddDbContextFactory<ExploreDbContext>(options =>
            {
                PrimaryDatabaseProviderComposition.ConfigureApplication(options,
                    postgres.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
                options.AddInterceptors(new DatabaseFailureObserver(this));
            });
            services.AddScoped(provider =>
            {
                // Capture the production-selected context before constructing any source port.
                // The unselected fixture scope owns real Local credential issuance.
                // It retains its true Migrator purpose; it is not mutation authority.
                if (provider.GetRequiredService<ExploreDbContextExecutionLease>().Context is { } selected)
                    return selected;
                var context = runtimeRequestContexts
                    ? postgres.CreateEnrolledTenantDbContext(provider.GetRequiredService<ITenantContext>(),
                        [PostgresTenantSessionInterceptor.RuntimeInstance, new DatabaseFailureObserver(this), .. ownerInterceptors])
                    : provider.GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContext();
                context.TenantContext = provider.GetRequiredService<ITenantContext>();
                context.CurrentUserService = provider.GetRequiredService<ICurrentUserService>();
                context.ClearTenantFilterBypass();
                context.BindTenantLease();
                return context;
            });
            services.TryAddScoped<ExploreDbContextExecutionLease>();
            services.RemoveAll<IPolicyMutationExecutionScopeFactory>();
            services.AddScoped<IPolicyMutationExecutionScopeFactory, PolicyMutationExecutionScopeFactory>();
            services.RemoveAll<IGlobalExploreDbContextFactory>();
            services.AddSingleton<IGlobalExploreDbContextFactory>(
                new GlobalExploreDbContextFactory(postgres.GetNativeQueueOptions(PrimaryDatabaseRole.Global)));
            services.RemoveAll<ITenantExploreDbContextFactory>();
            services.AddSingleton<ITenantExploreDbContextFactory>(
                new NativeTenantFactory(postgres, ownerInterceptors));
            services.RemoveAll<ISecretResolver>();
            services.AddSingleton<ISecretResolver>(secrets);
            services.RemoveAll<IPrivacyIdentityFenceKeyProvider>();
            services.AddSingleton<IPrivacyIdentityFenceKeyProvider>(PrivacyKeys);
            configureServices?.Invoke(services);
        });
    }

    private sealed class FailureObserver(TenantBrandingNativeAuthenticationHost host) : IExceptionHandler
    {
        public ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken token)
        {
            host.RequestFailure = exception;
            return ValueTask.FromResult(false);
        }
    }

    private sealed class DatabaseFailureObserver(TenantBrandingNativeAuthenticationHost host) : DbCommandInterceptor
    {
        public override void CommandFailed(DbCommand command, CommandErrorEventData eventData) =>
            host.RequestFailure = eventData.Exception;

        public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            host.RequestFailure = eventData.Exception;
            return Task.CompletedTask;
        }
    }

    public override async ValueTask DisposeAsync()
    {
        try
        {
            await base.DisposeAsync();
        }
        finally
        {
            foreach (var pair in previousEnvironment)
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            File.Delete(passwordBlocklistPath);
        }
    }

    internal async Task EnsurePrivacyAuthorityReadyAsync()
    {
        // Fixture-owned migration is separate from every selected application owner.
        var options = new DbContextOptionsBuilder<CoLocatedPrivacyErasureAuthorityDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureCoLocatedPrivacyErasureAuthority(
            options, postgres.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        await using var authority = new CoLocatedPrivacyErasureAuthorityDbContext(options.Options);
        var table = authority.Model.GetEntityTypes().First();
        int existing = await authority.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*)::integer AS \"Value\" FROM information_schema.tables WHERE table_schema = {0} AND table_name = {1}",
            table.GetSchema()!, table.GetTableName()!).SingleAsync();
        if (existing == 0)
            await authority.Database.ExecuteSqlRawAsync(authority.Database.GenerateCreateScript());
    }

    private sealed class NativeTenantFactory(PostgreSqlContainerFixture postgres, IInterceptor[] interceptors)
        : ITenantExploreDbContextFactory
    {
        public Task<ExploreDbContext> CreateDbContextAsync(Guid selectedTenant, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = postgres.CreateEnrolledTenantDbContext(new SelectedTenant(selectedTenant),
                [PostgresTenantSessionInterceptor.RuntimeInstance, .. interceptors]);
            context.BindTenantLease();
            return Task.FromResult(context);
        }

        private sealed record SelectedTenant(Guid TenantId) : ITenantContext;
    }

    // Only the external secret authority is substituted. Token issuance, signature/session
    // validation, credential persistence, source preparation and final admission are real.
    private sealed class SigningSecret : ISecretResolver, IRetainedSecretResolver
    {
        internal string Key { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

        public Task<SecretResolutionResult> ResolveAsync(
            string settingKey, Guid? tenantId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(settingKey == SecretDefinitionRegistry.Keys.Authentication.LocalJwtKey
                ? SecretResolutionResult.Resolved(new ResolvedSecret(settingKey, Key,
                    SecretSourceType.EnvironmentVariable, SecretScope.Instance, null, DateTimeOffset.UtcNow))
                : SecretResolutionResult.Unconfigured);
        }

        public Task<SecretResolutionResult> ResolveQualifiedAsync(string settingKey, SecretScope scope,
            Guid? scopeId, string qualifier, CancellationToken cancellationToken = default) =>
            ResolveAsync(settingKey, scopeId, cancellationToken);

        public Task<SecretResolutionResult> ResolveTenantBindingAsync(
            Guid tenantId, Guid bindingId, CancellationToken cancellationToken = default) =>
            Task.FromResult(SecretResolutionResult.Unconfigured);

        public Task InvalidateAsync(string settingKey, SecretScope scope, Guid? scopeId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        // No external storage credential is provisioned by this Local signing authority.
        public Task<RetainedSecretReference> CaptureAsync(
            string settingKey, Guid tenantId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("storage_secret_unavailable");

        public Task<SecretResolutionResult> ResolveAsync(
            RetainedSecretReference reference, CancellationToken cancellationToken) =>
            Task.FromResult(SecretResolutionResult.Unconfigured);
    }
}

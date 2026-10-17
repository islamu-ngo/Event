using Explore.Persistence.Services;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Event.Testing.DataProtection;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Domain.Settings;
using Explore.Infrastructure;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Persistence.Schema;
using Explore.Persistence.Seed;
using Explore.Secrets.Database;
using Explore.ServiceDefaults.DataProtection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Persistence.IntegrationTests.Fixtures;

/// <summary>
/// Hosts the actual API authentication pipeline against the test's database. A test receives
/// only the context selected by the production policy scope factory, after a real Local login.
/// No principal, credential lifetime, settings proof or human lease is manufactured here.
/// </summary>
internal sealed partial class AuthenticatedSettingsFixture : IAsyncDisposable
{
    private static readonly SemaphoreSlim EnvironmentOwner = new(1, 1);
    private readonly Dictionary<string, string?> previousEnvironment = [];
    private readonly Func<ExploreDbContext> createDatabase;
    private SettingsHost host = null!;
    private HttpClient client = null!;
    private string accessToken = null!;
    private string? ownedSqlitePath;
    private string? ownedPasswordBlocklistPath;
    internal Guid ActorId { get; private set; }

    private AuthenticatedSettingsFixture(Func<ExploreDbContext> createDatabase) =>
        this.createDatabase = createDatabase;

    internal static Task<AuthenticatedSettingsFixture> CreateAsync(
        Func<ExploreDbContext> createDatabase, IInterceptor? interceptor = null) =>
        CreateCoreAsync(createDatabase, interceptor, configurePostgres: null);

    private static async Task<AuthenticatedSettingsFixture> CreateCoreAsync(
        Func<ExploreDbContext> createDatabase, IInterceptor? interceptor,
        Func<ExploreDbContext, Dictionary<string, string?>, Task>? configurePostgres)
    {
        await EnvironmentOwner.WaitAsync(TestContext.Current!.Execution.CancellationToken);
        var fixture = new AuthenticatedSettingsFixture(createDatabase);
        try
        {
            fixture.SetEnvironment("SECRET_PROVIDER", "Environment");
            fixture.SetEnvironment("SecretProvider__Provider", "Environment");
            fixture.SetEnvironment("PrivacyErasure__Authority__Topology", "CoLocated");
            fixture.SetEnvironment("IdentityDatabase__Topology", "Colocated");
            fixture.SetEnvironment("Authentication__Provider", "local");
            fixture.SetEnvironment("Authorization__Provider", "local");
            fixture.SetEnvironment("Authentication__Local__JwtKey", null);
            fixture.SetEnvironment("AUTHENTICATION_LOCAL_JWT_KEY",
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)));
            fixture.SetEnvironment("PRIVACY_ERASURE_IDENTITY_FENCE_KEY_ID", Guid.CreateVersion7().ToString("N"));
            fixture.SetEnvironment("PRIVACY_ERASURE_IDENTITY_FENCE_KEY",
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

            var configuration = new Dictionary<string, string?>
            {
                ["Authentication:Provider"] = "local",
                ["Authorization:Provider"] = "local",
                ["SecretProvider:Provider"] = "Environment",
                ["PrivacyErasure:Authority:Topology"] = "CoLocated",
                ["IdentityDatabase:Topology"] = "Colocated",
                ["Deployment:Mode"] = "SingleTenant",
                ["Deployment:DefaultTenantId"] = PlatformDefaults.DefaultTenantId.ToString(),
                ["PublicBaseUrl"] = "https://settings.example.test",
                ["EffectiveHostAuthority:CanonicalOrigin"] = "https://settings.example.test",
                ["OutboxProcessor:Enabled"] = "false",
                ["EmailDispatchProcessor:Enabled"] = "false",
                ["Testing:SkipJwtAuthorityWarmup"] = "true",
                ["RateLimiting:DisableInTesting"] = "true",
                ["Instance:OperatorIdentity:OperatorId"] = Guid.CreateVersion7().ToString(),
                ["Instance:OperatorIdentity:PublicName"] = "Settings test operator",
                ["Instance:OperatorIdentity:LegalName"] = "Settings test operator",
                ["Instance:OperatorIdentity:IsOfficialInstance"] = "false",
                ["Instance:OperatorIdentity:OfficialOrigin"] = "https://settings.example.test",
                ["Instance:OperatorIdentity:OperatorKindCode"] = "registered_organization",
                ["Instance:OperatorIdentity:JurisdictionCountryCode"] = "BE",
                ["Instance:OperatorIdentity:RegistrationIdentifier"] = "BE 0123.456.789",
                ["Instance:OperatorIdentity:PublicContactEmail"] = "operator@example.test",
                ["Instance:OperatorIdentity:WebsiteUrl"] = "https://settings.example.test",
                ["Instance:OperatorIdentity:LegalNoticeUrl"] = "https://settings.example.test/legal",
                ["Instance:OperatorIdentity:PrivacyUrl"] = "https://settings.example.test/privacy",
                ["Instance:OperatorIdentity:TermsUrl"] = "https://settings.example.test/terms"
            };
            fixture.ownedPasswordBlocklistPath = Path.Combine(
                Path.GetTempPath(), $"settings-password-policy-{Guid.CreateVersion7():N}.json");
            byte[] passwordPolicy = JsonSerializer.SerializeToUtf8Bytes(new
            {
                Version = 1,
                Revision = Guid.CreateVersion7().ToString("N"),
                Source = "Runtime-generated settings fixture authority",
                License = "Project-owned test data",
                Passwords = new[] { Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) }
            });
            await File.WriteAllBytesAsync(fixture.ownedPasswordBlocklistPath, passwordPolicy,
                TestContext.Current!.Execution.CancellationToken);
            configuration["Authentication:Local:PasswordBlocklistPath"] = fixture.ownedPasswordBlocklistPath;
            configuration["Authentication:Local:PasswordBlocklistSha256"] =
                Convert.ToHexString(SHA256.HashData(passwordPolicy));
            fixture.SetEnvironment("Authentication__Local__PasswordBlocklistPath",
                configuration["Authentication:Local:PasswordBlocklistPath"]);
            fixture.SetEnvironment("Authentication__Local__PasswordBlocklistSha256",
                configuration["Authentication:Local:PasswordBlocklistSha256"]);
            await using var seed = createDatabase();
            if (configurePostgres is null)
            {
                string path = new SqliteConnectionStringBuilder(seed.Database.GetConnectionString()).DataSource;
                configuration["Database:Provider"] = "Sqlite";
                configuration["Database:Database"] = path;
                configuration["Database:Runtime:Database"] = path;
            }
            else
            {
                await configurePostgres(seed, configuration);
            }
            foreach (var item in configuration.Where(pair => pair.Key.StartsWith("Database:", StringComparison.Ordinal)))
                fixture.SetEnvironment(item.Key.Replace(":", "__", StringComparison.Ordinal), item.Value);

            Guid bootstrapActor = await InstanceSettingsCommandFixture.SeedAdministratorAsync(seed);
            if (!await seed.Tenants.AnyAsync(row => row.Id == PlatformDefaults.DefaultTenantId))
                seed.Tenants.Add(new Tenant
                {
                    Id = PlatformDefaults.DefaultTenantId,
                    FullName = "Settings authentication",
                    Slug = "settings-authentication",
                    TenantStatusId = (int)TenantStatusEnum.Active,
                    TenantStatus = null!
                });
            if (!await seed.InstanceBootstrapStates.AnyAsync())
            {
                var bootstrap = InstanceBootstrapState.CreateInteractivePending(
                    Guid.CreateVersion7(), DeploymentMode.SingleTenant, DateTime.UtcNow);
                bootstrap.CompleteInteractive(bootstrapActor, DateTime.UtcNow);
                seed.InstanceBootstrapStates.Add(bootstrap);
            }
            if (!await seed.Set<SecretBinding>().AnyAsync(row =>
                    row.SettingKey == SecretDefinitionRegistry.Keys.Authentication.LocalJwtKey))
                seed.Set<SecretBinding>().Add(SecretBinding.CreateEnvironmentVariable(
                    SecretDefinitionRegistry.Keys.Authentication.LocalJwtKey, SecretScope.Instance, null,
                    "AUTHENTICATION_LOCAL_JWT_KEY"));
            await seed.SaveChangesAsync();

            fixture.host = new SettingsHost(configuration, createDatabase, interceptor);
            await using (var scope = fixture.host.Services.CreateAsyncScope())
            {
                await using DbContext authority = configurePostgres is null
                    ? await scope.ServiceProvider.GetRequiredService<IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>>()
                        .CreateDbContextAsync()
                    : new CoLocatedPrivacyErasureAuthorityDbContext(
                        scope.ServiceProvider.GetRequiredService<DbContextOptions<CoLocatedPrivacyErasureAuthorityDbContext>>());
                var table = authority.Model.GetEntityTypes().First();
                bool exists = configurePostgres is null
                    ? await seed.Database.SqlQueryRaw<int>(
                        "SELECT COUNT(*) AS \"Value\" FROM sqlite_master WHERE type = 'table' AND name = {0}",
                        table.GetTableName()!).SingleAsync() != 0
                    : await seed.Database.SqlQueryRaw<int>(
                        "SELECT COUNT(*)::integer AS \"Value\" FROM information_schema.tables WHERE table_schema = {0} AND table_name = {1}",
                        table.GetSchema()!, table.GetTableName()!).SingleAsync() != 0;
                if (!exists)
                    await seed.Database.ExecuteSqlRawAsync(authority.Database.GenerateCreateScript());
            }
            fixture.client = fixture.host.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri(configuration["EffectiveHostAuthority:CanonicalOrigin"]!),
                AllowAutoRedirect = false
            });
            var login = await fixture.CreateLocalAdministratorAsync(bootstrapActor);
            using var response = await fixture.client.PostAsJsonAsync("/api/auth/local/login", login);
            if (response.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException($"Real Local settings login failed: {response.StatusCode}.");
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!body.RootElement.GetProperty("success").GetBoolean())
                throw new InvalidOperationException("Real Local settings login was rejected.");
            fixture.accessToken = body.RootElement.GetProperty("token").GetString()!;
            fixture.host.RuntimeRequests = true;
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    private async Task<LocalAuthRequestDto> CreateLocalAdministratorAsync(Guid initiator)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        if (!services.GetRequiredService<LocalPasswordBlocklistSnapshot>().IsUsable)
            throw new InvalidOperationException("The settings fixture's pinned Local password policy was not admitted.");
        var credentials = services.GetRequiredService<ILocalCredentialAdministration>();
        var manager = services.GetRequiredService<UserManager<LocalIdentityUser>>();
        string email = $"settings-{Guid.CreateVersion7():N}@example.test";
        string password = $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}";
        var created = await credentials.CreatePendingAsync(new LocalCredentialCreateRequest(
            Guid.CreateVersion7(), initiator, email, "Settings", "Administrator"), CancellationToken.None);
        if (created.Outcome != LocalCredentialCreateOutcome.Created)
            throw new InvalidOperationException($"Local settings account creation failed: {created.Outcome}.");
        var receipt = created.Receipt!;
        ActorId = receipt.LocalSubjectId;
        await using (var seed = createDatabase())
        {
            seed.Users.Add(new User
            {
                Id = ActorId,
                Pii = new UserPii { Email = email, FirstName = "Settings", LastName = "Administrator" },
                EmailVerified = true,
                CreatedAt = DateTime.UtcNow
            });
            seed.Actors.Add(new Actor
            {
                Id = receipt.PersonalActorId,
                UserId = ActorId,
                User = null!,
                ActorTypeId = (int)ActorTypeEnum.User,
                ActorType = null!,
                Pii = new ActorPii { DisplayName = "Settings administrator" }
            });
            seed.UserExternalLogins.Add(new UserExternalLogin
            {
                Id = receipt.ExternalLoginId,
                UserId = ActorId,
                User = null!,
                AuthenticationProviderId = (int)AuthenticationProviderKind.Local,
                AuthenticationProvider = null!,
                ProviderKey = ActorId.ToString("D")
            });
            seed.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(),
                UserId = ActorId,
                User = null!,
                RoleId = (int)RoleEnum.Admin,
                Role = null!,
                GrantedAt = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }
        var pending = (await credentials.ReadProvisioningAsync(receipt.OperationId, CancellationToken.None))!;
        if (await credentials.ActivateChangeRequiredAsync(new LocalCredentialActivationRequest(
                receipt.OperationId, pending.OperationConcurrencyStamp), CancellationToken.None)
            != LocalCredentialActivationOutcome.Activated)
            throw new InvalidOperationException("Local settings account activation failed.");
        var identity = (await manager.FindByIdAsync(ActorId.ToString("D")))!;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (await credentials.ReplaceAsync(new LocalCredentialReplacementRequest(
                new LocalCredentialReplacementAuthority(
                    new LocalCredentialReplacementSubject(ActorId, receipt.OperationId, identity.SecurityStamp!),
                    now, now.AddMinutes(5)), password), CancellationToken.None)
            != LocalCredentialReplacementOutcome.Replaced)
            throw new InvalidOperationException("Local settings credential replacement failed.");
        await services.GetRequiredService<ExploreDbContext>().Entry(identity).ReloadAsync();
        identity.EmailConfirmed = true;
        if (!(await manager.UpdateAsync(identity)).Succeeded)
            throw new InvalidOperationException("Local settings email verification failed.");
        await using (var seed = createDatabase())
        {
            (await seed.Users.SingleAsync(row => row.Id == ActorId)).EmailVerified = true;
            await seed.SaveChangesAsync();
        }
        return new LocalAuthRequestDto(email, password);
    }

    internal async Task<T> RunAsync<T>(Guid? tenantId, Func<Session, Task<T>> operation,
        bool mixed = false, bool boundNativeLockWait = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (tenantId is { } tenant)
        {
            await using var seed = createDatabase();
            if (!await seed.TenantUsers.IgnoreQueryFilters().AnyAsync(row => row.TenantId == tenant && row.UserId == ActorId))
            {
                var membership = new TenantUser
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenant,
                    Tenant = null!,
                    UserId = ActorId,
                    User = null!,
                    StatusId = (int)TenantUserStatusEnum.Active,
                    JoinedAt = DateTime.UtcNow
                };
                seed.TenantUsers.Add(membership);
                seed.TenantUserRoleGrants.Add(new TenantUserRoleGrant
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenant,
                    Tenant = null!,
                    TenantUserId = membership.Id,
                    TenantUser = membership,
                    RoleId = (int)RoleEnum.TenantAdmin,
                    Role = null!,
                    RoleScopeId = (int)RoleScopeEnum.Tenant,
                    GrantedAt = DateTime.UtcNow
                });
                await seed.SaveChangesAsync();
            }
        }
        await using var request = host.Services.CreateAsyncScope();
        var accessor = request.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var context = new DefaultHttpContext { RequestServices = request.ServiceProvider };
        accessor.HttpContext = context;
        try
        {
            context.Request.Headers.Authorization = "Bearer " + accessToken;
            var authenticated = await context.AuthenticateAsync();
            if (!authenticated.Succeeded)
                throw new InvalidOperationException(
                    $"The actual API rejected its Local access token ({authenticated.Failure?.GetType().Name ?? "no_principal"}).");
            context.User = authenticated.Principal!;
            await using var selected = await request.ServiceProvider
                .GetRequiredService<IPolicyMutationExecutionScopeFactory>()
                .CreateAsync(mixed ? PolicyMutationScope.MixedSettingsTenant(tenantId!.Value)
                    : tenantId is { } target ? new(target) : PolicyMutationScope.Instance, cancellationToken);
            var session = new Session(selected.Services, ActorId, tenantId);
            if (boundNativeLockWait)
            {
                await session.Context.Database.OpenConnectionAsync();
                await session.Context.Database.ExecuteSqlRawAsync("SET lock_timeout = '100ms'");
            }
            try { return await operation(session); }
            finally
            {
                if (boundNativeLockWait)
                    await session.Context.Database.ExecuteSqlRawAsync("RESET lock_timeout");
            }
        }
        finally { accessor.HttpContext = null; }
    }

    internal sealed class Session(IServiceProvider services, Guid actorId, Guid? tenantId)
    {
        internal IServiceProvider Services => services;
        internal Guid ActorId => actorId;
        internal ExploreDbContext Context => services.GetRequiredService<ExploreDbContext>();
        internal ISettingMutationLock MutationLock => services.GetRequiredService<ISettingMutationLock>();
        internal HumanSettingsDataBoundary Boundary => services.GetRequiredService<HumanSettingsDataBoundary>();
        internal IHumanSettingsMutationPreparation Preparation => services.GetRequiredService<IHumanSettingsMutationPreparation>();

        internal async Task<T> ValueAsync<T>(string key, string input, string storedValue,
            Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
        {
            var proof = tenantId is { } tenant
                ? await Preparation.PrepareAsync(new SetInstanceTenantSettingCommand(tenant, key, input), storedValue, cancellationToken)
                : await Preparation.PrepareAsync(new UpdateSettingCommand
                { Key = key, Value = input, Scope = SettingScope.Instance }, storedValue, cancellationToken);
            return await Preparation.ExecutePreparedAsync(proof, (_, token) => operation(token), cancellationToken);
        }

        internal async Task<T> LockAsync<T>(string key, bool locked, Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            var proof = tenantId is { } tenant
                ? locked
                    ? await Preparation.PrepareAsync(new LockInstanceTenantSettingCommand(tenant, key), cancellationToken)
                    : await Preparation.PrepareAsync(new UnlockInstanceTenantSettingCommand(tenant, key), cancellationToken)
                : locked
                    ? await Preparation.PrepareAsync(new LockSettingCommand { Key = key, Scope = SettingScope.Instance }, cancellationToken)
                    : await Preparation.PrepareAsync(new UnlockSettingCommand { Key = key, Scope = SettingScope.Instance }, cancellationToken);
            return await Preparation.ExecutePreparedAsync(proof, (_, token) => operation(token), cancellationToken);
        }

        internal async Task<T> RemoveAsync<T>(string key, Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            var proof = await Preparation.PrepareAsync(new ResetSettingCommand { Key = key, Scope = SettingScope.Tenant }, cancellationToken);
            return await Preparation.ExecutePreparedAsync(proof, (_, token) => operation(token), cancellationToken);
        }
    }

    private void SetEnvironment(string key, string? value)
    {
        previousEnvironment.TryAdd(key, Environment.GetEnvironmentVariable(key));
        Environment.SetEnvironmentVariable(key, value);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            client?.Dispose();
            if (host is not null) await host.DisposeAsync();
        }
        finally
        {
            foreach (var item in previousEnvironment)
                Environment.SetEnvironmentVariable(item.Key, item.Value);
            EnvironmentOwner.Release();
            if (ownedSqlitePath is { } path)
                DeleteSqlite(path);
            if (ownedPasswordBlocklistPath is { } passwordPolicyPath)
                File.Delete(passwordPolicyPath);
        }
    }

    private static void DeleteSqlite(string path)
    {
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            DefaultTimeout = 30,
            Pooling = true
        }.ToString()))
            SqliteConnection.ClearPool(connection);
        File.Delete(path);
        File.Delete(path + "-wal");
        File.Delete(path + "-shm");
    }

    private sealed class SettingsHost(
        Dictionary<string, string?> configuration, Func<ExploreDbContext> createDatabase,
        IInterceptor? interceptor) : WebApplicationFactory<Program>
    {
        internal bool RuntimeRequests { get; set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Authentication:Provider", "local");
            builder.UseSetting("SecretProvider:Provider", "Environment");
            builder.UseSetting("IdentityDatabase:Topology", "Colocated");
            builder.ConfigureAppConfiguration((_, settings) => settings.AddInMemoryCollection(configuration));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IExternalDataProtectionSource>();
                services.AddSingleton<IExternalDataProtectionSource>(new ExternalDataProtectionTestAuthority("api"));
                services.RemoveAll<IDistributedCache>();
                services.AddDistributedMemoryCache();
                services.AddHumanSettingsMutationPreparation();
                services.AddHumanMixedSettingsSources(IdentityDatabaseTopology.Colocated);
                services.AddScoped<HumanSettingsDataBoundary>();
                if (interceptor is not null)
                {
                    var settings = new ConfigurationBuilder().AddInMemoryCollection(configuration).Build();
                    var databases = new ObservedDatabases(settings, interceptor);
                    services.AddSingleton<IGlobalExploreDbContextFactory>(databases);
                    services.AddSingleton<ITenantExploreDbContextFactory>(databases);
                }
                services.RemoveAll<ExploreDbContext>();
                services.AddScoped(provider =>
                {
                    if (provider.GetRequiredService<ExploreDbContextExecutionLease>().Context is { } selected)
                        return selected;
                    var context = RuntimeRequests
                        && configuration["Database:Provider"] == nameof(PrimaryDatabaseProvider.PostgreSql)
                        ? new ExploreDbContext(provider.GetRequiredService<DbContextOptions<ExploreDbContext>>())
                        : createDatabase();
                    context.ClearTenantFilterBypass();
                    context.TenantContext = provider.GetService<ITenantContext>();
                    context.CurrentUserService = provider.GetService<ICurrentUserService>();
                    context.BindTenantLease();
                    return context;
                });
                foreach (var descriptor in services.Where(item =>
                    item.ImplementationType?.FullName is "OpenFeature.Hosting.HostedFeatureLifecycleService"
                        or "Explore.API.Authentication.JwtAuthorityWarmupHostedService"
                        or "Explore.Infrastructure.Authentication.LocalIdentityLifecycleDeliveryWorker").ToArray())
                    services.Remove(descriptor);
            });
        }
    }

    /// <summary>Attaches test observation to physical connections, without replacing scope or proof issuers.</summary>
    private sealed class ObservedDatabases(IConfiguration configuration, IInterceptor interceptor)
        : IGlobalExploreDbContextFactory, ITenantExploreDbContextFactory
    {
        public Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            CreateAsync(null, cancellationToken);

        public Task<ExploreDbContext> CreateDbContextAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            CreateAsync(tenantId, cancellationToken);

        private Task<ExploreDbContext> CreateAsync(Guid? tenantId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var options = TestDbContextOptions.Create<ExploreDbContext>();
            var database = tenantId.HasValue
                ? PrimaryDatabaseConfiguration.BindRuntime(configuration)
                : PrimaryDatabaseConfiguration.BindGlobal(configuration);
            PrimaryDatabaseProviderComposition.ConfigureApplication(options, database);
            if (database.Provider == PrimaryDatabaseProvider.PostgreSql)
                options.AddInterceptors(PostgresTenantSessionInterceptor.RuntimeInstance);
            options.AddInterceptors(interceptor);
            var context = new ExploreDbContext(options.Options)
            {
                TenantContext = tenantId is { } target ? new SelectedTenant(target) : null
            };
            context.BindTenantLease();
            return Task.FromResult(context);
        }

        private sealed record SelectedTenant(Guid TenantId) : ITenantContext;
    }
}

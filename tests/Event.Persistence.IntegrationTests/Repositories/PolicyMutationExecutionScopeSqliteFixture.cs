using System.Security.Claims;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.Operations;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Infrastructure;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Services;
using Explore.Secrets.Database;
using Explore.Secrets.Extensions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using IAsyncInitializer = TUnit.Core.Interfaces.IAsyncInitializer;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed class PolicyMutationExecutionScopeSqliteFixture : IAsyncInitializer, IAsyncDisposable
{
    internal const string AuthorityKey = "policy.scope.authority";
    private readonly string _runtimePath = Path.Combine(Path.GetTempPath(), $"policy-runtime-{Guid.CreateVersion7():N}.db");
    private readonly string _globalPath = Path.Combine(Path.GetTempPath(), $"policy-global-{Guid.CreateVersion7():N}.db");
    private ServiceProvider? _provider;

    internal Guid CallerTenantId { get; } = Guid.CreateVersion7();
    internal Guid TargetTenantId { get; } = Guid.CreateVersion7();
    internal Guid AdministratorId { get; private set; }
    internal List<PolicyGraphConstruction> Constructions { get; } = [];
    internal List<PolicyGraphConstruction> EmailConstructions { get; } = [];
    internal IServiceProvider Services => _provider!;

    public async Task InitializeAsync()
    {
        await EmailDispatchSqliteFixture.CreateDatabaseAsync(_runtimePath);
        await EmailDispatchSqliteFixture.CreateDatabaseAsync(_globalPath);
        await using (var runtime = EmailDispatchSqliteFixture.CreateContext(_runtimePath))
        {
            AdministratorId = await InstanceSettingsCommandFixture.SeedAdministratorAsync(runtime);
            runtime.SystemSettings.Add(AuthorityMarker("\"runtime\""));
            foreach (Guid tenantId in new[] { CallerTenantId, TargetTenantId })
            {
                var tenant = new Tenant
                {
                    Id = tenantId,
                    FullName = "Policy scope tenant",
                    Slug = $"policy-scope-{tenantId:N}",
                    TenantStatusId = (int)TenantStatusEnum.Active,
                    TenantStatus = null!,
                    CreatedAt = DateTime.UtcNow
                };
                runtime.TenantSettingOverrides.Add(new TenantSetting
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenant.Id,
                    Tenant = tenant,
                    SettingKey = AuthorityKey,
                    Value = tenantId == CallerTenantId ? "\"caller\"" : "\"target\"",
                    CreatedAt = DateTime.UtcNow
                });
                var membership = new TenantUser
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenant.Id,
                    Tenant = tenant,
                    UserId = AdministratorId,
                    User = null!,
                    StatusId = (int)TenantUserStatusEnum.Active,
                    JoinedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                };
                runtime.TenantUserRoleGrants.Add(new TenantUserRoleGrant
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenant.Id,
                    Tenant = tenant,
                    TenantUserId = membership.Id,
                    TenantUser = membership,
                    RoleId = (int)RoleEnum.TenantAdmin,
                    Role = null!,
                    RoleScopeId = (int)RoleScopeEnum.Tenant,
                    GrantedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                });
            }
            await runtime.SaveChangesAsync();
        }
        await using (var global = EmailDispatchSqliteFixture.CreateContext(_globalPath))
        {
            // The independent Global administration reader must see the same persisted actor
            // as the Runtime caller; a claim alone never grants instance administration.
            var role = await global.Roles.SingleAsync(row => row.MasterCode == "platform.admin");
            var user = new User
            {
                Id = AdministratorId,
                CreatedAt = DateTime.UtcNow,
                Pii = new UserPii { Email = string.Empty, FirstName = "Policy", LastName = "Administrator" }
            };
            global.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(),
                UserId = user.Id,
                User = user,
                RoleId = role.Id,
                Role = role,
                GrantedAt = DateTime.UtcNow
            });
            global.SystemSettings.Add(AuthorityMarker("\"global\""));
            await global.SaveChangesAsync();
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["Database:Database"] = _runtimePath,
            ["Database:Global:Database"] = _globalPath,
            ["Authentication:Provider"] = "Local",
            ["Authorization:Provider"] = "local",
            ["SecretProvider:Provider"] = "Environment"
        }).Build();
        var services = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            EnvironmentName = Environments.Production
        }).Services;
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddHybridCache();
        services.AddMetrics();
        services.AddSingleton<ProjectionMetrics>();
        services.AddSingleton<BusinessMetrics>();
        services.ConfigureApplicationServices(configuration);
        services.ConfigureInfrastructureServices(configuration);
        services.AddSecretProvider(configuration);
        services.AddSecretResolution();
        services.ConfigurePersistenceServices(configuration, skipLookupCacheInitializer: true);
        var runtimeOptions = PrimaryDatabaseConfiguration.BindRuntime(configuration);
        services.AddEmailPolicyMutationPersistence(runtimeOptions, PrimaryDatabaseConfiguration.BindGlobal(configuration),
            runtimeOptions with { Role = PrimaryDatabaseRole.EmailPolicyInstanceMutation },
            runtimeOptions with { Role = PrimaryDatabaseRole.EmailPolicyTenantMutation });
        services.AddScoped<EmailDeliverySettingsWriter>(provider =>
        {
            var context = provider.GetRequiredService<ExploreDbContext>();
            EmailConstructions.Add(new(context, context.TenantFilterTenantId, context.CurrentUserService?.UserId,
                provider.GetRequiredService<ExploreDbContextExecutionLease>().PolicyPurpose));
            return ActivatorUtilities.CreateInstance<EmailDeliverySettingsWriter>(provider);
        });
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddHttpContextAccessor();
        services.AddScoped<ITenantResolverService, TenantResolverService>();
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<PolicyScopeDisposalWitness>();

        // Keep the real writer and its full DI graph. Capture the immutable lease facts at
        // construction, before execution-scope disposal resets a pooled context.
        services.AddResourceVisitorSettingsPersistence();
        var visitorFactory = services.Last(descriptor => descriptor.ServiceType == typeof(IVisitorAccessSettingsWriter))
            .ImplementationFactory!;
        services.AddScoped<IVisitorAccessSettingsWriter>(provider =>
        {
            var context = provider.GetRequiredService<ExploreDbContext>();
            Constructions.Add(new(context, context.TenantFilterTenantId, context.CurrentUserService?.UserId,
                provider.GetRequiredService<ExploreDbContextExecutionLease>().PolicyPurpose));
            return (IVisitorAccessSettingsWriter)visitorFactory(provider);
        });
        services.ValidateNativeOperationRegistrations();
        _provider = services.BuildIsolatedServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    internal AsyncServiceScope OpenCaller()
    {
        var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(CallerTenantId);
        return scope;
    }

    internal IDisposable AuthenticateAdministrator()
    {
        var accessor = Services.GetRequiredService<IHttpContextAccessor>();
        var previous = accessor.HttpContext;
        accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, AdministratorId.ToString())], "policy-scope-fixture"))
        };
        return new RestoreHttpContext(accessor, previous);
    }

    private static SystemSetting AuthorityMarker(string value) => new()
    {
        Id = Guid.CreateVersion7(),
        SettingKey = AuthorityKey,
        Value = value,
        ValueType = SettingRegistry.Get(GovernanceSettingKeys.Email.FromName)!.ValueType,
        CreatedAt = DateTime.UtcNow
    };

    public async ValueTask DisposeAsync()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();
        // Clear only this fixture's file pools, never another test's SQLite connections.
        foreach (string path in new[] { _runtimePath, _globalPath })
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            SqliteConnection.ClearPool(connection);
            File.Delete(path);
            File.Delete(path + "-wal");
            File.Delete(path + "-shm");
        }
    }

    private sealed class RestoreHttpContext(IHttpContextAccessor accessor, HttpContext? previous) : IDisposable
    {
        public void Dispose() => accessor.HttpContext = previous;
    }
}

internal sealed record PolicyGraphConstruction(
    ExploreDbContext Context, Guid? TenantId, Guid? ActorId, PolicyMutationPurpose? Purpose);

internal sealed class PolicyScopeDisposalWitness : IAsyncDisposable
{
    internal bool IsDisposed { get; private set; }

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        return ValueTask.CompletedTask;
    }
}

using System.IdentityModel.Tokens.Jwt;
using Event.Api.IntegrationTests.Builders;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Seed;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Fixtures;

internal static partial class TestDbContextServiceCollectionExtensions
{
    public static IServiceCollection AddKeycloakSecurityDbContext(
        this IServiceCollection services,
        string databaseName,
        KeycloakTokenClient keycloakTokens,
        Guid tenantId)
    {
        services.AddSingleton(_ =>
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = databaseName,
                Mode = SqliteOpenMode.Memory,
                Cache = SqliteCacheMode.Shared,
                Pooling = false
            }.ToString());
            try
            {
                connection.Open();
                var options = new DbContextOptionsBuilder<ExploreDbContext>()
                    .UseSqlite(connection.ConnectionString).UseSnakeCaseNamingConvention()
                    .AddInterceptors(SqliteNamedLockTransactionInterceptor.Instance,
                        SqliteProjectionLockTransactionInterceptor.Instance);
                using var database = new ExploreDbContext(options.Options);
                database.BindTenantLease();
                database.Database.EnsureCreated();
                SqliteDatabaseInitializer.InitializeAsync(database, CancellationToken.None).GetAwaiter().GetResult();
                LookupTableSeeder.SeedAsync(database).GetAwaiter().GetResult();
                database.Tenants.Add(new TenantBuilder().WithId(tenantId)
                    .WithSlug($"security-{tenantId:N}").Build());
                foreach (var (id, code, scope) in new[]
                {
                    (RoleEnum.Admin, "platform.admin", RoleScopeEnum.Platform),
                    (RoleEnum.TenantAdmin, "tenant.admin", RoleScopeEnum.Tenant)
                })
                {
                    if (!database.Roles.Any(role => role.Id == (int)id))
                        database.Roles.Add(new Role
                        {
                            Id = (int)id,
                            MasterCode = code,
                            FullName = code,
                            Scope = scope,
                            RoleScope = null!,
                            IsSystem = true
                        });
                }
                Guid tenantAdministratorId = Guid.Empty;
                foreach (var (token, role) in new (string, RoleEnum?)[]
                {
                    (keycloakTokens.GetAdminTokenAsync().GetAwaiter().GetResult(), RoleEnum.Admin),
                    (keycloakTokens.GetUserTokenAsync().GetAwaiter().GetResult(), null),
                    (keycloakTokens.GetTenantAdminTokenAsync().GetAwaiter().GetResult(), RoleEnum.TenantAdmin)
                })
                {
                    var principal = new JwtSecurityTokenHandler().ReadJwtToken(token);
                    Guid userId = Guid.CreateVersion7();
                    database.Users.Add(new UserBuilder().WithId(userId).Build());
                    database.UserExternalLogins.Add(new UserExternalLogin
                    {
                        Id = Guid.CreateVersion7(),
                        UserId = userId,
                        User = null!,
                        AuthenticationProviderId = (int)"keycloak".ParseAuthenticationProviderKind(),
                        AuthenticationProvider = null!,
                        ProviderKey = PlatformIdentityPrincipalExtensions
                            .CreateOidcAccountKey(principal.Issuer, principal.Subject).Value,
                        ProviderDisplayName = "keycloak",
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = userId
                    });
                    if (role == RoleEnum.Admin)
                        database.PlatformUserRoles.Add(new PlatformUserRole
                        {
                            Id = Guid.CreateVersion7(),
                            UserId = userId,
                            User = null!,
                            RoleId = (int)RoleEnum.Admin,
                            Role = null!,
                            GrantedAt = DateTime.UtcNow,
                            GrantedBy = userId
                        });
                    else if (role == RoleEnum.TenantAdmin)
                        tenantAdministratorId = userId;
                }
                database.SaveChanges();
                using var tenantDatabase = new ExploreDbContext(options.Options)
                {
                    TenantContext = new TenantIdentity(tenantId)
                };
                tenantDatabase.BindTenantLease();
                Guid membershipId = Guid.CreateVersion7();
                tenantDatabase.TenantUsers.Add(new TenantUser
                {
                    Id = membershipId,
                    TenantId = tenantId,
                    Tenant = null!,
                    UserId = tenantAdministratorId,
                    User = null!,
                    StatusId = (int)TenantUserStatusEnum.Active,
                    JoinedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                });
                tenantDatabase.TenantUserRoleGrants.Add(new TenantUserRoleGrant
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenantId,
                    Tenant = null!,
                    TenantUserId = membershipId,
                    TenantUser = null!,
                    RoleId = (int)RoleEnum.TenantAdmin,
                    Role = null!,
                    RoleScopeId = (int)RoleScopeEnum.Tenant,
                    GrantedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                });
                tenantDatabase.SaveChanges();
                return connection;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        });
        services.AddDbContextFactory<ExploreDbContext>((provider, options) => options
            .UseSqlite(provider.GetRequiredService<SqliteConnection>().ConnectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(SqliteNamedLockTransactionInterceptor.Instance,
                SqliteProjectionLockTransactionInterceptor.Instance));
        return services.AddTestContextOwners();
    }

    public static IServiceCollection AddInMemoryExploreDbContext(
        this IServiceCollection services,
        string databaseName)
    {
        services.AddDbContextFactory<ExploreDbContext>(options =>
        {
            options.UseInMemoryDatabase(databaseName);
            options.ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning));
        });

        return services.AddTestContextOwners();
    }

    public static IServiceCollection AddPostgreSqlExploreDbContext(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContextFactory<ExploreDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
            options.UseSnakeCaseNamingConvention();
            options.ConfigureWarnings(warnings =>
                warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
        });

        services.AddPolicyMutationExecutionScopes();
        services.AddCheckoutDispatchPersistence();
        services.AddScoped(sp =>
        {
            if (sp.GetRequiredService<ExploreDbContextExecutionLease>().Context is { } selected)
                return selected;
            var factory = sp.GetRequiredService<IDbContextFactory<ExploreDbContext>>();
            var context = factory.CreateDbContext();

            context.ClearTenantFilterBypass();
            context.TenantContext = sp.GetService<ITenantContext>();
            context.CurrentUserService = sp.GetService<ICurrentUserService>();
            context.BindTenantLease();

            return context;
        });

        return services;
    }

    public static IServiceCollection RemoveExploreDbContextRegistrations(this IServiceCollection services)
    {
        var descriptors = services
            .Where(IsExploreDbContextRegistration)
            .ToList();

        foreach (var descriptor in descriptors)
        {
            services.Remove(descriptor);
        }

        return services;
    }

    private static bool IsExploreDbContextRegistration(ServiceDescriptor descriptor)
    {
        return IsExploreDbContextType(descriptor.ServiceType)
            || IsExploreDbContextType(descriptor.ImplementationType);
    }

    private static bool IsExploreDbContextType(Type? type)
    {
        if (type is null)
        {
            return false;
        }

        if (type == typeof(ExploreDbContext)
            || type == typeof(DbContextOptions)
            || type == typeof(DbContextOptions<ExploreDbContext>)
            || type == typeof(IDbContextFactory<ExploreDbContext>))
        {
            return true;
        }

        return type.IsGenericType
            && type.GetGenericArguments().Contains(typeof(ExploreDbContext))
            && type.Namespace?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == true;
    }
}

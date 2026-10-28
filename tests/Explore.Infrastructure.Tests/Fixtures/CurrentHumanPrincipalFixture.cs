using System.IdentityModel.Tokens.Jwt;
using System.Diagnostics;
using System.Data.Common;
using System.Security.Cryptography;
using Explore.API.Extensions;
using Explore.Application;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Users.Handlers.Queries;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure;
using Explore.Infrastructure.Identity;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Seed;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Explore.Infrastructure.Tests.Fixtures;

/// <summary>
/// A selected relational request owner with real JWT validation, persisted provider binding and
/// native human source ports. The in-process issuer replaces only external OIDC transport.
/// No API host, successful admission substitute or cached source observation is involved.
/// </summary>
internal sealed class CurrentHumanPrincipalFixture : IAsyncDisposable
{
    private readonly RSA _signingKey = RSA.Create(2048);
    private readonly string _issuer = $"https://principal-{Guid.CreateVersion7():N}.invalid/realms/event";
    private readonly string _subject = $"principal-{Guid.CreateVersion7():N}";
    private readonly string _databasePath;
    private readonly bool _ownsDatabase;
    private readonly ExploreDbContext? _borrowedContext;
    private readonly ICurrentUserService? _originalCurrentUserService;
    private readonly Func<UserExternalLogin, Task>? _seedBinding;
    private readonly ServiceProvider _provider;
    private readonly ServiceProvider? _executionProvider;
    private readonly AsyncServiceScope _scope;
    private readonly IHttpContextAccessor _contexts;
    private readonly DefaultHttpContext _request;
    private readonly DatabaseObservation _databaseObservation;
    private bool _disposed;

    internal Guid UserId { get; }
    internal ITenantContext Tenant { get; }
    internal ExploreDbContext Context => Services.GetRequiredService<ExploreDbContext>();
    internal IServiceProvider Services => _scope.ServiceProvider;
    internal IAdminContext Admin => Services.GetRequiredService<IAdminContext>();
    internal MachinePrincipalAccessor Machine => Services.GetRequiredService<MachinePrincipalAccessor>();
    internal IHumanCerbosPrincipalReader Reader => Services.GetRequiredService<IHumanCerbosPrincipalReader>();
    internal int DatabaseCommands => _databaseObservation.Commands;

    /// <summary>
    /// Borrowed mode retains the supplied relational owner and requires its user, tenant and
    /// authentication-provider lookup to be seeded already. Service overrides are for external
    /// dependencies or the real privacy owner, never substitute preparations or finalization.
    /// PostgreSQL additionally requires the original structured purposes and a Migrator-owned
    /// callback that persists the supplied genuine issuer/subject binding before authentication.
    /// </summary>
    internal CurrentHumanPrincipalFixture(Guid userId, Guid tenantId, string? databasePath = null,
        ExploreDbContext? borrowedContext = null, Action<IServiceCollection>? configureServices = null,
        TimeProvider? timeProvider = null, IConfiguration? purposeConfiguration = null,
        Func<UserExternalLogin, Task>? seedBinding = null)
    {
        UserId = userId;
        _borrowedContext = borrowedContext;
        _seedBinding = seedBinding;
        if (borrowedContext is not null)
            _subject = userId.ToString("D");
        if (borrowedContext is not null && (!borrowedContext.Database.IsRelational() || databasePath is not null))
            throw new ArgumentException("Borrowing requires a relational context and no separate database path.", nameof(borrowedContext));
        bool postgres = borrowedContext?.Database.IsNpgsql() == true;
        if (postgres)
        {
            ArgumentNullException.ThrowIfNull(purposeConfiguration);
            ArgumentNullException.ThrowIfNull(seedBinding);
            var runtime = PrimaryDatabaseConfiguration.BindRuntime(purposeConfiguration);
            var options = borrowedContext!.GetService<IDbContextOptions>();
            if (options.FindExtension<PrimaryDatabasePublicIdentityOptionsExtension>()?.PublicDigest
                    != PrimaryDatabasePublicIdentity.Digest(runtime)
                || options.FindExtension<PostgresRuntimeIdentityOptionsExtension>()?.DeclaredLogin != runtime.Username)
                throw new ArgumentException("Structured Runtime purpose must match the borrowed physical owner and schema.", nameof(purposeConfiguration));
        }
        Tenant = borrowedContext is null ? new SelectedTenant(tenantId)
            : borrowedContext.TenantContext
                ?? throw new ArgumentException("The borrowed context must already select its tenant.", nameof(borrowedContext));
        if (Tenant.TenantId != tenantId)
            throw new ArgumentException("The selected tenant must match the borrowed context.", nameof(tenantId));
        _ownsDatabase = borrowedContext is null && databasePath is null;
        _databasePath = borrowedContext?.Database.GetDbConnection().DataSource
            ?? databasePath ?? Path.Combine(Path.GetTempPath(), $"human-principal-{Guid.CreateVersion7():N}.db");
        _databaseObservation = new DatabaseObservation(_databasePath);
        var configurationBuilder = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = borrowedContext?.Database.ProviderName switch
            {
                RelationalNamedLock.SqlServerProvider => "SqlServer",
                RelationalNamedLock.MySqlProvider => "MySql",
                "Npgsql.EntityFrameworkCore.PostgreSQL" => "PostgreSql",
                null or "Microsoft.EntityFrameworkCore.Sqlite" => "Sqlite",
                _ => throw new NotSupportedException("The borrowed native database provider is not supported by this fixture.")
            },
            ["Database:Database"] = borrowedContext is not null && !borrowedContext.Database.IsSqlite()
                ? borrowedContext.Database.GetDbConnection().Database : _databasePath,
            ["Database:Global:Database"] = borrowedContext is not null && !borrowedContext.Database.IsSqlite()
                ? borrowedContext.Database.GetDbConnection().Database : _databasePath
        });
        if (purposeConfiguration is not null)
            configurationBuilder.AddConfiguration(purposeConfiguration);
        IConfiguration configuration = configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["IdentityDatabase:Topology"] = "Colocated",
            ["Authentication:Provider"] = "keycloak",
            ["Keycloak:Authority"] = _issuer,
            ["Keycloak:MetadataAddress"] = $"{_issuer}/.well-known/openid-configuration",
            ["Keycloak:Audience"] = "islamu-event-api",
            ["Keycloak:RequireHttpsMetadata"] = "false"
        }).Build();
        var services = new ServiceCollection();
        var environment = new FixtureEnvironment();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddSingleton<IWebHostEnvironment>(environment);
        services.AddSingleton(Tenant);
        if (borrowedContext is not null)
        {
            services.AddSingleton<IHostEnvironment>(environment);
            services.AddHybridCache();
            services.AddMetrics();
            services.AddSingleton<ProjectionMetrics>();
            services.AddSingleton<BusinessMetrics>();
            services.ConfigureApplicationServices(configuration);
            services.ConfigureInfrastructureServices(configuration, environment, registerBackgroundWorkers: false);
            services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        }
        services.AddApiAuthentication(configuration, environment, skipAuthorityWarmup: true);
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            var metadata = new OpenIdConnectConfiguration { Issuer = _issuer };
            metadata.SigningKeys.Add(new RsaSecurityKey(_signingKey.ExportParameters(false)) { KeyId = "fixture-issuer" });
            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
        });
        services.ConfigurePersistenceServices(configuration,
            skipDbContextRegistration: borrowedContext is not null && !postgres, skipLookupCacheInitializer: true);
        if (borrowedContext is not null && !postgres)
        {
            // Clone native options, not deployment configuration or the caller's physical connection.
            var options = (DbContextOptions<ExploreDbContext>)borrowedContext.GetService<IDbContextOptions>();
            var readers = new NativeContexts(new DbContextOptionsBuilder<ExploreDbContext>(options)
                .UseModel(borrowedContext.Model).Options,
                DbProviderFactories.GetFactory(borrowedContext.Database.GetDbConnection())
                    ?? throw new InvalidOperationException("Borrowed contexts require a native provider connection factory."),
                options.Extensions.OfType<RelationalOptionsExtension>().Single().ConnectionString
                    ?? borrowedContext.Database.GetDbConnection().ConnectionString);
            services.AddSingleton<IGlobalExploreDbContextFactory>(readers);
            services.AddSingleton<ITenantExploreDbContextFactory>(readers);
            services.AddPolicyMutationExecutionScopes();
        }
        services.AddScoped<IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>,
            ResolveCurrentUserIdByIdentityRequestHandler>();
        services.AddScoped<IHumanCommitScopePreparation>(provider =>
            (IHumanCommitScopePreparation)provider.GetRequiredService<IPolicyMutationExecutionScopeFactory>());
        services.AddScoped<IAdminContext, AdminContext>();
        services.AddScoped<MachinePrincipalAccessor>();
        services.AddScoped<IMachinePrincipalAccessor>(provider => provider.GetRequiredService<MachinePrincipalAccessor>());
        services.AddScoped<IHumanCerbosPrincipalReader>(provider => new HumanCerbosPrincipalReader(
            provider.GetRequiredService<IHumanCommitScopePreparation>(),
            provider.GetRequiredService<IHttpContextAccessor>(),
            provider.GetRequiredService<IHumanAuthenticationLifetimeObserver>(),
            provider.GetRequiredService<IMachinePrincipalAccessor>(), Tenant, provider.GetRequiredService<TimeProvider>(),
            () => provider.GetRequiredService<IHumanPrincipalSourceFence>()));
        if (timeProvider is not null)
            services.AddSingleton(timeProvider);
        configureServices?.Invoke(services);
        if (borrowedContext is not null)
        {
            services.RemoveAll<ExploreDbContext>();
            services.AddScoped(provider =>
                InventoryHoldExpiryExecutionScopeFactory.GetSelectedRuntimeContext(provider)
                ?? GenericOutboxExecutionScopeFactory.GetSelectedRuntimeContext(provider)
                ?? throw new InvalidOperationException("A child context requires its native selected execution owner."));
            _executionProvider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            var executionScopes = _executionProvider.GetRequiredService<IServiceScopeFactory>();
            services.AddScoped<IPolicyMutationExecutionScopeFactory>(provider =>
                new PolicyMutationExecutionScopeFactory(provider, executionScopes,
                    provider.GetRequiredService<IGlobalExploreDbContextFactory>(),
                    provider.GetRequiredService<ITenantExploreDbContextFactory>(),
                    provider.GetRequiredService<ExploreDbContextExecutionLease>()));
            services.AddScoped<IConfigurationInstanceImportExecutor>(provider =>
                new ConfigurationInstanceImportExecutor(provider, executionScopes,
                    provider.GetRequiredService<IGlobalExploreDbContextFactory>()));
            services.AddScoped<IConfigurationManifestTenantReadScopeFactory>(provider =>
                new ConfigurationManifestTenantReadScopeFactory(provider, executionScopes,
                    provider.GetRequiredService<ITenantExploreDbContextFactory>()));
            services.RemoveAll<ExploreDbContext>();
            // The parent borrows externally; native child scopes capture and dispose only their selected owner.
            services.AddSingleton(borrowedContext);
        }
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        _scope = _provider.CreateAsyncScope();
        _contexts = Services.GetRequiredService<IHttpContextAccessor>();
        _request = new DefaultHttpContext { RequestServices = Services };
        _request.Request.Path = "/principal-fixture";
        if (borrowedContext is not null)
        {
            _originalCurrentUserService = borrowedContext.CurrentUserService;
            borrowedContext.CurrentUserService = Services.GetRequiredService<ICurrentUserService>();
        }
    }

    // Installation must happen synchronously in the caller's execution context.
    internal Task InitializeAsync(bool authenticate = true)
    {
        _contexts.HttpContext = _request;
        if (_borrowedContext is not null)
            Services.GetRequiredService<ITenantContextAccessor>().SetTenant(Tenant.TenantId);
        return InitializeCoreAsync(authenticate);
    }

    private async Task InitializeCoreAsync(bool authenticate)
    {
        if (_borrowedContext is null)
            await Context.Database.EnsureCreatedAsync();
        else if (Context.Database.CurrentTransaction is not null || Context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Borrowed authentication requires committed seed data and no owning transaction.");
        if (_ownsDatabase)
        {
            await SqliteDatabaseInitializer.InitializeAsync(Context, CancellationToken.None);
            await LookupTableSeeder.SeedAsync(Context, CancellationToken.None);
            Context.Users.Add(new User
            {
                Id = UserId,
                Pii = new UserPii
                {
                    Email = $"{UserId:N}@example.test",
                    FirstName = "Principal",
                    LastName = "Fixture"
                }
            });
            Context.Tenants.Add(new Tenant
            {
                Id = Tenant.TenantId,
                FullName = "Principal fixture",
                Slug = $"principal-{Tenant.TenantId:N}",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!
            });
        }
        if (_borrowedContext is null
            && !await Context.Set<AuthenticationProvider>().AnyAsync(row => row.Id == (int)AuthenticationProviderKind.Keycloak))
            Context.Set<AuthenticationProvider>().Add(new AuthenticationProvider
            {
                Id = (int)AuthenticationProviderKind.Keycloak,
                MasterCode = "keycloak",
                FullName = "Keycloak"
            });
        var binding = new UserExternalLogin
        {
            Id = Guid.CreateVersion7(),
            UserId = UserId,
            User = null!,
            AuthenticationProviderId = (int)AuthenticationProviderKind.Keycloak,
            AuthenticationProvider = null!,
            ProviderKey = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(_issuer, _subject).Value
        };
        if (_seedBinding is not null)
            await _seedBinding(binding);
        else
        {
            Context.UserExternalLogins.Add(binding);
            await Context.SaveChangesAsync();
            if (_borrowedContext is null)
                Context.ChangeTracker.Clear();
            else
                Context.Entry(binding).State = EntityState.Detached;
        }
        if (!authenticate)
            return;

        var validationNow = DateTimeOffset.UtcNow;
        var selectedNow = Services.GetRequiredService<TimeProvider>().GetUtcNow();
        var expiresAt = (selectedNow > validationNow ? selectedNow : validationNow).AddDays(1);
        var payload = new JwtPayload
        {
            ["iss"] = _issuer,
            ["sub"] = _subject,
            ["aud"] = "islamu-event-api",
            ["typ"] = "Bearer",
            ["nbf"] = DateTimeOffset.UnixEpoch.ToUnixTimeSeconds(),
            ["exp"] = expiresAt.ToUnixTimeSeconds()
        };
        string? client = Services.GetRequiredService<IConfiguration>()
            .GetSection("Keycloak:PermittedClients").Get<string[]>()?.FirstOrDefault();
        if (client is not null)
            payload["azp"] = client;
        var key = new RsaSecurityKey(_signingKey) { KeyId = "fixture-issuer" };
        string token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            new JwtHeader(new SigningCredentials(key, SecurityAlgorithms.RsaSha256)), payload));
        _request.Request.Headers.Authorization = "Bearer " + token;
        var result = await _request.AuthenticateAsync();
        if (!result.Succeeded)
            throw new InvalidOperationException("The fixture's signed original human was not authenticated.", result.Failure);
        _request.User = result.Principal!;
        if (_borrowedContext?.Database.IsNpgsql() == true && await Admin.ResolveUserIdAsync() != UserId)
            throw new UnauthorizedAccessException("The signed PostgreSQL human requires its persisted Global-readable provider binding.");
    }

    internal void EnterRequest() => _contexts.HttpContext = _request;

    /// <summary>Binds real authenticated custody on the selected context before its owning transaction starts.</summary>
    internal Task PrepareAsync(CancellationToken cancellationToken = default)
    {
        EnterRequest();
        return Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(cancellationToken).AsTask();
    }

    internal void SignOut()
    {
        _request.User = new System.Security.Claims.ClaimsPrincipal();
        _request.Request.Headers.Remove("Authorization");
        EnterRequest();
    }

    internal async Task GrantInstanceAdminAsync()
    {
        Context.PlatformUserRoles.Add(new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = UserId,
            User = null!,
            RoleId = (int)RoleEnum.Admin,
            Role = null!,
            GrantedAt = DateTime.UtcNow
        });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
    }

    internal async Task GrantTenantAdminAsync()
    {
        var member = new TenantUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = Tenant.TenantId,
            Tenant = null!,
            UserId = UserId,
            User = null!,
            StatusId = (int)TenantUserStatusEnum.Active
        };
        Context.TenantUserRoleGrants.Add(new TenantUserRoleGrant
        {
            Id = Guid.CreateVersion7(),
            TenantId = Tenant.TenantId,
            Tenant = null!,
            TenantUserId = member.Id,
            TenantUser = member,
            RoleId = (int)RoleEnum.TenantAdmin,
            Role = null!,
            RoleScopeId = (int)RoleScopeEnum.Tenant,
            GrantedAt = DateTime.UtcNow
        });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
    }

    internal async Task GrantOrganizationAdminAsync(Guid organizationId)
    {
        var placement = new OrganizationTenant
        {
            Id = Guid.CreateVersion7(),
            TenantId = Tenant.TenantId,
            Tenant = null!,
            OrganizationId = organizationId,
            Organization = new Organization { Id = organizationId, Pii = new OrganizationPii { FullName = "Principal organization" } },
            ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
            ApprovalStatus = null!
        };
        Context.OrganizationMembers.Add(new OrganizationMember
        {
            Id = Guid.CreateVersion7(),
            TenantId = Tenant.TenantId,
            Tenant = null!,
            OrganizationTenantId = placement.Id,
            OrganizationTenant = placement,
            UserId = UserId,
            User = null!,
            RoleId = (int)RoleEnum.OrgAdmin,
            Role = null!
        });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
    }

    internal async Task GrantGroupAdminAsync(Guid groupId)
    {
        var placement = new GroupTenant
        {
            Id = Guid.CreateVersion7(),
            TenantId = Tenant.TenantId,
            Tenant = null!,
            GroupId = groupId,
            Group = new Group { Id = groupId, FullName = "Principal group" },
            ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
            ApprovalStatus = null!
        };
        Context.GroupMembers.Add(new GroupMember
        {
            Id = Guid.CreateVersion7(),
            TenantId = Tenant.TenantId,
            Tenant = null!,
            GroupTenantId = placement.Id,
            GroupTenant = placement,
            UserId = UserId,
            User = null!,
            RoleId = (int)RoleEnum.GroupAdmin,
            Role = null!
        });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
    }

    internal async Task GrantEventOwnerAsync(Guid eventId)
    {
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            UserId = UserId,
            User = null!,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = "Principal owner" }
        };
        Context.Events.Add(new Explore.Domain.Event
        {
            Id = eventId,
            Title = "Principal event",
            TenantId = Tenant.TenantId,
            Tenant = null!,
            ActorId = actor.Id,
            Actor = actor,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormat = null!,
            EventStatus = null!
        });
        Context.EventRoleAssignments.Add(EventRoleAssignment.Create(Tenant.TenantId, eventId, UserId,
            (int)RoleEnum.EventOwner, EventRoleAssignmentStatus.Active, DateTime.UnixEpoch, null, UserId));
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        string? ownedConnectionString = _borrowedContext is null ? Context.Database.GetConnectionString() : null;
        _contexts.HttpContext = null;
        if (_borrowedContext is not null)
            _borrowedContext.CurrentUserService = _originalCurrentUserService;
        await _scope.DisposeAsync();
        if (_executionProvider is not null)
            await _executionProvider.DisposeAsync();
        await _provider.DisposeAsync();
        _signingKey.Dispose();
        _databaseObservation.Dispose();
        if (_borrowedContext is null)
        {
            using var connection = new SqliteConnection(ownedConnectionString);
            SqliteConnection.ClearPool(connection);
        }
        if (_ownsDatabase)
        {
            File.Delete(_databasePath);
            File.Delete(_databasePath + "-wal");
            File.Delete(_databasePath + "-shm");
        }
    }

    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;

    /// <summary>Independent native connections for identity reads and production-selected child owners.</summary>
    private sealed class NativeContexts(DbContextOptions<ExploreDbContext> options,
        DbProviderFactory connections, string connectionString)
        : IGlobalExploreDbContextFactory, ITenantExploreDbContextFactory
    {
        public Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            CreateAsync(null, cancellationToken);

        public Task<ExploreDbContext> CreateDbContextAsync(Guid tenantId, CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
            return CreateAsync(tenantId, cancellationToken);
        }

        private Task<ExploreDbContext> CreateAsync(Guid? tenantId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var connection = connections.CreateConnection()
                ?? throw new InvalidOperationException("The selected native provider did not create a connection.");
            try
            {
                connection.ConnectionString = connectionString;
                var builder = new DbContextOptionsBuilder<ExploreDbContext>(options);
                ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(
                    options.Extensions.OfType<RelationalOptionsExtension>().Single().WithConnection(connection, true));
                var context = new ExploreDbContext(builder.Options)
                {
                    TenantContext = tenantId is { } tenant ? new SelectedTenant(tenant) : null
                };
                context.BindTenantLease();
                return Task.FromResult(context);
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }
    }

    private sealed class DatabaseObservation : IObserver<DiagnosticListener>,
        IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly string _path;
        private readonly IDisposable _listeners;
        private IDisposable? _commands;
        private int _count;

        internal DatabaseObservation(string path)
        {
            _path = path;
            _listeners = DiagnosticListener.AllListeners.Subscribe(this);
        }

        internal int Commands => Volatile.Read(ref _count);

        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name == DbLoggerCategory.Name)
                _commands = listener.Subscribe(this, name => name == RelationalEventId.CommandExecuting.Name);
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Value is CommandEventData command
                && string.Equals(command.Command.Connection?.DataSource, _path, StringComparison.Ordinal))
                Interlocked.Increment(ref _count);
        }

        public void OnError(Exception error) { }
        public void OnCompleted() { }

        public void Dispose()
        {
            _listeners.Dispose();
            _commands?.Dispose();
        }
    }

    private sealed class FixtureEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = typeof(CurrentHumanPrincipalFixture).Assembly.GetName().Name!;
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}

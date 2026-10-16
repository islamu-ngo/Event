using System.IdentityModel.Tokens.Jwt;
using System.Diagnostics;
using System.Security.Cryptography;
using Explore.API.Extensions;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Users.Handlers.Queries;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Seed;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Explore.Infrastructure.Tests.Fixtures;

/// <summary>
/// A selected SQLite request owner with real JWT validation, persisted provider binding and
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
    private readonly ServiceProvider _provider;
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
    internal int DatabaseCommands => _databaseObservation.Commands;

    internal CurrentHumanPrincipalFixture(Guid userId, Guid tenantId, string? databasePath = null)
    {
        UserId = userId;
        Tenant = new SelectedTenant(tenantId);
        _ownsDatabase = databasePath is null;
        _databasePath = databasePath ?? Path.Combine(Path.GetTempPath(), $"human-principal-{Guid.CreateVersion7():N}.db");
        _databaseObservation = new DatabaseObservation(_databasePath);
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["Database:Database"] = _databasePath,
            ["Database:Global:Database"] = _databasePath,
            ["IdentityDatabase:Topology"] = "Colocated",
            ["Authentication:Provider"] = "keycloak",
            ["Keycloak:Authority"] = _issuer,
            ["Keycloak:RequireHttpsMetadata"] = "false"
        }).Build();
        var services = new ServiceCollection();
        var environment = new FixtureEnvironment();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddSingleton<IWebHostEnvironment>(environment);
        services.AddSingleton(Tenant);
        services.AddApiAuthentication(configuration, environment, skipAuthorityWarmup: true);
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            var metadata = new OpenIdConnectConfiguration { Issuer = _issuer };
            metadata.SigningKeys.Add(new RsaSecurityKey(_signingKey.ExportParameters(false)) { KeyId = "fixture-issuer" });
            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
        });
        services.ConfigurePersistenceServices(configuration, skipLookupCacheInitializer: true);
        services.AddScoped<IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>,
            ResolveCurrentUserIdByIdentityRequestHandler>();
        services.AddScoped<IAdminContext, AdminContext>();
        services.AddScoped<MachinePrincipalAccessor>();
        services.AddScoped<IMachinePrincipalAccessor>(provider => provider.GetRequiredService<MachinePrincipalAccessor>());
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        _scope = _provider.CreateAsyncScope();
        _contexts = Services.GetRequiredService<IHttpContextAccessor>();
        _request = new DefaultHttpContext { RequestServices = Services };
        _request.Request.Path = "/principal-fixture";
    }

    // Installation must happen synchronously in the caller's execution context.
    internal Task InitializeAsync(bool authenticate = true)
    {
        _contexts.HttpContext = _request;
        return InitializeCoreAsync(authenticate);
    }

    private async Task InitializeCoreAsync(bool authenticate)
    {
        await Context.Database.EnsureCreatedAsync();
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
        if (!await Context.Set<AuthenticationProvider>().AnyAsync(row => row.Id == (int)AuthenticationProviderKind.Keycloak))
            Context.Set<AuthenticationProvider>().Add(new AuthenticationProvider
            {
                Id = (int)AuthenticationProviderKind.Keycloak,
                MasterCode = "keycloak",
                FullName = "Keycloak"
            });
        Context.UserExternalLogins.Add(new UserExternalLogin
        {
            Id = Guid.CreateVersion7(),
            UserId = UserId,
            User = null!,
            AuthenticationProviderId = (int)AuthenticationProviderKind.Keycloak,
            AuthenticationProvider = null!,
            ProviderKey = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(_issuer, _subject).Value
        });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
        if (!authenticate)
            return;

        var payload = new JwtPayload
        {
            ["iss"] = _issuer,
            ["sub"] = _subject,
            ["aud"] = "islamu-event-api",
            ["typ"] = "Bearer",
            ["nbf"] = DateTimeOffset.UnixEpoch.ToUnixTimeSeconds(),
            ["exp"] = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds()
        };
        var key = new RsaSecurityKey(_signingKey) { KeyId = "fixture-issuer" };
        string token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            new JwtHeader(new SigningCredentials(key, SecurityAlgorithms.RsaSha256)), payload));
        _request.Request.Headers.Authorization = "Bearer " + token;
        var result = await _request.AuthenticateAsync();
        if (!result.Succeeded)
            throw new InvalidOperationException("The fixture's signed original human was not authenticated.", result.Failure);
        _request.User = result.Principal!;
    }

    internal void EnterRequest() => _contexts.HttpContext = _request;

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
        using var connection = new SqliteConnection(Context.Database.GetConnectionString());
        _contexts.HttpContext = null;
        await _scope.DisposeAsync();
        await _provider.DisposeAsync();
        _signingKey.Dispose();
        _databaseObservation.Dispose();
        SqliteConnection.ClearPool(connection);
        if (_ownsDatabase)
        {
            File.Delete(_databasePath);
            File.Delete(_databasePath + "-wal");
            File.Delete(_databasePath + "-shm");
        }
    }

    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;

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

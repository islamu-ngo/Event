
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Explore.Application;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure;
using Explore.Infrastructure.Services;
using Explore.Infrastructure.Services.Keycloak;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Event.Persistence.IntegrationTests.Fixtures;

internal sealed class AccountAuthorityLifecycleEmailFixture : IAsyncDisposable
{
    internal const string Issuer = "https://keycloak.example.test/auth/realms/ISLAMU";
    internal const string Subject = "keycloak-user-123";
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"authority-routing-{Guid.CreateVersion7():N}.db");
    private ServiceProvider _provider = null!;
    private AsyncServiceScope _scope;

    internal ExploreDbContext Context => _scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
    internal IAccountAuthorityLifecycleEmailService Service =>
        _scope.ServiceProvider.GetRequiredService<IAccountAuthorityLifecycleEmailService>();
    internal IServiceProvider Services => _scope.ServiceProvider;
    internal ProviderHttpHandler Http { get; } = new();
    internal Guid TenantId { get; } = Guid.CreateVersion7();
    internal Guid UserId { get; } = Guid.CreateVersion7();
    internal Guid KeycloakLoginId { get; private set; }
    internal Guid LocalLoginId { get; private set; }
    internal Guid AtprotoLoginId { get; private set; }

    internal static async Task<AccountAuthorityLifecycleEmailFixture> CreateAsync(
        bool lifecycleEnabled = true, bool providerConfigured = true,
        string baseUrl = "https://keycloak.example.test/auth")
    {
        var fixture = new AccountAuthorityLifecycleEmailFixture();
        await EmailDispatchSqliteFixture.CreateDatabaseAsync(fixture._path);
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.ConfigureApplicationServices(configuration);
        services.ConfigureInfrastructureServices(configuration);
        services.AddScoped(_ => EmailDispatchSqliteFixture.CreateContext(fixture._path));
        services.AddIdentityCore<LocalIdentityUser>().AddEntityFrameworkStores<ExploreDbContext>().AddLocalLifecycleTokenProviders();
        services.AddScoped(provider => new LocalIdentityCredentialStateStore(provider.GetRequiredService<ExploreDbContext>(),
            provider.GetRequiredService<ExploreDbContext>(), provider.GetRequiredService<UserManager<LocalIdentityUser>>(), TimeProvider.System));
        services.AddScoped<ILocalIdentityLifecycleStore>(provider => new LocalIdentityLifecycleStore(provider.GetRequiredService<ExploreDbContext>(),
            provider.GetRequiredService<ExploreDbContext>(), provider.GetRequiredService<UserManager<LocalIdentityUser>>(), TimeProvider.System,
            provider.GetRequiredService<LocalIdentityCredentialStateStore>()));
        services.AddScoped<ILocalIdentityLifecycleDeliveryStore>(provider => new LocalIdentityLifecycleDeliveryStore(provider.GetRequiredService<ExploreDbContext>(),
            provider.GetRequiredService<ExploreDbContext>(), provider.GetRequiredService<LocalIdentityCredentialStateStore>(), TimeProvider.System));
        services.AddScoped<IUserExternalLoginRepository, UserExternalLoginRepository>();
        services.AddScoped<ITenantUserRepository, TenantUserRepository>();
        services.AddScoped<IUnitOfWork, EfCoreUnitOfWork>();
        services.AddScoped<ISettingMutationLock, RelationalSettingMutationLock>();
        services.AddScoped<INotificationIntentRepository, NotificationIntentRepository>();
        services.AddScoped<IPrivacyErasureStateRepository, PrivacyErasureStateRepository>();
        services.Configure<AccountAuthorityLifecycleEmailOptions>(options =>
        {
            options.Enabled = lifecycleEnabled;
            options.ProviderConfigured = providerConfigured;
        });
        services.Configure<KeycloakLifecycleEmailOptions>(options =>
        {
            options.Enabled = true;
            options.BaseUrl = baseUrl;
            options.Realm = "ISLAMU";
            options.AdminUsername = "runtime-admin";
            options.AdminPassword = fixture.Http.AdminPassword;
            options.DefaultClientId = "event-client";
            options.DefaultLifespanSeconds = 900;
        });
        services.AddHttpClient(KeycloakAccountAuthorityLifecycleEmailService.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => fixture.Http);
        fixture._provider = services.BuildIsolatedServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        fixture._scope = fixture._provider.CreateAsyncScope();
        var tenantAccessor = new TenantContextAccessor(new Microsoft.AspNetCore.Http.HttpContextAccessor());
        tenantAccessor.SetTenant(fixture.TenantId);
        fixture.Context.TenantContext = new TenantContext(tenantAccessor,
            new TenantResolverService([], tenantAccessor, Options.Create(new DeploymentSettings())));
        var user = new User
        {
            Id = fixture.UserId,
            Pii = new UserPii { Email = string.Empty, FirstName = "Native", LastName = "Account" },
            EmailVerified = false,
            CreatedAt = DateTime.UtcNow
        };
        var tenant = new Tenant
        {
            Id = fixture.TenantId,
            FullName = "Lifecycle fixture",
            Slug = $"lifecycle-{fixture.TenantId:N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        fixture.Context.Users.Add(user);
        fixture.Context.Tenants.Add(tenant);
        fixture.Context.TenantUsers.Add(new TenantUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            UserId = user.Id,
            User = user,
            StatusId = (int)TenantUserStatusEnum.Active,
            JoinedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });
        await fixture.Context.SaveChangesAsync();
        fixture.KeycloakLoginId = await fixture.AddLoginAsync(user.Id,
            PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(Issuer, Subject));
        fixture.LocalLoginId = await fixture.AddLoginAsync(user.Id,
            new ProviderAccountKey(AuthenticationProviderKind.Local, user.Id.ToString("D")));
        fixture.AtprotoLoginId = await fixture.AddLoginAsync(user.Id,
            new ProviderAccountKey(AuthenticationProviderKind.Atproto, "did:plc:ewvi7nxzyoun6zhxrhs64oiz"));
        fixture.Context.ChangeTracker.Clear();
        return fixture;
    }

    internal async Task<Guid> AddLoginAsync(Guid userId, ProviderAccountKey key)
    {
        var login = new UserExternalLogin
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            User = null!,
            AuthenticationProviderId = (int)key.ProviderKind,
            AuthenticationProvider = null!,
            ProviderKey = key.Value,
            CreatedAt = DateTime.UtcNow
        };
        Context.UserExternalLogins.Add(login);
        await Context.SaveChangesAsync();
        return login.Id;
    }

    internal AccountAuthorityLifecycleEmailRequest Request(Guid? loginId = null) => new(
        UserId: UserId, ExternalLoginId: loginId ?? KeycloakLoginId, TenantId: TenantId,
        ProposedEmail: "proposed@example.test", ClientId: "event-client",
        RedirectUri: "https://event.example.test/account", LifespanSeconds: 300,
        CorrelationId: Guid.CreateVersion7().ToString("N"));

    public async ValueTask DisposeAsync()
    {
        var connectionString = Context.Database.GetConnectionString();
        await _scope.DisposeAsync();
        await _provider.DisposeAsync();
        using var connection = new SqliteConnection(connectionString);
        SqliteConnection.ClearPool(connection);
        File.Delete(_path);
        File.Delete(_path + "-wal");
        File.Delete(_path + "-shm");
    }

    internal sealed class ProviderHttpHandler : HttpMessageHandler
    {
        internal string AdminPassword { get; } = Guid.NewGuid().ToString("N");
        internal string AdminToken { get; } = Guid.NewGuid().ToString("N");
        internal List<RecordedRequest> Requests { get; } = [];
        internal HttpStatusCode EmailStatus { get; set; } = HttpStatusCode.NoContent;
        internal bool FailTokenTransport { get; set; }
        internal bool FailEmailTransport { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, request.Headers.Authorization,
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/auth/realms/master/protocol/openid-connect/token")
            {
                if (FailTokenTransport) throw new HttpRequestException(AdminPassword);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { access_token = AdminToken }),
                        Encoding.UTF8, "application/json")
                };
            }
            if (request.Method == HttpMethod.Put && request.RequestUri!.AbsolutePath.EndsWith("/execute-actions-email", StringComparison.Ordinal))
            {
                if (FailEmailTransport) throw new HttpRequestException(AdminPassword);
                return new HttpResponseMessage(EmailStatus) { Content = new StringContent(AdminPassword) };
            }
            throw new InvalidOperationException($"Unexpected provider request {request.Method} {request.RequestUri}");
        }
    }

    internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, AuthenticationHeaderValue? Authorization, string Body);
}

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Authentication;

/// <summary>Actual Local ticket, selected Global/Runtime owner and native storage source custody.</summary>
internal sealed class StorageProbeNativeFixture : IAsyncDisposable
{
    private readonly PostgreSqlContainerFixture postgres;
    private readonly TenantBrandingNativeAuthenticationHost root;
    private readonly WebApplicationFactory<Program> host;
    private HttpClient? client;
    private AsyncServiceScope requestScope;
    private bool hasScope;
    private IPolicyMutationExecutionScope? selection;
    private IHttpContextAccessor? accessor;
    internal Guid Tenant { get; } = Guid.CreateVersion7();
    internal Guid Actor { get; private set; }
    internal Guid Binding { get; private set; }
    internal Guid Grant { get; private set; }
    internal string SetupSecret { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    internal DefaultHttpContext Request { get; private set; } = null!;
    internal ClockSource Clock { get; } = new();
    internal StorageProbeNativeRemote Remote { get; }
    internal IServiceProvider Services => selection!.Services;
    internal ExploreDbContext Owner => Services.GetRequiredService<ExploreDbContext>();
    internal HumanAuthenticationLifetime Lifetime => Services.GetRequiredService<ExploreDbContextExecutionLease>().HumanLifetime!;

    private StorageProbeNativeFixture(PostgreSqlContainerFixture postgres)
    {
        this.postgres = postgres;
        Remote = new(postgres);
        root = new(postgres, Tenant, []);
        host = root.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["SETUP_SECRET"] = SetupSecret, ["SETUP_SECRET_REQUIRED"] = "true" }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
                Remote.Register(services);
            });
        });
    }

    internal static async Task<StorageProbeNativeFixture> CreateAsync(PostgreSqlContainerFixture postgres, string branch)
    {
        var fixture = new StorageProbeNativeFixture(postgres);
        try { await fixture.InitializeAsync(branch); return fixture; }
        catch { await fixture.DisposeAsync(); throw; }
    }

    private async Task InitializeAsync(string branch)
    {
        string slug = $"probe-{Tenant:N}";
        await using (var migration = postgres.CreateDbContext())
        {
            await PostgresStorageProbeSourceContract.ApplyAsync(migration, postgres.NativeQueueEnrollment);
            await Explore.Persistence.Schema.PostgresExternalLoginReadPermissions.ApplyAsync(
                migration, postgres.NativeQueueEnrollment);
            migration.Tenants.Add(new()
            {
                Id = Tenant,
                Slug = slug,
                FullName = "Storage probe",
                TenantStatus = null!,
                TenantStatusId = (int)TenantStatusEnum.Active,
                CreatedAt = Clock.GetUtcNow().UtcDateTime
            });
            await migration.SaveChangesAsync();
        }
        client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        await using (var privacyScope = host.Services.CreateAsyncScope())
        {
            await privacyScope.ServiceProvider.GetRequiredService<EmbeddedPrivacyErasureAuthorityStorage>().EnsureReadyAsync();
            await using var privacy = await privacyScope.ServiceProvider
                .GetRequiredService<IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>>().CreateDbContextAsync();
            await privacy.Database.MigrateAsync();
        }
        var credentials = await ProvisionAsync();
        client.DefaultRequestHeaders.Add("X-Tenant-Slug", slug);
        using var response = await client.PostAsJsonAsync("/api/auth/local/login", credentials);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(body.RootElement.GetProperty("success").GetBoolean()).IsTrue();
        // The ticket is genuinely signed by ordinary login. Setup admission subsequently
        // requires the real accepted header and exact pending/absent bootstrap state.
        await SeedSourcesAsync(branch);
        requestScope = host.Services.CreateAsyncScope();
        hasScope = true;
        Request = new() { RequestServices = requestScope.ServiceProvider };
        Request.Request.Method = HttpMethods.Post;
        Request.Request.Path = branch.StartsWith("tenant", StringComparison.Ordinal)
            ? "/api/tenant/settings/storage/test" : "/api/instance/settings/storage/test";
        Request.Request.Headers.Authorization = "Bearer " + body.RootElement.GetProperty("token").GetString()!;
        if (branch.StartsWith("setup", StringComparison.Ordinal)) Request.Request.Headers["X-Setup-Secret"] = SetupSecret;
        requestScope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(Tenant);
        accessor = requestScope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        await InRequestAsync(async () =>
        {
            var authentication = await Request.AuthenticateAsync();
            await Assert.That(authentication.Succeeded).IsTrue();
            Request.User = authentication.Principal!;
            selection = await requestScope.ServiceProvider.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
                .CreateAsync(branch.StartsWith("tenant", StringComparison.Ordinal) ? new(Tenant) : PolicyMutationScope.Instance, default);
            Remote.Owner = Owner;
            await Assert.That(Owner.Database.IsNpgsql()).IsTrue();
            await Assert.That(Owner.TenantFilterTenantId).IsEqualTo(
                branch.StartsWith("tenant", StringComparison.Ordinal) ? (Guid?)Tenant : null);
            await Assert.That(Services.GetRequiredService<IHumanCommitScopePreparation>().Subject!.PrincipalId).IsEqualTo(Actor);
            await Assert.That(Lifetime.LocalSession).IsNotNull();
        });
    }

    private async Task<LocalAuthRequestDto> ProvisionAsync()
    {
        await using var scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(Tenant);
        var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var credentials = scope.ServiceProvider.GetRequiredService<ILocalCredentialAdministration>();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
        DateTime now = Clock.GetUtcNow().UtcDateTime;
        var initiator = new User
        {
            Id = Guid.CreateVersion7(),
            CreatedAt = now,
            Pii = new UserPii { Email = string.Empty, FirstName = "Credential", LastName = "Provisioner" }
        };
        database.Users.Add(initiator);
        await database.InstanceBootstrapStates.ExecuteDeleteAsync();
        var bootstrap = InstanceBootstrapState.CreateInteractivePending(Guid.CreateVersion7(), DeploymentMode.SingleTenant, now);
        bootstrap.CompleteInteractive(initiator.Id, now);
        database.InstanceBootstrapStates.Add(bootstrap);
        await database.SaveChangesAsync();
        string email = $"probe-{Guid.CreateVersion7():N}@example.test";
        string password = $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}";
        var created = await credentials.CreatePendingAsync(new LocalCredentialCreateRequest(
            Guid.CreateVersion7(), initiator.Id, email, "Storage", "Probe"), default);
        await Assert.That(created.Outcome).IsEqualTo(LocalCredentialCreateOutcome.Created);
        var receipt = created.Receipt!;
        Actor = receipt.LocalSubjectId;
        Binding = receipt.ExternalLoginId;
        Grant = Guid.CreateVersion7();
        var user = new User
        {
            Id = Actor,
            EmailVerified = true,
            CreatedAt = now,
            Pii = new UserPii { Email = email, FirstName = "Storage", LastName = "Probe" }
        };
        database.AddRange(user,
            new Actor
            {
                Id = receipt.PersonalActorId,
                UserId = Actor,
                User = user,
                CreatedAt = now,
                ActorTypeId = (int)ActorTypeEnum.User,
                ActorType = null!,
                Pii = new ActorPii { DisplayName = "Storage Probe" }
            },
            new UserExternalLogin
            {
                Id = Binding,
                UserId = Actor,
                User = user,
                AuthenticationProviderId = (int)AuthenticationProviderKind.Local,
                AuthenticationProvider = null!,
                ProviderKey = Actor.ToString("D"),
                CreatedAt = now
            });
        await database.SaveChangesAsync();
        var pending = (await credentials.ReadProvisioningAsync(receipt.OperationId, default))!;
        await Assert.That(await credentials.ActivateChangeRequiredAsync(
            new(receipt.OperationId, pending.OperationConcurrencyStamp), default)).IsEqualTo(LocalCredentialActivationOutcome.Activated);
        var local = (await manager.FindByIdAsync(Actor.ToString("D")))!;
        await database.Entry(local).ReloadAsync();
        DateTimeOffset issued = Clock.GetUtcNow();
        await Assert.That(await credentials.ReplaceAsync(new LocalCredentialReplacementRequest(
            new LocalCredentialReplacementAuthority(new(Actor, receipt.OperationId, local.SecurityStamp!), issued, issued.AddMinutes(5)),
            password), default)).IsEqualTo(LocalCredentialReplacementOutcome.Replaced);
        await database.Entry(local).ReloadAsync();
        local.EmailConfirmed = true;
        await Assert.That((await manager.UpdateAsync(local)).Succeeded).IsTrue();
        return new(email, password);
    }

    private async Task SeedSourcesAsync(string branch)
    {
        await using var seed = postgres.CreateDbContext();
        if (branch.StartsWith("setup", StringComparison.Ordinal))
        {
            await seed.InstanceBootstrapStates.ExecuteDeleteAsync();
            if (branch != "setup-absent")
                seed.InstanceBootstrapStates.Add(InstanceBootstrapState.CreateInteractivePending(
                    Guid.CreateVersion7(), DeploymentMode.SingleTenant, Clock.GetUtcNow().UtcDateTime));
        }
        else if (branch == "tenant-administrator")
        {
            var member = new TenantUser
            {
                Id = Guid.CreateVersion7(),
                TenantId = Tenant,
                Tenant = null!,
                UserId = Actor,
                User = null!,
                StatusId = (int)TenantUserStatusEnum.Active,
                JoinedAt = Clock.GetUtcNow().UtcDateTime
            };
            seed.TenantUsers.Add(member);
            seed.TenantUserRoleGrants.Add(new()
            {
                Id = Grant,
                TenantId = Tenant,
                Tenant = null!,
                TenantUserId = member.Id,
                TenantUser = null!,
                RoleId = (int)RoleEnum.TenantAdmin,
                Role = null!,
                RoleScopeId = (int)RoleScopeEnum.Tenant,
                GrantedAt = Clock.GetUtcNow().UtcDateTime
            });
        }
        else seed.PlatformUserRoles.Add(new()
        {
            Id = Grant,
            UserId = Actor,
            User = null!,
            RoleId = (int)RoleEnum.Admin,
            Role = null!,
            GrantedAt = Clock.GetUtcNow().UtcDateTime
        });
        foreach (var (key, value) in new Dictionary<string, string>
        {
            [GovernanceSettingKeys.Storage.Provider] = "\"s3_compatible\"",
            [GovernanceSettingKeys.Storage.Endpoint] = "\"https://probe.example.test\"",
            [GovernanceSettingKeys.Storage.BucketName] = "\"probe-bucket\"",
            [GovernanceSettingKeys.Storage.Region] = "\"us-east-1\"",
            [GovernanceSettingKeys.Storage.RouteMatrix] = "{\"Version\":1,\"Routes\":[]}"
        })
        {
            var row = await seed.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
            if (row is null) seed.SystemSettings.Add(new() { SettingKey = key, Value = value });
            else { row.Value = value; row.IsLocked = false; }
        }
        await seed.SecretBindings.Where(row => row.SettingKey == Explore.Domain.Secrets.SecretDefinitionRegistry.Keys.Storage.AccessKeyId
            || row.SettingKey == Explore.Domain.Secrets.SecretDefinitionRegistry.Keys.Storage.SecretAccessKey).ExecuteDeleteAsync();
        await seed.SaveChangesAsync();
    }

    internal async Task InRequestAsync(Func<Task> action)
    {
        var previous = accessor!.HttpContext;
        accessor.HttpContext = Request;
        try { await action(); }
        finally { accessor.HttpContext = previous; }
    }

    public async ValueTask DisposeAsync()
    {
        if (selection is not null) await selection.DisposeAsync();
        if (hasScope) await requestScope.DisposeAsync();
        client?.Dispose();
        await host.DisposeAsync();
        await root.DisposeAsync();
        root.PrivacyKeys.Dispose();
    }

    internal sealed class ClockSource : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        internal void Set(DateTimeOffset value) => now = value;
    }
}

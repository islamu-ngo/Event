using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Authentication;

/// <summary>
/// Real signed Local authentication and production Global selection. Only external secret
/// authorities and the root clock are substituted by the existing authentication host.
/// </summary>
internal sealed class ResolverConfigurationNativeFixture : IAsyncDisposable
{
    private readonly PostgreSqlContainerFixture postgres;
    private readonly TenantBrandingNativeAuthenticationHost root;
    private readonly WebApplicationFactory<Program> host;
    private HttpClient? client;
    private AsyncServiceScope requestScope;
    private bool hasScope;
    private readonly bool mixed;
    private readonly bool tenantScope;
    private IPolicyMutationExecutionScope? selection;
    private IHttpContextAccessor? accessor;
    internal DefaultHttpContext Request { get; private set; } = null!;
    internal ClockSource Clock { get; } = new();
    internal Guid Actor { get; private set; }
    internal Guid Binding { get; private set; }
    internal Guid Grant { get; private set; }
    internal Guid Tenant { get; }
    internal IServiceProvider Services => selection!.Services;
    internal ExploreDbContext Owner => Services.GetRequiredService<ExploreDbContext>();
    internal Probe Writer => (Probe)Services.GetRequiredService<IResolverConfigurationSettingsWriter>();
    internal HumanAuthenticationLifetime Lifetime => Services.GetRequiredService<ExploreDbContextExecutionLease>().HumanLifetime!;

    private ResolverConfigurationNativeFixture(PostgreSqlContainerFixture postgres, Guid tenant,
        bool mixed, bool tenantScope)
    {
        this.postgres = postgres;
        this.mixed = mixed;
        this.tenantScope = tenantScope;
        Tenant = tenant;
        root = new(postgres, tenant, [], Clock);
        host = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddScoped<IResolverConfigurationMutationAuthority, ResolverConfigurationMutationAuthority>();
            services.AddScoped<ResolverConfigurationSettingsWriter>();
            services.AddScoped<IResolverConfigurationSettingsWriter, Probe>();
        }));
    }

    internal static async Task<ResolverConfigurationNativeFixture> CreateAsync(PostgreSqlContainerFixture postgres,
        bool mixed = false, bool tenantScope = false)
    {
        Guid tenant = Guid.CreateVersion7();
        var fixture = new ResolverConfigurationNativeFixture(postgres, tenant, mixed, tenantScope);
        try { await fixture.InitializeAsync(tenant); return fixture; }
        catch { await fixture.DisposeAsync(); throw; }
    }

    private async Task InitializeAsync(Guid tenant)
    {
        string slug = $"resolver-{tenant:N}";
        await using (var migration = postgres.CreateDbContext())
        {
            await PostgresResolverConfigurationContract.ApplyAsync(migration, postgres.NativeQueueEnrollment);
            await Explore.Persistence.Schema.PostgresExternalLoginReadPermissions.ApplyAsync(
                migration, postgres.NativeQueueEnrollment);
            migration.Tenants.Add(new()
            {
                Id = tenant,
                Slug = slug,
                FullName = "Resolver native test",
                TenantStatus = null!,
                TenantStatusId = (int)TenantStatusEnum.Active,
                CreatedAt = Clock.GetUtcNow().UtcDateTime
            });
            await migration.SaveChangesAsync();
        }
        client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        await root.EnsurePrivacyAuthorityReadyAsync();
        var credentials = await ProvisionAsync(tenant);
        client.DefaultRequestHeaders.Add("X-Tenant-Slug", slug);
        using var response = await client.PostAsJsonAsync("/api/auth/local/login", credentials);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(body.RootElement.GetProperty("success").GetBoolean()).IsTrue();
        root.UseRuntimeRequestContexts();
        requestScope = host.Services.CreateAsyncScope();
        hasScope = true;
        Request = new() { RequestServices = requestScope.ServiceProvider };
        Request.Request.Method = HttpMethods.Patch;
        Request.Request.Path = "/api/instance/settings/resolver-config";
        Request.Request.Headers.Authorization = "Bearer " + body.RootElement.GetProperty("token").GetString()!;
        requestScope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(tenant);
        accessor = requestScope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        await InRequestAsync(async () =>
        {
            var authentication = await Request.AuthenticateAsync();
            await Assert.That(authentication.Failure).IsNull();
            await Assert.That(authentication.Succeeded).IsTrue();
            Request.User = authentication.Principal!;
            selection = await requestScope.ServiceProvider.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
                .CreateAsync(mixed
                    ? tenantScope ? PolicyMutationScope.MixedSettingsTenant(tenant) : PolicyMutationScope.MixedSettingsInstance
                    : PolicyMutationScope.Instance, default);
            await Assert.That(Owner.Database.IsNpgsql()).IsTrue();
            await Assert.That(Owner.TenantFilterTenantId).IsEqualTo(tenantScope ? tenant : (Guid?)null);
            await Assert.That(Services.GetRequiredService<IHumanCommitScopePreparation>().Subject!.PrincipalId).IsEqualTo(Actor);
            await Assert.That(Lifetime.LocalSession).IsNotNull();
        });
    }

    private async Task<LocalAuthRequestDto> ProvisionAsync(Guid tenant)
    {
        await using var scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(tenant);
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
        if (!await database.InstanceBootstrapStates.AnyAsync())
        {
            var bootstrap = InstanceBootstrapState.CreateInteractivePending(Guid.CreateVersion7(), DeploymentMode.SingleTenant, now);
            bootstrap.CompleteInteractive(initiator.Id, now);
            database.InstanceBootstrapStates.Add(bootstrap);
        }
        await database.SaveChangesAsync();
        string email = $"resolver-{Guid.CreateVersion7():N}@example.test";
        string password = $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}";
        var created = await credentials.CreatePendingAsync(new LocalCredentialCreateRequest(
            Guid.CreateVersion7(), initiator.Id, email, "Resolver", "Administrator"), default);
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
            Pii = new UserPii { Email = email, FirstName = "Resolver", LastName = "Administrator" }
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
                Pii = new ActorPii { DisplayName = "Resolver Administrator" }
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
        if (tenantScope)
        {
            var member = new TenantUser
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenant,
                Tenant = null!,
                UserId = Actor,
                User = user,
                StatusId = (int)TenantUserStatusEnum.Active,
                JoinedAt = now
            };
            database.AddRange(member, new TenantUserRoleGrant
            {
                Id = Grant,
                TenantId = tenant,
                Tenant = null!,
                TenantUserId = member.Id,
                TenantUser = member,
                RoleId = (int)RoleEnum.TenantAdmin,
                Role = null!,
                RoleScopeId = (int)RoleScopeEnum.Tenant,
                GrantedAt = now
            });
        }
        else
            database.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Grant,
                UserId = Actor,
                User = user,
                RoleId = (int)RoleEnum.Admin,
                Role = null!,
                GrantedAt = now
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
        private DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()).AddTicks(1234567);
        public override DateTimeOffset GetUtcNow() => now;
        internal void Set(DateTimeOffset instant) => now = instant;
    }

    internal sealed class Probe(ResolverConfigurationSettingsWriter writer) : IResolverConfigurationSettingsWriter
    {
        internal ResolverConfigurationSettingsWriter Inner => writer;
        internal ResolverConfigurationMutation? Mutation { get; private set; }
        internal Func<CancellationToken, Task>? AfterObservation { get; set; }
        internal Func<ResolverConfigurationMutation, CancellationToken, Task>? AfterWrite { get; set; }
        public DateTime NormalizeEffectTimestamp(DateTime timestamp) => writer.NormalizeEffectTimestamp(timestamp);
        public async Task<ImmutableDictionary<string, ResolverConfigurationSettingSnapshot?>> ObserveAsync(
            IResolverConfigurationMutationSource source, IUnitOfWork owner, CancellationToken token)
        {
            var rows = await writer.ObserveAsync(source, owner, token);
            if (AfterObservation is not null) await AfterObservation(token);
            return rows;
        }
        public async Task ExecuteAsync(ResolverConfigurationMutation mutation, CancellationToken token)
        {
            Mutation = mutation;
            await writer.ExecuteAsync(mutation, token);
            if (AfterWrite is not null) await AfterWrite(mutation, token);
        }
        public Task RequireAppliedAsync(ResolverConfigurationMutation mutation, CancellationToken token) =>
            writer.RequireAppliedAsync(mutation, token);
    }
}

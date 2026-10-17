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
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Constants;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Privacy.ErasureAuthority;
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

/// <summary>Real signed Local request, existing enrolled Global pool and controlled root clock.</summary>
internal sealed class OperatorIdentityEditorNativeFixture : IAsyncDisposable
{
    private readonly TenantBrandingNativeAuthenticationHost root;
    private readonly WebApplicationFactory<Program> host;
    private HttpClient? client;
    private AsyncServiceScope requestScope;
    private bool hasRequest;
    private IPolicyMutationExecutionScope? selection;
    private IHttpContextAccessor? accessor;
    private DefaultHttpContext request = null!;
    private readonly PostgreSqlContainerFixture postgres;
    internal EffectClock Clock { get; } = new();
    internal Guid Actor { get; private set; }
    internal Guid Binding { get; private set; }
    internal Guid Grant { get; private set; }
    internal IServiceProvider Services => selection!.Services;
    internal ExploreDbContext Owner => Services.GetRequiredService<ExploreDbContext>();
    internal Probe Writer => (Probe)Services.GetRequiredService<IOperatorIdentitySettingsWriter>();
    internal HumanAuthenticationLifetime Lifetime => Services.GetRequiredService<ExploreDbContextExecutionLease>().HumanLifetime!;

    private OperatorIdentityEditorNativeFixture(PostgreSqlContainerFixture postgres, Guid tenant)
    {
        this.postgres = postgres;
        root = new(postgres, tenant, []);
        host = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddScoped<IOperatorIdentityMutationAuthority, OperatorIdentityMutationAuthority>();
            services.AddScoped<OperatorIdentitySettingsWriter>();
            services.AddScoped<IOperatorIdentitySettingsWriter, Probe>();
        }));
    }

    internal static async Task<OperatorIdentityEditorNativeFixture> CreateAsync(PostgreSqlContainerFixture postgres)
    {
        Guid tenant = Guid.CreateVersion7();
        var fixture = new OperatorIdentityEditorNativeFixture(postgres, tenant);
        try { await fixture.InitializeAsync(tenant); return fixture; }
        catch { await fixture.DisposeAsync(); throw; }
    }

    private async Task InitializeAsync(Guid tenant)
    {
        string slug = $"operator-{tenant:N}";
        await using (var migration = postgres.CreateDbContext())
        {
            await PostgresOperatorIdentityEditorContract.ApplyAsync(migration, postgres.NativeQueueEnrollment);
            // The original lookup executor resolves the signed Local subject before Global
            // editor selection. No direct identity-table access is granted to that login.
            var names = new HumanPrincipalSourceSql(migration);
            await migration.Database.ExecuteSqlRawAsync(
                $"GRANT {PostgresExternalLoginAuthenticationContract.GlobalRole} TO {names.Quote(postgres.NativeQueueEnrollment.GlobalLogin)} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE");
            migration.Tenants.Add(new Tenant
            {
                Id = tenant,
                Slug = slug,
                FullName = "Operator native test",
                TenantStatus = null!,
                TenantStatusId = (int)TenantStatusEnum.Active,
                CreatedAt = Clock.GetUtcNow().UtcDateTime
            });
            var route = await migration.SystemSettings.SingleOrDefaultAsync(row =>
                row.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider);
            if (route is null)
                migration.SystemSettings.Add(new SystemSetting
                {
                    SettingKey = GovernanceSettingKeys.Security.AuthorizationProvider,
                    Value = "\"local\"",
                    ValueType = SettingValueType.String
                });
            else route.Value = "\"local\"";
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
        LocalAuthRequestDto login = await ProvisionAsync(tenant);
        client.DefaultRequestHeaders.Add("X-Tenant-Slug", slug);
        using var response = await client.PostAsJsonAsync("/api/auth/local/login", login);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(body.RootElement.GetProperty("success").GetBoolean()).IsTrue();
        requestScope = host.Services.CreateAsyncScope();
        hasRequest = true;
        request = new DefaultHttpContext { RequestServices = requestScope.ServiceProvider };
        request.Request.Method = HttpMethods.Put;
        request.Request.Path = "/api/instance-operator-identity";
        request.Request.Headers.Authorization = "Bearer " + body.RootElement.GetProperty("token").GetString()!;
        requestScope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(tenant);
        accessor = requestScope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        await InRequestAsync(async () =>
        {
            var authentication = await request.AuthenticateAsync();
            await Assert.That(authentication.Succeeded).IsTrue();
            request.User = authentication.Principal!;
            selection = await requestScope.ServiceProvider.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
                .CreateAsync(PolicyMutationScope.Instance, default);
            await Assert.That(Owner.Database.IsNpgsql()).IsTrue();
            await Assert.That(Owner.TenantFilterTenantId).IsNull();
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
            Pii = new UserPii { Email = string.Empty, FirstName = "Credential", LastName = "Provisioner" },
            CreatedAt = now
        };
        database.Users.Add(initiator);
        if (!await database.InstanceBootstrapStates.AnyAsync())
        {
            var bootstrap = InstanceBootstrapState.CreateInteractivePending(Guid.CreateVersion7(), DeploymentMode.SingleTenant, now);
            bootstrap.CompleteInteractive(initiator.Id, now);
            database.InstanceBootstrapStates.Add(bootstrap);
        }
        await database.SaveChangesAsync();
        string email = $"operator-{Guid.CreateVersion7():N}@example.test";
        string password = $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}";
        var created = await credentials.CreatePendingAsync(new LocalCredentialCreateRequest(
            Guid.CreateVersion7(), initiator.Id, email, "Operator", "Administrator"), default);
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
            Pii = new UserPii { Email = email, FirstName = "Operator", LastName = "Administrator" }
        };
        database.AddRange(user,
            new Actor
            {
                Id = receipt.PersonalActorId,
                UserId = Actor,
                User = user,
                ActorTypeId = (int)ActorTypeEnum.User,
                ActorType = null!,
                Pii = new ActorPii { DisplayName = "Operator Administrator" },
                CreatedAt = now
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
            },
            new PlatformUserRole { Id = Grant, UserId = Actor, User = user, RoleId = (int)RoleEnum.Admin, Role = null!, GrantedAt = now });
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
        await Assert.That((await credentials.ReadLinkedIdentityAsync(Actor, default))!.CredentialState).IsEqualTo(LocalCredentialState.Ready);
        return new(email, password);
    }

    internal async Task InRequestAsync(Func<Task> action)
    {
        var previous = accessor!.HttpContext;
        accessor.HttpContext = request;
        try { await action(); }
        finally { accessor.HttpContext = previous; }
    }

    public async ValueTask DisposeAsync()
    {
        if (selection is not null) await selection.DisposeAsync();
        if (hasRequest) await requestScope.DisposeAsync();
        client?.Dispose();
        await host.DisposeAsync();
        await root.DisposeAsync();
        root.PrivacyKeys.Dispose();
    }

    internal sealed class EffectClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()).AddTicks(1234567);
        public override DateTimeOffset GetUtcNow() => now;
        internal void Set(DateTimeOffset instant) => now = instant;
    }

    internal sealed class Probe(OperatorIdentitySettingsWriter writer) : IOperatorIdentitySettingsWriter
    {
        internal OperatorIdentityMutation? Mutation { get; private set; }
        internal Func<CancellationToken, Task>? AfterObservation { get; set; }
        internal Func<OperatorIdentityMutation, CancellationToken, Task>? AfterWrite { get; set; }
        internal OperatorIdentitySettingsWriter Inner => writer;
        public DateTime NormalizeEffectTimestamp(DateTime instant) => writer.NormalizeEffectTimestamp(instant);
        public Task<string?> NormalizeAuditPayloadAsync(OperatorIdentityImportAudit? audit, CancellationToken token) =>
            writer.NormalizeAuditPayloadAsync(audit, token);
        public async Task<OperatorIdentitySettingSnapshot?> ObserveAsync(IOperatorIdentityMutationSource source, IUnitOfWork owner, CancellationToken token)
        {
            var result = await writer.ObserveAsync(source, owner, token);
            if (AfterObservation is not null) await AfterObservation(token);
            return result;
        }
        public async Task ExecuteAsync(OperatorIdentityMutation mutation, CancellationToken token)
        {
            Mutation = mutation;
            await writer.ExecuteAsync(mutation, token);
            if (AfterWrite is not null) await AfterWrite(mutation, token);
        }
    }
}

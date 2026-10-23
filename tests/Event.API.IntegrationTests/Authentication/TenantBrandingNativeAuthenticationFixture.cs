using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Documents;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

/// <summary>
/// A fresh Local account is provisioned and authenticated in PostgreSQL, not copied from
/// another identity store. Production selection retains the HTTP subject/lifetime on the
/// enrolled Runtime context used by every branding source port and finalizer.
/// </summary>
internal sealed class TenantBrandingNativeAuthenticationFixture : IAsyncDisposable
{
    private readonly TenantBrandingNativeAuthenticationHost host;
    private readonly HttpClient client;
    private readonly AsyncServiceScope requestScope;
    private readonly DefaultHttpContext request;
    private readonly IHttpContextAccessor accessor;
    private readonly PostgreSqlContainerFixture postgres;
    private readonly bool actorReadsOnly;
    private IPolicyMutationExecutionScope? selected;

    private TenantBrandingNativeAuthenticationFixture(
        TenantBrandingNativeAuthenticationHost host, PostgreSqlContainerFixture postgres, bool actorReadsOnly)
    {
        this.host = host;
        this.postgres = postgres;
        this.actorReadsOnly = actorReadsOnly;
        client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        requestScope = host.Services.CreateAsyncScope();
        request = new DefaultHttpContext { RequestServices = requestScope.ServiceProvider };
        accessor = requestScope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
    }

    internal HumanCommitSubject Subject { get; private set; } = null!;
    internal HumanAuthenticationLifetime Lifetime { get; private set; } = null!;
    internal IServiceProvider Services => selected?.Services ?? requestScope.ServiceProvider;
    internal ExploreDbContext Owner => Services.GetRequiredService<ExploreDbContext>();
    internal ITenantContext TenantContext => Services.GetRequiredService<ITenantContext>();
    internal IHumanCommitScopePreparation HumanScope => Services.GetRequiredService<IHumanCommitScopePreparation>();
    internal IHumanTenantBrandingMutationPreparation Preparation => Services.GetRequiredService<IHumanTenantBrandingMutationPreparation>();
    internal IHumanActionCommitFinalizer Finalizer => Services.GetRequiredService<IHumanActionCommitFinalizer>();
    internal ITenantAdministratorCommitAuthority Authority => Services.GetRequiredService<ITenantAdministratorCommitAuthority>();
    internal Guid TenantGrantId { get; private set; }
    internal Guid PlatformGrantId { get; private set; }

    internal static async Task<TenantBrandingNativeAuthenticationFixture> CreateAsync(
        PostgreSqlContainerFixture postgres, Guid tenantId, params IInterceptor[] ownerInterceptors)
        => await CreateAsync(postgres, tenantId, TimeProvider.System, ownerInterceptors);

    internal static async Task<TenantBrandingNativeAuthenticationFixture> CreateAsync(
        PostgreSqlContainerFixture postgres, Guid tenantId, TimeProvider clock,
        params IInterceptor[] ownerInterceptors)
        => await CreateAsync(postgres, tenantId, clock, null, ownerInterceptors);

    internal static async Task<TenantBrandingNativeAuthenticationFixture> CreateAsync(
        PostgreSqlContainerFixture postgres, Guid tenantId, TimeProvider clock,
        Action<IServiceCollection>? configureServices, params IInterceptor[] ownerInterceptors)
        => await CreateCoreAsync(postgres, tenantId, clock, configureServices, false, ownerInterceptors);

    internal static Task<TenantBrandingNativeAuthenticationFixture> CreateForActorReadsAsync(
        PostgreSqlContainerFixture postgres, Guid tenantId, TimeProvider? clock = null,
        Action<IServiceCollection>? configureServices = null)
        => CreateCoreAsync(postgres, tenantId, clock ?? TimeProvider.System, configureServices, true, []);

    internal static Task<TenantBrandingNativeAuthenticationFixture> CreateForRuntimeReadsAsync(
        PostgreSqlContainerFixture postgres, Guid tenantId, TimeProvider clock,
        Action<IServiceCollection>? configureServices = null)
        => CreateCoreAsync(postgres, tenantId, clock, configureServices, true, [], originalRuntime: true);

    private static async Task<TenantBrandingNativeAuthenticationFixture> CreateCoreAsync(
        PostgreSqlContainerFixture postgres, Guid tenantId, TimeProvider clock,
        Action<IServiceCollection>? configureServices, bool actorReadsOnly, IInterceptor[] ownerInterceptors,
        bool originalRuntime = false)
    {
        var host = new TenantBrandingNativeAuthenticationHost(
            postgres, tenantId, ownerInterceptors, clock, configureServices);
        TenantBrandingNativeAuthenticationFixture? fixture = null;
        try
        {
            fixture = new TenantBrandingNativeAuthenticationFixture(host, postgres, actorReadsOnly);
            await fixture.InitializeAsync(tenantId, originalRuntime);
            return fixture;
        }
        catch
        {
            if (fixture is not null) await fixture.DisposeAsync();
            else
            {
                await host.DisposeAsync();
                host.PrivacyKeys.Dispose();
            }
            throw;
        }
    }

    private async Task InitializeAsync(Guid tenantId, bool originalRuntime)
    {
        await EnrollBrandingAccessAsync();
        await host.EnsurePrivacyAuthorityReadyAsync();
        var (login, binding) = await ProvisionLocalAccountAsync(tenantId);
        await SeedTenantGrantsAsync(tenantId, binding.LocalSubjectId);
        await using (var setup = postgres.CreateDbContext())
        {
            string slug = await setup.Tenants.Where(row => row.Id == tenantId).Select(row => row.Slug).SingleAsync();
            client.DefaultRequestHeaders.Add("X-Tenant-Slug", slug);
        }
        using var response = await client.PostAsJsonAsync("/api/auth/local/login", login);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK)
            .Because(host.RequestFailure?.ToString() ?? await response.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(body.RootElement.GetProperty("success").GetBoolean()).IsTrue();
        host.UseRuntimeRequestContexts();
        request.Request.Headers.Authorization = "Bearer " + body.RootElement.GetProperty("token").GetString()!;
        requestScope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(tenantId);
        await InOriginalRequestAsync(async () =>
        {
            var authentication = await request.AuthenticateAsync();
            await Assert.That(authentication.Succeeded).IsTrue();
            request.User = authentication.Principal!;
            Lifetime = await requestScope.ServiceProvider.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
                .ObserveAsync(request.User, CancellationToken.None)
                ?? throw new InvalidOperationException("The validated Local request must retain credential custody.");
            if (!originalRuntime)
                selected = await requestScope.ServiceProvider.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
                    .CreateAsync(new PolicyMutationScope(tenantId), CancellationToken.None);
            await HumanScope.PrepareAsync(CancellationToken.None);
            Subject = HumanScope.Subject
                ?? throw new InvalidOperationException("Native selection must retain the canonically resolved Local account.");
            var ownerLease = Services.GetRequiredService<ExploreDbContextExecutionLease>();
            ownerLease.RequireOriginalOwner(Owner);
            await Assert.That(ownerLease.Context).IsSameReferenceAs(Owner);
            await Assert.That(ownerLease.HumanScope!.Subject).IsSameReferenceAs(Subject);
            await Assert.That(ownerLease.HumanLifetime).IsSameReferenceAs(Lifetime);
            await Assert.That(Subject.PrincipalId).IsEqualTo(binding.LocalSubjectId);
            await Assert.That(Subject.AccountKey).IsEqualTo(Lifetime.AccountKey);
            await Assert.That(Lifetime.LocalSession).IsNotNull();
            await Assert.That(Lifetime.IsValidAt(Services.GetRequiredService<TimeProvider>().GetUtcNow())).IsTrue();
            await Assert.That(Services.GetRequiredService<ICurrentUserService>().UserId).IsEqualTo((Guid?)Subject.PrincipalId);
            await Assert.That(Owner.Database.IsNpgsql()).IsTrue();
            await Assert.That(Owner.IsTenantFilterBypassed).IsFalse();
            await Assert.That(Owner.BindTenantLease()).IsEqualTo((Guid?)tenantId);
            await Assert.That(Owner.Database.CurrentTransaction).IsNull();
            // Read through the real enrolled source contract: no direct Runtime identity-table grant.
            var principal = await Services.GetRequiredService<IHumanPrincipalSourceFence>().ObserveAsync();
            await Assert.That(principal.PrincipalId).IsEqualTo(Subject.PrincipalId);
            await Assert.That(principal.LocalSession).IsNotNull();
            await Assert.That(principal.LocalSession!.Accounts.Count()).IsEqualTo(1);
            return true;
        });
    }

    private async Task EnrollBrandingAccessAsync()
    {
        await using var setup = postgres.CreateDbContext();
        // Global reads only the binding columns; Runtime retains no identity-table access.
        await setup.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            await Explore.Persistence.Schema.PostgresExternalLoginReadPermissions.ApplyAsync(setup, postgres.NativeQueueEnrollment));
        var sql = setup.GetService<ISqlGenerationHelper>();
        string global = sql.DelimitIdentifier(postgres.NativeQueueEnrollment.GlobalLogin);
        string runtime = sql.DelimitIdentifier(postgres.NativeQueueEnrollment.RuntimeLogin);
        string Table(Type type)
        {
            var entity = setup.Model.FindEntityType(type)!;
            return sql.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
        }
        // Ordinary branding data access is distinct from execute-only human authority fences.
        // The tenant document remains under the existing FORCE RLS policy.
        if (!actorReadsOnly)
        {
            await setup.Database.ExecuteSqlRawAsync($"GRANT SELECT ON {Table(typeof(SystemSetting))} TO {runtime}");
            await setup.Database.ExecuteSqlRawAsync(
                $"GRANT SELECT, UPDATE ON {Table(typeof(TenantSettingsDocument))} TO {runtime}");
        }

    }

    private async Task SeedTenantGrantsAsync(Guid tenantId, Guid userId)
    {
        await using var setup = postgres.CreateDbContext();
        DateTime now = DateTime.UtcNow;
        var member = new TenantUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            UserId = userId,
            User = null!,
            StatusId = (int)TenantUserStatusEnum.Active,
            JoinedAt = now,
            CreatedAt = now
        };
        var grant = new TenantUserRoleGrant
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            TenantUserId = member.Id,
            TenantUser = member,
            RoleId = (int)RoleEnum.TenantAdmin,
            Role = null!,
            RoleScopeId = (int)RoleScopeEnum.Tenant,
            GrantedAt = now,
            CreatedAt = now
        };
        var platform = new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            User = null!,
            RoleId = (int)RoleEnum.Admin,
            Role = null!,
            GrantedAt = now
        };
        setup.AddRange(member, grant, platform);
        var route = await setup.SystemSettings.SingleOrDefaultAsync(row =>
            row.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider);
        if (route is null)
            setup.SystemSettings.Add(new SystemSetting
            {
                SettingKey = GovernanceSettingKeys.Security.AuthorizationProvider,
                Value = "\"local\"",
                ValueType = SettingValueType.String
            });
        else route.Value = "\"local\"";
        await setup.SaveChangesAsync();
        TenantGrantId = grant.Id;
        PlatformGrantId = platform.Id;
    }

    private async Task<(LocalAuthRequestDto Login, LocalIdentityBinding Binding)> ProvisionLocalAccountAsync(Guid tenantId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(tenantId);
        await using var setup = postgres.CreateDbContext();
        setup.TenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        setup.ClearTenantFilterBypass();
        setup.BindTenantLease();
        scope.ServiceProvider.GetRequiredService<ExploreDbContextExecutionLease>().Bind(setup, tenantId);
        var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var credentials = scope.ServiceProvider.GetRequiredService<ILocalCredentialAdministration>();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
        var initiator = new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii { Email = string.Empty, FirstName = "Credential", LastName = "Provisioner" },
            CreatedAt = DateTime.UtcNow
        };
        database.Users.Add(initiator);
        if (!await database.InstanceBootstrapStates.AnyAsync())
        {
            var bootstrap = InstanceBootstrapState.CreateInteractivePending(
                Guid.CreateVersion7(), DeploymentMode.SingleTenant, DateTime.UtcNow);
            bootstrap.CompleteInteractive(initiator.Id, DateTime.UtcNow);
            database.InstanceBootstrapStates.Add(bootstrap);
        }
        await database.SaveChangesAsync();
        string email = $"branding-{Guid.CreateVersion7():N}@example.test";
        string password = $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}";
        var created = await credentials.CreatePendingAsync(new LocalCredentialCreateRequest(
            Guid.CreateVersion7(), initiator.Id, email, "Branding", "Administrator"), CancellationToken.None);
        await Assert.That(created.Outcome).IsEqualTo(LocalCredentialCreateOutcome.Created);
        var receipt = created.Receipt!;
        var user = new User
        {
            Id = receipt.LocalSubjectId,
            EmailVerified = true,
            CreatedAt = DateTime.UtcNow,
            Pii = new UserPii { Email = email, FirstName = "Branding", LastName = "Administrator" }
        };
        database.AddRange(user, new Actor
        {
            Id = receipt.PersonalActorId,
            UserId = user.Id,
            User = user,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = "Branding Administrator" },
            CreatedAt = DateTime.UtcNow
        }, new UserExternalLogin
        {
            Id = receipt.ExternalLoginId,
            UserId = user.Id,
            User = user,
            AuthenticationProviderId = (int)AuthenticationProviderKind.Local,
            AuthenticationProvider = null!,
            ProviderKey = user.Id.ToString("D"),
            CreatedAt = DateTime.UtcNow
        });
        await database.SaveChangesAsync();
        var pending = (await credentials.ReadProvisioningAsync(receipt.OperationId, CancellationToken.None))!;
        await Assert.That(await credentials.ActivateChangeRequiredAsync(
            new LocalCredentialActivationRequest(receipt.OperationId, pending.OperationConcurrencyStamp), CancellationToken.None))
            .IsEqualTo(LocalCredentialActivationOutcome.Activated);
        var identityUser = (await manager.FindByIdAsync(receipt.LocalSubjectId.ToString("D")))!;
        await database.Entry(identityUser).ReloadAsync();
        DateTimeOffset issuedAt = host.Services.GetRequiredService<TimeProvider>().GetUtcNow();
        await Assert.That(await credentials.ReplaceAsync(new LocalCredentialReplacementRequest(
            new LocalCredentialReplacementAuthority(
                new LocalCredentialReplacementSubject(identityUser.Id, receipt.OperationId, identityUser.SecurityStamp!),
                issuedAt, issuedAt.AddMinutes(5)), password), CancellationToken.None))
            .IsEqualTo(LocalCredentialReplacementOutcome.Replaced);
        await database.Entry(identityUser).ReloadAsync();
        identityUser.EmailConfirmed = true;
        await Assert.That((await manager.UpdateAsync(identityUser)).Succeeded).IsTrue();
        var binding = await credentials.ReadLinkedIdentityAsync(receipt.LocalSubjectId, CancellationToken.None)
            ?? throw new InvalidOperationException("Fresh PostgreSQL provisioning must establish the exact Local binding.");
        await Assert.That(binding.CredentialState).IsEqualTo(LocalCredentialState.Ready);
        return (new LocalAuthRequestDto(email, password), binding);
    }

    internal async Task<T> InOriginalRequestAsync<T>(Func<Task<T>> operation)
    {
        HttpContext? previous = accessor.HttpContext;
        accessor.HttpContext = request;
        try { return await operation(); }
        finally { accessor.HttpContext = previous; }
    }

    public async ValueTask DisposeAsync()
    {
        if (selected is not null) await selected.DisposeAsync();
        await requestScope.DisposeAsync();
        client.Dispose();
        await host.DisposeAsync();
        host.PrivacyKeys.Dispose();
        using var privacyConnection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = host.PrivacyKeys.AuthorityPath
        }.ToString());
        SqliteConnection.ClearPool(privacyConnection);
        string directory = Path.GetDirectoryName(host.PrivacyKeys.AuthorityPath)!;
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

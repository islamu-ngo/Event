using System.Security.Claims;
using Cerbos.Api.V1.Effect;
using Cerbos.Sdk;
using Cerbos.Sdk.Builder;
using Cerbos.Sdk.Response;
using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Authorization;

public sealed class TenantAuthorityMembershipParityTests
{
    public enum MembershipState { Active, Suspended, Banned, Removed, Deleted, Revoked }

    [Test]
    [Arguments(MembershipState.Active, true, true)]
    [Arguments(MembershipState.Suspended, false, true)]
    [Arguments(MembershipState.Banned, false, true)]
    [Arguments(MembershipState.Removed, false, true)]
    [Arguments(MembershipState.Deleted, false, false)]
    [Arguments(MembershipState.Revoked, false, false)]
    public async Task Enumeration_MatchesBoolean_WithoutNarrowingInventory(
        MembershipState state, bool expectedAuthority, bool expectedInventory)
    {
        await using var database = new AuthorityDatabase();
        await database.InitializeAsync(state);
        var grants = new TenantUserRoleGrantRepository(database.Context);
        var inventory = await grants.GetByUserId(database.UserId);
        await Assert.That(inventory.Any(g => g.TenantId == database.TenantId)).IsEqualTo(expectedInventory);
        await Assert.That(await grants.IsTenantAdmin(database.TenantId, database.UserId)).IsEqualTo(expectedAuthority);

        // Each overload gets a cold cache; neither can be rescued by the other's cached result.
        foreach (var explicitUser in new[] { false, true })
        {
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var admin = database.CreateAdmin(cache);
            var ids = explicitUser
                ? await admin.GetAdminTenantIdsAsync(database.UserId)
                : await admin.GetAdminTenantIdsAsync();
            await Assert.That(ids.Contains(database.TenantId)).IsEqualTo(expectedAuthority);
            await Assert.That(ids).Contains(database.SecondTenantId);
            await Assert.That(ids).DoesNotContain(database.ForeignTenantId);
            await Assert.That(ids.Count).IsEqualTo(expectedAuthority ? 2 : 1);
            await Assert.That(await admin.IsTenantAdminAsync(database.TenantId)).IsEqualTo(expectedAuthority);
            await Assert.That(await admin.GetAdminTenantIdsAsync(database.ForeignUserId))
                .IsEquivalentTo([database.ForeignTenantId]);
        }
    }

    [Test]
    [Arguments(TenantStatusEnum.Provisioning)]
    [Arguments(TenantStatusEnum.Active)]
    [Arguments(TenantStatusEnum.Suspended)]
    [Arguments(TenantStatusEnum.Archived)]
    [Arguments(TenantStatusEnum.Purged)]
    public async Task ActiveMembership_AuthorityDoesNotIntroduceTenantLifecycleFiltering(TenantStatusEnum status)
    {
        await using var database = new AuthorityDatabase();
        await database.InitializeAsync(MembershipState.Active, status);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var admin = database.CreateAdmin(cache);
        await Assert.That(await admin.IsTenantAdminAsync(database.TenantId)).IsTrue();
        await Assert.That(await admin.GetAdminTenantIdsAsync()).Contains(database.TenantId);
    }

    [Test]
    [Arguments(MembershipState.Active, true, "local")]
    [Arguments(MembershipState.Suspended, false, "local")]
    [Arguments(MembershipState.Banned, false, "local")]
    [Arguments(MembershipState.Removed, false, "local")]
    [Arguments(MembershipState.Deleted, false, "local")]
    [Arguments(MembershipState.Revoked, false, "local")]
    [Arguments(MembershipState.Active, true, "cerbos")]
    [Arguments(MembershipState.Suspended, false, "cerbos")]
    [Arguments(MembershipState.Banned, false, "cerbos")]
    [Arguments(MembershipState.Removed, false, "cerbos")]
    [Arguments(MembershipState.Deleted, false, "cerbos")]
    [Arguments(MembershipState.Revoked, false, "cerbos")]
    public async Task SelectedProvider_CategoryDelete_UsesPersistedMembershipEligibility(
        MembershipState state, bool expectedAuthority, string providerName)
    {
        await using var database = new AuthorityDatabase();
        await database.InitializeAsync(state);
        foreach (var machineOwner in new[] { false, true })
        foreach (var batch in new[] { false, true })
        {
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var machine = Substitute.For<IMachinePrincipalAccessor>();
            machine.IsMachineCaller.Returns(machineOwner);
            machine.Current.Returns(machineOwner
                ? new ApiKeyPrincipalContext("membership-key", database.TenantId,
                    ExternalApiKeyOwnerType.User, database.UserId, [ExternalApiKeyScopes.AdminTenant])
                : null);
            var remote = new CategoryPolicyBoundary();
            var provider = database.CreateProvider(providerName, cache, machine, remote);
            var own = DeleteCategory(database.TenantId);
            var foreign = DeleteCategory(database.ForeignTenantId);
            var checks = new[] { own, foreign, DeleteCategory(database.TenantId) };
            if (batch)
            {
                var decisions = await provider.AuthorizeBatchAsync(checks);
                await Assert.That(decisions.Count).IsEqualTo(3);
                await Assert.That(decisions[0].IsAllowed).IsEqualTo(expectedAuthority);
                await Assert.That(decisions[1].IsAllowed).IsFalse();
                await Assert.That(decisions[2].IsAllowed).IsEqualTo(expectedAuthority);
            }
            else
            {
                await Assert.That((await provider.AuthorizeAsync(own)).IsAllowed).IsEqualTo(expectedAuthority);
                await Assert.That((await provider.AuthorizeAsync(foreign)).IsAllowed).IsFalse();
            }
            await Assert.That(remote.Requests.Count > 0).IsEqualTo(providerName == "cerbos");
            foreach (var request in remote.Requests)
            {
                var ids = request.Principal.Attr["tenantMemberships"].StructValue.Fields.Keys;
                await Assert.That(ids.Contains(database.TenantId.ToString())).IsEqualTo(expectedAuthority);
                await Assert.That(ids).Contains(database.SecondTenantId.ToString());
                await Assert.That(ids).DoesNotContain(database.ForeignTenantId.ToString());
                await Assert.That(request.Principal.Id).IsEqualTo(machineOwner
                    ? "api_key:membership-key" : database.UserId.ToString());
            }
        }
    }

    [Test]
    public async Task UserOwnedMachine_LocalRequiresScopeAndOwnerAuthorityInExactTenant()
    {
        await using var database = new AuthorityDatabase();
        await database.InitializeAsync(MembershipState.Active);
        foreach (var batch in new[] { false, true })
        foreach (var scenario in new[] { "read-only-scope", "foreign-owner", "wrong-key-tenant" })
        {
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var machine = Substitute.For<IMachinePrincipalAccessor>();
            machine.IsMachineCaller.Returns(true);
            machine.Current.Returns(new ApiKeyPrincipalContext("membership-key",
                scenario == "wrong-key-tenant" ? database.SecondTenantId : database.TenantId,
                ExternalApiKeyOwnerType.User,
                scenario == "foreign-owner" ? database.ForeignUserId : database.UserId,
                scenario == "read-only-scope" ? [ExternalApiKeyScopes.LookupsRead] : [ExternalApiKeyScopes.AdminTenant]));
            var provider = database.CreateProvider("local", cache, machine, new CategoryPolicyBoundary());
            var check = DeleteCategory(database.TenantId);
            if (batch)
                await Assert.That((await provider.AuthorizeBatchAsync([check, check, check])).All(d => !d.IsAllowed)).IsTrue();
            else
                await Assert.That((await provider.AuthorizeAsync(check)).IsAllowed).IsFalse();
        }
    }

    private static AuthorizationRequest DeleteCategory(Guid tenantId) => TestAuthorizationRequest.Create(
        ResourceKinds.Category, Guid.CreateVersion7().ToString(), AuthorizationActions.Delete,
        new Dictionary<string, object> { ["tenantId"] = tenantId });

    /// <summary>
    /// An external client substitute for the bundled category delete rule and tenant derived role:
    /// authenticated parent role plus matching resource tenant in the production principal map.
    /// It neither manufactures membership attributes nor claims to execute a live Cerbos PDP.
    /// </summary>
    private sealed class CategoryPolicyBoundary
    {
        public List<Cerbos.Api.V1.Request.CheckResourcesRequest> Requests { get; } = [];
        public ICerbosClient CreateClient()
        {
            var client = Substitute.For<ICerbosClient>();
            client.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
                .Returns(call =>
                {
                    var request = call.ArgAt<CheckResourcesRequest>(0).ToCheckResourcesRequest();
                    Requests.Add(request);
                    var response = new Cerbos.Api.V1.Response.CheckResourcesResponse();
                    foreach (var resource in request.Resources)
                    {
                        var result = new Cerbos.Api.V1.Response.CheckResourcesResponse.Types.ResultEntry
                        {
                            Resource = new() { Id = resource.Resource.Id, Kind = resource.Resource.Kind }
                        };
                        foreach (var action in resource.Actions)
                        {
                            var allowed = resource.Resource.Kind == ResourceKinds.Category
                                && action == AuthorizationActions.Delete
                                && request.Principal.Roles.Contains("islamuevent_authenticated_user")
                                && (request.Principal.Attr["isInstanceAdmin"].BoolValue
                                    || resource.Resource.Attr.TryGetValue("tenantId", out var tenant)
                                    && request.Principal.Attr["tenantMemberships"].StructValue.Fields.ContainsKey(tenant.StringValue));
                            result.Actions.Add(action, allowed ? Effect.Allow : Effect.Deny);
                        }
                        response.Results.Add(result);
                    }
                    return new CheckResourcesResponse(response);
                });
            return client;
        }
    }

    private sealed class AuthorityDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = ":memory:"
        }.ToString());
        public ExploreDbContext Context { get; }
        public Guid UserId { get; } = Guid.CreateVersion7();
        public Guid ForeignUserId { get; } = Guid.CreateVersion7();
        public Guid TenantId { get; } = Guid.CreateVersion7();
        public Guid SecondTenantId { get; } = Guid.CreateVersion7();
        public Guid ForeignTenantId { get; } = Guid.CreateVersion7();

        public AuthorityDatabase()
        {
            Context = new ExploreDbContext(new DbContextOptionsBuilder<ExploreDbContext>()
                .UseSqlite(_connection).UseSnakeCaseNamingConvention().Options);
        }

        public async Task InitializeAsync(MembershipState state, TenantStatusEnum tenantStatus = TenantStatusEnum.Active)
        {
            await _connection.OpenAsync();
            await Context.Database.EnsureCreatedAsync();
            Context.RoleScopes.Add(new RoleScope { Id = (int)RoleScopeEnum.Tenant, MasterCode = "tenant", FullName = "Tenant" });
            Context.Roles.AddRange(
                new Role { Id = (int)RoleEnum.TenantAdmin, MasterCode = "tenant.admin", FullName = "Admin", Scope = RoleScopeEnum.Tenant },
                new Role { Id = (int)RoleEnum.TenantMember, MasterCode = "tenant.member", FullName = "Member", Scope = RoleScopeEnum.Tenant });
            foreach (var status in Enum.GetValues<TenantStatusEnum>())
                Context.TenantStatuses.Add(new TenantStatus { Id = (int)status, MasterCode = status.ToString(), FullName = status.ToString(), IsActiveState = status == TenantStatusEnum.Active });
            foreach (var userId in new[] { UserId, ForeignUserId })
                Context.Users.Add(new User { Id = userId, Pii = new UserPii
                {
                    Email = $"{userId}@example.test", FirstName = "Authority", LastName = "Member"
                } });
            AddMembership(TenantId, UserId, state, tenantStatus);
            AddMembership(SecondTenantId, UserId, MembershipState.Active, TenantStatusEnum.Active);
            AddMembership(ForeignTenantId, ForeignUserId, MembershipState.Active, TenantStatusEnum.Active);
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();
            Context.TenantContext = new TenantContext(TenantId);
        }

        private void AddMembership(Guid tenantId, Guid userId, MembershipState state, TenantStatusEnum tenantStatus)
        {
            var tenant = new Tenant { Id = tenantId, FullName = "Authority tenant", Slug = tenantId.ToString(), TenantStatusId = (int)tenantStatus, TenantStatus = null! };
            var membership = new TenantUser
            {
                Id = Guid.CreateVersion7(), TenantId = tenantId, Tenant = tenant, UserId = userId, User = null!,
                StatusId = (int)(state switch
                {
                    MembershipState.Suspended => TenantUserStatusEnum.Suspended,
                    MembershipState.Banned => TenantUserStatusEnum.Banned,
                    MembershipState.Removed => TenantUserStatusEnum.Removed,
                    _ => TenantUserStatusEnum.Active
                }),
                IsDeleted = state == MembershipState.Deleted
            };
            Context.TenantUsers.Add(membership);
            foreach (var role in new[] { RoleEnum.TenantAdmin, RoleEnum.TenantMember })
                Context.TenantUserRoleGrants.Add(new TenantUserRoleGrant
                {
                    Id = Guid.CreateVersion7(), TenantId = tenantId, Tenant = tenant,
                    TenantUserId = membership.Id, TenantUser = membership,
                    RoleId = (int)role, Role = null!, RoleScopeId = (int)RoleScopeEnum.Tenant,
                    RevokedAt = state == MembershipState.Revoked ? new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc) : null
                });
        }

        public AdminContext CreateAdmin(IMemoryCache cache) => new(
            new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, UserId.ToString())], "test")) } },
            new PlatformUserRoleRepository(Context), new TenantUserRoleGrantRepository(Context),
            new OrganizationMemberRepository(Context), new GroupMemberRepository(Context),
            new UserExternalLoginRepository(Context), cache, NullLogger<AdminContext>.Instance);

        public RuntimeAuthorizationProvider CreateProvider(string providerName, IMemoryCache cache,
            IMachinePrincipalAccessor machine, CategoryPolicyBoundary remote)
        {
            var admin = CreateAdmin(cache);
            var organizations = new OrganizationMemberRepository(Context);
            var groups = new GroupMemberRepository(Context);
            var events = new EventAuthoritySnapshotService(Context);
            var settings = Substitute.For<IHierarchicalSettingsResolver>();
            var tenant = new TenantContext(TenantId);
            var principal = new CerbosPrincipalBuilder(admin, machine, events, organizations, groups);
            var cerbos = new CerbosAuthorizationService(remote.CreateClient(), principal, admin, machine,
                settings, tenant, Substitute.For<ICerbosClientFactory>(), Options.Create(new CerbosSettings()),
                NullLogger<CerbosAuthorizationService>.Instance);
            var local = new FallbackAuthorizationService(admin, machine, events, organizations, groups,
                settings, tenant, NullLogger<FallbackAuthorizationService>.Instance);
            return new RuntimeAuthorizationProvider(cerbos, local, Substitute.For<ICerbosConfigResolver>(),
                new SystemSettingRepository(Context, new RelationalSettingMutationLock(Context, new EfCoreUnitOfWork(Context))),
                cache, NullLogger<RuntimeAuthorizationProvider>.Instance,
                Options.Create(new AuthorizationProviderDeploymentOptions { Provider = providerName }));
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed record TenantContext(Guid TenantId) : ITenantContext;
}

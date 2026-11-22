using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class PostgresAdministrativeAuthorityTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExactUserEnumerationIsCompleteWithAbsentOrForeignRequestTenant(bool foreignTenant)
    {
        await using var rows = await AuthorityRows.CreateAsync(fixture);
        await using var request = rows.Authentication.Context(foreignTenant ? rows.Authentication.B : null);
        await rows.Authentication.AssertOrdinaryRoleAsync(request);
        await Assert.That(await request.TenantUsers.IgnoreQueryFilters()
            .AnyAsync(row => row.TenantId == rows.Authentication.A)).IsFalse();
        var reader = new AdministrativeAuthorityRepository(rows.Factory);

        await Assert.That(await reader.IsInstanceAdminAsync(rows.Authentication.UserId)).IsTrue();
        await Assert.That(await reader.IsInstanceAdminAsync(rows.Authentication.OtherUserId)).IsFalse();
        await Assert.That(await reader.GetAdminTenantIdsAsync(rows.Authentication.UserId))
            .IsEquivalentTo([rows.Authentication.A, rows.Authentication.B, rows.Authentication.Inactive]);
        await Assert.That(await reader.GetAdminTenantIdsAsync(rows.Authentication.OtherUserId))
            .IsEquivalentTo([rows.Authentication.B]);
        await Assert.That(await reader.GetAdminOrganizationIdsAsync(rows.Authentication.UserId))
            .IsEquivalentTo([rows.OrganizationA, rows.OrganizationB]);
        await Assert.That(await reader.GetAdminGroupIdsAsync(rows.Authentication.UserId))
            .IsEquivalentTo([rows.GroupA, rows.GroupB]);
        await Assert.That(await reader.GetAdminOrganizationIdsAsync(rows.Authentication.OtherUserId)).IsEmpty();
        await Assert.That(await reader.GetAdminGroupIdsAsync(rows.Authentication.OtherUserId)).IsEmpty();
        await Assert.That(await reader.GetAdminOrganizationIdsAsync(rows.Authentication.UserId, rows.Authentication.A))
            .IsEquivalentTo([rows.OrganizationA]);
        await Assert.That(await reader.GetAdminGroupIdsAsync(rows.Authentication.UserId, rows.Authentication.A))
            .IsEquivalentTo([rows.GroupA]);
        await Assert.That(await reader.GetAdminOrganizationIdsAsync(rows.Authentication.UserId, rows.Authentication.B))
            .IsEquivalentTo([rows.OrganizationB]);
        await Assert.That(await reader.GetAdminGroupIdsAsync(rows.Authentication.UserId, rows.Authentication.B))
            .IsEquivalentTo([rows.GroupB]);
        await Assert.That(await reader.IsInstanceAdminAsync(Guid.Empty)).IsFalse();
        await Assert.That(await reader.GetAdminTenantIdsAsync(Guid.Empty)).IsEmpty();
        await Assert.That(await reader.GetAdminOrganizationIdsAsync(Guid.Empty)).IsEmpty();
        await Assert.That(await reader.GetAdminGroupIdsAsync(Guid.Empty)).IsEmpty();
        await Assert.That(request.TenantFilterTenantId).IsEqualTo(foreignTenant ? rows.Authentication.B : (Guid?)null);
    }

    [Test]
    [Category("AdministrativeScopeIntegrity")]
    public async Task SharedOrganizationAndGroupAcrossTenantsAreReturnedOnce()
    {
        await using var rows = await AuthorityRows.CreateAsync(fixture, sharedScopes: true);
        var reader = new AdministrativeAuthorityRepository(rows.Factory);
        await Assert.That(await reader.GetAdminOrganizationIdsAsync(rows.Authentication.UserId))
            .IsEquivalentTo([rows.OrganizationA, rows.OrganizationB]);
        await Assert.That(await reader.GetAdminGroupIdsAsync(rows.Authentication.UserId))
            .IsEquivalentTo([rows.GroupA, rows.GroupB]);
        await Assert.That(await reader.GetAdminOrganizationIdsAsync(rows.Authentication.UserId, rows.Authentication.B))
            .IsEquivalentTo([rows.OrganizationA, rows.OrganizationB]);
        await Assert.That(await reader.GetAdminGroupIdsAsync(rows.Authentication.UserId, rows.Authentication.B))
            .IsEquivalentTo([rows.GroupA, rows.GroupB]);
    }

    [Test]
    [Arguments("revoked", Categories = new[] { "AdministrativeGrantEligibility" })]
    [Arguments("suspended", Categories = new[] { "AdministrativeMembershipState" })]
    [Arguments("banned", Categories = new[] { "AdministrativeMembershipState" })]
    [Arguments("removed", Categories = new[] { "AdministrativeMembershipState" })]
    [Arguments("deleted", Categories = new[] { "AdministrativeMembershipDeletion", "AdministrativeScopeIntegrity" })]
    [Arguments("member-role", Categories = new[] { "AdministrativeGrantEligibility" })]
    [Arguments("foreign-tenant", Categories = new[] { "AdministrativeTenantGrantFence", "AdministrativeIdentityIntegrity" })]
    public async Task TenantEnumerationRetainsMembershipAndGrantEligibility(string exclusion)
    {
        await using var rows = await AuthorityRows.CreateAsync(fixture);
        await using (var seed = fixture.CreateDbContext())
        {
            seed.BindTenantLease();
            var membership = await seed.TenantUsers.SingleAsync(row =>
                row.UserId == rows.Authentication.UserId && row.TenantId == rows.Authentication.A);
            var grant = await seed.TenantUserRoleGrants.SingleAsync(row => row.TenantUserId == membership.Id);
            switch (exclusion)
            {
                case "revoked": grant.RevokedAt = PostgresAuthenticationLookupAuthorityTests.AuthenticationRows.Now; break;
                case "deleted": membership.IsDeleted = true; break;
                case "member-role": grant.RoleId = (int)RoleEnum.TenantMember; break;
                case "foreign-tenant": grant.TenantId = rows.Authentication.B; break;
                case "suspended": membership.StatusId = (int)TenantUserStatusEnum.Suspended; break;
                case "banned": membership.StatusId = (int)TenantUserStatusEnum.Banned; break;
                case "removed": membership.StatusId = (int)TenantUserStatusEnum.Removed; break;
                default: throw new ArgumentOutOfRangeException(nameof(exclusion));
            }
            if (exclusion == "foreign-tenant")
            {
                var rejected = await Assert.ThrowsAsync<DbUpdateException>(() => seed.SaveChangesAsync());
                var constraint = rejected?.InnerException as PostgresException;
                await Assert.That(constraint?.SqlState).IsEqualTo("23503");
                await Assert.That(constraint?.TableName).IsEqualTo("tenant_user_role_grants");
            }
            else
            {
                await seed.SaveChangesAsync();
            }
        }
        var reader = new AdministrativeAuthorityRepository(rows.Factory);
        Guid[] expected = exclusion == "foreign-tenant"
            ? [rows.Authentication.A, rows.Authentication.B, rows.Authentication.Inactive]
            : [rows.Authentication.B, rows.Authentication.Inactive];
        await Assert.That(await reader.GetAdminTenantIdsAsync(rows.Authentication.UserId))
            .IsEquivalentTo(expected);
    }

    [Test]
    [Arguments(TenantStatusEnum.Provisioning, Categories = new[] { "AdministrativePendingLifecycle" })]
    [Arguments(TenantStatusEnum.Suspended, Categories = new[] { "AdministrativePendingLifecycle" })]
    [Arguments(TenantStatusEnum.Archived, Categories = new[] { "AdministrativeTerminalLifecycle" })]
    [Arguments(TenantStatusEnum.Purged, Categories = new[] { "AdministrativeTerminalLifecycle" })]
    public async Task TenantEnumerationDoesNotAddLifecycleFiltering(TenantStatusEnum status)
    {
        await using var rows = await AuthorityRows.CreateAsync(fixture);
        await using (var seed = fixture.CreateDbContext())
        {
            seed.BindTenantLease();
            var tenant = await seed.Tenants.SingleAsync(row => row.Id == rows.Authentication.A);
            tenant.TenantStatusId = (int)status;
            await seed.SaveChangesAsync();
        }
        var reader = new AdministrativeAuthorityRepository(rows.Factory);
        await Assert.That(await reader.GetAdminTenantIdsAsync(rows.Authentication.UserId))
            .IsEquivalentTo([rows.Authentication.A, rows.Authentication.B, rows.Authentication.Inactive]);
    }

    [Test]
    [Arguments("membership", Categories = new[] { "AdministrativeDeletion" })]
    [Arguments("participation", Categories = new[] { "AdministrativeDeletion" })]
    [Arguments("root", Categories = new[] { "AdministrativeDeletion" })]
    [Arguments("member-role", Categories = new[] { "AdministrativeParticipation" })]
    [Arguments("unapproved", Categories = new[] { "AdministrativeParticipation" })]
    [Arguments("foreign-subject", Categories = new[] { "AdministrativeJoinIsolation", "AdministrativeIdentityIntegrity" })]
    [Arguments("foreign-tenant", Categories = new[] { "AdministrativeParticipationTenantFence", "AdministrativeIdentityIntegrity" })]
    public async Task OrganizationAndGroupEnumerationRetainOriginalJoinsWithoutApprovalFiltering(string state)
    {
        await using var rows = await AuthorityRows.CreateAsync(fixture);
        await using (var seed = fixture.CreateDbContext())
        {
            seed.BindTenantLease();
            var organization = await seed.OrganizationMembers.Include(row => row.OrganizationTenant)
                .ThenInclude(row => row.Organization).SingleAsync(row =>
                    row.OrganizationTenant.OrganizationId == rows.OrganizationA);
            var group = await seed.GroupMembers.Include(row => row.GroupTenant)
                .ThenInclude(row => row.Group).SingleAsync(row => row.GroupTenant.GroupId == rows.GroupA);
            switch (state)
            {
                case "membership": organization.IsDeleted = true; group.IsDeleted = true; break;
                case "participation": organization.OrganizationTenant.IsDeleted = true; group.GroupTenant.IsDeleted = true; break;
                case "root": organization.OrganizationTenant.Organization.IsDeleted = true; group.GroupTenant.Group.IsDeleted = true; break;
                case "member-role": organization.RoleId = (int)RoleEnum.OrgMember; group.RoleId = (int)RoleEnum.GroupMember; break;
                case "foreign-subject":
                    organization.UserId = rows.Authentication.OtherUserId;
                    group.UserId = rows.Authentication.OtherUserId;
                    break;
                case "foreign-tenant":
                    organization.TenantId = rows.Authentication.B;
                    group.TenantId = rows.Authentication.B;
                    break;
                case "unapproved":
                    organization.OrganizationTenant.ApprovalStatusId = (int)ApprovalStatusEnum.Rejected;
                    group.GroupTenant.ApprovalStatusId = (int)ApprovalStatusEnum.Revoked;
                    organization.OrganizationTenant.IsSuspended = true;
                    group.GroupTenant.IsSuspended = true;
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(state));
            }
            if (state == "foreign-tenant")
            {
                var rejected = await Assert.ThrowsAsync<DbUpdateException>(() => seed.SaveChangesAsync());
                var constraint = rejected?.InnerException as PostgresException;
                await Assert.That(constraint?.SqlState).IsEqualTo("23503");
                await Assert.That(constraint?.TableName is "organization_members" or "group_members").IsTrue();
            }
            else
            {
                await seed.SaveChangesAsync();
            }
        }
        var reader = new AdministrativeAuthorityRepository(rows.Factory);
        Guid[] expectedOrganizations = state is "unapproved" or "foreign-tenant"
            ? [rows.OrganizationA, rows.OrganizationB]
            : [rows.OrganizationB];
        Guid[] expectedGroups = state is "unapproved" or "foreign-tenant"
            ? [rows.GroupA, rows.GroupB]
            : [rows.GroupB];
        await Assert.That(await reader.GetAdminOrganizationIdsAsync(rows.Authentication.UserId))
            .IsEquivalentTo(expectedOrganizations);
        await Assert.That(await reader.GetAdminGroupIdsAsync(rows.Authentication.UserId))
            .IsEquivalentTo(expectedGroups);
    }

    [Test]
    [Arguments("tenant")]
    [Arguments("organization", Categories = new[] { "AdministrativeRuntimePermission" })]
    [Arguments("group", Categories = new[] { "AdministrativeRuntimePermission" })]
    public async Task RuntimeCannotReadAdministrativeRowsAcrossTenants(string scope)
    {
        await using var rows = await AuthorityRows.CreateAsync(fixture);
        await using var request = rows.Authentication.Context(rows.Authentication.B);
        await rows.Authentication.AssertOrdinaryRoleAsync(request);
        if (scope == "tenant")
        {
            await Assert.That(await request.TenantUserRoleGrants.IgnoreQueryFilters()
                .AnyAsync(row => row.TenantId == rows.Authentication.A)).IsFalse();
            return;
        }

        var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            _ = scope switch
            {
                "organization" => await request.OrganizationMembers.IgnoreQueryFilters()
                    .AnyAsync(row => row.TenantId == rows.Authentication.A),
                "group" => await request.GroupMembers.IgnoreQueryFilters()
                    .AnyAsync(row => row.TenantId == rows.Authentication.A),
                _ => throw new ArgumentOutOfRangeException(nameof(scope))
            };
        });
        await Assert.That(denied?.SqlState).IsEqualTo("42501");
    }

    [Test]
    [Category("AdministrativeScopeIntegrity")]
    public async Task GlobalAdministrativeReadsDoNotGrantPrivateColumnsOrWrites()
    {
        await using var rows = await AuthorityRows.CreateAsync(fixture);
        await using var context = await rows.Factory.CreateDbContextAsync();
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        var names = new HumanPrincipalSourceSql(context);
        // Existing setup/worker purposes already read selected PII and membership
        // columns. Protect columns outside that declared union, not those old rights.
        (Type Type, string Property)[] protectedColumns =
        [
            (typeof(UserPii), "_dateOfBirth"),
            (typeof(OrganizationMember), nameof(OrganizationMember.Id)),
            (typeof(GroupMember), nameof(GroupMember.Id))
        ];
        foreach (var (type, property) in protectedColumns)
        {
            var entity = context.Model.FindEntityType(type)!;
            var store = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table(
                entity.GetTableName()!, entity.GetSchema());
            string column = entity.FindProperty(property)!.GetColumnName(store)!;
            command.CommandText = $"SELECT {names.Quote(column)} FROM {names.Quote(entity.GetSchema()!)}.{names.Quote(entity.GetTableName()!)} LIMIT 1";
            var denied = await Assert.ThrowsAsync<PostgresException>(async () => { await command.ExecuteScalarAsync(); });
            await Assert.That(denied?.SqlState).IsEqualTo("42501");
        }
        var write = await Assert.ThrowsAsync<PostgresException>(() =>
            context.OrganizationMembers.IgnoreQueryFilters().Where(row => row.UserId == rows.Authentication.UserId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.RoleId, (int)RoleEnum.OrgMember)));
        await Assert.That(write?.SqlState).IsEqualTo("42501");
    }

    private sealed class AuthorityRows : IAsyncDisposable
    {
        internal PostgresAuthenticationLookupAuthorityTests.AuthenticationRows Authentication { get; private set; } = null!;
        internal GlobalExploreDbContextFactory Factory { get; private set; } = null!;
        internal Guid OrganizationA { get; } = Guid.CreateVersion7();
        internal Guid OrganizationB { get; } = Guid.CreateVersion7();
        internal Guid GroupA { get; } = Guid.CreateVersion7();
        internal Guid GroupB { get; } = Guid.CreateVersion7();

        internal static async Task<AuthorityRows> CreateAsync(PostgreSqlContainerFixture fixture, bool sharedScopes = false)
        {
            var rows = new AuthorityRows();
            rows.Authentication = await PostgresAuthenticationLookupAuthorityTests.AuthenticationRows.CreateAsync(fixture);
            await using var seed = fixture.CreateDbContext();
            seed.BindTenantLease();
            await PostgresAdministrativeAuthorityReadPermissions.ApplyAsync(seed, fixture.NativeQueueEnrollment);
            seed.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(),
                UserId = rows.Authentication.UserId,
                User = null!,
                RoleId = (int)RoleEnum.Admin,
                Role = null!
            });
            foreach (var membership in await seed.TenantUsers.Where(row =>
                row.UserId == rows.Authentication.UserId || row.UserId == rows.Authentication.OtherUserId).ToListAsync())
                seed.TenantUserRoleGrants.Add(new TenantUserRoleGrant
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = membership.TenantId,
                    Tenant = null!,
                    TenantUserId = membership.Id,
                    TenantUser = membership,
                    RoleId = (int)RoleEnum.TenantAdmin,
                    Role = null!,
                    RoleScopeId = (int)RoleScopeEnum.Tenant
                });
            AddScopes(rows.Authentication.A, rows.OrganizationA, rows.GroupA);
            AddScopes(rows.Authentication.B, rows.OrganizationB, rows.GroupB);
            if (sharedScopes)
                AddScopes(rows.Authentication.B, rows.OrganizationA, rows.GroupA);
            await seed.SaveChangesAsync();

            rows.Factory = new GlobalExploreDbContextFactory(
                fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global), poolSize: 1);
            await using var verify = await rows.Factory.CreateDbContextAsync();
            await verify.Database.OpenConnectionAsync();
            await using var probe = verify.Database.GetDbConnection().CreateCommand();
            probe.CommandText = "SELECT current_user, rolsuper, rolbypassrls FROM pg_catalog.pg_roles WHERE rolname = current_user";
            await using var result = await probe.ExecuteReaderAsync();
            await Assert.That(await result.ReadAsync()).IsTrue();
            await Assert.That(result.GetString(0)).IsEqualTo(fixture.NativeQueueEnrollment.GlobalLogin);
            await Assert.That(result.GetBoolean(1)).IsFalse();
            await Assert.That(result.GetBoolean(2)).IsFalse();
            return rows;

            void AddScopes(Guid tenantId, Guid organizationId, Guid groupId)
            {
                var organization = seed.Organizations.Local.SingleOrDefault(row => row.Id == organizationId) ?? new Organization
                {
                    Id = organizationId,
                    Pii = new OrganizationPii { FullName = "Authority organization" },
                    ConcurrencyStamp = Guid.CreateVersion7()
                };
                var participation = new OrganizationTenant
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenantId,
                    Tenant = null!,
                    OrganizationId = organizationId,
                    Organization = organization,
                    ApprovalStatusId = (int)ApprovalStatusEnum.Pending,
                    ApprovalStatus = null!,
                    ConcurrencyStamp = Guid.CreateVersion7()
                };
                seed.OrganizationMembers.Add(new OrganizationMember
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenantId,
                    Tenant = null!,
                    UserId = rows.Authentication.UserId,
                    User = null!,
                    OrganizationTenantId = participation.Id,
                    OrganizationTenant = participation,
                    RoleId = (int)RoleEnum.OrgAdmin,
                    Role = null!
                });
                var group = seed.Groups.Local.SingleOrDefault(row => row.Id == groupId) ?? new Group
                {
                    Id = groupId,
                    FullName = "Authority group",
                    ConcurrencyStamp = Guid.CreateVersion7()
                };
                var groupParticipation = new GroupTenant
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenantId,
                    Tenant = null!,
                    GroupId = groupId,
                    Group = group,
                    ApprovalStatusId = (int)ApprovalStatusEnum.Pending,
                    ApprovalStatus = null!,
                    ConcurrencyStamp = Guid.CreateVersion7()
                };
                seed.GroupMembers.Add(new GroupMember
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenantId,
                    Tenant = null!,
                    UserId = rows.Authentication.UserId,
                    User = null!,
                    GroupTenantId = groupParticipation.Id,
                    GroupTenant = groupParticipation,
                    RoleId = (int)RoleEnum.GroupAdmin,
                    Role = null!
                });
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Authentication.DisposeAsync();
        }
    }
}

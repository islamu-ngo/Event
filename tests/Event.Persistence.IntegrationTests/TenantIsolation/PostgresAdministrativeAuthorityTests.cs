using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
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
        await Assert.That(request.TenantFilterTenantId).IsEqualTo(foreignTenant ? rows.Authentication.B : (Guid?)null);
    }

    [Test]
    [Arguments("revoked")]
    [Arguments("suspended")]
    [Arguments("banned")]
    [Arguments("removed")]
    [Arguments("deleted")]
    [Arguments("member-role")]
    public async Task TenantEnumerationRetainsMembershipAndGrantEligibility(string exclusion)
    {
        await using var rows = await AuthorityRows.CreateAsync(fixture);
        await using (var seed = fixture.CreateDbContext())
        {
            var membership = await seed.TenantUsers.SingleAsync(row =>
                row.UserId == rows.Authentication.UserId && row.TenantId == rows.Authentication.A);
            var grant = await seed.TenantUserRoleGrants.SingleAsync(row => row.TenantUserId == membership.Id);
            switch (exclusion)
            {
                case "revoked": grant.RevokedAt = PostgresAuthenticationLookupAuthorityTests.AuthenticationRows.Now; break;
                case "deleted": membership.IsDeleted = true; break;
                case "member-role": grant.RoleId = (int)RoleEnum.TenantMember; break;
                case "suspended": membership.StatusId = (int)TenantUserStatusEnum.Suspended; break;
                case "banned": membership.StatusId = (int)TenantUserStatusEnum.Banned; break;
                case "removed": membership.StatusId = (int)TenantUserStatusEnum.Removed; break;
                default: throw new ArgumentOutOfRangeException(nameof(exclusion));
            }
            await seed.SaveChangesAsync();
        }
        var reader = new AdministrativeAuthorityRepository(rows.Factory);
        await Assert.That(await reader.GetAdminTenantIdsAsync(rows.Authentication.UserId))
            .IsEquivalentTo([rows.Authentication.B, rows.Authentication.Inactive]);
    }

    [Test]
    [Arguments(TenantStatusEnum.Provisioning)]
    [Arguments(TenantStatusEnum.Suspended)]
    [Arguments(TenantStatusEnum.Archived)]
    [Arguments(TenantStatusEnum.Purged)]
    public async Task TenantEnumerationDoesNotAddLifecycleFiltering(TenantStatusEnum status)
    {
        await using var rows = await AuthorityRows.CreateAsync(fixture);
        await using (var seed = fixture.CreateDbContext())
        {
            var tenant = await seed.Tenants.SingleAsync(row => row.Id == rows.Authentication.A);
            tenant.TenantStatusId = (int)status;
            await seed.SaveChangesAsync();
        }
        var reader = new AdministrativeAuthorityRepository(rows.Factory);
        await Assert.That(await reader.GetAdminTenantIdsAsync(rows.Authentication.UserId))
            .IsEquivalentTo([rows.Authentication.A, rows.Authentication.B, rows.Authentication.Inactive]);
    }

    [Test]
    [Arguments("membership")]
    [Arguments("participation")]
    [Arguments("root")]
    [Arguments("member-role")]
    [Arguments("unapproved")]
    public async Task OrganizationAndGroupEnumerationRetainOriginalJoinsWithoutApprovalFiltering(string state)
    {
        await using var rows = await AuthorityRows.CreateAsync(fixture);
        await using (var seed = fixture.CreateDbContext())
        {
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
                case "unapproved":
                    organization.OrganizationTenant.ApprovalStatusId = (int)ApprovalStatusEnum.Rejected;
                    group.GroupTenant.ApprovalStatusId = (int)ApprovalStatusEnum.Revoked;
                    organization.OrganizationTenant.IsSuspended = true;
                    group.GroupTenant.IsSuspended = true;
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(state));
            }
            await seed.SaveChangesAsync();
        }
        var reader = new AdministrativeAuthorityRepository(rows.Factory);
        Guid[] expectedOrganizations = state == "unapproved" ? [rows.OrganizationA, rows.OrganizationB] : [rows.OrganizationB];
        Guid[] expectedGroups = state == "unapproved" ? [rows.GroupA, rows.GroupB] : [rows.GroupB];
        await Assert.That(await reader.GetAdminOrganizationIdsAsync(rows.Authentication.UserId))
            .IsEquivalentTo(expectedOrganizations);
        await Assert.That(await reader.GetAdminGroupIdsAsync(rows.Authentication.UserId))
            .IsEquivalentTo(expectedGroups);
    }

    [Test]
    [Arguments("instance")]
    [Arguments("tenant")]
    [Arguments("organization")]
    [Arguments("group")]
    public async Task RuntimeCannotExecuteAdministrativeDiscovery(string scope)
    {
        await using var rows = await AuthorityRows.CreateAsync(fixture);
        await using var request = rows.Authentication.Context(rows.Authentication.A);
        await rows.Authentication.AssertOrdinaryRoleAsync(request);
        await using var command = request.Database.GetDbConnection().CreateCommand();
        command.CommandText = scope switch
        {
            "instance" => $"SELECT {PostgresAdministrativeAuthorityContract.InstanceFunctionSql(request)}(@user)",
            "tenant" => $"SELECT * FROM {PostgresAdministrativeAuthorityContract.TenantFunctionSql(request)}(@user)",
            "organization" => $"SELECT * FROM {PostgresAdministrativeAuthorityContract.OrganizationFunctionSql(request)}(@user, NULL)",
            "group" => $"SELECT * FROM {PostgresAdministrativeAuthorityContract.GroupFunctionSql(request)}(@user, NULL)",
            _ => throw new ArgumentOutOfRangeException(nameof(scope))
        };
        var parameter = command.CreateParameter();
        parameter.ParameterName = "user";
        parameter.Value = rows.Authentication.UserId;
        command.Parameters.Add(parameter);
        var denied = await Assert.ThrowsAsync<PostgresException>(async () => { await command.ExecuteScalarAsync(); });
        await Assert.That(denied?.SqlState).IsEqualTo("42501");
    }

    [Test]
    public async Task GlobalExecutorCannotReadPrivateTablesAndOwnerCannotLogin()
    {
        await using var rows = await AuthorityRows.CreateAsync(fixture);
        await using var context = await rows.Factory.CreateDbContextAsync();
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        foreach (Type type in new[] { typeof(PlatformUserRole), typeof(TenantUserRoleGrant), typeof(TenantUser),
            typeof(OrganizationMember), typeof(OrganizationTenant), typeof(Organization),
            typeof(GroupMember), typeof(GroupTenant), typeof(Group), typeof(UserPii), typeof(ActorPii) })
        {
            var entity = context.Model.FindEntityType(type)!;
            command.CommandText = $"SELECT 1 FROM \"{entity.GetSchema()}\".\"{entity.GetTableName()}\" LIMIT 1";
            var denied = await Assert.ThrowsAsync<PostgresException>(async () => { await command.ExecuteScalarAsync(); });
            await Assert.That(denied?.SqlState).IsEqualTo("42501");
        }
        command.CommandText = $"""
            SELECT rolcanlogin OR rolsuper OR rolbypassrls FROM pg_catalog.pg_roles
            WHERE rolname = '{PostgresAdministrativeAuthorityContract.OwnerRole}'
            """;
        await Assert.That((bool)(await command.ExecuteScalarAsync())!).IsFalse();
        command.CommandText = $"""
            SELECT count(*) FROM pg_catalog.pg_proc p
            JOIN pg_catalog.pg_roles r ON r.oid = p.proowner
            JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
            WHERE r.rolname = '{PostgresAdministrativeAuthorityContract.OwnerRole}'
              AND n.nspname = @schema
              AND p.prosecdef AND 'search_path=pg_catalog' = ANY(p.proconfig)
            """;
        var schema = command.CreateParameter();
        schema.ParameterName = "schema";
        schema.Value = rows.Authentication.Schema;
        command.Parameters.Add(schema);
        await Assert.That((long)(await command.ExecuteScalarAsync())!).IsEqualTo(4);
    }

    private sealed class AuthorityRows(PostgreSqlContainerFixture fixture) : IAsyncDisposable
    {
        internal PostgresAuthenticationLookupAuthorityTests.AuthenticationRows Authentication { get; private set; } = null!;
        internal GlobalExploreDbContextFactory Factory { get; private set; } = null!;
        internal Guid OrganizationA { get; } = Guid.CreateVersion7();
        internal Guid OrganizationB { get; } = Guid.CreateVersion7();
        internal Guid GroupA { get; } = Guid.CreateVersion7();
        internal Guid GroupB { get; } = Guid.CreateVersion7();
        private readonly string _role = $"administrative_global_{Guid.CreateVersion7():N}";
        private string _connectionString = null!;

        internal static async Task<AuthorityRows> CreateAsync(PostgreSqlContainerFixture fixture)
        {
            var rows = new AuthorityRows(fixture);
            rows.Authentication = await PostgresAuthenticationLookupAuthorityTests.AuthenticationRows.CreateAsync(fixture);
            await using var seed = fixture.CreateDbContext();
            await PostgresAdministrativeAuthorityContract.ApplyAsync(seed);
            seed.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(),
                UserId = rows.Authentication.UserId,
                User = null!,
                RoleId = (int)RoleEnum.Admin,
                Role = null!
            });
            foreach (var membership in await seed.TenantUsers.ToListAsync())
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
            await seed.SaveChangesAsync();

            string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            await using var administration = new NpgsqlConnection(fixture.ConnectionString);
            await administration.OpenAsync();
            await using var grant = administration.CreateCommand();
            grant.CommandText = $"""
                CREATE ROLE "{rows._role}" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD '{password}';
                GRANT {PostgresAdministrativeAuthorityContract.GlobalRole} TO "{rows._role}";
                """;
            await grant.ExecuteNonQueryAsync();
            var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
            {
                Username = rows._role,
                Password = password
            };
            rows._connectionString = connection.ConnectionString;
            rows.Factory = new GlobalExploreDbContextFactory(new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Global,
                Provider = PrimaryDatabaseProvider.PostgreSql,
                Host = connection.Host,
                Port = connection.Port,
                Database = connection.Database,
                Username = rows._role,
                Password = password,
                Schema = rows.Authentication.Schema,
                TlsMode = PrimaryDatabaseTlsMode.Disabled
            }, poolSize: 1);
            await using var verify = await rows.Factory.CreateDbContextAsync();
            await verify.Database.OpenConnectionAsync();
            await using var probe = verify.Database.GetDbConnection().CreateCommand();
            probe.CommandText = "SELECT current_user, rolsuper, rolbypassrls FROM pg_catalog.pg_roles WHERE rolname = current_user";
            await using var result = await probe.ExecuteReaderAsync();
            await Assert.That(await result.ReadAsync()).IsTrue();
            await Assert.That(result.GetString(0)).IsEqualTo(rows._role);
            await Assert.That(result.GetBoolean(1)).IsFalse();
            await Assert.That(result.GetBoolean(2)).IsFalse();
            return rows;

            void AddScopes(Guid tenantId, Guid organizationId, Guid groupId)
            {
                var organization = new Organization
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
                var group = new Group
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
            using var pool = new NpgsqlConnection(_connectionString);
            NpgsqlConnection.ClearPool(pool);
            await using var administration = new NpgsqlConnection(fixture.ConnectionString);
            await administration.OpenAsync();
            await using var revoke = administration.CreateCommand();
            revoke.CommandText = $"""
                REVOKE {PostgresAdministrativeAuthorityContract.GlobalRole} FROM "{_role}";
                DROP ROLE "{_role}";
                """;
            await revoke.ExecuteNonQueryAsync();
            await Authentication.DisposeAsync();
        }
    }
}

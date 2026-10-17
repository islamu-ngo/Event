using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Repositories;

/// <summary>Real enrolled Runtime reads; native principal parameters are not human custody admissions.</summary>
[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed partial class PostgresAiActorContextReadContractTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task RuntimeReturnsOnlyDetachedDisplayAndOwnerGraphsOnOriginalTransaction()
    {
        var seed = await SeedAsync();
        await using var context = Open(PrimaryDatabaseRole.Runtime, seed.Tenant);
        await context.Database.OpenConnectionAsync();
        await using var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var rows = await ReadAsync(context, seed);
        await Assert.That(rows.Count).IsEqualTo(3);
        var personal = rows.Single(row => row.Id == seed.Personal);
        var organization = rows.Single(row => row.Id == seed.OrganizationActor);
        var group = rows.Single(row => row.Id == seed.GroupActor);
        await Assert.That(personal.DisplayName).IsEqualTo("Original personal display");
        await Assert.That(personal.UserId).IsEqualTo(seed.User);
        await Assert.That(personal.User).IsNull();
        await Assert.That(organization.Organization!.FullName).IsEqualTo("Organization display");
        await Assert.That(organization.Organization.Email).IsNull();
        await Assert.That(organization.Organization.Address).IsNull();
        await Assert.That(group.Group!.FullName).IsEqualTo("Group display");
        await Assert.That(rows.All(row => row.Pii.ProfilePicture is null
            && row.AtprotoIdentities.Count == 0 && row.ServicePrincipal is null
            && row.ExternalActorSubject is null && row.Description is null)).IsTrue();
        await Assert.That(context.ChangeTracker.Entries().Count()).IsEqualTo(0);
        await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(owner);
        await owner.RollbackAsync();
    }

    [Test]
    [Arguments("actor-deleted")]
    [Arguments("actor-suspended")]
    [Arguments("owner-deleted")]
    [Arguments("placement-deleted")]
    [Arguments("placement-pending")]
    [Arguments("placement-suspended")]
    [Arguments("member-deleted")]
    [Arguments("wrong-role-scope")]
    [Arguments("mapping-absent")]
    public async Task OrganizationAndGroupRequireEveryLiveGrantCondition(string condition)
    {
        var seed = await SeedAsync();
        await using var migration = fixture.CreateDbContext();
        var names = new HumanPrincipalSourceSql(migration);
        foreach (bool organization in new[] { true, false })
        {
            Type placement = organization ? typeof(OrganizationTenant) : typeof(GroupTenant);
            Type member = organization ? typeof(OrganizationMember) : typeof(GroupMember);
            Type entity = organization ? typeof(Organization) : typeof(Group);
            Guid actor = organization ? seed.OrganizationActor : seed.GroupActor;
            Guid owner = organization ? seed.Organization : seed.Group;
            Guid link = organization ? seed.OrganizationPlacement : seed.GroupPlacement;
            Guid membership = organization ? seed.OrganizationMember : seed.GroupMember;
            int role = organization ? seed.OrganizationRole : seed.GroupRole;
            string update = condition switch
            {
                "actor-deleted" => Update(typeof(Actor), "IsDeleted", "TRUE", actor),
                "actor-suspended" => Update(typeof(Actor), "IsSuspended", "TRUE", actor),
                "owner-deleted" => Update(entity, "IsDeleted", "TRUE", owner),
                "placement-deleted" => Update(placement, "IsDeleted", "TRUE", link),
                "placement-pending" => Update(placement, "ApprovalStatusId", ((int)ApprovalStatusEnum.Pending).ToString(), link),
                "placement-suspended" => Update(placement, "IsSuspended", "TRUE", link),
                "member-deleted" => Update(member, "IsDeleted", "TRUE", membership),
                "wrong-role-scope" => $"UPDATE {names.Table(typeof(Role))} SET {names.Column(typeof(Role), "RoleScopeId")} = {(int)RoleScopeEnum.Platform} WHERE {names.Column(typeof(Role), "Id")} = {role}",
                "mapping-absent" => $"DELETE FROM {names.Table(typeof(RolePermission))} WHERE {names.Column(typeof(RolePermission), "RoleId")} = {role}",
                _ => throw new ArgumentOutOfRangeException(nameof(condition))
            };
            await migration.Database.ExecuteSqlRawAsync(update);
        }
        var rows = await ReadFreshAsync(seed);
        await Assert.That(rows.Count).IsEqualTo(1);
        await Assert.That(rows.Single().Id).IsEqualTo(seed.Personal);

        string Update(Type type, string property, string value, Guid key) =>
            $"UPDATE {names.Table(type)} SET {names.Column(type, property)} = {value} WHERE {names.Column(type, "Id")} = '{key:D}'::uuid";
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task InactivePermissionOrLegacyAdministratorWithoutMappingCannotGrantActors(bool legacyWithoutMapping)
    {
        var seed = await SeedAsync();
        await using var migration = fixture.CreateDbContext();
        var permission = await migration.Set<Permission>().SingleAsync(row => row.MasterCode == PermissionCodes.EventCreate);
        bool originallyActive = permission.IsActive;
        int[] roles = [(int)RoleEnum.OrgAdmin, (int)RoleEnum.GroupAdmin];
        var mappings = await migration.Set<RolePermission>().AsNoTracking()
            .Where(row => roles.Contains(row.RoleId) && row.PermissionId == permission.Id).ToListAsync();
        try
        {
            if (legacyWithoutMapping)
            {
                await migration.OrganizationMembers.Where(row => row.Id == seed.OrganizationMember)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.RoleId, (int)RoleEnum.OrgAdmin));
                await migration.GroupMembers.Where(row => row.Id == seed.GroupMember)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.RoleId, (int)RoleEnum.GroupAdmin));
                await migration.Set<RolePermission>().Where(row => roles.Contains(row.RoleId) && row.PermissionId == permission.Id)
                    .ExecuteDeleteAsync();
            }
            else
            {
                permission.IsActive = false;
                await migration.SaveChangesAsync();
            }
            var rows = await ReadFreshAsync(seed);
            await Assert.That(rows.Count).IsEqualTo(1);
            await Assert.That(rows.Single().Id).IsEqualTo(seed.Personal);
        }
        finally
        {
            if (legacyWithoutMapping)
            {
                migration.Set<RolePermission>().AddRange(mappings);
            }
            else
            {
                permission.IsActive = originallyActive;
            }
            await migration.SaveChangesAsync();
        }
    }

    [Test]
    [Arguments("user-deleted")]
    [Arguments("membership-deleted")]
    [Arguments("membership-inactive")]
    [Arguments("tenant-inactive")]
    [Arguments("personal-suspended")]
    public async Task OriginalHumanAndTenantMembershipMustRemainLive(string condition)
    {
        var seed = await SeedAsync();
        await using var migration = fixture.CreateDbContext();
        var sql = new HumanPrincipalSourceSql(migration);
        string change = condition switch
        {
            "user-deleted" => $"UPDATE {sql.Table(typeof(User))} SET is_deleted = TRUE WHERE id = '{seed.User:D}'",
            "membership-deleted" => $"UPDATE {sql.Table(typeof(TenantUser))} SET is_deleted = TRUE WHERE tenant_id = '{seed.Tenant:D}' AND user_id = '{seed.User:D}'",
            "membership-inactive" => $"UPDATE {sql.Table(typeof(TenantUser))} SET status_id = {(int)TenantUserStatusEnum.Suspended} WHERE tenant_id = '{seed.Tenant:D}' AND user_id = '{seed.User:D}'",
            "tenant-inactive" => $"UPDATE {sql.Table(typeof(Tenant))} SET tenant_status_id = {(int)TenantStatusEnum.Suspended} WHERE id = '{seed.Tenant:D}'",
            "personal-suspended" => $"UPDATE {sql.Table(typeof(Actor))} SET is_suspended = TRUE WHERE id = '{seed.Personal:D}'",
            _ => throw new ArgumentOutOfRangeException(nameof(condition))
        };
        await migration.Database.ExecuteSqlRawAsync(change);
        var rows = await ReadFreshAsync(seed);
        await Assert.That(rows.Count).IsEqualTo(condition == "personal-suspended" ? 2 : 0);
        await Assert.That(rows.Any(row => row.Id == seed.Personal)).IsFalse();
    }

    [Test]
    public async Task OtherTenantMembershipDoesNotLeakOrganizationOrGroupActors()
    {
        var seed = await SeedAsync();
        var foreign = await SeedAsync();
        await using var context = Open(PrimaryDatabaseRole.Runtime, foreign.Tenant);
        await context.Database.OpenConnectionAsync();
        await using var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var rows = await PostgresAiActorContextReadContract.ReadAsync(
            context, fixture.NativeQueueEnrollment, foreign.Tenant, seed.User, default);
        await Assert.That(rows.Count).IsEqualTo(0);
        var mismatch = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var command = NativeCommand(context, seed.Tenant, seed.User);
            await command.ExecuteScalarAsync();
        });
        await Assert.That(mismatch!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        await owner.RollbackAsync();
    }

    [Test]
    public async Task RuntimeAndGlobalCannotReadPrivateTablesOrAssumeDefiner()
    {
        var seed = await SeedAsync();
        foreach (var role in new[] { PrimaryDatabaseRole.Runtime, PrimaryDatabaseRole.Global })
        {
            await using var context = Open(role, seed.Tenant);
            await context.Database.OpenConnectionAsync();
            foreach (Type type in new[] { typeof(Actor), typeof(ActorPii), typeof(User), typeof(UserPii),
                typeof(TenantUser), typeof(Organization), typeof(OrganizationPii), typeof(OrganizationMember), typeof(GroupMember) })
            {
                var error = await Assert.ThrowsAsync<PostgresException>(() =>
                    context.Database.ExecuteSqlRawAsync($"SELECT * FROM {new HumanPrincipalSourceSql(context).Table(type)} LIMIT 1"));
                await Assert.That(error!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
            }
            var promotion = await Assert.ThrowsAsync<PostgresException>(() =>
                context.Database.ExecuteSqlRawAsync($"SET ROLE {PostgresAiActorContextReadContract.OwnerRole}"));
            await Assert.That(promotion!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        }
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Global)]
    [Arguments(PrimaryDatabaseRole.Migrator)]
    public async Task OtherDatabasePurposesCannotExecuteNativeDataRoutine(PrimaryDatabaseRole role)
    {
        var seed = await SeedAsync();
        await using var context = Open(role, seed.Tenant);
        await context.Database.OpenConnectionAsync();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await using var command = NativeCommand(context, seed.Tenant, seed.User);
        var error = await Assert.ThrowsAsync<PostgresException>(async () => await command.ExecuteScalarAsync());
        await Assert.That(error!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task RuntimeCannotInstallOrSelectWithoutSerializableOwner()
    {
        var seed = await SeedAsync();
        await using var context = Open(PrimaryDatabaseRole.Runtime, seed.Tenant);
        await context.Database.OpenConnectionAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ReadAsync(context, seed));
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            await using var command = NativeCommand(context, seed.Tenant, seed.User);
            var error = await Assert.ThrowsAsync<PostgresException>(async () => await command.ExecuteScalarAsync());
            await Assert.That(error!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
            await transaction.RollbackAsync();
        }
        var install = await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresAiActorContextReadContract.ApplyAsync(context, fixture.NativeQueueEnrollment));
        await Assert.That(install!.MessageText).IsEqualTo("ai_actor_context_migrator_required");
    }

    [Test]
    public async Task UnpreparedRepositoryDoesNotManufactureHumanAdmission()
    {
        var seed = await SeedAsync();
        await using var context = Open(PrimaryDatabaseRole.Runtime, seed.Tenant);
        await context.Database.OpenConnectionAsync();
        var lease = new ExploreDbContextExecutionLease();
        lease.Bind(context, seed.Tenant);
        await using var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var repository = new AiActorContextRepository(context, lease);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repository.GetAvailableActorsAsync());
        await Assert.That(context.ChangeTracker.Entries().Count()).IsEqualTo(0);
        await owner.RollbackAsync();
    }

    [Test]
    public async Task ExactMaximumSucceedsAndOverflowNeverReturnsTruncatedActors()
    {
        var seed = await SeedAsync();
        await using (var migration = fixture.CreateDbContext())
        {
            await migration.Database.OpenConnectionAsync();
            await using var setup = await migration.Database.BeginTransactionAsync();
            var names = new HumanPrincipalSourceSql(migration);
            string Field<T>(string property) => names.ColumnName(typeof(T), property);
            string Clone<T>(string templateKey, string keyProperty, string replacements, string dependency) =>
                $"""
                INSERT INTO {names.Table(typeof(T))}
                SELECT (pg_catalog.jsonb_populate_record(NULL::{names.Table(typeof(T))},
                    pg_catalog.to_jsonb(original) || pg_catalog.jsonb_build_object({replacements}))).*
                FROM {names.Table(typeof(T))} original CROSS JOIN ids {dependency}
                WHERE original.{names.Column(typeof(T), keyProperty)} = @{templateKey}
                RETURNING {names.Column(typeof(T), keyProperty)}
                """;
            await using var setupCommand = ((NpgsqlConnection)migration.Database.GetDbConnection()).CreateCommand();
            setupCommand.Transaction = (NpgsqlTransaction)setup.GetDbTransaction();
            // Copy the persisted seed's complete row shape, changing only aggregate and
            // relationship identities. Ordinary INSERT triggers and constraints still run.
            setupCommand.CommandText = $"""
                WITH ids AS MATERIALIZED (
                    SELECT pg_catalog.uuidv7() AS group_id, pg_catalog.uuidv7() AS actor_id,
                    pg_catalog.uuidv7() AS placement_id, pg_catalog.uuidv7() AS member_id
                    FROM pg_catalog.generate_series(1,997)
                ),
                written_groups AS ({Clone<Group>("group", "Id", $"'{Field<Group>("Id")}',ids.group_id", "")}),
                written_actors AS ({Clone<Actor>("actor", "Id",
                    $"'{Field<Actor>("Id")}',ids.actor_id,'{Field<Actor>("GroupId")}',ids.group_id",
                    $"JOIN written_groups ON written_groups.{names.Column(typeof(Group), "Id")} = ids.group_id")}),
                written_pii AS ({Clone<ActorPii>("actor", "ActorId", $"'{Field<ActorPii>("ActorId")}',ids.actor_id",
                    $"JOIN written_actors ON written_actors.{names.Column(typeof(Actor), "Id")} = ids.actor_id")}),
                written_placements AS ({Clone<GroupTenant>("placement", "Id",
                    $"'{Field<GroupTenant>("Id")}',ids.placement_id,'{Field<GroupTenant>("GroupId")}',ids.group_id",
                    $"JOIN written_groups ON written_groups.{names.Column(typeof(Group), "Id")} = ids.group_id")})
                {Clone<GroupMember>("member", "Id",
                    $"'{Field<GroupMember>("Id")}',ids.member_id,'{Field<GroupMember>("GroupTenantId")}',ids.placement_id",
                    $"JOIN written_placements ON written_placements.{names.Column(typeof(GroupTenant), "Id")} = ids.placement_id")};
                """;
            setupCommand.Parameters.AddWithValue("group", seed.Group);
            setupCommand.Parameters.AddWithValue("actor", seed.GroupActor);
            setupCommand.Parameters.AddWithValue("placement", seed.GroupPlacement);
            setupCommand.Parameters.AddWithValue("member", seed.GroupMember);
            await setupCommand.ExecuteNonQueryAsync();
            await setup.CommitAsync();
        }
        await Assert.That((await ReadFreshAsync(seed)).Count).IsEqualTo(1000);
        await using (var migration = fixture.CreateDbContext())
        {
            var user = await migration.Users.IgnoreAutoIncludes().SingleAsync(row => row.Id == seed.User);
            var tenant = await migration.Tenants.SingleAsync(row => row.Id == seed.Tenant);
            var role = await migration.Set<Role>().SingleAsync(row => row.Id == seed.GroupRole);
            AddGroup(migration, tenant, user, role);
            await migration.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => ReadFreshAsync(seed));
        await using var context = Open(PrimaryDatabaseRole.Runtime, seed.Tenant);
        await context.Database.OpenConnectionAsync();
        await using var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await using var command = NativeCommand(context, seed.Tenant, seed.User);
        var native = await Assert.ThrowsAsync<PostgresException>(async () => await command.ExecuteScalarAsync());
        await Assert.That(native!.SqlState).IsEqualTo(PostgresErrorCodes.ProgramLimitExceeded);
        await Assert.That(native.MessageText).IsEqualTo("ai_actor_context_overflow");
        await owner.RollbackAsync();
    }

    private ExploreDbContext Open(PrimaryDatabaseRole role, Guid tenant)
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder, fixture.GetNativeQueueOptions(role));
        builder.AddInterceptors(role == PrimaryDatabaseRole.Runtime
            ? PostgresTenantSessionInterceptor.RuntimeInstance : PostgresTenantSessionInterceptor.Instance);
        builder.UseNpgsql(options => options.ExecutionStrategy(dependencies => new NonRetryingExecutionStrategy(dependencies)));
        return new(builder.Options) { TenantContext = new SelectedTenant(tenant) };
    }

    private Task<IReadOnlyList<Actor>> ReadAsync(ExploreDbContext context, Seed seed) =>
        PostgresAiActorContextReadContract.ReadAsync(context, fixture.NativeQueueEnrollment, seed.Tenant, seed.User, default);

    private async Task<IReadOnlyList<Actor>> ReadFreshAsync(Seed seed)
    {
        await using var context = Open(PrimaryDatabaseRole.Runtime, seed.Tenant);
        await context.Database.OpenConnectionAsync();
        await using var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var rows = await ReadAsync(context, seed);
        await owner.RollbackAsync();
        return rows;
    }

    private static NpgsqlCommand NativeCommand(ExploreDbContext context, Guid tenant, Guid principal)
    {
        var command = ((NpgsqlConnection)context.Database.GetDbConnection()).CreateCommand();
        command.Transaction = (NpgsqlTransaction?)context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"SELECT * FROM {PostgresAiActorContextReadContract.FunctionSql(context)}(@tenant,@principal)";
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("principal", principal);
        return command;
    }

    private async Task<Seed> SeedAsync(int groupCount = 1)
    {
        await using var context = fixture.CreateDbContext();
        await PostgresAiActorContextReadContract.ApplyAsync(context, fixture.NativeQueueEnrollment);
        return await SeedRowsAsync(context, groupCount);
    }

    private static async Task<Seed> SeedRowsAsync(ExploreDbContext context, int groupCount = 1)
    {
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "Actor context",
            Slug = Guid.CreateVersion7().ToString("N"),
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        var user = new User { Id = Guid.CreateVersion7(), Pii = null! };
        var personal = new Actor
        {
            Id = Guid.CreateVersion7(),
            User = user,
            UserId = user.Id,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = "Original personal display" }
        };
        int nextRole = await context.Set<Role>().MaxAsync(role => role.Id) + 1;
        var organizationRole = new Role
        {
            Id = nextRole,
            MasterCode = $"actor-context-{Guid.CreateVersion7():N}",
            FullName = "Organization capability",
            RoleScopeId = (int)RoleScopeEnum.Organization
        };
        var groupRole = new Role
        {
            Id = nextRole + 1,
            MasterCode = $"actor-context-{Guid.CreateVersion7():N}",
            FullName = "Group capability",
            RoleScopeId = (int)RoleScopeEnum.Group
        };
        var permission = await context.Set<Permission>().SingleAsync(row => row.MasterCode == PermissionCodes.EventCreate);
        context.AddRange(tenant, user, personal, organizationRole, groupRole,
            new RolePermission { Role = organizationRole, RoleId = organizationRole.Id, Permission = permission, PermissionId = permission.Id },
            new RolePermission { Role = groupRole, RoleId = groupRole.Id, Permission = permission, PermissionId = permission.Id },
            new TenantUser
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenant.Id,
                Tenant = tenant,
                UserId = user.Id,
                User = user,
                ActorId = personal.Id,
                Actor = personal,
                StatusId = (int)TenantUserStatusEnum.Active
            });
        var organization = new Organization
        {
            Id = Guid.CreateVersion7(),
            Pii = new OrganizationPii { FullName = "Organization display", Email = "not-returned@example.invalid", Address = "Not returned" }
        };
        var organizationActor = new Actor
        {
            Id = Guid.CreateVersion7(),
            Organization = organization,
            OrganizationId = organization.Id,
            ActorTypeId = (int)ActorTypeEnum.Organization,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = "Organization actor display" }
        };
        var placement = new OrganizationTenant
        {
            Id = Guid.CreateVersion7(),
            Organization = organization,
            OrganizationId = organization.Id,
            Tenant = tenant,
            TenantId = tenant.Id,
            ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
            ApprovalStatus = null!
        };
        var member = new OrganizationMember
        {
            Id = Guid.CreateVersion7(),
            OrganizationTenant = placement,
            OrganizationTenantId = placement.Id,
            Tenant = tenant,
            TenantId = tenant.Id,
            User = user,
            UserId = user.Id,
            Role = organizationRole,
            RoleId = organizationRole.Id
        };
        context.AddRange(organization, organizationActor, placement, member);
        (Guid Group, Guid Actor, Guid Placement, Guid Member) first = default;
        for (int i = 0; i < groupCount; i++)
        {
            var created = AddGroup(context, tenant, user, groupRole);
            if (i == 0) first = created;
        }
        await context.SaveChangesAsync();
        return new(tenant.Id, user.Id, personal.Id, organization.Id, organizationActor.Id, placement.Id,
            member.Id, first.Group, first.Actor, first.Placement, first.Member, organizationRole.Id, groupRole.Id);
    }

    private static (Guid Group, Guid Actor, Guid Placement, Guid Member) AddGroup(
        ExploreDbContext context, Tenant tenant, User user, Role role)
    {
        var group = new Group { Id = Guid.CreateVersion7(), FullName = "Group display" };
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            Group = group,
            GroupId = group.Id,
            ActorTypeId = (int)ActorTypeEnum.Group,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = "Group actor display" }
        };
        var placement = new GroupTenant
        {
            Id = Guid.CreateVersion7(),
            Group = group,
            GroupId = group.Id,
            Tenant = tenant,
            TenantId = tenant.Id,
            ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
            ApprovalStatus = null!
        };
        var member = new GroupMember
        {
            Id = Guid.CreateVersion7(),
            GroupTenant = placement,
            GroupTenantId = placement.Id,
            Tenant = tenant,
            TenantId = tenant.Id,
            User = user,
            UserId = user.Id,
            Role = role,
            RoleId = role.Id
        };
        context.AddRange(group, actor, placement, member);
        return (group.Id, actor.Id, placement.Id, member.Id);
    }

    private sealed record Seed(Guid Tenant, Guid User, Guid Personal, Guid Organization, Guid OrganizationActor,
        Guid OrganizationPlacement, Guid OrganizationMember, Guid Group, Guid GroupActor, Guid GroupPlacement,
        Guid GroupMember, int OrganizationRole, int GroupRole);
    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;
}

using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class PostgresEventCreationPublisherReadContractTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task RuntimeReturnsOwnUnavailableMembershipWithoutActorOrRoleFallback()
    {
        var seed = await SeedAsync();
        await using var context = Open(PrimaryDatabaseRole.Runtime, seed.Tenant);
        await context.Database.OpenConnectionAsync();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var result = await PostgresEventCreationPublisherReadContract.ReadAsync(
            context, fixture.NativeQueueEnrollment, seed.Tenant, seed.User, default);
        await Assert.That(result.Groups.Count).IsEqualTo(1);
        await Assert.That(result.Groups.Single().Id).IsEqualTo(seed.Member);
        await Assert.That(result.Groups.Single().GroupTenant.Group.FullName).IsEqualTo("Unavailable native group");
        await Assert.That(result.Groups.Single().RoleId).IsEqualTo(seed.Role);
        await Assert.That(result.Groups.Single().GroupTenant.Group.Actor).IsNull();
        await Assert.That(result.Groups.Single().User).IsNull();
        await Assert.That(result.Permissions.Count).IsEqualTo(0);
        await Assert.That(context.ChangeTracker.Entries().Count()).IsEqualTo(0);
        await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task OtherUserAndTenantCannotAcquireTheMembership()
    {
        var seed = await SeedAsync();
        var foreign = await SeedAsync();
        foreach (var target in new[] { (seed.Tenant, foreign.User), (foreign.Tenant, seed.User) })
        {
            await using var context = Open(PrimaryDatabaseRole.Runtime, target.Item1);
            await context.Database.OpenConnectionAsync();
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var result = await PostgresEventCreationPublisherReadContract.ReadAsync(
                context, fixture.NativeQueueEnrollment, target.Item1, target.Item2, default);
            await Assert.That(result.Organizations.Count + result.Groups.Count + result.Permissions.Count).IsEqualTo(0);
            await transaction.RollbackAsync();
        }
    }

    [Test]
    [Arguments("member")]
    [Arguments("placement")]
    [Arguments("owner")]
    [Arguments("tenant-membership")]
    public async Task DeletedMembershipSourcesCannotReturnOptions(string source)
    {
        var seed = await SeedAsync();
        await using var setup = fixture.CreateDbContext();
        switch (source)
        {
            case "member":
                await setup.GroupMembers.Where(row => row.Id == seed.Member)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
                break;
            case "placement":
                await setup.GroupTenants.Where(row => row.Id == seed.Placement)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
                break;
            case "owner":
                await setup.Groups.Where(row => row.Id == seed.Group)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
                break;
            case "tenant-membership":
                await setup.TenantUsers.Where(row => row.TenantId == seed.Tenant && row.UserId == seed.User)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(source));
        }
        await using var context = Open(PrimaryDatabaseRole.Runtime, seed.Tenant);
        await context.Database.OpenConnectionAsync();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var result = await PostgresEventCreationPublisherReadContract.ReadAsync(
            context, fixture.NativeQueueEnrollment, seed.Tenant, seed.User, default);
        await Assert.That(result.Groups.Count).IsEqualTo(0);
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task RuntimeAndGlobalRetainNoPrivateMembershipReadOrDefinerPromotion()
    {
        var seed = await SeedAsync();
        foreach (var purpose in new[] { PrimaryDatabaseRole.Runtime, PrimaryDatabaseRole.Global })
        {
            await using var context = Open(purpose, seed.Tenant);
            await context.Database.OpenConnectionAsync();
            var names = new HumanPrincipalSourceSql(context);
            foreach (Type type in new[] { typeof(OrganizationMember), typeof(GroupMember), typeof(OrganizationPii), typeof(RolePermission) })
            {
                var error = await Assert.ThrowsAsync<PostgresException>(() =>
                    context.Database.ExecuteSqlRawAsync($"SELECT * FROM {names.Table(type)} LIMIT 1"));
                await Assert.That(error!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
            }
            var promotion = await Assert.ThrowsAsync<PostgresException>(() =>
                context.Database.ExecuteSqlRawAsync($"SET ROLE {PostgresEventCreationPublisherReadContract.OwnerRole}"));
            await Assert.That(promotion!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        }
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Global)]
    [Arguments(PrimaryDatabaseRole.Migrator)]
    public async Task OtherPurposesCannotExecuteNativeRead(PrimaryDatabaseRole purpose)
    {
        var seed = await SeedAsync();
        await using var context = Open(purpose, seed.Tenant);
        await context.Database.OpenConnectionAsync();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlRawAsync(
                $"SELECT * FROM {PostgresEventCreationPublisherReadContract.FunctionSql(context)}({{0}},{{1}})", seed.Tenant, seed.User));
        await Assert.That(error!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task UnpreparedRepositoryAndNonSerializableReadFailClosed()
    {
        var seed = await SeedAsync();
        await using var context = Open(PrimaryDatabaseRole.Runtime, seed.Tenant);
        await context.Database.OpenConnectionAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => PostgresEventCreationPublisherReadContract.ReadAsync(
            context, fixture.NativeQueueEnrollment, seed.Tenant, seed.User, default));
        var lease = new ExploreDbContextExecutionLease();
        lease.Bind(context, seed.Tenant);
        await using (var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new EventCreationPublisherRepository(context, lease).ReadAsync());
            await transaction.RollbackAsync();
        }
        var install = await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresEventCreationPublisherReadContract.ApplyAsync(context, fixture.NativeQueueEnrollment));
        await Assert.That(install!.MessageText).IsEqualTo("event_creation_publisher_migrator_required");
    }

    [Test]
    [Arguments("")]
    [Arguments("018e4e5c-7f00-7000-8000-000000000002")]
    public async Task ChangedSessionTenantCannotReuseTheBoundOwner(string selected)
    {
        var seed = await SeedAsync();
        await using var context = Open(PrimaryDatabaseRole.Runtime, seed.Tenant);
        await context.Database.OpenConnectionAsync();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await context.Database.ExecuteSqlRawAsync("SELECT pg_catalog.set_config('app.current_tenant_id',{0},true)", selected);
        var error = await Assert.ThrowsAsync<PostgresException>(() => PostgresEventCreationPublisherReadContract.ReadAsync(
            context, fixture.NativeQueueEnrollment, seed.Tenant, seed.User, default));
        await Assert.That(error!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments("body", "event_creation_publisher_body_drift")]
    [Arguments("search-path", "native_privilege_guard_not_ready")]
    [Arguments("acl", "native_privilege_guard_not_ready")]
    [Arguments("policy", "event_creation_publisher_policy_drift")]
    [Arguments("column", "event_creation_publisher_source_catalog_drift")]
    public async Task InstallerRejectsDriftWithoutRepairingIt(string mutation, string reason)
    {
        await using var setup = fixture.CreateDbContext();
        await PostgresEventCreationPublisherReadContract.ApplyAsync(setup, fixture.NativeQueueEnrollment);
        await using var transaction = await setup.Database.BeginTransactionAsync();
        var names = new HumanPrincipalSourceSql(setup);
        string function = PostgresEventCreationPublisherReadContract.FunctionSql(setup);
        string change = mutation switch
        {
            "body" => $"""
                CREATE OR REPLACE FUNCTION {function}(p_tenant uuid,p_principal uuid)
                    RETURNS TABLE(is_organization boolean,membership_id uuid,placement_id uuid,publisher_id uuid,
                        publisher_role_id integer,display_name text,event_create_permission_id integer)
                    LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                    AS $drift$ BEGIN RETURN; END; $drift$
                """,
            "search-path" => $"ALTER FUNCTION {function}(uuid,uuid) SET search_path = public,pg_catalog",
            "acl" => $"GRANT EXECUTE ON FUNCTION {function}(uuid,uuid) TO PUBLIC",
            "policy" => $"ALTER POLICY event_creation_publisher_read ON {names.Table(typeof(GroupMember))} USING (true)",
            "column" => $"ALTER TABLE {names.Table(typeof(GroupMember))} ADD COLUMN publisher_unexpected integer",
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        await setup.Database.ExecuteSqlRawAsync(change);
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresEventCreationPublisherReadContract.ApplyAsync(setup, fixture.NativeQueueEnrollment));
        await Assert.That(error!.SqlState).IsEqualTo(PostgresErrorCodes.ObjectNotInPrerequisiteState);
        await Assert.That(error.MessageText).IsEqualTo(reason);
        await transaction.RollbackAsync();
    }

    private ExploreDbContext Open(PrimaryDatabaseRole purpose, Guid tenant)
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder, fixture.GetNativeQueueOptions(purpose));
        builder.AddInterceptors(purpose == PrimaryDatabaseRole.Runtime
            ? PostgresTenantSessionInterceptor.RuntimeInstance : PostgresTenantSessionInterceptor.Instance);
        builder.UseNpgsql(options => options.ExecutionStrategy(dependencies => new NonRetryingExecutionStrategy(dependencies)));
        return new(builder.Options) { TenantContext = new SelectedTenant(tenant) };
    }

    private async Task<Seed> SeedAsync()
    {
        await using var setup = fixture.CreateDbContext();
        await PostgresEventCreationPublisherReadContract.ApplyAsync(setup, fixture.NativeQueueEnrollment);
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "Native publisher test",
            Slug = Guid.CreateVersion7().ToString("N"),
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        var user = new User { Id = Guid.CreateVersion7(), Pii = null! };
        var role = new Role
        {
            Id = await setup.Set<Role>().MaxAsync(row => row.Id) + 1,
            MasterCode = $"publisher-{Guid.CreateVersion7():N}",
            FullName = "No publishing permission",
            RoleScopeId = (int)RoleScopeEnum.Group
        };
        var group = new Group { Id = Guid.CreateVersion7(), FullName = "Unavailable native group" };
        var placement = new GroupTenant
        {
            Id = Guid.CreateVersion7(),
            Group = group,
            GroupId = group.Id,
            Tenant = tenant,
            TenantId = tenant.Id,
            ApprovalStatusId = (int)ApprovalStatusEnum.Pending,
            ApprovalStatus = null!,
            IsSuspended = true
        };
        var member = new GroupMember
        {
            Id = Guid.CreateVersion7(),
            GroupTenantId = placement.Id,
            GroupTenant = placement,
            Tenant = tenant,
            TenantId = tenant.Id,
            User = user,
            UserId = user.Id,
            Role = role,
            RoleId = role.Id
        };
        setup.AddRange(member, new TenantUser
        {
            Id = Guid.CreateVersion7(),
            Tenant = tenant,
            TenantId = tenant.Id,
            User = user,
            UserId = user.Id,
            StatusId = (int)TenantUserStatusEnum.Active
        });
        await setup.SaveChangesAsync();
        return new(tenant.Id, user.Id, group.Id, placement.Id, member.Id, role.Id);
    }

    private sealed record Seed(Guid Tenant, Guid User, Guid Group, Guid Placement, Guid Member, int Role);
    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;
}

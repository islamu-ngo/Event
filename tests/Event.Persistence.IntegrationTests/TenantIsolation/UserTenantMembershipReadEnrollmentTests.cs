using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class UserTenantMembershipReadEnrollmentTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task GlobalEfGraphPreservesExactUserAndActiveMembershipSemantics()
    {
        var rows = await SeedAsync();
        await using var global = await GlobalFactory().CreateDbContextAsync();
        var result = await global.TenantUsers
            .IgnoreTenantFilter(TenantFilterBypassReasons.UserTenantMembershipEnumeration)
            .IgnoreAutoIncludes().AsNoTracking()
            .Where(member => member.UserId == rows.User && !member.IsDeleted
                && member.StatusId == (int)TenantUserStatusEnum.Active
                && member.Tenant.TenantStatusId == (int)TenantStatusEnum.Active)
            .Select(member => new TenantUser
            {
                Id = member.Id,
                TenantId = member.TenantId,
                UserId = member.UserId,
                User = null!,
                ActorId = member.ActorId,
                StatusId = member.StatusId,
                JoinedAt = member.JoinedAt,
                SuspendedAt = member.SuspendedAt,
                SuspendedBy = member.SuspendedBy,
                BanExpiresAt = member.BanExpiresAt,
                RemovedAt = member.RemovedAt,
                RemovedBy = member.RemovedBy,
                ModerationNote = member.ModerationNote,
                CreatedAt = member.CreatedAt,
                CreatedBy = member.CreatedBy,
                UpdatedAt = member.UpdatedAt,
                UpdatedBy = member.UpdatedBy,
                IsDeleted = member.IsDeleted,
                DeletedAt = member.DeletedAt,
                DeletedBy = member.DeletedBy,
                Tenant = new Tenant
                {
                    Id = member.Tenant.Id,
                    FullName = member.Tenant.FullName,
                    Slug = member.Tenant.Slug,
                    Description = member.Tenant.Description,
                    TenantStatusId = member.Tenant.TenantStatusId,
                    CreatedAt = member.Tenant.CreatedAt,
                    CreatedBy = member.Tenant.CreatedBy,
                    UpdatedAt = member.Tenant.UpdatedAt,
                    UpdatedBy = member.Tenant.UpdatedBy,
                    TenantStatus = new TenantStatus
                    {
                        Id = member.Tenant.TenantStatus.Id,
                        MasterCode = member.Tenant.TenantStatus.MasterCode,
                        FullName = member.Tenant.TenantStatus.FullName,
                        Description = member.Tenant.TenantStatus.Description,
                        IsActiveState = member.Tenant.TenantStatus.IsActiveState
                    }
                }
            }).ToListAsync();

        await Assert.That(result.Select(member => member.TenantId)).IsEquivalentTo([rows.A, rows.B]);
        await Assert.That(result.All(member => member.UserId == rows.User && member.User is null
            && member.Tenant.Id == member.TenantId && member.Tenant.Slug == $"directory-{member.TenantId:N}"
            && member.Tenant.TenantStatus.Id == (int)TenantStatusEnum.Active)).IsTrue();
        await Assert.That(result.Single(member => member.TenantId == rows.A).ModerationNote).IsEqualTo("original membership note");
        await Assert.That(global.ChangeTracker.Entries().Any()).IsFalse();
        global.RequireBoundTenantLease();
        await Assert.That(global.TenantFilterTenantId).IsNull();
    }

    [Test]
    [Arguments("membership")]
    [Arguments("tenant")]
    [Arguments("status")]
    public async Task GlobalReadInventoryIsFixedDirectAndNondelegable(string source)
    {
        await using var global = await GlobalFactory().CreateDbContextAsync();
        var (type, properties) = source switch
        {
            "membership" => (typeof(TenantUser), new[] { "Id", "TenantId", "UserId", "ActorId", "StatusId", "JoinedAt",
                "SuspendedAt", "SuspendedBy", "BanExpiresAt", "RemovedAt", "RemovedBy", "ModerationNote", "CreatedAt",
                "CreatedBy", "UpdatedAt", "UpdatedBy", "IsDeleted", "DeletedAt", "DeletedBy" }),
            "tenant" => (typeof(Tenant), new[] { "Id", "FullName", "Slug", "Description", "TenantStatusId", "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy" }),
            "status" => (typeof(TenantStatus), new[] { "Id", "MasterCode", "FullName", "Description", "IsActiveState" }),
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };
        var entity = global.Model.FindEntityType(type)!;
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        string[] columns = [.. properties.Select(property => entity.FindProperty(property)!.GetColumnName(store)!)];
        await global.Database.OpenConnectionAsync();
        await using var command = global.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT NOT pg_catalog.has_table_privilege(current_user,@table,'SELECT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                AND NOT pg_catalog.has_any_column_privilege(current_user,@table,'UPDATE,REFERENCES')
                AND NOT EXISTS (SELECT 1 FROM unnest(@columns) expected(name)
                    WHERE NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                        CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                        WHERE a.attrelid=pg_catalog.to_regclass(@table) AND a.attname=expected.name
                            AND acl.grantee=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname=current_user)
                            AND acl.grantor=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname=@migrator)
                            AND acl.privilege_type='SELECT' AND NOT acl.is_grantable))
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                    WHERE a.attrelid=pg_catalog.to_regclass(@table) AND a.attnum<>0 AND NOT a.attisdropped
                        AND (pg_catalog.has_column_privilege(current_user,a.attrelid,a.attnum,'SELECT WITH GRANT OPTION')
                            OR (NOT (a.attname=ANY(@columns)) AND pg_catalog.has_column_privilege(current_user,a.attrelid,a.attnum,'SELECT'))))
            """;
        command.Parameters.Add(new NpgsqlParameter("table", Table(entity)));
        command.Parameters.Add(new NpgsqlParameter("columns", columns));
        command.Parameters.Add(new NpgsqlParameter("migrator", fixture.NativeQueueEnrollment.MigratorLogin));
        await Assert.That(await command.ExecuteScalarAsync()).IsEqualTo(true);
    }

    [Test]
    public async Task DeploymentHasNoMembershipBusinessRoutineOrCapabilityRoles()
    {
        await using var context = OpenMigrator();
        await context.Database.OpenConnectionAsync();
        var entity = context.Model.FindEntityType(typeof(TenantUser))!;
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT pg_catalog.to_regprocedure(@routine) IS NULL
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname IN
                    ('event_user_membership_owner','event_user_membership_global','event_user_membership_migrator'))
            """;
        command.Parameters.Add(new NpgsqlParameter("routine",
            $"{Quote(entity.GetSchema() ?? "public")}.{Quote(entity.GetTableName() + "_active_for_user")}(uuid)"));
        await Assert.That(await command.ExecuteScalarAsync()).IsEqualTo(true);
    }

    [Test]
    public async Task DirectoryReadDoesNotGrantPrivateUserProfileAccess()
    {
        await using var global = await GlobalFactory().CreateDbContextAsync();
        var denied = await Assert.ThrowsAsync<PostgresException>(() => global.Users.IgnoreAutoIncludes()
            .AsNoTracking().Where(user => user.Id == Guid.Empty).Select(user => user.Pii.Email).ToListAsync());
        await Assert.That(denied.SqlState).IsEqualTo("42501");
    }

    [Test]
    public async Task RuntimeMembershipReadRetainsSelectedTenantIsolation()
    {
        var rows = await SeedAsync();
        await using var runtime = fixture.CreateEnrolledTenantDbContext(new SelectedTenant(rows.A));
        var result = await runtime.TenantUsers.IgnoreQueryFilters().AsNoTracking()
            .Where(member => member.UserId == rows.User).Select(member => member.TenantId).ToListAsync();
        await Assert.That(result).IsEquivalentTo([rows.A]);
        await Assert.That(runtime.TenantFilterTenantId).IsEqualTo(rows.A);
    }

    private async Task<(Guid User, Guid A, Guid B)> SeedAsync()
    {
        await using var strategyContext = OpenMigrator();
        return await strategyContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            Guid user = Guid.CreateVersion7(), other = Guid.CreateVersion7();
            Guid a = Guid.CreateVersion7(), b = Guid.CreateVersion7(), inactive = Guid.CreateVersion7(), suspendedMember = Guid.CreateVersion7(), deletedMember = Guid.CreateVersion7();
            await using var context = OpenMigrator();
            await using var gate = new EventDiscoveryEnrollmentGate(context);
            await using var custody = await gate.AcquireAsync();
            context.Users.AddRange(CreateUser(user), CreateUser(other));
            context.Tenants.AddRange(CreateTenant(a), CreateTenant(b), CreateTenant(inactive, TenantStatusEnum.Suspended),
                CreateTenant(suspendedMember), CreateTenant(deletedMember));
            context.TenantUsers.AddRange(CreateMember(a, user, note: "original membership note"), CreateMember(b, user),
                CreateMember(inactive, user), CreateMember(b, other),
                CreateMember(suspendedMember, user, TenantUserStatusEnum.Suspended), CreateMember(deletedMember, user, deleted: true));
            await context.SaveChangesAsync();
            return (user, a, b);
        });
    }

    private ExploreDbContext OpenMigrator()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        var context = new ExploreDbContext(options.Options);
        context.BindTenantLease();
        return context;
    }

    private GlobalExploreDbContextFactory GlobalFactory() => new(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
    private static User CreateUser(Guid id) => new() { Id = id, Pii = null!, ConcurrencyStamp = Guid.CreateVersion7() };
    private static Tenant CreateTenant(Guid id, TenantStatusEnum status = TenantStatusEnum.Active) => new()
    { Id = id, FullName = "Directory tenant", Slug = $"directory-{id:N}", TenantStatusId = (int)status, TenantStatus = null! };
    private static TenantUser CreateMember(Guid tenant, Guid user, TenantUserStatusEnum status = TenantUserStatusEnum.Active,
        bool deleted = false, string? note = null) => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant,
            UserId = user,
            Tenant = null!,
            User = null!,
            StatusId = (int)status,
            JoinedAt = new DateTime(2028, 4, 5, 12, 0, 0, DateTimeKind.Utc),
            IsDeleted = deleted,
            ModerationNote = note
        };
    private static string Table(IEntityType entity) => $"{Quote(entity.GetSchema() ?? "public")}.{Quote(entity.GetTableName()!)}";
    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;
}

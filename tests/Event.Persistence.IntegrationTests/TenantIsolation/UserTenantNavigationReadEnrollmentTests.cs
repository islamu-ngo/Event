using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class UserTenantNavigationReadEnrollmentTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task EfFirstGlobalReadsOnlySelectedLiveNavigationFacts()
    {
        Guid subject = Guid.CreateVersion7(), other = Guid.CreateVersion7(), deleted = Guid.CreateVersion7();
        Guid preference = Guid.CreateVersion7();
        await using (var seed = Open())
        {
            seed.Users.AddRange(CreateUser(subject, preference), CreateUser(other, null), CreateUser(deleted, preference, deleted: true));
            await seed.SaveChangesAsync();
        }
        var factory = GlobalFactory();
        await using var global = await factory.CreateDbContextAsync();
        var selected = await global.Users.IgnoreAutoIncludes().AsNoTracking()
            .Where(user => user.Id == subject && !user.IsDeleted)
            .Select(user => new User
            {
                Id = user.Id, IsDeleted = user.IsDeleted, LastActiveTenantId = user.LastActiveTenantId, Pii = null!
            }).SingleOrDefaultAsync();
        await Assert.That(selected?.Id).IsEqualTo(subject);
        await Assert.That(selected!.LastActiveTenantId).IsEqualTo(preference);
        await Assert.That(selected.Pii).IsNull();
        await Assert.That(global.ChangeTracker.Entries().Any()).IsFalse();

        var repository = new UserTenantNavigationRepository(factory, fixture.NativeQueueEnrollment);
        await Assert.That((await repository.GetNavigationUserAsync(subject, default))?.LastActiveTenantId).IsEqualTo(preference);
        var different = await repository.GetNavigationUserAsync(other, default);
        await Assert.That(different?.Id).IsEqualTo(other);
        await Assert.That(different!.LastActiveTenantId).IsNull();
        await Assert.That(await repository.GetNavigationUserAsync(deleted, default)).IsNull();
        await Assert.That(await repository.GetNavigationUserAsync(Guid.CreateVersion7(), default)).IsNull();
    }

    [Test]
    public async Task EfFirstDeploymentInstallsNoNavigationBusinessRoutineOrCapabilityRoles()
    {
        await using var context = Open();
        await context.Database.OpenConnectionAsync();
        var entity = context.Model.FindEntityType(typeof(User))!;
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT pg_catalog.to_regprocedure(@routine) IS NULL
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles
                    WHERE rolname IN ('event_user_navigation_owner','event_user_navigation_global','event_user_navigation_migrator'))
            """;
        command.Parameters.Add(new NpgsqlParameter("routine", $"{Quote(entity.GetSchema() ?? "public")}.\"user_tenant_navigation_read\"(uuid)"));
        await Assert.That(await command.ExecuteScalarAsync()).IsEqualTo(true);
    }

    [Test]
    public async Task EfFirstGlobalHasExactlyThreeNondelegableUserColumnReads()
    {
        await using var global = await GlobalFactory().CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        var entity = global.Model.FindEntityType(typeof(User))!;
        var mapping = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        string table = $"{Quote(entity.GetSchema() ?? "public")}.{Quote(entity.GetTableName()!)}";
        string[] columns = [.. new[] { nameof(User.Id), nameof(User.IsDeleted), nameof(User.LastActiveTenantId) }
            .Select(property => entity.FindProperty(property)!.GetColumnName(mapping)!)];
        await using var command = global.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT NOT pg_catalog.has_table_privilege(current_user,@table,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                AND NOT pg_catalog.has_any_column_privilege(current_user,@table,'INSERT,UPDATE,REFERENCES')
                AND NOT EXISTS (SELECT 1 FROM unnest(@columns) expected(name)
                    WHERE NOT pg_catalog.has_column_privilege(current_user,@table,expected.name,'SELECT'))
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute attribute
                    WHERE attribute.attrelid=pg_catalog.to_regclass(@table) AND attribute.attnum<>0 AND NOT attribute.attisdropped
                        AND (pg_catalog.has_column_privilege(current_user,attribute.attrelid,attribute.attnum,'SELECT WITH GRANT OPTION')
                            OR (NOT (attribute.attname=ANY(@columns))
                                AND pg_catalog.has_column_privilege(current_user,attribute.attrelid,attribute.attnum,'SELECT'))))
            """;
        command.Parameters.Add(new NpgsqlParameter("table", table));
        command.Parameters.Add(new NpgsqlParameter("columns", columns));
        await Assert.That(await command.ExecuteScalarAsync()).IsEqualTo(true);
    }

    [Test]
    [Arguments("audit")]
    [Arguments("profile")]
    [Arguments("write")]
    public async Task EfFirstGlobalCannotReadPrivateUserDataOrChangePreference(string operation)
    {
        await using var global = await GlobalFactory().CreateDbContextAsync();
        var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            var users = global.Users.IgnoreAutoIncludes().AsNoTracking().Where(user => user.Id == Guid.Empty);
            if (operation == "audit") _ = await users.Select(user => user.CreatedAt).ToListAsync();
            else if (operation == "profile") _ = await users.Select(user => user.Pii.Email).ToListAsync();
            else _ = await users.ExecuteUpdateAsync(setters => setters.SetProperty(user => user.LastActiveTenantId, (Guid?)null));
        });
        await Assert.That(denied.SqlState).IsEqualTo("42501");
    }

    [Test]
    public async Task EfFirstRuntimeCannotReadTheGlobalNavigationColumns()
    {
        await using var runtime = fixture.CreateEnrolledTenantlessDbContext();
        await Assert.That(await runtime.Database.SqlQueryRaw<string>("SELECT session_user::text AS \"Value\"")
            .SingleAsync()).IsEqualTo(fixture.NativeQueueEnrollment.RuntimeLogin);
        var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
            await runtime.Users.IgnoreAutoIncludes().AsNoTracking()
                .Select(user => new { user.Id, user.IsDeleted, user.LastActiveTenantId }).ToListAsync());
        await Assert.That(denied.SqlState).IsEqualTo("42501");
    }

    private GlobalExploreDbContextFactory GlobalFactory() => new(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));

    private ExploreDbContext Open()
    {
        var options = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        return new ExploreDbContext(options.Options);
    }

    private static User CreateUser(Guid id, Guid? preference, bool deleted = false) => new()
    {
        Id = id, LastActiveTenantId = preference, IsDeleted = deleted,
        ConcurrencyStamp = Guid.CreateVersion7(), CreatedAt = DateTime.UtcNow, Pii = null!
    };

    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}

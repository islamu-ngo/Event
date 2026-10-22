using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Repositories;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class UserMembershipReadPortTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExactUserProjectionDoesNotBorrowAbsentOrForeignRequestTenancy(bool foreignTenant)
    {
        var rows = await SeedAsync();
        await using var request = foreignTenant
            ? fixture.CreateEnrolledTenantDbContext(new FixedTenant(rows.Inactive))
            : fixture.CreateEnrolledTenantlessDbContext();
        request.BindTenantLease();
        var repository = new UserTenantMembershipRepository(GlobalFactory(),
            new TenantExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime)),
            fixture.NativeQueueEnrollment);
        var result = await repository.GetActiveTenantsForUserAsync(rows.User);
        await Assert.That(result.Select(row => row.TenantId)).IsEquivalentTo([rows.A, rows.B]);
        await Assert.That(result.All(row => row.UserId == rows.User
            && row.StatusId == (int)TenantUserStatusEnum.Active
            && row.Tenant.Slug == $"membership-{row.TenantId:N}"
            && row.Tenant.TenantStatus.Id == (int)TenantStatusEnum.Active)).IsTrue();
        await Assert.That(await repository.GetActiveTenantsForUserAsync(Guid.CreateVersion7())).IsEmpty();
        await Assert.That(await repository.GetActiveTenantsForUserAsync(Guid.Empty)).IsEmpty();
        await Assert.That(request.TenantFilterTenantId).IsEqualTo(foreignTenant ? rows.Inactive : (Guid?)null);
        await request.Database.OpenConnectionAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PostgresUserTenantMembershipReadPermissions.RequireReadyAsync(request, fixture.NativeQueueEnrollment));
    }

    [Test]
    [Arguments("global-table-read")]
    [Arguments("global-write")]
    [Arguments("global-grant-option")]
    [Arguments("global-system-column")]
    [Arguments("global-flags")]
    [Arguments("global-schema-create")]
    [Arguments("runtime-via-login")]
    [Arguments("runtime-set-only")]
    [Arguments("runtime-admin-only")]
    [Arguments("global-set-only-read")]
    [Arguments("global-admin-only-read")]
    [Arguments("runtime-owner")]
    public async Task AuthorityDriftFailsEntryAndInstallerWithoutRepair(string drift)
    {
        await using var strategy = OpenMigrator();
        await strategy.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var context = OpenMigrator();
            await using var transaction = await context.Database.BeginTransactionAsync();
            var enrollment = fixture.NativeQueueEnrollment;
            var entity = context.Model.FindEntityType(typeof(TenantUser))!;
            string table = Table(entity), schema = Quote(entity.GetSchema() ?? "public");
            var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            string id = Quote(entity.FindProperty(nameof(TenantUser.Id))!.GetColumnName(store)!);
            string note = Quote(entity.FindProperty(nameof(TenantUser.ModerationNote))!.GetColumnName(store)!);
            string global = Quote(enrollment.GlobalLogin), runtime = Quote(enrollment.RuntimeLogin);
            string dormant = Quote("membership_drift_" + Guid.CreateVersion7().ToString("N"));
            string change = drift switch
            {
                "global-table-read" => $"GRANT SELECT ON {table} TO {global}",
                "global-write" => $"GRANT UPDATE ({note}) ON {table} TO {global}",
                "global-grant-option" => $"GRANT SELECT ({id}) ON {table} TO {global} WITH GRANT OPTION",
                "global-system-column" => $"GRANT SELECT (xmin) ON {table} TO {global}",
                "global-flags" => $"ALTER ROLE {global} BYPASSRLS",
                "global-schema-create" => $"GRANT CREATE ON SCHEMA {schema} TO {global}",
                "runtime-via-login" => $"GRANT {global} TO {runtime} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE",
                "runtime-set-only" => $"GRANT {global} TO {runtime} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE",
                "runtime-admin-only" => $"GRANT {global} TO {runtime} WITH INHERIT FALSE, SET FALSE, ADMIN TRUE",
                "global-set-only-read" => $"CREATE ROLE {dormant} NOLOGIN; GRANT SELECT ({id}) ON {table} TO {dormant}; GRANT {dormant} TO {global} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE",
                "global-admin-only-read" => $"CREATE ROLE {dormant} NOLOGIN; GRANT SELECT ({id}) ON {table} TO {dormant}; GRANT {dormant} TO {global} WITH INHERIT FALSE, SET FALSE, ADMIN TRUE",
                "runtime-owner" => $"CREATE ROLE {dormant} NOLOGIN; GRANT CREATE ON SCHEMA {schema} TO {dormant}; ALTER TABLE {table} OWNER TO {dormant}; REVOKE CREATE ON SCHEMA {schema} FROM {dormant}; REVOKE ALL ON {table} FROM {dormant}; GRANT {dormant} TO {runtime} WITH INHERIT FALSE, SET FALSE, ADMIN TRUE",
                _ => throw new ArgumentOutOfRangeException(nameof(drift))
            };
            await context.Database.ExecuteSqlRawAsync(change);
            string before = await CatalogueImageAsync(context, table);
            await transaction.CreateSavepointAsync("before_entry");
            await context.Database.ExecuteSqlRawAsync($"SET LOCAL SESSION AUTHORIZATION {global}");
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresUserTenantMembershipReadPermissions.RequireReadyAsync(context, enrollment));
            await transaction.RollbackToSavepointAsync("before_entry");
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PostgresUserTenantMembershipReadPermissions.ApplyAsync(context, enrollment));
            await Assert.That(await CatalogueImageAsync(context, table)).IsEqualTo(before);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PostgresUserTenantMembershipReadPermissions.ApplyAsync(context, enrollment));
            await Assert.That(await CatalogueImageAsync(context, table)).IsEqualTo(before);
        });
    }

    [Test]
    public async Task RoleSubstitutionCannotReplaceTheOriginalGlobalLogin()
    {
        await using var strategy = OpenMigrator();
        await strategy.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var context = OpenMigrator();
            await using var transaction = await context.Database.BeginTransactionAsync();
            string substitute = Quote("membership_substitute_" + Guid.CreateVersion7().ToString("N"));
            await context.Database.ExecuteSqlRawAsync($"CREATE ROLE {substitute} NOLOGIN; SET LOCAL ROLE {substitute}");
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresUserTenantMembershipReadPermissions.RequireReadyAsync(context, fixture.NativeQueueEnrollment));
        });
    }

    [Test]
    public async Task ReaderAndInstallerRejectDifferentConfiguredGlobalLogin()
    {
        var changed = fixture.NativeQueueEnrollment with
        {
            GlobalLogin = fixture.NativeQueueEnrollment.EmailInstanceMutationLogin,
            EmailInstanceMutationLogin = fixture.NativeQueueEnrollment.GlobalLogin
        };
        await using var global = await GlobalFactory().CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PostgresUserTenantMembershipReadPermissions.RequireReadyAsync(global, changed));
        await using var strategy = OpenMigrator();
        await strategy.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var context = OpenMigrator();
            await using var transaction = await context.Database.BeginTransactionAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PostgresUserTenantMembershipReadPermissions.ApplyAsync(context, changed));
        });
    }

    [Test]
    public async Task RuntimeCannotInstallOrEnterTheGlobalReadAndMissingEnrollmentFailsClosed()
    {
        await using var runtime = fixture.CreateEnrolledTenantlessDbContext();
        await runtime.Database.OpenConnectionAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PostgresUserTenantMembershipReadPermissions.RequireReadyAsync(runtime, fixture.NativeQueueEnrollment));
        await runtime.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresUserTenantMembershipReadPermissions.ApplyAsync(runtime, fixture.NativeQueueEnrollment)));
        await using var global = await GlobalFactory().CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PostgresUserTenantMembershipReadPermissions.RequireReadyAsync(global, null));
    }

    [Test]
    public async Task OriginalInstallerIsIdempotentAndPreservesCallerTransaction()
    {
        await using var strategy = OpenMigrator();
        await strategy.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var context = OpenMigrator();
            await using var transaction = await context.Database.BeginTransactionAsync();
            var connection = context.Database.GetDbConnection();
            string table = Table(context.Model.FindEntityType(typeof(TenantUser))!);
            string before = await CatalogueImageAsync(context, table);
            await PostgresUserTenantMembershipReadPermissions.ApplyAsync(context, fixture.NativeQueueEnrollment);
            await PostgresUserTenantMembershipReadPermissions.ApplyAsync(context, fixture.NativeQueueEnrollment);
            await Assert.That(await CatalogueImageAsync(context, table)).IsEqualTo(before);
            await Assert.That(ReferenceEquals(connection, context.Database.GetDbConnection())).IsTrue();
            await Assert.That(ReferenceEquals(transaction, context.Database.CurrentTransaction)).IsTrue();
        });
    }

    [Test]
    [Arguments("unapproved-user-read")]
    [Arguments("membership-write")]
    [Arguments("runtime-membership-read")]
    public async Task PublicationProofRecognizesOnlyApprovedGlobalDirectoryReads(string drift)
    {
        await using var strategy = OpenMigrator();
        await strategy.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var context = OpenMigrator();
            await using var transaction = await context.Database.BeginTransactionAsync();
            var enrollment = fixture.NativeQueueEnrollment;
            // Navigation and membership are both installed by this point. Their real fixed
            // reads must not prevent the unrelated publication contract from reattesting.
            await PostgresPublicationFanoutExecutionContract.ApplyAsync(context, enrollment);
            var entity = context.Model.FindEntityType(drift == "unapproved-user-read" ? typeof(User) : typeof(TenantUser))!;
            var mapping = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            string property = drift switch
            {
                "unapproved-user-read" => nameof(User.EmailVerified),
                "membership-write" => nameof(TenantUser.ModerationNote),
                _ => nameof(TenantUser.Id)
            };
            string column = Quote(entity.FindProperty(property)!.GetColumnName(mapping)!);
            string privilege = drift == "membership-write" ? "UPDATE" : "SELECT";
            string recipient = drift == "runtime-membership-read" ? enrollment.RuntimeLogin : enrollment.GlobalLogin;
            string table = Table(entity);
            await context.Database.ExecuteSqlRawAsync($"GRANT {privilege} ({column}) ON {table} TO {Quote(recipient)}");
            string before = await CatalogueImageAsync(context, table);
            await transaction.CreateSavepointAsync("before_fanout_proof");
            var denied = await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresPublicationFanoutExecutionContract.ApplyAsync(context, enrollment));
            await Assert.That(denied.SqlState).IsEqualTo("42501");
            await Assert.That(denied.MessageText).IsEqualTo("publication_fanout_private_source_privilege");
            await transaction.RollbackToSavepointAsync("before_fanout_proof");
            await Assert.That(await CatalogueImageAsync(context, table)).IsEqualTo(before);
        });
    }

    [Test]
    public async Task PublicationProofAcceptsCompleteCreatorInsertionButRejectsAnUnapprovedGlobalInsertWithoutRepair()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        CancellationToken token = timeout.Token;
        await using var strategy = OpenMigrator();
        await strategy.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var context = OpenMigrator();
            await using var transaction = await context.Database.BeginTransactionAsync(token);
            var connection = (NpgsqlConnection)context.Database.GetDbConnection();
            var physical = transaction.GetDbTransaction();
            int backend = connection.ProcessID;
            var enrollment = fixture.NativeQueueEnrollment;
            try
            {
                await PostgresTenantCreationPermissions.ApplyAsync(context, enrollment,
                    IdentityDatabaseTopology.Colocated, token);
                await Assert.That(PostgresTenantCreationPermissions.TryGetGlobalColumns(context, typeof(TenantUser),
                    out _, out var creatorInsert)).IsTrue();
                await Assert.That(creatorInsert.Count).IsEqualTo(19);
                string membership = Table(context.Model.FindEntityType(typeof(TenantUser))!);
                foreach (string column in creatorInsert)
                    await Assert.That(await context.Database.SqlQuery<bool>(
                        $"SELECT pg_catalog.has_column_privilege({enrollment.GlobalLogin}, {membership}, {column}, 'INSERT') AS \"Value\"")
                        .SingleAsync(token)).IsTrue();

                // This is the second production-migration shape: creator rights now exist
                // when the unchanged original publication owner is reattested.
                await PostgresPublicationFanoutExecutionContract.ApplyAsync(context, enrollment, token);
                var user = context.Model.FindEntityType(typeof(User))!;
                var mapping = StoreObjectIdentifier.Table(user.GetTableName()!, user.GetSchema());
                string userTable = Table(user);
                string extra = Quote(user.FindProperty(nameof(User.EmailVerified))!.GetColumnName(mapping)!);
                string healthyUser = await CatalogueImageAsync(context, userTable);
                string healthyMembership = await CatalogueImageAsync(context, membership);
                await transaction.CreateSavepointAsync("before_creator_extra_insert", token);
                await context.Database.ExecuteSqlRawAsync(
                    $"GRANT INSERT ({extra}) ON {userTable} TO {Quote(enrollment.GlobalLogin)}", token);
                string attackedUser = await CatalogueImageAsync(context, userTable);
                await transaction.CreateSavepointAsync("before_creator_fanout_refusal", token);
                var denied = await Assert.ThrowsAsync<PostgresException>(() =>
                    PostgresPublicationFanoutExecutionContract.ApplyAsync(context, enrollment, token));
                await Assert.That(denied.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
                await Assert.That(denied.MessageText).IsEqualTo("publication_fanout_private_source_privilege");
                await transaction.RollbackToSavepointAsync("before_creator_fanout_refusal", token);
                await Assert.That(await CatalogueImageAsync(context, userTable)).IsEqualTo(attackedUser);
                await Assert.That(await CatalogueImageAsync(context, membership)).IsEqualTo(healthyMembership);

                // Undo only this attack, not the legitimate creator profile, then prove
                // the same original owner can reattest the restored catalogue.
                await transaction.RollbackToSavepointAsync("before_creator_extra_insert", token);
                await Assert.That(await CatalogueImageAsync(context, userTable)).IsEqualTo(healthyUser);
                await PostgresPublicationFanoutExecutionContract.ApplyAsync(context, enrollment, token);
                await Assert.That(await CatalogueImageAsync(context, membership)).IsEqualTo(healthyMembership);
                await Assert.That(context.Database.GetDbConnection()).IsSameReferenceAs(connection);
                await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
                await Assert.That(context.Database.CurrentTransaction!.GetDbTransaction()).IsSameReferenceAs(physical);
                await Assert.That(connection.ProcessID).IsEqualTo(backend);
            }
            finally { await transaction.RollbackAsync(CancellationToken.None); }
        });
    }

    private async Task<(Guid User, Guid A, Guid B, Guid Inactive)> SeedAsync()
    {
        await using var strategy = OpenMigrator();
        return await strategy.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            Guid user = Guid.CreateVersion7(), other = Guid.CreateVersion7();
            Guid a = Guid.CreateVersion7(), b = Guid.CreateVersion7(), inactive = Guid.CreateVersion7();
            await using var context = OpenMigrator();
            await using var gate = new EventDiscoveryEnrollmentGate(context);
            await using var custody = await gate.AcquireAsync();
            context.Users.AddRange(CreateUser(user), CreateUser(other));
            context.Tenants.AddRange(CreateTenant(a, TenantStatusEnum.Active), CreateTenant(b, TenantStatusEnum.Active),
                CreateTenant(inactive, TenantStatusEnum.Suspended));
            context.TenantUsers.AddRange(Member(a, user), Member(b, user), Member(inactive, user), Member(b, other));
            await context.SaveChangesAsync();
            return (user, a, b, inactive);
        });
    }

    private static async Task<string> CatalogueImageAsync(ExploreDbContext context, string table)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = """
            SELECT jsonb_build_object('owner',c.relowner,'table',c.relacl,
                'columns',(SELECT jsonb_agg(jsonb_build_array(a.attnum,a.attacl) ORDER BY a.attnum)
                    FROM pg_catalog.pg_attribute a WHERE a.attrelid=c.oid AND a.attnum<>0),
                'memberships',(SELECT jsonb_agg(jsonb_build_array(m.roleid,m.member,m.grantor,m.admin_option,m.inherit_option,m.set_option)
                    ORDER BY m.roleid,m.member,m.grantor) FROM pg_catalog.pg_auth_members m),
                'roles',(SELECT jsonb_agg(jsonb_build_array(r.oid,r.rolsuper,r.rolbypassrls,r.rolcreaterole,r.rolcreatedb,r.rolreplication)
                    ORDER BY r.oid) FROM pg_catalog.pg_roles r),
                'schema',(SELECT n.nspacl FROM pg_catalog.pg_namespace n WHERE n.oid=c.relnamespace))::text
            FROM pg_catalog.pg_class c WHERE c.oid=pg_catalog.to_regclass(@table)
            """;
        command.Parameters.Add(new NpgsqlParameter("table", table));
        return (string)(await command.ExecuteScalarAsync())!;
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
    private static Tenant CreateTenant(Guid id, TenantStatusEnum status) => new()
    { Id = id, FullName = "Membership tenant", Slug = $"membership-{id:N}", TenantStatusId = (int)status, TenantStatus = null! };
    private static TenantUser Member(Guid tenant, Guid user) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenant,
        Tenant = null!,
        UserId = user,
        User = null!,
        StatusId = (int)TenantUserStatusEnum.Active,
        JoinedAt = new DateTime(2028, 4, 5, 12, 0, 0, DateTimeKind.Utc)
    };
    private sealed record FixedTenant(Guid TenantId) : ITenantContext;
    private static string Table(IEntityType entity) => $"{Quote(entity.GetSchema() ?? "public")}.{Quote(entity.GetTableName()!)}";
    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}

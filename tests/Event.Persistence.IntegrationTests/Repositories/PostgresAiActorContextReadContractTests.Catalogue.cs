using System.Data;
using Explore.Domain;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed partial class PostgresAiActorContextReadContractTests
{
    [Test]
    public async Task CatalogueRoleColumnsCoexistWithActorReadsAndConversationFencesAfterReinstallation()
    {
        await using (var baseline = fixture.CreateDbContext())
            await AssertCataloguePermissionBaselineAsync(baseline, "before actor seeding");
        var seed = await SeedAsync();
        await using (var migration = fixture.CreateDbContext())
        {
            await AssertCataloguePermissionBaselineAsync(migration, "after actor seeding");
            await PostgresLookupCatalogueReadPermissions.ApplyAsync(migration, fixture.NativeQueueEnrollment);
            await PostgresAiActorContextReadContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
            await PostgresAiConversationOwnerFenceContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
            await PostgresEventCreationPublisherReadContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        }
        await using (var catalogue = fixture.CreateEnrolledTenantlessDbContext())
        {
            var roles = await GlobalLookupCatalogueProviderOperations.ReadAsync<Role>(
                catalogue, fixture.NativeQueueEnrollment, default);
            await Assert.That(roles.Any(role => role.Id == seed.OrganizationRole)).IsTrue();
        }
        await Assert.That((await ReadFreshAsync(seed)).Count).IsEqualTo(3);
        await using var runtime = Open(PrimaryDatabaseRole.Runtime, seed.Tenant);
        await runtime.Database.OpenConnectionAsync();
        await using var owner = await runtime.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var role = await runtime.Set<Role>().AsNoTracking().SingleAsync(value => value.Id == seed.OrganizationRole);
        await Assert.That(role.Id).IsEqualTo(seed.OrganizationRole);
        await Assert.That(await PostgresAiConversationOwnerFenceContract.FenceAsync(runtime,
            fixture.NativeQueueEnrollment, seed.Tenant, seed.User, seed.Personal, seed.Personal,
            privacy: false, default)).IsTrue();
        await Assert.That(runtime.Database.CurrentTransaction).IsSameReferenceAs(owner);
        await owner.RollbackAsync();
    }

    [Test]
    [Arguments("update")]
    [Arguments("table-select")]
    [Arguments("xmin")]
    [Arguments("grant-option")]
    [Arguments("inherited")]
    [Arguments("set-only")]
    [Arguments("admin")]
    [Arguments("public")]
    [Arguments("global")]
    [Arguments("private-role-permission")]
    public async Task CatalogueExceptionCannotExpandActorOrConversationSourceAuthority(string change)
    {
        await using var migration = fixture.CreateDbContext();
        await using var transaction = await migration.Database.BeginTransactionAsync();
        var names = new HumanPrincipalSourceSql(migration);
        string table = names.Table(typeof(Role));
        string id = names.Column(typeof(Role), nameof(Role.Id));
        string runtime = names.Quote(fixture.NativeQueueEnrollment.RuntimeLogin);
        string otherRole = names.Quote("catalogue_actor_drift_" + Guid.CreateVersion7().ToString("N"));
        await migration.Database.ExecuteSqlRawAsync(change switch
        {
            "update" => $"GRANT UPDATE ({id}) ON {table} TO {runtime}",
            "table-select" => $"GRANT SELECT ON {table} TO {runtime}",
            "xmin" => $"GRANT SELECT (xmin) ON {table} TO {runtime}",
            "grant-option" => $"GRANT SELECT ({id}) ON {table} TO {runtime} WITH GRANT OPTION",
            "inherited" => $"CREATE ROLE {otherRole} NOLOGIN; GRANT SELECT ({id}) ON {table} TO {otherRole}; GRANT {otherRole} TO {runtime} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE",
            "set-only" => $"CREATE ROLE {otherRole} NOLOGIN; GRANT SELECT ({id}) ON {table} TO {otherRole}; GRANT {otherRole} TO {runtime} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE",
            "admin" => $"CREATE ROLE {otherRole} NOLOGIN; GRANT SELECT ({id}) ON {table} TO {otherRole}; GRANT {otherRole} TO {runtime} WITH INHERIT FALSE, SET FALSE, ADMIN TRUE",
            "public" => $"GRANT SELECT ({id}) ON {table} TO PUBLIC",
            "global" => $"GRANT SELECT ({id}) ON {table} TO {names.Quote(fixture.NativeQueueEnrollment.GlobalLogin)}",
            "private-role-permission" => $"GRANT SELECT ({names.Column(typeof(RolePermission), nameof(RolePermission.RoleId))}) ON {names.Table(typeof(RolePermission))} TO {runtime}",
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        });
        // PUBLIC grants reach every dedicated role and fail the shared schema-wide privilege guard.
        // Other Role grants leave Publisher's independent source inventory unchanged.
        if (change == "public")
        {
            await transaction.CreateSavepointAsync("before_publisher_admission");
            var publisherFailure = await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresEventCreationPublisherReadContract.ApplyAsync(migration, fixture.NativeQueueEnrollment));
            await Assert.That(publisherFailure!.SqlState).IsEqualTo(PostgresErrorCodes.ObjectNotInPrerequisiteState);
            await Assert.That(publisherFailure.MessageText).IsEqualTo("native_privilege_guard_not_ready");
            await transaction.RollbackToSavepointAsync("before_publisher_admission");
        }
        else if (change != "private-role-permission")
            await PostgresEventCreationPublisherReadContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        await transaction.CreateSavepointAsync("before_actor_admission");
        var actorFailure = await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresAiActorContextReadContract.ApplyAsync(migration, fixture.NativeQueueEnrollment));
        await Assert.That(actorFailure!.SqlState).IsEqualTo(change == "public"
            ? PostgresErrorCodes.ObjectNotInPrerequisiteState : PostgresErrorCodes.InsufficientPrivilege);
        await Assert.That(actorFailure.MessageText).IsEqualTo(change == "public"
            ? "native_privilege_guard_not_ready" : "ai_actor_context_private_source_privilege");
        await transaction.RollbackToSavepointAsync("before_actor_admission");
        var fenceFailure = await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresAiConversationOwnerFenceContract.ApplyAsync(migration, fixture.NativeQueueEnrollment));
        await Assert.That(fenceFailure!.SqlState).IsEqualTo(change == "public"
            ? PostgresErrorCodes.ObjectNotInPrerequisiteState : PostgresErrorCodes.InsufficientPrivilege);
        await Assert.That(fenceFailure.MessageText).IsEqualTo(change == "public"
            ? "native_privilege_guard_not_ready" : "ai_conversation_owner_private_source_privilege");
        await transaction.RollbackAsync();
    }

    private async Task AssertCataloguePermissionBaselineAsync(Explore.Persistence.ExploreDbContext context, string stage)
    {
        var names = new HumanPrincipalSourceSql(context);
        Type[] types = [typeof(EventType), typeof(EventFormat), typeof(EventStatus), typeof(AudienceAge),
            typeof(AudienceGender), typeof(Madhab), typeof(Language), typeof(VisibilityType), typeof(ApprovalStatus),
            typeof(RegistrationMode), typeof(Role), typeof(OrganizationPosition), typeof(ActorType),
            typeof(DidCustodyType), typeof(FileType), typeof(TagType), typeof(OwnerType)];
        string[] tables = types.Select(names.Table).ToArray();
        string[] labels = types.Select(type => type.Name).ToArray();
        var expected = types.SelectMany(type =>
        {
            string[] properties = type == typeof(Role)
                ? ["Id", "MasterCode", "FullName", "Description", "RoleScopeId", "IsSystem"]
                : type == typeof(EventType) ? ["Id", "MasterCode", "FullName", "Description", "TenantId"]
                : type == typeof(AudienceAge) ? ["Id", "MasterCode", "FullName", "Description", "MinAge", "MaxAge"]
                : ["Id", "MasterCode", "FullName", "Description"];
            return properties.Select(property => (Table: names.Table(type), Column: names.ColumnName(type, property)));
        }).ToArray();
        string[] otherLogins = [fixture.NativeQueueEnrollment.GlobalLogin,
            fixture.NativeQueueEnrollment.EmailInstanceMutationLogin, fixture.NativeQueueEnrollment.EmailTenantMutationLogin];
        string[] otherLabels = ["Global", "EmailInstance", "EmailTenant"];
        var violations = await context.Database.SqlQueryRaw<string>(
            """
            WITH sources AS (SELECT * FROM unnest({0}::text[],{1}::text[]) AS s(table_name,label)),
            expected AS (SELECT * FROM unnest({2}::text[],{3}::text[]) AS e(table_name,column_name)),
            members AS (SELECT * FROM pg_catalog.pg_roles WHERE pg_catalog.pg_has_role({4},oid,'MEMBER')),
            other_logins AS (SELECT * FROM unnest({5}::text[],{6}::text[]) AS l(login,label))
            SELECT 'Runtime.privileged-member-or-schema-create' AS "Value" FROM members r
            WHERE r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication
                OR pg_catalog.has_schema_privilege(r.oid,{7},'CREATE')
            UNION ALL
            SELECT s.label || '.relation-kind-or-owner' FROM sources s
            LEFT JOIN pg_catalog.pg_class c ON c.oid = pg_catalog.to_regclass(s.table_name)
            WHERE c.oid IS NULL OR c.relkind <> 'r' OR c.relpersistence <> 'p'
                OR c.relowner <> pg_catalog.to_regrole({8})::oid
            UNION ALL
            SELECT 'Runtime.' || s.label || '.table-privilege' FROM sources s CROSS JOIN members r
            WHERE pg_catalog.has_table_privilege(r.oid,s.table_name,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
            UNION ALL
            SELECT 'Runtime.' || s.label || '.' || a.attname || '.unexpected-column-authority'
            FROM sources s CROSS JOIN members r JOIN pg_catalog.pg_attribute a ON true
            WHERE a.attrelid = pg_catalog.to_regclass(s.table_name) AND a.attnum <> 0 AND NOT a.attisdropped
                AND (pg_catalog.has_column_privilege(r.oid,a.attrelid,a.attnum,'INSERT,UPDATE,REFERENCES,SELECT WITH GRANT OPTION')
                    OR (pg_catalog.has_column_privilege(r.oid,a.attrelid,a.attnum,'SELECT')
                        AND (r.rolname <> {4} OR NOT EXISTS (SELECT 1 FROM expected e
                            WHERE e.table_name = s.table_name AND e.column_name = a.attname))))
            UNION ALL
            SELECT 'Runtime.' || s.label || '.' || e.column_name || '.missing-direct-select'
            FROM sources s JOIN expected e ON e.table_name = s.table_name
            WHERE NOT pg_catalog.has_column_privilege({4},s.table_name,e.column_name,'SELECT')
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                    CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                    WHERE a.attrelid = pg_catalog.to_regclass(s.table_name) AND a.attname = e.column_name
                        AND acl.grantee = pg_catalog.to_regrole({4})::oid AND acl.privilege_type = 'SELECT' AND NOT acl.is_grantable)
            UNION ALL
            SELECT l.label || '.runtime-membership' FROM other_logins l
            WHERE pg_catalog.pg_has_role(l.login,{4},'MEMBER') OR pg_catalog.pg_has_role({4},l.login,'MEMBER')
            UNION ALL
            SELECT l.label || '.' || s.label || '.source-authority' FROM other_logins l
            CROSS JOIN sources s CROSS JOIN pg_catalog.pg_roles r
            WHERE l.label = 'Global' AND pg_catalog.pg_has_role(l.login,r.oid,'MEMBER')
                AND (pg_catalog.has_table_privilege(r.oid,s.table_name,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                        WHERE a.attrelid = pg_catalog.to_regclass(s.table_name) AND a.attnum <> 0 AND NOT a.attisdropped
                            AND pg_catalog.has_column_privilege(r.oid,a.attrelid,a.attnum,'SELECT,INSERT,UPDATE,REFERENCES')))
            UNION ALL
            SELECT 'Runtime.protected-source-authority' FROM members r CROSS JOIN unnest({9}::text[]) t(table_name)
            WHERE pg_catalog.has_table_privilege(r.oid,t.table_name,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                    WHERE a.attrelid = pg_catalog.to_regclass(t.table_name) AND a.attnum <> 0 AND NOT a.attisdropped
                        AND pg_catalog.has_column_privilege(r.oid,a.attrelid,a.attnum,'SELECT,INSERT,UPDATE,REFERENCES'))
            """, tables, labels, expected.Select(value => value.Table).ToArray(), expected.Select(value => value.Column).ToArray(),
            fixture.NativeQueueEnrollment.RuntimeLogin, otherLogins, otherLabels, names.Schema,
            fixture.NativeQueueEnrollment.MigratorLogin, new[] { names.Table(typeof(RolePermission)), names.Table(typeof(UserPii)) })
            .ToListAsync();
        await Assert.That(violations).IsEmpty().Because(stage);
    }
}

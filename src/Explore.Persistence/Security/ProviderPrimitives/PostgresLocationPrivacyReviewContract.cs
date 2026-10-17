using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>Execute-only instance count over active review flags, without location payload access.</summary>
public static class PostgresLocationPrivacyReviewContract
{
    public const string OwnerRole = "event_location_review_owner";
    public const string MigratorRole = "event_location_review_migrator";
    public const string GlobalRole = "event_location_review_global";

    private static string Schema(ExploreDbContext context) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? context.Model.FindEntityType(typeof(EventLocation))!.GetSchema() ?? "public";

    public static string FunctionSql(ExploreDbContext context) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier("location_privacy_review_depth", Schema(context));

    public static async Task ApplyAsync(ExploreDbContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
            return;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync(BuildSql(context), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public static string BuildSql(ExploreDbContext context)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        var entity = context.Model.FindEntityType(typeof(EventLocation))!;
        string schema = sql.DelimitIdentifier(Schema(context));
        string table = sql.DelimitIdentifier(entity.GetTableName()!, Schema(context));
        var identifier = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        string Column(string property) =>
            sql.DelimitIdentifier(entity.FindProperty(property)!.GetColumnName(identifier)!);
        string review = Column(nameof(EventLocation.NeedsPrivacyReview));
        string deleted = Column(nameof(EventLocation.IsDeleted));
        string function = FunctionSql(context);
        string priorAcl = PostgresPrivilegeGuard.BuildRoutineAclAssertSql(
            [$"{function}()"], OwnerRole, GlobalRole, requireComplete: false);
        string currentAcl = PostgresPrivilegeGuard.BuildRoutineAclAssertSql(
            [$"{function}()"], OwnerRole, GlobalRole, requireComplete: true);
        return $"""
            DO $prior_acl$ BEGIN {priorAcl} END; $prior_acl$;
            DO $roles$
            DECLARE role_name text;
            BEGIN
                FOREACH role_name IN ARRAY ARRAY['{OwnerRole}', '{MigratorRole}', '{GlobalRole}'] LOOP
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = role_name) THEN
                        EXECUTE format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', role_name);
                    END IF;
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = role_name
                        AND (rolcanlogin OR rolsuper OR rolcreatedb OR rolcreaterole OR rolinherit OR rolreplication OR rolbypassrls)) THEN
                        RAISE EXCEPTION 'location_review_role_invalid';
                    END IF;
                END LOOP;
                IF pg_has_role('{GlobalRole}', '{OwnerRole}', 'MEMBER')
                    OR pg_has_role('{GlobalRole}', '{MigratorRole}', 'MEMBER')
                    OR pg_has_role('{OwnerRole}', '{GlobalRole}', 'MEMBER')
                    OR pg_has_role('{MigratorRole}', '{GlobalRole}', 'MEMBER')
                    OR EXISTS (
                        WITH RECURSIVE memberships AS (
                            SELECT roleid FROM pg_catalog.pg_auth_members
                            WHERE member = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = CURRENT_USER)
                            UNION SELECT edge.roleid FROM pg_catalog.pg_auth_members edge
                            JOIN memberships inherited ON edge.member = inherited.roleid)
                        SELECT 1 FROM memberships WHERE roleid =
                            (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{GlobalRole}')) THEN
                    RAISE EXCEPTION 'location_review_migration_authority_invalid';
                END IF;
            END $roles$;
            GRANT {OwnerRole} TO {MigratorRole};
            GRANT {MigratorRole} TO CURRENT_USER;
            GRANT USAGE, CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {GlobalRole};
            REVOKE ALL ON {table} FROM {OwnerRole}, {GlobalRole};
            GRANT SELECT ({review}, {deleted}) ON {table} TO {OwnerRole};
            DROP POLICY IF EXISTS "location_privacy_review_count" ON {table};
            CREATE POLICY "location_privacy_review_count" ON {table} FOR SELECT TO {OwnerRole}
                USING ({review} AND NOT {deleted});
            DO $install$
            DECLARE previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{OwnerRole}', true);
                EXECUTE $ddl$
                    CREATE OR REPLACE FUNCTION {function}()
                    RETURNS integer LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
                    AS $body$
                    BEGIN
                        {currentAcl}
                        RETURN (SELECT count(*)::integer FROM {table} WHERE {review} AND NOT {deleted});
                    END;
                    $body$;
                $ddl$;
                EXECUTE $acl$
                    REVOKE ALL ON FUNCTION {function}() FROM PUBLIC, {GlobalRole};
                    GRANT EXECUTE ON FUNCTION {function}() TO {GlobalRole};
                $acl$;
                PERFORM set_config('role', previous_role, true);
            END $install$;
            REVOKE CREATE ON SCHEMA {schema} FROM {OwnerRole};
            DO $current_acl$ BEGIN {currentAcl} END; $current_acl$;
            """;
    }
}

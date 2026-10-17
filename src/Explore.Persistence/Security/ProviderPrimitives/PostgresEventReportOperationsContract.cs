using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>Migration-owned scalar report synchronization counts with parent soft-delete filtering.</summary>
public static class PostgresEventReportOperationsContract
{
    public const string OwnerRole = "event_report_operations_owner";
    public const string MigratorRole = "event_report_operations_migrator";
    public const string GlobalRole = "event_report_operations_global";
    private const string PolicyName = "report_operations_authority";

    public static string FunctionSql(ExploreDbContext context) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier("event_report_operations_count", Schema(context));

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
        string Table<T>() => sql.DelimitIdentifier(context.Model.FindEntityType(typeof(T))!.GetTableName()!, Schema(context));
        string Column<T>(string name)
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            return sql.DelimitIdentifier(entity.FindProperty(name)!.GetColumnName(store)!);
        }
        string schema = sql.DelimitIdentifier(Schema(context));
        string links = Table<EventReportExternalLink>();
        string reports = Table<EventReport>();
        string linkTenant = Column<EventReportExternalLink>(nameof(EventReportExternalLink.TenantId));
        string reportId = Column<EventReportExternalLink>(nameof(EventReportExternalLink.ReportId));
        string state = Column<EventReportExternalLink>(nameof(EventReportExternalLink.SyncState));
        string created = Column<EventReportExternalLink>(nameof(EventReportExternalLink.CreatedAt));
        string id = Column<EventReport>(nameof(EventReport.Id));
        string tenant = Column<EventReport>(nameof(EventReport.TenantId));
        string deleted = Column<EventReport>(nameof(EventReport.IsDeleted));
        string function = FunctionSql(context);
        return $$"""
            DO $roles$
            DECLARE role_name text;
            BEGIN
                FOREACH role_name IN ARRAY ARRAY['{{OwnerRole}}','{{MigratorRole}}','{{GlobalRole}}'] LOOP
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = role_name) THEN
                        EXECUTE format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS', role_name);
                    END IF;
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = role_name
                        AND (rolcanlogin OR rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication OR rolbypassrls)) THEN
                        RAISE EXCEPTION 'report_operations_role_invalid';
                    END IF;
                END LOOP;
                IF EXISTS (
                    WITH RECURSIVE memberships AS (
                        SELECT roleid FROM pg_catalog.pg_auth_members
                        WHERE member = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = CURRENT_USER)
                        UNION SELECT edge.roleid FROM pg_catalog.pg_auth_members edge
                        JOIN memberships inherited ON edge.member = inherited.roleid)
                    SELECT 1 FROM memberships WHERE roleid =
                        (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{{GlobalRole}}')) THEN
                    RAISE EXCEPTION 'report_operations_migration_authority_invalid';
                END IF;
            END; $roles$;
            GRANT {{OwnerRole}} TO {{MigratorRole}};
            GRANT {{MigratorRole}} TO CURRENT_USER;
            GRANT USAGE, CREATE ON SCHEMA {{schema}} TO {{OwnerRole}};
            GRANT USAGE ON SCHEMA {{schema}} TO {{GlobalRole}};
            REVOKE ALL ON {{links}}, {{reports}} FROM {{OwnerRole}};
            GRANT SELECT ({{linkTenant}}, {{reportId}}, {{state}}, {{created}}) ON {{links}} TO {{OwnerRole}};
            GRANT SELECT ({{id}}, {{tenant}}, {{deleted}}) ON {{reports}} TO {{OwnerRole}};
            DROP POLICY IF EXISTS "{{PolicyName}}" ON {{links}};
            CREATE POLICY "{{PolicyName}}" ON {{links}} FOR SELECT TO {{OwnerRole}} USING (true);
            DROP POLICY IF EXISTS "{{PolicyName}}" ON {{reports}};
            CREATE POLICY "{{PolicyName}}" ON {{reports}} FOR SELECT TO {{OwnerRole}} USING (true);
            DO $install$
            DECLARE previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{{OwnerRole}}', true);
                EXECUTE $ddl$
                CREATE OR REPLACE FUNCTION {{function}}(p_state integer, p_before timestamp with time zone)
                RETURNS integer LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog
                AS $body$
                    SELECT count(*)::integer FROM {{links}} link
                    JOIN {{reports}} report ON report.{{id}} = link.{{reportId}}
                        AND report.{{tenant}} = link.{{linkTenant}}
                    WHERE NOT report.{{deleted}} AND link.{{state}} = p_state
                        AND (p_before IS NULL OR link.{{created}} < p_before)
                $body$;
                $ddl$;
                EXECUTE $acl$
                    REVOKE ALL ON FUNCTION {{function}}(integer, timestamp with time zone) FROM PUBLIC, {{GlobalRole}};
                    GRANT EXECUTE ON FUNCTION {{function}}(integer, timestamp with time zone) TO {{GlobalRole}};
                $acl$;
                PERFORM set_config('role', previous_role, true);
            END; $install$;
            REVOKE CREATE ON SCHEMA {{schema}} FROM {{OwnerRole}};
            """;
    }

    private static string Schema(ExploreDbContext context) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? context.Model.FindEntityType(typeof(EventReportExternalLink))!.GetSchema() ?? "public";
}

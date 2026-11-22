using Explore.Domain;
using System.Security.Cryptography;
using System.Text;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>Migration-owned metadata reads for application-owned report synchronization counts.</summary>
public static class PostgresEventReportOperationsContract
{
    private const string RetiredOwnerRole = "event_report_operations_owner";
    private const string RetiredMigratorRole = "event_report_operations_migrator";
    public const string GlobalRole = "event_report_operations_global";
    private const string PolicyName = "report_operations_authority";

    internal static string GlobalPolicyException(ExploreDbContext context, string login) =>
        "(" + string.Join(" OR ", new[] { typeof(EventReportExternalLink), typeof(EventReport) }.Select(type =>
        {
            string table = context.Model.FindEntityType(type)!.GetTableName()!;
            return $"""
                (session_user={Text(login)} AND current_user=session_user
                AND n.nspname={Text(Schema(context))} AND c.relname={Text(table)}
                AND p.polname={Text(PolicyName)} AND p.polcmd='r' AND p.polpermissive
                AND p.polroles=ARRAY[(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(GlobalRole)})]
                AND p.polwithcheck IS NULL AND p.polqual IS NOT NULL
                AND pg_catalog.obj_description(p.oid,'pg_policy')={Text(PolicyStamp(context, table))} || pg_catalog.md5(p.polqual::text))
                """;
        })) + ")";

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
        string function = sql.DelimitIdentifier("event_report_operations_count", Schema(context));
        return $$"""
            DO $roles$
            DECLARE role_name text;
            BEGIN
                FOREACH role_name IN ARRAY ARRAY['{{GlobalRole}}'] LOOP
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
            GRANT USAGE ON SCHEMA {{schema}} TO {{GlobalRole}};
            DROP FUNCTION IF EXISTS {{function}}(integer, timestamp with time zone);
            REVOKE ALL ON {{links}}, {{reports}} FROM {{GlobalRole}};
            GRANT SELECT ({{linkTenant}}, {{reportId}}, {{state}}, {{created}}) ON {{links}} TO {{GlobalRole}};
            GRANT SELECT ({{id}}, {{tenant}}, {{deleted}}) ON {{reports}} TO {{GlobalRole}};
            DROP POLICY IF EXISTS "{{PolicyName}}" ON {{links}};
            CREATE POLICY "{{PolicyName}}" ON {{links}} FOR SELECT TO {{GlobalRole}} USING (true);
            DROP POLICY IF EXISTS "{{PolicyName}}" ON {{reports}};
            CREATE POLICY "{{PolicyName}}" ON {{reports}} FOR SELECT TO {{GlobalRole}} USING (true);
            DO $stamp$
            BEGIN
                EXECUTE (SELECT format('COMMENT ON POLICY %I ON %s IS %L', p.polname,
                    {{Text(links)}}, {{Text(PolicyStamp(context, context.Model.FindEntityType(typeof(EventReportExternalLink))!.GetTableName()!))}} || md5(p.polqual::text))
                    FROM pg_policy p WHERE p.polrelid={{Text(links)}}::regclass AND p.polname='{{PolicyName}}');
                EXECUTE (SELECT format('COMMENT ON POLICY %I ON %s IS %L', p.polname,
                    {{Text(reports)}}, {{Text(PolicyStamp(context, context.Model.FindEntityType(typeof(EventReport))!.GetTableName()!))}} || md5(p.polqual::text))
                    FROM pg_policy p WHERE p.polrelid={{Text(reports)}}::regclass AND p.polname='{{PolicyName}}');
            END; $stamp$;
            DO $retire$
            BEGIN
                IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='{{RetiredOwnerRole}}') THEN
                    REVOKE ALL ON {{links}}, {{reports}} FROM {{RetiredOwnerRole}};
                    REVOKE CREATE ON SCHEMA {{schema}} FROM {{RetiredOwnerRole}};
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='{{RetiredMigratorRole}}') THEN
                        REVOKE {{RetiredOwnerRole}} FROM {{RetiredMigratorRole}};
                    END IF;
                END IF;
            END; $retire$;
            """;
    }

    private static string PolicyStamp(ExploreDbContext context, string table) => "event-report-operations-read:v1:"
        + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Schema(context) + ":" + table + ":metadata:true"))) + ":";

    private static string Text(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string Schema(ExploreDbContext context) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? context.Model.FindEntityType(typeof(EventReportExternalLink))!.GetSchema() ?? "public";
}

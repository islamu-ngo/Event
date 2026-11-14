using Explore.Domain;
using System.Security.Cryptography;
using System.Text;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>Migration-owned read permissions restricted to the two public domain-setting keys.</summary>
public static class PostgresManagedTenantDomainContract
{
    private const string RetiredOwnerRole = "event_managed_domain_owner";
    private const string RetiredMigratorRole = "event_managed_domain_migrator";
    public const string GlobalRole = "event_managed_domain_global";
    private const string PolicyName = "managed_domain_authority";

    internal static string GlobalPolicyException(ExploreDbContext context, string login)
    {
        var entity = context.Model.FindEntityType(typeof(TenantSetting))!;
        return $"""
            session_user={Text(login)} AND current_user=session_user
            AND n.nspname={Text(Schema(context))} AND c.relname={Text(entity.GetTableName()!)}
            AND p.polname={Text(PolicyName)} AND p.polcmd='r' AND p.polpermissive
            AND p.polroles=ARRAY[(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(GlobalRole)})]
            AND p.polwithcheck IS NULL AND p.polqual IS NOT NULL
            AND pg_catalog.obj_description(p.oid,'pg_policy')={Text(PolicyStamp(context))} || pg_catalog.md5(p.polqual::text)
            """;
    }

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
        var entity = context.Model.FindEntityType(typeof(TenantSetting))!;
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        string schema = sql.DelimitIdentifier(Schema(context));
        string table = sql.DelimitIdentifier(entity.GetTableName()!, Schema(context));
        string key = sql.DelimitIdentifier(entity.FindProperty(nameof(TenantSetting.SettingKey))!.GetColumnName(store)!);
        string columns = string.Join(", ", entity.GetProperties().Select(property =>
            sql.DelimitIdentifier(property.GetColumnName(store)!)));
        string function = sql.DelimitIdentifier("managed_tenant_domain_find", Schema(context));
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
                        RAISE EXCEPTION 'managed_domain_role_invalid';
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
                    RAISE EXCEPTION 'managed_domain_migration_authority_invalid';
                END IF;
            END; $roles$;
            GRANT USAGE ON SCHEMA {{schema}} TO {{GlobalRole}};
            DROP FUNCTION IF EXISTS {{function}}(text, text);
            REVOKE ALL ON {{table}} FROM {{GlobalRole}};
            GRANT SELECT ({{columns}}) ON {{table}} TO {{GlobalRole}};
            DROP POLICY IF EXISTS "{{PolicyName}}" ON {{table}};
            CREATE POLICY "{{PolicyName}}" ON {{table}} FOR SELECT TO {{GlobalRole}}
                USING ({{key}} IN ('domains.tenant_subdomain', 'domains.tenant_custom_domain'));
            DO $stamp$
            BEGIN
                EXECUTE (SELECT format('COMMENT ON POLICY %I ON %s IS %L', p.polname,
                    {{Text(table)}}, {{Text(PolicyStamp(context))}} || md5(p.polqual::text))
                    FROM pg_policy p WHERE p.polrelid={{Text(table)}}::regclass AND p.polname='{{PolicyName}}');
            END; $stamp$;
            DO $retire$
            BEGIN
                IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='{{RetiredOwnerRole}}') THEN
                    REVOKE ALL ON {{table}} FROM {{RetiredOwnerRole}};
                    REVOKE CREATE ON SCHEMA {{schema}} FROM {{RetiredOwnerRole}};
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='{{RetiredMigratorRole}}') THEN
                        REVOKE {{RetiredOwnerRole}} FROM {{RetiredMigratorRole}};
                    END IF;
                END IF;
            END; $retire$;
            """;
    }

    private static string PolicyStamp(ExploreDbContext context) => "event-managed-domain-read:v1:"
        + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Schema(context) + ":"
            + context.Model.FindEntityType(typeof(TenantSetting))!.GetTableName() + ":domains.tenant_subdomain:domains.tenant_custom_domain"))) + ":";

    private static string Text(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string Schema(ExploreDbContext context) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? context.Model.FindEntityType(typeof(TenantSetting))!.GetSchema() ?? "public";
}

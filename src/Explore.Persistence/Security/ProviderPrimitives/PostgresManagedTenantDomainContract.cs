using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>Migration-owned exact-host discovery restricted to the two public domain-setting keys.</summary>
public static class PostgresManagedTenantDomainContract
{
    public const string OwnerRole = "event_managed_domain_owner";
    public const string MigratorRole = "event_managed_domain_migrator";
    public const string GlobalRole = "event_managed_domain_global";
    private const string PolicyName = "managed_domain_authority";

    public static string FunctionSql(ExploreDbContext context) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier("managed_tenant_domain_find", Schema(context));

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
        string value = sql.DelimitIdentifier(entity.FindProperty(nameof(TenantSetting.Value))!.GetColumnName(store)!);
        string columns = string.Join(", ", entity.GetProperties().Select(property =>
            sql.DelimitIdentifier(property.GetColumnName(store)!)));
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
            GRANT {{OwnerRole}} TO {{MigratorRole}};
            GRANT {{MigratorRole}} TO CURRENT_USER;
            GRANT USAGE, CREATE ON SCHEMA {{schema}} TO {{OwnerRole}};
            GRANT USAGE ON SCHEMA {{schema}} TO {{GlobalRole}};
            REVOKE ALL ON {{table}} FROM {{OwnerRole}};
            GRANT SELECT ({{columns}}) ON {{table}} TO {{OwnerRole}};
            DROP POLICY IF EXISTS "{{PolicyName}}" ON {{table}};
            CREATE POLICY "{{PolicyName}}" ON {{table}} FOR SELECT TO {{OwnerRole}}
                USING ({{key}} IN ('domains.tenant_subdomain', 'domains.tenant_custom_domain'));
            DO $install$
            DECLARE previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{{OwnerRole}}', true);
                EXECUTE $ddl$
                CREATE OR REPLACE FUNCTION {{function}}(p_serialized text, p_trailing_dot text)
                RETURNS SETOF {{table}} LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog
                AS $body$
                    SELECT settings.* FROM {{table}} settings
                    WHERE settings.{{key}} IN ('domains.tenant_subdomain', 'domains.tenant_custom_domain')
                        AND (lower(settings.{{value}}) = p_serialized OR lower(settings.{{value}}) = p_trailing_dot)
                    LIMIT 1
                $body$;
                $ddl$;
                EXECUTE $acl$
                    REVOKE ALL ON FUNCTION {{function}}(text, text) FROM PUBLIC, {{GlobalRole}};
                    GRANT EXECUTE ON FUNCTION {{function}}(text, text) TO {{GlobalRole}};
                $acl$;
                PERFORM set_config('role', previous_role, true);
            END; $install$;
            REVOKE CREATE ON SCHEMA {{schema}} FROM {{OwnerRole}};
            """;
    }

    private static string Schema(ExploreDbContext context) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? context.Model.FindEntityType(typeof(TenantSetting))!.GetSchema() ?? "public";
}

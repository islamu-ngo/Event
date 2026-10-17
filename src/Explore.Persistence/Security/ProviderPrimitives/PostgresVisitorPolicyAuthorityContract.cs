using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Supplies only visitor-authority overrides and live account-required participation to
/// the existing mutation transaction. Global receives execute authority, not table access.
/// </summary>
public static class PostgresVisitorPolicyAuthorityContract
{
    public const string OwnerRole = "event_visitor_policy_owner";
    public const string MigratorRole = "event_visitor_policy_migrator";
    public const string GlobalRole = "event_visitor_policy_global";
    private const string PolicyName = "visitor_policy_authority";

    public static string OverridesFunctionSql(ExploreDbContext context) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier("visitor_policy_authority_overrides", Schema(context));

    public static string ParticipationFunctionSql(ExploreDbContext context) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier("visitor_policy_account_required", Schema(context));

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
        string[] overrideProperties =
        {
            nameof(TenantSetting.Id), nameof(TenantSetting.TenantId), nameof(TenantSetting.SettingKey),
            nameof(TenantSetting.Value), nameof(TenantSetting.IsLocked), nameof(TenantSetting.CreatedAt),
            nameof(TenantSetting.CreatedBy), nameof(TenantSetting.UpdatedAt), nameof(TenantSetting.UpdatedBy)
        };
        string overridesColumns = string.Join(", ", overrideProperties.Select(Column<TenantSetting>));
        string overridesReturns = string.Join(", ", overrideProperties.Select(property =>
            $"{Column<TenantSetting>(property)} {context.Model.FindEntityType(typeof(TenantSetting))!
                .FindProperty(property)!.GetRelationalTypeMapping().StoreType}"));
        string schema = sql.DelimitIdentifier(Schema(context));
        string overrides = Table<TenantSetting>();
        string participation = Table<EventParticipationConfiguration>();
        string events = Table<Explore.Domain.Event>();
        string key = Column<TenantSetting>(nameof(TenantSetting.SettingKey));
        string tenant = Column<TenantSetting>(nameof(TenantSetting.TenantId));
        string participationId = Column<EventParticipationConfiguration>(nameof(EventParticipationConfiguration.Id));
        string participationTenant = Column<EventParticipationConfiguration>(nameof(EventParticipationConfiguration.TenantId));
        string participationDeleted = Column<EventParticipationConfiguration>(nameof(EventParticipationConfiguration.IsDeleted));
        string identity = Column<EventParticipationConfiguration>(nameof(EventParticipationConfiguration.IdentityAccessModeId));
        string eventId = Column<Explore.Domain.Event>(nameof(Explore.Domain.Event.Id));
        string eventTenant = Column<Explore.Domain.Event>(nameof(Explore.Domain.Event.TenantId));
        string eventDeleted = Column<Explore.Domain.Event>(nameof(Explore.Domain.Event.IsDeleted));
        // The compiled authority fence is the sole key source; callers cannot supply keys or projections.
        string keys = string.Join(", ", VisitorAccessCapabilityResolver.AuthoritySettingKeys
            .Select(value => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'"));
        string overridesFunction = OverridesFunctionSql(context);
        string participationFunction = ParticipationFunctionSql(context);
        return $$"""
            DO $roles$
            DECLARE role_name text;
            BEGIN
                FOREACH role_name IN ARRAY ARRAY['{{OwnerRole}}', '{{MigratorRole}}', '{{GlobalRole}}'] LOOP
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = role_name) THEN
                        EXECUTE format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', role_name);
                    END IF;
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = role_name
                        AND (rolcanlogin OR rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication OR rolbypassrls)) THEN
                        RAISE EXCEPTION 'visitor_policy_authority_not_ready';
                    END IF;
                END LOOP;
                IF pg_has_role('{{GlobalRole}}', '{{OwnerRole}}', 'MEMBER')
                   OR pg_has_role('{{GlobalRole}}', '{{MigratorRole}}', 'MEMBER') THEN
                    RAISE EXCEPTION 'visitor_policy_authority_not_ready';
                END IF;
                IF EXISTS (
                    WITH RECURSIVE memberships AS (
                        SELECT roleid FROM pg_catalog.pg_auth_members
                        WHERE member = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = CURRENT_USER)
                        UNION SELECT edge.roleid FROM pg_catalog.pg_auth_members edge
                        JOIN memberships inherited ON edge.member = inherited.roleid)
                    SELECT 1 FROM memberships WHERE roleid =
                        (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{{GlobalRole}}')) THEN
                    RAISE EXCEPTION 'visitor_policy_migration_authority_invalid';
                END IF;
            END; $roles$;
            GRANT {{OwnerRole}} TO {{MigratorRole}};
            GRANT {{MigratorRole}} TO CURRENT_USER;
            GRANT USAGE, CREATE ON SCHEMA {{schema}} TO {{OwnerRole}};
            GRANT USAGE ON SCHEMA {{schema}} TO {{GlobalRole}};
            REVOKE ALL ON {{overrides}}, {{participation}}, {{events}} FROM {{OwnerRole}};
            GRANT SELECT ({{overridesColumns}}) ON {{overrides}} TO {{OwnerRole}};
            GRANT SELECT ({{participationId}}, {{participationTenant}}, {{participationDeleted}}, {{identity}})
                ON {{participation}} TO {{OwnerRole}};
            GRANT SELECT ({{eventId}}, {{eventTenant}}, {{eventDeleted}}) ON {{events}} TO {{OwnerRole}};
            DROP POLICY IF EXISTS "{{PolicyName}}" ON {{overrides}};
            CREATE POLICY "{{PolicyName}}" ON {{overrides}} FOR SELECT TO {{OwnerRole}}
                USING ({{key}} IN ({{keys}}));
            DROP POLICY IF EXISTS "{{PolicyName}}" ON {{participation}};
            CREATE POLICY "{{PolicyName}}" ON {{participation}} FOR SELECT TO {{OwnerRole}}
                USING ({{identity}} = {{(int)IdentityAccessModeEnum.AccountRequired}} AND NOT {{participationDeleted}});
            DROP POLICY IF EXISTS "{{PolicyName}}" ON {{events}};
            CREATE POLICY "{{PolicyName}}" ON {{events}} FOR SELECT TO {{OwnerRole}}
                USING (NOT {{eventDeleted}});
            DO $install$
            DECLARE previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{{OwnerRole}}', true);
                EXECUTE $ddl$
                CREATE OR REPLACE FUNCTION {{overridesFunction}}()
                RETURNS TABLE ({{overridesReturns}}) LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog
                AS $body$
                    SELECT {{overridesColumns}} FROM {{overrides}} overrides
                    WHERE overrides.{{key}} IN ({{keys}})
                    ORDER BY overrides.{{tenant}}, overrides.{{key}}
                $body$;
                CREATE OR REPLACE FUNCTION {{participationFunction}}()
                RETURNS TABLE ("Value" uuid) LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog
                AS $body$
                    SELECT DISTINCT participation.{{participationTenant}} FROM {{participation}} participation
                    JOIN {{events}} source ON source.{{eventId}} = participation.{{participationId}}
                        AND source.{{eventTenant}} = participation.{{participationTenant}}
                    WHERE participation.{{identity}} = {{(int)IdentityAccessModeEnum.AccountRequired}}
                        AND NOT participation.{{participationDeleted}} AND NOT source.{{eventDeleted}}
                $body$;
                $ddl$;
                EXECUTE $acl$
                    REVOKE ALL ON FUNCTION {{overridesFunction}}(), {{participationFunction}}() FROM PUBLIC, {{GlobalRole}};
                    GRANT EXECUTE ON FUNCTION {{overridesFunction}}(), {{participationFunction}}() TO {{GlobalRole}};
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

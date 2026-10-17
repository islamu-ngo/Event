using Explore.Application.Settings;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Separates fixed-key policy readers from setting-mutation control reconciliation.
/// Native setting triggers bind control history to the original connection and transaction.
/// Neither purpose grants arbitrary setting reads or private queue access.
/// </summary>
public static partial class PostgresEmailDeliveryPolicyContract
{
    public const string OwnerRole = "event_email_policy_owner";
    public const string MigratorRole = "event_email_policy_migrator";
    public const string RuntimeRole = "event_email_policy_runtime";
    public const string GlobalRole = "event_email_policy_global";
    public const string InstanceMutationRole = "event_email_policy_instance_mutation";
    public const string TenantMutationRole = "event_email_policy_tenant_mutation";
    private const string PolicyName = "email_delivery_policy_authority";

    public static string FunctionSql(ExploreDbContext context) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier("email_delivery_policy_overrides", Schema(context));

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
            return;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await using (var command = context.Database.GetDbConnection().CreateCommand())
        {
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = $"SELECT pg_catalog.to_regrole('{PostgresMixedSettingsContract.OwnerRole}') IS NOT NULL";
            if (await command.ExecuteScalarAsync(cancellationToken) is true)
                await PostgresMixedSettingsContract.RequireReadyAsync(context, enrollment, cancellationToken);
        }
        await context.Database.ExecuteSqlRawAsync(BuildSql(context, enrollment), cancellationToken);
        await context.Database.ExecuteSqlRawAsync(BuildControlsSql(context, enrollment), cancellationToken);
        await context.Database.ExecuteSqlRawAsync(BuildMutationGrantsSql(context), cancellationToken);
        await context.Database.ExecuteSqlRawAsync(BuildProcessorMutationSql(context, enrollment), cancellationToken);
        await context.Database.ExecuteSqlRawAsync(
            $"DO $ready$ BEGIN {PrivilegeAssertion(context, enrollment)} END $ready$;",
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    internal static Task<List<TenantSetting>> ReadAsync(ExploreDbContext context, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Email policy overrides require their owning transaction.");
        return context.TenantSettingOverrides
            .FromSqlRaw($"SELECT * FROM {FunctionSql(context)}()")
            .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public static string BuildSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        string schema = sql.DelimitIdentifier(Schema(context));
        string Table<T>() => sql.DelimitIdentifier(context.Model.FindEntityType(typeof(T))!.GetTableName()!, Schema(context));
        string Column<T>(string property)
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            return sql.DelimitIdentifier(entity.FindProperty(property)!.GetColumnName(table)!);
        }
        string overrides = Table<TenantSetting>();
        string tenants = Table<Tenant>();
        string tenantId = Column<Tenant>(nameof(Tenant.Id));
        string key = Column<TenantSetting>(nameof(TenantSetting.SettingKey));
        string owner = Column<TenantSetting>(nameof(TenantSetting.TenantId));
        string keys = string.Join(", ", EmailDeliverySettingKeys.All.Select(value => $"'{value.Replace("'", "''")}'"));
        (string Name, string Type)[] columns =
        [
            (nameof(TenantSetting.Id), "uuid"), (nameof(TenantSetting.TenantId), "uuid"),
            (nameof(TenantSetting.SettingKey), "text"), (nameof(TenantSetting.Value), "text"),
            (nameof(TenantSetting.IsLocked), "boolean"), (nameof(TenantSetting.CreatedAt), "timestamp with time zone"),
            (nameof(TenantSetting.CreatedBy), "uuid"), (nameof(TenantSetting.UpdatedAt), "timestamp with time zone"),
            (nameof(TenantSetting.UpdatedBy), "uuid")
        ];
        string projection = string.Join(", ", columns.Select(column => $"setting.{Column<TenantSetting>(column.Name)}"));
        string grants = string.Join(", ", columns.Select(column => Column<TenantSetting>(column.Name)));
        string returns = string.Join(", ", columns.Select(column => $"{Column<TenantSetting>(column.Name)} {column.Type}"));
        string function = FunctionSql(context);
        var contract = BuildPrivilegeContract(context, enrollment);
        string guard = PrivilegeAssertion(context, enrollment);
        return $$"""
            DO $roles$
            DECLARE role_name text; v_count integer;
            BEGIN
                IF session_user <> {{LoginLiteral(enrollment.MigratorLogin)}} THEN
                    RAISE EXCEPTION 'email_policy_migration_login_mismatch' USING ERRCODE = '42501';
                END IF;
                SELECT count(*) INTO v_count FROM pg_catalog.pg_roles WHERE rolname IN (
                    '{{OwnerRole}}','{{MigratorRole}}','{{RuntimeRole}}','{{GlobalRole}}',
                    '{{InstanceMutationRole}}','{{TenantMutationRole}}');
                IF v_count = 0 THEN
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_proc p
                        JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
                        WHERE n.nspname = {{LoginLiteral(Schema(context))}}
                            AND p.proname LIKE 'email_delivery_policy_%')
                        OR pg_catalog.to_regclass({{LoginLiteral($"{Schema(context)}.email_delivery_policy_mutation_receipts")}}) IS NOT NULL THEN
                        RAISE EXCEPTION 'email_policy_bootstrap_artifacts_present' USING ERRCODE = '55000';
                    END IF;
                    FOREACH role_name IN ARRAY ARRAY['{{OwnerRole}}','{{MigratorRole}}','{{RuntimeRole}}','{{GlobalRole}}',
                        '{{InstanceMutationRole}}','{{TenantMutationRole}}'] LOOP
                        EXECUTE format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', role_name);
                    END LOOP;
                    EXECUTE 'GRANT {{OwnerRole}} TO {{MigratorRole}} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE';
                    EXECUTE format('GRANT {{RuntimeRole}} TO %I WITH INHERIT TRUE, SET FALSE, ADMIN FALSE', {{LoginLiteral(enrollment.RuntimeLogin)}});
                    EXECUTE format('GRANT {{GlobalRole}} TO %I WITH INHERIT TRUE, SET FALSE, ADMIN FALSE', {{LoginLiteral(enrollment.GlobalLogin)}});
                    EXECUTE format('GRANT {{InstanceMutationRole}} TO %I WITH INHERIT TRUE, SET FALSE, ADMIN FALSE', {{LoginLiteral(enrollment.EmailInstanceMutationLogin)}});
                    EXECUTE format('GRANT {{TenantMutationRole}} TO %I WITH INHERIT TRUE, SET FALSE, ADMIN FALSE', {{LoginLiteral(enrollment.EmailTenantMutationLogin)}});
                    EXECUTE format('GRANT {{MigratorRole}} TO %I WITH INHERIT FALSE, SET TRUE, ADMIN FALSE', {{LoginLiteral(enrollment.MigratorLogin)}});
                    FOREACH role_name IN ARRAY ARRAY['{{OwnerRole}}','{{RuntimeRole}}','{{GlobalRole}}',
                        '{{InstanceMutationRole}}','{{TenantMutationRole}}'] LOOP
                        -- Creator administration is retained until the declared grants exist.
                        IF EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members membership
                            JOIN pg_catalog.pg_roles granted ON granted.oid = membership.roleid
                            JOIN pg_catalog.pg_roles member ON member.oid = membership.member
                            WHERE granted.rolname = role_name AND member.rolname = session_user) THEN
                            EXECUTE format('REVOKE %I FROM %I', role_name, session_user);
                        END IF;
                    END LOOP;
                ELSIF v_count <> 6 OR ({{PostgresPrivilegeGuard.BuildRoleGraphSql(contract)}}) IS NOT TRUE
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles role WHERE role.rolname IN (
                        '{{OwnerRole}}','{{MigratorRole}}','{{RuntimeRole}}','{{GlobalRole}}',
                        '{{InstanceMutationRole}}','{{TenantMutationRole}}')
                        AND pg_catalog.has_schema_privilege(role.oid,
                            pg_catalog.to_regnamespace({{LoginLiteral(Schema(context))}}), 'CREATE')) THEN
                    RAISE EXCEPTION 'email_policy_role_graph_invalid' USING ERRCODE = '55000';
                END IF;
            END; $roles$;
            GRANT USAGE, CREATE ON SCHEMA {{schema}} TO {{OwnerRole}};
            GRANT USAGE ON SCHEMA {{schema}} TO {{RuntimeRole}}, {{GlobalRole}},
                {{InstanceMutationRole}}, {{TenantMutationRole}};
            REVOKE ALL ON {{overrides}}, {{tenants}} FROM {{OwnerRole}}, {{RuntimeRole}}, {{GlobalRole}};
            GRANT SELECT ({{grants}}) ON {{overrides}} TO {{OwnerRole}};
            GRANT SELECT ({{tenantId}}) ON {{tenants}} TO {{OwnerRole}};
            DROP POLICY IF EXISTS "{{PolicyName}}" ON {{overrides}};
            CREATE POLICY "{{PolicyName}}" ON {{overrides}} FOR SELECT TO {{OwnerRole}}
                USING ({{key}} IN ({{keys}}));
            DO $install$
            DECLARE previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{{OwnerRole}}', true);
                EXECUTE $ddl$
                CREATE OR REPLACE FUNCTION {{function}}()
                RETURNS TABLE ({{returns}})
                LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
                AS $body$
                BEGIN
                    {{guard}}
                    {{RestrictedCallerSql}}
                    IF session_user NOT IN ({{LoginLiteral(enrollment.RuntimeLogin)}}, {{LoginLiteral(enrollment.GlobalLogin)}},
                        {{LoginLiteral(enrollment.EmailInstanceMutationLogin)}}) THEN
                        RAISE EXCEPTION 'email_policy_reader_login_mismatch' USING ERRCODE = '42501';
                    END IF;
                    RETURN QUERY SELECT {{projection}} FROM {{overrides}} setting
                    WHERE setting.{{key}} IN ({{keys}})
                        AND EXISTS (SELECT 1 FROM {{tenants}} tenant WHERE tenant.{{tenantId}} = setting.{{owner}});
                END;
                $body$;
                $ddl$;
                EXECUTE $acl$
                    REVOKE ALL ON FUNCTION {{function}}() FROM PUBLIC, {{RuntimeRole}}, {{GlobalRole}},
                        {{InstanceMutationRole}}, {{TenantMutationRole}};
                    GRANT EXECUTE ON FUNCTION {{function}}() TO {{RuntimeRole}}, {{GlobalRole}}, {{InstanceMutationRole}};
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

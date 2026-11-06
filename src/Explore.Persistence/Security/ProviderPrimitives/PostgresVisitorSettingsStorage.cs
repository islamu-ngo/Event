using System.Text.Json.Nodes;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>
/// Fixed visitor-row storage rights for the approved Standard Runtime/Global purpose.
/// No human authorization, policy evaluation, workflow state or completion lives in SQL.
/// </summary>
public static class PostgresVisitorSettingsStorage
{
    public const string OwnerRole = "event_visitor_settings_owner";
    public const string RuntimeRole = "event_visitor_settings_runtime";
    public const string GlobalRole = "event_visitor_settings_global";
    private const string ReadFunction = "visitor_settings_rows";
    private const string WriteFunction = "visitor_settings_row_write";

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql()) return;
        enrollment.Validate();
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = BuildSql(context, enrollment);
        await command.ExecuteNonQueryAsync(cancellationToken);
        if (owned is not null) await owned.CommitAsync(cancellationToken);
    }

    internal static async Task<JsonObject> ReadAsync(ExploreDbContext context, Guid? tenant,
        Action requireOwner, CancellationToken token)
    {
        requireOwner();
        await RequireReadyAsync(context, token);
        requireOwner();
        await using var command = Command(context);
        command.CommandText = $"SELECT {Name(context, ReadFunction)}(@tenant)::text";
        Add(command, "tenant", NpgsqlDbType.Uuid, tenant);
        string image = (string)(await command.ExecuteScalarAsync(token)
            ?? throw new UnauthorizedAccessException("The visitor storage snapshot is absent."));
        requireOwner();
        return JsonNode.Parse(image)!.AsObject();
    }

    internal static async Task WriteAsync(ExploreDbContext context, Guid? tenant, Type type,
        JsonObject? before, JsonObject? after, Action requireOwner, CancellationToken token)
    {
        requireOwner();
        await RequireReadyAsync(context, token);
        requireOwner();
        await using var command = Command(context);
        command.CommandText = $"SELECT {Name(context, WriteFunction)}(@tenant,@family,@before,@after)";
        Add(command, "tenant", NpgsqlDbType.Uuid, tenant);
        Add(command, "family", NpgsqlDbType.Text, type.Name);
        Add(command, "before", NpgsqlDbType.Jsonb, before?.ToJsonString());
        Add(command, "after", NpgsqlDbType.Jsonb, after?.ToJsonString());
        await command.ExecuteNonQueryAsync(token);
        requireOwner();
    }

    private static async Task RequireReadyAsync(ExploreDbContext context, CancellationToken token)
    {
        var bodies = Bodies(context);
        await using var command = Command(context);
        command.CommandText = """
            SELECT count(*) FROM pg_catalog.pg_proc p
            JOIN pg_catalog.pg_roles r ON r.oid = p.proowner
            WHERE ((p.oid = pg_catalog.to_regprocedure(@read) AND p.prosrc = @read_body)
                OR (p.oid = pg_catalog.to_regprocedure(@write) AND p.prosrc = @write_body))
                AND r.rolname = @owner AND NOT r.rolcanlogin AND NOT r.rolsuper AND NOT r.rolbypassrls
                AND NOT r.rolcreatedb AND NOT r.rolcreaterole AND NOT r.rolreplication
                AND p.prosecdef AND p.proconfig = ARRAY['search_path=pg_catalog']::text[]
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(
                    COALESCE(p.proacl,pg_catalog.acldefault('f',p.proowner))) a
                    WHERE a.grantee = 0 AND a.privilege_type = 'EXECUTE')
            """;
        Add(command, "read", NpgsqlDbType.Text, Name(context, ReadFunction) + "(uuid)");
        Add(command, "write", NpgsqlDbType.Text, Name(context, WriteFunction) + "(uuid,text,jsonb,jsonb)");
        Add(command, "read_body", NpgsqlDbType.Text, bodies.Read);
        Add(command, "write_body", NpgsqlDbType.Text, bodies.Write);
        Add(command, "owner", NpgsqlDbType.Text, OwnerRole);
        if (Convert.ToInt64(await command.ExecuteScalarAsync(token)) != 2)
            throw new UnauthorizedAccessException("The fixed visitor storage catalog is not ready.");
    }

    public static string BuildSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        var sql = context.GetService<ISqlGenerationHelper>();
        string Identifier(string value) => sql.DelimitIdentifier(value);
        string schema = Identifier(Schema(context));
        var bodies = Bodies(context);
        var settingTables = new[] { typeof(SystemSetting), typeof(TenantSetting) };
        string settingRights = string.Join("\n", settingTables.Select(type =>
        {
            var model = context.Model.FindEntityType(type)!;
            var table = StoreObjectIdentifier.Table(model.GetTableName()!, model.GetSchema());
            string columns = string.Join(",", model.GetProperties().Select(property => Identifier(property.GetColumnName(table)!)));
            return $"GRANT SELECT ({columns}), INSERT ({columns}), UPDATE ({columns}) ON {Table(context, type)} TO {OwnerRole}; "
                + $"GRANT DELETE ON {Table(context, type)} TO {OwnerRole};";
        }));
        string Keys() => string.Join(",", VisitorAccessCapabilityResolver.AuthoritySettingKeys.Select(Text));
        string policies = string.Join("\n", new[] { typeof(SystemSetting), typeof(TenantSetting),
            typeof(EventParticipationConfiguration), typeof(Explore.Domain.Event) }.Select(type =>
        {
            string predicate = type == typeof(SystemSetting) || type == typeof(TenantSetting)
                ? $"{Column(context, type, "SettingKey")} COLLATE \"C\" IN ({Keys()})" : "TRUE";
            if (type != typeof(SystemSetting))
                predicate += $" AND ({Column(context, type, "TenantId")} = NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid"
                    + $" OR (NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'') IS NULL AND pg_catalog.pg_has_role(session_user,{Text(GlobalRole)},'MEMBER')))";
            string policy = Identifier("visitor_settings_storage");
            return $"DROP POLICY IF EXISTS {policy} ON {Table(context, type)}; "
                + $"CREATE POLICY {policy} ON {Table(context, type)} FOR {(settingTables.Contains(type) ? "ALL" : "SELECT")} TO {OwnerRole} USING ({predicate})"
                + (settingTables.Contains(type) ? $" WITH CHECK ({predicate})" : "") + ";";
        }));
        string participationColumns = string.Join(",", new[] { "Id", "TenantId", "IsDeleted", "IdentityAccessModeId" }
            .Select(property => Column(context, typeof(EventParticipationConfiguration), property)));
        string eventColumns = string.Join(",", new[] { "Id", "TenantId", "IsDeleted" }
            .Select(property => Column(context, typeof(Explore.Domain.Event), property)));
        string projectionColumns = string.Join(",", context.Model.FindEntityType(typeof(ManagedTenantProvisioningPolicySetting))!
            .GetProperties().Select(property => Column(context, typeof(ManagedTenantProvisioningPolicySetting), property.Name)));
        string projectionUpdates = string.Join(",", new[] { "IsPresent", "IsLocked", "Value", "Revision" }
            .Select(property => Column(context, typeof(ManagedTenantProvisioningPolicySetting), property)));
        return $$"""
            DO $roles$ DECLARE selected text; BEGIN
                FOREACH selected IN ARRAY ARRAY[{{Text(OwnerRole)}},{{Text(RuntimeRole)}},{{Text(GlobalRole)}}] LOOP
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname=selected) THEN
                        EXECUTE format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS',selected);
                    END IF;
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname=selected
                        AND (rolcanlogin OR rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication OR rolbypassrls)) THEN
                        RAISE EXCEPTION 'visitor_storage_role_invalid';
                    END IF;
                END LOOP;
                IF pg_catalog.pg_has_role({{Text(RuntimeRole)}},{{Text(OwnerRole)}},'MEMBER')
                    OR pg_catalog.pg_has_role({{Text(GlobalRole)}},{{Text(OwnerRole)}},'MEMBER') THEN
                    RAISE EXCEPTION 'visitor_storage_membership_invalid';
                END IF;
            END $roles$;
            GRANT {{OwnerRole}} TO {{Identifier(enrollment.MigratorLogin)}};
            GRANT {{RuntimeRole}} TO {{Identifier(enrollment.RuntimeLogin)}};
            GRANT {{GlobalRole}} TO {{Identifier(enrollment.GlobalLogin)}};
            GRANT USAGE,CREATE ON SCHEMA {{schema}} TO {{OwnerRole}};
            GRANT USAGE ON SCHEMA {{schema}} TO {{Identifier(enrollment.RuntimeLogin)}},{{Identifier(enrollment.GlobalLogin)}};
            {{settingRights}}
            GRANT SELECT ({{participationColumns}}) ON {{Table(context,typeof(EventParticipationConfiguration))}} TO {{OwnerRole}};
            GRANT SELECT ({{eventColumns}}) ON {{Table(context,typeof(Explore.Domain.Event))}} TO {{OwnerRole}};
            GRANT SELECT ({{projectionColumns}}), UPDATE ({{projectionUpdates}})
                ON {{Table(context,typeof(ManagedTenantProvisioningPolicySetting))}} TO {{Identifier(enrollment.GlobalLogin)}};
            {{policies}}
            DO $install$ DECLARE previous text := pg_catalog.current_setting('role'); BEGIN
                PERFORM pg_catalog.set_config('role',{{Text(OwnerRole)}},true);
                EXECUTE $ddl$
                    CREATE OR REPLACE FUNCTION {{Name(context,ReadFunction)}}(p_tenant uuid)
                    RETURNS jsonb LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
                    AS $body${{bodies.Read}}$body$;
                    CREATE OR REPLACE FUNCTION {{Name(context,WriteFunction)}}(p_tenant uuid,p_family text,p_before jsonb,p_after jsonb)
                    RETURNS void LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog
                    AS $body${{bodies.Write}}$body$;
                $ddl$;
                PERFORM pg_catalog.set_config('role',previous,true);
            END $install$;
            REVOKE ALL ON FUNCTION {{Name(context,ReadFunction)}}(uuid),{{Name(context,WriteFunction)}}(uuid,text,jsonb,jsonb) FROM PUBLIC;
            GRANT EXECUTE ON FUNCTION {{Name(context,ReadFunction)}}(uuid),{{Name(context,WriteFunction)}}(uuid,text,jsonb,jsonb) TO {{Identifier(enrollment.RuntimeLogin)}},{{Identifier(enrollment.GlobalLogin)}};
            REVOKE CREATE ON SCHEMA {{schema}} FROM {{OwnerRole}};
            """;
    }

    private static (string Read, string Write) Bodies(ExploreDbContext context)
    {
        string guard = $$"""
            IF current_user <> {{Text(OwnerRole)}} OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
                OR pg_catalog.current_setting('session_replication_role') <> 'origin'
                OR NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid IS DISTINCT FROM p_tenant
                OR (p_tenant IS NULL AND NOT pg_catalog.pg_has_role(session_user,{{Text(GlobalRole)}},'MEMBER'))
                OR (p_tenant IS NOT NULL AND NOT pg_catalog.pg_has_role(session_user,{{Text(RuntimeRole)}},'MEMBER')) THEN
                RAISE EXCEPTION 'visitor_storage_owner_required' USING ERRCODE='42501';
            END IF;
            """;
        string systems = Table(context, typeof(SystemSetting));
        string tenants = Table(context, typeof(TenantSetting));
        string participation = Table(context, typeof(EventParticipationConfiguration));
        string events = Table(context, typeof(Explore.Domain.Event));
        string C(Type type, string alias, string property) => alias + "." + Column(context, type, property);
        string Key(Type type, string alias) => C(type, alias, "SettingKey") + " COLLATE \"C\" IN ("
            + string.Join(",", VisitorAccessCapabilityResolver.AuthoritySettingKeys.Select(Text)) + ")";
        string read = $$"""
            BEGIN
                {{guard}}
                RETURN pg_catalog.jsonb_build_object(
                    'SystemSetting',COALESCE((SELECT pg_catalog.jsonb_agg({{Image(context,typeof(SystemSetting),"s")}} ORDER BY {{C(typeof(SystemSetting),"s","Id")}})
                        FROM {{systems}} s WHERE {{Key(typeof(SystemSetting),"s")}}),'[]'::jsonb),
                    'TenantSetting',COALESCE((SELECT pg_catalog.jsonb_agg({{Image(context,typeof(TenantSetting),"t")}} ORDER BY {{C(typeof(TenantSetting),"t","Id")}})
                        FROM {{tenants}} t WHERE {{Key(typeof(TenantSetting),"t")}} AND (p_tenant IS NULL OR {{C(typeof(TenantSetting),"t","TenantId")}}=p_tenant)),'[]'::jsonb),
                    'AccountRequiredTenantIds',COALESCE((SELECT pg_catalog.jsonb_agg(required.id ORDER BY required.id) FROM
                        (SELECT DISTINCT {{C(typeof(EventParticipationConfiguration),"p","TenantId")}} AS id FROM {{participation}} p
                        JOIN {{events}} e ON {{C(typeof(Explore.Domain.Event),"e","Id")}}={{C(typeof(EventParticipationConfiguration),"p","Id")}}
                            AND {{C(typeof(Explore.Domain.Event),"e","TenantId")}}={{C(typeof(EventParticipationConfiguration),"p","TenantId")}}
                        WHERE NOT {{C(typeof(EventParticipationConfiguration),"p","IsDeleted")}} AND NOT {{C(typeof(Explore.Domain.Event),"e","IsDeleted")}}
                            AND {{C(typeof(EventParticipationConfiguration),"p","IdentityAccessModeId")}}={{(int)IdentityAccessModeEnum.AccountRequired}}
                            AND (p_tenant IS NULL OR {{C(typeof(EventParticipationConfiguration),"p","TenantId")}}=p_tenant)) required),'[]'::jsonb));
            END
            """;
        string branches = string.Join("\n", new[] { typeof(SystemSetting), typeof(TenantSetting) }.Select(type =>
        {
            var model = context.Model.FindEntityType(type)!;
            var table = StoreObjectIdentifier.Table(model.GetTableName()!, model.GetSchema());
            var properties = model.GetProperties().ToArray();
            string Col(IProperty property) => context.GetService<ISqlGenerationHelper>().DelimitIdentifier(property.GetColumnName(table)!);
            string Map(string parameter) => "pg_catalog.jsonb_build_object(" + string.Join(",",
                properties.Select(property => $"{Text(property.GetColumnName(table)! )},{parameter}->{Text(property.Name)}")) + ")";
            string relation = Table(context, type);
            string row(string parameter) => $"pg_catalog.jsonb_populate_record(NULL::{relation},{Map(parameter)})";
            string predicate = string.Join(" AND ", properties.Select(property =>
                property.ClrType == typeof(string) && property.GetColumnType() != "jsonb"
                    ? $"t.{Col(property)} COLLATE \"C\" IS NOT DISTINCT FROM old.{Col(property)} COLLATE \"C\""
                    : $"t.{Col(property)} IS NOT DISTINCT FROM old.{Col(property)}"));
            string columns = string.Join(",", properties.Select(Col));
            string updates = string.Join(",", properties.Select(property => $"{Col(property)}=next.{Col(property)}"));
            string scope = type == typeof(SystemSetting) ? "p_tenant IS NOT NULL"
                : "(p_tenant IS NULL OR (COALESCE(p_after,p_before)->>'TenantId')::uuid IS DISTINCT FROM p_tenant)";
            return $$"""
                {{(type == typeof(SystemSetting) ? "IF" : "ELSIF")}} p_family={{Text(type.Name)}} THEN
                    IF {{scope}} THEN RAISE EXCEPTION 'visitor_storage_scope_invalid' USING ERRCODE='42501'; END IF;
                    IF p_before IS NULL THEN
                        INSERT INTO {{relation}} ({{columns}}) SELECT {{columns}} FROM {{row("p_after")}} next;
                    ELSIF p_after IS NULL THEN
                        DELETE FROM {{relation}} t USING {{row("p_before")}} old WHERE {{predicate}};
                    ELSE
                        UPDATE {{relation}} t SET {{updates}} FROM {{row("p_before")}} old,{{row("p_after")}} next WHERE {{predicate}};
                    END IF;
                    GET DIAGNOSTICS changed = ROW_COUNT;
                """;
        }));
        string write = $$"""
            DECLARE changed integer;
            BEGIN
                {{guard}}
                IF pg_catalog.current_setting('transaction_isolation') <> 'serializable'
                    OR (p_before IS NULL AND p_after IS NULL)
                    OR NOT ((COALESCE(p_after,p_before)->>'SettingKey') COLLATE "C" = ANY(ARRAY[{{string.Join(",",VisitorAccessCapabilityResolver.AuthoritySettingKeys.Select(Text))}}]::text[]))
                    OR (p_before IS NOT NULL AND p_after IS NOT NULL AND
                        (p_before->'Id' IS DISTINCT FROM p_after->'Id' OR p_before->'SettingKey' IS DISTINCT FROM p_after->'SettingKey'
                            OR p_before->'TenantId' IS DISTINCT FROM p_after->'TenantId')) THEN
                    RAISE EXCEPTION 'visitor_storage_row_invalid' USING ERRCODE='42501';
                END IF;
                {{branches}}
                ELSE RAISE EXCEPTION 'visitor_storage_family_invalid' USING ERRCODE='42501'; END IF;
                IF changed <> 1 THEN RAISE EXCEPTION 'visitor_storage_row_changed' USING ERRCODE='40001'; END IF;
            END
            """;
        return (read, write);
    }

    private static string Image(ExploreDbContext context, Type type, string alias) =>
        "pg_catalog.jsonb_build_object(" + string.Join(",", context.Model.FindEntityType(type)!.GetProperties()
            .Select(property => $"{Text(property.Name)},{alias}.{Column(context,type,property.Name)}")) + ")";

    private static string Schema(ExploreDbContext context) => new HumanPrincipalSourceSql(context).Schema;
    private static string Name(ExploreDbContext context, string name) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(name, Schema(context));
    private static string Table(ExploreDbContext context, Type type)
    {
        var model = context.Model.FindEntityType(type)!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(model.GetTableName()!, model.GetSchema());
    }
    private static string Column(ExploreDbContext context, Type type, string property)
    {
        var model = context.Model.FindEntityType(type)!;
        var table = StoreObjectIdentifier.Table(model.GetTableName()!, model.GetSchema());
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(model.FindProperty(property)!.GetColumnName(table)!);
    }
    private static string Text(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    private static System.Data.Common.DbCommand Command(ExploreDbContext context)
    {
        var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        return command;
    }
    private static void Add(System.Data.Common.DbCommand command, string name, NpgsqlDbType type, object? value) =>
        command.Parameters.Add(new NpgsqlParameter(name,type) { Value = value ?? DBNull.Value });
}

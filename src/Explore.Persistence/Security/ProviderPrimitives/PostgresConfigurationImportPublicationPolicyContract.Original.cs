using System.Text.Json;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

public static partial class PostgresConfigurationImportPublicationPolicyContract
{
    internal static async Task<HumanConfigurationImportPublicationStore.Image> ReadOriginalAsync(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, CancellationToken token)
    {
        ValidateOwner(context);
        await AttestOriginalAsync(context, enrollment, token);
        using var result = await ExecuteAsync(context, "source", token, Parameter("tenant", NpgsqlDbType.Uuid, null));
        return result.RootElement.Deserialize<HumanConfigurationImportPublicationStore.Image>()
            ?? throw new UnauthorizedAccessException("Original import publication data is absent.");
    }

    internal static async Task WriteOriginalAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        Guid? sourceTenant, HumanConfigurationImportPublication publication,
        HumanConfigurationImportPublicationStore.Image before, HumanConfigurationImportPublicationStore.Image after,
        CancellationToken token)
    {
        ValidateOwner(context);
        await AttestOriginalAsync(context, enrollment, token);
        string mutations = JsonSerializer.Serialize(publication.Mutations.OrderBy(mutation => KeyOrder(mutation.Key))
            .Select(mutation => new
            {
                key = mutation.Key,
                kind = "set",
                value = mutation.JsonValue,
                tenant = publication.TenantId,
                locked = mutation.IsLocked,
                id = publication.TenantId is { } tenant
                    ? (Guid?)after.Tenants.Single(row => row.Tenant == tenant && row.Key == mutation.Key).Id : null
            }));
        using var result = await ExecuteAsync(context, "write_original", token,
            Parameter("source_tenant", NpgsqlDbType.Uuid, sourceTenant),
            Parameter("tenant", NpgsqlDbType.Uuid, publication.TenantId),
            Parameter("mutations", NpgsqlDbType.Jsonb, mutations),
            Parameter("actor", NpgsqlDbType.Uuid, publication.Subject.PrincipalId),
            Parameter("at", NpgsqlDbType.TimestampTz, publication.OccurredAt),
            Parameter("before", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(before)),
            Parameter("after", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(after)));
    }

    private static async Task AttestOriginalAsync(ExploreDbContext context,
        PostgresNativeQueueEnrollment enrollment, CancellationToken token)
    {
        enrollment.Validate();
        _ = BuildSql(context, out var functions);
        foreach (var function in functions)
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
            command.CommandText = """
                SELECT CURRENT_USER = SESSION_USER AND SESSION_USER = @login
                    AND p.prosrc = @body AND p.proowner = owner.oid AND NOT owner.rolcanlogin
                    AND p.prosecdef AND p.provolatile = 'v' AND p.proparallel = 'u'
                    AND NOT p.proleakproof AND NOT p.proisstrict AND NOT p.proretset AND p.prokind = 'f'
                    AND p.proargdefaults IS NULL AND p.proconfig = ARRAY['search_path=pg_catalog']::text[]
                    AND p.prolang = (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql')
                    AND p.prorettype = @returns::pg_catalog.regtype
                    AND pg_catalog.has_function_privilege(@login, p.oid, 'EXECUTE') = @executable
                FROM pg_catalog.pg_proc p
                JOIN pg_catalog.pg_roles owner ON owner.rolname = @owner
                WHERE p.oid = pg_catalog.to_regprocedure(@signature)
                """;
            command.Parameters.Add(Parameter("login", NpgsqlDbType.Text, enrollment.GlobalLogin));
            command.Parameters.Add(Parameter("owner", NpgsqlDbType.Text, OwnerRole));
            command.Parameters.Add(Parameter("signature", NpgsqlDbType.Text,
                FunctionSql(context, function.Name) + "(" + function.Signature + ")"));
            command.Parameters.Add(Parameter("body", NpgsqlDbType.Text,
                function.Body.Replace("__STATE_MIGRATOR_LOGIN__", Text(enrollment.MigratorLogin), StringComparison.Ordinal)));
            command.Parameters.Add(Parameter("returns", NpgsqlDbType.Text,
                function.Name == "assert_authority" ? "pg_catalog.void" : "pg_catalog.jsonb"));
            command.Parameters.Add(Parameter("executable", NpgsqlDbType.Boolean, function.Name != "write"));
            if (await command.ExecuteScalarAsync(token) is not true)
                throw new UnauthorizedAccessException("The independently expected original import native function changed.");
        }
    }

    private static string OriginalSourceBody(ExploreDbContext context)
    {
        string S(string property) => Column(context, typeof(SystemSetting), property);
        string T(string property) => Column(context, typeof(TenantSetting), property);
        static string Ticks(string column) => $"((EXTRACT(EPOCH FROM {column}) * 10000000)::bigint + 621355968000000000)";
        string systemColumns = string.Join(", ", new[] { "Id", "SettingKey", "Value", "IsLocked",
            "SettingValueTypeId", "AllowedValues", "Description", "Category", "DisplayOrder",
            "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy" }.Select(property => "s." + S(property)));
        string tenantColumns = string.Join(", ", new[] { "Id", "TenantId", "SettingKey", "Value", "IsLocked",
            "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy" }.Select(property => "t." + T(property)));
        return $"""
            DECLARE systems jsonb; tenants jsonb;
            BEGIN
                PERFORM {FunctionSql(context, "assert_authority")}();
                IF p_tenant = '00000000-0000-0000-0000-000000000000'::uuid THEN
                    RAISE EXCEPTION 'configuration_import_publication_source_target' USING ERRCODE = '22023';
                END IF;
                WITH locked AS MATERIALIZED (
                    SELECT {systemColumns} FROM {Table(context, typeof(SystemSetting))} s
                    WHERE s.{S("SettingKey")} = ANY ({FixedKeysSql()})
                    ORDER BY s.{S("SettingKey")} COLLATE "C" FOR UPDATE)
                SELECT COALESCE(pg_catalog.jsonb_agg(pg_catalog.jsonb_build_object(
                    'Id', s.{S("Id")}, 'Key', s.{S("SettingKey")}, 'Value', s.{S("Value")},
                    'Locked', s.{S("IsLocked")}, 'Type', s.{S("SettingValueTypeId")},
                    'Allowed', s.{S("AllowedValues")}::text, 'Description', s.{S("Description")},
                    'Category', s.{S("Category")}, 'Order', s.{S("DisplayOrder")},
                    'Created', {Ticks("s." + S("CreatedAt"))}, 'Creator', s.{S("CreatedBy")},
                    'Updated', {Ticks("s." + S("UpdatedAt"))}, 'Updater', s.{S("UpdatedBy")})
                    ORDER BY s.{S("SettingKey")} COLLATE "C"), '[]'::jsonb) INTO systems FROM locked s;
                WITH locked AS MATERIALIZED (
                    SELECT {tenantColumns} FROM {Table(context, typeof(TenantSetting))} t
                    WHERE t.{T("SettingKey")} = ANY ({FixedKeysSql()})
                        AND (p_tenant IS NULL OR t.{T("TenantId")} = p_tenant)
                    ORDER BY t.{T("TenantId")}, t.{T("SettingKey")} COLLATE "C" FOR UPDATE)
                SELECT COALESCE(pg_catalog.jsonb_agg(pg_catalog.jsonb_build_object(
                    'Id', t.{T("Id")}, 'Tenant', t.{T("TenantId")}, 'Key', t.{T("SettingKey")},
                    'Value', t.{T("Value")}, 'Locked', t.{T("IsLocked")},
                    'Created', {Ticks("t." + T("CreatedAt"))}, 'Creator', t.{T("CreatedBy")},
                    'Updated', {Ticks("t." + T("UpdatedAt"))}, 'Updater', t.{T("UpdatedBy")})
                    ORDER BY t.{T("TenantId")}, t.{T("SettingKey")} COLLATE "C"), '[]'::jsonb) INTO tenants FROM locked t;
                RETURN pg_catalog.jsonb_build_object('Systems', systems, 'Tenants', tenants);
            END;
            """;
    }

    private static string OriginalWriteBody(ExploreDbContext context) => $"""
        DECLARE result jsonb;
        BEGIN
            PERFORM {FunctionSql(context, "assert_authority")}();
            IF p_before IS NULL OR p_after IS NULL
                OR p_source_tenant IS NOT NULL AND p_source_tenant IS DISTINCT FROM p_tenant
                OR {FunctionSql(context, "source")}(p_source_tenant) IS DISTINCT FROM p_before THEN
                RAISE EXCEPTION 'configuration_import_publication_original_changed' USING ERRCODE = '40001';
            END IF;
            result := {FunctionSql(context, "write")}(p_tenant, p_mutations, p_actor, p_at);
            IF {FunctionSql(context, "source")}(p_source_tenant) IS DISTINCT FROM p_after THEN
                RAISE EXCEPTION 'configuration_import_publication_postimage_changed' USING ERRCODE = '40001';
            END IF;
            RETURN result;
        END;
        """;
}

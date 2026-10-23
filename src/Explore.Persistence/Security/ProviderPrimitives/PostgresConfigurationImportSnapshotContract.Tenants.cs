using Explore.Application.Features.ConfigurationManifest.Validation;
using Explore.Domain;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

public static partial class PostgresConfigurationImportSnapshotContract
{
    public static async Task<IReadOnlyList<Tenant>> ReadActiveTenantsAsync(
        ExploreDbContext context, int maximumCount, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            maximumCount, ConfigurationManifestValidator.MaximumTenantCount + 1);
        var tenants = await ReadAsync(context, "tenants", null, null, RestoreTenant,
            cancellationToken, "\n" + BuildTenantReadBody(context) + "\n");
        return tenants.Take(maximumCount).ToArray();
    }

    public static async Task<IReadOnlyList<Tenant>> ReadTenantsBySlugsAsync(
        ExploreDbContext context, IReadOnlyCollection<string> slugs, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(slugs);
        string[] requested = slugs.ToArray();
        if (requested.Length > ConfigurationManifestValidator.MaximumTenantCount
            || requested.Any(string.IsNullOrWhiteSpace)
            || requested.Distinct(StringComparer.Ordinal).Count() != requested.Length)
            throw new ArgumentException("Import target slugs must be bounded, distinct and non-empty.", nameof(slugs));
        return await ReadAsync(context, "tenant_slugs", null, requested, RestoreTenant,
            cancellationToken, "\n" + BuildTenantReadBody(context, bySlugs: true) + "\n");
    }

    private static Tenant RestoreTenant(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        FullName = reader.GetString(1),
        Slug = reader.GetString(2),
        TenantStatusId = reader.GetInt32(3),
        TenantStatus = new TenantStatus
        {
            Id = reader.GetInt32(3),
            MasterCode = reader.GetString(4),
            FullName = reader.GetString(5),
            Description = reader.IsDBNull(6) ? null : reader.GetString(6),
            IsActiveState = reader.GetBoolean(7)
        }
    };

    private static string BuildTenantReadBody(ExploreDbContext context, bool bySlugs = false)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        string Table(Type type) => sql.DelimitIdentifier(
            context.Model.FindEntityType(type)!.GetTableName()!, Schema(context, type));
        string Column(Type type, string property)
        {
            var entity = context.Model.FindEntityType(type)!;
            return sql.DelimitIdentifier(entity.FindProperty(property)!.GetColumnName(
                StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!);
        }
        string T(string property) => Column(typeof(Tenant), property);
        string S(string property) => Column(typeof(TenantStatus), property);
        string softDelete = context.Model.FindEntityType(typeof(Tenant))!.FindProperty("IsDeleted") is null
            ? "" : $"AND NOT tenant.{T("IsDeleted")}";
        string selection = bySlugs
            ? $"tenant.{T("Slug")} COLLATE \"C\" = ANY (p_keys)"
            : $"status.{S("IsActiveState")}";
        return $"""
            BEGIN
                {(bySlugs ? $"""
                IF p_keys IS NULL OR cardinality(p_keys) > {ConfigurationManifestValidator.MaximumTenantCount}
                    OR EXISTS (SELECT 1 FROM unnest(p_keys) key WHERE key IS NULL OR btrim(key) = '')
                    OR cardinality(p_keys) <> (SELECT count(DISTINCT key COLLATE "C") FROM unnest(p_keys) key)
                THEN RAISE EXCEPTION 'configuration_import_target_slugs_invalid' USING ERRCODE = '22023';
                END IF;
                """ : "")}
                IF p_tenant IS NOT NULL OR current_user <> '{OwnerRole}'
                    OR current_setting('role') <> 'none'
                    OR NOT pg_catalog.pg_has_role(session_user, '{GlobalRole}', 'USAGE')
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname IN
                        ('{OwnerRole}', '{GlobalRole}')
                        AND (rolcanlogin OR rolsuper OR rolbypassrls OR rolcreatedb
                            OR rolcreaterole OR rolreplication OR rolinherit))
                    OR pg_catalog.pg_has_role(session_user, '{OwnerRole}', 'MEMBER')
                    OR pg_catalog.pg_has_role(session_user, '{MigratorRole}', 'MEMBER')
                THEN RAISE EXCEPTION 'configuration_import_tenant_source_not_ready' USING ERRCODE = '42501';
                END IF;
                RETURN QUERY
                SELECT tenant.{T("Id")}, tenant.{T("FullName")}::text,
                    tenant.{T("Slug")}::text, tenant.{T("TenantStatusId")},
                    status.{S("MasterCode")}::text, status.{S("FullName")}::text,
                    status.{S("Description")}::text, status.{S("IsActiveState")}
                FROM {Table(typeof(Tenant))} tenant
                JOIN {Table(typeof(TenantStatus))} status ON status.{S("Id")} = tenant.{T("TenantStatusId")}
                WHERE {selection} {softDelete}
                ORDER BY tenant.{T("Id")}
                LIMIT {ConfigurationManifestValidator.MaximumTenantCount + 1};
            END;
            """;
    }

    private static string BuildTenantReadInstallSql(ExploreDbContext context)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        string Table(Type type) => sql.DelimitIdentifier(
            context.Model.FindEntityType(type)!.GetTableName()!, Schema(context, type));
        string Column(Type type, string property)
        {
            var entity = context.Model.FindEntityType(type)!;
            return sql.DelimitIdentifier(entity.FindProperty(property)!.GetColumnName(
                StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!);
        }
        string[] fields = context.Model.FindEntityType(typeof(Tenant))!.FindProperty("IsDeleted") is null
            ? ["Id", "FullName", "Slug", "TenantStatusId"]
            : ["Id", "FullName", "Slug", "TenantStatusId", "IsDeleted"];
        string allowed = string.Join(",", fields.Select(field => Text(context.Model.FindEntityType(typeof(Tenant))!
            .FindProperty(field)!.GetColumnName(StoreObjectIdentifier.Table(
                context.Model.FindEntityType(typeof(Tenant))!.GetTableName()!,
                context.Model.FindEntityType(typeof(Tenant))!.GetSchema()))!)));
        return $"""
            GRANT USAGE ON SCHEMA {sql.DelimitIdentifier(Schema(context, typeof(Tenant)))},
                {sql.DelimitIdentifier(Schema(context, typeof(TenantStatus)))} TO {OwnerRole};
            DO $tenant_grants$
            BEGIN
                IF EXISTS (SELECT 1 FROM pg_catalog.pg_proc procedure
                        WHERE procedure.oid = to_regprocedure({Text(FunctionSql(context, "tenant_slugs") + "(uuid,text[])")})
                            AND (procedure.prosrc IS DISTINCT FROM {Text("\n" + BuildTenantReadBody(context, bySlugs: true) + "\n")}
                                OR NOT COALESCE(({BootstrapFunctionAuthorityPredicate()}), false)))
                    OR has_table_privilege('{OwnerRole}', {Text(Table(typeof(Tenant)))}::regclass,
                        'INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                    OR has_any_column_privilege('{OwnerRole}', {Text(Table(typeof(Tenant)))}::regclass,
                        'INSERT,UPDATE,REFERENCES')
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute attribute
                        WHERE attribute.attrelid = {Text(Table(typeof(Tenant)))}::regclass
                            AND attribute.attnum > 0 AND NOT attribute.attisdropped
                            AND attribute.attname NOT IN ({allowed})
                            AND has_column_privilege('{OwnerRole}', attribute.attrelid, attribute.attnum, 'SELECT'))
                    OR has_table_privilege('{GlobalRole}', {Text(Table(typeof(Tenant)))}::regclass,
                        'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                    OR has_any_column_privilege('{GlobalRole}', {Text(Table(typeof(Tenant)))}::regclass,
                        'SELECT,INSERT,UPDATE,REFERENCES')
                THEN RAISE EXCEPTION 'configuration_import_tenant_privilege_drift' USING ERRCODE = '55000';
                END IF;
            END; $tenant_grants$;
            GRANT SELECT ({string.Join(",", fields.Select(field => Column(typeof(Tenant), field)))})
                ON {Table(typeof(Tenant))} TO {OwnerRole};
            GRANT SELECT ({string.Join(",", new[] { "Id", "MasterCode", "FullName", "Description", "IsActiveState" }
                .Select(field => Column(typeof(TenantStatus), field)))})
                ON {Table(typeof(TenantStatus))} TO {OwnerRole};
            """;
    }

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}

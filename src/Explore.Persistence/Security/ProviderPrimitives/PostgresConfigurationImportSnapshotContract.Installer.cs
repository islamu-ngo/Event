using System.Security.Cryptography;
using System.Text;
using Explore.Domain;
using Explore.Domain.Settings.Documents;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

public static partial class PostgresConfigurationImportSnapshotContract
{
    public static async Task ApplyAsync(ExploreDbContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
            return;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync(BuildSql(context), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static string Schema(ExploreDbContext context, Type type) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? context.Model.FindEntityType(type)!.GetSchema() ?? "public";

    private static string FunctionSql(ExploreDbContext context, string operation) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            "configuration_import_snapshot_" + operation, Schema(context, typeof(TenantSetting)));

    private static string BuildSql(ExploreDbContext context)
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
        string C<T>(string property) => Column(typeof(T), property);
        string T<T>() => Table(typeof(T));
        string Text(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
        string Array(string[] values) => $"ARRAY[{string.Join(", ", values.Select(Text))}]::text[]";
        string settingCatalog = Array(SettingKeys);
        string documentCatalog = Array(DocumentKeys);
        string policies = T<PaidEventPolicyVersion>();
        string PolicyMatch<T>(string alias) => $"""
            {alias}.{C<T>("PaidEventPolicyVersionId")} = policy.{C<PaidEventPolicyVersion>("Id")}
            AND {alias}.{C<T>("PolicyScopeKey")} = policy.{C<PaidEventPolicyVersion>("PolicyScopeKey")}
            AND {alias}.{C<T>("TenantId")} IS NOT DISTINCT FROM policy.{C<PaidEventPolicyVersion>("TenantId")}
            """;
        string ChildPredicate<T>() => $"""
            EXISTS (SELECT 1 FROM {policies} policy
                WHERE policy.{C<PaidEventPolicyVersion>("IsActive")}
                  AND {PolicyMatch<T>(sql.DelimitIdentifier(context.Model.FindEntityType(typeof(T))!.GetTableName()!))})
            """;
        (string Property, string Type)[] settingColumns =
        [
            ("Id", "uuid"), ("TenantId", "uuid"), ("SettingKey", "text"), ("Value", "text"),
            ("IsLocked", "boolean"), ("CreatedAt", "timestamp with time zone"), ("CreatedBy", "uuid"),
            ("UpdatedAt", "timestamp with time zone"), ("UpdatedBy", "uuid")
        ];
        (string Property, string Type)[] documentColumns =
        [
            ("Id", "uuid"), ("TenantId", "uuid"), ("DocumentKey", "text"), ("SchemaVersion", "integer"),
            ("DefaultsVersion", "text"), ("PayloadJson", "text"), ("ConcurrencyStamp", "uuid"),
            ("CreatedAt", "timestamp with time zone"), ("CreatedBy", "uuid"),
            ("UpdatedAt", "timestamp with time zone"), ("UpdatedBy", "uuid")
        ];
        string[] policyColumns =
        [
            "Id", "TenantId", "PolicyScopeKey", "VersionNumber", "IsActive", "ActiveUniquenessSlot",
            "IsPaymentsEnabled", "RequiresLocalVerification", "DefaultCurrencyCode",
            "RequiresFirstPaidEventReview", "FarFutureReviewThresholdDays",
            "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy"
        ];
        string[] riskColumns =
        [
            "CurrencyCode", "PerEventSalesCeilingMinor", "PerEventSalesCountCeiling",
            "RollingOrganizerSalesCeilingMinor", "RollingOrganizerSalesCountCeiling",
            "RollingOrganizerWindowDays", "HighValueReviewThresholdMinor"
        ];
        (Type Type, string[] Columns, string Predicate)[] relations =
        [
            (typeof(TenantSetting), settingColumns.Select(column => column.Property).ToArray(),
                $"{C<TenantSetting>("SettingKey")} COLLATE \"C\" = ANY ({settingCatalog})"),
            (typeof(TenantSettingsDocument), documentColumns.Select(column => column.Property).ToArray(),
                $"{C<TenantSettingsDocument>("DocumentKey")} COLLATE \"C\" = ANY ({documentCatalog})"),
            (typeof(PaidEventPolicyVersion), policyColumns, C<PaidEventPolicyVersion>("IsActive")),
            (typeof(PaidEventPolicyAllowedOrganizerKind),
                ["TenantId", "PolicyScopeKey", "PaidEventPolicyVersionId", "Ordinal", "ActorTypeId"],
                ChildPredicate<PaidEventPolicyAllowedOrganizerKind>()),
            (typeof(PaidEventPolicyAllowedCurrency),
                ["TenantId", "PolicyScopeKey", "PaidEventPolicyVersionId", "Ordinal", "CurrencyCode"],
                ChildPredicate<PaidEventPolicyAllowedCurrency>()),
            (typeof(PaidEventPolicyRefundProtection),
                ["TenantId", "PolicyScopeKey", "PaidEventPolicyVersionId", "Ordinal", "RefundProtectionId"],
                ChildPredicate<PaidEventPolicyRefundProtection>()),
            (typeof(PaidEventPolicyCurrencyRiskLimitRow),
                ["TenantId", "PolicyScopeKey", "PaidEventPolicyVersionId", "Ordinal", .. riskColumns],
                ChildPredicate<PaidEventPolicyCurrencyRiskLimitRow>())
        ];
        var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model);
        string Fingerprint(Type type, string predicate) => "event-configuration-import-snapshot:v1:" +
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"{Schema(context, type)}:{context.Model.FindEntityType(type)!.GetTableName()}:{predicate}"))) + ":";
        string expected = string.Join(",\n", relations.Select(relation =>
        {
            var entity = context.Model.FindEntityType(relation.Type)!;
            var tenantTable = tenantTables.Single(table => table.TableName == entity.GetTableName());
            return $"({Text(Table(relation.Type))}::regclass, " +
                $"{Text(Fingerprint(relation.Type, relation.Predicate))}, " +
                $"{Text(PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(tenantTable, Schema(context, relation.Type)))})";
        }));
        string grants = string.Join("\n", relations.Select(relation =>
        {
            var entity = context.Model.FindEntityType(relation.Type)!;
            string allColumns = string.Join(", ", entity.GetProperties()
                .Select(property => Column(relation.Type, property.Name)).Distinct());
            string selected = string.Join(", ", relation.Columns.Select(property => Column(relation.Type, property)));
            return $"""
                REVOKE ALL ON TABLE {Table(relation.Type)} FROM {OwnerRole}, {GlobalRole};
                REVOKE ALL ({allColumns}) ON TABLE {Table(relation.Type)} FROM {OwnerRole}, {GlobalRole};
                GRANT SELECT ({selected}) ON TABLE {Table(relation.Type)} TO {OwnerRole};
                DROP POLICY IF EXISTS "{PolicyName}" ON {Table(relation.Type)};
                CREATE POLICY "{PolicyName}" ON {Table(relation.Type)}
                    FOR SELECT TO {OwnerRole} USING ({relation.Predicate});
                DO $stamp$
                BEGIN
                    EXECUTE (
                        SELECT format('COMMENT ON POLICY %I ON %s IS %L', p.polname,
                            {Text(Table(relation.Type))},
                            {Text(Fingerprint(relation.Type, relation.Predicate))} || md5(p.polqual::text))
                        FROM pg_catalog.pg_policy p
                        WHERE p.polrelid = {Text(Table(relation.Type))}::regclass AND p.polname = '{PolicyName}');
                END; $stamp$;
                """;
        }));
        string roleNames = $"'{OwnerRole}', '{MigratorRole}', '{GlobalRole}'";
        string elevated = "rolcanlogin OR rolsuper OR rolbypassrls OR rolcreatedb OR rolcreaterole OR rolreplication OR rolinherit";
        (Type Type, string[] Columns)[] readRelations = relations
            .Select(relation => (relation.Type, relation.Columns))
            .Concat(new (Type Type, string[] Columns)[]
            {
                (typeof(Tenant), context.Model.FindEntityType(typeof(Tenant))!.FindProperty("IsDeleted") is null
                    ? ["Id", "FullName", "Slug", "TenantStatusId"]
                    : ["Id", "FullName", "Slug", "TenantStatusId", "IsDeleted"]),
                (typeof(TenantStatus), ["Id", "MasterCode", "FullName", "Description", "IsActiveState"]),
                (typeof(ConfigurationManifestOperation), BootstrapColumns)
            }).ToArray();
        string protectedTables = string.Join(", ", readRelations.Select(relation => Text(Table(relation.Type)) + "::regclass"));
        string? targetSchema = context.GetService<IDbContextOptions>()
            .FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema;
        string schemaNames = string.Join(", ", context.Model.GetEntityTypes()
            .Where(entity => entity.GetTableName() is not null)
            .Select(entity => targetSchema ?? entity.GetSchema() ?? "public")
            .Distinct(StringComparer.Ordinal).Select(Text));
        string unsafeOwnership = $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_class relation
                JOIN pg_catalog.pg_namespace namespace ON namespace.oid = relation.relnamespace
                WHERE namespace.nspname IN ({schemaNames}) AND relation.relkind IN ('r', 'p')
                  AND relation.relowner IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roleNames})))
            """;
        string unsafeParents = $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members edge
                JOIN pg_catalog.pg_roles member ON member.oid = edge.member
                JOIN pg_catalog.pg_roles parent ON parent.oid = edge.roleid
                WHERE member.rolname IN ('{OwnerRole}', '{GlobalRole}')
                   OR member.rolname = '{MigratorRole}' AND parent.rolname <> '{OwnerRole}'
                   OR parent.rolname = '{OwnerRole}' AND member.rolname <> '{MigratorRole}')
            """;
        string unsafeTablePrivileges = $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_class relation
                JOIN pg_catalog.pg_namespace namespace ON namespace.oid = relation.relnamespace
                WHERE namespace.nspname IN ({schemaNames}) AND relation.relkind IN ('r', 'p')
                  AND (has_table_privilege('{GlobalRole}', relation.oid, 'SELECT, INSERT, UPDATE, DELETE, TRUNCATE, REFERENCES, TRIGGER')
                    OR has_any_column_privilege('{GlobalRole}', relation.oid, 'SELECT, INSERT, UPDATE, REFERENCES')
                    OR has_table_privilege('{OwnerRole}', relation.oid, 'SELECT, INSERT, UPDATE, DELETE, TRUNCATE, REFERENCES, TRIGGER')
                    OR has_any_column_privilege('{OwnerRole}', relation.oid, 'INSERT, UPDATE, REFERENCES')
                    OR relation.oid NOT IN ({protectedTables})
                        AND has_any_column_privilege('{OwnerRole}', relation.oid, 'SELECT')))
            """;
        string excludedColumns = string.Join(" OR ", readRelations.Select(relation =>
        {
            var selected = relation.Columns.ToHashSet(StringComparer.Ordinal);
            var excluded = context.Model.FindEntityType(relation.Type)!.GetProperties()
                .Where(property => !selected.Contains(property.Name)).ToArray();
            return excluded.Length == 0 ? "false" : string.Join(" OR ", excluded.Select(property =>
                $"has_column_privilege('{OwnerRole}', {Text(Table(relation.Type))}::regclass, " +
                $"{Text(property.GetColumnName(StoreObjectIdentifier.Table(
                    context.Model.FindEntityType(relation.Type)!.GetTableName()!,
                    context.Model.FindEntityType(relation.Type)!.GetSchema()))!)}, 'SELECT')"));
        }));
        string authorityGuard = $"""
            IF CURRENT_USER <> '{OwnerRole}'
                OR (SELECT count(*) FROM pg_catalog.pg_roles WHERE rolname IN ({roleNames})) <> 3
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname IN ({roleNames}) AND ({elevated}))
                OR {unsafeParents} OR {unsafeOwnership} OR {unsafeTablePrivileges}
                OR ({excludedColumns})
                OR EXISTS (
                    SELECT 1 FROM (VALUES {expected}) AS expected(relation_id, read_fingerprint, tenant_fingerprint)
                    LEFT JOIN pg_catalog.pg_class relation ON relation.oid = expected.relation_id
                    LEFT JOIN pg_catalog.pg_policy read_policy ON read_policy.polrelid = relation.oid
                        AND read_policy.polname = '{PolicyName}'
                    LEFT JOIN pg_catalog.pg_policy tenant_policy ON tenant_policy.polrelid = relation.oid
                        AND tenant_policy.polname = 'tenant_isolation'
                    WHERE relation.oid IS NULL OR NOT relation.relrowsecurity OR NOT relation.relforcerowsecurity
                        OR read_policy.oid IS NULL OR read_policy.polcmd <> 'r' OR NOT read_policy.polpermissive
                        OR read_policy.polwithcheck IS NOT NULL OR read_policy.polqual IS NULL
                        OR read_policy.polroles <> ARRAY[(SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{OwnerRole}')]
                        OR obj_description(read_policy.oid, 'pg_policy') IS DISTINCT FROM
                            expected.read_fingerprint || md5(read_policy.polqual::text)
                        OR tenant_policy.oid IS NULL OR tenant_policy.polcmd <> '*' OR NOT tenant_policy.polpermissive
                        OR tenant_policy.polroles <> ARRAY[0::oid] OR tenant_policy.polqual IS NULL
                        OR tenant_policy.polwithcheck IS NULL
                        OR obj_description(tenant_policy.oid, 'pg_policy') IS DISTINCT FROM
                            expected.tenant_fingerprint || md5(tenant_policy.polqual::text)
                                || ':' || md5(tenant_policy.polwithcheck::text)) THEN
                RAISE EXCEPTION 'configuration_import_snapshot_authority_not_ready' USING ERRCODE = '55000';
            END IF;
            """;
        string emptyUuid = "'00000000-0000-0000-0000-000000000000'::uuid";
        string KeyGuard(string[] keys) => $"""
            IF p_tenant IS NULL OR p_tenant = {emptyUuid}
                OR p_keys IS NULL OR cardinality(p_keys) > {keys.Length}
                OR COALESCE(array_ndims(p_keys), 1) <> 1
                OR EXISTS (SELECT 1 FROM unnest(p_keys) requested(key)
                    WHERE key IS NULL OR key COLLATE "C" <> ALL ({Array(keys)}))
                OR (SELECT count(DISTINCT key COLLATE "C") FROM unnest(p_keys) requested(key)) <> cardinality(p_keys) THEN
                RAISE EXCEPTION 'configuration_import_snapshot_target_invalid' USING ERRCODE = '22023';
            END IF;
            """;
        string ScalarFunction<T>(string operation, (string Property, string Type)[] columns, string key, string[] keys)
        {
            string returns = string.Join(", ", columns.Select(column => $"{C<T>(column.Property)} {column.Type}"));
            string projection = string.Join(", ", columns.Select(column => $"stored.{C<T>(column.Property)}::{column.Type}"));
            return $"""
                CREATE OR REPLACE FUNCTION {FunctionSql(context, operation)}(p_tenant uuid, p_keys text[])
                RETURNS TABLE ({returns}) LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
                AS $body$
                BEGIN
                    {KeyGuard(keys)}
                    {authorityGuard}
                    RETURN QUERY SELECT {projection} FROM {T<T>()} stored
                        WHERE stored.{C<T>("TenantId")} = p_tenant
                          AND stored.{C<T>(key)} COLLATE "C" = ANY ({Array(keys)})
                          AND stored.{C<T>(key)} COLLATE "C" = ANY (p_keys)
                        ORDER BY stored.{C<T>(key)} COLLATE "C", stored.{C<T>("Id")};
                END; $body$;
                """;
        }
        string ChildValues<T>(string property) => $"""
            COALESCE((SELECT jsonb_agg(child.{C<T>(property)} ORDER BY child.{C<T>("Ordinal")})
                FROM {T<T>()} child WHERE {PolicyMatch<T>("child")}), '[]'::jsonb)
            """;
        string parentJson = string.Join(",\n", policyColumns.Select(property =>
        {
            string value = $"policy.{C<PaidEventPolicyVersion>(property)}";
            if (property is "CreatedAt" or "UpdatedAt")
                value = $"to_char({value} AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI:SS.US\"Z\"')";
            return $"{Text(property)}, {value}";
        }));
        string riskJson = string.Join(", ", riskColumns.Select(property =>
            $"{Text(property)}, child.{C<PaidEventPolicyCurrencyRiskLimitRow>(property)}"));
        string policyFunction = $"""
            CREATE OR REPLACE FUNCTION {FunctionSql(context, "policy")}(p_tenant uuid)
            RETURNS SETOF text LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
            AS $body$
            BEGIN
                IF p_tenant = {emptyUuid} THEN
                    RAISE EXCEPTION 'configuration_import_snapshot_target_invalid' USING ERRCODE = '22023';
                END IF;
                {authorityGuard}
                RETURN QUERY SELECT jsonb_build_object(
                    {parentJson},
                    'AllowedOrganizerKindIds', {ChildValues<PaidEventPolicyAllowedOrganizerKind>("ActorTypeId")},
                    'AllowedCurrencyCodes', {ChildValues<PaidEventPolicyAllowedCurrency>("CurrencyCode")},
                    'RefundProtectionIds', {ChildValues<PaidEventPolicyRefundProtection>("RefundProtectionId")},
                    'CurrencyRiskLimits', COALESCE(
                        (SELECT jsonb_agg(jsonb_build_object({riskJson}) ORDER BY child.{C<PaidEventPolicyCurrencyRiskLimitRow>("Ordinal")})
                         FROM {T<PaidEventPolicyCurrencyRiskLimitRow>()} child
                         WHERE {PolicyMatch<PaidEventPolicyCurrencyRiskLimitRow>("child")}), '[]'::jsonb))::text
                FROM {policies} policy
                WHERE policy.{C<PaidEventPolicyVersion>("IsActive")}
                  AND policy.{C<PaidEventPolicyVersion>("TenantId")} IS NOT DISTINCT FROM p_tenant
                ORDER BY policy.{C<PaidEventPolicyVersion>("Id")}
                LIMIT 2;
            END; $body$;
            """;
        string schema = sql.DelimitIdentifier(Schema(context, typeof(TenantSetting)));
        string schemas = string.Join(", ", relations.Select(relation => Schema(context, relation.Type))
            .Distinct(StringComparer.Ordinal).Select(sql.DelimitIdentifier));
        string TenantFunction(bool bySlugs) => $"""
            CREATE OR REPLACE FUNCTION {FunctionSql(context, bySlugs ? "tenant_slugs" : "tenants")}(p_tenant uuid{(bySlugs ? ", p_keys text[]" : "")})
            RETURNS TABLE ({C<Tenant>("Id")} uuid, {C<Tenant>("FullName")} text,
                {C<Tenant>("Slug")} text, {C<Tenant>("TenantStatusId")} integer,
                status_master_code text, status_full_name text, status_description text, status_is_active boolean)
            LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
            AS $tenant_read$
            {BuildTenantReadBody(context, bySlugs)}
            $tenant_read$;
            """;
        string bootstrapFunction = $"""
            CREATE OR REPLACE FUNCTION {FunctionSql(context, "bootstrap")}(p_tenant uuid)
            RETURNS TABLE ({string.Join(", ", BootstrapColumns.Select(property =>
                BootstrapColumn(context, property) + " " + context.Model.FindEntityType(
                    typeof(ConfigurationManifestOperation))!.FindProperty(property)!.GetColumnType()))})
            LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
            AS $bootstrap_read$
            {BuildBootstrapReadBody(context)}
            $bootstrap_read$;
            """;
        string acl = string.Join("\n", new[] { ("settings", "uuid, text[]"), ("documents", "uuid, text[]"), ("policy", "uuid"), ("tenants", "uuid"), ("tenant_slugs", "uuid, text[]"), ("bootstrap", "uuid") }
            .Select(function => $"""
                REVOKE ALL ON FUNCTION {FunctionSql(context, function.Item1)}({function.Item2}) FROM PUBLIC, {GlobalRole};
                GRANT EXECUTE ON FUNCTION {FunctionSql(context, function.Item1)}({function.Item2}) TO {GlobalRole};
                """));
        string functionAuthorityInvalid = $"""
            EXISTS (
                SELECT 1 FROM pg_catalog.pg_proc procedure
                CROSS JOIN LATERAL aclexplode(COALESCE(procedure.proacl,
                    acldefault('f', procedure.proowner))) permission
                WHERE procedure.oid IN (
                    {Text(FunctionSql(context, "settings") + "(uuid,text[])")}::regprocedure,
                    {Text(FunctionSql(context, "documents") + "(uuid,text[])")}::regprocedure,
                    {Text(FunctionSql(context, "policy") + "(uuid)")}::regprocedure,
                    {Text(FunctionSql(context, "tenants") + "(uuid)")}::regprocedure,
                    {Text(FunctionSql(context, "tenant_slugs") + "(uuid,text[])")}::regprocedure,
                    {Text(FunctionSql(context, "bootstrap") + "(uuid)")}::regprocedure)
                  AND (procedure.proowner <> (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{OwnerRole}')
                    OR permission.grantee NOT IN (
                        SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ('{OwnerRole}', '{GlobalRole}'))
                    OR permission.grantee = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{GlobalRole}')
                        AND permission.is_grantable))
            """;
        return $"""
            DO $roles$
            DECLARE v_role text;
            BEGIN
                FOREACH v_role IN ARRAY ARRAY[{roleNames}] LOOP
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = v_role) THEN
                        EXECUTE format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', v_role);
                    END IF;
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = v_role AND ({elevated})) THEN
                        RAISE EXCEPTION 'configuration_import_snapshot_authority_not_ready';
                    END IF;
                END LOOP;
                IF {unsafeParents} OR {unsafeOwnership}
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members edge
                        JOIN pg_catalog.pg_roles parent ON parent.oid = edge.roleid
                        JOIN pg_catalog.pg_roles member ON member.oid = edge.member
                        WHERE parent.rolname = '{MigratorRole}' AND member.rolname <> CURRENT_USER)
                    OR EXISTS (
                        WITH RECURSIVE inherited_roles(roleid) AS (
                            SELECT roleid FROM pg_catalog.pg_auth_members
                            WHERE member = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = CURRENT_USER)
                            UNION SELECT edge.roleid FROM pg_catalog.pg_auth_members edge
                            JOIN inherited_roles inherited ON edge.member = inherited.roleid)
                        SELECT 1 FROM inherited_roles
                        WHERE roleid = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{GlobalRole}')) THEN
                    RAISE EXCEPTION 'configuration_import_snapshot_authority_not_ready';
                END IF;
            END; $roles$;
            GRANT {OwnerRole} TO {MigratorRole};
            GRANT {MigratorRole} TO CURRENT_USER;
            GRANT USAGE ON SCHEMA {schemas} TO {OwnerRole};
            GRANT USAGE, CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {GlobalRole};
            {grants}
            {BuildTenantReadInstallSql(context)}
            {BuildBootstrapReadInstallSql(context)}
            DO $install$
            DECLARE previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{OwnerRole}', true);
                EXECUTE $ddl$
                {ScalarFunction<TenantSetting>("settings", settingColumns, "SettingKey", SettingKeys)}
                {ScalarFunction<TenantSettingsDocument>("documents", documentColumns, "DocumentKey", DocumentKeys)}
                {policyFunction}
                {TenantFunction(false)}
                {TenantFunction(true)}
                {bootstrapFunction}
                $ddl$;
                EXECUTE $acl$ {acl} $acl$;
                PERFORM set_config('role', previous_role, true);
            END; $install$;
            REVOKE CREATE ON SCHEMA {schema} FROM {OwnerRole};
            DO $closed$
            BEGIN
                IF has_schema_privilege('{OwnerRole}', {Text(Schema(context, typeof(TenantSetting)))}, 'CREATE')
                    OR has_schema_privilege('{GlobalRole}', {Text(Schema(context, typeof(TenantSetting)))}, 'CREATE')
                    OR {unsafeTablePrivileges} OR ({excludedColumns})
                    OR {functionAuthorityInvalid} THEN
                    RAISE EXCEPTION 'configuration_import_snapshot_authority_not_ready';
                END IF;
            END; $closed$;
            """;
    }
}

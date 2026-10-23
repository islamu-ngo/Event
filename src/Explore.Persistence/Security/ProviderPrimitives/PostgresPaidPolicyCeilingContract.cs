using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Reads bounded active policy validation aggregates on the caller's owning transaction.
/// The execute-only capability grants no policy-table access or mutation authority.
/// </summary>
public static class PostgresPaidPolicyCeilingContract
{
    public const string OwnerRole = "event_paid_policy_ceiling_owner";
    public const string MigratorRole = "event_paid_policy_ceiling_migrator";
    public const string RuntimeRole = "event_paid_policy_ceiling_runtime";
    private const string PolicyName = "paid_policy_ceiling_validation";

    public static string FunctionSql(ExploreDbContext context) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier("paid_event_policy_ceiling_page", Schema(context));

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
        string schema = sql.DelimitIdentifier(Schema(context));
        string Table<T>() => sql.DelimitIdentifier(context.Model.FindEntityType(typeof(T))!.GetTableName()!, Schema(context));
        string Column<T>(string name)
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            var mapping = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            return sql.DelimitIdentifier(entity.FindProperty(name)!.GetColumnName(mapping)!);
        }
        string Columns<T>(params string[] names) => string.Join(", ", names.Select(Column<T>));
        string policies = Table<PaidEventPolicyVersion>();
        string kinds = Table<PaidEventPolicyAllowedOrganizerKind>();
        string currencies = Table<PaidEventPolicyAllowedCurrency>();
        string protections = Table<PaidEventPolicyRefundProtection>();
        string risks = Table<PaidEventPolicyCurrencyRiskLimitRow>();
        string id = Column<PaidEventPolicyVersion>(nameof(PaidEventPolicyVersion.Id));
        string tenant = Column<PaidEventPolicyVersion>(nameof(PaidEventPolicyVersion.TenantId));
        string scope = Column<PaidEventPolicyVersion>(nameof(PaidEventPolicyVersion.PolicyScopeKey));
        string active = Column<PaidEventPolicyVersion>(nameof(PaidEventPolicyVersion.IsActive));
        string ParentMatch<T>(string alias) =>
            $"{alias}.{Column<T>("PaidEventPolicyVersionId")} = policy.{id}" +
            $" AND {alias}.{Column<T>("PolicyScopeKey")} = policy.{scope}" +
            $" AND {alias}.{Column<T>("TenantId")} = policy.{tenant}";
        string ChildPredicate<T>() =>
            $"EXISTS (SELECT 1 FROM {policies} policy WHERE policy.{active} AND policy.{tenant} IS NOT NULL" +
            $" AND {ParentMatch<T>(sql.DelimitIdentifier(context.Model.FindEntityType(typeof(T))!.GetTableName()!))})";
        string ChildPolicy<T>() => $"""
            DROP POLICY IF EXISTS "{PolicyName}" ON {Table<T>()};
            CREATE POLICY "{PolicyName}" ON {Table<T>()} FOR SELECT TO {OwnerRole}
                USING ({ChildPredicate<T>()});
            """;
        string parentPredicate = $"{active} AND {tenant} IS NOT NULL";
        var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model);
        (string Table, string Sql, string ReadFingerprint, string TenantFingerprint) Specification<T>(string predicate)
        {
            string tableName = context.Model.FindEntityType(typeof(T))!.GetTableName()!;
            var tenantTable = tenantTables.Single(table => table.TableName == tableName);
            string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"{Schema(context)}:{tableName}:{predicate}")));
            return (tableName, Table<T>(), $"event-paid-policy-ceiling:v1:{digest}:",
                PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(tenantTable, Schema(context)));
        }
        var specifications = new[]
        {
            Specification<PaidEventPolicyVersion>(parentPredicate),
            Specification<PaidEventPolicyAllowedOrganizerKind>(ChildPredicate<PaidEventPolicyAllowedOrganizerKind>()),
            Specification<PaidEventPolicyAllowedCurrency>(ChildPredicate<PaidEventPolicyAllowedCurrency>()),
            Specification<PaidEventPolicyRefundProtection>(ChildPredicate<PaidEventPolicyRefundProtection>()),
            Specification<PaidEventPolicyCurrencyRiskLimitRow>(ChildPredicate<PaidEventPolicyCurrencyRiskLimitRow>())
        };
        string Literal(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
        string expectedPolicies = string.Join(",\n", specifications.Select(specification =>
            $"({Literal(Schema(context))}, {Literal(specification.Table)}, " +
            $"{Literal(specification.ReadFingerprint)}, {Literal(specification.TenantFingerprint)})"));
        string stamps = string.Join("\n", specifications.Select(specification => $"""
            DO $stamp$
            BEGIN
                EXECUTE (
                    SELECT format('COMMENT ON POLICY %I ON %s IS %L',
                        p.polname, {Literal(specification.Sql)},
                        {Literal(specification.ReadFingerprint)} || md5(p.polqual::text))
                    FROM pg_catalog.pg_policy p
                    WHERE p.polrelid = {Literal(specification.Sql)}::regclass AND p.polname = '{PolicyName}');
            END; $stamp$;
            """));
        string function = FunctionSql(context);
        return $$"""
            DO $roles$
            DECLARE role_name text;
            BEGIN
                FOREACH role_name IN ARRAY ARRAY['{{OwnerRole}}','{{MigratorRole}}','{{RuntimeRole}}'] LOOP
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = role_name) THEN
                        EXECUTE format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS', role_name);
                    END IF;
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = role_name
                        AND (rolcanlogin OR rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication OR rolbypassrls)) THEN
                        RAISE EXCEPTION 'paid_policy_ceiling_role_invalid';
                    END IF;
                END LOOP;
                IF EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members
                    WHERE member = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{{OwnerRole}}'))
                    OR pg_has_role('{{RuntimeRole}}', '{{OwnerRole}}', 'MEMBER')
                    OR pg_has_role('{{RuntimeRole}}', '{{MigratorRole}}', 'MEMBER') THEN
                    RAISE EXCEPTION 'paid_policy_ceiling_owner_invalid';
                END IF;
                IF EXISTS (
                    WITH RECURSIVE memberships AS (
                        SELECT roleid FROM pg_catalog.pg_auth_members
                        WHERE member = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = CURRENT_USER)
                        UNION SELECT edge.roleid FROM pg_catalog.pg_auth_members edge
                        JOIN memberships inherited ON edge.member = inherited.roleid)
                    SELECT 1 FROM memberships WHERE roleid =
                        (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{{RuntimeRole}}')) THEN
                    RAISE EXCEPTION 'paid_policy_ceiling_migration_authority_invalid';
                END IF;
            END; $roles$;
            GRANT {{OwnerRole}} TO {{MigratorRole}};
            GRANT {{MigratorRole}} TO CURRENT_USER;
            GRANT USAGE, CREATE ON SCHEMA {{schema}} TO {{OwnerRole}};
            GRANT USAGE ON SCHEMA {{schema}} TO {{RuntimeRole}};
            REVOKE ALL ON {{policies}}, {{kinds}}, {{currencies}}, {{protections}}, {{risks}} FROM {{OwnerRole}}, {{RuntimeRole}};
            GRANT SELECT ({{Columns<PaidEventPolicyVersion>(
                nameof(PaidEventPolicyVersion.Id), nameof(PaidEventPolicyVersion.TenantId),
                nameof(PaidEventPolicyVersion.PolicyScopeKey), nameof(PaidEventPolicyVersion.VersionNumber),
                nameof(PaidEventPolicyVersion.IsActive), nameof(PaidEventPolicyVersion.IsPaymentsEnabled),
                nameof(PaidEventPolicyVersion.RequiresLocalVerification), nameof(PaidEventPolicyVersion.DefaultCurrencyCode),
                nameof(PaidEventPolicyVersion.RequiresFirstPaidEventReview), nameof(PaidEventPolicyVersion.FarFutureReviewThresholdDays))}})
                ON {{policies}} TO {{OwnerRole}};
            GRANT SELECT ({{Columns<PaidEventPolicyAllowedOrganizerKind>(
                "TenantId", "PolicyScopeKey", "PaidEventPolicyVersionId", "Ordinal", "ActorTypeId")}})
                ON {{kinds}} TO {{OwnerRole}};
            GRANT SELECT ({{Columns<PaidEventPolicyAllowedCurrency>(
                "TenantId", "PolicyScopeKey", "PaidEventPolicyVersionId", "Ordinal", "CurrencyCode")}})
                ON {{currencies}} TO {{OwnerRole}};
            GRANT SELECT ({{Columns<PaidEventPolicyRefundProtection>(
                "TenantId", "PolicyScopeKey", "PaidEventPolicyVersionId", "Ordinal", "RefundProtectionId")}})
                ON {{protections}} TO {{OwnerRole}};
            GRANT SELECT ({{Columns<PaidEventPolicyCurrencyRiskLimitRow>(
                "TenantId", "PolicyScopeKey", "PaidEventPolicyVersionId", "Ordinal", "CurrencyCode",
                "PerEventSalesCeilingMinor", "PerEventSalesCountCeiling", "RollingOrganizerSalesCeilingMinor",
                "RollingOrganizerSalesCountCeiling", "RollingOrganizerWindowDays", "HighValueReviewThresholdMinor")}})
                ON {{risks}} TO {{OwnerRole}};
            DROP POLICY IF EXISTS "{{PolicyName}}" ON {{policies}};
            CREATE POLICY "{{PolicyName}}" ON {{policies}} FOR SELECT TO {{OwnerRole}}
                USING ({{parentPredicate}});
            {{ChildPolicy<PaidEventPolicyAllowedOrganizerKind>()}}
            {{ChildPolicy<PaidEventPolicyAllowedCurrency>()}}
            {{ChildPolicy<PaidEventPolicyRefundProtection>()}}
            {{ChildPolicy<PaidEventPolicyCurrencyRiskLimitRow>()}}
            {{stamps}}
            DO $install$
            DECLARE previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{{OwnerRole}}', true);
                EXECUTE $ddl$
                CREATE OR REPLACE FUNCTION {{function}}(p_offset integer, p_limit integer)
                RETURNS SETOF text LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
                AS $body$
                BEGIN
                    IF p_offset IS NULL OR p_offset < 0 OR p_limit IS NULL
                        OR p_limit < 1 OR p_limit > {{IPaidEventPolicyRepository.MaximumActiveTenantPolicyPageSize}} THEN
                        RAISE EXCEPTION 'paid_policy_ceiling_page_invalid' USING ERRCODE = '22023';
                    END IF;
                    IF CURRENT_USER <> '{{OwnerRole}}' OR NOT EXISTS (
                        SELECT 1 FROM pg_catalog.pg_roles
                        WHERE rolname = CURRENT_USER AND NOT rolcanlogin AND NOT rolsuper
                            AND NOT rolbypassrls AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolreplication)
                        OR EXISTS (
                            SELECT 1 FROM (VALUES {{expectedPolicies}})
                                AS expected(schema_name, table_name, read_fingerprint, tenant_fingerprint)
                            LEFT JOIN pg_catalog.pg_namespace namespace ON namespace.nspname = expected.schema_name
                            LEFT JOIN pg_catalog.pg_class relation
                                ON relation.relnamespace = namespace.oid AND relation.relname = expected.table_name
                            LEFT JOIN pg_catalog.pg_policy read_policy
                                ON read_policy.polrelid = relation.oid AND read_policy.polname = '{{PolicyName}}'
                            LEFT JOIN pg_catalog.pg_policy tenant_policy
                                ON tenant_policy.polrelid = relation.oid AND tenant_policy.polname = 'tenant_isolation'
                            WHERE relation.oid IS NULL OR NOT relation.relrowsecurity OR NOT relation.relforcerowsecurity
                                OR read_policy.oid IS NULL OR read_policy.polcmd <> 'r' OR NOT read_policy.polpermissive
                                OR read_policy.polwithcheck IS NOT NULL OR read_policy.polqual IS NULL
                                OR read_policy.polroles <> ARRAY[
                                    (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{{OwnerRole}}')]
                                OR obj_description(read_policy.oid, 'pg_policy') IS DISTINCT FROM
                                    expected.read_fingerprint || md5(read_policy.polqual::text)
                                OR tenant_policy.oid IS NULL OR tenant_policy.polcmd <> '*'
                                OR tenant_policy.polroles <> ARRAY[0::oid] OR NOT tenant_policy.polpermissive
                                OR tenant_policy.polqual IS NULL OR tenant_policy.polwithcheck IS NULL
                                OR obj_description(tenant_policy.oid, 'pg_policy') IS DISTINCT FROM
                                    expected.tenant_fingerprint || md5(tenant_policy.polqual::text)
                                        || ':' || md5(tenant_policy.polwithcheck::text)) THEN
                        RAISE EXCEPTION 'paid_policy_ceiling_authority_not_ready' USING ERRCODE = '55000';
                    END IF;
                    RETURN QUERY
                    SELECT jsonb_build_object(
                        'Id', policy.{{id}},
                        'TenantId', policy.{{tenant}},
                        'VersionNumber', policy.{{Column<PaidEventPolicyVersion>("VersionNumber")}},
                        'IsPaymentsEnabled', policy.{{Column<PaidEventPolicyVersion>("IsPaymentsEnabled")}},
                        'RequiresLocalVerification', policy.{{Column<PaidEventPolicyVersion>("RequiresLocalVerification")}},
                        'DefaultCurrencyCode', policy.{{Column<PaidEventPolicyVersion>("DefaultCurrencyCode")}},
                        'RequiresFirstPaidEventReview', policy.{{Column<PaidEventPolicyVersion>("RequiresFirstPaidEventReview")}},
                        'FarFutureReviewThresholdDays', policy.{{Column<PaidEventPolicyVersion>("FarFutureReviewThresholdDays")}},
                        'AllowedOrganizerKindIds', COALESCE(
                            (SELECT jsonb_agg(kind.{{Column<PaidEventPolicyAllowedOrganizerKind>("ActorTypeId")}}
                                ORDER BY kind.{{Column<PaidEventPolicyAllowedOrganizerKind>("Ordinal")}})
                             FROM {{kinds}} kind WHERE {{ParentMatch<PaidEventPolicyAllowedOrganizerKind>("kind")}}), '[]'::jsonb),
                        'AllowedCurrencyCodes', COALESCE(
                            (SELECT jsonb_agg(currency.{{Column<PaidEventPolicyAllowedCurrency>("CurrencyCode")}}
                                ORDER BY currency.{{Column<PaidEventPolicyAllowedCurrency>("Ordinal")}})
                             FROM {{currencies}} currency WHERE {{ParentMatch<PaidEventPolicyAllowedCurrency>("currency")}}), '[]'::jsonb),
                        'RefundProtectionIds', COALESCE(
                            (SELECT jsonb_agg(protection.{{Column<PaidEventPolicyRefundProtection>("RefundProtectionId")}}
                                ORDER BY protection.{{Column<PaidEventPolicyRefundProtection>("Ordinal")}})
                             FROM {{protections}} protection WHERE {{ParentMatch<PaidEventPolicyRefundProtection>("protection")}}), '[]'::jsonb),
                        'CurrencyRiskLimits', COALESCE(
                            (SELECT jsonb_agg(jsonb_build_object(
                                'CurrencyCode', risk.{{Column<PaidEventPolicyCurrencyRiskLimitRow>("CurrencyCode")}},
                                'PerEventSalesCeilingMinor', risk.{{Column<PaidEventPolicyCurrencyRiskLimitRow>("PerEventSalesCeilingMinor")}},
                                'PerEventSalesCountCeiling', risk.{{Column<PaidEventPolicyCurrencyRiskLimitRow>("PerEventSalesCountCeiling")}},
                                'RollingOrganizerSalesCeilingMinor', risk.{{Column<PaidEventPolicyCurrencyRiskLimitRow>("RollingOrganizerSalesCeilingMinor")}},
                                'RollingOrganizerSalesCountCeiling', risk.{{Column<PaidEventPolicyCurrencyRiskLimitRow>("RollingOrganizerSalesCountCeiling")}},
                                'RollingOrganizerWindowDays', risk.{{Column<PaidEventPolicyCurrencyRiskLimitRow>("RollingOrganizerWindowDays")}},
                                'HighValueReviewThresholdMinor', risk.{{Column<PaidEventPolicyCurrencyRiskLimitRow>("HighValueReviewThresholdMinor")}})
                                ORDER BY risk.{{Column<PaidEventPolicyCurrencyRiskLimitRow>("Ordinal")}})
                             FROM {{risks}} risk WHERE {{ParentMatch<PaidEventPolicyCurrencyRiskLimitRow>("risk")}}), '[]'::jsonb))::text
                    FROM {{policies}} policy
                    WHERE policy.{{active}} AND policy.{{tenant}} IS NOT NULL
                    ORDER BY policy.{{tenant}}, policy.{{id}}
                    OFFSET p_offset LIMIT p_limit;
                END; $body$;
                $ddl$;
                EXECUTE $acl$
                    REVOKE ALL ON FUNCTION {{function}}(integer, integer) FROM PUBLIC, {{RuntimeRole}};
                    GRANT EXECUTE ON FUNCTION {{function}}(integer, integer) TO {{RuntimeRole}};
                $acl$;
                PERFORM set_config('role', previous_role, true);
            END; $install$;
            REVOKE CREATE ON SCHEMA {{schema}} FROM {{OwnerRole}};
            """;
    }

    private static string Schema(ExploreDbContext context) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? context.Model.FindEntityType(typeof(PaidEventPolicyVersion))!.GetSchema() ?? "public";
}

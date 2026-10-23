using System.Globalization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.PaidEventPolicies;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Persistence.Security;

public static partial class PostgresConfigurationImportPaidPolicyContract
{
    private static string TargetPredicate(ExploreDbContext context, string alias, bool allowPage = true)
    {
        string P(string name) => alias + Column(context, typeof(PaidEventPolicyVersion), name);
        string target = $"""
            {P("TenantId")} IS NOT DISTINCT FROM
                NULLIF(pg_catalog.current_setting('app.paid_policy_import_tenant', true), '')::uuid
            AND {P("PolicyScopeKey")} = CASE WHEN {P("TenantId")} IS NULL THEN 'instance'
                ELSE 'tenant:' || pg_catalog.replace({P("TenantId")}::text, '-', '') END
            """;
        return allowPage
            ? $"(({target}) OR (pg_catalog.current_setting('app.paid_policy_import_page', true) = 'true' " +
                $"AND {P("IsActive")} AND {P("TenantId")} IS NOT NULL AND {P("ActiveUniquenessSlot")} = 0 " +
                $"AND {P("PolicyScopeKey")} = 'tenant:' || pg_catalog.replace({P("TenantId")}::text, '-', '')))"
            : $"({target}) AND pg_catalog.current_setting('app.paid_policy_import_page', true) = 'false'";
    }

    private static string ParentMatch(ExploreDbContext context, Type child, string alias) =>
        $"{alias}.{Column(context, child, "PaidEventPolicyVersionId")} = policy.{Column(context, typeof(PaidEventPolicyVersion), "Id")} " +
        $"AND {alias}.{Column(context, child, "PolicyScopeKey")} = policy.{Column(context, typeof(PaidEventPolicyVersion), "PolicyScopeKey")} " +
        $"AND {alias}.{Column(context, child, "TenantId")} IS NOT DISTINCT FROM policy.{Column(context, typeof(PaidEventPolicyVersion), "TenantId")}";

    private static string TargetBody(ExploreDbContext context) => $"""
        BEGIN
            PERFORM {FunctionSql(context, "assert_authority")}();
            IF p_page IS NULL OR p_tenant = '{Guid.Empty}'::uuid OR p_page AND p_tenant IS NOT NULL
                OR pg_catalog.current_setting('transaction_isolation') <> 'serializable' THEN
                RAISE EXCEPTION 'configuration_import_paid_policy_target_invalid' USING ERRCODE = '22023';
            END IF;
            PERFORM pg_catalog.set_config('app.paid_policy_import_tenant', COALESCE(p_tenant::text, ''), true);
            PERFORM pg_catalog.set_config('app.paid_policy_import_page', p_page::text, true);
            PERFORM pg_catalog.pg_advisory_xact_lock(
                {RelationalSettingMutationLock.ComputeStableLockKey(PaidEventPolicyMutationLockKeys.Instance).ToString(CultureInfo.InvariantCulture)}::bigint);
            IF p_tenant IS NOT NULL THEN
                PERFORM pg_catalog.pg_advisory_xact_lock(('x' || pg_catalog.substr(
                    pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(
                        'explore:setting-mutation:paid-event-policy:tenant:' || p_tenant::text, 'UTF8')), 'hex'), 1, 16))::bit(64)::bigint);
            END IF;
            RETURN;
        END;
        """;

    private static string AggregateSql(ExploreDbContext context) =>
        "pg_catalog.jsonb_build_object('root', pg_catalog.to_jsonb(policy), " +
        string.Join(", ", ChildRelations.Select(child =>
            $"{Text(child.Operation)}, COALESCE((SELECT pg_catalog.jsonb_agg(pg_catalog.to_jsonb(row) " +
            $"ORDER BY row.{Column(context, child.Type, "Ordinal")}) FROM {Table(context, child.Type)} row " +
            $"WHERE {ParentMatch(context, child.Type, "row")}), '[]'::jsonb)")) + ")::text";

    private static string ReadBody(ExploreDbContext context) => $"""
        DECLARE result text;
        BEGIN
            PERFORM {FunctionSql(context, "set_target")}(p_tenant, false);
            SELECT {AggregateSql(context)} INTO result
                FROM {Table(context, typeof(PaidEventPolicyVersion))} policy
                WHERE {TargetPredicate(context, "policy.", allowPage: false)}
                    AND policy.{Column(context, typeof(PaidEventPolicyVersion), "IsActive")}
                    AND policy.{Column(context, typeof(PaidEventPolicyVersion), "ActiveUniquenessSlot")} = 0
                FOR UPDATE OF policy;
            RETURN result;
        END;
        """;

    private static string PageBody(ExploreDbContext context) => $"""
        BEGIN
            PERFORM {FunctionSql(context, "set_target")}(NULL, true);
            IF p_offset IS NULL OR p_offset < 0 OR p_limit IS NULL
                OR p_limit NOT BETWEEN 1 AND {IPaidEventPolicyRepository.MaximumActiveTenantPolicyPageSize} THEN
                RAISE EXCEPTION 'configuration_import_paid_policy_page_invalid' USING ERRCODE = '22023';
            END IF;
            RETURN QUERY SELECT {AggregateSql(context)}
                FROM {Table(context, typeof(PaidEventPolicyVersion))} policy
                WHERE policy.{Column(context, typeof(PaidEventPolicyVersion), "IsActive")}
                    AND policy.{Column(context, typeof(PaidEventPolicyVersion), "TenantId")} IS NOT NULL
                    AND policy.{Column(context, typeof(PaidEventPolicyVersion), "ActiveUniquenessSlot")} = 0
                ORDER BY policy.{Column(context, typeof(PaidEventPolicyVersion), "TenantId")},
                    policy.{Column(context, typeof(PaidEventPolicyVersion), "Id")}
                OFFSET p_offset LIMIT p_limit;
        END;
        """;

    private static string RequiredShape(ExploreDbContext context, string json, Type type, string[] properties)
    {
        string columns = string.Join(", ", properties.Select(property => Text(ColumnName(context, type, property))));
        return $"pg_catalog.jsonb_typeof({json}) IS DISTINCT FROM 'object' " +
            $"OR NOT ({json} ?& ARRAY[{columns}]::text[]) " +
            $"OR EXISTS (SELECT 1 FROM pg_catalog.jsonb_object_keys({json}) field WHERE field <> ALL (ARRAY[{columns}]::text[]))";
    }

    private static string WriteBody(ExploreDbContext context)
    {
        Type rootType = typeof(PaidEventPolicyVersion);
        string policies = Table(context, rootType);
        string P(string property) => Column(context, rootType, property);
        string declarations = string.Join("\n", ChildRelations.Select(child =>
            $"{child.Operation}_row {Table(context, child.Type)}%ROWTYPE;"));
        string childValidation = string.Join("\n", ChildRelations.Select(child =>
        {
            string C(string property) => Column(context, child.Type, property);
            string row = child.Operation + "_row";
            string json = "p_revision->" + Text(child.Operation);
            string additional;
            if (child.Type == typeof(PaidEventPolicyAllowedOrganizerKind))
                additional = $"{row}.{C("ActorTypeId")} IS NULL OR {row}.{C("ActorTypeId")} NOT IN " +
                    $"({(int)ActorTypeEnum.Organization}, {(int)ActorTypeEnum.Group}, {(int)ActorTypeEnum.User})";
            else if (child.Type == typeof(PaidEventPolicyRefundProtection))
                additional = $"{row}.{C("RefundProtectionId")} IS NULL OR {row}.{C("RefundProtectionId")} NOT IN " +
                    $"({string.Join(", ", Enum.GetValues<PaidEventRefundProtection>().Select(value => (int)value))})";
            else
            {
                string supported = string.Join(", ", new[]
                {
                    "BHD", "IQD", "JOD", "KWD", "LYD", "OMR", "TND", "BIF", "CLP", "DJF", "GNF", "JPY",
                    "KMF", "KRW", "MGA", "PYG", "RWF", "UGX", "VND", "VUV", "XAF", "XOF", "XPF",
                    "AED", "AUD", "BDT", "BRL", "CAD", "CHF", "CNY", "CZK", "DKK", "EGP", "EUR", "GBP",
                    "HKD", "HUF", "IDR", "ILS", "INR", "KES", "MAD", "MXN", "MYR", "NGN", "NOK", "NZD",
                    "PHP", "PKR", "PLN", "QAR", "RON", "SAR", "SEK", "SGD", "THB", "TRY", "TWD", "UAH", "USD", "ZAR"
                }.Select(Text));
                additional = $"{row}.{C("CurrencyCode")} IS NULL OR {row}.{C("CurrencyCode")} NOT IN ({supported})";
                if (child.Type == typeof(PaidEventPolicyCurrencyRiskLimitRow))
                {
                    string CurrencyJson(string expression) => expression + "->>" +
                        Text(ColumnName(context, typeof(PaidEventPolicyAllowedCurrency), "CurrencyCode"));
                    additional += $" OR NOT EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(p_revision->'currencies') currency " +
                        $"WHERE {CurrencyJson("currency")} = {row}.{C("CurrencyCode")}) OR " +
                        string.Join(" OR ", child.Columns.Where(name => name.EndsWith("Minor", StringComparison.Ordinal)
                            || name.EndsWith("CountCeiling", StringComparison.Ordinal) || name == "RollingOrganizerWindowDays")
                            .Select(name => $"{row}.{C(name)} <= 0")) +
                        $" OR (({row}.{C("RollingOrganizerSalesCeilingMinor")} IS NOT NULL " +
                        $"OR {row}.{C("RollingOrganizerSalesCountCeiling")} IS NOT NULL) " +
                        $"<> ({row}.{C("RollingOrganizerWindowDays")} IS NOT NULL))";
                }
            }
            string distinctProperty = child.Operation switch
            {
                "kinds" => "ActorTypeId",
                "protections" => "RefundProtectionId",
                _ => "CurrencyCode"
            };
            return $"""
                IF pg_catalog.jsonb_typeof({json}) IS DISTINCT FROM 'array'
                    {(child.Operation == "risks" ? "" : $"OR pg_catalog.jsonb_array_length({json}) < 1")}
                    OR EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements({json}) element(value)
                        GROUP BY element.value->>{Text(ColumnName(context, child.Type, distinctProperty))} HAVING count(*) > 1) THEN
                    RAISE EXCEPTION 'configuration_import_paid_policy_children_invalid' USING ERRCODE = '22023';
                END IF;
                ordinal := 0;
                FOR item IN SELECT value FROM pg_catalog.jsonb_array_elements({json}) LOOP
                    IF {RequiredShape(context, "item", child.Type, child.Columns)} THEN
                        RAISE EXCEPTION 'configuration_import_paid_policy_child_shape' USING ERRCODE = '22023';
                    END IF;
                    SELECT * INTO {row} FROM pg_catalog.jsonb_populate_record(NULL::{Table(context, child.Type)}, item);
                    IF {row}.{C("TenantId")} IS DISTINCT FROM p_tenant
                        OR {row}.{C("PolicyScopeKey")} IS DISTINCT FROM r.{P("PolicyScopeKey")}
                        OR {row}.{C("PaidEventPolicyVersionId")} IS DISTINCT FROM r.{P("Id")}
                        OR {row}.{C("Ordinal")} IS DISTINCT FROM ordinal OR {additional} THEN
                        RAISE EXCEPTION 'configuration_import_paid_policy_child_invalid' USING ERRCODE = '22023';
                    END IF;
                    ordinal := ordinal + 1;
                END LOOP;
                """;
        }));
        string childInserts = string.Join("\n", ChildRelations.Select(child => $"""
            FOR item IN SELECT value FROM pg_catalog.jsonb_array_elements(p_revision->{Text(child.Operation)}) LOOP
                SELECT * INTO {child.Operation}_row FROM pg_catalog.jsonb_populate_record(NULL::{Table(context, child.Type)}, item);
                INSERT INTO {Table(context, child.Type)} ({string.Join(", ", child.Columns.Select(name => Column(context, child.Type, name)))})
                    VALUES ({string.Join(", ", child.Columns.Select(name => child.Operation + "_row." + Column(context, child.Type, name)))});
            END LOOP;
            """));
        string currencyCode = Text(ColumnName(context, typeof(PaidEventPolicyAllowedCurrency), "CurrencyCode"));
        string protectionId = Text(ColumnName(context, typeof(PaidEventPolicyRefundProtection), "RefundProtectionId"));
        return $"""
            DECLARE r {policies}%ROWTYPE; affected integer; item jsonb; ordinal integer;
                {declarations}
            BEGIN
                PERFORM {FunctionSql(context, "set_target")}(p_tenant, false);
                IF pg_catalog.jsonb_typeof(p_revision) IS DISTINCT FROM 'object'
                    OR NOT (p_revision ?& ARRAY['root','kinds','currencies','protections','risks']::text[])
                    OR EXISTS (SELECT 1 FROM pg_catalog.jsonb_object_keys(p_revision) field
                        WHERE field <> ALL (ARRAY['root','kinds','currencies','protections','risks']::text[]))
                    OR {RequiredShape(context, "p_revision->'root'", rootType, RootColumns)} THEN
                    RAISE EXCEPTION 'configuration_import_paid_policy_revision_shape' USING ERRCODE = '22023';
                END IF;
                SELECT * INTO r FROM pg_catalog.jsonb_populate_record(NULL::{policies}, p_revision->'root');
                IF r.{P("Id")} IS NULL OR r.{P("Id")} = '{Guid.Empty}'::uuid
                    OR r.{P("TenantId")} IS DISTINCT FROM p_tenant
                    OR r.{P("PolicyScopeKey")} IS DISTINCT FROM (CASE WHEN p_tenant IS NULL THEN 'instance'
                        ELSE 'tenant:' || pg_catalog.replace(p_tenant::text, '-', '') END)
                    OR r.{P("IsActive")} IS DISTINCT FROM true OR r.{P("ActiveUniquenessSlot")} IS DISTINCT FROM 0
                    OR r.{P("VersionNumber")} IS NULL OR r.{P("VersionNumber")} < 1
                    OR r.{P("IsPaymentsEnabled")} IS NULL OR r.{P("RequiresLocalVerification")} IS NULL
                    OR r.{P("RequiresFirstPaidEventReview")} IS NULL OR r.{P("FarFutureReviewThresholdDays")} <= 0
                    OR p_at IS NULL OR NOT pg_catalog.isfinite(p_at) OR p_at <= '0001-01-01 00:00:00+00'::timestamptz
                    OR p_at >= '10000-01-01 00:00:00+00'::timestamptz OR p_actor = '{Guid.Empty}'::uuid
                    OR r.{P("CreatedAt")} IS DISTINCT FROM p_at OR r.{P("CreatedBy")} IS DISTINCT FROM p_actor
                    OR (p_revision->'root'->>{Text(ColumnName(context, rootType, "CreatedAt"))}) !~ 'Z$'
                    OR r.{P("UpdatedAt")} IS NOT NULL OR r.{P("UpdatedBy")} IS NOT NULL
                    OR p_expected_id IS NULL AND (p_tenant IS NULL OR p_expected_version IS NOT NULL OR r.{P("VersionNumber")} <> 1)
                    OR p_expected_id IS NOT NULL AND (p_expected_id = '{Guid.Empty}'::uuid
                        OR p_expected_version IS NULL OR p_expected_version < 1
                        OR r.{P("Id")} = p_expected_id OR r.{P("VersionNumber")} <> p_expected_version + 1) THEN
                    RAISE EXCEPTION 'configuration_import_paid_policy_revision_invalid' USING ERRCODE = '22023';
                END IF;
                {childValidation}
                IF r.{P("DefaultCurrencyCode")} IS NOT NULL AND NOT EXISTS (
                    SELECT 1 FROM pg_catalog.jsonb_array_elements(p_revision->'currencies') currency
                    WHERE currency->>{currencyCode} = r.{P("DefaultCurrencyCode")})
                    OR EXISTS (SELECT 1 FROM unnest(ARRAY[
                        {(int)PaidEventRefundProtection.OrganizerCancellationFullRefund},
                        {(int)PaidEventRefundProtection.MaterialChangeBuyerChoiceOrFullRefund},
                        {(int)PaidEventRefundProtection.DuplicateOrIncorrectChargeFullRefund},
                        {(int)PaidEventRefundProtection.SubstantialNonDeliveryRemedy},
                        {(int)PaidEventRefundProtection.AttendeeBuyerChangeTermsDisclosedSubjectToLaw},
                        {(int)PaidEventRefundProtection.CardDisputeRightsNotWaived},
                        {(int)PaidEventRefundProtection.CancelledEventPlatformAmountsRefundedByDefault}]) required(id)
                        WHERE NOT EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(p_revision->'protections') protection
                            WHERE (protection->>{protectionId})::integer = required.id)) THEN
                    RAISE EXCEPTION 'configuration_import_paid_policy_normalization_invalid' USING ERRCODE = '22023';
                END IF;
                IF p_expected_id IS NULL THEN
                    IF EXISTS (SELECT 1 FROM {policies} policy WHERE {TargetPredicate(context, "policy.", allowPage: false)}) THEN
                        RAISE EXCEPTION 'configuration_import_paid_policy_concurrency_conflict' USING ERRCODE = '40001';
                    END IF;
                ELSE
                    UPDATE {policies} policy SET {P("IsActive")} = false,
                        {P("ActiveUniquenessSlot")} = policy.{P("VersionNumber")},
                        {P("UpdatedAt")} = COALESCE(policy.{P("UpdatedAt")}, p_at),
                        {P("UpdatedBy")} = COALESCE(p_actor, policy.{P("UpdatedBy")})
                        WHERE {TargetPredicate(context, "policy.", allowPage: false)}
                            AND policy.{P("Id")} = p_expected_id AND policy.{P("VersionNumber")} = p_expected_version
                            AND policy.{P("IsActive")} AND policy.{P("ActiveUniquenessSlot")} = 0;
                    GET DIAGNOSTICS affected = ROW_COUNT;
                    IF affected <> 1 THEN
                        RAISE EXCEPTION 'configuration_import_paid_policy_concurrency_conflict' USING ERRCODE = '40001';
                    END IF;
                END IF;
                INSERT INTO {policies} ({string.Join(", ", RootColumns.Select(P))})
                    VALUES ({string.Join(", ", RootColumns.Select(name => "r." + P(name)))});
                {childInserts}
                RETURN;
            END;
            """;
    }
}

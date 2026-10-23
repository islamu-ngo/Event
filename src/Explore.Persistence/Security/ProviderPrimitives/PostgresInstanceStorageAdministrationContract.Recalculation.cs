using Explore.Domain;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

public static partial class PostgresInstanceStorageAdministrationContract
{
    public static string RecalculateFunctionSql(ExploreDbContext context) =>
        FunctionSql(context, "instance_storage_recalculate");

    private static string BuildRecalculationSql(ExploreDbContext context)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        string Table<T>()
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            return sql.DelimitIdentifier(entity.GetTableName()!, Schema(context, entity));
        }
        string Column<T>(string property)
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            return sql.DelimitIdentifier(entity.FindProperty(property)!.GetColumnName(
                StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!);
        }
        string Columns<T>(params string[] properties) => string.Join(", ", properties.Select(Column<T>));
        string C(string property) => Column<StorageUsageCounter>(property);
        string schema = sql.DelimitIdentifier(Schema(context, context.Model.FindEntityType(typeof(StorageUsageCounter))!));
        string counters = Table<StorageUsageCounter>(), objects = Table<StorageObject>();
        string sessions = Table<StorageUploadSession>(), tombstones = Table<StorageObjectDeletionTombstone>();
        string id = C(nameof(StorageUsageCounter.Id)), tenant = C(nameof(StorageUsageCounter.TenantId));
        string provider = C(nameof(StorageUsageCounter.Provider)), stamp = C(nameof(StorageUsageCounter.ConcurrencyStamp));
        string used = C(nameof(StorageUsageCounter.UsedBytes)), reserved = C(nameof(StorageUsageCounter.ReservedBytes));
        string quarantined = C(nameof(StorageUsageCounter.QuarantinedBytes)), count = C(nameof(StorageUsageCounter.ObjectCount));
        string last = C(nameof(StorageUsageCounter.LastRecalculatedAt));
        string created = C(nameof(StorageUsageCounter.CreatedAt)), updated = C(nameof(StorageUsageCounter.UpdatedAt));
        string totals = StorageScopeTotalsQuery.BuildSql(context, "v_scope.\"TenantId\"", "v_scope.\"Provider\"");
        string function = RecalculateFunctionSql(context);
        return $$"""
            GRANT CREATE ON SCHEMA {{schema}} TO {{OwnerRole}};
            GRANT SELECT ({{id}}, {{tenant}}, {{provider}}, {{stamp}}, {{used}}, {{reserved}}, {{quarantined}}, {{count}}, {{last}})
                ON {{counters}} TO {{OwnerRole}};
            GRANT SELECT ({{Columns<StorageObject>(nameof(StorageObject.Id), nameof(StorageObject.TenantId),
                nameof(StorageObject.Provider), nameof(StorageObject.Size), nameof(StorageObject.Purpose),
                nameof(StorageObject.LifecycleState))}}) ON {{objects}} TO {{OwnerRole}};
            GRANT SELECT ({{Columns<StorageUploadSession>(nameof(StorageUploadSession.TenantId),
                nameof(StorageUploadSession.Provider), nameof(StorageUploadSession.StorageObjectId),
                nameof(StorageUploadSession.Status), nameof(StorageUploadSession.ReservedBytes))}})
                ON {{sessions}} TO {{OwnerRole}};
            GRANT SELECT ({{Column<StorageObjectDeletionTombstone>(nameof(StorageObjectDeletionTombstone.Id))}})
                ON {{tombstones}} TO {{OwnerRole}};
            GRANT INSERT ({{id}}, {{tenant}}, {{provider}}, {{stamp}}, {{used}}, {{reserved}}, {{quarantined}}, {{count}},
                {{created}}, {{updated}}) ON {{counters}} TO {{OwnerRole}};
            GRANT UPDATE ({{stamp}}, {{used}}, {{reserved}}, {{quarantined}}, {{count}}, {{last}}, {{updated}})
                ON {{counters}} TO {{OwnerRole}};
            DROP POLICY IF EXISTS instance_storage_admin_repair ON {{counters}};
            CREATE POLICY instance_storage_admin_repair ON {{counters}} FOR ALL TO {{OwnerRole}}
                USING (true) WITH CHECK (true);
            DROP POLICY IF EXISTS instance_storage_admin_tombstones ON {{tombstones}};
            CREATE POLICY instance_storage_admin_tombstones ON {{tombstones}} FOR SELECT TO {{OwnerRole}} USING (true);
            DO $install_repair$
            DECLARE v_previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{{OwnerRole}}', true);
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {{function}}()
                RETURNS TABLE ("Provider" text, "UsedBytes" bigint, "ReservedBytes" bigint,
                    "QuarantinedBytes" bigint, "ObjectCount" bigint, "LastRecalculatedAt" timestamp with time zone)
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                AS $body$
                DECLARE v_scope record; v_totals record; v_id uuid; v_stamp uuid; v_rows integer;
                    v_now timestamp with time zone := clock_timestamp();
                BEGIN
                    FOR v_scope IN SELECT * FROM {{ScopesFunctionSql(context)}}() ORDER BY "TenantId", "Provider"
                    LOOP
                        INSERT INTO {{counters}} ({{id}}, {{tenant}}, {{provider}}, {{stamp}},
                            {{used}}, {{reserved}}, {{quarantined}}, {{count}}, {{created}}, {{updated}})
                            VALUES (uuidv7(), v_scope."TenantId", v_scope."Provider", uuidv7(),
                                0, 0, 0, 0, v_now, v_now) ON CONFLICT ({{tenant}}, {{provider}}) DO NOTHING;
                        SELECT c.{{id}}, c.{{stamp}} INTO v_id, v_stamp FROM {{counters}} c
                            WHERE c.{{tenant}} = v_scope."TenantId" AND c.{{provider}} = v_scope."Provider" FOR UPDATE;
                        UPDATE {{counters}} c SET {{stamp}} = uuidv7()
                            WHERE c.{{id}} = v_id AND c.{{stamp}} = v_stamp;
                        GET DIAGNOSTICS v_rows = ROW_COUNT;
                        IF v_rows <> 1 THEN
                            RAISE EXCEPTION 'storage accounting fence changed' USING ERRCODE = '40001';
                        END IF;
                        SELECT * INTO v_totals FROM ({{totals}}) scope_totals;
                        UPDATE {{counters}} c SET {{used}} = v_totals."Used", {{reserved}} = v_totals."Reserved",
                            {{quarantined}} = v_totals."Quarantined", {{count}} = v_totals."Count",
                            {{last}} = v_now, {{updated}} = v_now WHERE c.{{id}} = v_id;
                    END LOOP;
                    RETURN QUERY SELECT * FROM {{UsageFunctionSql(context)}}();
                END $body$ $ddl$;
                EXECUTE 'REVOKE ALL ON FUNCTION {{function}}() FROM PUBLIC';
                EXECUTE 'GRANT EXECUTE ON FUNCTION {{function}}() TO {{GlobalRole}}';
                PERFORM set_config('role', v_previous_role, true);
            END $install_repair$;
            REVOKE CREATE ON SCHEMA {{schema}} FROM {{OwnerRole}};
            """;
    }
}

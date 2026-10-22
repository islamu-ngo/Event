using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace Explore.Persistence.Security;

public static partial class PostgresMixedSettingsContract
{
    private static bool CanInsert(Type type) => type != typeof(Explore.Domain.EventLocation)
        && type != typeof(Explore.Domain.TenantPlanAssignment);

    private static Type[] AllWriteTypes => [.. MixedSettingsDataImage.WriteTypes, typeof(Explore.Domain.TenantPlanAssignment),
        .. MixedSettingsDataImage.GovernanceWriteTypes];

    private static string[] UpdateColumns(Type type) =>
        type == typeof(Explore.Domain.SystemSetting)
            ? ["Value", "IsLocked", "SettingValueTypeId", "AllowedValues", "Description", "Category", "DisplayOrder", "UpdatedAt", "UpdatedBy"]
        : type == typeof(Explore.Domain.TenantSetting) ? ["Value", "IsLocked", "UpdatedAt", "UpdatedBy"]
        : type == typeof(Explore.Domain.TenantPlanAssignment) ? ["UpdatedAt", "UpdatedBy"]
        : type == typeof(Explore.Domain.Modules.TenantCapability) ? ["IsEnabled", "EnabledAt", "EnabledBy", "UpdatedAt", "UpdatedBy"]
        : type == typeof(Explore.Domain.Settings.Documents.TenantSettingsDocument) ? ["PayloadJson", "ConcurrencyStamp", "UpdatedAt", "UpdatedBy"]
        : type == typeof(Explore.Domain.EmailDispatchProcessorState) || type == typeof(Explore.Domain.EmailDispatchTenantControl)
            ? ["DeliveryPolicyRevision", "OptionalSuppressedThroughRevision", "OptionalSuppressedThroughUtc", "UpdatedAt", "UpdatedBy"]
        : type == typeof(Explore.Domain.EventLocation)
            ? ["NeedsPrivacyReview", "PolicyVersion", "ConcurrencyStamp", "LastPolicyActorUserId", "LastPolicyChangedAtUtc", "UpdatedAt", "UpdatedBy"]
        : [];

    private static (Type Type, string[] Columns)[] Sources(ExploreDbContext context) =>
        HumanPrincipalSourceSql.Sources.Concat(HumanPrincipalSourceSql.LocalSources)
            .Concat(HumanSupportAccessSourceSql.Sources)
            .Concat(MixedSettingsDataImage.ReadTypes.Select(type => (Type: type, Columns: MixedSettingsDataImage.Properties(context, type, typed: true))))
            .Concat(MixedSettingsDataImage.ReadTypes.Select(type => (Type: type, Columns: MixedSettingsDataImage.Properties(context, type))))
            .Concat(MixedSettingsDataImage.PlanReadTypes.Select(type => (Type: type, Columns: MixedSettingsDataImage.Properties(context, type))))
            .Concat(MixedSettingsDataImage.GovernanceReadTypes.Select(type => (Type: type, Columns: MixedSettingsDataImage.Properties(context, type))))
            .Concat(MixedSettingsDataImage.WriteTypes.Select(type =>
                (Type: type, Columns: context.Model.FindEntityType(type)!.GetProperties().Select(property => property.Name).ToArray())))
            .GroupBy(source => source.Type)
            .Select(group => (Type: group.Key, Columns: group.SelectMany(source => source.Columns).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()))
            .OrderBy(source => source.Type.Name, StringComparer.Ordinal).ToArray();

    private static PostgresPrivilegeGuard.Contract Privileges(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        if (new[] { enrollment.MigratorLogin, enrollment.GlobalLogin, enrollment.RuntimeLogin,
            enrollment.EmailInstanceMutationLogin, enrollment.EmailTenantMutationLogin }.Any(login => Roles.Contains(login)))
            throw new InvalidOperationException("mixed_settings_enrollment_collision");
        var sql = new HumanPrincipalSourceSql(context);
        return new(sql.Schema, Roles,
            Routines.Select(routine => new PostgresPrivilegeGuard.Routine(Prefix + routine.Name, routine.Signature,
                OwnerRole, routine.Name == "email_consume" ? [PostgresEmailDeliveryPolicyContract.OwnerRole]
                    : routine.Result == "trigger" ? [OwnerRole] : [GlobalRole])).ToArray(),
            [
                .. Sources(context).Select(source => new PostgresPrivilegeGuard.Relation(
                    context.Model.FindEntityType(source.Type)!.GetTableName()!, OwnerRole,
                    source.Type == typeof(Explore.Domain.TenantSetting) ? ["DELETE"] : [],
                    source.Columns.Select(property => new PostgresPrivilegeGuard.Column(sql.ColumnName(source.Type, property),
                        AllWriteTypes.Contains(source.Type)
                            ? ["SELECT", .. CanInsert(source.Type) ? new[] { "INSERT" } : [],
                                .. UpdateColumns(source.Type).Contains(property, StringComparer.Ordinal) ? new[] { "UPDATE" } : []]
                            : ["SELECT"])).ToArray())),
                new(SourceTable, OwnerRole, [], [new("id", ["SELECT"]), new("revision", ["SELECT", "UPDATE"])]),
                new(AttemptTable, OwnerRole, ["SELECT", "INSERT", "UPDATE"], [])
            ],
            [new(OwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(GlobalRole, enrollment.GlobalLogin, true, false, false)]);
    }

    internal static PostgresPrivilegeGuard.Contract ComposedPrivileges(ExploreDbContext context,
        PostgresNativeQueueEnrollment enrollment)
    {
        var mixed = Privileges(context, enrollment);
        var email = PostgresEmailDeliveryPolicyContract.BuildPrivilegeContract(context, enrollment);
        return new(mixed.Schema,
            [.. mixed.GuardedRoles, .. email.GuardedRoles],
            [.. mixed.Routines, .. email.Routines],
            [.. mixed.Relations, .. email.Relations],
            [.. mixed.Memberships, .. email.Memberships]);
    }

    private static string SourceGuard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model);
        return string.Join("\n", Sources(context).Select(source =>
        {
            var entity = context.Model.FindEntityType(source.Type)!;
            var declared = context.GetService<IDesignTimeModel>().Model.FindEntityType(source.Type)!;
            var properties = entity.GetProperties().ToArray();
            string table = Text(sql.Table(source.Type));
            bool tenant = tenantTables.Any(candidate => candidate.TableName == entity.GetTableName());
            string columns = string.Join(" OR ", properties.Select(property =>
            {
                string collation = property.FindAnnotation(PortableOrdinalAsciiPropertyExtensions.AnnotationName)?.Value is true
                    || property.FindAnnotation(LocationUnicodePropertyExtensions.AnnotationName)?.Value is true ? "C" : "default";
                string identity = NpgsqlPropertyExtensions.GetValueGenerationStrategy(declared.FindProperty(property.Name)!) switch
                {
                    NpgsqlValueGenerationStrategy.IdentityByDefaultColumn => "d",
                    NpgsqlValueGenerationStrategy.IdentityAlwaysColumn => "a",
                    _ => ""
                };
                return $"""
                    NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({table})
                        AND a.attname = {Text(sql.ColumnName(source.Type, property.Name))} AND a.attnum > 0 AND NOT a.attisdropped
                        AND pg_catalog.format_type(a.atttypid,a.atttypmod) = {Text(property.GetRelationalTypeMapping().StoreType)}
                        AND a.attnotnull = {(property.IsNullable ? "false" : "true")} AND a.attgenerated = '' AND a.attidentity = {Text(identity)}
                        AND (a.attcollation = 0 OR a.attcollation = pg_catalog.to_regcollation({Text("pg_catalog." + sql.Quote(collation))})))
                    """;
            }));
            string keys = string.Join("\n", entity.GetKeys().Select(key =>
                    (key.Properties, Filter: (string?)null, Descending: (IReadOnlyList<bool>?)null))
                .Concat(declared.GetIndexes().Where(index => index.IsUnique).Select(index =>
                    (index.Properties, Filter: index.GetFilter(), Descending: index.IsDescending)))
                .Select(index =>
                {
                    string? predicate = index.Filter switch
                    {
                        null => null,
                        "is_deleted = false AND is_to_be_announced = true" =>
                            "((is_deleted = false) AND (is_to_be_announced = true))",
                        "is_deleted = false AND is_to_be_announced = false AND location_id IS NOT NULL" =>
                            "((is_deleted = false) AND (is_to_be_announced = false) AND (location_id IS NOT NULL))",
                        "status_id = 2 AND ended_at_utc IS NULL" =>
                            "((status_id = 2) AND (ended_at_utc IS NULL))",
                        var filter => "(" + filter + ")"
                    };
                    return $"""
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_index i WHERE i.indrelid = pg_catalog.to_regclass({table})
                        AND i.indisunique AND i.indisvalid AND i.indisready AND i.indimmediate AND i.indexprs IS NULL
                        AND i.indnkeyatts = {index.Properties.Count} AND i.indnatts = {index.Properties.Count}
                        AND ARRAY(SELECT option.flag FROM unnest(i.indoption::smallint[])
                            WITH ORDINALITY option(flag,ord) ORDER BY option.ord)
                            = ARRAY[{string.Join(",", index.Properties.Select((_, position) =>
                                index.Descending is { Count: 0 } || index.Descending?[position] == true ? "3" : "0"))}]::smallint[]
                        AND NOT EXISTS (SELECT 1 FROM unnest(i.indclass::oid[]) operator_id
                            JOIN pg_catalog.pg_opclass o ON o.oid = operator_id JOIN pg_catalog.pg_am am ON am.oid = o.opcmethod
                            WHERE NOT o.opcdefault OR am.amname <> 'btree')
                        AND NOT EXISTS (SELECT 1 FROM unnest(i.indcollation::oid[]) WITH ORDINALITY col(value,ord)
                            JOIN unnest(i.indkey::smallint[]) WITH ORDINALITY k(attnum,ord) ON k.ord = col.ord
                            JOIN pg_catalog.pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.attnum
                            WHERE col.value <> a.attcollation)
                        AND {(predicate is null ? "i.indpred IS NULL" : $"pg_catalog.pg_get_expr(i.indpred,i.indrelid) = {Text(predicate)}")}
                        AND ARRAY(SELECT a.attname FROM unnest(i.indkey::smallint[]) WITH ORDINALITY k(id,ord)
                            JOIN pg_catalog.pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.id
                            WHERE k.ord <= i.indnkeyatts ORDER BY k.ord)
                            = ARRAY[{string.Join(",", index.Properties.Select(property => Text(sql.ColumnName(source.Type, property.Name))))}]::name[])
                    THEN RAISE EXCEPTION 'mixed_settings_source_key_drift' USING ERRCODE = '55000'; END IF;
                    """;
                }));
            return $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t WHERE t.oid = pg_catalog.to_regclass({table})
                    AND t.relkind = 'r' AND t.relpersistence = 'p'
                    AND t.relowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid
                    AND ({(source.Type == typeof(Explore.Domain.SystemSetting)
                        ? PostgresLocalIdentitySystemSettingPolicies.Compatible(OwnerRole, "t")
                        : $"t.relrowsecurity = {tenant.ToString().ToLowerInvariant()} AND t.relforcerowsecurity = {tenant.ToString().ToLowerInvariant()}")}))
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r WHERE r.ev_class = pg_catalog.to_regclass({table}))
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_inherits i
                        WHERE i.inhrelid = pg_catalog.to_regclass({table}) OR i.inhparent = pg_catalog.to_regclass({table}))
                    OR (SELECT count(*) FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({table})
                        AND a.attnum > 0 AND NOT a.attisdropped) <> {properties.Length}
                    OR {columns}
                THEN RAISE EXCEPTION 'mixed_settings_source_catalog_drift' USING ERRCODE = '55000'; END IF;
                {keys}
                """;
        }));
    }

    private static string Guard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string roles = string.Join(",", Roles.Select(Text));
        string routineIds = string.Join(",", Routines.Select(routine =>
            $"pg_catalog.to_regprocedure({Text(FunctionSql(context, routine.Name) + "(" + routine.Signature + ")")})"));
        string routines = string.Join("\n", Routines.Select(routine => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                WHERE f.oid = pg_catalog.to_regprocedure({Text(FunctionSql(context, routine.Name) + "(" + routine.Signature + ")")})
                    AND f.provolatile = 'v' AND f.proparallel = 'u' AND NOT f.proleakproof AND NOT f.proisstrict
                    AND NOT f.proretset AND f.proargdefaults IS NULL AND f.prorettype = 'pg_catalog.{routine.Result}'::regtype
                    AND f.prolang = (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql')
                    AND pg_catalog.obj_description(f.oid,'pg_proc') =
                        '{Stamp}' || pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex'))
            THEN RAISE EXCEPTION 'mixed_settings_routine_drift' USING ERRCODE = '55000'; END IF;
            """));
        string triggers = string.Join("\n", Sources(context).Select(source => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t
                WHERE t.tgrelid = pg_catalog.to_regclass({Text(sql.Table(source.Type))}) AND t.tgname = '{Prefix}changed'
                    AND t.tgfoid = pg_catalog.to_regprocedure({Text(FunctionSql(context, "changed") + "()")})
                    AND t.tgtype = 62 AND t.tgenabled = 'A' AND NOT t.tgisinternal AND t.tgqual IS NULL
                    AND t.tgnargs = 0 AND t.tgargs = ''::bytea AND t.tgattr = ''::int2vector AND t.tgconstraint = 0
                    AND NOT t.tgdeferrable AND NOT t.tginitdeferred AND t.tgoldtable IS NULL AND t.tgnewtable IS NULL)
            THEN RAISE EXCEPTION 'mixed_settings_source_exclusion_drift' USING ERRCODE = '55000'; END IF;
            """));
        var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model);
        string policies = string.Join("\n", Sources(context).Where(source =>
            tenantTables.Any(candidate => candidate.TableName == context.Model.FindEntityType(source.Type)!.GetTableName()))
            .Select(source => $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid = {Text(sql.Table(source.Type))}::regclass
                    AND p.polname = '{Policy}' AND p.polcmd = '{(AllWriteTypes.Contains(source.Type) ? "*" : "r")}'
                    AND p.polpermissive AND p.polroles = ARRAY[pg_catalog.to_regrole('{OwnerRole}')::oid]
                    AND pg_catalog.pg_get_expr(p.polqual,p.polrelid) = 'true'
                    AND {(AllWriteTypes.Contains(source.Type) ? "pg_catalog.pg_get_expr(p.polwithcheck,p.polrelid) = 'true'" : "p.polwithcheck IS NULL")})
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid = {Text(sql.Table(source.Type))}::regclass
                        AND p.polname NOT IN ('{Policy}','{PostgresTenantRowLevelSecurityModel.PolicyName}')
                        AND (0::oid = ANY(p.polroles) OR EXISTS (SELECT 1 FROM unnest(p.polroles) role_id
                            WHERE pg_catalog.pg_has_role('{OwnerRole}',role_id,'USAGE'))))
                THEN RAISE EXCEPTION 'mixed_settings_source_policy_drift' USING ERRCODE = '55000'; END IF;
                """));
        string internalTables = string.Join("\n", new[] { SourceTable, AttemptTable }.Select(name => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t WHERE t.oid = pg_catalog.to_regclass({Text(sql.Name(name))})
                AND t.relkind = 'r' AND t.relpersistence = 'p' AND NOT t.relrowsecurity AND NOT t.relforcerowsecurity
                AND t.relowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid)
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgrelid = pg_catalog.to_regclass({Text(sql.Name(name))}) AND NOT t.tgisinternal
                    AND NOT (t.tgrelid = pg_catalog.to_regclass({Text(sql.Name(AttemptTable))})
                        AND t.tgname = '{Prefix}settlement'
                        AND t.tgfoid = pg_catalog.to_regprocedure({Text(FunctionSql(context, "settlement") + "()")})))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r WHERE r.ev_class = pg_catalog.to_regclass({Text(sql.Name(name))}))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_inherits i WHERE i.inhrelid = pg_catalog.to_regclass({Text(sql.Name(name))})
                    OR i.inhparent = pg_catalog.to_regclass({Text(sql.Name(name))}))
            THEN RAISE EXCEPTION 'mixed_settings_receipt_catalog_drift' USING ERRCODE = '55000'; END IF;
            """));
        var fields = new[]
        {
            (SourceTable, "id", "int2", true), (SourceTable, "revision", "int8", true),
            (AttemptTable, "attempt", "int8", true), (AttemptTable, "backend", "int4", true),
            (AttemptTable, "tenant", "uuid", false), (AttemptTable, "actor", "uuid", true),
            (AttemptTable, "request", "jsonb", true), (AttemptTable, "accepted", "jsonb", false),
            (AttemptTable, "completed", "jsonb", true), (AttemptTable, "effects", "jsonb", true),
            (AttemptTable, "email_state", "jsonb", true)
        };
        string fieldChecks = string.Join("\n", fields.Select(field => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({Text(sql.Name(field.Item1))})
                AND a.attname = {Text(field.Item2)} AND a.attnum > 0 AND NOT a.attisdropped
                AND a.atttypid = 'pg_catalog.{field.Item3}'::regtype AND a.atttypmod = -1
                AND a.attnotnull = {field.Item4.ToString().ToLowerInvariant()} AND a.attgenerated = '' AND a.attidentity = ''
                AND NOT a.atthasdef AND a.attcollation = 0)
            THEN RAISE EXCEPTION 'mixed_settings_receipt_column_drift' USING ERRCODE = '55000'; END IF;
            """));
        string scalarDenial = string.Join("\n", AllWriteTypes.Select(type =>
        {
            string insertDenial = $"pg_catalog.has_any_column_privilege({Text(enrollment.GlobalLogin)},{Text(sql.Table(type))}::regclass,'INSERT')";
            // Only the actual creator INSERT-source intersection may coexist.
            // Its complete catalogue proof includes the restrictive document-key policy.
            if (PostgresTenantCreationPermissions.TryGetGlobalColumns(context, type, out _, out var insertColumns)
                && insertColumns.Count != 0)
                insertDenial += $" AND ({PostgresTenantCreationPermissions.OptionalGlobalCataloguePredicate(context, enrollment)}) IS NOT TRUE";
            if (PostgresManagedTenantWorkerCreationPermissions.TryGetGlobalColumns(context, type, out _, out var workerInsert, out _)
                && workerInsert.Count != 0)
                insertDenial += $" AND ({PostgresManagedTenantWorkerCreationPermissions.CompleteGlobalCataloguePredicate(context, enrollment)}) IS NOT TRUE";
            return $"""
                IF pg_catalog.has_table_privilege({Text(enrollment.GlobalLogin)},{Text(sql.Table(type))}::regclass,'INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                    OR pg_catalog.has_any_column_privilege({Text(enrollment.GlobalLogin)},{Text(sql.Table(type))}::regclass,'UPDATE,REFERENCES')
                    OR ({insertDenial})
                THEN RAISE EXCEPTION 'mixed_settings_ambient_scalar_authority' USING ERRCODE = '55000'; END IF;
                """;
        }));
        return $"""
            {SourceGuard(context, enrollment)}
            {PostgresManagedProviderProvisioningContract.SharedWriterAssertionSql(context, enrollment)}
            {PostgresPrivilegeGuard.BuildAssertSql(ComposedPrivileges(context, enrollment))}
            {routines}
            {triggers}
            {policies}
            {internalTables}
            {fieldChecks}
            {scalarDenial}
            IF (SELECT count(*) FROM pg_catalog.pg_attribute a WHERE a.attrelid = {Text(sql.Name(SourceTable))}::regclass AND a.attnum > 0 AND NOT a.attisdropped) <> 2
                OR (SELECT count(*) FROM pg_catalog.pg_attribute a WHERE a.attrelid = {Text(sql.Name(AttemptTable))}::regclass AND a.attnum > 0 AND NOT a.attisdropped) <> 9
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c WHERE c.conrelid = {Text(sql.Name(SourceTable))}::regclass
                    AND c.contype = 'p' AND NOT c.condeferrable AND c.convalidated AND pg_catalog.pg_get_constraintdef(c.oid) = 'PRIMARY KEY (id)'
                    AND EXISTS (SELECT 1 FROM pg_catalog.pg_index i WHERE i.indexrelid = c.conindid
                        AND i.indisunique AND i.indisvalid AND i.indisready AND i.indimmediate))
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c WHERE c.conrelid = {Text(sql.Name(AttemptTable))}::regclass
                    AND c.contype = 'p' AND NOT c.condeferrable AND c.convalidated AND pg_catalog.pg_get_constraintdef(c.oid) = 'PRIMARY KEY (attempt, backend)'
                    AND EXISTS (SELECT 1 FROM pg_catalog.pg_index i WHERE i.indexrelid = c.conindid
                        AND i.indisunique AND i.indisvalid AND i.indisready AND i.indimmediate))
                OR (SELECT count(*) FROM pg_catalog.pg_constraint c WHERE c.conrelid = {Text(sql.Name(SourceTable))}::regclass AND c.contype <> 'n') <> 3
                OR (SELECT count(*) FROM pg_catalog.pg_constraint c WHERE c.conrelid = {Text(sql.Name(AttemptTable))}::regclass AND c.contype <> 'n') <> 2
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c WHERE c.conrelid = {Text(sql.Name(SourceTable))}::regclass
                    AND c.contype = 'c' AND c.convalidated AND NOT c.connoinherit AND pg_catalog.pg_get_expr(c.conbin,c.conrelid) = '(id = 1)')
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c WHERE c.conrelid = {Text(sql.Name(SourceTable))}::regclass
                    AND c.contype = 'c' AND c.convalidated AND NOT c.connoinherit AND pg_catalog.pg_get_expr(c.conbin,c.conrelid) = '(revision >= 0)')
                OR (SELECT count(*) FROM {sql.Name(SourceTable)}) <> 1 OR NOT EXISTS (SELECT 1 FROM {sql.Name(SourceTable)} WHERE id = 1)
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgrelid = {Text(sql.Name(AttemptTable))}::regclass
                    AND t.tgname = '{Prefix}settlement' AND t.tgfoid = pg_catalog.to_regprocedure({Text(FunctionSql(context, "settlement") + "()")})
                    AND t.tgtype = 5 AND t.tgenabled = 'A' AND NOT t.tgisinternal AND t.tgqual IS NULL
                    AND t.tgnargs = 0 AND t.tgargs = ''::bytea AND t.tgattr = ''::int2vector
                    AND t.tgconstraint <> 0 AND t.tgdeferrable AND t.tginitdeferred
                    AND t.tgoldtable IS NULL AND t.tgnewtable IS NULL)
            THEN RAISE EXCEPTION 'mixed_settings_receipt_key_drift' USING ERRCODE = '55000'; END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n WHERE n.nspname = {Text(sql.Schema)}
                    AND n.nspowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid)
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r WHERE r.rolname IN ({roles})
                    AND pg_catalog.shobj_description(r.oid,'pg_authid') IS DISTINCT FROM '{Stamp}role:' || r.rolname)
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n CROSS JOIN pg_catalog.pg_roles r
                    WHERE r.rolname IN ({roles}) AND pg_catalog.has_schema_privilege(r.oid,n.oid,'CREATE'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r WHERE r.rolname IN ({roles})
                    AND NOT pg_catalog.has_schema_privilege(r.oid,{Text(sql.Schema)},'USAGE'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_class t CROSS JOIN pg_catalog.pg_roles r
                    WHERE r.rolname IN ({roles}) AND t.relkind = 'S' AND pg_catalog.has_sequence_privilege(r.oid,t.oid,'USAGE,SELECT,UPDATE'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_database d CROSS JOIN pg_catalog.pg_roles r
                    WHERE r.rolname IN ({roles}) AND pg_catalog.has_database_privilege(r.oid,d.oid,'CREATE'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_class t JOIN pg_catalog.pg_namespace n ON n.oid = t.relnamespace
                    CROSS JOIN pg_catalog.pg_roles r WHERE r.rolname IN ({roles}) AND n.nspname <> {Text(sql.Schema)}
                        AND n.nspname NOT IN ('pg_catalog','information_schema') AND n.nspname NOT LIKE 'pg_toast%'
                        AND t.relkind IN ('r','p','v','m','f')
                        AND (pg_catalog.has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                            OR pg_catalog.has_any_column_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,REFERENCES')))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_shdepend d WHERE d.refclassid = 'pg_catalog.pg_authid'::regclass
                    AND d.refobjid IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles})) AND d.deptype = 'o'
                    AND (d.classid <> 'pg_catalog.pg_proc'::regclass OR d.dbid <> (SELECT oid FROM pg_catalog.pg_database WHERE datname = current_database())
                        OR d.objid NOT IN ({routineIds})))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc f WHERE f.oid NOT IN ({routineIds})
                    AND (f.proowner IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles}))
                        OR EXISTS (SELECT 1 FROM pg_catalog.aclexplode(f.proacl) a WHERE a.grantee IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles})))))
            THEN RAISE EXCEPTION 'mixed_settings_authority_drift' USING ERRCODE = '55000'; END IF;
            """;
    }

    private static string ExactBodies(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        string.Join("\n", Functions(context, enrollment).Select(function => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                WHERE f.oid = pg_catalog.to_regprocedure({Text(FunctionSql(context, function.Name) + "(" + function.Signature + ")")})
                    AND pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex') = {Text(Hash(function.Body))})
            THEN RAISE EXCEPTION 'mixed_settings_exact_body_drift' USING ERRCODE = '55000'; END IF;
            """));
}

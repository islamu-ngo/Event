using System.Data.Common;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Explore.Persistence.Security;

/// <summary>Fixed Runtime room-update authority; separate from footer and key-management execution.</summary>
public static class PostgresMachineRoomMutationAuthorityContract
{
    public const string OwnerRole = "event_machine_room_owner";
    public const string MigratorRole = "event_machine_room_migrator";
    public const string RuntimeRole = "event_machine_room_runtime";
    internal const string FunctionName = "machine_room_update_authority";

    public static async Task ApplyAsync(ExploreDbContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql()) return;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync(BuildSql(context), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public static string BuildSql(ExploreDbContext context)
    {
        var sql = new InstanceApiKeyCapabilitySql(context);
        var function = Function(context);
        return sql.Roles(OwnerRole, MigratorRole, RuntimeRole)
            + sql.Grants<User>(OwnerRole, RuntimeRole, ["Id", "IsDeleted"], ["Id"])
            + sql.Grants<PlatformUserRole>(OwnerRole, RuntimeRole, ["Id", "UserId", "RoleId"], ["Id"])
            + sql.Grants<Role>(OwnerRole, RuntimeRole, ["Id", "MasterCode", "RoleScopeId"], ["Id"])
            + sql.Grants<Tenant>(OwnerRole, RuntimeRole, ["Id", "TenantStatusId"], ["Id"])
            + sql.Grants<TenantStatus>(OwnerRole, RuntimeRole, ["Id", "IsActiveState"], ["Id"])
            + sql.Grants<LocationRoom>(OwnerRole, RuntimeRole, ["Id", "TenantId", "LocationId", "IsDeleted"], ["Id"])
            + sql.Grants<Location>(OwnerRole, RuntimeRole, ["Id", "TenantId", "LocationPrivacyStateId"], ["Id"])
            + sql.Grants<ExternalApiKey>(OwnerRole, RuntimeRole,
                ["Id", "KeyId", "TenantId", "OwnerId", "ExternalApiKeyOwnerTypeId", "ExternalApiKeyStatusId", "ExpiresAt", "Scopes"], ["Id"])
            + sql.Define(function.Name, function.Arguments, function.Returns, function.Body, OwnerRole, RuntimeRole)
            + sql.Finish(OwnerRole);
    }

    internal static InstanceApiKeySqlFunction Function(ExploreDbContext context)
    {
        var sql = new InstanceApiKeyCapabilitySql(context);
        string K(string name) => sql.Column<ExternalApiKey>(name);
        string forbiddenRoles = string.Join(", ", PostgresMachineMutationAuthorityContract.FrozenRoles.Select(role => $"'{role}'"));
        string forbiddenFunctions = string.Join(", ", FrozenFunctions(context).Select(value => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'"));
        return new(FunctionName,
            "p_key uuid, p_key_id text, p_owner uuid, p_tenant uuid, p_room uuid, p_destination uuid, p_scopes text[], p_expires timestamp with time zone, p_status integer",
            "boolean", $$"""
            DECLARE
                v_assignments uuid[]; v_roles integer[];
                v_tenant_status integer; v_status integer; v_scopes text;
                v_expires timestamp with time zone; v_now timestamp with time zone;
                v_source uuid; v_destination uuid; v_location uuid;
            BEGIN
                IF current_setting('transaction_isolation') <> 'serializable'
                   OR p_tenant IS NULL OR p_tenant = '00000000-0000-0000-0000-000000000000'::uuid
                   OR p_room IS NULL OR p_room = '00000000-0000-0000-0000-000000000000'::uuid
                   OR p_destination = '00000000-0000-0000-0000-000000000000'::uuid
                   OR nullif(current_setting('app.current_tenant_id', true), '')::uuid IS DISTINCT FROM p_tenant
                   OR NOT pg_catalog.pg_has_role(session_user, '{{RuntimeRole}}', 'USAGE')
                   OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles role WHERE role.rolname IN ({{forbiddenRoles}})
                       AND pg_catalog.pg_has_role(session_user, role.oid, 'MEMBER'))
                   OR EXISTS (SELECT 1 FROM unnest(ARRAY[{{forbiddenFunctions}}]) signature
                       WHERE coalesce(pg_catalog.has_function_privilege(session_user, pg_catalog.to_regprocedure(signature), 'EXECUTE'), false))
                   OR p_status IS NULL OR p_status NOT IN ({{(int)ExternalApiKeyStatusEnum.Active}}, {{(int)ExternalApiKeyStatusEnum.PendingRotation}})
                   OR NOT coalesce(ARRAY(SELECT lower(s) FROM unnest(p_scopes) s)
                       && ARRAY['{{ExternalApiKeyScopes.AdminTenant}}', '{{ExternalApiKeyScopes.AdminInstance}}'], false)
                THEN RETURN false; END IF;
                PERFORM pg_catalog.pg_advisory_xact_lock(pg_catalog.hashtextextended('instance-api-key-owner:' || p_owner::text, 0));
                PERFORM u.{{sql.Column<User>("Id")}} FROM {{sql.Table<User>()}} u
                    WHERE u.{{sql.Column<User>("Id")}} = p_owner FOR UPDATE;
                IF NOT EXISTS (SELECT 1 FROM {{sql.Table<User>()}} u
                    WHERE u.{{sql.Column<User>("Id")}} = p_owner AND NOT u.{{sql.Column<User>("IsDeleted")}})
                THEN RETURN false; END IF;
                SELECT array_agg(a.{{sql.Column<PlatformUserRole>("Id")}} ORDER BY a.{{sql.Column<PlatformUserRole>("Id")}}),
                       array_agg(DISTINCT a.{{sql.Column<PlatformUserRole>("RoleId")}})
                INTO v_assignments, v_roles FROM {{sql.Table<PlatformUserRole>()}} a
                JOIN {{sql.Table<Role>()}} r ON r.{{sql.Column<Role>("Id")}} = a.{{sql.Column<PlatformUserRole>("RoleId")}}
                WHERE a.{{sql.Column<PlatformUserRole>("UserId")}} = p_owner
                    AND r.{{sql.Column<Role>("RoleScopeId")}} = {{(int)RoleScopeEnum.Platform}}
                    AND r.{{sql.Column<Role>("MasterCode")}} = 'platform.admin';
                PERFORM a.{{sql.Column<PlatformUserRole>("Id")}} FROM {{sql.Table<PlatformUserRole>()}} a
                    WHERE a.{{sql.Column<PlatformUserRole>("Id")}} = ANY(v_assignments)
                    ORDER BY a.{{sql.Column<PlatformUserRole>("Id")}} FOR UPDATE;
                PERFORM r.{{sql.Column<Role>("Id")}} FROM {{sql.Table<Role>()}} r
                    WHERE r.{{sql.Column<Role>("Id")}} = ANY(v_roles) ORDER BY r.{{sql.Column<Role>("Id")}} FOR UPDATE;
                IF NOT EXISTS (SELECT 1 FROM {{sql.Table<PlatformUserRole>()}} a
                    JOIN {{sql.Table<Role>()}} r ON r.{{sql.Column<Role>("Id")}} = a.{{sql.Column<PlatformUserRole>("RoleId")}}
                    WHERE a.{{sql.Column<PlatformUserRole>("Id")}} = ANY(v_assignments)
                        AND a.{{sql.Column<PlatformUserRole>("UserId")}} = p_owner
                        AND r.{{sql.Column<Role>("Id")}} = ANY(v_roles)
                        AND r.{{sql.Column<Role>("RoleScopeId")}} = {{(int)RoleScopeEnum.Platform}}
                        AND r.{{sql.Column<Role>("MasterCode")}} = 'platform.admin')
                THEN RETURN false; END IF;
                SELECT k.{{K("ExternalApiKeyStatusId")}}, k.{{K("ExpiresAt")}}, k.{{K("Scopes")}}
                INTO v_status, v_expires, v_scopes FROM {{sql.Table<ExternalApiKey>()}} k
                WHERE k.{{K("Id")}} = p_key AND k.{{K("KeyId")}} = p_key_id AND k.{{K("TenantId")}} IS NULL
                    AND k.{{K("OwnerId")}} = p_owner AND k.{{K("ExternalApiKeyOwnerTypeId")}} = {{(int)ExternalApiKeyOwnerType.InstanceAdmin}} FOR UPDATE;
                IF NOT FOUND THEN RETURN false; END IF;
                SELECT t.{{sql.Column<Tenant>("TenantStatusId")}} INTO v_tenant_status FROM {{sql.Table<Tenant>()}} t
                    WHERE t.{{sql.Column<Tenant>("Id")}} = p_tenant FOR UPDATE;
                IF NOT FOUND THEN RETURN false; END IF;
                PERFORM s.{{sql.Column<TenantStatus>("Id")}} FROM {{sql.Table<TenantStatus>()}} s
                    WHERE s.{{sql.Column<TenantStatus>("Id")}} = v_tenant_status FOR UPDATE;
                IF NOT EXISTS (SELECT 1 FROM {{sql.Table<TenantStatus>()}} s
                    WHERE s.{{sql.Column<TenantStatus>("Id")}} = v_tenant_status AND s.{{sql.Column<TenantStatus>("IsActiveState")}})
                THEN RETURN false; END IF;
                SELECT r.{{sql.Column<LocationRoom>("LocationId")}} INTO v_source FROM {{sql.Table<LocationRoom>()}} r
                    WHERE r.{{sql.Column<LocationRoom>("Id")}} = p_room AND r.{{sql.Column<LocationRoom>("TenantId")}} = p_tenant
                        AND NOT r.{{sql.Column<LocationRoom>("IsDeleted")}} FOR UPDATE;
                IF NOT FOUND THEN RETURN false; END IF;
                v_destination := coalesce(p_destination, v_source);
                FOR v_location IN SELECT DISTINCT id FROM unnest(ARRAY[v_source, v_destination]) id ORDER BY id LOOP
                    PERFORM l.{{sql.Column<Location>("Id")}} FROM {{sql.Table<Location>()}} l
                        WHERE l.{{sql.Column<Location>("Id")}} = v_location AND l.{{sql.Column<Location>("TenantId")}} = p_tenant
                            AND l.{{sql.Column<Location>("LocationPrivacyStateId")}} <> {{(int)LocationPrivacyStateEnum.Erased}} FOR UPDATE;
                    IF NOT FOUND THEN RETURN false; END IF;
                END LOOP;
                v_now := clock_timestamp();
                RETURN v_status IN ({{(int)ExternalApiKeyStatusEnum.Active}}, {{(int)ExternalApiKeyStatusEnum.PendingRotation}})
                    AND (p_expires IS NULL OR p_expires > v_now) AND (v_expires IS NULL OR v_expires > v_now)
                    AND ARRAY(SELECT lower(s) FROM regexp_split_to_table(v_scopes, '[ ,]+') s)
                        && ARRAY['{{ExternalApiKeyScopes.AdminTenant}}', '{{ExternalApiKeyScopes.AdminInstance}}'];
            END
            """, OwnerRole, RuntimeRole);
    }

    private static string[] FrozenFunctions(ExploreDbContext context)
    {
        var sql = new InstanceApiKeyCapabilitySql(context);
        return
        [
            PostgresTenantLookupContract.FunctionSql(context) + "()",
            PostgresExternalApiKeyAuthenticationContract.FunctionSql(context) + "(text)",
            PostgresExternalApiKeyAuthenticationContract.UsageFunctionSql(context) + "(uuid, timestamp with time zone, text, timestamp with time zone)",
            PostgresAdmissionScannerAuthenticationContract.FunctionSql(context) + "(integer[], text[])",
            .. PostgresInstanceApiKeyManagementContract.Functions(context).Concat(PostgresInstanceApiKeyIssuanceContract.Functions(context))
                .Append(PostgresPlatformApiKeyUsageReportContract.Function(context))
                .Select(function => sql.Function(function.Name) + "(" + function.ArgumentTypes + ")")
        ];
    }

    internal static void AddReadiness(DbCommand command, ExploreDbContext context, bool required)
    {
        var sql = new InstanceApiKeyCapabilitySql(context);
        var function = Function(context);
        command.CommandText += """
            AND (
                NOT @machine_room_required
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles r WHERE r.rolname = @machine_room_executor
                    AND pg_catalog.pg_has_role(current_user, r.oid, 'MEMBER'))
                AND NOT coalesce(pg_catalog.has_function_privilege(current_user, pg_catalog.to_regprocedure(@machine_room_signature), 'EXECUTE'), false)
                OR EXISTS (
                    SELECT 1 FROM pg_catalog.pg_proc f JOIN pg_catalog.pg_roles owner ON owner.oid = f.proowner
                    JOIN pg_catalog.pg_roles executor ON executor.rolname = @machine_room_executor
                    WHERE f.oid = pg_catalog.to_regprocedure(@machine_room_signature)
                        AND pg_catalog.pg_has_role(current_user, executor.oid, 'USAGE')
                        AND NOT executor.rolcanlogin AND NOT executor.rolsuper AND NOT executor.rolbypassrls
                        AND NOT executor.rolcreaterole AND NOT executor.rolcreatedb AND NOT executor.rolreplication
                        AND owner.rolname = @machine_room_owner
                        AND NOT owner.rolcanlogin AND NOT owner.rolsuper AND NOT owner.rolbypassrls
                        AND NOT owner.rolcreaterole AND NOT owner.rolcreatedb AND NOT owner.rolreplication
                        AND NOT pg_catalog.pg_has_role(current_user, owner.oid, 'MEMBER')
                        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                            WHERE r.rolname = @machine_room_migrator AND pg_catalog.pg_has_role(current_user, r.oid, 'MEMBER'))
                        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members edge WHERE edge.member IN (owner.oid, executor.oid))
                        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles r WHERE r.rolname = ANY(@machine_room_forbidden)
                            AND pg_catalog.pg_has_role(current_user, r.oid, 'MEMBER'))
                        AND NOT EXISTS (SELECT 1 FROM unnest(@machine_room_forbidden_functions) signature
                            WHERE coalesce(pg_catalog.has_function_privilege(current_user, pg_catalog.to_regprocedure(signature), 'EXECUTE'), false))
                        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc other
                            WHERE other.pronamespace = f.pronamespace AND other.prosecdef AND other.oid <> f.oid
                                AND pg_catalog.has_function_privilege(executor.oid, other.oid, 'EXECUTE'))
                        AND NOT pg_catalog.has_table_privilege(executor.oid, pg_catalog.to_regclass(@machine_room_keys), 'SELECT, INSERT, UPDATE, DELETE, TRUNCATE')
                        AND NOT pg_catalog.has_any_column_privilege(executor.oid, pg_catalog.to_regclass(@machine_room_keys), 'SELECT, INSERT, UPDATE, REFERENCES')
                        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute col
                            WHERE col.attrelid = pg_catalog.to_regclass(@machine_room_keys) AND col.attnum > 0 AND NOT col.attisdropped
                                AND (col.attname <> ALL(@machine_room_key_columns) AND pg_catalog.has_column_privilege(owner.oid, col.attrelid, col.attnum, 'SELECT')
                                    OR col.attname <> @machine_room_key_id AND pg_catalog.has_column_privilege(owner.oid, col.attrelid, col.attnum, 'UPDATE')
                                    OR pg_catalog.has_column_privilege(owner.oid, col.attrelid, col.attnum, 'INSERT, REFERENCES')))
                        AND NOT pg_catalog.has_table_privilege(owner.oid, pg_catalog.to_regclass(@machine_room_keys), 'DELETE, TRUNCATE')
                        AND f.prosecdef AND f.provolatile = 'v' AND f.proparallel = 'u' AND f.prosrc = @machine_room_body
                        AND f.proconfig IS NOT DISTINCT FROM ARRAY['search_path=pg_catalog']::text[]
                        AND pg_catalog.has_function_privilege(current_user, f.oid, 'EXECUTE')
                        AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(coalesce(f.proacl, pg_catalog.acldefault('f', f.proowner))) permission
                            WHERE permission.grantee = 0 AND permission.privilege_type = 'EXECUTE')
                )
            )
            """;
        command.Parameters.Add(new NpgsqlParameter("machine_room_required", required));
        command.Parameters.Add(new NpgsqlParameter("machine_room_signature", sql.Function(FunctionName) + "(" + function.ArgumentTypes + ")"));
        command.Parameters.Add(new NpgsqlParameter("machine_room_executor", RuntimeRole));
        command.Parameters.Add(new NpgsqlParameter("machine_room_owner", OwnerRole));
        command.Parameters.Add(new NpgsqlParameter("machine_room_migrator", MigratorRole));
        command.Parameters.Add(new NpgsqlParameter("machine_room_forbidden", PostgresMachineMutationAuthorityContract.FrozenRoles));
        command.Parameters.Add(new NpgsqlParameter("machine_room_forbidden_functions", FrozenFunctions(context)));
        command.Parameters.Add(new NpgsqlParameter("machine_room_keys", sql.Table<ExternalApiKey>()));
        var entity = context.Model.FindEntityType(typeof(ExternalApiKey))!;
        var mapping = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        string[] columns = ["Id", "KeyId", "TenantId", "OwnerId", "ExternalApiKeyOwnerTypeId", "ExternalApiKeyStatusId", "ExpiresAt", "Scopes"];
        command.Parameters.Add(new NpgsqlParameter("machine_room_key_columns", columns.Select(name => entity.FindProperty(name)!.GetColumnName(mapping)!).ToArray()));
        command.Parameters.Add(new NpgsqlParameter("machine_room_key_id", entity.FindProperty(nameof(ExternalApiKey.Id))!.GetColumnName(mapping)!));
        command.Parameters.Add(new NpgsqlParameter("machine_room_body", function.Source));
    }

    internal static async Task RequireReadyAsync(ExploreDbContext context, CancellationToken cancellationToken)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = "SELECT true ";
        AddReadiness(command, context, required: true);
        if (await command.ExecuteScalarAsync(cancellationToken) is not true)
            throw new InvalidOperationException("machine_room_authority_not_ready");
    }
}

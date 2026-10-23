using System.Text.Json;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Explore.Persistence.Security;

internal sealed class InstanceApiKeyCapabilitySql(ExploreDbContext context)
{
    private readonly ISqlGenerationHelper _sql = context.GetService<ISqlGenerationHelper>();

    internal const string ActorArguments = "p_actor uuid, p_provider integer, p_account text";
    internal const string ActorDeclarations = "v_binding uuid; v_assignments uuid[]; v_roles integer[];";

    internal string Schema => context.GetService<IDbContextOptions>()
        .FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? context.Model.FindEntityType(typeof(ExternalApiKey))!.GetSchema() ?? "public";

    internal string Table<T>() => _sql.DelimitIdentifier(
        context.Model.FindEntityType(typeof(T))!.GetTableName()!, Schema);

    internal string Column<T>(string property)
    {
        var entity = context.Model.FindEntityType(typeof(T))!;
        return _sql.DelimitIdentifier(entity.FindProperty(property)!.GetColumnName(
            StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!);
    }

    internal string Function(string name) => _sql.DelimitIdentifier(name, Schema);

    internal string MetadataColumns => string.Join(", ", MetadataProperties.Select(property =>
        $"{Column<ExternalApiKey>(property.Name)} {property.GetRelationalTypeMapping().StoreType}"));

    internal string MetadataProjection(string alias) => string.Join(", ", MetadataProperties.Select(property =>
        property.Name switch
        {
            nameof(ExternalApiKey.SecretHash) => "''::character varying",
            nameof(ExternalApiKey.LastUsedIp) => "NULL::character varying",
            _ => $"{alias}.{Column<ExternalApiKey>(property.Name)}"
        }));

    internal string[] MetadataReadColumns => MetadataProperties
        .Where(property => property.Name is not nameof(ExternalApiKey.SecretHash) and not nameof(ExternalApiKey.LastUsedIp))
        .Select(property => property.Name).ToArray();

    private IEnumerable<IProperty> MetadataProperties =>
        context.Model.FindEntityType(typeof(ExternalApiKey))!.GetProperties().OrderBy(property => property.Name, StringComparer.Ordinal);

    internal string ActorFence => $$"""
        IF p_actor IS NULL OR p_actor = '00000000-0000-0000-0000-000000000000'::uuid
           OR (p_provider IS NULL) <> (p_account IS NULL) THEN
            RAISE EXCEPTION 'instance_key_authority_denied' USING ERRCODE = '42501';
        END IF;
        PERFORM u.{{Column<User>(nameof(User.Id))}} FROM {{Table<User>()}} AS u
        WHERE u.{{Column<User>(nameof(User.Id))}} = p_actor FOR UPDATE;
        IF NOT EXISTS (SELECT 1 FROM {{Table<User>()}} AS u
            WHERE u.{{Column<User>(nameof(User.Id))}} = p_actor AND NOT u.{{Column<User>(nameof(User.IsDeleted))}}) THEN
            RAISE EXCEPTION 'instance_key_authority_denied' USING ERRCODE = '42501';
        END IF;
        IF p_provider IS NOT NULL THEN
            SELECT l.{{Column<UserExternalLogin>(nameof(UserExternalLogin.Id))}} INTO v_binding
            FROM {{Table<UserExternalLogin>()}} AS l
            WHERE l.{{Column<UserExternalLogin>(nameof(UserExternalLogin.UserId))}} = p_actor
              AND l.{{Column<UserExternalLogin>(nameof(UserExternalLogin.AuthenticationProviderId))}} = p_provider
              AND l.{{Column<UserExternalLogin>(nameof(UserExternalLogin.ProviderKey))}} = p_account
            FOR UPDATE;
            IF v_binding IS NULL THEN
                RAISE EXCEPTION 'instance_key_authority_denied' USING ERRCODE = '42501';
            END IF;
        END IF;
        SELECT array_agg(a.{{Column<PlatformUserRole>(nameof(PlatformUserRole.Id))}}
                   ORDER BY a.{{Column<PlatformUserRole>(nameof(PlatformUserRole.Id))}}),
               array_agg(DISTINCT a.{{Column<PlatformUserRole>(nameof(PlatformUserRole.RoleId))}})
        INTO v_assignments, v_roles
        FROM {{Table<PlatformUserRole>()}} AS a
        JOIN {{Table<Role>()}} AS r ON r.{{Column<Role>(nameof(Role.Id))}} = a.{{Column<PlatformUserRole>(nameof(PlatformUserRole.RoleId))}}
        WHERE a.{{Column<PlatformUserRole>(nameof(PlatformUserRole.UserId))}} = p_actor
          AND r.{{Column<Role>(nameof(Role.RoleScopeId))}} = {{(int)RoleScopeEnum.Platform}}
          AND r.{{Column<Role>(nameof(Role.MasterCode))}} = 'platform.admin';
        PERFORM a.{{Column<PlatformUserRole>(nameof(PlatformUserRole.Id))}} FROM {{Table<PlatformUserRole>()}} AS a
        WHERE a.{{Column<PlatformUserRole>(nameof(PlatformUserRole.Id))}} = ANY(v_assignments)
        ORDER BY a.{{Column<PlatformUserRole>(nameof(PlatformUserRole.Id))}} FOR UPDATE;
        PERFORM r.{{Column<Role>(nameof(Role.Id))}} FROM {{Table<Role>()}} AS r
        WHERE r.{{Column<Role>(nameof(Role.Id))}} = ANY(v_roles)
        ORDER BY r.{{Column<Role>(nameof(Role.Id))}} FOR UPDATE;
        IF NOT EXISTS (
            SELECT 1 FROM {{Table<PlatformUserRole>()}} AS a
            JOIN {{Table<Role>()}} AS r ON r.{{Column<Role>(nameof(Role.Id))}} = a.{{Column<PlatformUserRole>(nameof(PlatformUserRole.RoleId))}}
            WHERE a.{{Column<PlatformUserRole>(nameof(PlatformUserRole.Id))}} = ANY(v_assignments)
              AND a.{{Column<PlatformUserRole>(nameof(PlatformUserRole.UserId))}} = p_actor
              AND r.{{Column<Role>(nameof(Role.Id))}} = ANY(v_roles)
              AND r.{{Column<Role>(nameof(Role.RoleScopeId))}} = {{(int)RoleScopeEnum.Platform}}
              AND r.{{Column<Role>(nameof(Role.MasterCode))}} = 'platform.admin') THEN
            RAISE EXCEPTION 'instance_key_authority_denied' USING ERRCODE = '42501';
        END IF;
        """;

    internal string Roles(string owner, string migrator, string executor) => $$"""
        DO $roles$
        DECLARE r text;
        BEGIN
            FOREACH r IN ARRAY ARRAY['{{owner}}', '{{migrator}}', '{{executor}}'] LOOP
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = r) THEN
                    EXECUTE format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', r);
                ELSE
                    EXECUTE format('ALTER ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', r);
                END IF;
            END LOOP;
            IF pg_has_role('{{executor}}', '{{owner}}', 'MEMBER')
               OR pg_has_role('{{executor}}', '{{migrator}}', 'MEMBER')
               OR pg_has_role('{{owner}}', '{{executor}}', 'MEMBER')
               OR pg_has_role('{{migrator}}', '{{executor}}', 'MEMBER') THEN
                RAISE EXCEPTION 'instance_key_executor_and_migration_authority_must_remain_separate';
            END IF;
            IF EXISTS (
                WITH RECURSIVE inherited(roleid) AS (
                    SELECT edge.roleid FROM pg_catalog.pg_auth_members AS edge
                    WHERE edge.member = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = CURRENT_USER)
                    UNION
                    SELECT edge.roleid FROM pg_catalog.pg_auth_members AS edge
                    JOIN inherited ON edge.member = inherited.roleid)
                SELECT 1 FROM inherited
                WHERE roleid = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{{executor}}')) THEN
                RAISE EXCEPTION 'instance_key_executor_and_migration_authority_must_remain_separate';
            END IF;
        END $roles$;
        GRANT {{owner}} TO {{migrator}};
        GRANT {{migrator}} TO CURRENT_USER;
        GRANT USAGE ON SCHEMA {{_sql.DelimitIdentifier(Schema)}} TO {{executor}};
        GRANT USAGE, CREATE ON SCHEMA {{_sql.DelimitIdentifier(Schema)}} TO {{owner}};
        """;

    internal string ActorGrants(string owner, string executor) =>
        Grants<User>(owner, executor, ["Id", "IsDeleted"], ["Id"])
        + Grants<UserExternalLogin>(owner, executor, ["Id", "UserId", "AuthenticationProviderId", "ProviderKey"], ["Id"])
        + Grants<PlatformUserRole>(owner, executor, ["Id", "UserId", "RoleId"], ["Id"])
        + Grants<Role>(owner, executor, ["Id", "RoleScopeId", "MasterCode"], ["Id"]);

    internal string Grants<T>(string owner, string executor, string[] select, string[] update, string[]? insert = null)
    {
        string table = Table<T>();
        string policy = owner + "_read";
        string columns = string.Join(", ", context.Model.FindEntityType(typeof(T))!.GetProperties()
            .Select(property => Column<T>(property.Name)));
        string result = $"""
            REVOKE ALL ON TABLE {table} FROM {owner}, {executor};
            REVOKE SELECT ({columns}), INSERT ({columns}), UPDATE ({columns}), REFERENCES ({columns})
                ON TABLE {table} FROM {owner}, {executor};
            GRANT SELECT ({string.Join(", ", select.Select(Column<T>))}) ON {table} TO {owner};
            DROP POLICY IF EXISTS "{policy}" ON {table};
            CREATE POLICY "{policy}" ON {table} FOR SELECT TO {owner} USING (true);

            """;
        if (update.Length > 0)
            result += $"""
                GRANT UPDATE ({string.Join(", ", update.Select(Column<T>))}) ON {table} TO {owner};
                DROP POLICY IF EXISTS "{owner}_update" ON {table};
                CREATE POLICY "{owner}_update" ON {table} FOR UPDATE TO {owner} USING (true) WITH CHECK (true);

                """;
        if (insert is { Length: > 0 })
            result += $"""
                GRANT INSERT ({string.Join(", ", insert.Select(Column<T>))}) ON {table} TO {owner};
                DROP POLICY IF EXISTS "{owner}_insert" ON {table};
                CREATE POLICY "{owner}_insert" ON {table} FOR INSERT TO {owner} WITH CHECK (true);

                """;
        return result;
    }

    internal string Define(string name, string arguments, string returns, string body, string owner, string executor)
    {
        string function = Function(name);
        string types = string.Join(", ", arguments.Split(',').Select(argument =>
            argument.Trim()[(argument.Trim().IndexOf(' ') + 1)..]));
        return $$"""
            DO $define$
            DECLARE previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{{owner}}', true);
                EXECUTE $ddl$
                    CREATE OR REPLACE FUNCTION {{function}}({{arguments}})
                    RETURNS {{returns}}
                    LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                    AS $function${{body.TrimEnd(';')}};$function$;
                $ddl$;
                EXECUTE $acl$
                    REVOKE ALL ON FUNCTION {{function}}({{types}}) FROM PUBLIC, {{executor}};
                    GRANT EXECUTE ON FUNCTION {{function}}({{types}}) TO {{executor}};
                $acl$;
                PERFORM set_config('role', previous_role, true);
            END $define$;

            """;
    }

    internal string Finish(string owner) =>
        $"REVOKE CREATE ON SCHEMA {_sql.DelimitIdentifier(Schema)} FROM {owner};";

    internal async Task RequireReadyAsync(
        string executor, IReadOnlyList<InstanceApiKeySqlFunction> functions, CancellationToken cancellationToken,
        PlatformApiKeySourceEnrollment? sources = null)
    {
        if (!context.Database.IsNpgsql())
            return;
        if (executor != PostgresInstanceApiKeyIssuanceContract.ExecutorRole)
        {
            if (sources is null)
                throw new InvalidOperationException("platform_key_source_enrollment_required");
            await context.Database.ExecuteSqlRawAsync(
                PostgresPlatformApiKeySourceContract.AdmissionSql(context, sources), cancellationToken);
            functions = [.. functions, .. PostgresPlatformApiKeySourceContract.Functions(context, sources, executor)];
        }
        string expected = JsonSerializer.Serialize(functions.Select(function => new
        {
            Signature = Function(function.Name) + "(" + function.ArgumentTypes + ")",
            function.Source,
            function.Owner
        }));
        string[] forbiddenSignatures =
        [
            .. PostgresInstanceApiKeyManagementContract.Functions(context)
                .Concat(PostgresInstanceApiKeyIssuanceContract.Functions(context))
                .Append(PostgresPlatformApiKeyUsageReportContract.Function(context))
                .Where(function => function.Executor != executor)
                .Select(function => Function(function.Name) + "(" + function.ArgumentTypes + ")"),
            PostgresExternalApiKeyAuthenticationContract.FunctionSql(context) + "(text)",
            PostgresExternalApiKeyAuthenticationContract.UsageFunctionSql(context)
                + "(uuid, timestamp with time zone, text, timestamp with time zone)",
            PostgresAdmissionScannerAuthenticationContract.FunctionSql(context) + "(integer[], text[])"
        ];
        string[] privateTables =
        [
            Table<User>(), Table<UserExternalLogin>(), Table<PlatformUserRole>(),
            Table<ExternalApiKey>(), Table<ExternalApiKeyIssuanceReceipt>(), Table<ExternalApiKeyQuota>(),
            Table<AdmissionScannerCapability>()
        ];
        const string query = """
            WITH RECURSIVE inherited_roles(roleid) AS (
                SELECT membership.roleid FROM pg_catalog.pg_auth_members AS membership
                WHERE membership.member = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = CURRENT_USER)
                UNION
                SELECT membership.roleid FROM pg_catalog.pg_auth_members AS membership
                JOIN inherited_roles AS inherited ON membership.member = inherited.roleid)
            SELECT pg_catalog.pg_has_role(current_user, @executor, 'USAGE')
              AND NOT EXISTS (
                  SELECT 1 FROM inherited_roles
                  WHERE roleid <> (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = @executor))
              AND NOT EXISTS (
                  SELECT 1 FROM unnest(@forbidden) AS forbidden(signature)
                  WHERE pg_catalog.has_function_privilege(
                      current_user, pg_catalog.to_regprocedure(forbidden.signature), 'EXECUTE'))
              AND NOT EXISTS (
                  SELECT 1 FROM pg_catalog.pg_proc AS other_function
                  JOIN pg_catalog.pg_namespace AS function_schema ON function_schema.oid = other_function.pronamespace
                  WHERE function_schema.nspname = @schema AND other_function.prosecdef
                    AND pg_catalog.has_function_privilege(current_user, other_function.oid, 'EXECUTE')
                    AND NOT EXISTS (
                        SELECT 1 FROM jsonb_to_recordset(CAST(@expected AS jsonb)) AS admitted("Signature" text)
                        WHERE pg_catalog.to_regprocedure(admitted."Signature") = other_function.oid))
              AND NOT EXISTS (
                  SELECT 1 FROM unnest(@tables) AS table_name
                  WHERE pg_catalog.to_regclass(table_name) IS NULL
                    OR pg_catalog.has_table_privilege(current_user, table_name, 'SELECT, INSERT, UPDATE, DELETE, TRUNCATE')
                    OR pg_catalog.has_any_column_privilege(current_user, table_name, 'SELECT, INSERT, UPDATE, REFERENCES'))
              AND NOT EXISTS (
                  SELECT 1
                  FROM jsonb_to_recordset(CAST(@expected AS jsonb))
                      AS expected("Signature" text, "Source" text, "Owner" text)
                  LEFT JOIN pg_catalog.pg_proc AS function
                      ON function.oid = pg_catalog.to_regprocedure(expected."Signature")
                  LEFT JOIN pg_catalog.pg_roles AS owner_role ON owner_role.oid = function.proowner
                  WHERE function.oid IS NULL OR owner_role.rolname <> expected."Owner"
                    OR owner_role.rolcanlogin OR owner_role.rolsuper OR owner_role.rolbypassrls
                    OR owner_role.rolcreatedb OR owner_role.rolcreaterole OR owner_role.rolreplication
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members AS membership
                        WHERE membership.member = owner_role.oid)
                    OR NOT function.prosecdef OR function.prosrc <> expected."Source"
                    OR function.proconfig IS DISTINCT FROM ARRAY['search_path=pg_catalog']::text[]
                    OR NOT pg_catalog.has_function_privilege(current_user, function.oid, 'EXECUTE')
                    OR pg_catalog.pg_has_role(current_user, owner_role.oid, 'SET')
                    OR EXISTS (
                        SELECT 1 FROM pg_catalog.aclexplode(
                            coalesce(function.proacl, pg_catalog.acldefault('f', function.proowner))) AS permission
                        WHERE permission.grantee = 0 AND permission.privilege_type = 'EXECUTE'))
              AS "Value"
            """;
        bool ready = await context.Database.SqlQueryRaw<bool>(query,
                new NpgsqlParameter("executor", executor),
                new NpgsqlParameter("schema", Schema),
                new NpgsqlParameter("forbidden", forbiddenSignatures),
                new NpgsqlParameter("tables", privateTables),
                new NpgsqlParameter("expected", expected))
            .SingleAsync(cancellationToken);
        if (!ready)
            throw new InvalidOperationException("instance_api_key_capability_not_ready");
    }
}

internal sealed record InstanceApiKeySqlFunction(
    string Name, string Arguments, string Returns, string Body, string Owner, string Executor)
{
    internal string Source => Body.TrimEnd(';') + ";";

    internal string ArgumentTypes => string.Join(", ", Arguments.Split(',').Select(argument =>
        argument.Trim()[(argument.Trim().IndexOf(' ') + 1)..]));
}

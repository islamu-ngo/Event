namespace Explore.Persistence.Security;

/// <summary>
/// Generates read-only PostgreSQL authority predicates for trusted, deployment-owned contracts.
/// Embed the assertion before work in every SECURITY DEFINER body and at transactional install exit.
/// This guard never revokes grants or discovers its allowlist from live ACLs.
/// </summary>
public static class PostgresPrivilegeGuard
{
    /// <summary>Definer-entry restriction for ordinary callers, not migration installation sessions.</summary>
    public const string RestrictedCallerSql = """
        IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles role WHERE role.rolname = session_user
            AND role.rolcanlogin AND NOT role.rolsuper AND NOT role.rolbypassrls
            AND NOT role.rolcreatedb AND NOT role.rolcreaterole AND NOT role.rolreplication) THEN
            RAISE EXCEPTION 'native_caller_authority_invalid' USING ERRCODE = '42501';
        END IF;
        """;

    /// <summary>
    /// Checks fixed routine ACLs before replacement and after owned grants are restored.
    /// Additional per-routine executors are source-declared nested callers, never catalogue-derived.
    /// </summary>
    public static string BuildRoutineAclAssertSql(
        IReadOnlyList<string> signatures, string owner, string executor, bool requireComplete,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? additionalExecutors = null)
    {
        ArgumentNullException.ThrowIfNull(signatures);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(executor);
        if (signatures.Count == 0 || signatures.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Routine signatures must be explicit.", nameof(signatures));
        string requested = Rows(signatures.Select(signature =>
        {
            IReadOnlyList<string> extra = additionalExecutors?.GetValueOrDefault(signature) ?? [];
            return $"({Text(signature)}, ARRAY[{string.Join(", ", extra.Select(Text))}]::text[])";
        }), "NULL::text, ARRAY[]::text[]");
        return $"""
            IF (
                WITH requested(signature, additional_executors) AS ({requested}),
                resolved AS (
                    SELECT requested.signature, requested.additional_executors, routine.oid, routine.proowner, routine.proacl,
                        routine.prokind, routine.prosecdef
                    FROM requested LEFT JOIN pg_catalog.pg_proc routine
                        ON routine.oid = pg_catalog.to_regprocedure(requested.signature))
                SELECT NOT EXISTS (
                    SELECT 1 FROM resolved routine
                    WHERE (routine.oid IS NULL AND {(requireComplete ? "true" : "false")})
                        OR (routine.oid IS NOT NULL AND (
                            routine.proowner IS DISTINCT FROM pg_catalog.to_regrole({Text(owner)})::oid
                            OR routine.prokind <> 'f' OR NOT routine.prosecdef
                            OR EXISTS (
                                SELECT 1 FROM pg_catalog.aclexplode(COALESCE(routine.proacl,
                                    pg_catalog.acldefault('f', routine.proowner))) acl
                                WHERE acl.privilege_type <> 'EXECUTE'
                                    OR (acl.grantee NOT IN (
                                        routine.proowner, COALESCE(pg_catalog.to_regrole({Text(executor)})::oid, 0::oid))
                                        AND NOT EXISTS (
                                            SELECT 1 FROM unnest(routine.additional_executors) expected(role_name)
                                            WHERE acl.grantee = pg_catalog.to_regrole(expected.role_name)::oid))
                                    OR acl.grantee = 0
                                    OR (acl.grantee <> routine.proowner AND acl.is_grantable))
                            OR ({(requireComplete ? "true" : "false")} AND (
                                pg_catalog.to_regrole({Text(executor)}) IS NULL
                                OR NOT pg_catalog.has_function_privilege(
                                    pg_catalog.to_regrole({Text(executor)})::oid, routine.oid, 'EXECUTE'))))))
            ) IS NOT TRUE THEN
                RAISE EXCEPTION 'native_routine_acl_drift' USING ERRCODE = '55000';
            END IF;
            """;
    }

    private static readonly string[] TablePrivileges =
        ["SELECT", "INSERT", "UPDATE", "DELETE", "TRUNCATE", "REFERENCES", "TRIGGER", "MAINTAIN"];
    private static readonly string[] ColumnPrivileges = ["SELECT", "INSERT", "UPDATE", "REFERENCES"];

    /// <summary>Checks the PostgreSQL17 table capability without passing its name to older engines.</summary>
    internal static string MaintainPrivilegeSql(string role, string relation, bool withGrantOption) => $"""
        CASE WHEN pg_catalog.current_setting('server_version_num')::integer >= 170000
            THEN pg_catalog.has_table_privilege({role}, {relation}, 'MAINTAIN{(withGrantOption ? " WITH GRANT OPTION" : "")}')
            ELSE false END
        """;

    /// <param name="GuardedRoles">Dedicated NOLOGIN, NOINHERIT owner, executor and migrator roles.</param>
    /// <param name="Memberships">
    /// Complete allowed incoming and outgoing edges touching guarded roles, including trusted deployment
    /// login enrollment. Never populate this list by copying pg_auth_members.
    /// </param>
    public sealed record Contract(
        string Schema,
        IReadOnlyList<string> GuardedRoles,
        IReadOnlyList<Routine> Routines,
        IReadOnlyList<Relation> Relations,
        IReadOnlyList<Membership> Memberships);

    /// <param name="Name">Unquoted routine name in the contract schema.</param>
    /// <param name="IdentityArguments">
    /// Trusted PostgreSQL input type names, schema-qualified for non-catalog types; empty for no arguments.
    /// This is passed to to_regprocedure as a text literal, never executed as SQL.
    /// </param>
    public sealed record Routine(string Name, string IdentityArguments, string Owner, IReadOnlyList<string> Executors);

    /// <summary>
    /// Exact effective privileges for one guarded role and one relation. Unlisted relations and columns
    /// in the contract schema have no privileges. Table privileges imply their column privileges.
    /// No guarded role may hold grant options.
    /// </summary>
    public sealed record Relation(
        string Name, string Role, IReadOnlyList<string> Privileges, IReadOnlyList<Column> Columns);

    public sealed record Column(string Name, IReadOnlyList<string> Privileges);

    /// <summary>Parent is the granted role; Member receives it. All edge options are explicit.</summary>
    public sealed record Membership(string Parent, string Member, bool Inherit, bool Set, bool Admin);

    /// <summary>
    /// PL/pgSQL statements, not a DO block. NULL also fails closed. Use only after bootstrap has created
    /// every declared role, relation, routine and membership, with temporary schema CREATE removed.
    /// </summary>
    public static string BuildAssertSql(Contract contract) => $"""
        BEGIN
            IF ({BuildReadySql(contract)}) IS NOT TRUE THEN
                RAISE EXCEPTION 'native_privilege_guard_not_ready' USING ERRCODE = '55000';
            END IF;
        EXCEPTION WHEN insufficient_privilege THEN
            RAISE EXCEPTION 'native_privilege_guard_not_ready' USING ERRCODE = '55000';
        END;
        """;

    /// <summary>
    /// Exact role graph and safe dedicated-role attributes. On reinstall, evaluate before role or
    /// membership ALTER/GRANT statements so installer normalization cannot conceal edge drift.
    /// Missing roles or missing required edges are not a successful bootstrap.
    /// </summary>
    public static string BuildRoleGraphSql(Contract contract)
    {
        Validate(contract);
        string edges = Rows(contract.Memberships.Select(edge =>
            $"({Text(edge.Parent)}, {Text(edge.Member)}, {Boolean(edge.Inherit)}, {Boolean(edge.Set)}, {Boolean(edge.Admin)})"),
            "NULL::text, NULL::text, false, false, false");
        return $"""
            COALESCE((WITH guard_roles(name) AS ({RoleRows(contract)}),
                guard_edges(parent, member, inherit_option, set_option, admin_option) AS ({edges})
            SELECT
                NOT EXISTS (SELECT 1 FROM guard_roles expected
                    LEFT JOIN pg_catalog.pg_roles actual ON actual.rolname = expected.name
                    WHERE actual.oid IS NULL OR actual.rolcanlogin OR actual.rolinherit
                        OR actual.rolsuper OR actual.rolbypassrls OR actual.rolcreatedb
                        OR actual.rolcreaterole OR actual.rolreplication)
                AND NOT EXISTS (SELECT 1 FROM guard_edges expected
                    JOIN pg_catalog.pg_roles member ON member.rolname = expected.member
                    WHERE expected.member NOT IN (SELECT guard_roles.name FROM guard_roles)
                        AND (NOT member.rolcanlogin
                            OR ((expected.inherit_option OR NOT expected.set_option)
                                AND (member.rolsuper OR member.rolbypassrls OR member.rolcreatedb
                                    OR member.rolcreaterole OR member.rolreplication))))
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members actual
                    JOIN pg_catalog.pg_roles parent ON parent.oid = actual.roleid
                    JOIN pg_catalog.pg_roles member ON member.oid = actual.member
                    WHERE (parent.rolname IN (SELECT guard_roles.name FROM guard_roles)
                        OR member.rolname IN (SELECT guard_roles.name FROM guard_roles))
                    AND NOT EXISTS (SELECT 1 FROM guard_edges expected
                        WHERE expected.parent = parent.rolname AND expected.member = member.rolname
                            AND expected.inherit_option = actual.inherit_option
                            AND expected.set_option = actual.set_option
                            AND expected.admin_option = actual.admin_option))
                AND NOT EXISTS (SELECT 1 FROM guard_edges expected
                    WHERE NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members actual
                        JOIN pg_catalog.pg_roles parent ON parent.oid = actual.roleid
                        JOIN pg_catalog.pg_roles member ON member.oid = actual.member
                        WHERE expected.parent = parent.rolname AND expected.member = member.rolname
                            AND expected.inherit_option = actual.inherit_option
                            AND expected.set_option = actual.set_option
                            AND expected.admin_option = actual.admin_option))), false)
            """;
    }

    /// <summary>
    /// Checks exact routine identities/owners/ACLs and all effective table/column privileges in the
    /// schema, including PUBLIC and inherited access. Safe for pg_catalog-only search_path.
    /// Caller/session enrollment, body fingerprints, RLS policies and business fences remain with
    /// the owning native contract.
    /// </summary>
    public static string BuildReadySql(Contract contract)
    {
        // System attributes cannot carry independent ACLs. Exact table privileges
        // and assignable user-column privileges cover the grantable surface.
        // With table authority checked separately, a column difference must occur
        // in declared or effective column grants; all other keys share table coverage.
        string graph = BuildRoleGraphSql(contract);
        string routines = Rows(contract.Routines.Select(routine =>
            $"({Text(routine.Name)}, {Text(Signature(contract.Schema, routine))}, {Text(routine.Owner)})"),
            "NULL::text, NULL::text, NULL::text");
        string executors = Rows(contract.Routines.SelectMany(routine => routine.Executors.Select(executor =>
            $"({Text(Signature(contract.Schema, routine))}, {Text(executor)})")), "NULL::text, NULL::text");
        string relations = Rows(contract.Relations.Select(relation => $"({Text(relation.Name)}, {Text(relation.Role)})"),
            "NULL::text, NULL::text");
        string tableGrants = Rows(contract.Relations.SelectMany(relation => relation.Privileges.Select(privilege =>
            $"({Text(relation.Name)}, {Text(relation.Role)}, {Text(privilege)})")), "NULL::text, NULL::text, NULL::text");
        string columns = Rows(contract.Relations.SelectMany(relation => relation.Columns.Select(column =>
            $"({Text(relation.Name)}, {Text(relation.Role)}, {Text(column.Name)})")), "NULL::text, NULL::text, NULL::text");
        string columnGrants = Rows(contract.Relations.SelectMany(relation => relation.Columns.SelectMany(column =>
            column.Privileges.Select(privilege =>
                $"({Text(relation.Name)}, {Text(relation.Role)}, {Text(column.Name)}, {Text(privilege)})"))),
            "NULL::text, NULL::text, NULL::text, NULL::text");
        return $"""
            COALESCE((WITH guard_roles(name) AS ({RoleRows(contract)}),
                guard_routines(name, signature, owner) AS ({routines}),
                guard_executors(signature, role_name) AS ({executors}),
                guard_relations(name, role_name) AS ({relations}),
                guard_table_grants(name, role_name, privilege) AS ({tableGrants}),
                guard_columns(name, role_name, column_name) AS ({columns}),
                guard_column_grants(name, role_name, column_name, privilege) AS ({columnGrants}),
                guard_schema_relations AS (
                    SELECT relation.oid, relation.relname FROM pg_catalog.pg_class relation
                    JOIN pg_catalog.pg_namespace namespace ON namespace.oid = relation.relnamespace
                    WHERE namespace.nspname = {Text(contract.Schema)} AND relation.relkind IN ('r','p','v','m','f')),
                guard_effective_roles(role_name, grantee) AS MATERIALIZED (
                    SELECT expected.name, inherited.oid FROM guard_roles expected
                    JOIN pg_catalog.pg_roles role ON role.rolname = expected.name
                    CROSS JOIN pg_catalog.pg_roles inherited
                    WHERE pg_catalog.pg_has_role(role.oid, inherited.oid, 'USAGE')
                    UNION ALL SELECT expected.name, 0::oid FROM guard_roles expected),
                guard_column_authority AS MATERIALIZED (
                    SELECT relation.relname AS name, column_info.attname AS column_name,
                        effective.role_name, acl.privilege_type AS privilege,
                        bool_or(acl.is_grantable) AS grant_option
                    FROM guard_schema_relations relation
                    JOIN pg_catalog.pg_attribute column_info ON column_info.attrelid = relation.oid
                    CROSS JOIN LATERAL pg_catalog.aclexplode(column_info.attacl) acl
                    JOIN guard_effective_roles effective ON effective.grantee = acl.grantee
                    WHERE column_info.attnum > 0 AND NOT column_info.attisdropped
                    GROUP BY relation.relname, column_info.attname, effective.role_name, acl.privilege_type)
            SELECT ({graph})
                AND pg_catalog.to_regnamespace({Text(contract.Schema)}) IS NOT NULL
                AND NOT EXISTS (SELECT 1 FROM guard_roles expected
                    JOIN pg_catalog.pg_roles role ON role.rolname = expected.name
                    WHERE pg_catalog.has_schema_privilege(role.oid,
                        pg_catalog.to_regnamespace({Text(contract.Schema)}), 'CREATE'))
                AND NOT EXISTS (SELECT 1 FROM guard_routines expected
                    LEFT JOIN pg_catalog.pg_proc routine
                        ON routine.oid = pg_catalog.to_regprocedure(expected.signature)
                    LEFT JOIN pg_catalog.pg_roles owner ON owner.oid = routine.proowner
                    WHERE routine.oid IS NULL OR owner.rolname IS DISTINCT FROM expected.owner
                        OR routine.prokind <> 'f' OR NOT routine.prosecdef
                        OR routine.proconfig IS DISTINCT FROM ARRAY['search_path=pg_catalog']::text[]
                        OR EXISTS (
                            SELECT 1 FROM pg_catalog.aclexplode(
                                COALESCE(routine.proacl, pg_catalog.acldefault('f', routine.proowner))) acl
                            WHERE acl.privilege_type <> 'EXECUTE'
                                OR (acl.is_grantable AND acl.grantee <> routine.proowner)
                                OR (acl.grantee <> routine.proowner AND NOT EXISTS (
                                    SELECT 1 FROM guard_executors executor
                                    JOIN pg_catalog.pg_roles role ON role.rolname = executor.role_name
                                    WHERE executor.signature = expected.signature AND role.oid = acl.grantee))))
                AND NOT EXISTS (SELECT 1 FROM guard_executors expected
                    LEFT JOIN pg_catalog.pg_roles role ON role.rolname = expected.role_name
                    WHERE pg_catalog.has_function_privilege(role.oid,
                        pg_catalog.to_regprocedure(expected.signature), 'EXECUTE') IS NOT TRUE)
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc routine
                    JOIN pg_catalog.pg_namespace namespace ON namespace.oid = routine.pronamespace
                    WHERE namespace.nspname = {Text(contract.Schema)}
                        AND routine.proname IN (SELECT guard_routines.name FROM guard_routines)
                        AND NOT EXISTS (SELECT 1 FROM guard_routines expected
                            WHERE routine.oid = pg_catalog.to_regprocedure(expected.signature)))
                AND NOT EXISTS (SELECT 1 FROM guard_relations expected
                    WHERE NOT EXISTS (SELECT 1 FROM guard_schema_relations relation WHERE relation.relname = expected.name))
                AND NOT EXISTS (SELECT 1 FROM guard_columns expected
                    WHERE NOT EXISTS (SELECT 1 FROM guard_schema_relations relation
                        JOIN pg_catalog.pg_attribute column_info ON column_info.attrelid = relation.oid
                        WHERE relation.relname = expected.name AND column_info.attname = expected.column_name
                            AND column_info.attnum > 0 AND NOT column_info.attisdropped))
                AND NOT EXISTS (SELECT 1 FROM guard_schema_relations relation
                    CROSS JOIN guard_roles expected JOIN pg_catalog.pg_roles role ON role.rolname = expected.name
                    CROSS JOIN (VALUES {string.Join(", ", TablePrivileges.Select(privilege => $"({Text(privilege)})"))}) permission(privilege)
                    LEFT JOIN guard_table_grants allowed ON allowed.name = relation.relname
                        AND allowed.role_name = role.rolname AND allowed.privilege = permission.privilege
                    WHERE (CASE WHEN permission.privilege = 'MAINTAIN'
                            THEN {MaintainPrivilegeSql("role.oid", "relation.oid", false)}
                            ELSE pg_catalog.has_table_privilege(role.oid, relation.oid, permission.privilege) END)
                        IS DISTINCT FROM (allowed.name IS NOT NULL)
                        OR (CASE WHEN permission.privilege = 'MAINTAIN'
                            THEN {MaintainPrivilegeSql("role.oid", "relation.oid", true)}
                            ELSE pg_catalog.has_table_privilege(role.oid, relation.oid, permission.privilege || ' WITH GRANT OPTION') END))
                AND NOT EXISTS (SELECT 1 FROM guard_column_authority actual_column
                    FULL JOIN guard_column_grants allowed_column
                        ON actual_column.name = allowed_column.name
                        AND actual_column.column_name = allowed_column.column_name
                        AND actual_column.role_name = allowed_column.role_name
                        AND actual_column.privilege = allowed_column.privilege
                    LEFT JOIN guard_table_grants allowed_table
                        ON allowed_table.name = COALESCE(actual_column.name, allowed_column.name)
                        AND allowed_table.role_name = COALESCE(actual_column.role_name, allowed_column.role_name)
                        AND allowed_table.privilege = COALESCE(actual_column.privilege, allowed_column.privilege)
                    WHERE (allowed_table.name IS NULL
                        AND (actual_column.name IS NULL) IS DISTINCT FROM (allowed_column.name IS NULL))
                        OR COALESCE(actual_column.grant_option, false))), false)
            """;
    }

    private static string RoleRows(Contract contract) =>
        Rows(contract.GuardedRoles.Select(role => $"({Text(role)})"), "NULL::text");

    private static string Rows(IEnumerable<string> rows, string emptyColumns)
    {
        string values = string.Join(", ", rows);
        return values.Length == 0 ? $"SELECT {emptyColumns} WHERE false" : $"VALUES {values}";
    }

    private static string Signature(string schema, Routine routine) =>
        $"{Identifier(schema)}.{Identifier(routine.Name)}({routine.IdentityArguments})";

    private static string Identifier(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    // Octal dollar escaping also keeps data from closing the installer's enclosing dollar-quoted body.
    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
    private static string Boolean(bool value) => value ? "true" : "false";

    private static void Validate(Contract contract)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contract.Schema);
        if (contract.GuardedRoles.Count == 0 || contract.Routines.Count == 0)
            throw new ArgumentException("A privilege guard requires dedicated roles and routines.", nameof(contract));
        foreach (string role in contract.GuardedRoles) ArgumentException.ThrowIfNullOrWhiteSpace(role);
        if (contract.GuardedRoles.Distinct(StringComparer.Ordinal).Count() != contract.GuardedRoles.Count)
            throw new ArgumentException("Guarded roles must be unique.", nameof(contract));
        if (contract.Routines.Select(routine => (routine.Name, routine.IdentityArguments)).Distinct().Count() != contract.Routines.Count
            || contract.Relations.Select(relation => (relation.Name, relation.Role)).Distinct().Count() != contract.Relations.Count
            || contract.Memberships.Select(edge => (edge.Parent, edge.Member)).Distinct().Count() != contract.Memberships.Count)
            throw new ArgumentException("Routine, relation-role and membership identities must be unique.", nameof(contract));
        foreach (Routine routine in contract.Routines)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(routine.Name);
            ArgumentNullException.ThrowIfNull(routine.IdentityArguments);
            if (!contract.GuardedRoles.Contains(routine.Owner, StringComparer.Ordinal) || routine.Executors.Count == 0
                || routine.Executors.Any(role => !contract.GuardedRoles.Contains(role, StringComparer.Ordinal)))
                throw new ArgumentException("Routine owners and executors must be declared guarded roles.", nameof(contract));
        }
        foreach (Relation relation in contract.Relations)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(relation.Name);
            if (!contract.GuardedRoles.Contains(relation.Role, StringComparer.Ordinal)
                || relation.Privileges.Any(privilege => !TablePrivileges.Contains(privilege, StringComparer.Ordinal))
                || relation.Columns.Any(column => string.IsNullOrWhiteSpace(column.Name)
                    || column.Privileges.Any(privilege => !ColumnPrivileges.Contains(privilege, StringComparer.Ordinal)))
                || relation.Columns.Select(column => column.Name).Distinct(StringComparer.Ordinal).Count() != relation.Columns.Count)
                throw new ArgumentException("Relation matrices require guarded roles and exact PostgreSQL privileges.", nameof(contract));
        }
        foreach (Membership edge in contract.Memberships)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(edge.Parent);
            ArgumentException.ThrowIfNullOrWhiteSpace(edge.Member);
            if (edge.Parent == edge.Member || (!contract.GuardedRoles.Contains(edge.Parent, StringComparer.Ordinal)
                && !contract.GuardedRoles.Contains(edge.Member, StringComparer.Ordinal)))
                throw new ArgumentException("Memberships must touch a guarded role and cannot be self edges.", nameof(contract));
        }
    }
}

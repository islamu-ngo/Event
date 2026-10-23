using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Domain.Settings;

namespace Explore.Persistence.Security;

public static partial class PostgresProviderConfigurationContract
{
    private static string KeysSql => string.Join(",", ProviderConfigurationState.Keys.Select(Text));
    private static string Instant(string value) => $"pg_catalog.to_char({value} AT TIME ZONE 'UTC','YYYY-MM-DD\"T\"HH24:MI:SS.US\"Z\"')";

    private static string Caller(PostgresNativeQueueEnrollment enrollment, bool attempt) => $"""
        {PostgresPrivilegeGuard.RestrictedCallerSql}
        IF current_user <> '{OwnerRole}' OR session_user <> {Text(enrollment.GlobalLogin)}
            OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
            OR pg_catalog.current_setting('session_replication_role') <> 'origin'
            OR NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'') IS NOT NULL
            {(attempt ? "OR pg_catalog.current_setting('transaction_isolation') <> 'serializable'" : "")}
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                WHERE (pg_catalog.pg_has_role(session_user,r.oid,'USAGE') OR pg_catalog.pg_has_role(session_user,r.oid,'SET'))
                    AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication))
        THEN RAISE EXCEPTION 'provider_configuration_caller' USING ERRCODE = '42501'; END IF;
        """;

    private static string Projection(ExploreDbContext context, Type type, string alias)
    {
        var sql = new HumanPrincipalSourceSql(context);
        return "pg_catalog.jsonb_build_object(" + string.Join(",", Properties(context, type).Select(name =>
        {
            var property = context.Model.FindEntityType(type)!.FindProperty(name)!;
            string value = $"{alias}.{sql.Column(type, name)}";
            if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?)) value = Instant(value);
            if (type == typeof(SystemSetting) && name == "AllowedValues") value += "::text";
            string field = type == typeof(SystemSetting) ? name switch
            { "SettingKey" => "Key", "SettingValueTypeId" => "ValueType", _ => name } : name;
            return $"{Text(field)},{value}";
        })) + ")";
    }

    private static string ReadBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string Rows(Type type, string predicate = "true") => $"""
            (SELECT COALESCE(pg_catalog.jsonb_agg({Projection(context, type, "r")}
                ORDER BY r.{sql.Column(type, "Id")}), '[]'::jsonb)::text
                FROM {sql.Table(type)} r WHERE {predicate})
            """;
        string bindings = $"r.{sql.Column(typeof(SecretBinding), "SettingKey")} COLLATE \"C\" IN ({string.Join(",", ProviderConfigurationState.SecretKeys.Select(Text))})"
            + $" AND r.{sql.Column(typeof(SecretBinding), "SettingScopeId")} = 1 AND r.{sql.Column(typeof(SecretBinding), "ScopeId")} IS NULL"
            + $" AND r.{sql.Column(typeof(SecretBinding), "Qualifier")} = ''";
        string tenants = $"r.{sql.Column(typeof(TenantSetting), "SettingKey")} COLLATE \"C\" IN "
            + $"({string.Join(",", VisitorAccessCapabilityResolver.AuthoritySettingKeys.Select(Text))})";
        return $"""
            DECLARE generation bigint;
            BEGIN
                {Guard(context, enrollment)}
                {Caller(enrollment, false)}
                SELECT revision INTO generation FROM {sql.Name(SourceTable)} WHERE id = 1;
                IF generation IS NULL THEN RAISE EXCEPTION 'provider_configuration_anchor_missing' USING ERRCODE = '55000'; END IF;
                RETURN pg_catalog.jsonb_build_object('Actor',p_actor,'Revision',generation,'System',
                    (SELECT pg_catalog.jsonb_object_agg(k.key,
                        CASE WHEN r.{sql.Column(typeof(SystemSetting), "Id")} IS NULL THEN 'null'::jsonb
                            ELSE {Projection(context, typeof(SystemSetting), "r")} END)
                    FROM unnest(ARRAY[{KeysSql}]::text[]) k(key) LEFT JOIN {sql.Table(typeof(SystemSetting))} r
                        ON r.{sql.Column(typeof(SystemSetting), "SettingKey")} COLLATE "C" = k.key COLLATE "C"),
                    'Bindings',{Rows(typeof(SecretBinding), bindings)},'Tenants',{Rows(typeof(TenantSetting), tenants)},
                    'Participation',{Rows(typeof(EventParticipationConfiguration))},'Events',{Rows(typeof(Explore.Domain.Event))},
                    'Bootstrap',{Rows(typeof(InstanceBootstrapState))},
                    'Users',{Rows(typeof(User), $"r.{sql.Column(typeof(User), "Id")} = p_actor")},
                    'Logins',{Rows(typeof(UserExternalLogin), $"r.{sql.Column(typeof(UserExternalLogin), "UserId")} = p_actor")});
            END;
            """;
    }

    private static string AcquireBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) => $"""
        DECLARE generation bigint;
        BEGIN
            {Guard(context, enrollment)}
            {Caller(enrollment, true)}
            UPDATE {new HumanPrincipalSourceSql(context).Name(SourceTable)} SET revision = revision
                WHERE id = 1 RETURNING revision INTO generation;
            IF generation IS NULL THEN RAISE EXCEPTION 'provider_configuration_anchor_missing' USING ERRCODE = '55000'; END IF;
            IF p_before IS DISTINCT FROM {FunctionSql(context, "read")}(p_actor)
            THEN RAISE EXCEPTION 'provider_configuration_source_changed' USING ERRCODE = '40001'; END IF;
            RETURN generation;
        END;
        """;

    private static string ChangedBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        return $"""
            BEGIN
                IF current_user <> '{OwnerRole}' OR TG_WHEN <> 'BEFORE' OR TG_LEVEL <> 'STATEMENT'
                    OR TG_OP NOT IN ('INSERT','UPDATE','DELETE','TRUNCATE')
                    OR TG_RELID NOT IN ({string.Join(",", Sources.Select(type => $"{Text(sql.Table(type))}::regclass"))})
                THEN RAISE EXCEPTION 'provider_configuration_trigger_target' USING ERRCODE = '55000'; END IF;
                {Guard(context, enrollment)}
                UPDATE {sql.Name(SourceTable)} SET revision = revision + 1 WHERE id = 1;
                IF NOT FOUND THEN RAISE EXCEPTION 'provider_configuration_anchor_missing' USING ERRCODE = '55000'; END IF;
                RETURN NULL;
            END;
            """;
    }

    private static string WriteBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, bool authentication, bool setup)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string C(string name) => sql.Column(typeof(SystemSetting), name);
        var authenticationKeys = VisitorAccessCapabilityResolver.AuthoritySettingKeys
            .Where(key => key != GovernanceSettingKeys.PublicExperience.VisitorAccessMode).ToArray();
        var allowed = authentication ? authenticationKeys : new[]
        {
            GovernanceSettingKeys.Security.AuthorizationProvider, GovernanceSettingKeys.Cerbos.GrpcEndpoint,
            GovernanceSettingKeys.Cerbos.CustomAdminEndpoint
        };
        string fields = string.Join(",", RowColumns.Select(name => Text(name switch
        { "SettingKey" => "Key", "SettingValueTypeId" => "ValueType", _ => name })));
        string specs = string.Join(",", allowed.Select((key, index) =>
        {
            var definition = SettingRegistry.Get(key)!;
            string? description = authentication ? definition.Description : index switch
            {
                0 => "Authorization provider: 'cerbos' for external PDP, 'local' for built-in RBAC",
                1 => "Cerbos PDP gRPC endpoint for authorization requests",
                _ => "Cerbos Admin API endpoint for policy package publishing"
            };
            return $"({Text(key)},{(authentication ? (int)definition.ValueType : (int)SettingValueType.String)},"
                + $"{(authentication && key is GovernanceSettingKeys.Authentication.PrimaryProviderId or GovernanceSettingKeys.Authentication.AtprotoLoginEnabled or GovernanceSettingKeys.Authentication.GoogleSsoEnabled ? "false" : "true")},"
                + $"{(description is null ? "NULL" : Text(description))},"
                + $"{(authentication ? definition.Category is null ? "NULL" : Text(definition.Category) : Text("Security"))},{(authentication ? 0 : index + 1)})";
        }));
        const string instant = "^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}([.][0-9]{1,6})?Z$";
        const string uuid7 = "^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$";
        string authenticationGroup = $"""
            IF pg_catalog.jsonb_array_length(p_effects) NOT BETWEEN 9 AND 11
                OR EXISTS (SELECT 1 FROM unnest(ARRAY[{string.Join(",", authenticationKeys.Where(key =>
                    key is not (GovernanceSettingKeys.Authentication.KeycloakAuthority or GovernanceSettingKeys.Authentication.KeycloakClientId)).Select(Text))}]::text[]) k
                    WHERE NOT EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(p_effects) e WHERE e->>'Key' = k))
            THEN RAISE EXCEPTION 'provider_configuration_group' USING ERRCODE = '42501'; END IF;
            """;
        string authorizationGroup = $"""
            IF pg_catalog.jsonb_array_length(p_effects) > 3 OR pg_catalog.jsonb_array_length(p_effects) > 0
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(p_effects) e
                    WHERE e->>'Key' = {Text(GovernanceSettingKeys.Security.AuthorizationProvider)} AND e->>'Value' IN ('"local"','"cerbos"'))
            THEN RAISE EXCEPTION 'provider_configuration_group' USING ERRCODE = '42501'; END IF;
            """;
        return $"""
            DECLARE {(setup ? "p_actor uuid := NULL;" : "")} effect jsonb; normalized jsonb; old jsonb; spec record; affected integer;
            BEGIN
                {Guard(context, enrollment)}
                {Caller(enrollment, true)}
                {(setup ? "" : "IF p_actor IS NULL OR p_actor = '00000000-0000-0000-0000-000000000000'::uuid THEN RAISE EXCEPTION 'provider_configuration_actor' USING ERRCODE = '42501'; END IF;")}
                IF p_occurred IS NULL OR NOT pg_catalog.isfinite(p_occurred) OR p_occurred < '0001-01-01T00:00:00Z'::timestamptz
                    OR p_occurred >= '10000-01-01T00:00:00Z'::timestamptz OR pg_catalog.jsonb_typeof(p_effects) IS DISTINCT FROM 'array'
                THEN RAISE EXCEPTION 'provider_configuration_input' USING ERRCODE = '22023'; END IF;
                PERFORM {FunctionSql(context, "acquire")}(p_actor,p_before);
                {(setup ? $"""
                    IF EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements((p_before->>'Bootstrap')::jsonb) b
                        WHERE (b->>'Status')::integer = {(int)InstanceBootstrapStatus.Completed})
                    THEN RAISE EXCEPTION 'provider_configuration_setup_completed' USING ERRCODE = '42501'; END IF;
                    """ : "")}
                IF (SELECT count(DISTINCT e->>'Key') FROM pg_catalog.jsonb_array_elements(p_effects) e) <> pg_catalog.jsonb_array_length(p_effects)
                THEN RAISE EXCEPTION 'provider_configuration_duplicate_key' USING ERRCODE = '22023'; END IF;
                {(authentication ? authenticationGroup : authorizationGroup)}
                FOR effect IN SELECT value FROM pg_catalog.jsonb_array_elements(p_effects) LOOP
                    IF pg_catalog.jsonb_typeof(effect) IS DISTINCT FROM 'object'
                    THEN RAISE EXCEPTION 'provider_configuration_row_shape' USING ERRCODE = '22023'; END IF;
                    IF ((SELECT count(*) FROM pg_catalog.jsonb_object_keys(effect)) = 13
                        AND effect ?& ARRAY[{fields}] AND pg_catalog.jsonb_typeof(effect->'Key') = 'string'
                        AND pg_catalog.jsonb_typeof(effect->'Id') = 'string' AND pg_catalog.jsonb_typeof(effect->'Value') = 'string'
                        AND pg_catalog.jsonb_typeof(effect->'ValueType') = 'number' AND pg_catalog.jsonb_typeof(effect->'IsLocked') = 'boolean'
                        AND pg_catalog.jsonb_typeof(effect->'DisplayOrder') = 'number'
                        AND effect->>'CreatedAt' ~ {Text(instant)} AND effect->>'UpdatedAt' ~ {Text(instant)}) IS DISTINCT FROM true
                    THEN RAISE EXCEPTION 'provider_configuration_row_shape' USING ERRCODE = '22023'; END IF;
                    SELECT * INTO spec FROM (VALUES {specs}) s(key,type,locked,description,category,ordering) WHERE s.key = effect->>'Key';
                    IF NOT FOUND THEN RAISE EXCEPTION 'provider_configuration_foreign_effect' USING ERRCODE = '42501'; END IF;
                    old := p_before->'System'->(effect->>'Key');
                    normalized := pg_catalog.jsonb_set(pg_catalog.jsonb_set(effect,ARRAY['CreatedAt'],
                        pg_catalog.to_jsonb({Instant("(effect->>'CreatedAt')::timestamptz")})),ARRAY['UpdatedAt'],
                        pg_catalog.to_jsonb({Instant("(effect->>'UpdatedAt')::timestamptz")}));
                    IF (effect->>'UpdatedAt')::timestamptz IS DISTINCT FROM p_occurred
                        OR (effect->>'UpdatedBy')::uuid IS DISTINCT FROM p_actor
                        OR spec.locked AND (effect->>'IsLocked')::boolean IS DISTINCT FROM true
                        OR (effect->>'Id')::uuid = '00000000-0000-0000-0000-000000000000'::uuid
                        OR spec.type = {(int)SettingValueType.Boolean} AND effect->>'Value' NOT IN ('true','false')
                        OR spec.type = {(int)SettingValueType.String} AND pg_catalog.jsonb_typeof((effect->>'Value')::jsonb) IS DISTINCT FROM 'string'
                        OR effect->>'Key' = {Text(GovernanceSettingKeys.Authentication.PrimaryProviderId)}
                            AND effect->>'Value' NOT IN ('{(int)AuthenticationProviderKind.Local}','{(int)AuthenticationProviderKind.Keycloak}','{(int)AuthenticationProviderKind.Atproto}')
                    THEN RAISE EXCEPTION 'provider_configuration_fixed_effect' USING ERRCODE = '42501'; END IF;
                    IF old = 'null'::jsonb THEN
                        IF effect->>'Id' !~ {Text(uuid7)}
                            OR (effect->>'CreatedAt')::timestamptz IS DISTINCT FROM p_occurred
                            OR (effect->>'CreatedBy')::uuid IS DISTINCT FROM p_actor
                        THEN RAISE EXCEPTION 'provider_configuration_creation' USING ERRCODE = '42501'; END IF;
                    ELSE
                        IF normalized->'Id' IS DISTINCT FROM old->'Id' OR normalized->'CreatedAt' IS DISTINCT FROM old->'CreatedAt'
                            OR normalized->'CreatedBy' IS DISTINCT FROM old->'CreatedBy'
                        THEN RAISE EXCEPTION 'provider_configuration_creation' USING ERRCODE = '42501'; END IF;
                    END IF;
                    IF {(authentication ? "old <> 'null'::jsonb" : "false")} THEN
                        IF normalized - 'Value' - 'IsLocked' - 'UpdatedAt' - 'UpdatedBy'
                            IS DISTINCT FROM old - 'Value' - 'IsLocked' - 'UpdatedAt' - 'UpdatedBy'
                        THEN RAISE EXCEPTION 'provider_configuration_metadata' USING ERRCODE = '42501'; END IF;
                    ELSE
                        IF (effect->>'ValueType')::integer IS DISTINCT FROM spec.type OR effect->'AllowedValues' IS DISTINCT FROM 'null'::jsonb
                            OR effect->>'Description' IS DISTINCT FROM spec.description OR effect->>'Category' IS DISTINCT FROM spec.category
                            OR (effect->>'DisplayOrder')::integer IS DISTINCT FROM spec.ordering
                        THEN RAISE EXCEPTION 'provider_configuration_metadata' USING ERRCODE = '42501'; END IF;
                    END IF;
                    IF old = 'null'::jsonb THEN
                        INSERT INTO {sql.Table(typeof(SystemSetting))} ({string.Join(",", RowColumns.Select(C))}) VALUES (
                            (effect->>'Id')::uuid,effect->>'Key',effect->>'Value',(effect->>'ValueType')::integer,(effect->>'IsLocked')::boolean,
                            (effect->>'AllowedValues')::jsonb,effect->>'Description',effect->>'Category',(effect->>'DisplayOrder')::integer,
                            (effect->>'CreatedAt')::timestamptz,(effect->>'CreatedBy')::uuid,p_occurred,p_actor);
                    ELSE
                        UPDATE {sql.Table(typeof(SystemSetting))} SET {C("Value")} = effect->>'Value',
                            {C("SettingValueTypeId")} = (effect->>'ValueType')::integer, {C("IsLocked")} = (effect->>'IsLocked')::boolean,
                            {C("AllowedValues")} = (effect->>'AllowedValues')::jsonb, {C("Description")} = effect->>'Description',
                            {C("Category")} = effect->>'Category', {C("DisplayOrder")} = (effect->>'DisplayOrder')::integer,
                            {C("UpdatedAt")} = p_occurred, {C("UpdatedBy")} = p_actor
                            WHERE {C("Id")} = (effect->>'Id')::uuid AND {C("SettingKey")} = effect->>'Key';
                    END IF;
                    GET DIAGNOSTICS affected = ROW_COUNT;
                    IF affected <> 1 THEN RAISE EXCEPTION 'provider_configuration_write_conflict' USING ERRCODE = '40001'; END IF;
                END LOOP;
            END;
            """;
    }

    private static IEnumerable<(string Name, string Signature, string Arguments, string Result, string Body)> Functions(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        yield return ("read", "uuid", "p_actor uuid", "jsonb", ReadBody(context, enrollment));
        yield return ("acquire", "uuid,jsonb", "p_actor uuid,p_before jsonb", "int8", AcquireBody(context, enrollment));
        yield return ("changed", "", "", "trigger", ChangedBody(context, enrollment));
        foreach (bool authentication in new[] { true, false })
            foreach (bool setup in new[] { false, true })
                yield return ((authentication ? "authentication_" : "authorization_") + (setup ? "setup" : "human"),
                    (setup ? "" : "uuid,") + "jsonb,jsonb,timestamp with time zone",
                    (setup ? "" : "p_actor uuid,") + "p_before jsonb,p_effects jsonb,p_occurred timestamp with time zone",
                    "void", WriteBody(context, enrollment, authentication, setup));
    }
}

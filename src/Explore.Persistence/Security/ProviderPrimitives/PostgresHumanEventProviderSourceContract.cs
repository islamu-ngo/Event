using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Exact native source input, not generated migration output. Predicate epochs are identified by
/// a real Event, form, binding, connection, message or row. There is no tenant-wide sentinel.
/// Writers reserve both OLD and NEW identities before changing any contributing source.
/// </summary>
public static partial class PostgresHumanEventProviderSourceContract
{
    public const string OwnerRole = "event_human_provider_source_owner";
    public const string MigratorRole = "event_human_provider_source_migrator";
    public const string RuntimeRole = "event_human_provider_source_runtime";
    private const string Observe = "human_event_provider_source_observe";
    private const string Acquire = "human_event_provider_source_acquire";
    private const string Validate = "human_event_provider_source_validate";
    private const string Write = "human_event_provider_source_write";
    private const string Epoch = "human_event_provider_source_epochs";
    private const string WriterTrigger = "zz_human_event_provider_source_write";
    private const string TruncateTrigger = "zz_human_event_provider_source_truncate";
    private const string ReadPolicy = "human_event_provider_source_read";
    private const string Signature = "uuid,uuid,uuid,integer,uuid,uuid,uuid,uuid,uuid,uuid,uuid";
    private const string Arguments = "p_actor uuid,p_tenant uuid,p_event uuid,p_operation integer,p_binding uuid,p_connection uuid,p_effect uuid,p_submission uuid,p_form uuid,p_version uuid,p_secret uuid";
    private const string Stamp = "human-event-provider-source:v1:";
    private static readonly string[] Roles = [OwnerRole, MigratorRole, RuntimeRole];

    public static string ObserveFunctionSql(ExploreDbContext context) => Names(context).Name(Observe);
    public static string AcquireFunctionSql(ExploreDbContext context) => Names(context).Name(Acquire);
    public static string ValidateFunctionSql(ExploreDbContext context) => Names(context).Name(Validate);
    private static HumanPrincipalSourceSql Names(ExploreDbContext context) => new(context);
    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.Database.IsNpgsql()) return;
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        command.CommandText = BuildSql(context, enrollment);
        await command.ExecuteNonQueryAsync(cancellationToken);
        if (owned is not null) await owned.CommitAsync(cancellationToken);
    }

    internal static string AdmissionSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        $"DO $admission$ BEGIN {Guard(context, enrollment)} {ExactBodies(context, enrollment)} END $admission$;";

    private static string Touch(ExploreDbContext context, string tenant, string kind, string id) => $"""
        IF ({tenant}) IS NOT NULL AND ({id}) IS NOT NULL THEN
            INSERT INTO {Names(context).Name(Epoch)} AS epoch (tenant_id,kind,source_id,revision)
                VALUES ({tenant},{Text(kind)},{id},false)
                ON CONFLICT (tenant_id,kind,source_id) DO UPDATE SET revision = NOT epoch.revision;
        END IF;
        """;

    /// <summary>Every predicate contributor maps to the same key in reader and compulsory writer.</summary>
    private static string RowReservations(ExploreDbContext context, HumanEventProviderSource source, Type type,
        string row, string tenant)
    {
        var sql = new HumanEventProviderSourceSql(context);
        string Value(string property) => $"NULLIF({row}->>{Text(property)},'')::uuid";
        var keys = new List<(string Kind, string Value)> { ($"row:{(int)source}", Value("Id")) };
        foreach (var property in sql.Properties(type))
        {
            string? kind = property.Name switch
            {
                "EventId" => "event",
                "RegistrationFormId" => "form",
                "RegistrationFormVersionId" => "version",
                "RegistrationProviderBindingId" => "binding",
                "RegistrationProviderConnectionId" or "ExternalRegistrationProviderConnectionId" => "connection",
                "RegistrationSubmissionId" => "submission",
                "IncomingWebhookMessageId" => "message",
                "ApiTokenSecretBindingId" or "WebhookSecretBindingId" => "secret",
                _ => null
            };
            if (kind is not null) keys.Add((kind, Value(property.Name)));
        }
        string? own = source switch
        {
            HumanEventProviderSource.Form => "form",
            HumanEventProviderSource.Version => "version",
            HumanEventProviderSource.Binding => "binding",
            HumanEventProviderSource.Connection => "connection",
            HumanEventProviderSource.Submission => "submission",
            HumanEventProviderSource.Effect => "effect",
            HumanEventProviderSource.Message => "message",
            HumanEventProviderSource.SecretBinding => "secret",
            _ => null
        };
        if (own is not null) keys.Add((own, Value("Id")));
        if (source is HumanEventProviderSource.Effect or HumanEventProviderSource.Message or HumanEventProviderSource.SecretBinding)
        {
            string property = source == HumanEventProviderSource.Effect ? "ProviderDecisionId"
                : source == HumanEventProviderSource.Message ? "ProviderMessageId" : "Qualifier";
            string prefix = $"pg_catalog.split_part({row}->>{Text(property)},':',1)";
            keys.Add(("binding", $"CASE WHEN {prefix} ~ '^[0-9a-fA-F]{{32}}$' THEN {prefix}::uuid ELSE NULL END"));
        }
        return string.Join("\n", keys.OrderBy(key => key.Kind, StringComparer.Ordinal)
            .Select(key => Touch(context, tenant, key.Kind, key.Value)));
    }

    private static string Caller(PostgresNativeQueueEnrollment enrollment, bool acquire) => $"""
        {PostgresPrivilegeGuard.RestrictedCallerSql}
        IF current_user <> '{OwnerRole}' OR session_user <> {Text(enrollment.RuntimeLogin)}
            OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
            OR pg_catalog.current_setting('session_replication_role') <> 'origin'
            {(acquire ? "OR pg_catalog.current_setting('transaction_isolation') <> 'serializable'" : "")}
            OR NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid IS DISTINCT FROM p_tenant
            OR p_actor IS NULL OR p_tenant IS NULL OR p_event IS NULL
            OR '00000000-0000-0000-0000-000000000000'::uuid = ANY(ARRAY[p_actor,p_tenant,p_event])
            OR p_operation IS NULL OR p_operation NOT BETWEEN 1 AND 12
            OR p_operation IN (1,2,6,7,8,9) AND p_binding IS NULL
            OR p_operation NOT IN (1,2,6,7,8,9,11) AND p_binding IS NOT NULL
            OR p_operation IN (5,6,10) AND p_connection IS NULL
            OR p_operation NOT IN (5,6,10) AND p_connection IS NOT NULL
            OR p_operation NOT IN (3,4) AND (p_effect IS NOT NULL OR p_submission IS NOT NULL)
            OR p_operation IN (5,6) AND (p_form IS NULL OR p_version IS NULL)
            OR p_operation NOT IN (5,6,10) AND p_form IS NOT NULL
            OR p_operation NOT IN (5,6) AND (p_version IS NOT NULL OR p_secret IS NOT NULL)
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                WHERE (pg_catalog.pg_has_role(session_user,r.oid,'USAGE') OR pg_catalog.pg_has_role(session_user,r.oid,'SET'))
                    AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication))
        THEN RAISE EXCEPTION 'human_event_provider_caller_mismatch' USING ERRCODE = '42501'; END IF;
        """;

    private const string SelectionFingerprint = """
        pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(pg_catalog.jsonb_build_array(
            p_actor,p_tenant,p_event,p_operation,p_binding,p_connection,p_effect,p_submission,p_form,p_version,p_secret)::text,'UTF8')),'hex')
        """;

    private static string Digest(string image) =>
        $"pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(({image})::text,'UTF8')),'hex')";

    private static string Mark(ExploreDbContext context, string kind, string id, string image = "NULL") => $"""
        UPDATE {Names(context).Name(Epoch)} SET reader_tx = pg_catalog.pg_current_xact_id(),
            reader_signature = {SelectionFingerprint}, reader_hash = {Digest(image)}
            WHERE tenant_id = p_tenant AND kind = {Text(kind)} AND source_id = {id};
        IF NOT FOUND THEN RAISE EXCEPTION 'human_event_provider_original_marker_missing' USING ERRCODE = '55000'; END IF;
        """;

    private static string Body(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, bool acquire, bool final = false)
    {
        var sql = new HumanEventProviderSourceSql(context);
        string Originals(HumanEventProviderSource source, Type type) => $"""
            EXISTS (SELECT 1 FROM {Names(context).Name(Epoch)} original
                WHERE original.tenant_id = p_tenant AND original.kind = 'row:{(int)source}'
                    AND original.source_id = {sql.C(type, "s", "Id")}
                    AND original.reader_tx = pg_catalog.pg_current_xact_id()
                    AND original.reader_signature = {SelectionFingerprint})
            """;
        string reads = string.Join("\n", HumanEventProviderSourceSql.Sources.Select(source => $"""
            SELECT COALESCE(pg_catalog.jsonb_agg(pg_catalog.to_jsonb(r)),'[]'::jsonb) INTO rows
                FROM ({HumanEventProviderSourceSql.Native(sql.Query(source.Source, source.Type, final ? Originals(source.Source, source.Type) : null))}) r;
            {(acquire && !final ? $"""
                FOR item IN SELECT value FROM pg_catalog.jsonb_array_elements(rows) LOOP
                    {RowReservations(context, source.Source, source.Type, "item", "p_tenant")}
                    {Mark(context, $"row:{(int)source.Source}", "(item->>'Id')::uuid", "item")}
                END LOOP;
                """ : "")}
            result := result || pg_catalog.jsonb_build_object('{(int)source.Source}',rows);
            """));
        string roots = string.Join("\n", new[]
        {
            ("event","p_event"), ("binding","p_binding"), ("connection","p_connection"),
            ("effect","p_effect"), ("submission","p_submission"), ("form","p_form"),
            ("version","p_version"), ("secret","p_secret")
        }.Select(key => Touch(context, "p_tenant", key.Item1, key.Item2)));
        string custody = final ? $"""
            IF NOT EXISTS (SELECT 1 FROM {Names(context).Name(Epoch)} original WHERE original.tenant_id = p_tenant
                AND original.kind = 'event' AND original.source_id = p_event
                AND original.reader_tx = pg_catalog.pg_current_xact_id()
                AND original.reader_signature = {SelectionFingerprint})
            THEN RAISE EXCEPTION 'human_event_provider_original_selection_changed' USING ERRCODE = '55000'; END IF;
            IF EXISTS (SELECT 1 FROM {Names(context).Name(Epoch)} written
                WHERE written.writer_tx = pg_catalog.pg_current_xact_id() AND written.before_hash IS NOT NULL
                    AND NOT COALESCE(written.reader_tx = pg_catalog.pg_current_xact_id()
                        AND written.reader_signature = {SelectionFingerprint}
                        AND written.reader_hash = written.before_hash,false))
            THEN RAISE EXCEPTION 'human_event_provider_unobserved_write' USING ERRCODE = '55000'; END IF;
            """ : acquire ? $"""
            IF EXISTS (SELECT 1 FROM {Names(context).Name(Epoch)} original
                WHERE original.reader_tx = pg_catalog.pg_current_xact_id() OR original.writer_tx = pg_catalog.pg_current_xact_id())
            THEN RAISE EXCEPTION 'human_event_provider_original_already_acquired' USING ERRCODE = '55000'; END IF;
            {roots}
            {Mark(context, "event", "p_event")}
            """ : "";
        return $"""
            DECLARE result jsonb := pg_catalog.jsonb_build_object(); rows jsonb; item jsonb;
            BEGIN
                {Guard(context, enrollment)}
                {Caller(enrollment, acquire)}
                {custody}
                {reads}
                {(final ? $"""
                    IF EXISTS (SELECT 1 FROM {Names(context).Name(Epoch)} written
                        WHERE written.writer_tx = pg_catalog.pg_current_xact_id() AND written.after_hash IS NOT NULL
                            AND (written.tenant_id <> p_tenant OR NOT EXISTS (
                                SELECT 1 FROM pg_catalog.jsonb_array_elements(result->pg_catalog.split_part(written.kind,':',2)) r
                                WHERE (r.value->>'Id')::uuid = written.source_id)))
                    THEN RAISE EXCEPTION 'human_event_provider_write_left_original_predicate' USING ERRCODE = '55000'; END IF;
                    """ : "")}
                {Caller(enrollment, acquire)}
                {Guard(context, enrollment)}
                RETURN result;
            END;
            """;
    }

    private static string WriteBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanEventProviderSourceSql(context);
        string cases = string.Join("\n", HumanEventProviderSourceSql.Sources.Select(source =>
        {
            string Image(string value) => string.Join(" || ", sql.Properties(source.Type).Chunk(20).Select(properties =>
                "pg_catalog.jsonb_build_object(" + string.Join(",", properties.Select(property =>
                    $"{Text(property.Name)},({value})->{Text(sql.Names.ColumnName(source.Type, property.Name))}")) + ")"));
            string tenantProperty = source.Source == HumanEventProviderSource.SecretBinding ? "ScopeId" : "TenantId";
            return $"""
                WHEN {Text(sql.Names.Table(source.Type))}::regclass THEN
                    item := {Image("raw")};
                    tenant := NULLIF(item->>{Text(tenantProperty)},'')::uuid;
                    {RowReservations(context, source.Source, source.Type, "item", "tenant")}
                    IF tenant IS NOT NULL THEN
                        UPDATE {Names(context).Name(Epoch)} SET
                            before_hash = CASE WHEN writer_tx = pg_catalog.pg_current_xact_id() THEN before_hash
                                ELSE CASE WHEN TG_OP = 'INSERT' THEN NULL ELSE {Digest(Image("pg_catalog.to_jsonb(OLD)"))} END END,
                            after_hash = CASE WHEN TG_OP = 'DELETE' THEN NULL ELSE {Digest(Image("pg_catalog.to_jsonb(NEW)"))} END,
                            writer_tx = pg_catalog.pg_current_xact_id()
                            WHERE tenant_id = tenant AND kind = 'row:{(int)source.Source}' AND source_id = (item->>'Id')::uuid;
                    END IF;
                """;
        }));
        return $"""
            DECLARE raw jsonb; item jsonb; tenant uuid; images jsonb;
            BEGIN
                IF TG_OP = 'TRUNCATE' THEN
                    RAISE EXCEPTION 'human_event_provider_truncate_forbidden' USING ERRCODE = '42501';
                END IF;
                IF TG_WHEN <> 'BEFORE' OR TG_LEVEL <> 'ROW' OR TG_OP NOT IN ('INSERT','UPDATE','DELETE') THEN
                    RAISE EXCEPTION 'human_event_provider_trigger_target' USING ERRCODE = '55000';
                END IF;
                {Guard(context, enrollment)}
                images := CASE TG_OP WHEN 'INSERT' THEN pg_catalog.jsonb_build_array(pg_catalog.to_jsonb(NEW))
                    WHEN 'DELETE' THEN pg_catalog.jsonb_build_array(pg_catalog.to_jsonb(OLD))
                    ELSE pg_catalog.jsonb_build_array(pg_catalog.to_jsonb(OLD),pg_catalog.to_jsonb(NEW)) END;
                FOR raw IN SELECT value FROM pg_catalog.jsonb_array_elements(images) LOOP
                    CASE TG_RELID
                        {cases}
                        ELSE RAISE EXCEPTION 'human_event_provider_trigger_relation' USING ERRCODE = '55000';
                    END CASE;
                END LOOP;
                IF TG_OP = 'DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
            END;
            """;
    }

    private static (string Name, string Signature, string Arguments, string Result, string Body)[] Functions(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
    [
        (Observe, Signature, Arguments, "jsonb", Body(context, enrollment, false)),
        (Acquire, Signature, Arguments, "jsonb", Body(context, enrollment, true)),
        (Validate, Signature, Arguments, "jsonb", Body(context, enrollment, true, true)),
        (Write, "", "", "trigger", WriteBody(context, enrollment))
    ];

    private static string ExactBodies(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        string.Join("\n", Functions(context, enrollment).Select(function => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                WHERE f.oid = pg_catalog.to_regprocedure({Text(Names(context).Name(function.Name) + "(" + function.Signature + ")")})
                    AND pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex') = {Text(Hash(function.Body))}
                    AND pg_catalog.obj_description(f.oid,'pg_proc') = {Text(Stamp + Hash(function.Body))})
            THEN RAISE EXCEPTION 'human_event_provider_body_drift' USING ERRCODE = '55000'; END IF;
            """));

    private static PostgresPrivilegeGuard.Contract Privileges(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        var sql = new HumanEventProviderSourceSql(context);
        return new(sql.Names.Schema, Roles,
            [new(Observe, Signature, OwnerRole, [RuntimeRole]), new(Acquire, Signature, OwnerRole, [RuntimeRole]),
                new(Validate, Signature, OwnerRole, [RuntimeRole]),
                new(Write, "", OwnerRole, [OwnerRole])],
            [.. HumanEventProviderSourceSql.Sources.Select(source => new PostgresPrivilegeGuard.Relation(
                context.Model.FindEntityType(source.Type)!.GetTableName()!, OwnerRole, [],
                sql.Properties(source.Type).Select(property => new PostgresPrivilegeGuard.Column(
                    sql.Names.ColumnName(source.Type, property.Name), ["SELECT"])).ToArray())),
                new(Epoch, OwnerRole, [], [new("tenant_id", ["SELECT", "INSERT"]),
                    new("kind", ["SELECT", "INSERT"]), new("source_id", ["SELECT", "INSERT"]),
                    new("revision", ["SELECT", "INSERT", "UPDATE"]),
                    new("reader_tx", ["SELECT", "UPDATE"]), new("reader_signature", ["SELECT", "UPDATE"]),
                    new("reader_hash", ["SELECT", "UPDATE"]), new("writer_tx", ["SELECT", "UPDATE"]),
                    new("before_hash", ["SELECT", "UPDATE"]), new("after_hash", ["SELECT", "UPDATE"])])],
            [new(OwnerRole, MigratorRole, false, true, false), new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(RuntimeRole, enrollment.RuntimeLogin, true, false, false)]);
    }
}

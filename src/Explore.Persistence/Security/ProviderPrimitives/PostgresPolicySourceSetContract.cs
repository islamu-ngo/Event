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
/// An independent, execute-only source-set capability. Its nonlogin owner can update only
/// a private, non-PII singleton, never RolePermission or settings. Source triggers participate
/// regardless of repository, native import, seeder, or borrowed-transaction entry point.
/// </summary>
public static partial class PostgresPolicySourceSetContract
{
    public const string OwnerRole = "event_policy_source_set_owner";
    public const string MigratorRole = "event_policy_source_set_migrator";
    public const string RuntimeRole = "event_policy_source_set_runtime";
    public const string GlobalRole = "event_policy_source_set_global";
    private const string Sentinel = "policy_source_set";
    private const string Acquire = "policy_source_set_acquire";
    private const string Write = "policy_source_set_write";
    private const string SettingWrite = "policy_source_set_setting_write";
    private const string Stamp = "policy-source-set:v1:";
    private static readonly string[] Roles = [OwnerRole, MigratorRole, RuntimeRole, GlobalRole];
    private static readonly Type[] Sources = [typeof(RolePermission), typeof(SystemSetting), typeof(TenantSetting)];

    public static string AcquireFunctionSql(ExploreDbContext context) => Name(context, Acquire);

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.Database.IsNpgsql())
            return;
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = BuildSql(context, enrollment);
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        await command.ExecuteNonQueryAsync(cancellationToken);
        if (owned is not null)
            await owned.CommitAsync(cancellationToken);
    }

    internal static string AdmissionSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        $"DO $admission$ BEGIN {Guard(context, enrollment)} {ExactBodies(context, enrollment)} END $admission$;";

    internal static string OriginalSourceAssertionSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        Guard(context, enrollment) + "\n" + ExactBodies(context, enrollment);

    private static string Schema(ExploreDbContext context) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? context.Model.FindEntityType(typeof(SystemSetting))!.GetSchema() ?? "public";

    private static string Name(ExploreDbContext context, string name) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(name, Schema(context));

    private static string Table(ExploreDbContext context, Type type) =>
        Name(context, context.Model.FindEntityType(type)!.GetTableName()!);

    private static string Key(ExploreDbContext context, Type type)
    {
        var entity = context.Model.FindEntityType(type)!;
        string column = entity.FindProperty(nameof(SystemSetting.SettingKey))!.GetColumnName(
            StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(column);
    }

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string Touch(ExploreDbContext context) => $"""
        UPDATE {Name(context, Sentinel)} SET revision = NOT revision WHERE id = 1;
        IF NOT FOUND THEN
            RAISE EXCEPTION 'policy_source_set_anchor_missing' USING ERRCODE = '55000';
        END IF;
        """;

    private static string AcquireBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) => $"""
        BEGIN
            {Guard(context, enrollment)}
            {PostgresPrivilegeGuard.RestrictedCallerSql}
            IF current_user <> '{OwnerRole}'
                OR pg_catalog.current_setting('role') NOT IN ('none', session_user::text)
                OR pg_catalog.current_setting('transaction_isolation') <> 'serializable'
                OR NULLIF(pg_catalog.current_setting('app.current_tenant_id', true), '')::uuid IS DISTINCT FROM p_tenant
                OR NOT COALESCE((
                    p_purpose = 1 AND session_user = {Text(enrollment.RuntimeLogin)} AND p_tenant IS NOT NULL
                    OR p_purpose IN (2,3) AND session_user = {Text(enrollment.GlobalLogin)} AND p_tenant IS NULL
                ), false)
                OR p_tenant = '00000000-0000-0000-0000-000000000000'::uuid
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                    WHERE (pg_catalog.pg_has_role(session_user, r.oid, 'USAGE')
                        OR pg_catalog.pg_has_role(session_user, r.oid, 'SET'))
                        AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication))
            THEN RAISE EXCEPTION 'policy_source_set_caller_mismatch' USING ERRCODE = '42501'; END IF;
            {Touch(context)}
        END;
        """;

    private static string WriteBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) => $"""
        BEGIN
            IF TG_WHEN <> 'BEFORE' OR TG_LEVEL <> 'STATEMENT'
                OR NOT (TG_RELID = {Text(Table(context, typeof(RolePermission)))}::regclass
                    AND TG_OP IN ('INSERT','UPDATE','DELETE','TRUNCATE')
                    OR TG_RELID IN ({Text(Table(context, typeof(SystemSetting)))}::regclass,
                        {Text(Table(context, typeof(TenantSetting)))}::regclass) AND TG_OP = 'TRUNCATE')
            THEN RAISE EXCEPTION 'policy_source_set_trigger_target' USING ERRCODE = '55000'; END IF;
            {Guard(context, enrollment)}
            {Touch(context)}
            RETURN NULL;
        END;
        """;

    private static string SettingBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        string key = Key(context, typeof(SystemSetting));
        if (key != Key(context, typeof(TenantSetting)))
            throw new InvalidOperationException("policy_source_set_setting_key_mapping_changed");
        string keys = string.Join(",", HumanAuthorizationRouteFacts.Keys.Select(Text));
        return $"""
            BEGIN
                -- AFTER observes the final key even if another BEFORE trigger rewrites it.
                -- The row is still uncommitted until this transaction acquires the sentinel.
                IF TG_WHEN <> 'AFTER' OR TG_LEVEL <> 'ROW'
                    OR TG_RELID NOT IN ({Text(Table(context, typeof(SystemSetting)))}::regclass,
                        {Text(Table(context, typeof(TenantSetting)))}::regclass)
                    OR TG_OP NOT IN ('INSERT','UPDATE','DELETE')
                THEN RAISE EXCEPTION 'policy_source_set_trigger_target' USING ERRCODE = '55000'; END IF;
                IF TG_OP = 'INSERT' THEN
                    IF NEW.{key} NOT IN ({keys}) THEN RETURN NEW; END IF;
                ELSIF TG_OP = 'DELETE' THEN
                    IF OLD.{key} NOT IN ({keys}) THEN RETURN OLD; END IF;
                ELSE
                    IF OLD.{key} NOT IN ({keys}) AND NEW.{key} NOT IN ({keys}) THEN RETURN NEW; END IF;
                END IF;
                {Guard(context, enrollment)}
                {Touch(context)}
                IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                RETURN NEW;
            END;
            """;
    }

    private static (string Name, string Signature, string Arguments, string Result, string Body)[] Functions(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
    [
        (Acquire, "integer,uuid", "p_purpose integer, p_tenant uuid", "void", AcquireBody(context, enrollment)),
        (Write, "", "", "trigger", WriteBody(context, enrollment)),
        (SettingWrite, "", "", "trigger", SettingBody(context, enrollment))
    ];

    private static string ExactBodies(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        string.Join("\n", Functions(context, enrollment).Select(function => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                WHERE f.oid = pg_catalog.to_regprocedure({Text(Name(context, function.Name) + "(" + function.Signature + ")")})
                    AND pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc, 'UTF8')), 'hex') =
                        {Text(Hash(function.Body))}
                    AND pg_catalog.obj_description(f.oid, 'pg_proc') = {Text(Stamp + Hash(function.Body))})
            THEN RAISE EXCEPTION 'policy_source_set_body_drift' USING ERRCODE = '55000'; END IF;
            """));
}

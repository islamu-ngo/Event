using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Execute-only Local webhook fleet observation, enrolled only to the declared Global login.
/// The nonlogin owner reads predicate and endpoint join columns, never transport or credentials.
/// </summary>
public static class PostgresWebhookLocalDeliveryHealthContract
{
    public const string OwnerRole = "webhook_local_health_owner";
    public const string MigratorRole = "webhook_local_health_migrator";
    public const string ExecutorRole = "webhook_local_health_executor";
    public const string PolicyName = "webhook_local_health_observation";

    public static string FunctionSql(ExploreDbContext context) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            context.Model.FindEntityType(typeof(WebhookLocalTargetSnapshot))!.GetTableName()! + "_health_snapshot",
            Schema(context));

    internal static Task<WebhookLocalDeliveryHealth> ObserveAsync(
        ExploreDbContext context, DateTimeOffset observedAtUtc, CancellationToken cancellationToken) =>
        context.Database.SqlQueryRaw<WebhookLocalDeliveryHealth>(
            $$"""
            SELECT "DueLocalTargets" AS due_local_targets,
                "StaleDeliveringTargets" AS stale_delivering_targets
            FROM {{FunctionSql(context)}}({0})
            """, observedAtUtc).SingleAsync(cancellationToken);

    /// <summary>Install under migration authority after model-derived forced RLS.</summary>
    public static async Task ApplyAsync(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, CancellationToken cancellationToken = default)
    {
        enrollment.Validate();
        if (!context.Database.IsNpgsql())
            return;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync(BuildSql(context, enrollment), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public static string BuildSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        var sql = context.GetService<ISqlGenerationHelper>();
        string schemaName = Schema(context);
        string schema = sql.DelimitIdentifier(schemaName);
        string function = FunctionSql(context);
        string signature = function + "(timestamp with time zone)";
        (Type Type, string[] Properties)[] sources =
        [
            (typeof(WebhookLocalTargetSnapshot), [
                nameof(WebhookLocalTargetSnapshot.DeliveryStatusId),
                nameof(WebhookLocalTargetSnapshot.NextActionAtUtc),
                nameof(WebhookLocalTargetSnapshot.ProcessingLeaseExpiresAtUtc),
                nameof(WebhookLocalTargetSnapshot.WebhookEndpointId)]),
            (typeof(WebhookEndpoint), [nameof(WebhookEndpoint.Id), nameof(WebhookEndpoint.StatusId)])
        ];
        var relations = sources.Select(source =>
        {
            var entity = context.Model.FindEntityType(source.Type)!;
            string mappedSchema = context.GetService<IDbContextOptions>()
                .FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema ?? entity.GetSchema() ?? "public";
            if (mappedSchema != schemaName)
                throw new InvalidOperationException("webhook_local_health_sources_require_one_schema");
            return new PostgresPrivilegeGuard.Relation(entity.GetTableName()!, OwnerRole, [],
                source.Properties.Select(property => new PostgresPrivilegeGuard.Column(
                    ColumnName(context, source.Type, property), ["SELECT"])).ToArray());
        }).ToArray();
        var contract = new PostgresPrivilegeGuard.Contract(schemaName,
            [OwnerRole, MigratorRole, ExecutorRole],
            [new(context.Model.FindEntityType(typeof(WebhookLocalTargetSnapshot))!.GetTableName()! + "_health_snapshot",
                "timestamp with time zone", OwnerRole, [ExecutorRole])],
            relations,
            [
                new(OwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(ExecutorRole, enrollment.GlobalLogin, true, false, false)
            ]);
        string guard = PostgresPrivilegeGuard.BuildAssertSql(contract);
        string tables = string.Join(", ", sources.Select(source => Text(Table(context, source.Type)) + "::regclass"));
        var installation = new FleetHealthInstallationSql(
            contract, enrollment, OwnerRole, MigratorRole, ExecutorRole, PolicyName, "webhook_local_health");
        string sourceGuard = installation.SourceGuard(tables);
        string observationGuard = installation.ObservationGuard(sourceGuard, tables, signature);
        string grants = string.Join("\n", relations.Select(relation => FleetHealthInstallationSql.ReadPolicy(
            sql.DelimitIdentifier(relation.Name, schemaName),
            relation.Columns.Select(column => sql.DelimitIdentifier(column.Name)), OwnerRole, PolicyName)));
        string targets = Table(context, typeof(WebhookLocalTargetSnapshot));
        string endpoints = Table(context, typeof(WebhookEndpoint));
        string C<T>(string property) => sql.DelimitIdentifier(ColumnName(context, typeof(T), property));
        string status = C<WebhookLocalTargetSnapshot>(nameof(WebhookLocalTargetSnapshot.DeliveryStatusId));
        string due = C<WebhookLocalTargetSnapshot>(nameof(WebhookLocalTargetSnapshot.NextActionAtUtc));
        string expires = C<WebhookLocalTargetSnapshot>(nameof(WebhookLocalTargetSnapshot.ProcessingLeaseExpiresAtUtc));
        string endpointId = C<WebhookLocalTargetSnapshot>(nameof(WebhookLocalTargetSnapshot.WebhookEndpointId));
        string endpointKey = C<WebhookEndpoint>(nameof(WebhookEndpoint.Id));
        string endpointStatus = C<WebhookEndpoint>(nameof(WebhookEndpoint.StatusId));
        return $"""
            DO $enrollment$
            DECLARE v_role text;
            BEGIN
                IF session_user::text IS DISTINCT FROM {Text(enrollment.MigratorLogin)}
                    OR current_setting('role') NOT IN ('none', {Text(enrollment.MigratorLogin)})
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles
                        WHERE rolname = {Text(enrollment.MigratorLogin)} AND rolcanlogin) THEN
                    RAISE EXCEPTION 'webhook_local_health_enrollment_invalid' USING ERRCODE = '55000';
                END IF;
                {sourceGuard}
                {installation.Enroll(guard, observationGuard)}
            END $enrollment$;
            GRANT USAGE, CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {ExecutorRole};
            {grants}
            DO $install$
            DECLARE v_previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{OwnerRole}', true);
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {function}(p_observed_at timestamp with time zone)
                RETURNS TABLE ("DueLocalTargets" integer, "StaleDeliveringTargets" integer)
                LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
                AS $function$
                BEGIN
                    {guard}
                    {PostgresPrivilegeGuard.RestrictedCallerSql}
                    {observationGuard}
                    IF session_user::text IS DISTINCT FROM {Text(enrollment.GlobalLogin)}
                        OR current_setting('role') NOT IN ('none', {Text(enrollment.GlobalLogin)})
                        OR NULLIF(current_setting('app.current_tenant_id', true), '') IS NOT NULL THEN
                        RAISE EXCEPTION 'webhook_local_health_unbound_global_login_required' USING ERRCODE = '42501';
                    END IF;
                    IF p_observed_at IS NULL OR NOT isfinite(p_observed_at) THEN
                        RAISE EXCEPTION 'invalid local webhook health observation' USING ERRCODE = '22023';
                    END IF;
                    RETURN QUERY SELECT
                        (SELECT count(*)::integer FROM {targets} AS target
                            JOIN {endpoints} AS endpoint ON endpoint.{endpointKey} = target.{endpointId}
                            WHERE target.{status} IN (
                                {(int)WebhookLocalDeliveryStatus.Pending}, {(int)WebhookLocalDeliveryStatus.RetryDue})
                                AND target.{due} <= p_observed_at
                                AND endpoint.{endpointStatus} = {(int)WebhookEndpointStatus.Active}),
                        (SELECT count(*)::integer FROM {targets} AS target
                            WHERE target.{status} = {(int)WebhookLocalDeliveryStatus.Delivering}
                                AND target.{expires} IS NOT NULL AND target.{expires} <= p_observed_at);
                END $function$ $ddl$;
                EXECUTE $acl$
                    REVOKE ALL ON FUNCTION {signature} FROM PUBLIC, {ExecutorRole};
                    GRANT EXECUTE ON FUNCTION {signature} TO {ExecutorRole};
                $acl$;
                PERFORM set_config('role', v_previous_role, true);
            END $install$;
            REVOKE CREATE ON SCHEMA {schema} FROM {OwnerRole};
            DO $verify$
            BEGIN
                {guard}
                {observationGuard}
            END $verify$;
            """;
    }

    private static string ColumnName(ExploreDbContext context, Type type, string property)
    {
        var entity = context.Model.FindEntityType(type)!;
        return entity.FindProperty(property)!.GetColumnName(StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!;
    }

    private static string Table(ExploreDbContext context, Type type) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(context.Model.FindEntityType(type)!.GetTableName()!, Schema(context));

    private static string Schema(ExploreDbContext context) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? context.Model.FindEntityType(typeof(WebhookLocalTargetSnapshot))!.GetSchema() ?? "public";

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}

using Explore.Application.Contracts.Recovery;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Execute-only recovery fleet observation, enrolled only to the declared Global login.
/// The nonlogin owner has SELECT on predicate columns, never business identities or rows.
/// </summary>
public static class PostgresTicketingRecoveryHealthContract
{
    public const string OwnerRole = "ticketing_recovery_health_owner";
    public const string MigratorRole = "ticketing_recovery_health_migrator";
    public const string ExecutorRole = "ticketing_recovery_health_executor";
    public const string PolicyName = "ticketing_recovery_health_observation";

    public static string FunctionSql(ExploreDbContext context) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            context.Model.FindEntityType(typeof(TicketingRecoveryCheckpoint))!.GetTableName()! + "_health_snapshot",
            Schema(context));

    internal static Task<TicketingRecoveryAggregateHealth> ObserveAsync(
        ExploreDbContext context, CancellationToken cancellationToken) =>
        context.Database.SqlQueryRaw<TicketingRecoveryAggregateHealth>(
            $"""
            SELECT "RecoveryOnly" AS recovery_only, "Failed" AS failed,
                "PendingReissues" AS pending_reissues, "AmbiguousEffects" AS ambiguous_effects,
                "DeadLetteredEffects" AS dead_lettered_effects, "PoisonEffects" AS poison_effects,
                "OldestDueAt" AS oldest_due_at
            FROM {FunctionSql(context)}()
            """).SingleAsync(cancellationToken);

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
        string signature = function + "()";
        (Type Type, string[] Properties)[] sources =
        [
            (typeof(TicketingRecoveryCheckpoint), [nameof(TicketingRecoveryCheckpoint.Status)]),
            (typeof(TicketingRecoveryReissueIntent), [nameof(TicketingRecoveryReissueIntent.Status)]),
            (typeof(FairReturnOrchestrationEffect), [
                nameof(FairReturnOrchestrationEffect.StatusId),
                nameof(FairReturnOrchestrationEffect.LastFailureCode),
                nameof(FairReturnOrchestrationEffect.NextAttemptAt)])
        ];
        var relations = sources.Select(source =>
        {
            var entity = context.Model.FindEntityType(source.Type)!;
            string mappedSchema = context.GetService<IDbContextOptions>()
                .FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema ?? entity.GetSchema() ?? "public";
            if (mappedSchema != schemaName)
                throw new InvalidOperationException("ticketing_recovery_health_sources_require_one_schema");
            return new PostgresPrivilegeGuard.Relation(entity.GetTableName()!, OwnerRole, [],
                source.Properties.Select(property => new PostgresPrivilegeGuard.Column(
                    ColumnName(context, source.Type, property), ["SELECT"])).ToArray());
        }).ToArray();
        var contract = new PostgresPrivilegeGuard.Contract(schemaName,
            [OwnerRole, MigratorRole, ExecutorRole],
            [new(context.Model.FindEntityType(typeof(TicketingRecoveryCheckpoint))!.GetTableName()! + "_health_snapshot",
                "", OwnerRole, [ExecutorRole])],
            relations,
            [
                new(OwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(ExecutorRole, enrollment.GlobalLogin, true, false, false)
            ]);
        string guard = PostgresPrivilegeGuard.BuildAssertSql(contract);
        string tables = string.Join(", ", sources.Select(source => Text(Table(context, source.Type)) + "::regclass"));
        var installation = new FleetHealthInstallationSql(
            contract, enrollment, OwnerRole, MigratorRole, ExecutorRole, PolicyName, "ticketing_recovery_health");
        string sourceGuard = installation.SourceGuard(tables);
        string observationGuard = installation.ObservationGuard(sourceGuard, tables, signature);
        string grants = string.Join("\n", relations.Select(relation => FleetHealthInstallationSql.ReadPolicy(
            sql.DelimitIdentifier(relation.Name, schemaName),
            relation.Columns.Select(column => sql.DelimitIdentifier(column.Name)), OwnerRole, PolicyName)));
        string checkpoints = Table(context, typeof(TicketingRecoveryCheckpoint));
        string reissues = Table(context, typeof(TicketingRecoveryReissueIntent));
        string effects = Table(context, typeof(FairReturnOrchestrationEffect));
        string checkpointStatus = sql.DelimitIdentifier(ColumnName(context, typeof(TicketingRecoveryCheckpoint), nameof(TicketingRecoveryCheckpoint.Status)));
        string reissueStatus = sql.DelimitIdentifier(ColumnName(context, typeof(TicketingRecoveryReissueIntent), nameof(TicketingRecoveryReissueIntent.Status)));
        string effectStatus = sql.DelimitIdentifier(ColumnName(context, typeof(FairReturnOrchestrationEffect), nameof(FairReturnOrchestrationEffect.StatusId)));
        string failure = sql.DelimitIdentifier(ColumnName(context, typeof(FairReturnOrchestrationEffect), nameof(FairReturnOrchestrationEffect.LastFailureCode)));
        string due = sql.DelimitIdentifier(ColumnName(context, typeof(FairReturnOrchestrationEffect), nameof(FairReturnOrchestrationEffect.NextAttemptAt)));
        return $"""
            DO $enrollment$
            DECLARE v_role text;
            BEGIN
                IF session_user::text IS DISTINCT FROM {Text(enrollment.MigratorLogin)}
                    OR current_setting('role') NOT IN ('none', {Text(enrollment.MigratorLogin)})
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles
                        WHERE rolname = {Text(enrollment.MigratorLogin)} AND rolcanlogin) THEN
                    RAISE EXCEPTION 'ticketing_recovery_health_enrollment_invalid' USING ERRCODE = '55000';
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
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {function}()
                RETURNS TABLE (
                    "RecoveryOnly" integer, "Failed" integer, "PendingReissues" integer,
                    "AmbiguousEffects" integer, "DeadLetteredEffects" integer, "PoisonEffects" integer,
                    "OldestDueAt" timestamp with time zone)
                LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
                AS $function$
                BEGIN
                    {guard}
                    {PostgresPrivilegeGuard.RestrictedCallerSql}
                    {observationGuard}
                    IF session_user::text IS DISTINCT FROM {Text(enrollment.GlobalLogin)}
                        OR current_setting('role') NOT IN ('none', {Text(enrollment.GlobalLogin)})
                        OR NULLIF(current_setting('app.current_tenant_id', true), '') IS NOT NULL THEN
                        RAISE EXCEPTION 'ticketing_recovery_health_unbound_global_login_required' USING ERRCODE = '42501';
                    END IF;
                    RETURN QUERY SELECT
                        (SELECT count(*)::integer FROM {checkpoints} AS checkpoint
                            WHERE checkpoint.{checkpointStatus} = {(int)TicketingRecoveryStatus.RecoveryOnly}),
                        (SELECT count(*)::integer FROM {checkpoints} AS checkpoint
                            WHERE checkpoint.{checkpointStatus} = {(int)TicketingRecoveryStatus.Failed}),
                        (SELECT count(*)::integer FROM {reissues} AS reissue
                            WHERE reissue.{reissueStatus} = {(int)TicketingRecoveryReissueStatus.Pending}),
                        count(*) FILTER (WHERE effect.{effectStatus} = {(int)FairReturnOrchestrationEffectStatus.Unknown})::integer,
                        count(*) FILTER (WHERE effect.{effectStatus} = {(int)FairReturnOrchestrationEffectStatus.DeadLettered})::integer,
                        count(*) FILTER (WHERE left(effect.{failure}, 7) = 'POISON_')::integer,
                        min(effect.{due}) FILTER (WHERE effect.{effectStatus} IN (
                            {(int)FairReturnOrchestrationEffectStatus.Pending}, {(int)FairReturnOrchestrationEffectStatus.Unknown}))
                    FROM {effects} AS effect;
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
        ?? context.Model.FindEntityType(typeof(TicketingRecoveryCheckpoint))!.GetSchema() ?? "public";

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}

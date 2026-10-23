using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Moves only actor references for an exact source/target pair on the caller's
/// existing disclosure transaction, without granting cross-tenant table DML.
/// </summary>
public static class PostgresActorConsolidationContract
{
    public const string OwnerRole = "event_actor_consolidation_owner";
    public const string MigratorRole = "event_actor_consolidation_migrator";
    public const string RuntimeRole = "event_actor_consolidation_runtime";
    private const string PolicyName = "actor_consolidation_authority";

    public static string FunctionSql(ExploreDbContext context) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier("actor_consolidation_move", Schema(context));

    public static async Task ApplyAsync(ExploreDbContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
            return;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync(BuildSql(context), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    internal static Task<bool> MoveAsync(
        ExploreDbContext context, Guid source, Guid target, int targetType, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Actor consolidation requires its owning transaction.");
        string query = $"SELECT {FunctionSql(context)}({{0}}, {{1}}, {{2}}) AS \"Value\"";
        return context.Database.SqlQueryRaw<bool>(query, source, target, targetType).SingleAsync(cancellationToken);
    }

    public static string BuildSql(ExploreDbContext context)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        string schema = sql.DelimitIdentifier(Schema(context));
        string Table<T>() => sql.DelimitIdentifier(context.Model.FindEntityType(typeof(T))!.GetTableName()!, Schema(context));
        string Column<T>(string property)
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            return sql.DelimitIdentifier(entity.FindProperty(property)!.GetColumnName(table)!);
        }
        string actors = Table<Actor>();
        string actorId = Column<Actor>(nameof(Actor.Id));
        string actorType = Column<Actor>(nameof(Actor.ActorTypeId));
        string actorDeleted = Column<Actor>(nameof(Actor.IsDeleted));
        string actorSuspended = Column<Actor>(nameof(Actor.IsSuspended));
        string identities = Table<AtprotoIdentity>();
        string identityActor = Column<AtprotoIdentity>(nameof(AtprotoIdentity.ActorId));
        string identityDeleted = Column<AtprotoIdentity>(nameof(AtprotoIdentity.IsDeleted));
        string events = Table<Event>();
        string eventActor = Column<Event>(nameof(Event.ActorId));
        string organizer = Column<Event>(nameof(Event.OrganizerActorId));
        string eventDeleted = Column<Event>(nameof(Event.IsDeleted));
        string series = Table<EventSeries>();
        string seriesActor = Column<EventSeries>(nameof(EventSeries.ActorId));
        string seriesDeleted = Column<EventSeries>(nameof(EventSeries.IsDeleted));
        string speakers = Table<EventSessionSpeaker>();
        string speakerActor = Column<EventSessionSpeaker>(nameof(EventSessionSpeaker.ActorId));
        string speakerTenant = Column<EventSessionSpeaker>(nameof(EventSessionSpeaker.TenantId));
        string speakerSession = Column<EventSessionSpeaker>(nameof(EventSessionSpeaker.EventSessionId));
        string subscriptions = Table<ActorSubscription>();
        string subscriptionActor = Column<ActorSubscription>(nameof(ActorSubscription.TargetActorId));
        string subscriptionType = Column<ActorSubscription>(nameof(ActorSubscription.TargetActorTypeId));
        string subscriptionTenant = Column<ActorSubscription>(nameof(ActorSubscription.TenantId));
        string subscriber = Column<ActorSubscription>(nameof(ActorSubscription.SubscriberTenantUserId));
        string subscriptionDeleted = Column<ActorSubscription>(nameof(ActorSubscription.IsDeleted));
        string policies = string.Join("\n", new[] { events, series, speakers, subscriptions }.Select(table => $"""
            DROP POLICY IF EXISTS "{PolicyName}" ON {table};
            CREATE POLICY "{PolicyName}" ON {table} TO {OwnerRole} USING (true) WITH CHECK (true);
            """));
        string function = FunctionSql(context);
        return $$"""
            DO $roles$
            DECLARE role_name text;
            BEGIN
                FOREACH role_name IN ARRAY ARRAY['{{OwnerRole}}','{{MigratorRole}}','{{RuntimeRole}}'] LOOP
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = role_name) THEN
                        EXECUTE format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS', role_name);
                    END IF;
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = role_name
                        AND (rolcanlogin OR rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication OR rolbypassrls)) THEN
                        RAISE EXCEPTION 'actor_consolidation_role_invalid';
                    END IF;
                END LOOP;
                IF EXISTS (
                    WITH RECURSIVE memberships AS (
                        SELECT roleid FROM pg_catalog.pg_auth_members
                        WHERE member = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = CURRENT_USER)
                        UNION SELECT edge.roleid FROM pg_catalog.pg_auth_members edge
                        JOIN memberships inherited ON edge.member = inherited.roleid)
                    SELECT 1 FROM memberships WHERE roleid =
                        (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{{RuntimeRole}}')) THEN
                    RAISE EXCEPTION 'actor_consolidation_migration_authority_invalid';
                END IF;
            END; $roles$;
            GRANT {{OwnerRole}} TO {{MigratorRole}};
            GRANT {{MigratorRole}} TO CURRENT_USER;
            GRANT USAGE, CREATE ON SCHEMA {{schema}} TO {{OwnerRole}};
            GRANT USAGE ON SCHEMA {{schema}} TO {{RuntimeRole}};
            REVOKE ALL ON {{actors}}, {{identities}}, {{events}}, {{series}}, {{speakers}}, {{subscriptions}} FROM {{OwnerRole}};
            GRANT SELECT ({{actorId}}, {{actorType}}, {{actorDeleted}}, {{actorSuspended}}) ON {{actors}} TO {{OwnerRole}};
            GRANT SELECT ({{identityActor}}, {{identityDeleted}}), UPDATE ({{identityActor}}) ON {{identities}} TO {{OwnerRole}};
            GRANT SELECT ({{eventActor}}, {{organizer}}, {{eventDeleted}}), UPDATE ({{eventActor}}, {{organizer}}) ON {{events}} TO {{OwnerRole}};
            GRANT SELECT ({{seriesActor}}, {{seriesDeleted}}), UPDATE ({{seriesActor}}) ON {{series}} TO {{OwnerRole}};
            GRANT SELECT ({{speakerActor}}, {{speakerTenant}}, {{speakerSession}}), UPDATE ({{speakerActor}}) ON {{speakers}} TO {{OwnerRole}};
            GRANT SELECT ({{subscriptionActor}}, {{subscriptionType}}, {{subscriptionTenant}}, {{subscriber}}, {{subscriptionDeleted}}),
                UPDATE ({{subscriptionActor}}, {{subscriptionType}}) ON {{subscriptions}} TO {{OwnerRole}};
            {{policies}}
            DO $install$
            DECLARE previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{{OwnerRole}}', true);
                EXECUTE $ddl$
                CREATE OR REPLACE FUNCTION {{function}}(p_source uuid, p_target uuid, p_type integer)
                RETURNS boolean LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                AS $body$
                BEGIN
                    IF p_source IS NULL OR p_target IS NULL OR p_source = p_target
                        OR p_source = '00000000-0000-0000-0000-000000000000'::uuid
                        OR p_target = '00000000-0000-0000-0000-000000000000'::uuid THEN
                        RETURN false;
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM {{actors}} source, {{actors}} target
                        WHERE source.{{actorId}} = p_source AND target.{{actorId}} = p_target
                        AND source.{{actorType}} = {{(int)ActorTypeEnum.ExternalUnclassified}}
                        AND target.{{actorType}} = p_type
                        AND p_type IN ({{(int)ActorTypeEnum.Organization}}, {{(int)ActorTypeEnum.Group}})
                        AND NOT source.{{actorDeleted}} AND NOT source.{{actorSuspended}}
                        AND NOT target.{{actorDeleted}} AND NOT target.{{actorSuspended}}) THEN
                        RETURN false;
                    END IF;
                    IF EXISTS (SELECT 1 FROM {{speakers}} source JOIN {{speakers}} target
                        ON target.{{speakerTenant}} = source.{{speakerTenant}}
                        AND target.{{speakerSession}} = source.{{speakerSession}}
                        WHERE source.{{speakerActor}} = p_source AND target.{{speakerActor}} = p_target)
                        OR EXISTS (SELECT 1 FROM {{subscriptions}} source JOIN {{subscriptions}} target
                        ON target.{{subscriptionTenant}} = source.{{subscriptionTenant}}
                        AND target.{{subscriber}} = source.{{subscriber}}
                        WHERE source.{{subscriptionActor}} = p_source AND target.{{subscriptionActor}} = p_target
                        AND NOT source.{{subscriptionDeleted}} AND NOT target.{{subscriptionDeleted}}) THEN
                        RETURN false;
                    END IF;
                    UPDATE {{identities}} SET {{identityActor}} = p_target
                        WHERE {{identityActor}} = p_source AND NOT {{identityDeleted}};
                    UPDATE {{events}} SET
                        {{eventActor}} = CASE WHEN {{eventActor}} = p_source THEN p_target ELSE {{eventActor}} END,
                        {{organizer}} = CASE WHEN {{organizer}} = p_source THEN p_target ELSE {{organizer}} END
                        WHERE ({{eventActor}} = p_source OR {{organizer}} = p_source) AND NOT {{eventDeleted}};
                    UPDATE {{series}} SET {{seriesActor}} = p_target
                        WHERE {{seriesActor}} = p_source AND NOT {{seriesDeleted}};
                    UPDATE {{speakers}} SET {{speakerActor}} = p_target WHERE {{speakerActor}} = p_source;
                    UPDATE {{subscriptions}} SET {{subscriptionActor}} = p_target, {{subscriptionType}} = p_type
                        WHERE {{subscriptionActor}} = p_source AND NOT {{subscriptionDeleted}};
                    RETURN true;
                END; $body$;
                $ddl$;
                EXECUTE $acl$
                    REVOKE ALL ON FUNCTION {{function}}(uuid, uuid, integer) FROM PUBLIC, {{RuntimeRole}};
                    GRANT EXECUTE ON FUNCTION {{function}}(uuid, uuid, integer) TO {{RuntimeRole}};
                $acl$;
                PERFORM set_config('role', previous_role, true);
            END; $install$;
            REVOKE CREATE ON SCHEMA {{schema}} FROM {{OwnerRole}};
            """;
    }

    private static string Schema(ExploreDbContext context) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? context.Model.FindEntityType(typeof(Actor))!.GetSchema() ?? "public";
}

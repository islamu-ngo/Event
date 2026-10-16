using Explore.Domain;
using Explore.Domain.Federation;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>
/// Migration-owned cross-tenant disclosure operations. The executor can discover
/// dependency IDs and advance bounded epochs, but cannot read private source rows
/// or inherit the function owner's table permissions.
/// </summary>
public static partial class PostgresDiscoveryDisclosureContract
{
    public const string OwnerRole = "event_discovery_disclosure_owner";
    public const string MigratorRole = "event_discovery_disclosure_migrator";
    public const string RuntimeRole = "event_discovery_disclosure_runtime";
    public const string PolicyName = "discovery_disclosure_authority";

    public static string FunctionSql(ExploreDbContext context, string operation) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            "discovery_disclosure_" + operation, Schema(context));

    /// <summary>Install before discovery backfill or seeding, using migration authority only.</summary>
    public static async Task ApplyAsync(ExploreDbContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
            return;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync(BuildSql(context), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    internal static async Task<Guid[]> DependenciesAsync(
        ExploreDbContext context, string source, Guid[] ids, CancellationToken cancellationToken)
    {
        string query = $"SELECT * FROM {FunctionSql(context, "dependencies")}({{0}}, {{1}})";
        return await context.Database.SqlQueryRaw<Guid>(query, source, ids).ToArrayAsync(cancellationToken);
    }

    internal static Task FenceTenantsAsync(
        ExploreDbContext context, Guid[] ids, CancellationToken cancellationToken)
    {
        string query = $"SELECT {FunctionSql(context, "fence_tenants")}({{0}})";
        return context.Database.ExecuteSqlRawAsync(query, [ids], cancellationToken);
    }

    internal static Task FinalizeAsync(
        ExploreDbContext context, Guid[] tenants, IReadOnlyCollection<Guid> disclosureTenants,
        IReadOnlyDictionary<Guid, (long? Expected, long Advancement)> identities,
        CancellationToken cancellationToken)
    {
        string query = $"SELECT {FunctionSql(context, "finalize")}(@tenants, @ids, @disclosure, @expected, @advances)";
        return context.Database.ExecuteSqlRawAsync(query,
        [
            new NpgsqlParameter("tenants", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = tenants },
            new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
                { Value = tenants.Select(_ => Guid.CreateVersion7()).ToArray() },
            new NpgsqlParameter("disclosure", NpgsqlDbType.Array | NpgsqlDbType.Boolean)
                { Value = tenants.Select(disclosureTenants.Contains).ToArray() },
            new NpgsqlParameter("expected", NpgsqlDbType.Array | NpgsqlDbType.Bigint)
                { Value = tenants.Select(id => identities.GetValueOrDefault(id).Expected).ToArray() },
            new NpgsqlParameter("advances", NpgsqlDbType.Array | NpgsqlDbType.Bigint)
                { Value = tenants.Select(id => identities.GetValueOrDefault(id).Advancement).ToArray() }
        ], cancellationToken);
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
        string tenants = Table<Tenant>();
        string tenantId = Column<Tenant>(nameof(Tenant.Id));
        string revisions = Table<EventDiscoveryRevision>();
        string events = Table<Event>();
        string eventTenant = Column<Event>(nameof(Event.TenantId));
        string eventActor = Column<Event>(nameof(Event.ActorId));
        string organizer = Column<Event>(nameof(Event.OrganizerActorId));
        string submitter = Column<Event>(nameof(Event.SubmittedByUserId));
        string eventRecord = Column<Event>(nameof(Event.AtprotoRecordId));
        string presentations = Table<AtprotoRecordTenantPresentation>();
        string presentationTenant = Column<AtprotoRecordTenantPresentation>(nameof(AtprotoRecordTenantPresentation.TenantId));
        string presentationRecord = Column<AtprotoRecordTenantPresentation>(nameof(AtprotoRecordTenantPresentation.AtprotoRecordId));
        string ownership = Table<AtprotoOutboundRecordOwnership>();
        string ownershipTenant = Column<AtprotoOutboundRecordOwnership>(nameof(AtprotoOutboundRecordOwnership.TenantId));
        string ownershipRecord = Column<AtprotoOutboundRecordOwnership>(nameof(AtprotoOutboundRecordOwnership.AtprotoRecordId));
        string speakers = Table<EventSessionSpeaker>();
        string speakerTenant = Column<EventSessionSpeaker>(nameof(EventSessionSpeaker.TenantId));
        string speakerActor = Column<EventSessionSpeaker>(nameof(EventSessionSpeaker.ActorId));
        string series = Table<EventSeries>();
        string seriesTenant = Column<EventSeries>(nameof(EventSeries.TenantId));
        string seriesActor = Column<EventSeries>(nameof(EventSeries.ActorId));
        string memberships = Table<TenantUser>();
        string membershipTenant = Column<TenantUser>(nameof(TenantUser.TenantId));
        string membershipUser = Column<TenantUser>(nameof(TenantUser.UserId));
        string storage = Table<StorageObject>();
        string storageTenant = Column<StorageObject>(nameof(StorageObject.TenantId));
        string storageActor = Column<StorageObject>(nameof(StorageObject.ActorId));
        string actors = Table<Actor>();
        string actorId = Column<Actor>(nameof(Actor.Id));
        string actorUser = Column<Actor>(nameof(Actor.UserId));
        string dependencies = FunctionSql(context, "dependencies");
        string catalog = FunctionSql(context, "catalog");
        string enrollment = FunctionSql(context, "enrollment");
        string fence = FunctionSql(context, "fence_tenants");
        string finalize = FunctionSql(context, "finalize");
        string expectedFunctions = string.Join(", ", new[]
        {
            catalog + "()", enrollment + "()", dependencies + "(text,uuid[])",
            fence + "(uuid[])", finalize + "(uuid[],uuid[],boolean[],bigint[],bigint[])"
        }.Select(signature => $"('{signature.Replace("'", "''", StringComparison.Ordinal)}')"));
        string priorAcl = RoutineAcl(context, false);
        string currentAcl = RoutineAcl(context, true);
        var bodies = NestedBodies(context);
        string readPolicies = string.Join(Environment.NewLine,
            new[] { events, presentations, ownership, speakers, series, memberships, storage }
                .Select(table => $"""
                    DROP POLICY IF EXISTS "{PolicyName}" ON {table};
                    CREATE POLICY "{PolicyName}" ON {table} FOR SELECT TO {OwnerRole} USING (true);
                    """));
        return $"""
            DO $prior_acl$ BEGIN {priorAcl} END; $prior_acl$;
            DO $roles$
            DECLARE v_role text;
            BEGIN
                FOREACH v_role IN ARRAY ARRAY['{OwnerRole}', '{MigratorRole}', '{RuntimeRole}'] LOOP
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = v_role) THEN
                        EXECUTE format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', v_role);
                    END IF;
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles
                        WHERE rolname = v_role AND (rolcanlogin OR rolsuper OR rolcreatedb
                            OR rolcreaterole OR rolinherit OR rolreplication OR rolbypassrls)) THEN
                        RAISE EXCEPTION 'discovery_disclosure_role_invalid' USING ERRCODE = '42501';
                    END IF;
                END LOOP;
                IF EXISTS (
                    SELECT 1 FROM pg_catalog.pg_auth_members edge
                    JOIN pg_catalog.pg_roles member ON member.oid = edge.member
                    JOIN pg_catalog.pg_roles parent ON parent.oid = edge.roleid
                    WHERE member.rolname IN ('{OwnerRole}', '{RuntimeRole}')
                        OR member.rolname = '{MigratorRole}' AND parent.rolname <> '{OwnerRole}'
                        OR parent.rolname = '{OwnerRole}' AND member.rolname <> '{MigratorRole}'
                        OR parent.rolname = '{MigratorRole}' AND member.rolname <> CURRENT_USER
                        OR parent.rolname IN ('{OwnerRole}', '{MigratorRole}', '{RuntimeRole}')
                            AND edge.admin_option
                        OR parent.rolname = '{OwnerRole}'
                            AND (edge.inherit_option OR NOT edge.set_option)
                        OR parent.rolname = '{MigratorRole}' AND NOT edge.set_option) THEN
                    RAISE EXCEPTION 'discovery_disclosure_membership_invalid' USING ERRCODE = '42501';
                END IF;
                IF EXISTS (
                    SELECT 1 FROM pg_catalog.pg_shdepend dependency
                    JOIN pg_catalog.pg_roles role ON role.oid = dependency.refobjid
                    WHERE dependency.refclassid = 'pg_catalog.pg_authid'::regclass
                        AND role.rolname IN ('{OwnerRole}', '{MigratorRole}', '{RuntimeRole}')
                        AND dependency.deptype = 'o'
                        AND (role.rolname <> '{OwnerRole}'
                            OR dependency.classid <> 'pg_catalog.pg_proc'::regclass
                            OR dependency.dbid <> (SELECT oid FROM pg_catalog.pg_database
                                WHERE datname = pg_catalog.current_database())
                            OR NOT EXISTS (
                                SELECT 1 FROM (VALUES {expectedFunctions}) expected(signature)
                                WHERE dependency.objid = pg_catalog.to_regprocedure(expected.signature)))) THEN
                    RAISE EXCEPTION 'discovery_disclosure_ownership_invalid' USING ERRCODE = '42501';
                END IF;
                IF pg_has_role('{RuntimeRole}', '{OwnerRole}', 'MEMBER')
                   OR pg_has_role('{RuntimeRole}', '{MigratorRole}', 'MEMBER')
                   OR pg_has_role('{OwnerRole}', '{RuntimeRole}', 'MEMBER')
                   OR pg_has_role('{MigratorRole}', '{RuntimeRole}', 'MEMBER')
                   OR EXISTS (
                       WITH RECURSIVE inherited(roleid) AS (
                           SELECT roleid FROM pg_catalog.pg_auth_members
                           WHERE member = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = CURRENT_USER)
                           UNION
                           SELECT edge.roleid FROM pg_catalog.pg_auth_members edge
                           JOIN inherited parent ON edge.member = parent.roleid)
                       SELECT 1 FROM inherited
                       WHERE roleid = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{RuntimeRole}')) THEN
                    RAISE EXCEPTION 'discovery disclosure roles must remain separate' USING ERRCODE = '42501';
                END IF;
            END; $roles$;
            GRANT {OwnerRole} TO {MigratorRole} WITH INHERIT FALSE, SET TRUE;
            GRANT {MigratorRole} TO CURRENT_USER WITH INHERIT FALSE, SET TRUE;
            GRANT USAGE, CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {RuntimeRole};
            REVOKE ALL ON TABLE {tenants}, {revisions}, {events}, {presentations}, {ownership},
                {speakers}, {series}, {memberships}, {storage}, {actors} FROM {OwnerRole};
            GRANT SELECT ({tenantId}), UPDATE ({tenantId}) ON {tenants} TO {OwnerRole};
            GRANT SELECT, INSERT, UPDATE ON {revisions} TO {OwnerRole};
            GRANT SELECT ({eventTenant}, {eventActor}, {organizer}, {submitter}, {eventRecord}) ON {events} TO {OwnerRole};
            GRANT SELECT ({presentationTenant}, {presentationRecord}) ON {presentations} TO {OwnerRole};
            GRANT SELECT ({ownershipTenant}, {ownershipRecord}) ON {ownership} TO {OwnerRole};
            GRANT SELECT ({speakerTenant}, {speakerActor}) ON {speakers} TO {OwnerRole};
            GRANT SELECT ({seriesTenant}, {seriesActor}) ON {series} TO {OwnerRole};
            GRANT SELECT ({membershipTenant}, {membershipUser}) ON {memberships} TO {OwnerRole};
            GRANT SELECT ({storageTenant}, {storageActor}) ON {storage} TO {OwnerRole};
            GRANT SELECT ({actorId}, {actorUser}) ON {actors} TO {OwnerRole};
            {readPolicies}
            DROP POLICY IF EXISTS "{PolicyName}" ON {tenants};
            CREATE POLICY "{PolicyName}" ON {tenants} FOR ALL TO {OwnerRole} USING (true) WITH CHECK (true);
            DROP POLICY IF EXISTS "{PolicyName}" ON {revisions};
            CREATE POLICY "{PolicyName}" ON {revisions} FOR ALL TO {OwnerRole} USING (true) WITH CHECK (true);

            DO $bootstrap$
            DECLARE v_previous_role text := current_setting('role');
            BEGIN
            PERFORM set_config('role', '{OwnerRole}', true);
            EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {catalog}()
            RETURNS boolean LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
            AS $body$
            BEGIN
                {currentAcl}
                RETURN EXISTS (SELECT 1 FROM {tenants});
            END; $body$ $ddl$;

            EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {enrollment}()
            RETURNS uuid LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog
            AS $body$
            DECLARE v_anchor uuid;
            BEGIN
                {currentAcl}
                SELECT {tenantId} INTO v_anchor FROM {tenants} ORDER BY {tenantId} LIMIT 1;
                UPDATE {tenants} SET {tenantId} = {tenantId} WHERE {tenantId} = v_anchor;
                RETURN COALESCE(v_anchor, '00000000-0000-0000-0000-000000000000'::uuid);
            END; $body$ $ddl$;

            EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {dependencies}(p_source text, p_ids uuid[])
            RETURNS SETOF uuid LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
            AS $body${bodies["dependencies"]}$body$ $ddl$;

            EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {fence}(p_tenants uuid[])
            RETURNS void LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog
            AS $body${bodies["fence_tenants"]}$body$ $ddl$;

            EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {finalize}(
                p_tenants uuid[], p_ids uuid[], p_disclosure boolean[], p_expected bigint[], p_advances bigint[])
            RETURNS void LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog
            AS $body${bodies["finalize"]}$body$ $ddl$;
            EXECUTE $acl$
                REVOKE ALL ON FUNCTION {catalog}(), {enrollment}(), {dependencies}(text, uuid[]),
                    {fence}(uuid[]), {finalize}(uuid[], uuid[], boolean[], bigint[], bigint[]) FROM PUBLIC, {RuntimeRole};
                GRANT EXECUTE ON FUNCTION {catalog}(), {enrollment}(), {dependencies}(text, uuid[]),
                    {fence}(uuid[]), {finalize}(uuid[], uuid[], boolean[], bigint[], bigint[]) TO {RuntimeRole};
            $acl$;
            PERFORM set_config('role', v_previous_role, true);
            END; $bootstrap$;
            REVOKE CREATE ON SCHEMA {schema} FROM {OwnerRole};
            DO $current_acl$ BEGIN {currentAcl} END; $current_acl$;
            """;
    }

    private static string Schema(ExploreDbContext context) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? context.Model.FindEntityType(typeof(Tenant))!.GetSchema() ?? "public";
}

using Explore.Application.Contracts.Admissions;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>Exposes paired digest lookup only; ambiguous credentials remain visible to native rejection.</summary>
public static class PostgresAdmissionScannerAuthenticationContract
{
    public const string OwnerRole = "event_scanner_authentication_owner";
    public const string MigratorRole = "event_scanner_authentication_migrator";
    public const string GlobalRole = "event_scanner_authentication_global";

    public static string FunctionSql(ExploreDbContext context)
    {
        var entity = context.Model.FindEntityType(typeof(AdmissionScannerCapability))!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            entity.GetTableName()! + "_for_authentication", Schema(context, entity));
    }

    public static async Task ApplyAsync(ExploreDbContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
            return;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync(BuildSql(context), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public static string BuildSql(ExploreDbContext context)
    {
        var entity = context.Model.FindEntityType(typeof(AdmissionScannerCapability))!;
        var mapping = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var sql = context.GetService<ISqlGenerationHelper>();
        string schema = sql.DelimitIdentifier(Schema(context, entity));
        string table = sql.DelimitIdentifier(mapping.Name, Schema(context, entity));
        string Column(string property) => sql.DelimitIdentifier(entity.FindProperty(property)!.GetColumnName(mapping)!);
        string columns = string.Join(", ", entity.GetProperties()
            .Select(property => sql.DelimitIdentifier(property.GetColumnName(mapping)!)));
        string version = Column(nameof(AdmissionScannerCapability.LookupKeyVersion));
        string digest = Column(nameof(AdmissionScannerCapability.LookupDigest));
        string function = FunctionSql(context);
        string priorAcl = PostgresPrivilegeGuard.BuildRoutineAclAssertSql(
            [$"{function}(integer[], text[])"], OwnerRole, GlobalRole, requireComplete: false);
        string currentAcl = PostgresPrivilegeGuard.BuildRoutineAclAssertSql(
            [$"{function}(integer[], text[])"], OwnerRole, GlobalRole, requireComplete: true);
        return $"""
            DO $prior_acl$ BEGIN {priorAcl} END; $prior_acl$;
            DO $contract$
            DECLARE v_role text;
            BEGIN
                FOREACH v_role IN ARRAY ARRAY['{OwnerRole}', '{MigratorRole}', '{GlobalRole}'] LOOP
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = v_role) THEN
                        EXECUTE format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', v_role);
                    ELSE
                        EXECUTE format('ALTER ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', v_role);
                    END IF;
                END LOOP;
                IF pg_has_role('{GlobalRole}', '{OwnerRole}', 'MEMBER')
                   OR pg_has_role('{GlobalRole}', '{MigratorRole}', 'MEMBER')
                   OR pg_has_role('{OwnerRole}', '{GlobalRole}', 'MEMBER')
                   OR pg_has_role('{MigratorRole}', '{GlobalRole}', 'MEMBER') THEN
                    RAISE EXCEPTION 'scanner authentication execution and migration authority must remain separate';
                END IF;
                IF EXISTS (
                    WITH RECURSIVE inherited_roles(roleid) AS (
                        SELECT edge.roleid FROM pg_catalog.pg_auth_members AS edge
                        WHERE edge.member = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = CURRENT_USER)
                        UNION
                        SELECT edge.roleid FROM pg_catalog.pg_auth_members AS edge
                        INNER JOIN inherited_roles AS inherited ON edge.member = inherited.roleid)
                    SELECT 1 FROM inherited_roles
                    WHERE roleid = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{GlobalRole}')) THEN
                    RAISE EXCEPTION 'scanner authentication execution and migration logins must remain separate';
                END IF;
            END $contract$;
            GRANT {OwnerRole} TO {MigratorRole};
            GRANT {MigratorRole} TO CURRENT_USER;
            GRANT USAGE, CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {GlobalRole};
            REVOKE ALL ON TABLE {table} FROM {OwnerRole}, {GlobalRole};
            GRANT SELECT ({columns}) ON {table} TO {OwnerRole};
            DROP POLICY IF EXISTS scanner_authentication_read ON {table};
            CREATE POLICY scanner_authentication_read ON {table} FOR SELECT TO {OwnerRole} USING (true);
            DO $bootstrap$
            DECLARE v_previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{OwnerRole}', true);
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {function}(p_versions integer[], p_digests text[])
                RETURNS SETOF {table}
                LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
                AS $function$
                BEGIN
                    {currentAcl}
                    IF p_versions IS NULL OR p_digests IS NULL
                       OR array_ndims(p_versions) <> 1 OR array_ndims(p_digests) <> 1
                       OR cardinality(p_versions) NOT BETWEEN 1 AND {AdmissionScannerCapabilityDigestOptions.MaximumKeyVersions}
                       OR cardinality(p_versions) <> cardinality(p_digests) THEN
                        RETURN;
                    END IF;
                    IF EXISTS (
                        SELECT 1 FROM unnest(p_versions, p_digests) AS candidate(key_version, lookup_digest)
                        WHERE candidate.key_version IS NULL OR candidate.key_version <= 0
                           OR candidate.lookup_digest IS NULL OR length(candidate.lookup_digest) > 256
                           OR candidate.lookup_digest !~ '[^[:space:]]')
                       OR EXISTS (
                        SELECT 1 FROM unnest(p_versions, p_digests) AS candidate(key_version, lookup_digest)
                        GROUP BY candidate.key_version, candidate.lookup_digest HAVING count(*) > 1) THEN
                        RETURN;
                    END IF;
                    RETURN QUERY
                        SELECT capability.* FROM {table} AS capability
                        WHERE EXISTS (
                            SELECT 1 FROM unnest(p_versions, p_digests) AS candidate(key_version, lookup_digest)
                            WHERE capability.{version} = candidate.key_version
                              AND capability.{digest} = candidate.lookup_digest)
                        LIMIT 2;
                END;
                $function$ $ddl$;
                EXECUTE $acl$
                    REVOKE ALL ON FUNCTION {function}(integer[], text[]) FROM PUBLIC, {GlobalRole};
                    GRANT EXECUTE ON FUNCTION {function}(integer[], text[]) TO {GlobalRole};
                $acl$;
                PERFORM set_config('role', v_previous_role, true);
            END $bootstrap$;
            REVOKE CREATE ON SCHEMA {schema} FROM {OwnerRole};
            DO $current_acl$ BEGIN {currentAcl} END; $current_acl$;
            """;
    }

    private static string Schema(ExploreDbContext context, IEntityType entity) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? entity.GetSchema() ?? "public";
}

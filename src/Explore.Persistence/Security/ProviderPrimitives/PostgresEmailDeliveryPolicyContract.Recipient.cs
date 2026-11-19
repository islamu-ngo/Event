using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Recipient resolution authority for the email dispatch pipeline: a SECURITY DEFINER
/// function reading exactly the users/user_pii/identity-email-claim/evidence columns
/// needed to resolve a dispatch address, owned by a dedicated NOLOGIN role. The ordinary
/// runtime connector resolves recipients through the function and never needs direct
/// users/user_pii table privileges (PII stays function-mediated, like the local
/// administration and human source contracts).
///
/// Explicit authorization scope (B19): the trusted delivery component may resolve any
/// recipient's delivery details by identifier (global lookup by design); authorized
/// delivery logins receive EXECUTE only, and per-category business rules remain in
/// application code.
/// </summary>
public static partial class PostgresEmailDeliveryPolicyContract
{
    public const string RecipientOwnerRole = "event_email_recipient_owner";

    public static string RecipientFunctionSql(ExploreDbContext context) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier("email_delivery_recipient", Schema(context));

    /// <summary>
    /// Creates the recipient owner role ahead of the main role bootstrap so reinstall-time
    /// role-graph assertions (which cover the full seven-role contract) observe it. On a
    /// fresh database the email policy roles do not exist yet; the membership grant then
    /// completes idempotently in <see cref="BuildRecipientSql"/>.
    /// </summary>
    internal static string BuildRecipientRolesSql(PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        return $$"""
            DO $recipient_roles$
            BEGIN
                IF session_user <> {{LoginLiteral(enrollment.MigratorLogin)}} THEN
                    RAISE EXCEPTION 'email_recipient_migration_login_mismatch' USING ERRCODE = '42501';
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = '{{RecipientOwnerRole}}') THEN
                    EXECUTE format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', '{{RecipientOwnerRole}}');
                END IF;
                IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = '{{MigratorRole}}') THEN
                    EXECUTE 'GRANT {{RecipientOwnerRole}} TO {{MigratorRole}} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE';
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members membership
                        JOIN pg_catalog.pg_roles granted ON granted.oid = membership.roleid
                        JOIN pg_catalog.pg_roles member ON member.oid = membership.member
                        WHERE granted.rolname = '{{RecipientOwnerRole}}' AND member.rolname = session_user) THEN
                        EXECUTE format('REVOKE %I FROM %I', '{{RecipientOwnerRole}}', session_user);
                    END IF;
                END IF;
            END; $recipient_roles$;
            """;
    }

    internal static string BuildRecipientSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        var sql = context.GetService<ISqlGenerationHelper>();
        string schema = sql.DelimitIdentifier(Schema(context));
        string Table<T>() => sql.DelimitIdentifier(context.Model.FindEntityType(typeof(T))!.GetTableName()!, Schema(context));
        string Column<T>(string property)
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            return sql.DelimitIdentifier(entity.FindProperty(property)!.GetColumnName(table)!);
        }
        // PL/pgSQL RETURN QUERY performs positional attribute mapping: declared return
        // types must mirror the migrated columns exactly or execution fails (42804).
        string StoreType<T>(string property)
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            return entity.FindProperty(property)!.GetColumnType(table)
                ?? throw new InvalidOperationException($"email_recipient_store_type_missing:{property}");
        }
        string users = Table<User>();
        string pii = Table<UserPii>();
        string claims = Table<UserIdentityEmailClaim>();
        string evidence = Table<UserIdentityEmailEvidence>();
        string userId = Column<User>(nameof(User.Id));
        string isDeleted = Column<User>(nameof(User.IsDeleted));
        string piiUserId = Column<UserPii>(nameof(UserPii.UserId));
        string piiEmail = Column<UserPii>(nameof(UserPii.Email));
        string claimId = Column<UserIdentityEmailClaim>(nameof(UserIdentityEmailClaim.Id));
        string claimUserId = Column<UserIdentityEmailClaim>(nameof(UserIdentityEmailClaim.UserId));
        string normalizedEmail = Column<UserIdentityEmailClaim>(nameof(UserIdentityEmailClaim.NormalizedEmail));
        string evidenceUserId = Column<UserIdentityEmailEvidence>(nameof(UserIdentityEmailEvidence.UserId));
        string evidenceClaimId = Column<UserIdentityEmailEvidence>(nameof(UserIdentityEmailEvidence.ClaimId));
        string evidenceIsActive = Column<UserIdentityEmailEvidence>(nameof(UserIdentityEmailEvidence.IsActive));
        string function = RecipientFunctionSql(context);
        string guard = PrivilegeAssertion(context, enrollment);
        return $$"""
            GRANT {{RecipientOwnerRole}} TO {{MigratorRole}} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE;
            DO $recipient_revoke$
            BEGIN
                IF EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members membership
                    JOIN pg_catalog.pg_roles granted ON granted.oid = membership.roleid
                    JOIN pg_catalog.pg_roles member ON member.oid = membership.member
                    WHERE granted.rolname = '{{RecipientOwnerRole}}' AND member.rolname = session_user) THEN
                    EXECUTE format('REVOKE %I FROM %I', '{{RecipientOwnerRole}}', session_user);
                END IF;
            END; $recipient_revoke$;
            GRANT USAGE, CREATE ON SCHEMA {{schema}} TO {{RecipientOwnerRole}};
            REVOKE ALL ON {{users}}, {{pii}}, {{claims}}, {{evidence}} FROM {{RecipientOwnerRole}};
            GRANT SELECT ({{userId}}, {{isDeleted}}) ON {{users}} TO {{RecipientOwnerRole}};
            GRANT SELECT ({{piiUserId}}, {{piiEmail}}) ON {{pii}} TO {{RecipientOwnerRole}};
            GRANT SELECT ({{claimId}}, {{claimUserId}}, {{normalizedEmail}}) ON {{claims}} TO {{RecipientOwnerRole}};
            GRANT SELECT ({{evidenceUserId}}, {{evidenceClaimId}}, {{evidenceIsActive}}) ON {{evidence}} TO {{RecipientOwnerRole}};
            DO $recipient_install$
            DECLARE previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{{RecipientOwnerRole}}', true);
                EXECUTE $ddl$
                CREATE OR REPLACE FUNCTION {{function}}(p_recipient_id uuid)
                RETURNS TABLE (user_id {{StoreType<User>(nameof(User.Id))}}, is_deleted {{StoreType<User>(nameof(User.IsDeleted))}},
                    contact_email {{StoreType<UserPii>(nameof(UserPii.Email))}}, claim_id {{StoreType<UserIdentityEmailClaim>(nameof(UserIdentityEmailClaim.Id))}},
                    normalized_email {{StoreType<UserIdentityEmailClaim>(nameof(UserIdentityEmailClaim.NormalizedEmail))}},
                    claim_verified boolean)
                LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
                AS $body$
                BEGIN
                    {{guard}}
                    {{RestrictedCallerSql}}
                    IF session_user NOT IN ({{LoginLiteral(enrollment.RuntimeLogin)}}, {{LoginLiteral(enrollment.GlobalLogin)}},
                        {{LoginLiteral(enrollment.EmailInstanceMutationLogin)}}) THEN
                        RAISE EXCEPTION 'email_recipient_reader_login_mismatch' USING ERRCODE = '42501';
                    END IF;
                    RETURN QUERY
                    SELECT u.{{userId}}, u.{{isDeleted}}, p.{{piiEmail}}, c.{{claimId}}, c.{{normalizedEmail}},
                        EXISTS (SELECT 1 FROM {{evidence}} e
                            WHERE e.{{evidenceUserId}} = u.{{userId}}
                                AND e.{{evidenceClaimId}} = c.{{claimId}}
                                AND e.{{evidenceIsActive}})
                    FROM {{users}} u
                    LEFT JOIN {{pii}} p ON p.{{piiUserId}} = u.{{userId}}
                    LEFT JOIN {{claims}} c ON c.{{claimUserId}} = u.{{userId}}
                    WHERE u.{{userId}} = p_recipient_id;
                END;
                $body$;
                $ddl$;
                EXECUTE $acl$
                    REVOKE ALL ON FUNCTION {{function}}(uuid) FROM PUBLIC, {{RuntimeRole}}, {{GlobalRole}},
                        {{InstanceMutationRole}}, {{TenantMutationRole}};
                    GRANT EXECUTE ON FUNCTION {{function}}(uuid) TO {{RuntimeRole}}, {{GlobalRole}}, {{InstanceMutationRole}};
                $acl$;
                PERFORM set_config('role', previous_role, true);
            END; $recipient_install$;
            REVOKE CREATE ON SCHEMA {{schema}} FROM {{RecipientOwnerRole}};
            """;
    }
}

using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>Policy facts and fixed corrections, without granting executors private-table authority.</summary>
public static partial class PostgresLocationPrivacyGovernanceContract
{
    public const string OwnerRole = "event_location_governance_owner";
    public const string MigratorRole = "event_location_governance_migrator";
    public const string RuntimeRole = "event_location_governance_runtime";
    public const string GlobalRole = "event_location_governance_global";
    private const string PolicyName = "location_privacy_governance";
    internal static IReadOnlyList<string> SettingKeys { get; } = Array.AsReadOnly(new[]
    {
        GovernanceSettingKeys.LocationPrivacy.AllowHomeLocations,
        GovernanceSettingKeys.LocationPrivacy.AllowPublicExactAddress,
        GovernanceSettingKeys.LocationPrivacy.AllowPublicCoordinates,
        GovernanceSettingKeys.LocationPrivacy.MinimumHomeAudience,
        GovernanceSettingKeys.LocationPrivacy.DefaultRevealOffset
    });

    public static string OverridesFunctionSql(ExploreDbContext context) => new Names(context).Function("overrides");
    public static string ProjectionsFunctionSql(ExploreDbContext context) => new Names(context).Function("projections");
    public static string CorrectionsFunctionSql(ExploreDbContext context) => new Names(context).Function("correct");
    public static string RetainReceiptFunctionSql(ExploreDbContext context) => new Names(context).Function("retain_receipt");

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
        var names = new Names(context);
        string setting = names.Table<TenantSetting>();
        string projection = names.Table<EventLocation>();
        string location = names.Table<Location>();
        string audit = names.Table<EventLocationDisclosureAudit>();
        string outbox = names.Table<OutboxMessage>();
        string receipt = names.Table<LocationPrivacyCorrectionReceipt>();
        string key = names.Column<TenantSetting>(nameof(TenantSetting.SettingKey));
        string deleted = names.Column<EventLocation>(nameof(EventLocation.IsDeleted));
        string ownerGrants = $"""
            GRANT SELECT ({names.Columns<TenantSetting>(nameof(TenantSetting.Id), nameof(TenantSetting.TenantId),
                nameof(TenantSetting.SettingKey), nameof(TenantSetting.Value))}) ON {setting} TO {OwnerRole};
            GRANT SELECT ({names.Columns<EventLocation>(
                nameof(EventLocation.Id), nameof(EventLocation.TenantId), nameof(EventLocation.EventId),
                nameof(EventLocation.LocationId), nameof(EventLocation.IsDeleted), nameof(EventLocation.IsToBeAnnounced),
                nameof(EventLocation.ShowVenueName), nameof(EventLocation.ShowCity), nameof(EventLocation.ShowCountry),
                nameof(EventLocation.ShowRoomName), nameof(EventLocation.ShowStreetAddress), nameof(EventLocation.ShowPostcode),
                nameof(EventLocation.ShowCoordinates), nameof(EventLocation.FullDetailsAudienceId),
                nameof(EventLocation.RevealFullDetailsFromUtc), nameof(EventLocation.CreatedAt),
                nameof(EventLocation.PolicyVersion), nameof(EventLocation.ConcurrencyStamp))}) ON {projection} TO {OwnerRole};
            GRANT SELECT ({names.Columns<Location>(nameof(Location.Id), nameof(Location.TenantId),
                nameof(Location.LocationKindId))}) ON {location} TO {OwnerRole};
            GRANT UPDATE ({names.Columns<EventLocation>(nameof(EventLocation.NeedsPrivacyReview),
                nameof(EventLocation.PolicyVersion), nameof(EventLocation.LastPolicyActorUserId),
                nameof(EventLocation.LastPolicyChangedAtUtc), nameof(EventLocation.UpdatedAt),
                nameof(EventLocation.UpdatedBy), nameof(EventLocation.ConcurrencyStamp))}) ON {projection} TO {OwnerRole};
            GRANT INSERT ({names.AllColumns<EventLocationDisclosureAudit>()}) ON {audit} TO {OwnerRole};
            GRANT INSERT ({names.OutboxColumns}) ON {outbox} TO {OwnerRole};
            GRANT SELECT ({names.Columns<EventLocationDisclosureAudit>(
                nameof(EventLocationDisclosureAudit.Id), nameof(EventLocationDisclosureAudit.TenantId),
                nameof(EventLocationDisclosureAudit.EventLocationId), nameof(EventLocationDisclosureAudit.NewPolicyVersion),
                nameof(EventLocationDisclosureAudit.Reason), nameof(EventLocationDisclosureAudit.OccurredAtUtc),
                nameof(EventLocationDisclosureAudit.CorrectionMessageId))})
                ON {audit} TO {OwnerRole};
            GRANT SELECT ({names.Columns<OutboxMessage>(nameof(OutboxMessage.Id), nameof(OutboxMessage.EventType),
                nameof(OutboxMessage.AggregateType), nameof(OutboxMessage.AggregateId), nameof(OutboxMessage.Payload),
                nameof(OutboxMessage.CreatedAt), nameof(OutboxMessage.Status))}) ON {outbox} TO {OwnerRole};
            GRANT INSERT ({names.AllColumns<LocationPrivacyCorrectionReceipt>()}) ON {receipt} TO {OwnerRole};
            """;
        string functions = $"{names.Function("overrides")}(text, uuid), " +
            $"{names.Function("projections")}(uuid), " +
            $"{names.Function("correct")}(text, uuid, uuid, timestamp with time zone, jsonb), " +
            $"{names.Function("retain_receipt")}(uuid, uuid, uuid)";
        return $$"""
            DO $roles$
            DECLARE role_name text;
            BEGIN
                FOREACH role_name IN ARRAY ARRAY['{{OwnerRole}}','{{MigratorRole}}','{{RuntimeRole}}','{{GlobalRole}}'] LOOP
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = role_name) THEN
                        EXECUTE format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', role_name);
                    END IF;
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = role_name
                        AND (rolcanlogin OR rolsuper OR rolcreatedb OR rolcreaterole OR rolinherit OR rolreplication OR rolbypassrls)) THEN
                        RAISE EXCEPTION 'location_governance_role_invalid';
                    END IF;
                END LOOP;
                IF EXISTS (
                    SELECT 1 FROM pg_catalog.pg_auth_members edge
                    JOIN pg_catalog.pg_roles member_role ON member_role.oid = edge.member
                    JOIN pg_catalog.pg_roles granted_role ON granted_role.oid = edge.roleid
                    WHERE member_role.rolname IN ('{{OwnerRole}}','{{RuntimeRole}}','{{GlobalRole}}')
                        OR (member_role.rolname = '{{MigratorRole}}' AND granted_role.rolname <> '{{OwnerRole}}'))
                    OR pg_has_role('{{RuntimeRole}}', '{{OwnerRole}}', 'MEMBER')
                    OR pg_has_role('{{RuntimeRole}}', '{{MigratorRole}}', 'MEMBER')
                    OR pg_has_role('{{GlobalRole}}', '{{OwnerRole}}', 'MEMBER')
                    OR pg_has_role('{{GlobalRole}}', '{{MigratorRole}}', 'MEMBER')
                    OR EXISTS (
                        WITH RECURSIVE memberships AS (
                            SELECT roleid FROM pg_catalog.pg_auth_members
                            WHERE member = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = CURRENT_USER)
                            UNION SELECT edge.roleid FROM pg_catalog.pg_auth_members edge
                            JOIN memberships inherited ON edge.member = inherited.roleid)
                        SELECT 1 FROM memberships WHERE roleid IN (
                            SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ('{{RuntimeRole}}','{{GlobalRole}}'))) THEN
                    RAISE EXCEPTION 'location_governance_migration_authority_invalid';
                END IF;
            END; $roles$;
            GRANT {{OwnerRole}} TO {{MigratorRole}};
            GRANT {{MigratorRole}} TO CURRENT_USER;
            REVOKE CREATE ON SCHEMA {{names.SchemaSql}} FROM PUBLIC, {{RuntimeRole}}, {{GlobalRole}};
            GRANT USAGE, CREATE ON SCHEMA {{names.SchemaSql}} TO {{OwnerRole}};
            GRANT USAGE ON SCHEMA {{names.SchemaSql}} TO {{RuntimeRole}}, {{GlobalRole}};
            {{names.Revoke<TenantSetting>()}}
            {{names.Revoke<EventLocation>()}}
            {{names.Revoke<Location>()}}
            {{names.Revoke<EventLocationDisclosureAudit>()}}
            {{names.Revoke<OutboxMessage>()}}
            {{names.Revoke<LocationPrivacyCorrectionReceipt>()}}
            DO $authority$
            DECLARE role_name text; table_name text;
            BEGIN
                FOREACH role_name IN ARRAY ARRAY['{{OwnerRole}}','{{RuntimeRole}}','{{GlobalRole}}'] LOOP
                    FOREACH table_name IN ARRAY ARRAY[
                        '{{setting.Replace("'", "''", StringComparison.Ordinal)}}',
                        '{{projection.Replace("'", "''", StringComparison.Ordinal)}}',
                        '{{location.Replace("'", "''", StringComparison.Ordinal)}}',
                        '{{audit.Replace("'", "''", StringComparison.Ordinal)}}',
                        '{{outbox.Replace("'", "''", StringComparison.Ordinal)}}',
                        '{{receipt.Replace("'", "''", StringComparison.Ordinal)}}'] LOOP
                        IF has_table_privilege(role_name, table_name, 'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                            OR has_any_column_privilege(role_name, table_name, 'SELECT,INSERT,UPDATE,REFERENCES') THEN
                            RAISE EXCEPTION 'location_governance_inherited_table_authority_invalid';
                        END IF;
                    END LOOP;
                END LOOP;
            END; $authority$;
            {{ownerGrants}}
            DROP POLICY IF EXISTS "{{PolicyName}}" ON {{setting}};
            CREATE POLICY "{{PolicyName}}" ON {{setting}} FOR SELECT TO {{OwnerRole}}
                USING ({{key}} IN ({{names.KeysSql}}));
            DROP POLICY IF EXISTS "{{PolicyName}}_read" ON {{projection}};
            CREATE POLICY "{{PolicyName}}_read" ON {{projection}} FOR SELECT TO {{OwnerRole}} USING (NOT {{deleted}});
            DROP POLICY IF EXISTS "{{PolicyName}}_write" ON {{projection}};
            CREATE POLICY "{{PolicyName}}_write" ON {{projection}} FOR UPDATE TO {{OwnerRole}}
                USING (NOT {{deleted}}) WITH CHECK (NOT {{deleted}});
            DROP POLICY IF EXISTS "{{PolicyName}}" ON {{location}};
            CREATE POLICY "{{PolicyName}}" ON {{location}} FOR SELECT TO {{OwnerRole}} USING (true);
            DROP POLICY IF EXISTS "{{PolicyName}}" ON {{audit}};
            CREATE POLICY "{{PolicyName}}" ON {{audit}} FOR INSERT TO {{OwnerRole}} WITH CHECK (true);
            DROP POLICY IF EXISTS "{{PolicyName}}_receipt_read" ON {{audit}};
            CREATE POLICY "{{PolicyName}}_receipt_read" ON {{audit}} FOR SELECT TO {{OwnerRole}} USING (true);
            DROP POLICY IF EXISTS "{{PolicyName}}" ON {{outbox}};
            CREATE POLICY "{{PolicyName}}" ON {{outbox}} FOR INSERT TO {{OwnerRole}} WITH CHECK (true);
            DROP POLICY IF EXISTS "{{PolicyName}}_receipt_read" ON {{outbox}};
            CREATE POLICY "{{PolicyName}}_receipt_read" ON {{outbox}} FOR SELECT TO {{OwnerRole}}
                USING ({{names.Column<OutboxMessage>(nameof(OutboxMessage.EventType))}} = 'location.privacy.corrected');
            DROP POLICY IF EXISTS "{{PolicyName}}" ON {{receipt}};
            CREATE POLICY "{{PolicyName}}" ON {{receipt}} FOR INSERT TO {{OwnerRole}} WITH CHECK (true);
            DO $install$
            DECLARE previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{{OwnerRole}}', true);
                EXECUTE $ddl$
                {{BuildReadSql(names)}}
                {{BuildCorrectionSql(names)}}
                {{BuildReceiptSql(names)}}
                $ddl$;
                EXECUTE $acl$
                    REVOKE ALL ON FUNCTION {{functions}} FROM PUBLIC, {{RuntimeRole}}, {{GlobalRole}};
                    GRANT EXECUTE ON FUNCTION {{functions}} TO {{RuntimeRole}}, {{GlobalRole}};
                $acl$;
                PERFORM set_config('role', previous_role, true);
            END; $install$;
            DROP TRIGGER IF EXISTS location_correction_receipt_custody ON {{receipt}};
            CREATE TRIGGER location_correction_receipt_custody
                BEFORE INSERT OR UPDATE OR DELETE ON {{receipt}} FOR EACH ROW
                EXECUTE FUNCTION {{names.Function("receipt_custody")}}();
            REVOKE CREATE ON SCHEMA {{names.SchemaSql}} FROM {{OwnerRole}};
            """;
    }

    private sealed class Names(ExploreDbContext context)
    {
        private readonly ISqlGenerationHelper _sql = context.GetService<ISqlGenerationHelper>();
        private string Schema => context.GetService<IDbContextOptions>()
            .FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
            ?? context.Model.FindEntityType(typeof(EventLocation))!.GetSchema() ?? "public";
        public string SchemaSql => _sql.DelimitIdentifier(Schema);
        public string KeysSql => string.Join(", ", SettingKeys.Select(value => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'"));
        public string Function(string suffix) => _sql.DelimitIdentifier("location_privacy_governance_" + suffix, Schema);
        public string Table<T>() => _sql.DelimitIdentifier(context.Model.FindEntityType(typeof(T))!.GetTableName()!, Schema);
        public string Column<T>(string property)
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            return _sql.DelimitIdentifier(entity.FindProperty(property)!.GetColumnName(table)!);
        }
        public string Columns<T>(params string[] properties) => string.Join(", ", properties.Select(Column<T>));
        public IProperty[] Properties<T>() => context.Model.FindEntityType(typeof(T))!.GetProperties().ToArray();
        public string AllColumns<T>() => Columns<T>(Properties<T>().Select(property => property.Name).ToArray());
        public IProperty[] OutboxProperties => Properties<OutboxMessage>().Where(property => !property.IsNullable
            || property.Name is nameof(OutboxMessage.Id) or nameof(OutboxMessage.AggregateType)
                or nameof(OutboxMessage.AggregateId) or nameof(OutboxMessage.EventType)
                or nameof(OutboxMessage.Payload) or nameof(OutboxMessage.Status)
                or nameof(OutboxMessage.CreatedAt) or nameof(OutboxMessage.MaxRetries)).ToArray();
        public string OutboxColumns => Columns<OutboxMessage>(OutboxProperties.Select(property => property.Name).ToArray());
        public string Revoke<T>() => $"""
            REVOKE ALL ON {Table<T>()} FROM {OwnerRole}, {RuntimeRole}, {GlobalRole};
            REVOKE SELECT ({AllColumns<T>()}), INSERT ({AllColumns<T>()}), UPDATE ({AllColumns<T>()}),
                REFERENCES ({AllColumns<T>()}) ON {Table<T>()} FROM {OwnerRole}, {RuntimeRole}, {GlobalRole};
            """;
    }
}

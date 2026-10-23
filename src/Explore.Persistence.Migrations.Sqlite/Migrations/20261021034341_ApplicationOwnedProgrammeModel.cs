using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class ApplicationOwnedProgrammeModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "evidence_kind",
                table: "ie_integration_sync_outbox",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ie_atproto_federation_instance_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    setting_key = table.Column<string>(type: "TEXT", unicode: false, maxLength: 42, nullable: false, collation: "BINARY"),
                    value = table.Column<string>(type: "TEXT", unicode: false, maxLength: 15, nullable: false, collation: "BINARY"),
                    is_locked = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: true),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    updated_by = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_atproto_federation_instance_settings", x => x.id);
                    table.CheckConstraint("ck_atproto_federation_instance_settings_key", "(setting_key = 'federation.atproto_events_enabled' AND length(setting_key) = 33)\nOR (setting_key = 'federation.atproto_events_backfill_enabled' AND length(setting_key) = 42)\nOR (setting_key = 'federation.atproto_events_backfill_mode' AND length(setting_key) = 39)");
                    table.CheckConstraint("ck_atproto_federation_instance_settings_value", "(setting_key IN ('federation.atproto_events_enabled', 'federation.atproto_events_backfill_enabled')\n    AND ((value = 'true' AND length(value) = 4) OR (value = 'false' AND length(value) = 5)))\nOR (setting_key = 'federation.atproto_events_backfill_mode'\n    AND ((lower(value) = '\"downtime_only\"' AND length(value) = 15)\n        OR (lower(value) = '\"full\"' AND length(value) = 6)))");
                });

            migrationBuilder.CreateTable(
                name: "ie_atproto_federation_tenant_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    setting_key = table.Column<string>(type: "TEXT", unicode: false, maxLength: 42, nullable: false, collation: "BINARY"),
                    value = table.Column<string>(type: "TEXT", unicode: false, maxLength: 15, nullable: false, collation: "BINARY"),
                    is_locked = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: true),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    updated_by = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_atproto_federation_tenant_settings", x => x.id);
                    table.CheckConstraint("ck_atproto_federation_tenant_settings_key", "(setting_key = 'federation.atproto_events_enabled' AND length(setting_key) = 33)\nOR (setting_key = 'federation.atproto_events_backfill_enabled' AND length(setting_key) = 42)\nOR (setting_key = 'federation.atproto_events_backfill_mode' AND length(setting_key) = 39)");
                    table.CheckConstraint("ck_atproto_federation_tenant_settings_value", "(setting_key IN ('federation.atproto_events_enabled', 'federation.atproto_events_backfill_enabled')\n    AND ((value = 'true' AND length(value) = 4) OR (value = 'false' AND length(value) = 5)))\nOR (setting_key = 'federation.atproto_events_backfill_mode'\n    AND ((lower(value) = '\"downtime_only\"' AND length(value) = 15)\n        OR (lower(value) = '\"full\"' AND length(value) = 6)))");
                    table.ForeignKey(
                        name: "fk_atproto_federation_tenant_settings_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ie_local_identity_eligibilities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    local_subject_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    current_operation_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    personal_actor_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    external_login_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    state = table.Column<int>(type: "INTEGER", nullable: false),
                    generation = table.Column<long>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    lockout_until_utc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_local_identity_eligibilities", x => x.id);
                    table.CheckConstraint("ck_local_identity_eligibility_audit", "updated_at IS NULL OR updated_at >= created_at");
                    table.CheckConstraint("ck_local_identity_eligibility_generation", "generation > 0");
                    table.CheckConstraint("ck_local_identity_eligibility_state", "state BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "fk_local_identity_eligibilities_local_identity_credential_operation_current_operation_id",
                        column: x => x.current_operation_id,
                        principalTable: "ie_local_identity_credential_operation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_local_identity_eligibilities_local_identity_users_local_subject_id",
                        column: x => x.local_subject_id,
                        principalTable: "ie_local_identity_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_managed_tenant_provisioning_policy_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    setting_key = table.Column<string>(type: "TEXT", unicode: false, maxLength: 256, nullable: false, collation: "BINARY"),
                    is_present = table.Column<bool>(type: "INTEGER", nullable: false),
                    is_locked = table.Column<bool>(type: "INTEGER", nullable: false),
                    value = table.Column<string>(type: "TEXT", nullable: true),
                    revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_managed_tenant_provisioning_policy_settings", x => x.id);
                    table.CheckConstraint("ck_provisioning_policy_absence", "is_present = true OR (is_locked = false AND value IS NULL)");
                    table.CheckConstraint("ck_provisioning_policy_revision", "revision >= 0");
                    table.CheckConstraint("ck_provisioning_policy_value", "value IS NULL OR setting_key IN ('deployment.mode','tenants.white_labeling_enabled','storage.default_tenant_quota_bytes','domains.allow_tenant_custom_domain')");
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_tenant_settings_no_dedicated_atproto_federation",
                table: "ie_tenant_setting_overrides",
                sql: "setting_key NOT IN ('federation.atproto_events_enabled', 'federation.atproto_events_backfill_enabled', 'federation.atproto_events_backfill_mode')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_system_settings_no_dedicated_atproto_federation",
                table: "ie_system_settings",
                sql: "setting_key NOT IN ('federation.atproto_events_enabled', 'federation.atproto_events_backfill_enabled', 'federation.atproto_events_backfill_mode')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_integration_sync_outbox_evidence_kind",
                table: "ie_integration_sync_outbox",
                sql: "evidence_kind BETWEEN 0 AND 5");

            migrationBuilder.CreateIndex(
                name: "ix_atproto_federation_instance_settings_setting_key",
                table: "ie_atproto_federation_instance_settings",
                column: "setting_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_atproto_federation_tenant_settings_tenant_id_setting_key",
                table: "ie_atproto_federation_tenant_settings",
                columns: new[] { "tenant_id", "setting_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_local_identity_eligibilities_current_operation_id",
                table: "ie_local_identity_eligibilities",
                column: "current_operation_id");

            migrationBuilder.CreateIndex(
                name: "ix_local_identity_eligibilities_local_subject_id",
                table: "ie_local_identity_eligibilities",
                column: "local_subject_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_managed_tenant_provisioning_policy_settings_setting_key",
                table: "ie_managed_tenant_provisioning_policy_settings",
                column: "setting_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ie_atproto_federation_instance_settings");

            migrationBuilder.DropTable(
                name: "ie_atproto_federation_tenant_settings");

            migrationBuilder.DropTable(
                name: "ie_local_identity_eligibilities");

            migrationBuilder.DropTable(
                name: "ie_managed_tenant_provisioning_policy_settings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tenant_settings_no_dedicated_atproto_federation",
                table: "ie_tenant_setting_overrides");

            migrationBuilder.DropCheckConstraint(
                name: "ck_system_settings_no_dedicated_atproto_federation",
                table: "ie_system_settings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_integration_sync_outbox_evidence_kind",
                table: "ie_integration_sync_outbox");

            migrationBuilder.DropColumn(
                name: "evidence_kind",
                table: "ie_integration_sync_outbox");
        }
    }
}

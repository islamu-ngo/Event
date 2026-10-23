using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.MySql.Migrations
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
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ie_atproto_federation_instance_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    setting_key = table.Column<string>(type: "varchar(42)", unicode: false, maxLength: 42, nullable: false, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    value = table.Column<string>(type: "varchar(15)", unicode: false, maxLength: 15, nullable: false, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    is_locked = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false, defaultValueSql: "(UTC_TIMESTAMP())"),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    updated_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_atproto_federation_instance_settings", x => x.id);
                    table.CheckConstraint("ck_atproto_federation_instance_settings_key", "(setting_key = 'federation.atproto_events_enabled' AND length(setting_key) = 33)\nOR (setting_key = 'federation.atproto_events_backfill_enabled' AND length(setting_key) = 42)\nOR (setting_key = 'federation.atproto_events_backfill_mode' AND length(setting_key) = 39)");
                    table.CheckConstraint("ck_atproto_federation_instance_settings_value", "(setting_key IN ('federation.atproto_events_enabled', 'federation.atproto_events_backfill_enabled')\n    AND ((value = 'true' AND length(value) = 4) OR (value = 'false' AND length(value) = 5)))\nOR (setting_key = 'federation.atproto_events_backfill_mode'\n    AND ((lower(value) = '\"downtime_only\"' AND length(value) = 15)\n        OR (lower(value) = '\"full\"' AND length(value) = 6)))");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_atproto_federation_tenant_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    setting_key = table.Column<string>(type: "varchar(42)", unicode: false, maxLength: 42, nullable: false, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    value = table.Column<string>(type: "varchar(15)", unicode: false, maxLength: 15, nullable: false, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    is_locked = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false, defaultValueSql: "(UTC_TIMESTAMP())"),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    updated_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci")
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_local_identity_eligibilities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    local_subject_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    current_operation_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    personal_actor_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    external_login_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    state = table.Column<int>(type: "int", nullable: false),
                    generation = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    lockout_until_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_local_identity_eligibilities", x => x.id);
                    table.CheckConstraint("ck_local_identity_eligibility_audit", "updated_at IS NULL OR updated_at >= created_at");
                    table.CheckConstraint("ck_local_identity_eligibility_generation", "generation > 0");
                    table.CheckConstraint("ck_local_identity_eligibility_state", "state BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "fk_ie_local_identity_eligibilities_ie_local_identity_cr_1f0b67f3",
                        column: x => x.current_operation_id,
                        principalTable: "ie_local_identity_credential_operation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_local_identity_eligibilities_ie_local_identity_us_20823c25",
                        column: x => x.local_subject_id,
                        principalTable: "ie_local_identity_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_managed_tenant_provisioning_policy_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    setting_key = table.Column<string>(type: "varchar(256)", unicode: false, maxLength: 256, nullable: false, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    is_present = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    is_locked = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    value = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_managed_tenant_provisioning_policy_settings", x => x.id);
                    table.CheckConstraint("ck_provisioning_policy_absence", "is_present = true OR (is_locked = false AND value IS NULL)");
                    table.CheckConstraint("ck_provisioning_policy_revision", "revision >= 0");
                    table.CheckConstraint("ck_provisioning_policy_value", "value IS NULL OR setting_key IN ('deployment.mode','tenants.white_labeling_enabled','storage.default_tenant_quota_bytes','domains.allow_tenant_custom_domain')");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

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

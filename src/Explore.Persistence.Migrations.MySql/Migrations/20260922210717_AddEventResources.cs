using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddEventResources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_event_ticket_types_tenant_id_catalog_id_id",
                table: "ie_event_ticket_types",
                columns: new[] { "tenant_id", "catalog_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_event_ticket_catalog_versions_tenant_id_event_id_id",
                table: "ie_event_ticket_catalog_versions",
                columns: new[] { "tenant_id", "event_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_ie_admission_targets_tenant_id_event_id_admission_ta_d1438e0a",
                table: "ie_admission_targets",
                columns: new[] { "tenant_id", "event_id", "admission_target_type_id", "id", "scope_id" });

            migrationBuilder.CreateTable(
                name: "ie_event_resource_delivery_types",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false),
                    master_code = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    full_name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_event_resource_delivery_types", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_event_resource_kinds",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false),
                    master_code = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    full_name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_event_resource_kinds", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_event_resources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    event_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    event_session_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    session_scope_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    event_resource_kind_id = table.Column<int>(type: "int", nullable: false),
                    event_resource_delivery_type_id = table.Column<int>(type: "int", nullable: false),
                    publication_state_id = table.Column<int>(type: "int", nullable: false),
                    disclosure_mode_id = table.Column<int>(type: "int", nullable: false),
                    title = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    public_title = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "varchar(5000)", maxLength: 5000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    sensitive_notes = table.Column<string>(type: "varchar(5000)", maxLength: 5000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    language_code = table.Column<string>(type: "varchar(35)", maxLength: 35, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    accessibility_note = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    accessible_alternative_event_resource_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    storage_object_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    external_destination_ciphertext = table.Column<string>(type: "text", maxLength: 8192, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    external_destination_protection_version = table.Column<int>(type: "int", nullable: true),
                    external_destination_safe_origin = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    availability_absolute_start_utc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    availability_absolute_end_utc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    availability_start_anchor_id = table.Column<int>(type: "int", nullable: true),
                    availability_start_offset_ticks = table.Column<long>(type: "bigint", nullable: true),
                    availability_end_anchor_id = table.Column<int>(type: "int", nullable: true),
                    availability_end_offset_ticks = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    updated_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    is_deleted = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    deleted_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    deleted_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    concurrency_stamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_event_resources", x => x.id);
                    table.UniqueConstraint("ak_event_resources_tenant_id_event_id_id", x => new { x.tenant_id, x.event_id, x.id });
                    table.UniqueConstraint("ak_event_resources_tenant_id_event_id_id_session_scope_id", x => new { x.tenant_id, x.event_id, x.id, x.session_scope_id });
                    table.UniqueConstraint("ak_event_resources_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_event_resources_alternative", "accessible_alternative_event_resource_id IS NULL OR accessible_alternative_event_resource_id <> id");
                    table.CheckConstraint("ck_event_resources_availability_end", "(availability_absolute_end_utc IS NULL AND availability_end_anchor_id IS NULL AND availability_end_offset_ticks IS NULL) OR (availability_absolute_end_utc IS NOT NULL AND availability_end_anchor_id IS NULL AND availability_end_offset_ticks IS NULL) OR (availability_absolute_end_utc IS NULL AND availability_end_anchor_id IS NOT NULL AND availability_end_anchor_id BETWEEN 1 AND 4 AND availability_end_offset_ticks IS NOT NULL)");
                    table.CheckConstraint("ck_event_resources_availability_order", "availability_absolute_start_utc IS NULL OR availability_absolute_end_utc IS NULL OR availability_absolute_end_utc > availability_absolute_start_utc");
                    table.CheckConstraint("ck_event_resources_availability_start", "(availability_absolute_start_utc IS NULL AND availability_start_anchor_id IS NULL AND availability_start_offset_ticks IS NULL) OR (availability_absolute_start_utc IS NOT NULL AND availability_start_anchor_id IS NULL AND availability_start_offset_ticks IS NULL) OR (availability_absolute_start_utc IS NULL AND availability_start_anchor_id IS NOT NULL AND availability_start_anchor_id BETWEEN 1 AND 4 AND availability_start_offset_ticks IS NOT NULL)");
                    table.CheckConstraint("ck_event_resources_disclosure", "disclosure_mode_id = 1 OR (public_title IS NOT NULL AND TRIM(public_title) <> '')");
                    table.CheckConstraint("ck_event_resources_identity", "sort_order >= 0 AND is_deleted IN (0, 1) AND TRIM(title) <> '' AND session_scope_id <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("ck_event_resources_payload", "(is_deleted = 1 AND (storage_object_id IS NULL AND external_destination_ciphertext IS NULL AND external_destination_protection_version IS NULL AND external_destination_safe_origin IS NULL)) OR (is_deleted = 0 AND ((publication_state_id IN (1, 4) AND ((storage_object_id IS NULL AND external_destination_ciphertext IS NULL AND external_destination_protection_version IS NULL AND external_destination_safe_origin IS NULL) OR (event_resource_delivery_type_id = 1 AND storage_object_id IS NOT NULL AND external_destination_ciphertext IS NULL AND external_destination_protection_version IS NULL AND external_destination_safe_origin IS NULL) OR (event_resource_delivery_type_id = 2 AND storage_object_id IS NULL AND external_destination_ciphertext IS NOT NULL AND TRIM(external_destination_ciphertext) <> '' AND external_destination_protection_version IS NOT NULL AND external_destination_protection_version > 0 AND external_destination_safe_origin IS NOT NULL AND TRIM(external_destination_safe_origin) <> ''))) OR (publication_state_id IN (2, 3) AND ((event_resource_delivery_type_id = 1 AND storage_object_id IS NOT NULL AND external_destination_ciphertext IS NULL AND external_destination_protection_version IS NULL AND external_destination_safe_origin IS NULL) OR (event_resource_delivery_type_id = 2 AND storage_object_id IS NULL AND external_destination_ciphertext IS NOT NULL AND TRIM(external_destination_ciphertext) <> '' AND external_destination_protection_version IS NOT NULL AND external_destination_protection_version > 0 AND external_destination_safe_origin IS NOT NULL AND TRIM(external_destination_safe_origin) <> '')))))");
                    table.CheckConstraint("ck_event_resources_session_scope", "(event_session_id IS NULL AND session_scope_id = event_id) OR (event_session_id IS NOT NULL AND session_scope_id = event_session_id)");
                    table.CheckConstraint("ck_event_resources_values", "event_resource_kind_id BETWEEN 1 AND 13 AND event_resource_delivery_type_id IN (1, 2) AND publication_state_id BETWEEN 1 AND 4 AND disclosure_mode_id BETWEEN 1 AND 3");
                    table.ForeignKey(
                        name: "fk_event_resources_events_tenant_id_event_id",
                        columns: x => new { x.tenant_id, x.event_id },
                        principalTable: "ie_events",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_event_resources_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_event_resources_ie_event_resource_delivery_types__160b7de8",
                        column: x => x.event_resource_delivery_type_id,
                        principalTable: "ie_event_resource_delivery_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_event_resources_ie_event_resource_kinds_event_res_e29df974",
                        column: x => x.event_resource_kind_id,
                        principalTable: "ie_event_resource_kinds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_event_resources_ie_event_resources_tenant_id_even_c7e7eeaa",
                        columns: x => new { x.tenant_id, x.event_id, x.accessible_alternative_event_resource_id },
                        principalTable: "ie_event_resources",
                        principalColumns: new[] { "tenant_id", "event_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_event_resources_ie_event_sessions_tenant_id_event_93c14f85",
                        columns: x => new { x.tenant_id, x.event_id, x.event_session_id },
                        principalTable: "ie_event_sessions",
                        principalColumns: new[] { "tenant_id", "event_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_event_resources_ie_storage_objects_tenant_id_stor_789d7a12",
                        columns: x => new { x.tenant_id, x.storage_object_id },
                        principalTable: "ie_storage_objects",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_event_resource_audience_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    event_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    event_resource_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    audience_kind_id = table.Column<int>(type: "int", nullable: false),
                    resource_event_session_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    resource_session_scope_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    event_session_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    event_ticket_catalog_version_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    event_ticket_type_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    admission_target_type_id = table.Column<int>(type: "int", nullable: true),
                    admission_target_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    admission_target_scope_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    require_confirmed_order = table.Column<int>(type: "int", nullable: false),
                    require_participant_approval = table.Column<int>(type: "int", nullable: false),
                    require_participant_completion = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_event_resource_audience_rules", x => x.id);
                    table.CheckConstraint("ck_event_resource_audience_rules_kind", "audience_kind_id BETWEEN 1 AND 9");
                    table.CheckConstraint("ck_event_resource_audience_rules_owner_session", "(resource_event_session_id IS NULL AND resource_session_scope_id = event_id) OR (resource_event_session_id IS NOT NULL AND resource_session_scope_id = resource_event_session_id AND (event_session_id IS NULL OR event_session_id = resource_event_session_id))");
                    table.CheckConstraint("ck_event_resource_audience_rules_participant", "audience_kind_id = 3 OR (require_confirmed_order = 0 AND require_participant_approval = 0 AND require_participant_completion = 0)");
                    table.CheckConstraint("ck_event_resource_audience_rules_session", "(audience_kind_id IN (3, 4, 7) AND event_session_id IS NOT NULL) OR audience_kind_id = 5 OR (audience_kind_id IN (1, 2, 6, 8, 9) AND event_session_id IS NULL)");
                    table.CheckConstraint("ck_event_resource_audience_rules_target", "(audience_kind_id = 5 AND admission_target_type_id IS NOT NULL AND admission_target_type_id IN (1, 2, 3) AND admission_target_id IS NOT NULL AND admission_target_scope_id IS NOT NULL AND ((admission_target_type_id = 1 AND admission_target_scope_id = event_id) OR admission_target_type_id = 2 OR (admission_target_type_id = 3 AND event_session_id IS NOT NULL AND admission_target_scope_id = event_session_id))) OR (audience_kind_id <> 5 AND admission_target_type_id IS NULL AND admission_target_id IS NULL AND admission_target_scope_id IS NULL)");
                    table.CheckConstraint("ck_event_resource_audience_rules_ticket", "((event_ticket_catalog_version_id IS NULL AND event_ticket_type_id IS NULL) OR (event_ticket_catalog_version_id IS NOT NULL AND event_ticket_type_id IS NOT NULL AND audience_kind_id IN (4, 5)))");
                    table.ForeignKey(
                        name: "fk_ie_event_resource_audience_rules_ie_admission_target_c2ffbd0d",
                        columns: x => new { x.tenant_id, x.event_id, x.admission_target_type_id, x.admission_target_id, x.admission_target_scope_id },
                        principalTable: "ie_admission_targets",
                        principalColumns: new[] { "tenant_id", "event_id", "admission_target_type_id", "id", "scope_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_event_resource_audience_rules_ie_event_resources__64bf4f25",
                        columns: x => new { x.tenant_id, x.event_id, x.event_resource_id, x.resource_session_scope_id },
                        principalTable: "ie_event_resources",
                        principalColumns: new[] { "tenant_id", "event_id", "id", "session_scope_id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_ie_event_resource_audience_rules_ie_event_sessions_t_b2df239a",
                        columns: x => new { x.tenant_id, x.event_id, x.event_session_id },
                        principalTable: "ie_event_sessions",
                        principalColumns: new[] { "tenant_id", "event_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_event_resource_audience_rules_ie_event_ticket_cat_333bc87d",
                        columns: x => new { x.tenant_id, x.event_id, x.event_ticket_catalog_version_id },
                        principalTable: "ie_event_ticket_catalog_versions",
                        principalColumns: new[] { "tenant_id", "event_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_event_resource_audience_rules_ie_event_ticket_typ_cb8feea3",
                        columns: x => new { x.tenant_id, x.event_ticket_catalog_version_id, x.event_ticket_type_id },
                        principalTable: "ie_event_ticket_types",
                        principalColumns: new[] { "tenant_id", "catalog_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_event_resource_audit_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    event_resource_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    responsible_manager_user_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    action = table.Column<int>(type: "int", nullable: false),
                    outcome = table.Column<int>(type: "int", nullable: false),
                    reason = table.Column<int>(type: "int", nullable: false),
                    timestamp = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_event_resource_audit_entries", x => x.id);
                    table.CheckConstraint("ck_event_resource_audit_entries_action", "action BETWEEN 1 AND 10");
                    table.CheckConstraint("ck_event_resource_audit_entries_outcome", "outcome BETWEEN 1 AND 3");
                    table.CheckConstraint("ck_event_resource_audit_entries_reason", "reason BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "fk_ie_event_resource_audit_entries_ie_event_resources_t_a1463f51",
                        columns: x => new { x.tenant_id, x.event_resource_id },
                        principalTable: "ie_event_resources",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_ie_event_resource_audience_rules_tenant_id_event_id__2c0050b4",
                table: "ie_event_resource_audience_rules",
                columns: new[] { "tenant_id", "event_id", "admission_target_type_id", "admission_target_id", "admission_target_scope_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ie_event_resource_audience_rules_tenant_id_event_id__c2b34881",
                table: "ie_event_resource_audience_rules",
                columns: new[] { "tenant_id", "event_id", "event_ticket_catalog_version_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ie_event_resource_audience_rules_tenant_id_event_id__f7179181",
                table: "ie_event_resource_audience_rules",
                columns: new[] { "tenant_id", "event_id", "event_session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ie_event_resource_audience_rules_tenant_id_event_id__fc1fb3b0",
                table: "ie_event_resource_audience_rules",
                columns: new[] { "tenant_id", "event_id", "event_resource_id", "resource_session_scope_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ie_event_resource_audience_rules_tenant_id_event_res_fefc489f",
                table: "ie_event_resource_audience_rules",
                columns: new[] { "tenant_id", "event_resource_id", "audience_kind_id", "event_session_id", "admission_target_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ie_event_resource_audience_rules_tenant_id_event_tic_4615808a",
                table: "ie_event_resource_audience_rules",
                columns: new[] { "tenant_id", "event_ticket_catalog_version_id", "event_ticket_type_id" });

            migrationBuilder.CreateIndex(
                name: "ix_event_resource_audit_entries_tenant_id_timestamp_id",
                table: "ie_event_resource_audit_entries",
                columns: new[] { "tenant_id", "timestamp", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_ie_event_resource_audit_entries_tenant_id_event_reso_7b2258f2",
                table: "ie_event_resource_audit_entries",
                columns: new[] { "tenant_id", "event_resource_id", "timestamp", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_event_resource_delivery_types_master_code",
                table: "ie_event_resource_delivery_types",
                column: "master_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_event_resource_kinds_master_code",
                table: "ie_event_resource_kinds",
                column: "master_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_event_resources_event_resource_delivery_type_id",
                table: "ie_event_resources",
                column: "event_resource_delivery_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_event_resources_event_resource_kind_id",
                table: "ie_event_resources",
                column: "event_resource_kind_id");

            migrationBuilder.CreateIndex(
                name: "ix_event_resources_tenant_id_event_id_event_session_id",
                table: "ie_event_resources",
                columns: new[] { "tenant_id", "event_id", "event_session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_event_resources_tenant_id_storage_object_id",
                table: "ie_event_resources",
                columns: new[] { "tenant_id", "storage_object_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_event_resources_tenant_id_event_id_accessible_alt_1c1e2426",
                table: "ie_event_resources",
                columns: new[] { "tenant_id", "event_id", "accessible_alternative_event_resource_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ie_event_resources_tenant_id_event_id_is_deleted_sor_1ad2bcba",
                table: "ie_event_resources",
                columns: new[] { "tenant_id", "event_id", "is_deleted", "sort_order", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ie_event_resource_audience_rules");

            migrationBuilder.DropTable(
                name: "ie_event_resource_audit_entries");

            migrationBuilder.DropTable(
                name: "ie_event_resources");

            migrationBuilder.DropTable(
                name: "ie_event_resource_delivery_types");

            migrationBuilder.DropTable(
                name: "ie_event_resource_kinds");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_event_ticket_types_tenant_id_catalog_id_id",
                table: "ie_event_ticket_types");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_event_ticket_catalog_versions_tenant_id_event_id_id",
                table: "ie_event_ticket_catalog_versions");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_ie_admission_targets_tenant_id_event_id_admission_ta_d1438e0a",
                table: "ie_admission_targets");
        }
    }
}

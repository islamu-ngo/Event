using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AllowRepeatedFailedConfigurationImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_fair_return_effect_state",
                schema: "islamu_event",
                table: "fair_return_orchestration_effects");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fair_return_effect_status",
                schema: "islamu_event",
                table: "fair_return_orchestration_effects");

            migrationBuilder.DropIndex(
                name: "ix_configuration_import_operations_session_id",
                schema: "islamu_event",
                table: "configuration_import_operations");

            migrationBuilder.DropIndex(
                name: "ix_ai_tool_executions_proposed_action_id",
                schema: "islamu_event",
                table: "ai_tool_executions");

            migrationBuilder.DropIndex(
                name: "ix_ai_tool_executions_tenant_id_proposed_action_id_started_at",
                schema: "islamu_event",
                table: "ai_tool_executions");

            migrationBuilder.AddColumn<DateTime>(
                name: "dispatch_committed_at",
                schema: "islamu_event",
                table: "refund_attempts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "dispatch_phase",
                schema: "islamu_event",
                table: "refund_attempts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "dispatch_token",
                schema: "islamu_event",
                table: "refund_attempts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "native_input_digest",
                schema: "islamu_event",
                table: "refund_attempts",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "native_operation_key",
                schema: "islamu_event",
                table: "refund_attempts",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "no_provider_handoff_proven_at",
                schema: "islamu_event",
                table: "refund_attempts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "provider_mutation_expires_at",
                schema: "islamu_event",
                table: "refund_attempts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "requested_amount_minor",
                schema: "islamu_event",
                table: "refund_attempts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "provider_sync_case_concurrency_stamp",
                schema: "islamu_event",
                table: "event_reports",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "provider_sync_case_id",
                schema: "islamu_event",
                table: "event_reports",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "provider_sync_outbox_id",
                schema: "islamu_event",
                table: "event_reports",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "provider_sync_payload_digest",
                schema: "islamu_event",
                table: "event_reports",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "provider_sync_submitted_at_utc",
                schema: "islamu_event",
                table: "event_reports",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "correction_message_id",
                schema: "islamu_event",
                table: "event_location_disclosure_audits",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "effect_outbox_id",
                schema: "islamu_event",
                table: "configuration_manifest_operations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "effect_payload_digest",
                schema: "islamu_event",
                table: "configuration_import_operations",
                type: "nchar(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "acting_actor_id",
                schema: "islamu_event",
                table: "ai_tool_executions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "action_confirmed_at",
                schema: "islamu_event",
                table: "ai_tool_executions",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "action_created_at",
                schema: "islamu_event",
                table: "ai_tool_executions",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "action_message_id",
                schema: "islamu_event",
                table: "ai_tool_executions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "concurrency_stamp",
                schema: "islamu_event",
                table: "ai_tool_executions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "conversation_id",
                schema: "islamu_event",
                table: "ai_tool_executions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "image_source_digest",
                schema: "islamu_event",
                table: "ai_tool_executions",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "image_source_message_id",
                schema: "islamu_event",
                table: "ai_tool_executions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "image_storage_object_id",
                schema: "islamu_event",
                table: "ai_tool_executions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "image_upload_operation_key",
                schema: "islamu_event",
                table: "ai_tool_executions",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "image_upload_session_id",
                schema: "islamu_event",
                table: "ai_tool_executions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payload_digest",
                schema: "islamu_event",
                table: "ai_tool_executions",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "primary_effect_linked_at",
                schema: "islamu_event",
                table: "ai_tool_executions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "primary_event_id",
                schema: "islamu_event",
                table: "ai_tool_executions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "principal_user_id",
                schema: "islamu_event",
                table: "ai_tool_executions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "reserved_event_id",
                schema: "islamu_event",
                table: "ai_tool_executions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "local_storage_quarantine",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider_binding_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    object_key = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    canonical_physical_path = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: false),
                    physical_path_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    observed_size = table.Column<long>(type: "bigint", nullable: false),
                    observed_last_modified_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    modified_before_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    admission_reason = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    admitted_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    lease_expires_at_utc_ticks = table.Column<long>(type: "bigint", nullable: true),
                    next_attempt_at_utc_ticks = table.Column<long>(type: "bigint", nullable: true),
                    settled_at_utc_ticks = table.Column<long>(type: "bigint", nullable: true),
                    outcome = table.Column<short>(type: "smallint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_local_storage_quarantine", x => x.id);
                    table.CheckConstraint("ck_local_quarantine_evidence", "observed_size >= 0 AND observed_last_modified_utc_ticks > 0 AND observed_last_modified_utc_ticks <= modified_before_utc_ticks AND modified_before_utc_ticks <= admitted_at_utc_ticks AND object_key <> '' AND canonical_physical_path <> '' AND admission_reason <> ''");
                    table.CheckConstraint("ck_local_quarantine_identity", "id <> '00000000-0000-0000-0000-000000000000' AND provider_binding_id <> '00000000-0000-0000-0000-000000000000' AND concurrency_stamp <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("ck_local_quarantine_state", "(state = 1 AND next_attempt_at_utc_ticks IS NOT NULL AND lease_expires_at_utc_ticks IS NULL AND settled_at_utc_ticks IS NULL AND outcome IS NULL) OR (state = 2 AND next_attempt_at_utc_ticks IS NULL AND lease_expires_at_utc_ticks IS NOT NULL AND settled_at_utc_ticks IS NULL AND outcome IS NULL) OR (state = 3 AND next_attempt_at_utc_ticks IS NULL AND lease_expires_at_utc_ticks IS NULL AND settled_at_utc_ticks IS NOT NULL AND outcome IN (1, 2, 3)) OR (state = 4 AND next_attempt_at_utc_ticks IS NULL AND lease_expires_at_utc_ticks IS NULL AND settled_at_utc_ticks IS NOT NULL AND outcome = 4)");
                    table.ForeignKey(
                        name: "fk_local_storage_quarantine_storage_provider_bindings_provider_binding_id",
                        column: x => x.provider_binding_id,
                        principalSchema: "islamu_event",
                        principalTable: "storage_provider_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "location_privacy_correction_receipts",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    event_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    event_location_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    policy_version = table.Column<int>(type: "int", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    disclosure_audit_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    erasure_intent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    authority_sequence = table.Column<long>(type: "bigint", nullable: true),
                    location_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    event_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_location_privacy_correction_receipts", x => x.id);
                    table.CheckConstraint("ck_location_correction_policy", "policy_version > 0");
                    table.CheckConstraint("ck_location_correction_source", "(event_type = 'location.privacy.corrected' AND disclosure_audit_id IS NOT NULL AND erasure_intent_id IS NULL AND authority_sequence IS NULL AND location_id IS NULL) OR (event_type = 'LocationPrivacyCorrectionRequested' AND disclosure_audit_id IS NULL AND erasure_intent_id IS NOT NULL AND authority_sequence IS NOT NULL AND authority_sequence > 0 AND location_id IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "registration_order_creation_receipt",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    operation_fingerprint = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    input_digest = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    registration_order_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_registration_order_creation_receipt", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_refund_attempts_tenant_id_native_operation_key",
                schema: "islamu_event",
                table: "refund_attempts",
                columns: new[] { "tenant_id", "native_operation_key" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_refund_attempts_dispatch_fence",
                schema: "islamu_event",
                table: "refund_attempts",
                sql: "dispatch_phase NOT IN (1, 3) OR (dispatch_token IS NOT NULL AND dispatch_committed_at IS NOT NULL AND provider_mutation_expires_at IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_refund_attempts_dispatch_phase",
                schema: "islamu_event",
                table: "refund_attempts",
                sql: "dispatch_phase BETWEEN 0 AND 4");

            migrationBuilder.AddCheckConstraint(
                name: "ck_refund_attempts_requested_amount",
                schema: "islamu_event",
                table: "refund_attempts",
                sql: "requested_amount_minor IS NULL OR requested_amount_minor > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fair_return_effect_state",
                schema: "islamu_event",
                table: "fair_return_orchestration_effects",
                sql: "(status_id IN (1, 5) AND lease_expires_at IS NULL AND lease_owner IS NULL AND completed_at IS NULL AND dead_lettered_at IS NULL) OR (status_id = 2 AND lease_expires_at IS NOT NULL AND lease_owner IS NOT NULL AND completed_at IS NULL AND dead_lettered_at IS NULL) OR (status_id = 3 AND lease_expires_at IS NULL AND lease_owner IS NULL AND completed_at IS NOT NULL AND dead_lettered_at IS NULL) OR (status_id = 4 AND lease_expires_at IS NULL AND lease_owner IS NULL AND completed_at IS NULL AND dead_lettered_at IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fair_return_effect_status",
                schema: "islamu_event",
                table: "fair_return_orchestration_effects",
                sql: "status_id BETWEEN 1 AND 5");

            migrationBuilder.AddCheckConstraint(
                name: "ck_event_resources_relative_availability_order",
                schema: "islamu_event",
                table: "event_resources",
                sql: "availability_start_anchor_id IS NULL OR availability_end_anchor_id IS NULL OR availability_start_anchor_id <> availability_end_anchor_id OR availability_end_offset_ticks > availability_start_offset_ticks");

            migrationBuilder.CreateIndex(
                name: "ix_event_reports_provider_sync_outbox_id",
                schema: "islamu_event",
                table: "event_reports",
                column: "provider_sync_outbox_id",
                unique: true,
                filter: "provider_sync_outbox_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_event_location_disclosure_audits_correction_message_id",
                schema: "islamu_event",
                table: "event_location_disclosure_audits",
                column: "correction_message_id");

            migrationBuilder.CreateIndex(
                name: "ix_configuration_import_operations_session_id",
                schema: "islamu_event",
                table: "configuration_import_operations",
                column: "session_id",
                unique: true,
                filter: "status <> 3");

            migrationBuilder.CreateIndex(
                name: "ix_ai_tool_executions_proposed_action_id",
                schema: "islamu_event",
                table: "ai_tool_executions",
                column: "proposed_action_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ai_tool_executions_reserved_event_id",
                schema: "islamu_event",
                table: "ai_tool_executions",
                column: "reserved_event_id",
                unique: true,
                filter: "[reserved_event_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_ai_tool_executions_tenant_id_primary_event_id",
                schema: "islamu_event",
                table: "ai_tool_executions",
                columns: new[] { "tenant_id", "primary_event_id" });

            migrationBuilder.CreateIndex(
                name: "ix_local_storage_quarantine_physical_path_hash",
                schema: "islamu_event",
                table: "local_storage_quarantine",
                column: "physical_path_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_local_storage_quarantine_provider_binding_id",
                schema: "islamu_event",
                table: "local_storage_quarantine",
                column: "provider_binding_id");

            migrationBuilder.CreateIndex(
                name: "ix_local_storage_quarantine_state_lease_expires_at_utc_ticks_id",
                schema: "islamu_event",
                table: "local_storage_quarantine",
                columns: new[] { "state", "lease_expires_at_utc_ticks", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_local_storage_quarantine_state_next_attempt_at_utc_ticks_id",
                schema: "islamu_event",
                table: "local_storage_quarantine",
                columns: new[] { "state", "next_attempt_at_utc_ticks", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_location_privacy_correction_receipts_tenant_id_event_location_id_policy_version_event_type",
                schema: "islamu_event",
                table: "location_privacy_correction_receipts",
                columns: new[] { "tenant_id", "event_location_id", "policy_version", "event_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_registration_order_creation_receipt_tenant_id_operation_fingerprint",
                schema: "islamu_event",
                table: "registration_order_creation_receipt",
                columns: new[] { "tenant_id", "operation_fingerprint" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_ai_tool_executions_events_tenant_id_primary_event_id",
                schema: "islamu_event",
                table: "ai_tool_executions",
                columns: new[] { "tenant_id", "primary_event_id" },
                principalSchema: "islamu_event",
                principalTable: "events",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_ai_tool_executions_events_tenant_id_primary_event_id",
                schema: "islamu_event",
                table: "ai_tool_executions");

            migrationBuilder.DropTable(
                name: "local_storage_quarantine",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "location_privacy_correction_receipts",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "registration_order_creation_receipt",
                schema: "islamu_event");

            migrationBuilder.DropIndex(
                name: "ix_refund_attempts_tenant_id_native_operation_key",
                schema: "islamu_event",
                table: "refund_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_refund_attempts_dispatch_fence",
                schema: "islamu_event",
                table: "refund_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_refund_attempts_dispatch_phase",
                schema: "islamu_event",
                table: "refund_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_refund_attempts_requested_amount",
                schema: "islamu_event",
                table: "refund_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fair_return_effect_state",
                schema: "islamu_event",
                table: "fair_return_orchestration_effects");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fair_return_effect_status",
                schema: "islamu_event",
                table: "fair_return_orchestration_effects");

            migrationBuilder.DropCheckConstraint(
                name: "ck_event_resources_relative_availability_order",
                schema: "islamu_event",
                table: "event_resources");

            migrationBuilder.DropIndex(
                name: "ix_event_reports_provider_sync_outbox_id",
                schema: "islamu_event",
                table: "event_reports");

            migrationBuilder.DropIndex(
                name: "ix_event_location_disclosure_audits_correction_message_id",
                schema: "islamu_event",
                table: "event_location_disclosure_audits");

            migrationBuilder.DropIndex(
                name: "ix_configuration_import_operations_session_id",
                schema: "islamu_event",
                table: "configuration_import_operations");

            migrationBuilder.DropIndex(
                name: "ix_ai_tool_executions_proposed_action_id",
                schema: "islamu_event",
                table: "ai_tool_executions");

            migrationBuilder.DropIndex(
                name: "ix_ai_tool_executions_reserved_event_id",
                schema: "islamu_event",
                table: "ai_tool_executions");

            migrationBuilder.DropIndex(
                name: "ix_ai_tool_executions_tenant_id_primary_event_id",
                schema: "islamu_event",
                table: "ai_tool_executions");

            migrationBuilder.DropColumn(
                name: "dispatch_committed_at",
                schema: "islamu_event",
                table: "refund_attempts");

            migrationBuilder.DropColumn(
                name: "dispatch_phase",
                schema: "islamu_event",
                table: "refund_attempts");

            migrationBuilder.DropColumn(
                name: "dispatch_token",
                schema: "islamu_event",
                table: "refund_attempts");

            migrationBuilder.DropColumn(
                name: "native_input_digest",
                schema: "islamu_event",
                table: "refund_attempts");

            migrationBuilder.DropColumn(
                name: "native_operation_key",
                schema: "islamu_event",
                table: "refund_attempts");

            migrationBuilder.DropColumn(
                name: "no_provider_handoff_proven_at",
                schema: "islamu_event",
                table: "refund_attempts");

            migrationBuilder.DropColumn(
                name: "provider_mutation_expires_at",
                schema: "islamu_event",
                table: "refund_attempts");

            migrationBuilder.DropColumn(
                name: "requested_amount_minor",
                schema: "islamu_event",
                table: "refund_attempts");

            migrationBuilder.DropColumn(
                name: "provider_sync_case_concurrency_stamp",
                schema: "islamu_event",
                table: "event_reports");

            migrationBuilder.DropColumn(
                name: "provider_sync_case_id",
                schema: "islamu_event",
                table: "event_reports");

            migrationBuilder.DropColumn(
                name: "provider_sync_outbox_id",
                schema: "islamu_event",
                table: "event_reports");

            migrationBuilder.DropColumn(
                name: "provider_sync_payload_digest",
                schema: "islamu_event",
                table: "event_reports");

            migrationBuilder.DropColumn(
                name: "provider_sync_submitted_at_utc",
                schema: "islamu_event",
                table: "event_reports");

            migrationBuilder.DropColumn(
                name: "correction_message_id",
                schema: "islamu_event",
                table: "event_location_disclosure_audits");

            migrationBuilder.DropColumn(
                name: "effect_outbox_id",
                schema: "islamu_event",
                table: "configuration_manifest_operations");

            migrationBuilder.DropColumn(
                name: "effect_payload_digest",
                schema: "islamu_event",
                table: "configuration_import_operations");

            migrationBuilder.DropColumn(
                name: "acting_actor_id",
                schema: "islamu_event",
                table: "ai_tool_executions");

            migrationBuilder.DropColumn(
                name: "action_confirmed_at",
                schema: "islamu_event",
                table: "ai_tool_executions");

            migrationBuilder.DropColumn(
                name: "action_created_at",
                schema: "islamu_event",
                table: "ai_tool_executions");

            migrationBuilder.DropColumn(
                name: "action_message_id",
                schema: "islamu_event",
                table: "ai_tool_executions");

            migrationBuilder.DropColumn(
                name: "concurrency_stamp",
                schema: "islamu_event",
                table: "ai_tool_executions");

            migrationBuilder.DropColumn(
                name: "conversation_id",
                schema: "islamu_event",
                table: "ai_tool_executions");

            migrationBuilder.DropColumn(
                name: "image_source_digest",
                schema: "islamu_event",
                table: "ai_tool_executions");

            migrationBuilder.DropColumn(
                name: "image_source_message_id",
                schema: "islamu_event",
                table: "ai_tool_executions");

            migrationBuilder.DropColumn(
                name: "image_storage_object_id",
                schema: "islamu_event",
                table: "ai_tool_executions");

            migrationBuilder.DropColumn(
                name: "image_upload_operation_key",
                schema: "islamu_event",
                table: "ai_tool_executions");

            migrationBuilder.DropColumn(
                name: "image_upload_session_id",
                schema: "islamu_event",
                table: "ai_tool_executions");

            migrationBuilder.DropColumn(
                name: "payload_digest",
                schema: "islamu_event",
                table: "ai_tool_executions");

            migrationBuilder.DropColumn(
                name: "primary_effect_linked_at",
                schema: "islamu_event",
                table: "ai_tool_executions");

            migrationBuilder.DropColumn(
                name: "primary_event_id",
                schema: "islamu_event",
                table: "ai_tool_executions");

            migrationBuilder.DropColumn(
                name: "principal_user_id",
                schema: "islamu_event",
                table: "ai_tool_executions");

            migrationBuilder.DropColumn(
                name: "reserved_event_id",
                schema: "islamu_event",
                table: "ai_tool_executions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fair_return_effect_state",
                schema: "islamu_event",
                table: "fair_return_orchestration_effects",
                sql: "(status_id = 1 AND lease_expires_at IS NULL AND lease_owner IS NULL AND completed_at IS NULL AND dead_lettered_at IS NULL) OR (status_id = 2 AND lease_expires_at IS NOT NULL AND lease_owner IS NOT NULL AND completed_at IS NULL AND dead_lettered_at IS NULL) OR (status_id = 3 AND lease_expires_at IS NULL AND lease_owner IS NULL AND completed_at IS NOT NULL AND dead_lettered_at IS NULL) OR (status_id = 4 AND lease_expires_at IS NULL AND lease_owner IS NULL AND completed_at IS NULL AND dead_lettered_at IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fair_return_effect_status",
                schema: "islamu_event",
                table: "fair_return_orchestration_effects",
                sql: "status_id BETWEEN 1 AND 4");

            migrationBuilder.CreateIndex(
                name: "ix_configuration_import_operations_session_id",
                schema: "islamu_event",
                table: "configuration_import_operations",
                column: "session_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ai_tool_executions_proposed_action_id",
                schema: "islamu_event",
                table: "ai_tool_executions",
                column: "proposed_action_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_tool_executions_tenant_id_proposed_action_id_started_at",
                schema: "islamu_event",
                table: "ai_tool_executions",
                columns: new[] { "tenant_id", "proposed_action_id", "started_at" });
        }
    }
}

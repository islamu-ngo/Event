using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BoundProgrammeModelCandidates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "dispatch_committed_at",
                schema: "islamu_event",
                table: "refund_attempts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "dispatch_phase",
                schema: "islamu_event",
                table: "refund_attempts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "dispatch_token",
                schema: "islamu_event",
                table: "refund_attempts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "native_input_digest",
                schema: "islamu_event",
                table: "refund_attempts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "native_operation_key",
                schema: "islamu_event",
                table: "refund_attempts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "no_provider_handoff_proven_at",
                schema: "islamu_event",
                table: "refund_attempts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "provider_mutation_expires_at",
                schema: "islamu_event",
                table: "refund_attempts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "requested_amount_minor",
                schema: "islamu_event",
                table: "refund_attempts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "regex_pattern",
                schema: "islamu_event",
                table: "event_template_custom_property_definitions",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "regex_pattern",
                schema: "islamu_event",
                table: "event_session_template_custom_property_definitions",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "regex_pattern",
                schema: "islamu_event",
                table: "event_session_custom_property_definitions",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "provider_sync_case_concurrency_stamp",
                schema: "islamu_event",
                table: "event_reports",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "provider_sync_case_id",
                schema: "islamu_event",
                table: "event_reports",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "provider_sync_outbox_id",
                schema: "islamu_event",
                table: "event_reports",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "provider_sync_payload_digest",
                schema: "islamu_event",
                table: "event_reports",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "provider_sync_submitted_at_utc",
                schema: "islamu_event",
                table: "event_reports",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "correction_message_id",
                schema: "islamu_event",
                table: "event_location_disclosure_audits",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "regex_pattern",
                schema: "islamu_event",
                table: "event_custom_property_definitions",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "regex_pattern",
                schema: "islamu_event",
                table: "custom_property_definitions",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "effect_outbox_id",
                schema: "islamu_event",
                table: "configuration_manifest_operations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "effect_payload_digest",
                schema: "islamu_event",
                table: "configuration_import_operations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "acting_actor_id",
                schema: "islamu_event",
                table: "ai_tool_executions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "action_confirmed_at",
                schema: "islamu_event",
                table: "ai_tool_executions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "action_created_at",
                schema: "islamu_event",
                table: "ai_tool_executions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "action_message_id",
                schema: "islamu_event",
                table: "ai_tool_executions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "concurrency_stamp",
                schema: "islamu_event",
                table: "ai_tool_executions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "conversation_id",
                schema: "islamu_event",
                table: "ai_tool_executions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "image_source_digest",
                schema: "islamu_event",
                table: "ai_tool_executions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "image_source_message_id",
                schema: "islamu_event",
                table: "ai_tool_executions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "image_storage_object_id",
                schema: "islamu_event",
                table: "ai_tool_executions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "image_upload_operation_key",
                schema: "islamu_event",
                table: "ai_tool_executions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "image_upload_session_id",
                schema: "islamu_event",
                table: "ai_tool_executions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payload_digest",
                schema: "islamu_event",
                table: "ai_tool_executions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "primary_effect_linked_at",
                schema: "islamu_event",
                table: "ai_tool_executions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "primary_event_id",
                schema: "islamu_event",
                table: "ai_tool_executions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "principal_user_id",
                schema: "islamu_event",
                table: "ai_tool_executions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "reserved_event_id",
                schema: "islamu_event",
                table: "ai_tool_executions",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.AlterColumn<string>(
                name: "regex_pattern",
                schema: "islamu_event",
                table: "event_template_custom_property_definitions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2048)",
                oldMaxLength: 2048,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "regex_pattern",
                schema: "islamu_event",
                table: "event_session_template_custom_property_definitions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2048)",
                oldMaxLength: 2048,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "regex_pattern",
                schema: "islamu_event",
                table: "event_session_custom_property_definitions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2048)",
                oldMaxLength: 2048,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "regex_pattern",
                schema: "islamu_event",
                table: "event_custom_property_definitions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2048)",
                oldMaxLength: 2048,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "regex_pattern",
                schema: "islamu_event",
                table: "custom_property_definitions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2048)",
                oldMaxLength: 2048,
                oldNullable: true);
        }
    }
}

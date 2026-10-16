using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.MySql.Migrations
{
    /// <inheritdoc />
    public partial class BoundProgrammeModelCandidates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "dispatch_committed_at",
                table: "ie_refund_attempts",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "dispatch_phase",
                table: "ie_refund_attempts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "dispatch_token",
                table: "ie_refund_attempts",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<string>(
                name: "native_input_digest",
                table: "ie_refund_attempts",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "native_operation_key",
                table: "ie_refund_attempts",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "no_provider_handoff_proven_at",
                table: "ie_refund_attempts",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "provider_mutation_expires_at",
                table: "ie_refund_attempts",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "requested_amount_minor",
                table: "ie_refund_attempts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "regex_pattern",
                table: "ie_event_template_custom_property_definitions",
                type: "varchar(2048)",
                maxLength: 2048,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(1000)",
                oldMaxLength: 1000,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "regex_pattern",
                table: "ie_event_session_template_custom_property_definitions",
                type: "varchar(2048)",
                maxLength: 2048,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(1000)",
                oldMaxLength: 1000,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "regex_pattern",
                table: "ie_event_session_custom_property_definitions",
                type: "varchar(2048)",
                maxLength: 2048,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(1000)",
                oldMaxLength: 1000,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<Guid>(
                name: "provider_sync_case_concurrency_stamp",
                table: "ie_event_reports",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "provider_sync_case_id",
                table: "ie_event_reports",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "provider_sync_outbox_id",
                table: "ie_event_reports",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<string>(
                name: "provider_sync_payload_digest",
                table: "ie_event_reports",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "provider_sync_submitted_at_utc",
                table: "ie_event_reports",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "correction_message_id",
                table: "ie_event_location_disclosure_audits",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AlterColumn<string>(
                name: "regex_pattern",
                table: "ie_event_custom_property_definitions",
                type: "varchar(2048)",
                maxLength: 2048,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(1000)",
                oldMaxLength: 1000,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "regex_pattern",
                table: "ie_custom_property_definitions",
                type: "varchar(2048)",
                maxLength: 2048,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(1000)",
                oldMaxLength: 1000,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<Guid>(
                name: "effect_outbox_id",
                table: "ie_configuration_manifest_operations",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<string>(
                name: "effect_payload_digest",
                table: "ie_configuration_import_operations",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<Guid>(
                name: "acting_actor_id",
                table: "ie_ai_tool_executions",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<DateTime>(
                name: "action_confirmed_at",
                table: "ie_ai_tool_executions",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "action_created_at",
                table: "ie_ai_tool_executions",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "action_message_id",
                table: "ie_ai_tool_executions",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "concurrency_stamp",
                table: "ie_ai_tool_executions",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "conversation_id",
                table: "ie_ai_tool_executions",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<string>(
                name: "image_source_digest",
                table: "ie_ai_tool_executions",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<Guid>(
                name: "image_source_message_id",
                table: "ie_ai_tool_executions",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "image_storage_object_id",
                table: "ie_ai_tool_executions",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<string>(
                name: "image_upload_operation_key",
                table: "ie_ai_tool_executions",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<Guid>(
                name: "image_upload_session_id",
                table: "ie_ai_tool_executions",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<string>(
                name: "payload_digest",
                table: "ie_ai_tool_executions",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "primary_effect_linked_at",
                table: "ie_ai_tool_executions",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "primary_event_id",
                table: "ie_ai_tool_executions",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "principal_user_id",
                table: "ie_ai_tool_executions",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "reserved_event_id",
                table: "ie_ai_tool_executions",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "dispatch_committed_at",
                table: "ie_refund_attempts");

            migrationBuilder.DropColumn(
                name: "dispatch_phase",
                table: "ie_refund_attempts");

            migrationBuilder.DropColumn(
                name: "dispatch_token",
                table: "ie_refund_attempts");

            migrationBuilder.DropColumn(
                name: "native_input_digest",
                table: "ie_refund_attempts");

            migrationBuilder.DropColumn(
                name: "native_operation_key",
                table: "ie_refund_attempts");

            migrationBuilder.DropColumn(
                name: "no_provider_handoff_proven_at",
                table: "ie_refund_attempts");

            migrationBuilder.DropColumn(
                name: "provider_mutation_expires_at",
                table: "ie_refund_attempts");

            migrationBuilder.DropColumn(
                name: "requested_amount_minor",
                table: "ie_refund_attempts");

            migrationBuilder.DropColumn(
                name: "provider_sync_case_concurrency_stamp",
                table: "ie_event_reports");

            migrationBuilder.DropColumn(
                name: "provider_sync_case_id",
                table: "ie_event_reports");

            migrationBuilder.DropColumn(
                name: "provider_sync_outbox_id",
                table: "ie_event_reports");

            migrationBuilder.DropColumn(
                name: "provider_sync_payload_digest",
                table: "ie_event_reports");

            migrationBuilder.DropColumn(
                name: "provider_sync_submitted_at_utc",
                table: "ie_event_reports");

            migrationBuilder.DropColumn(
                name: "correction_message_id",
                table: "ie_event_location_disclosure_audits");

            migrationBuilder.DropColumn(
                name: "effect_outbox_id",
                table: "ie_configuration_manifest_operations");

            migrationBuilder.DropColumn(
                name: "effect_payload_digest",
                table: "ie_configuration_import_operations");

            migrationBuilder.DropColumn(
                name: "acting_actor_id",
                table: "ie_ai_tool_executions");

            migrationBuilder.DropColumn(
                name: "action_confirmed_at",
                table: "ie_ai_tool_executions");

            migrationBuilder.DropColumn(
                name: "action_created_at",
                table: "ie_ai_tool_executions");

            migrationBuilder.DropColumn(
                name: "action_message_id",
                table: "ie_ai_tool_executions");

            migrationBuilder.DropColumn(
                name: "concurrency_stamp",
                table: "ie_ai_tool_executions");

            migrationBuilder.DropColumn(
                name: "conversation_id",
                table: "ie_ai_tool_executions");

            migrationBuilder.DropColumn(
                name: "image_source_digest",
                table: "ie_ai_tool_executions");

            migrationBuilder.DropColumn(
                name: "image_source_message_id",
                table: "ie_ai_tool_executions");

            migrationBuilder.DropColumn(
                name: "image_storage_object_id",
                table: "ie_ai_tool_executions");

            migrationBuilder.DropColumn(
                name: "image_upload_operation_key",
                table: "ie_ai_tool_executions");

            migrationBuilder.DropColumn(
                name: "image_upload_session_id",
                table: "ie_ai_tool_executions");

            migrationBuilder.DropColumn(
                name: "payload_digest",
                table: "ie_ai_tool_executions");

            migrationBuilder.DropColumn(
                name: "primary_effect_linked_at",
                table: "ie_ai_tool_executions");

            migrationBuilder.DropColumn(
                name: "primary_event_id",
                table: "ie_ai_tool_executions");

            migrationBuilder.DropColumn(
                name: "principal_user_id",
                table: "ie_ai_tool_executions");

            migrationBuilder.DropColumn(
                name: "reserved_event_id",
                table: "ie_ai_tool_executions");

            migrationBuilder.AlterColumn<string>(
                name: "regex_pattern",
                table: "ie_event_template_custom_property_definitions",
                type: "varchar(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(2048)",
                oldMaxLength: 2048,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "regex_pattern",
                table: "ie_event_session_template_custom_property_definitions",
                type: "varchar(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(2048)",
                oldMaxLength: 2048,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "regex_pattern",
                table: "ie_event_session_custom_property_definitions",
                type: "varchar(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(2048)",
                oldMaxLength: 2048,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "regex_pattern",
                table: "ie_event_custom_property_definitions",
                type: "varchar(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(2048)",
                oldMaxLength: 2048,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "regex_pattern",
                table: "ie_custom_property_definitions",
                type: "varchar(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(2048)",
                oldMaxLength: 2048,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }
    }
}

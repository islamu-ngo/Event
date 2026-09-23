using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BindEventResourceFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_storage_upload_sessions_storage_objects_storage_object_id",
                schema: "islamu_event",
                table: "storage_upload_sessions");

            migrationBuilder.DropIndex(
                name: "ix_storage_upload_sessions_storage_object_id",
                schema: "islamu_event",
                table: "storage_upload_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_storage_upload_sessions_purpose",
                schema: "islamu_event",
                table: "storage_upload_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_storage_objects_purpose",
                schema: "islamu_event",
                table: "storage_objects");

            migrationBuilder.AddColumn<Guid>(
                name: "expected_resource_version",
                schema: "islamu_event",
                table: "storage_upload_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "finalized_resource_version",
                schema: "islamu_event",
                table: "storage_upload_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "document_safety_state",
                schema: "islamu_event",
                table: "storage_objects",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "unavailable");

            migrationBuilder.AddColumn<Guid>(
                name: "inspected_object_id",
                schema: "islamu_event",
                table: "storage_objects",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "inspected_sha256_checksum",
                schema: "islamu_event",
                table: "storage_objects",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_storage_upload_sessions_tenant_id_storage_object_id",
                schema: "islamu_event",
                table: "storage_upload_sessions",
                columns: new[] { "tenant_id", "storage_object_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_storage_upload_sessions_purpose",
                schema: "islamu_event",
                table: "storage_upload_sessions",
                sql: "purpose IN ('legacy_image', 'profile_image', 'event_image', 'attachment', 'document', 'system_asset', 'event_resource')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_storage_upload_sessions_resource_owner",
                schema: "islamu_event",
                table: "storage_upload_sessions",
                sql: "(purpose <> 'event_resource' AND (owning_resource_kind IS NULL OR owning_resource_kind <> 'event_resource')) OR (purpose = 'event_resource' AND owning_resource_kind IS NOT NULL AND owning_resource_kind = 'event_resource' AND owning_resource_id IS NOT NULL AND owning_resource_id <> '00000000-0000-0000-0000-000000000000' AND user_id IS NOT NULL AND expected_resource_version IS NOT NULL AND expected_resource_version <> '00000000-0000-0000-0000-000000000000' AND visibility = 'private_owner')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_storage_objects_document_safety",
                schema: "islamu_event",
                table: "storage_objects",
                sql: "document_safety_state IN ('unavailable', 'unscanned', 'rejected')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_storage_objects_inspection_binding",
                schema: "islamu_event",
                table: "storage_objects",
                sql: "document_safety_state <> 'unscanned' OR (purpose = 'event_resource' AND inspected_object_id IS NOT NULL AND inspected_object_id = id AND inspected_sha256_checksum IS NOT NULL AND sha256_checksum IS NOT NULL AND inspected_sha256_checksum = sha256_checksum)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_storage_objects_purpose",
                schema: "islamu_event",
                table: "storage_objects",
                sql: "purpose IN ('legacy_image', 'profile_image', 'event_image', 'attachment', 'document', 'system_asset', 'event_resource')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_storage_objects_resource_owner",
                schema: "islamu_event",
                table: "storage_objects",
                sql: "(purpose <> 'event_resource' AND (owning_resource_kind IS NULL OR owning_resource_kind <> 'event_resource')) OR (purpose = 'event_resource' AND owning_resource_kind IS NOT NULL AND owning_resource_kind = 'event_resource' AND owning_resource_id IS NOT NULL AND owning_resource_id <> '00000000-0000-0000-0000-000000000000' AND visibility = 'private_owner')");

            migrationBuilder.AddForeignKey(
                name: "fk_storage_upload_sessions_storage_objects_tenant__be60349da031",
                schema: "islamu_event",
                table: "storage_upload_sessions",
                columns: new[] { "tenant_id", "storage_object_id" },
                principalSchema: "islamu_event",
                principalTable: "storage_objects",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_storage_upload_sessions_storage_objects_tenant__be60349da031",
                schema: "islamu_event",
                table: "storage_upload_sessions");

            migrationBuilder.DropIndex(
                name: "ix_storage_upload_sessions_tenant_id_storage_object_id",
                schema: "islamu_event",
                table: "storage_upload_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_storage_upload_sessions_purpose",
                schema: "islamu_event",
                table: "storage_upload_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_storage_upload_sessions_resource_owner",
                schema: "islamu_event",
                table: "storage_upload_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_storage_objects_document_safety",
                schema: "islamu_event",
                table: "storage_objects");

            migrationBuilder.DropCheckConstraint(
                name: "ck_storage_objects_inspection_binding",
                schema: "islamu_event",
                table: "storage_objects");

            migrationBuilder.DropCheckConstraint(
                name: "ck_storage_objects_purpose",
                schema: "islamu_event",
                table: "storage_objects");

            migrationBuilder.DropCheckConstraint(
                name: "ck_storage_objects_resource_owner",
                schema: "islamu_event",
                table: "storage_objects");

            migrationBuilder.DropColumn(
                name: "expected_resource_version",
                schema: "islamu_event",
                table: "storage_upload_sessions");

            migrationBuilder.DropColumn(
                name: "finalized_resource_version",
                schema: "islamu_event",
                table: "storage_upload_sessions");

            migrationBuilder.DropColumn(
                name: "document_safety_state",
                schema: "islamu_event",
                table: "storage_objects");

            migrationBuilder.DropColumn(
                name: "inspected_object_id",
                schema: "islamu_event",
                table: "storage_objects");

            migrationBuilder.DropColumn(
                name: "inspected_sha256_checksum",
                schema: "islamu_event",
                table: "storage_objects");

            migrationBuilder.CreateIndex(
                name: "ix_storage_upload_sessions_storage_object_id",
                schema: "islamu_event",
                table: "storage_upload_sessions",
                column: "storage_object_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_storage_upload_sessions_purpose",
                schema: "islamu_event",
                table: "storage_upload_sessions",
                sql: "purpose IN ('legacy_image', 'profile_image', 'event_image', 'attachment', 'document', 'system_asset')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_storage_objects_purpose",
                schema: "islamu_event",
                table: "storage_objects",
                sql: "purpose IN ('legacy_image', 'profile_image', 'event_image', 'attachment', 'document', 'system_asset')");

            migrationBuilder.AddForeignKey(
                name: "fk_storage_upload_sessions_storage_objects_storage_object_id",
                schema: "islamu_event",
                table: "storage_upload_sessions",
                column: "storage_object_id",
                principalSchema: "islamu_event",
                principalTable: "storage_objects",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}

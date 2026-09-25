using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class RetainEventResourceStorageAuthority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "producer_settled",
                schema: "islamu_event",
                table: "storage_upload_sessions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "provider_version_id",
                schema: "islamu_event",
                table: "storage_upload_sessions",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "storage_provider_binding_id",
                schema: "islamu_event",
                table: "storage_upload_sessions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "provider_version_id",
                schema: "islamu_event",
                table: "storage_objects",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "storage_provider_binding_id",
                schema: "islamu_event",
                table: "storage_objects",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "storage_provider_bindings",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    local_root_path = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: true),
                    endpoint = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    bucket_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    region = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    force_path_style = table.Column<bool>(type: "bit", nullable: false),
                    access_key_reference_binding_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    access_key_reference_setting_key = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    access_key_reference_scope = table.Column<int>(type: "int", nullable: true),
                    access_key_reference_scope_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    access_key_reference_qualifier = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    access_key_reference_source_type = table.Column<int>(type: "int", nullable: true),
                    access_key_reference_authority = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    access_key_reference_authority_endpoint = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    access_key_reference_authority_project = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    access_key_reference_environment_variable_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    access_key_reference_infisical_environment = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    access_key_reference_infisical_path = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    access_key_reference_infisical_key = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    secret_key_reference_binding_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    secret_key_reference_setting_key = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    secret_key_reference_scope = table.Column<int>(type: "int", nullable: true),
                    secret_key_reference_scope_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    secret_key_reference_qualifier = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    secret_key_reference_source_type = table.Column<int>(type: "int", nullable: true),
                    secret_key_reference_authority = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    secret_key_reference_authority_endpoint = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    secret_key_reference_authority_project = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    secret_key_reference_environment_variable_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    secret_key_reference_infisical_environment = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    secret_key_reference_infisical_path = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    secret_key_reference_infisical_key = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_storage_provider_bindings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "storage_object_deletion_tombstones",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    provider_binding_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    object_key = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    provider_object_version = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    next_attempt_at_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    lease_expires_at_utc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_storage_object_deletion_tombstones", x => x.id);
                    table.CheckConstraint("ck_storage_deletion_identity", "id <> '00000000-0000-0000-0000-000000000000' AND tenant_id <> '00000000-0000-0000-0000-000000000000' AND concurrency_stamp <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("ck_storage_deletion_provider", "provider IN ('local', 's3_compatible') AND object_key <> ''");
                    table.CheckConstraint("ck_storage_deletion_state", "(state = 1 AND next_attempt_at_utc IS NULL AND lease_expires_at_utc IS NULL) OR (state = 2 AND next_attempt_at_utc IS NOT NULL AND lease_expires_at_utc IS NULL) OR (state = 3 AND next_attempt_at_utc IS NULL AND lease_expires_at_utc IS NOT NULL) OR (state = 4 AND next_attempt_at_utc IS NULL AND lease_expires_at_utc IS NULL)");
                    table.ForeignKey(
                        name: "fk_storage_object_deletion_tombstones_storage_provider_bindings_provider_binding_id",
                        column: x => x.provider_binding_id,
                        principalSchema: "islamu_event",
                        principalTable: "storage_provider_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_storage_upload_sessions_storage_provider_binding_id",
                schema: "islamu_event",
                table: "storage_upload_sessions",
                column: "storage_provider_binding_id");

            migrationBuilder.CreateIndex(
                name: "ix_storage_objects_storage_provider_binding_id",
                schema: "islamu_event",
                table: "storage_objects",
                column: "storage_provider_binding_id");

            migrationBuilder.CreateIndex(
                name: "ix_storage_object_deletion_tombstones_provider_binding_id",
                schema: "islamu_event",
                table: "storage_object_deletion_tombstones",
                column: "provider_binding_id");

            migrationBuilder.CreateIndex(
                name: "ix_storage_object_deletion_tombstones_state_lease_expires_at_utc_id",
                schema: "islamu_event",
                table: "storage_object_deletion_tombstones",
                columns: new[] { "state", "lease_expires_at_utc", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_storage_object_deletion_tombstones_state_next_attempt_at_utc_id",
                schema: "islamu_event",
                table: "storage_object_deletion_tombstones",
                columns: new[] { "state", "next_attempt_at_utc", "id" });

            migrationBuilder.AddForeignKey(
                name: "fk_storage_objects_storage_provider_bindings_storage_provider_binding_id",
                schema: "islamu_event",
                table: "storage_objects",
                column: "storage_provider_binding_id",
                principalSchema: "islamu_event",
                principalTable: "storage_provider_bindings",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_storage_upload_sessions_storage_provider_bindings_storage_provider_binding_id",
                schema: "islamu_event",
                table: "storage_upload_sessions",
                column: "storage_provider_binding_id",
                principalSchema: "islamu_event",
                principalTable: "storage_provider_bindings",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_storage_objects_storage_provider_bindings_storage_provider_binding_id",
                schema: "islamu_event",
                table: "storage_objects");

            migrationBuilder.DropForeignKey(
                name: "fk_storage_upload_sessions_storage_provider_bindings_storage_provider_binding_id",
                schema: "islamu_event",
                table: "storage_upload_sessions");

            migrationBuilder.DropTable(
                name: "storage_object_deletion_tombstones",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "storage_provider_bindings",
                schema: "islamu_event");

            migrationBuilder.DropIndex(
                name: "ix_storage_upload_sessions_storage_provider_binding_id",
                schema: "islamu_event",
                table: "storage_upload_sessions");

            migrationBuilder.DropIndex(
                name: "ix_storage_objects_storage_provider_binding_id",
                schema: "islamu_event",
                table: "storage_objects");

            migrationBuilder.DropColumn(
                name: "producer_settled",
                schema: "islamu_event",
                table: "storage_upload_sessions");

            migrationBuilder.DropColumn(
                name: "provider_version_id",
                schema: "islamu_event",
                table: "storage_upload_sessions");

            migrationBuilder.DropColumn(
                name: "storage_provider_binding_id",
                schema: "islamu_event",
                table: "storage_upload_sessions");

            migrationBuilder.DropColumn(
                name: "provider_version_id",
                schema: "islamu_event",
                table: "storage_objects");

            migrationBuilder.DropColumn(
                name: "storage_provider_binding_id",
                schema: "islamu_event",
                table: "storage_objects");
        }
    }
}

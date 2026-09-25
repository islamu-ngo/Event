using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.MySql.Migrations
{
    /// <inheritdoc />
    public partial class RetainEventResourceStorageAuthority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "producer_settled",
                table: "ie_storage_upload_sessions",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "provider_version_id",
                table: "ie_storage_upload_sessions",
                type: "varchar(1024)",
                maxLength: 1024,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<Guid>(
                name: "storage_provider_binding_id",
                table: "ie_storage_upload_sessions",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<string>(
                name: "provider_version_id",
                table: "ie_storage_objects",
                type: "varchar(1024)",
                maxLength: 1024,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<Guid>(
                name: "storage_provider_binding_id",
                table: "ie_storage_objects",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateTable(
                name: "ie_storage_provider_bindings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    provider = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    local_root_path = table.Column<string>(type: "varchar(4096)", maxLength: 4096, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    endpoint = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    bucket_name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    region = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    force_path_style = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    access_key_reference_binding_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    access_key_reference_setting_key = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    access_key_reference_scope = table.Column<int>(type: "int", nullable: true),
                    access_key_reference_scope_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    access_key_reference_qualifier = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    access_key_reference_source_type = table.Column<int>(type: "int", nullable: true),
                    access_key_reference_authority = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    access_key_reference_authority_endpoint = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    access_key_reference_authority_project = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    access_key_reference_environment_variable_name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    access_key_reference_infisical_environment = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    access_key_reference_infisical_path = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    access_key_reference_infisical_key = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    secret_key_reference_binding_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    secret_key_reference_setting_key = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    secret_key_reference_scope = table.Column<int>(type: "int", nullable: true),
                    secret_key_reference_scope_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    secret_key_reference_qualifier = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    secret_key_reference_source_type = table.Column<int>(type: "int", nullable: true),
                    secret_key_reference_authority = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    secret_key_reference_authority_endpoint = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    secret_key_reference_authority_project = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    secret_key_reference_environment_variable_name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    secret_key_reference_infisical_environment = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    secret_key_reference_infisical_path = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    secret_key_reference_infisical_key = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_storage_provider_bindings", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_storage_object_deletion_tombstones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    provider = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    provider_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    object_key = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    provider_object_version = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    next_attempt_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    lease_expires_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_storage_object_deletion_tombstones", x => x.id);
                    table.CheckConstraint("ck_storage_deletion_identity", "id <> '00000000-0000-0000-0000-000000000000' AND tenant_id <> '00000000-0000-0000-0000-000000000000' AND concurrency_stamp <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("ck_storage_deletion_provider", "provider IN ('local', 's3_compatible') AND object_key <> ''");
                    table.CheckConstraint("ck_storage_deletion_state", "(state = 1 AND next_attempt_at_utc IS NULL AND lease_expires_at_utc IS NULL) OR (state = 2 AND next_attempt_at_utc IS NOT NULL AND lease_expires_at_utc IS NULL) OR (state = 3 AND next_attempt_at_utc IS NULL AND lease_expires_at_utc IS NOT NULL) OR (state = 4 AND next_attempt_at_utc IS NULL AND lease_expires_at_utc IS NULL)");
                    table.ForeignKey(
                        name: "fk_ie_storage_object_deletion_tombstones_ie_storage_pro_fcd33d88",
                        column: x => x.provider_binding_id,
                        principalTable: "ie_storage_provider_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_storage_upload_sessions_storage_provider_binding_id",
                table: "ie_storage_upload_sessions",
                column: "storage_provider_binding_id");

            migrationBuilder.CreateIndex(
                name: "ix_storage_objects_storage_provider_binding_id",
                table: "ie_storage_objects",
                column: "storage_provider_binding_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_storage_object_deletion_tombstones_state_lease_ex_1a600b07",
                table: "ie_storage_object_deletion_tombstones",
                columns: new[] { "state", "lease_expires_at_utc", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_ie_storage_object_deletion_tombstones_state_next_att_a79a8478",
                table: "ie_storage_object_deletion_tombstones",
                columns: new[] { "state", "next_attempt_at_utc", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_storage_object_deletion_tombstones_provider_binding_id",
                table: "ie_storage_object_deletion_tombstones",
                column: "provider_binding_id");

            migrationBuilder.AddForeignKey(
                name: "fk_ie_storage_objects_ie_storage_provider_bindings_stor_e20817d1",
                table: "ie_storage_objects",
                column: "storage_provider_binding_id",
                principalTable: "ie_storage_provider_bindings",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_ie_storage_upload_sessions_ie_storage_provider_bindi_e5a80ab3",
                table: "ie_storage_upload_sessions",
                column: "storage_provider_binding_id",
                principalTable: "ie_storage_provider_bindings",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_ie_storage_objects_ie_storage_provider_bindings_stor_e20817d1",
                table: "ie_storage_objects");

            migrationBuilder.DropForeignKey(
                name: "fk_ie_storage_upload_sessions_ie_storage_provider_bindi_e5a80ab3",
                table: "ie_storage_upload_sessions");

            migrationBuilder.DropTable(
                name: "ie_storage_object_deletion_tombstones");

            migrationBuilder.DropTable(
                name: "ie_storage_provider_bindings");

            migrationBuilder.DropIndex(
                name: "ix_storage_upload_sessions_storage_provider_binding_id",
                table: "ie_storage_upload_sessions");

            migrationBuilder.DropIndex(
                name: "ix_storage_objects_storage_provider_binding_id",
                table: "ie_storage_objects");

            migrationBuilder.DropColumn(
                name: "producer_settled",
                table: "ie_storage_upload_sessions");

            migrationBuilder.DropColumn(
                name: "provider_version_id",
                table: "ie_storage_upload_sessions");

            migrationBuilder.DropColumn(
                name: "storage_provider_binding_id",
                table: "ie_storage_upload_sessions");

            migrationBuilder.DropColumn(
                name: "provider_version_id",
                table: "ie_storage_objects");

            migrationBuilder.DropColumn(
                name: "storage_provider_binding_id",
                table: "ie_storage_objects");
        }
    }
}

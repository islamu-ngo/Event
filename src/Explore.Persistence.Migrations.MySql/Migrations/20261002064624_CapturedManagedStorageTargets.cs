using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.MySql.Migrations
{
    /// <inheritdoc />
    public partial class CapturedManagedStorageTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_storage_upload_sessions_provider_object_key",
                table: "ie_storage_upload_sessions");

            migrationBuilder.DropIndex(
                name: "ix_storage_upload_sessions_storage_provider_binding_id",
                table: "ie_storage_upload_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_storage_upload_sessions_provider",
                table: "ie_storage_upload_sessions");

            migrationBuilder.RenameColumn(
                name: "provider_object_key_uniqueness_hash",
                table: "ie_storage_objects",
                newName: "binding_object_key_uniqueness_hash");

            migrationBuilder.RenameIndex(
                name: "ix_storage_objects_provider_object_key_uniqueness_hash",
                table: "ie_storage_objects",
                newName: "ix_storage_objects_binding_object_key_uniqueness_hash");

            migrationBuilder.CreateTable(
                name: "ie_storage_producer_operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    provider_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    provider = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    object_key = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    provider_version_id = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    producer_settled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    binding_object_key_uniqueness_hash = table.Column<byte[]>(type: "binary(32)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_storage_producer_operations", x => x.id);
                    table.ForeignKey(
                        name: "fk_ie_storage_producer_operations_ie_storage_provider_b_d4a87c69",
                        column: x => x.provider_binding_id,
                        principalTable: "ie_storage_provider_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_ie_storage_upload_sessions_storage_provider_binding__f089d31e",
                table: "ie_storage_upload_sessions",
                columns: new[] { "storage_provider_binding_id", "object_key" },
                filter: "object_key IS NOT NULL")
                .Annotation("MySql:IndexPrefixLength", new[] { 0, 512 });

            migrationBuilder.AddCheckConstraint(
                name: "ck_storage_upload_sessions_bound_target",
                table: "ie_storage_upload_sessions",
                sql: "storage_provider_binding_id IS NOT NULL AND storage_provider_binding_id <> '00000000-0000-0000-0000-000000000000'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_storage_upload_sessions_provider",
                table: "ie_storage_upload_sessions",
                sql: "provider IN ('local', 's3_compatible')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_storage_objects_managed_target",
                table: "ie_storage_objects",
                sql: "(provider = 'legacy_external' AND storage_provider_binding_id IS NULL AND object_key IS NULL) OR (provider IN ('local', 's3_compatible') AND storage_provider_binding_id IS NOT NULL AND storage_provider_binding_id <> '00000000-0000-0000-0000-000000000000' AND ((object_key IS NOT NULL AND object_key <> '') OR (lifecycle_state = 'deleted' AND object_key IS NULL)))");

            migrationBuilder.CreateIndex(
                name: "ix_ie_storage_producer_operations_binding_object_key_un_67b79257",
                table: "ie_storage_producer_operations",
                column: "binding_object_key_uniqueness_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_storage_producer_operations_created_at_utc",
                table: "ie_storage_producer_operations",
                column: "created_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_storage_producer_operations_provider_binding_id",
                table: "ie_storage_producer_operations",
                column: "provider_binding_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ie_storage_producer_operations");

            migrationBuilder.DropIndex(
                name: "ix_ie_storage_upload_sessions_storage_provider_binding__f089d31e",
                table: "ie_storage_upload_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_storage_upload_sessions_bound_target",
                table: "ie_storage_upload_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_storage_upload_sessions_provider",
                table: "ie_storage_upload_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_storage_objects_managed_target",
                table: "ie_storage_objects");

            migrationBuilder.RenameColumn(
                name: "binding_object_key_uniqueness_hash",
                table: "ie_storage_objects",
                newName: "provider_object_key_uniqueness_hash");

            migrationBuilder.RenameIndex(
                name: "ix_storage_objects_binding_object_key_uniqueness_hash",
                table: "ie_storage_objects",
                newName: "ix_storage_objects_provider_object_key_uniqueness_hash");

            migrationBuilder.CreateIndex(
                name: "ix_storage_upload_sessions_provider_object_key",
                table: "ie_storage_upload_sessions",
                columns: new[] { "provider", "object_key" },
                filter: "object_key IS NOT NULL")
                .Annotation("MySql:IndexPrefixLength", new[] { 0, 512 });

            migrationBuilder.CreateIndex(
                name: "ix_storage_upload_sessions_storage_provider_binding_id",
                table: "ie_storage_upload_sessions",
                column: "storage_provider_binding_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_storage_upload_sessions_provider",
                table: "ie_storage_upload_sessions",
                sql: "provider IN ('local', 's3_compatible', 'legacy_external')");
        }
    }
}

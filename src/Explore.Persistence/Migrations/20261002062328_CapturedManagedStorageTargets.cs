using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CapturedManagedStorageTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_storage_upload_sessions_provider_object_key",
                schema: "islamu_event",
                table: "storage_upload_sessions");

            migrationBuilder.DropIndex(
                name: "ix_storage_upload_sessions_storage_provider_binding_id",
                schema: "islamu_event",
                table: "storage_upload_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_storage_upload_sessions_provider",
                schema: "islamu_event",
                table: "storage_upload_sessions");

            migrationBuilder.DropIndex(
                name: "ix_storage_objects_provider_object_key",
                schema: "islamu_event",
                table: "storage_objects");

            migrationBuilder.DropIndex(
                name: "ix_storage_objects_storage_provider_binding_id",
                schema: "islamu_event",
                table: "storage_objects");

            migrationBuilder.CreateTable(
                name: "storage_producer_operations",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    object_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    provider_version_id = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    producer_settled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_storage_producer_operations", x => x.id);
                    table.ForeignKey(
                        name: "fk_storage_producer_operations_storage_provider_bi_4ddc583051ba",
                        column: x => x.provider_binding_id,
                        principalSchema: "islamu_event",
                        principalTable: "storage_provider_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_storage_upload_sessions_storage_provider_bindin_d36d3544d53e",
                schema: "islamu_event",
                table: "storage_upload_sessions",
                columns: new[] { "storage_provider_binding_id", "object_key" },
                filter: "object_key IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_storage_upload_sessions_bound_target",
                schema: "islamu_event",
                table: "storage_upload_sessions",
                sql: "storage_provider_binding_id IS NOT NULL AND storage_provider_binding_id <> '00000000-0000-0000-0000-000000000000'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_storage_upload_sessions_provider",
                schema: "islamu_event",
                table: "storage_upload_sessions",
                sql: "provider IN ('local', 's3_compatible')");

            migrationBuilder.CreateIndex(
                name: "ix_storage_objects_storage_provider_binding_id_object_key",
                schema: "islamu_event",
                table: "storage_objects",
                columns: new[] { "storage_provider_binding_id", "object_key" },
                unique: true,
                filter: "object_key IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_storage_objects_managed_target",
                schema: "islamu_event",
                table: "storage_objects",
                sql: "(provider = 'legacy_external' AND storage_provider_binding_id IS NULL AND object_key IS NULL) OR (provider IN ('local', 's3_compatible') AND storage_provider_binding_id IS NOT NULL AND storage_provider_binding_id <> '00000000-0000-0000-0000-000000000000' AND ((object_key IS NOT NULL AND object_key <> '') OR (lifecycle_state = 'deleted' AND object_key IS NULL)))");

            migrationBuilder.CreateIndex(
                name: "ix_storage_producer_operations_created_at_utc",
                schema: "islamu_event",
                table: "storage_producer_operations",
                column: "created_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_storage_producer_operations_provider_binding_id_object_key",
                schema: "islamu_event",
                table: "storage_producer_operations",
                columns: new[] { "provider_binding_id", "object_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "storage_producer_operations",
                schema: "islamu_event");

            migrationBuilder.DropIndex(
                name: "ix_storage_upload_sessions_storage_provider_bindin_d36d3544d53e",
                schema: "islamu_event",
                table: "storage_upload_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_storage_upload_sessions_bound_target",
                schema: "islamu_event",
                table: "storage_upload_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_storage_upload_sessions_provider",
                schema: "islamu_event",
                table: "storage_upload_sessions");

            migrationBuilder.DropIndex(
                name: "ix_storage_objects_storage_provider_binding_id_object_key",
                schema: "islamu_event",
                table: "storage_objects");

            migrationBuilder.DropCheckConstraint(
                name: "ck_storage_objects_managed_target",
                schema: "islamu_event",
                table: "storage_objects");

            migrationBuilder.CreateIndex(
                name: "ix_storage_upload_sessions_provider_object_key",
                schema: "islamu_event",
                table: "storage_upload_sessions",
                columns: new[] { "provider", "object_key" },
                filter: "object_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_storage_upload_sessions_storage_provider_binding_id",
                schema: "islamu_event",
                table: "storage_upload_sessions",
                column: "storage_provider_binding_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_storage_upload_sessions_provider",
                schema: "islamu_event",
                table: "storage_upload_sessions",
                sql: "provider IN ('local', 's3_compatible', 'legacy_external')");

            migrationBuilder.CreateIndex(
                name: "ix_storage_objects_provider_object_key",
                schema: "islamu_event",
                table: "storage_objects",
                columns: new[] { "provider", "object_key" },
                unique: true,
                filter: "object_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_storage_objects_storage_provider_binding_id",
                schema: "islamu_event",
                table: "storage_objects",
                column: "storage_provider_binding_id");
        }
    }
}

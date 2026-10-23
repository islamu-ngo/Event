using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.MySql.Migrations
{
    /// <inheritdoc />
    public partial class PdsExactLineageActiveUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ie_pds_sync_outbox_tenant_id_source_entity_type_sour_1ad3be34",
                table: "ie_pds_sync_outbox");

            migrationBuilder.AlterColumn<string>(
                name: "record_key",
                table: "ie_pds_sync_outbox",
                type: "varchar(255)",
                unicode: false,
                maxLength: 255,
                nullable: false,
                collation: "ascii_bin",
                oldClrType: typeof(string),
                oldType: "varchar(255)",
                oldMaxLength: 255)
                .Annotation("MySql:CharSet", "ascii")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "did",
                table: "ie_pds_sync_outbox",
                type: "varchar(255)",
                unicode: false,
                maxLength: 255,
                nullable: false,
                collation: "ascii_bin",
                oldClrType: typeof(string),
                oldType: "varchar(255)",
                oldMaxLength: 255)
                .Annotation("MySql:CharSet", "ascii")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "collection",
                table: "ie_pds_sync_outbox",
                type: "varchar(255)",
                unicode: false,
                maxLength: 255,
                nullable: false,
                collation: "ascii_bin",
                oldClrType: typeof(string),
                oldType: "varchar(255)",
                oldMaxLength: 255)
                .Annotation("MySql:CharSet", "ascii")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "active_slot",
                table: "ie_pds_sync_outbox",
                type: "int",
                nullable: true,
                computedColumnSql: "CASE WHEN status IN (1, 2) AND superseded_at IS NULL THEN 1 ELSE NULL END",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_pds_sync_outbox_tenant_id_source_entity_type_sour_e98a3fff",
                table: "ie_pds_sync_outbox",
                columns: new[] { "tenant_id", "source_entity_type", "source_entity_id", "user_id", "did", "collection", "record_key", "source_version", "operation", "payload_hash", "active_slot" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ie_pds_sync_outbox_tenant_id_source_entity_type_sour_e98a3fff",
                table: "ie_pds_sync_outbox");

            migrationBuilder.DropColumn(
                name: "active_slot",
                table: "ie_pds_sync_outbox");

            migrationBuilder.AlterColumn<string>(
                name: "record_key",
                table: "ie_pds_sync_outbox",
                type: "varchar(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(255)",
                oldUnicode: false,
                oldMaxLength: 255,
                oldCollation: "ascii_bin")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "ascii");

            migrationBuilder.AlterColumn<string>(
                name: "did",
                table: "ie_pds_sync_outbox",
                type: "varchar(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(255)",
                oldUnicode: false,
                oldMaxLength: 255,
                oldCollation: "ascii_bin")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "ascii");

            migrationBuilder.AlterColumn<string>(
                name: "collection",
                table: "ie_pds_sync_outbox",
                type: "varchar(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(255)",
                oldUnicode: false,
                oldMaxLength: 255,
                oldCollation: "ascii_bin")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "ascii");

            migrationBuilder.CreateIndex(
                name: "ix_ie_pds_sync_outbox_tenant_id_source_entity_type_sour_1ad3be34",
                table: "ie_pds_sync_outbox",
                columns: new[] { "tenant_id", "source_entity_type", "source_entity_id", "source_version", "operation", "payload_hash" },
                unique: true,
                filter: "status IN (1, 2) AND superseded_at IS NULL");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class PdsExactLineageActiveUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_pds_sync_outbox_tenant_id_source_entity_type_source_entity_id_source_version_operation_payload_hash",
                table: "ie_pds_sync_outbox");

            migrationBuilder.AlterColumn<string>(
                name: "record_key",
                table: "ie_pds_sync_outbox",
                type: "TEXT",
                unicode: false,
                maxLength: 255,
                nullable: false,
                collation: "BINARY",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "did",
                table: "ie_pds_sync_outbox",
                type: "TEXT",
                unicode: false,
                maxLength: 255,
                nullable: false,
                collation: "BINARY",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "collection",
                table: "ie_pds_sync_outbox",
                type: "TEXT",
                unicode: false,
                maxLength: 255,
                nullable: false,
                collation: "BINARY",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 255);

            migrationBuilder.CreateIndex(
                name: "ix_pds_sync_outbox_tenant_id_source_entity_type_source_entity_id_user_id_did_collection_record_key_source_version_o_cc9a43ff9a50",
                table: "ie_pds_sync_outbox",
                columns: new[] { "tenant_id", "source_entity_type", "source_entity_id", "user_id", "did", "collection", "record_key", "source_version", "operation", "payload_hash" },
                unique: true,
                filter: "status IN (1, 2) AND superseded_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_pds_sync_outbox_tenant_id_source_entity_type_source_entity_id_user_id_did_collection_record_key_source_version_o_cc9a43ff9a50",
                table: "ie_pds_sync_outbox");

            migrationBuilder.AlterColumn<string>(
                name: "record_key",
                table: "ie_pds_sync_outbox",
                type: "TEXT",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldUnicode: false,
                oldMaxLength: 255,
                oldCollation: "BINARY");

            migrationBuilder.AlterColumn<string>(
                name: "did",
                table: "ie_pds_sync_outbox",
                type: "TEXT",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldUnicode: false,
                oldMaxLength: 255,
                oldCollation: "BINARY");

            migrationBuilder.AlterColumn<string>(
                name: "collection",
                table: "ie_pds_sync_outbox",
                type: "TEXT",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldUnicode: false,
                oldMaxLength: 255,
                oldCollation: "BINARY");

            migrationBuilder.CreateIndex(
                name: "ix_pds_sync_outbox_tenant_id_source_entity_type_source_entity_id_source_version_operation_payload_hash",
                table: "ie_pds_sync_outbox",
                columns: new[] { "tenant_id", "source_entity_type", "source_entity_id", "source_version", "operation", "payload_hash" },
                unique: true,
                filter: "status IN (1, 2) AND superseded_at IS NULL");
        }
    }
}

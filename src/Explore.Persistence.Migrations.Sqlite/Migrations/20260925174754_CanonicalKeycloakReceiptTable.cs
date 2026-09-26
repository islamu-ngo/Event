using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class CanonicalKeycloakReceiptTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "ie_KeycloakOperationReceipts",
                newName: "ie_keycloak_operation_receipts");

            migrationBuilder.RenameIndex(
                name: "ix_keycloakoperationreceipts_target_instance_id_target_authority_key_target_realm",
                table: "ie_keycloak_operation_receipts",
                newName: "ix_keycloak_operation_receipts_target_instance_id_target_authority_key_target_realm");

            migrationBuilder.RenameIndex(
                name: "ix_keycloakoperationreceipts_state",
                table: "ie_keycloak_operation_receipts",
                newName: "ix_keycloak_operation_receipts_state");

            migrationBuilder.RenameIndex(
                name: "ix_keycloakoperationreceipts_settled_at_utc_ticks",
                table: "ie_keycloak_operation_receipts",
                newName: "ix_keycloak_operation_receipts_settled_at_utc_ticks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "ie_keycloak_operation_receipts",
                newName: "ie_KeycloakOperationReceipts");

            migrationBuilder.RenameIndex(
                name: "ix_keycloak_operation_receipts_target_instance_id_target_authority_key_target_realm",
                table: "ie_KeycloakOperationReceipts",
                newName: "ix_keycloakoperationreceipts_target_instance_id_target_authority_key_target_realm");

            migrationBuilder.RenameIndex(
                name: "ix_keycloak_operation_receipts_state",
                table: "ie_KeycloakOperationReceipts",
                newName: "ix_keycloakoperationreceipts_state");

            migrationBuilder.RenameIndex(
                name: "ix_keycloak_operation_receipts_settled_at_utc_ticks",
                table: "ie_KeycloakOperationReceipts",
                newName: "ix_keycloakoperationreceipts_settled_at_utc_ticks");
        }
    }
}

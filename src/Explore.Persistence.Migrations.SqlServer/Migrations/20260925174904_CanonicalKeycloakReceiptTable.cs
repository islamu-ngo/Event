using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class CanonicalKeycloakReceiptTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "KeycloakOperationReceipts",
                schema: "islamu_event",
                newName: "keycloak_operation_receipts",
                newSchema: "islamu_event");

            migrationBuilder.RenameIndex(
                name: "ix_keycloakoperationreceipts_target_instance_id_target_authority_key_target_realm",
                schema: "islamu_event",
                table: "keycloak_operation_receipts",
                newName: "ix_keycloak_operation_receipts_target_instance_id_target_authority_key_target_realm");

            migrationBuilder.RenameIndex(
                name: "ix_keycloakoperationreceipts_state",
                schema: "islamu_event",
                table: "keycloak_operation_receipts",
                newName: "ix_keycloak_operation_receipts_state");

            migrationBuilder.RenameIndex(
                name: "ix_keycloakoperationreceipts_settled_at_utc_ticks",
                schema: "islamu_event",
                table: "keycloak_operation_receipts",
                newName: "ix_keycloak_operation_receipts_settled_at_utc_ticks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "keycloak_operation_receipts",
                schema: "islamu_event",
                newName: "KeycloakOperationReceipts",
                newSchema: "islamu_event");

            migrationBuilder.RenameIndex(
                name: "ix_keycloak_operation_receipts_target_instance_id_target_authority_key_target_realm",
                schema: "islamu_event",
                table: "KeycloakOperationReceipts",
                newName: "ix_keycloakoperationreceipts_target_instance_id_target_authority_key_target_realm");

            migrationBuilder.RenameIndex(
                name: "ix_keycloak_operation_receipts_state",
                schema: "islamu_event",
                table: "KeycloakOperationReceipts",
                newName: "ix_keycloakoperationreceipts_state");

            migrationBuilder.RenameIndex(
                name: "ix_keycloak_operation_receipts_settled_at_utc_ticks",
                schema: "islamu_event",
                table: "KeycloakOperationReceipts",
                newName: "ix_keycloakoperationreceipts_settled_at_utc_ticks");
        }
    }
}

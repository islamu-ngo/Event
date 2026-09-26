using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.MySql.Migrations
{
    /// <inheritdoc />
    public partial class CanonicalKeycloakReceiptAndSecretBindingScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_secret_bindings_setting_key_qualifier",
                table: "ie_secret_bindings");

            migrationBuilder.RenameTable(
                name: "ie_KeycloakOperationReceipts",
                newName: "ie_keycloak_operation_receipts");

            migrationBuilder.RenameIndex(
                name: "ix_keycloakoperationreceipts_state",
                table: "ie_keycloak_operation_receipts",
                newName: "ix_keycloak_operation_receipts_state");

            migrationBuilder.RenameIndex(
                name: "ix_keycloakoperationreceipts_settled_at_utc_ticks",
                table: "ie_keycloak_operation_receipts",
                newName: "ix_keycloak_operation_receipts_settled_at_utc_ticks");

            migrationBuilder.RenameIndex(
                name: "ix_ie_KeycloakOperationReceipts_target_instance_id_targ_31394dd3",
                table: "ie_keycloak_operation_receipts",
                newName: "ix_ie_keycloak_operation_receipts_target_instance_id_ta_53fb9b41");

            migrationBuilder.AddColumn<int>(
                name: "instance_slot",
                table: "ie_secret_bindings",
                type: "int",
                nullable: true,
                computedColumnSql: "CASE WHEN setting_scope_id = 1 THEN 1 ELSE NULL END",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "ix_secret_bindings_setting_key_qualifier_instance_slot",
                table: "ie_secret_bindings",
                columns: new[] { "setting_key", "qualifier", "instance_slot" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_secret_bindings_setting_key_qualifier_instance_slot",
                table: "ie_secret_bindings");

            migrationBuilder.DropColumn(
                name: "instance_slot",
                table: "ie_secret_bindings");

            migrationBuilder.RenameTable(
                name: "ie_keycloak_operation_receipts",
                newName: "ie_KeycloakOperationReceipts");

            migrationBuilder.RenameIndex(
                name: "ix_keycloak_operation_receipts_state",
                table: "ie_KeycloakOperationReceipts",
                newName: "ix_keycloakoperationreceipts_state");

            migrationBuilder.RenameIndex(
                name: "ix_keycloak_operation_receipts_settled_at_utc_ticks",
                table: "ie_KeycloakOperationReceipts",
                newName: "ix_keycloakoperationreceipts_settled_at_utc_ticks");

            migrationBuilder.RenameIndex(
                name: "ix_ie_keycloak_operation_receipts_target_instance_id_ta_53fb9b41",
                table: "ie_KeycloakOperationReceipts",
                newName: "ix_ie_KeycloakOperationReceipts_target_instance_id_targ_31394dd3");

            migrationBuilder.CreateIndex(
                name: "ix_secret_bindings_setting_key_qualifier",
                table: "ie_secret_bindings",
                columns: new[] { "setting_key", "qualifier" },
                unique: true,
                filter: "scope_id IS NULL");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class KeycloakOperationReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ie_KeycloakOperationReceipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    change_set = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    target_instance_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    target_authority = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    target_authority_key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    target_realm = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    target_client = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    actor = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    setup_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    digest = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    settled_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    settled_at_utc_ticks = table.Column<long>(type: "INTEGER", nullable: true),
                    concurrency_stamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    state = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    is_cancellation_requested = table.Column<bool>(type: "INTEGER", nullable: false),
                    step_outcomes = table.Column<string>(type: "TEXT", maxLength: 8192, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_keycloak_operation_receipts", x => x.id);
                    table.CheckConstraint("ck_keycloakoperationreceipts_expiry", "expires_at_utc > created_at_utc");
                    table.CheckConstraint("ck_keycloakoperationreceipts_settlement", "((state IN ('Previewed','Applying','OutcomeUnknown') AND settled_at_utc IS NULL AND settled_at_utc_ticks IS NULL) OR (state NOT IN ('Previewed','Applying','OutcomeUnknown') AND settled_at_utc IS NOT NULL AND settled_at_utc_ticks IS NOT NULL))");
                    table.CheckConstraint("ck_keycloakoperationreceipts_setupgeneration", "setup_generation > 0");
                    table.CheckConstraint("ck_keycloakoperationreceipts_state", "state IN ('Previewed','Applying','Verified','PartiallyApplied','OutcomeUnknown','Conflict','FailedBeforeWrite','Cancelled','Expired')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_keycloakoperationreceipts_settled_at_utc_ticks",
                table: "ie_KeycloakOperationReceipts",
                column: "settled_at_utc_ticks");

            migrationBuilder.CreateIndex(
                name: "ix_keycloakoperationreceipts_state",
                table: "ie_KeycloakOperationReceipts",
                column: "state");

            migrationBuilder.CreateIndex(
                name: "ix_keycloakoperationreceipts_target_instance_id_target_authority_key_target_realm",
                table: "ie_KeycloakOperationReceipts",
                columns: new[] { "target_instance_id", "target_authority_key", "target_realm" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ie_KeycloakOperationReceipts");
        }
    }
}

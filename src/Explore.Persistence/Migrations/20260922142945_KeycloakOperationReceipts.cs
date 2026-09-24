using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class KeycloakOperationReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KeycloakOperationReceipts",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    change_set = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    target_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_authority = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    target_authority_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target_realm = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    target_client = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    actor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    setup_generation = table.Column<long>(type: "bigint", nullable: false),
                    digest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    settled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    settled_at_utc_ticks = table.Column<long>(type: "bigint", nullable: true),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_cancellation_requested = table.Column<bool>(type: "boolean", nullable: false),
                    step_outcomes = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_keycloak_operation_receipts", x => x.id);
                    table.CheckConstraint("ck_keycloakoperationreceipts_expiry", "expires_at_utc > created_at_utc");
                    table.CheckConstraint("ck_keycloakoperationreceipts_settlement", "((state IN ('Previewed','Applying','OutcomeUnknown') AND settled_at_utc IS NULL AND settled_at_utc_ticks IS NULL) OR (state NOT IN ('Previewed','Applying','OutcomeUnknown') AND settled_at_utc IS NOT NULL AND settled_at_utc_ticks IS NOT NULL))");
                    table.CheckConstraint("ck_keycloakoperationreceipts_setupgeneration", "setup_generation > 0");
                    table.CheckConstraint("ck_keycloakoperationreceipts_state", "state IN ('Previewed','Applying','Verified','PartiallyApplied','OutcomeUnknown','Conflict','FailedBeforeWrite','Cancelled','Expired')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_keycloakoperationreceipts_settled_at_utc_ticks",
                schema: "islamu_event",
                table: "KeycloakOperationReceipts",
                column: "settled_at_utc_ticks");

            migrationBuilder.CreateIndex(
                name: "ix_keycloakoperationreceipts_state",
                schema: "islamu_event",
                table: "KeycloakOperationReceipts",
                column: "state");

            migrationBuilder.CreateIndex(
                name: "ix_keycloakoperationreceipts_target_instance_id_ta_a8dedcddfd6c",
                schema: "islamu_event",
                table: "KeycloakOperationReceipts",
                columns: new[] { "target_instance_id", "target_authority_key", "target_realm" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KeycloakOperationReceipts",
                schema: "islamu_event");
        }
    }
}

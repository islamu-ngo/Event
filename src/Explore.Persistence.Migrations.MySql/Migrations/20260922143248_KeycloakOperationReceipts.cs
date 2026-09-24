using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.MySql.Migrations
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
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    change_set = table.Column<string>(type: "varchar(4096)", maxLength: 4096, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    target_instance_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    target_authority = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    target_authority_key = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    target_realm = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    target_client = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    actor = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    setup_generation = table.Column<long>(type: "bigint", nullable: false),
                    digest = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at_utc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    settled_at_utc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    settled_at_utc_ticks = table.Column<long>(type: "bigint", nullable: true),
                    concurrency_stamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    state = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    is_cancellation_requested = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    step_outcomes = table.Column<string>(type: "varchar(8192)", maxLength: 8192, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_keycloak_operation_receipts", x => x.id);
                    table.CheckConstraint("ck_keycloakoperationreceipts_expiry", "expires_at_utc > created_at_utc");
                    table.CheckConstraint("ck_keycloakoperationreceipts_settlement", "((state IN ('Previewed','Applying','OutcomeUnknown') AND settled_at_utc IS NULL AND settled_at_utc_ticks IS NULL) OR (state NOT IN ('Previewed','Applying','OutcomeUnknown') AND settled_at_utc IS NOT NULL AND settled_at_utc_ticks IS NOT NULL))");
                    table.CheckConstraint("ck_keycloakoperationreceipts_setupgeneration", "setup_generation > 0");
                    table.CheckConstraint("ck_keycloakoperationreceipts_state", "state IN ('Previewed','Applying','Verified','PartiallyApplied','OutcomeUnknown','Conflict','FailedBeforeWrite','Cancelled','Expired')");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_ie_KeycloakOperationReceipts_target_instance_id_targ_31394dd3",
                table: "ie_KeycloakOperationReceipts",
                columns: new[] { "target_instance_id", "target_authority_key", "target_realm" });

            migrationBuilder.CreateIndex(
                name: "ix_keycloakoperationreceipts_settled_at_utc_ticks",
                table: "ie_KeycloakOperationReceipts",
                column: "settled_at_utc_ticks");

            migrationBuilder.CreateIndex(
                name: "ix_keycloakoperationreceipts_state",
                table: "ie_KeycloakOperationReceipts",
                column: "state");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ie_KeycloakOperationReceipts");
        }
    }
}

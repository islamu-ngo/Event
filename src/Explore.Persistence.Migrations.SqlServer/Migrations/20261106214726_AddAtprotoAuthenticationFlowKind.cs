using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddAtprotoAuthenticationFlowKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_moderation_case_evidence_moderation_cases_case_id",
                schema: "islamu_event",
                table: "moderation_case_evidence");

            migrationBuilder.DropCheckConstraint(
                name: "ck_atproto_transients_tenant_purpose",
                schema: "islamu_event",
                table: "atproto_transient_records");

            migrationBuilder.AddColumn<int>(
                name: "flow_kind",
                schema: "islamu_event",
                table: "atproto_transient_records",
                type: "int",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_atproto_transients_tenant_purpose",
                schema: "islamu_event",
                table: "atproto_transient_records",
                sql: "(purpose = 3 AND tenant_id IS NULL AND flow_kind IS NULL) OR (purpose IN (1, 2) AND tenant_id IS NOT NULL AND flow_kind IS NOT NULL AND flow_kind = 1) OR (purpose = 1 AND tenant_id IS NULL AND flow_kind IS NOT NULL AND flow_kind = 2)");

            migrationBuilder.AddForeignKey(
                name: "fk_moderation_case_evidence_moderation_cases_case_id",
                schema: "islamu_event",
                table: "moderation_case_evidence",
                column: "case_id",
                principalSchema: "islamu_event",
                principalTable: "moderation_cases",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_moderation_case_evidence_moderation_cases_case_id",
                schema: "islamu_event",
                table: "moderation_case_evidence");

            migrationBuilder.DropCheckConstraint(
                name: "ck_atproto_transients_tenant_purpose",
                schema: "islamu_event",
                table: "atproto_transient_records");

            migrationBuilder.DropColumn(
                name: "flow_kind",
                schema: "islamu_event",
                table: "atproto_transient_records");

            migrationBuilder.AddCheckConstraint(
                name: "ck_atproto_transients_tenant_purpose",
                schema: "islamu_event",
                table: "atproto_transient_records",
                sql: "(purpose = 3 AND tenant_id IS NULL) OR (purpose IN (1, 2) AND tenant_id IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "fk_moderation_case_evidence_moderation_cases_case_id",
                schema: "islamu_event",
                table: "moderation_case_evidence",
                column: "case_id",
                principalSchema: "islamu_event",
                principalTable: "moderation_cases",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}

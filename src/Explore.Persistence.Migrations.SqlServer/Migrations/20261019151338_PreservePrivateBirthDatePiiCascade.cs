using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class PreservePrivateBirthDatePiiCascade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_moderation_case_evidence_moderation_cases_case_id",
                schema: "islamu_event",
                table: "moderation_case_evidence");

            migrationBuilder.DropForeignKey(
                name: "fk_user_birth_date_collection_receipts_user_pii_user_id",
                schema: "islamu_event",
                table: "user_birth_date_collection_receipts");

            migrationBuilder.AddForeignKey(
                name: "fk_moderation_case_evidence_moderation_cases_case_id",
                schema: "islamu_event",
                table: "moderation_case_evidence",
                column: "case_id",
                principalSchema: "islamu_event",
                principalTable: "moderation_cases",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_user_birth_date_collection_receipts_user_pii_user_id",
                schema: "islamu_event",
                table: "user_birth_date_collection_receipts",
                column: "user_id",
                principalSchema: "islamu_event",
                principalTable: "user_pii",
                principalColumn: "user_id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_moderation_case_evidence_moderation_cases_case_id",
                schema: "islamu_event",
                table: "moderation_case_evidence");

            migrationBuilder.DropForeignKey(
                name: "fk_user_birth_date_collection_receipts_user_pii_user_id",
                schema: "islamu_event",
                table: "user_birth_date_collection_receipts");

            migrationBuilder.AddForeignKey(
                name: "fk_moderation_case_evidence_moderation_cases_case_id",
                schema: "islamu_event",
                table: "moderation_case_evidence",
                column: "case_id",
                principalSchema: "islamu_event",
                principalTable: "moderation_cases",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_user_birth_date_collection_receipts_user_pii_user_id",
                schema: "islamu_event",
                table: "user_birth_date_collection_receipts",
                column: "user_id",
                principalSchema: "islamu_event",
                principalTable: "user_pii",
                principalColumn: "user_id");
        }
    }
}

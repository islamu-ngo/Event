using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class CanonicalIdentityEmailOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_user_external_logins_id_user_id",
                schema: "islamu_event",
                table: "user_external_logins",
                columns: new[] { "id", "user_id" });

            migrationBuilder.CreateTable(
                name: "user_identity_email_claims",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    normalized_email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_identity_email_claims", x => x.id);
                    table.UniqueConstraint("ak_user_identity_email_claims_id_user_id", x => new { x.id, x.user_id });
                    table.ForeignKey(
                        name: "fk_user_identity_email_claims_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "user_identity_email_evidence",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    claim_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    external_login_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    observed_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_identity_email_evidence", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_identity_email_evidence_user_external_logins_external_login_id_user_id",
                        columns: x => new { x.external_login_id, x.user_id },
                        principalSchema: "islamu_event",
                        principalTable: "user_external_logins",
                        principalColumns: new[] { "id", "user_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_identity_email_evidence_user_identity_email_claims_claim_id_user_id",
                        columns: x => new { x.claim_id, x.user_id },
                        principalSchema: "islamu_event",
                        principalTable: "user_identity_email_claims",
                        principalColumns: new[] { "id", "user_id" });
                });

            migrationBuilder.CreateIndex(
                name: "ix_user_identity_email_claims_normalized_email",
                schema: "islamu_event",
                table: "user_identity_email_claims",
                column: "normalized_email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_identity_email_claims_user_id",
                schema: "islamu_event",
                table: "user_identity_email_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_identity_email_evidence_claim_id_user_id",
                schema: "islamu_event",
                table: "user_identity_email_evidence",
                columns: new[] { "claim_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_user_identity_email_evidence_external_login_id",
                schema: "islamu_event",
                table: "user_identity_email_evidence",
                column: "external_login_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_identity_email_evidence_external_login_id_user_id",
                schema: "islamu_event",
                table: "user_identity_email_evidence",
                columns: new[] { "external_login_id", "user_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_identity_email_evidence",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "user_identity_email_claims",
                schema: "islamu_event");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_user_external_logins_id_user_id",
                schema: "islamu_event",
                table: "user_external_logins");
        }
    }
}

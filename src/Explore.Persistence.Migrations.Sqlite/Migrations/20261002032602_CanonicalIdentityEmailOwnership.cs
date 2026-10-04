using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class CanonicalIdentityEmailOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_user_external_logins_id_user_id",
                table: "ie_user_external_logins",
                columns: new[] { "id", "user_id" });

            migrationBuilder.CreateTable(
                name: "ie_user_identity_email_claims",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    normalized_email = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_user_identity_email_claims", x => x.id);
                    table.UniqueConstraint("ak_user_identity_email_claims_id_user_id", x => new { x.id, x.user_id });
                    table.ForeignKey(
                        name: "fk_user_identity_email_claims_users_user_id",
                        column: x => x.user_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ie_user_identity_email_evidence",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    claim_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    external_login_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    observed_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_user_identity_email_evidence", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_identity_email_evidence_user_external_logins_external_login_id_user_id",
                        columns: x => new { x.external_login_id, x.user_id },
                        principalTable: "ie_user_external_logins",
                        principalColumns: new[] { "id", "user_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_identity_email_evidence_user_identity_email_claims_claim_id_user_id",
                        columns: x => new { x.claim_id, x.user_id },
                        principalTable: "ie_user_identity_email_claims",
                        principalColumns: new[] { "id", "user_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_user_identity_email_claims_normalized_email",
                table: "ie_user_identity_email_claims",
                column: "normalized_email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_identity_email_claims_user_id",
                table: "ie_user_identity_email_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_identity_email_evidence_claim_id_user_id",
                table: "ie_user_identity_email_evidence",
                columns: new[] { "claim_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_user_identity_email_evidence_external_login_id",
                table: "ie_user_identity_email_evidence",
                column: "external_login_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_identity_email_evidence_external_login_id_user_id",
                table: "ie_user_identity_email_evidence",
                columns: new[] { "external_login_id", "user_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ie_user_identity_email_evidence");

            migrationBuilder.DropTable(
                name: "ie_user_identity_email_claims");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_user_external_logins_id_user_id",
                table: "ie_user_external_logins");
        }
    }
}

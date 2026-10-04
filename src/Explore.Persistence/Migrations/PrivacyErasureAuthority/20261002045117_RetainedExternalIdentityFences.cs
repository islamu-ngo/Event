using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.PrivacyErasureAuthority
{
    /// <inheritdoc />
    public partial class RetainedExternalIdentityFences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "identity_key_id",
                schema: "privacy_erasure_authority",
                table: "authority_counter",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "identity_key_verification_tag",
                schema: "privacy_erasure_authority",
                table: "authority_counter",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "identity_fences",
                schema: "privacy_erasure_authority",
                columns: table => new
                {
                    authority_sequence = table.Column<long>(type: "bigint", nullable: false),
                    identity_kind = table.Column<int>(type: "integer", nullable: false),
                    key_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    retention_expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identity_fences", x => new { x.authority_sequence, x.identity_kind, x.key_id, x.fingerprint });
                    table.CheckConstraint("ck_identity_fences_digest", "length(fingerprint) = 64");
                    table.CheckConstraint("ck_identity_fences_key", "length(key_id) BETWEEN 1 AND 64");
                    table.CheckConstraint("ck_identity_fences_kind", "identity_kind BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "fk_identity_fences_erasure_intents_authority_sequence",
                        column: x => x.authority_sequence,
                        principalSchema: "privacy_erasure_authority",
                        principalTable: "erasure_intents",
                        principalColumn: "authority_sequence",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_erasure_intents_subject_kind_subject_id",
                schema: "privacy_erasure_authority",
                table: "erasure_intents",
                columns: new[] { "subject_kind", "subject_id" });

            migrationBuilder.CreateIndex(
                name: "ix_identity_fences_identity_kind_key_id_fingerprint",
                schema: "privacy_erasure_authority",
                table: "identity_fences",
                columns: new[] { "identity_kind", "key_id", "fingerprint" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "identity_fences",
                schema: "privacy_erasure_authority");

            migrationBuilder.DropIndex(
                name: "ix_erasure_intents_subject_kind_subject_id",
                schema: "privacy_erasure_authority",
                table: "erasure_intents");

            migrationBuilder.DropColumn(
                name: "identity_key_id",
                schema: "privacy_erasure_authority",
                table: "authority_counter");

            migrationBuilder.DropColumn(
                name: "identity_key_verification_tag",
                schema: "privacy_erasure_authority",
                table: "authority_counter");
        }
    }
}

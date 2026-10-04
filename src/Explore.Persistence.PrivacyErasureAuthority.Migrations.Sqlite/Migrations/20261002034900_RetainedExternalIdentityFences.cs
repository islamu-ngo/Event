using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.PrivacyErasureAuthority.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class RetainedExternalIdentityFences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "identity_key_id",
                table: "ie_authority_counter",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "identity_key_verification_tag",
                table: "ie_authority_counter",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ie_identity_fences",
                columns: table => new
                {
                    authority_sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    identity_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    key_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    fingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    retention_expires_at_utc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_identity_fences", x => new { x.authority_sequence, x.identity_kind, x.key_id, x.fingerprint });
                    table.CheckConstraint("ck_identity_fences_digest", "length(fingerprint) = 64");
                    table.CheckConstraint("ck_identity_fences_key", "length(key_id) BETWEEN 1 AND 64");
                    table.CheckConstraint("ck_identity_fences_kind", "identity_kind BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "fk_ie_identity_fences_ie_erasure_intents_authority_sequence",
                        column: x => x.authority_sequence,
                        principalTable: "ie_erasure_intents",
                        principalColumn: "authority_sequence",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ie_erasure_intents_subject_kind_subject_id",
                table: "ie_erasure_intents",
                columns: new[] { "subject_kind", "subject_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ie_identity_fences_identity_kind_key_id_fingerprint",
                table: "ie_identity_fences",
                columns: new[] { "identity_kind", "key_id", "fingerprint" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ie_identity_fences");

            migrationBuilder.DropIndex(
                name: "ix_ie_erasure_intents_subject_kind_subject_id",
                table: "ie_erasure_intents");

            migrationBuilder.DropColumn(
                name: "identity_key_id",
                table: "ie_authority_counter");

            migrationBuilder.DropColumn(
                name: "identity_key_verification_tag",
                table: "ie_authority_counter");
        }
    }
}

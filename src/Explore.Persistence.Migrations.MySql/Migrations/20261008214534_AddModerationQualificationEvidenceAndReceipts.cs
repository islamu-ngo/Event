using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddModerationQualificationEvidenceAndReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ie_moderation_qualification_evidence",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    purpose = table.Column<int>(type: "int", nullable: false),
                    case_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    subject_human_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    reviewer_human_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    controller_human_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    asserted_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    justification = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    conflict_state = table.Column<int>(type: "int", nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_qualification_evidence", x => x.id);
                    table.CheckConstraint("ck_moderation_qualification_evidence_assessor", "purpose = 1 OR (valid_until > recorded_at AND (reviewer_human_id IS NULL OR asserted_by_human_id <> reviewer_human_id) AND (controller_human_id IS NULL OR asserted_by_human_id <> controller_human_id) AND (subject_human_id IS NULL OR asserted_by_human_id <> subject_human_id))");
                    table.CheckConstraint("ck_moderation_qualification_evidence_revision", "revision = 1");
                    table.CheckConstraint("ck_moderation_qualification_evidence_shape", "(purpose = 1 AND subject_human_id IS NOT NULL AND asserted_by_human_id = subject_human_id AND reviewer_human_id IS NULL AND controller_human_id IS NULL AND conflict_state IS NULL AND valid_until IS NULL) OR (purpose = 2 AND subject_human_id IS NULL AND reviewer_human_id IS NOT NULL AND controller_human_id IS NOT NULL AND conflict_state IS NULL AND valid_until IS NOT NULL) OR (purpose = 3 AND subject_human_id IS NULL AND reviewer_human_id IS NOT NULL AND controller_human_id IS NOT NULL AND conflict_state IS NOT NULL AND conflict_state IN (2, 3) AND valid_until IS NOT NULL) OR (purpose = 4 AND reviewer_human_id IS NULL AND controller_human_id IS NULL AND conflict_state IS NULL AND valid_until IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_ie_moderation_qualification_evidence_ie_moderation_c_00efd021",
                        column: x => x.case_id,
                        principalTable: "ie_moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_qualification_mutation_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    request_digest = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_kind = table.Column<int>(type: "int", nullable: false),
                    source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    principal_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    result_revision = table.Column<long>(type: "bigint", nullable: false),
                    qualification_revision = table.Column<long>(type: "bigint", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_qualification_mutation_receipts", x => x.id);
                    table.CheckConstraint("ck_moderation_qualification_receipt_result", "source_kind BETWEEN 1 AND 5 AND result_revision > 0 AND qualification_revision > 0");
                    table.ForeignKey(
                        name: "fk_ie_moderation_qualification_mutation_receipts_ie_mod_4502af1f",
                        column: x => x.host_binding_id,
                        principalTable: "ie_moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_qualification_mutation_receipts_ie_ten_1347278d",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_qualification_evidence_trust_domain_id_cdb1beea",
                table: "ie_moderation_qualification_evidence",
                columns: new[] { "trust_domain_id", "tenant_id", "case_id", "purpose" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_qualification_evidence_case_id",
                table: "ie_moderation_qualification_evidence",
                column: "case_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_qualification_mutation_receipts_trust__b78cfed7",
                table: "ie_moderation_qualification_mutation_receipts",
                columns: new[] { "trust_domain_id", "tenant_id", "source_kind", "source_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_qualification_mutation_receipts_host_binding_id",
                table: "ie_moderation_qualification_mutation_receipts",
                column: "host_binding_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_qualification_mutation_receipts_tenant_id",
                table: "ie_moderation_qualification_mutation_receipts",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ie_moderation_qualification_evidence");

            migrationBuilder.DropTable(
                name: "ie_moderation_qualification_mutation_receipts");
        }
    }
}

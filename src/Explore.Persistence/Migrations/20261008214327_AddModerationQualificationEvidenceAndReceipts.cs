using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddModerationQualificationEvidenceAndReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "moderation_qualification_evidence",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<int>(type: "integer", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_human_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewer_human_id = table.Column<Guid>(type: "uuid", nullable: true),
                    controller_human_id = table.Column<Guid>(type: "uuid", nullable: true),
                    asserted_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    justification = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    conflict_state = table.Column<int>(type: "integer", nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_qualification_evidence", x => x.id);
                    table.CheckConstraint("ck_moderation_qualification_evidence_assessor", "purpose = 1 OR (valid_until > recorded_at AND (reviewer_human_id IS NULL OR asserted_by_human_id <> reviewer_human_id) AND (controller_human_id IS NULL OR asserted_by_human_id <> controller_human_id) AND (subject_human_id IS NULL OR asserted_by_human_id <> subject_human_id))");
                    table.CheckConstraint("ck_moderation_qualification_evidence_revision", "revision = 1");
                    table.CheckConstraint("ck_moderation_qualification_evidence_shape", "(purpose = 1 AND subject_human_id IS NOT NULL AND asserted_by_human_id = subject_human_id AND reviewer_human_id IS NULL AND controller_human_id IS NULL AND conflict_state IS NULL AND valid_until IS NULL) OR (purpose = 2 AND subject_human_id IS NULL AND reviewer_human_id IS NOT NULL AND controller_human_id IS NOT NULL AND conflict_state IS NULL AND valid_until IS NOT NULL) OR (purpose = 3 AND subject_human_id IS NULL AND reviewer_human_id IS NOT NULL AND controller_human_id IS NOT NULL AND conflict_state IS NOT NULL AND conflict_state IN (2, 3) AND valid_until IS NOT NULL) OR (purpose = 4 AND reviewer_human_id IS NULL AND controller_human_id IS NULL AND conflict_state IS NULL AND valid_until IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_moderation_qualification_evidence_moderation_cases_case_id",
                        column: x => x.case_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_qualification_mutation_receipts",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_kind = table.Column<int>(type: "integer", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    principal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    result_revision = table.Column<long>(type: "bigint", nullable: false),
                    qualification_revision = table.Column<long>(type: "bigint", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_qualification_mutation_receipts", x => x.id);
                    table.CheckConstraint("ck_moderation_qualification_receipt_result", "source_kind BETWEEN 1 AND 5 AND result_revision > 0 AND qualification_revision > 0");
                    table.ForeignKey(
                        name: "fk_moderation_qualification_mutation_receipts_mode_146c71d6becf",
                        column: x => x.host_binding_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_qualification_mutation_receipts_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "islamu_event",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_qualification_evidence_case_id",
                schema: "islamu_event",
                table: "moderation_qualification_evidence",
                column: "case_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_qualification_evidence_trust_domain__007234ff1c55",
                schema: "islamu_event",
                table: "moderation_qualification_evidence",
                columns: new[] { "trust_domain_id", "tenant_id", "case_id", "purpose" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_qualification_mutation_receipts_host_binding_id",
                schema: "islamu_event",
                table: "moderation_qualification_mutation_receipts",
                column: "host_binding_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_qualification_mutation_receipts_tenant_id",
                schema: "islamu_event",
                table: "moderation_qualification_mutation_receipts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_qualification_mutation_receipts_trus_87e5a6556515",
                schema: "islamu_event",
                table: "moderation_qualification_mutation_receipts",
                columns: new[] { "trust_domain_id", "tenant_id", "source_kind", "source_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "moderation_qualification_evidence",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_qualification_mutation_receipts",
                schema: "islamu_event");
        }
    }
}

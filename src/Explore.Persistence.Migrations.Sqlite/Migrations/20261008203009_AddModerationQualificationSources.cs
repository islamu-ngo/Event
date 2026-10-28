using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddModerationQualificationSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ie_moderation_findings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    case_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    case_occurrence_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    case_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    case_created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    subject_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    subject_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    subject_origin_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    subject_origin_trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    subject_origin_solution_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    subject_origin_instance_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    subject_origin_tenant_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    subject_origin_community_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    subject_origin_resource_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    scope_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    scope_trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    scope_solution_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    scope_instance_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    scope_tenant_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    scope_community_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    scope_resource_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    rule_owner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    rule_code = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    rule_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    adjudicator_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    adjudicator_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    adjudicator_origin_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    adjudicator_origin_trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    adjudicator_origin_solution_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    adjudicator_origin_instance_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    adjudicator_origin_tenant_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    adjudicator_origin_community_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    adjudicator_origin_resource_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    evidence_purpose = table.Column<int>(type: "INTEGER", nullable: false),
                    evidence_reference_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    urgency_qualification_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    evidence_recorded_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    justification = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    finalized_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    state = table.Column<int>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    reversal_reference_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    reversed_by_kind = table.Column<int>(type: "INTEGER", nullable: true),
                    reversed_by_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    reversed_by_origin_kind = table.Column<int>(type: "INTEGER", nullable: true),
                    reversed_by_origin_trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    reversed_by_origin_solution_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    reversed_by_origin_instance_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    reversed_by_origin_tenant_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    reversed_by_origin_community_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    reversed_by_origin_resource_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    reversal_justification = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    reversed_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_findings", x => x.id);
                    table.CheckConstraint("ck_moderation_finding_adjudicator", "adjudicator_kind = 1 AND adjudicator_id <> subject_id AND adjudicator_origin_trust_domain_id = scope_trust_domain_id");
                    table.CheckConstraint("ck_moderation_finding_evidence", "case_revision = 1 AND rule_revision > 0 AND evidence_recorded_at >= case_created_at AND evidence_recorded_at <= finalized_at AND finalized_at >= case_created_at AND ((evidence_purpose = 1 AND urgency_qualification_id IS NULL) OR (evidence_purpose = 2 AND urgency_qualification_id IS NOT NULL AND urgency_qualification_id <> evidence_reference_id))");
                    table.CheckConstraint("ck_moderation_finding_state", "(state = 1 AND revision = 1 AND reversed_at IS NULL AND reversal_reference_id IS NULL AND reversed_by_id IS NULL AND reversal_justification IS NULL AND updated_at = finalized_at) OR (state = 2 AND revision = 2 AND reversed_at IS NOT NULL AND reversal_reference_id IS NOT NULL AND reversed_by_id IS NOT NULL AND reversal_justification IS NOT NULL AND reversed_at >= finalized_at AND updated_at = reversed_at)");
                    table.CheckConstraint("ck_moderation_findings_adjudicator_origin_scope", "adjudicator_origin_kind IS NOT NULL AND ((adjudicator_origin_kind = 1 AND adjudicator_origin_trust_domain_id IS NOT NULL AND adjudicator_origin_solution_id IS NULL AND adjudicator_origin_instance_id IS NULL AND adjudicator_origin_tenant_id IS NULL AND adjudicator_origin_community_id IS NULL AND adjudicator_origin_resource_id IS NULL) OR (adjudicator_origin_kind = 2 AND adjudicator_origin_trust_domain_id IS NOT NULL AND adjudicator_origin_solution_id IS NOT NULL AND adjudicator_origin_instance_id IS NULL AND adjudicator_origin_tenant_id IS NULL AND adjudicator_origin_community_id IS NULL AND adjudicator_origin_resource_id IS NULL) OR (adjudicator_origin_kind = 3 AND adjudicator_origin_trust_domain_id IS NOT NULL AND adjudicator_origin_solution_id IS NOT NULL AND adjudicator_origin_instance_id IS NOT NULL AND adjudicator_origin_tenant_id IS NULL AND adjudicator_origin_community_id IS NULL AND adjudicator_origin_resource_id IS NULL) OR (adjudicator_origin_kind = 4 AND adjudicator_origin_trust_domain_id IS NOT NULL AND adjudicator_origin_solution_id IS NOT NULL AND adjudicator_origin_instance_id IS NOT NULL AND adjudicator_origin_tenant_id IS NOT NULL AND adjudicator_origin_community_id IS NULL AND adjudicator_origin_resource_id IS NULL) OR (adjudicator_origin_kind = 5 AND adjudicator_origin_trust_domain_id IS NOT NULL AND adjudicator_origin_solution_id IS NOT NULL AND adjudicator_origin_instance_id IS NOT NULL AND adjudicator_origin_tenant_id IS NOT NULL AND adjudicator_origin_community_id IS NOT NULL AND adjudicator_origin_resource_id IS NULL) OR (adjudicator_origin_kind = 6 AND adjudicator_origin_trust_domain_id IS NOT NULL AND adjudicator_origin_solution_id IS NOT NULL AND adjudicator_origin_instance_id IS NOT NULL AND adjudicator_origin_tenant_id IS NOT NULL AND adjudicator_origin_resource_id IS NOT NULL))");
                    table.CheckConstraint("ck_moderation_findings_adjudicator_subject", "adjudicator_kind BETWEEN 1 AND 4");
                    table.CheckConstraint("ck_moderation_findings_reversed_by_origin_scope", "(reversed_by_origin_kind IS NULL AND reversed_by_origin_trust_domain_id IS NULL AND reversed_by_origin_solution_id IS NULL AND reversed_by_origin_instance_id IS NULL AND reversed_by_origin_tenant_id IS NULL AND reversed_by_origin_community_id IS NULL AND reversed_by_origin_resource_id IS NULL) OR (reversed_by_origin_kind IS NOT NULL AND ((reversed_by_origin_kind = 1 AND reversed_by_origin_trust_domain_id IS NOT NULL AND reversed_by_origin_solution_id IS NULL AND reversed_by_origin_instance_id IS NULL AND reversed_by_origin_tenant_id IS NULL AND reversed_by_origin_community_id IS NULL AND reversed_by_origin_resource_id IS NULL) OR (reversed_by_origin_kind = 2 AND reversed_by_origin_trust_domain_id IS NOT NULL AND reversed_by_origin_solution_id IS NOT NULL AND reversed_by_origin_instance_id IS NULL AND reversed_by_origin_tenant_id IS NULL AND reversed_by_origin_community_id IS NULL AND reversed_by_origin_resource_id IS NULL) OR (reversed_by_origin_kind = 3 AND reversed_by_origin_trust_domain_id IS NOT NULL AND reversed_by_origin_solution_id IS NOT NULL AND reversed_by_origin_instance_id IS NOT NULL AND reversed_by_origin_tenant_id IS NULL AND reversed_by_origin_community_id IS NULL AND reversed_by_origin_resource_id IS NULL) OR (reversed_by_origin_kind = 4 AND reversed_by_origin_trust_domain_id IS NOT NULL AND reversed_by_origin_solution_id IS NOT NULL AND reversed_by_origin_instance_id IS NOT NULL AND reversed_by_origin_tenant_id IS NOT NULL AND reversed_by_origin_community_id IS NULL AND reversed_by_origin_resource_id IS NULL) OR (reversed_by_origin_kind = 5 AND reversed_by_origin_trust_domain_id IS NOT NULL AND reversed_by_origin_solution_id IS NOT NULL AND reversed_by_origin_instance_id IS NOT NULL AND reversed_by_origin_tenant_id IS NOT NULL AND reversed_by_origin_community_id IS NOT NULL AND reversed_by_origin_resource_id IS NULL) OR (reversed_by_origin_kind = 6 AND reversed_by_origin_trust_domain_id IS NOT NULL AND reversed_by_origin_solution_id IS NOT NULL AND reversed_by_origin_instance_id IS NOT NULL AND reversed_by_origin_tenant_id IS NOT NULL AND reversed_by_origin_resource_id IS NOT NULL)))");
                    table.CheckConstraint("ck_moderation_findings_reversed_by_subject", "(reversed_by_kind IS NULL AND reversed_by_id IS NULL AND reversed_by_origin_kind IS NULL) OR (reversed_by_kind IS NOT NULL AND reversed_by_kind BETWEEN 1 AND 4 AND reversed_by_id IS NOT NULL AND reversed_by_origin_kind IS NOT NULL)");
                    table.CheckConstraint("ck_moderation_findings_revision", "revision > 0");
                    table.CheckConstraint("ck_moderation_findings_scope_scope", "scope_kind IS NOT NULL AND ((scope_kind = 1 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 2 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 3 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 4 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 5 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NOT NULL AND scope_resource_id IS NULL) OR (scope_kind = 6 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_resource_id IS NOT NULL))");
                    table.CheckConstraint("ck_moderation_findings_subject_origin_scope", "subject_origin_kind IS NOT NULL AND ((subject_origin_kind = 1 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 2 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 3 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 4 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 5 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NOT NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 6 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_resource_id IS NOT NULL))");
                    table.CheckConstraint("ck_moderation_findings_subject_subject", "subject_kind BETWEEN 1 AND 4");
                    table.CheckConstraint("ck_moderation_findings_subject_trust", "subject_origin_trust_domain_id = scope_trust_domain_id");
                    table.ForeignKey(
                        name: "fk_moderation_findings_moderation_cases_case_id",
                        column: x => x.case_id,
                        principalTable: "ie_moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    jurisdiction_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    jurisdiction_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    jurisdiction_trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    jurisdiction_solution_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    jurisdiction_instance_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    jurisdiction_tenant_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    jurisdiction_community_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    jurisdiction_resource_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    publication_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    rule_count = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_policies", x => x.id);
                    table.CheckConstraint("ck_moderation_policies_jurisdiction_scope", "jurisdiction_kind IS NOT NULL AND ((jurisdiction_kind = 1 AND jurisdiction_trust_domain_id IS NOT NULL AND jurisdiction_solution_id IS NULL AND jurisdiction_instance_id IS NULL AND jurisdiction_tenant_id IS NULL AND jurisdiction_community_id IS NULL AND jurisdiction_resource_id IS NULL) OR (jurisdiction_kind = 2 AND jurisdiction_trust_domain_id IS NOT NULL AND jurisdiction_solution_id IS NOT NULL AND jurisdiction_instance_id IS NULL AND jurisdiction_tenant_id IS NULL AND jurisdiction_community_id IS NULL AND jurisdiction_resource_id IS NULL) OR (jurisdiction_kind = 3 AND jurisdiction_trust_domain_id IS NOT NULL AND jurisdiction_solution_id IS NOT NULL AND jurisdiction_instance_id IS NOT NULL AND jurisdiction_tenant_id IS NULL AND jurisdiction_community_id IS NULL AND jurisdiction_resource_id IS NULL) OR (jurisdiction_kind = 4 AND jurisdiction_trust_domain_id IS NOT NULL AND jurisdiction_solution_id IS NOT NULL AND jurisdiction_instance_id IS NOT NULL AND jurisdiction_tenant_id IS NOT NULL AND jurisdiction_community_id IS NULL AND jurisdiction_resource_id IS NULL) OR (jurisdiction_kind = 5 AND jurisdiction_trust_domain_id IS NOT NULL AND jurisdiction_solution_id IS NOT NULL AND jurisdiction_instance_id IS NOT NULL AND jurisdiction_tenant_id IS NOT NULL AND jurisdiction_community_id IS NOT NULL AND jurisdiction_resource_id IS NULL) OR (jurisdiction_kind = 6 AND jurisdiction_trust_domain_id IS NOT NULL AND jurisdiction_solution_id IS NOT NULL AND jurisdiction_instance_id IS NOT NULL AND jurisdiction_tenant_id IS NOT NULL AND jurisdiction_resource_id IS NOT NULL))");
                    table.CheckConstraint("ck_moderation_policy_jurisdiction", "jurisdiction_id = CASE jurisdiction_kind WHEN 1 THEN jurisdiction_trust_domain_id WHEN 2 THEN jurisdiction_solution_id WHEN 3 THEN jurisdiction_instance_id WHEN 4 THEN jurisdiction_tenant_id WHEN 5 THEN jurisdiction_community_id WHEN 6 THEN jurisdiction_resource_id END");
                    table.CheckConstraint("ck_moderation_policy_revision", "revision > 0 AND rule_count > 0");
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_reviewer_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    case_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    case_tenant_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    reviewer_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    subject_human_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    assigned_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    controller_human_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    identity_evidence_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    qualified_by_human_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    qualified_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    conflict_state = table.Column<int>(type: "INTEGER", nullable: false),
                    conflict_evidence_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    assessed_by_human_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    withdrawn_by_human_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_reviewer_assignments", x => x.id);
                    table.CheckConstraint("ck_moderation_reviewer_conflict", "(conflict_state = 1 AND conflict_evidence_id IS NULL AND assessed_by_human_id IS NULL) OR (conflict_state IN (2, 3) AND controller_human_id IS NOT NULL AND conflict_evidence_id IS NOT NULL AND assessed_by_human_id IS NOT NULL AND assessed_by_human_id <> reviewer_human_id AND assessed_by_human_id <> controller_human_id AND (subject_human_id IS NULL OR assessed_by_human_id <> subject_human_id))");
                    table.CheckConstraint("ck_moderation_reviewer_controller", "(controller_human_id IS NULL AND identity_evidence_id IS NULL AND qualified_by_human_id IS NULL AND qualified_until IS NULL) OR (controller_human_id IS NOT NULL AND identity_evidence_id IS NOT NULL AND qualified_by_human_id IS NOT NULL AND qualified_until IS NOT NULL AND qualified_until > created_at AND (subject_human_id IS NULL OR controller_human_id <> subject_human_id))");
                    table.CheckConstraint("ck_moderation_reviewer_revision", "revision > 0");
                    table.CheckConstraint("ck_moderation_reviewer_subject", "subject_human_id IS NULL OR reviewer_human_id <> subject_human_id");
                    table.CheckConstraint("ck_moderation_reviewer_withdrawal", "(withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR (withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at = updated_at AND withdrawn_at >= created_at)");
                    table.ForeignKey(
                        name: "fk_moderation_reviewer_assignments_moderation_cases_case_id",
                        column: x => x.case_id,
                        principalTable: "ie_moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_policy_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    policy_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    policy_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    rule_owner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    rule_code = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    rule_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    meaning = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    purpose_mask = table.Column<int>(type: "INTEGER", nullable: false),
                    capability_mask = table.Column<int>(type: "INTEGER", nullable: false),
                    active_capability_mask = table.Column<int>(type: "INTEGER", nullable: false),
                    maximum_finite_duration_ticks = table.Column<long>(type: "INTEGER", nullable: false),
                    allows_indefinite = table.Column<bool>(type: "INTEGER", nullable: false),
                    maximum_review_delay_ticks = table.Column<long>(type: "INTEGER", nullable: false),
                    minimum_approvals = table.Column<int>(type: "INTEGER", nullable: false),
                    maximum_approvals = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_policy_rules", x => new { x.policy_id, x.id });
                    table.CheckConstraint("ck_moderation_policy_rule_effect", "capability_mask > 0 AND (capability_mask & 511) = capability_mask AND (capability_mask & 1) = 0 AND active_capability_mask >= 0 AND (active_capability_mask & capability_mask) = active_capability_mask");
                    table.CheckConstraint("ck_moderation_policy_rule_purpose", "purpose_mask > 0 AND (purpose_mask & 14) = purpose_mask");
                    table.CheckConstraint("ck_moderation_policy_rule_revision", "policy_revision > 0 AND rule_revision > 0");
                    table.CheckConstraint("ck_moderation_policy_rule_terms", "maximum_finite_duration_ticks > 0 AND maximum_review_delay_ticks > 0 AND minimum_approvals IN (1, 2) AND maximum_approvals IN (1, 2) AND maximum_approvals >= minimum_approvals");
                    table.ForeignKey(
                        name: "fk_moderation_policy_rules_moderation_policies_policy_id",
                        column: x => x.policy_id,
                        principalTable: "ie_moderation_policies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_findings_case_id_rule_owner_id_rule_code_rule_revision",
                table: "ie_moderation_findings",
                columns: new[] { "case_id", "rule_owner_id", "rule_code", "rule_revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_findings_scope_trust_domain_id_subject_kind_subject_id_scope_tenant_id",
                table: "ie_moderation_findings",
                columns: new[] { "scope_trust_domain_id", "subject_kind", "subject_id", "scope_tenant_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_policies_jurisdiction_kind_jurisdiction_id",
                table: "ie_moderation_policies",
                columns: new[] { "jurisdiction_kind", "jurisdiction_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_policy_rules_policy_id_rule_owner_id_rule_code_rule_revision",
                table: "ie_moderation_policy_rules",
                columns: new[] { "policy_id", "rule_owner_id", "rule_code", "rule_revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_reviewer_assignments_case_id",
                table: "ie_moderation_reviewer_assignments",
                column: "case_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_reviewer_assignments_trust_domain_id_case_tenant_id_case_id_reviewer_human_id",
                table: "ie_moderation_reviewer_assignments",
                columns: new[] { "trust_domain_id", "case_tenant_id", "case_id", "reviewer_human_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ie_moderation_findings");

            migrationBuilder.DropTable(
                name: "ie_moderation_policy_rules");

            migrationBuilder.DropTable(
                name: "ie_moderation_reviewer_assignments");

            migrationBuilder.DropTable(
                name: "ie_moderation_policies");
        }
    }
}

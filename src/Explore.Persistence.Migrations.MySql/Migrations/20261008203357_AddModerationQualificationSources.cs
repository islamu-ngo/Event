using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.MySql.Migrations
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
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    case_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    case_occurrence_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    case_revision = table.Column<long>(type: "bigint", nullable: false),
                    case_created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    subject_kind = table.Column<int>(type: "int", nullable: false),
                    subject_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    subject_origin_kind = table.Column<int>(type: "int", nullable: false),
                    subject_origin_trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    subject_origin_solution_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    subject_origin_instance_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    subject_origin_tenant_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    subject_origin_community_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    subject_origin_resource_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    scope_kind = table.Column<int>(type: "int", nullable: false),
                    scope_trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    scope_solution_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    scope_instance_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    scope_tenant_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    scope_community_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    scope_resource_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    rule_owner_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    rule_code = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    rule_revision = table.Column<long>(type: "bigint", nullable: false),
                    adjudicator_kind = table.Column<int>(type: "int", nullable: false),
                    adjudicator_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    adjudicator_origin_kind = table.Column<int>(type: "int", nullable: false),
                    adjudicator_origin_trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    adjudicator_origin_solution_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    adjudicator_origin_instance_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    adjudicator_origin_tenant_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    adjudicator_origin_community_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    adjudicator_origin_resource_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    evidence_purpose = table.Column<int>(type: "int", nullable: false),
                    evidence_reference_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    urgency_qualification_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    evidence_recorded_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    justification = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    finalized_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    state = table.Column<int>(type: "int", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    reversal_reference_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    reversed_by_kind = table.Column<int>(type: "int", nullable: true),
                    reversed_by_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    reversed_by_origin_kind = table.Column<int>(type: "int", nullable: true),
                    reversed_by_origin_trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    reversed_by_origin_solution_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    reversed_by_origin_instance_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    reversed_by_origin_tenant_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    reversed_by_origin_community_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    reversed_by_origin_resource_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    reversal_justification = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reversed_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    jurisdiction_kind = table.Column<int>(type: "int", nullable: false),
                    jurisdiction_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    jurisdiction_trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    jurisdiction_solution_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    jurisdiction_instance_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    jurisdiction_tenant_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    jurisdiction_community_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    jurisdiction_resource_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    publication_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    rule_count = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_policies", x => x.id);
                    table.CheckConstraint("ck_moderation_policies_jurisdiction_scope", "jurisdiction_kind IS NOT NULL AND ((jurisdiction_kind = 1 AND jurisdiction_trust_domain_id IS NOT NULL AND jurisdiction_solution_id IS NULL AND jurisdiction_instance_id IS NULL AND jurisdiction_tenant_id IS NULL AND jurisdiction_community_id IS NULL AND jurisdiction_resource_id IS NULL) OR (jurisdiction_kind = 2 AND jurisdiction_trust_domain_id IS NOT NULL AND jurisdiction_solution_id IS NOT NULL AND jurisdiction_instance_id IS NULL AND jurisdiction_tenant_id IS NULL AND jurisdiction_community_id IS NULL AND jurisdiction_resource_id IS NULL) OR (jurisdiction_kind = 3 AND jurisdiction_trust_domain_id IS NOT NULL AND jurisdiction_solution_id IS NOT NULL AND jurisdiction_instance_id IS NOT NULL AND jurisdiction_tenant_id IS NULL AND jurisdiction_community_id IS NULL AND jurisdiction_resource_id IS NULL) OR (jurisdiction_kind = 4 AND jurisdiction_trust_domain_id IS NOT NULL AND jurisdiction_solution_id IS NOT NULL AND jurisdiction_instance_id IS NOT NULL AND jurisdiction_tenant_id IS NOT NULL AND jurisdiction_community_id IS NULL AND jurisdiction_resource_id IS NULL) OR (jurisdiction_kind = 5 AND jurisdiction_trust_domain_id IS NOT NULL AND jurisdiction_solution_id IS NOT NULL AND jurisdiction_instance_id IS NOT NULL AND jurisdiction_tenant_id IS NOT NULL AND jurisdiction_community_id IS NOT NULL AND jurisdiction_resource_id IS NULL) OR (jurisdiction_kind = 6 AND jurisdiction_trust_domain_id IS NOT NULL AND jurisdiction_solution_id IS NOT NULL AND jurisdiction_instance_id IS NOT NULL AND jurisdiction_tenant_id IS NOT NULL AND jurisdiction_resource_id IS NOT NULL))");
                    table.CheckConstraint("ck_moderation_policy_jurisdiction", "jurisdiction_id = CASE jurisdiction_kind WHEN 1 THEN jurisdiction_trust_domain_id WHEN 2 THEN jurisdiction_solution_id WHEN 3 THEN jurisdiction_instance_id WHEN 4 THEN jurisdiction_tenant_id WHEN 5 THEN jurisdiction_community_id WHEN 6 THEN jurisdiction_resource_id END");
                    table.CheckConstraint("ck_moderation_policy_revision", "revision > 0 AND rule_count > 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_reviewer_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    case_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    case_tenant_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    reviewer_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    subject_human_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    assigned_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    controller_human_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    identity_evidence_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    qualified_by_human_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    qualified_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    conflict_state = table.Column<int>(type: "int", nullable: false),
                    conflict_evidence_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    assessed_by_human_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    withdrawn_by_human_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false)
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
                        name: "fk_ie_moderation_reviewer_assignments_ie_moderation_cas_e23c44c7",
                        column: x => x.case_id,
                        principalTable: "ie_moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_policy_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    policy_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    rule_owner_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    rule_code = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    rule_revision = table.Column<long>(type: "bigint", nullable: false),
                    meaning = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    purpose_mask = table.Column<int>(type: "int", nullable: false),
                    capability_mask = table.Column<int>(type: "int", nullable: false),
                    active_capability_mask = table.Column<int>(type: "int", nullable: false),
                    maximum_finite_duration_ticks = table.Column<long>(type: "bigint", nullable: false),
                    allows_indefinite = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    maximum_review_delay_ticks = table.Column<long>(type: "bigint", nullable: false),
                    minimum_approvals = table.Column<int>(type: "int", nullable: false),
                    maximum_approvals = table.Column<int>(type: "int", nullable: false)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_findings_case_id_rule_owner_id_rule_co_a3f515ca",
                table: "ie_moderation_findings",
                columns: new[] { "case_id", "rule_owner_id", "rule_code", "rule_revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_findings_scope_trust_domain_id_subject_e7df19ce",
                table: "ie_moderation_findings",
                columns: new[] { "scope_trust_domain_id", "subject_kind", "subject_id", "scope_tenant_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_policies_jurisdiction_kind_jurisdiction_id",
                table: "ie_moderation_policies",
                columns: new[] { "jurisdiction_kind", "jurisdiction_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_policy_rules_policy_id_rule_owner_id_r_faa8da3b",
                table: "ie_moderation_policy_rules",
                columns: new[] { "policy_id", "rule_owner_id", "rule_code", "rule_revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_reviewer_assignments_trust_domain_id_c_1f78b2c4",
                table: "ie_moderation_reviewer_assignments",
                columns: new[] { "trust_domain_id", "case_tenant_id", "case_id", "reviewer_human_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_reviewer_assignments_case_id",
                table: "ie_moderation_reviewer_assignments",
                column: "case_id");
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

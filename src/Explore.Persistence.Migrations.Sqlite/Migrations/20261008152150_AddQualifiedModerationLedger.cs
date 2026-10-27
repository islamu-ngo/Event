using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddQualifiedModerationLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ie_moderation_cases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
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
                    occurrence_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    state = table.Column<int>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_cases", x => x.id);
                    table.CheckConstraint("ck_moderation_case_state", "state IN (1, 2)");
                    table.CheckConstraint("ck_moderation_cases_revision", "revision > 0");
                    table.CheckConstraint("ck_moderation_cases_scope_scope", "scope_kind IS NOT NULL AND ((scope_kind = 1 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 2 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 3 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 4 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 5 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NOT NULL AND scope_resource_id IS NULL) OR (scope_kind = 6 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_resource_id IS NOT NULL))");
                    table.CheckConstraint("ck_moderation_cases_subject_origin_scope", "subject_origin_kind IS NOT NULL AND ((subject_origin_kind = 1 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 2 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 3 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 4 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 5 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NOT NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 6 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_resource_id IS NOT NULL))");
                    table.CheckConstraint("ck_moderation_cases_subject_subject", "subject_kind BETWEEN 1 AND 4");
                    table.CheckConstraint("ck_moderation_cases_subject_trust", "subject_origin_trust_domain_id = scope_trust_domain_id");
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_protective_measures",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    subject_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    subject_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    subject_origin_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    subject_origin_trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    subject_origin_solution_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    subject_origin_instance_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    subject_origin_tenant_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    subject_origin_community_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    subject_origin_resource_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    protector_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    protector_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    protector_origin_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    protector_origin_trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    protector_origin_solution_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    protector_origin_instance_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    protector_origin_tenant_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    protector_origin_community_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    protector_origin_resource_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    issuing_scope_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    issuing_scope_trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    issuing_scope_solution_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    issuing_scope_instance_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    issuing_scope_tenant_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    issuing_scope_community_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    issuing_scope_resource_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    scope_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    scope_trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    scope_solution_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    scope_instance_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    scope_tenant_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    scope_community_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    scope_resource_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    reviewer_kind = table.Column<int>(type: "INTEGER", nullable: true),
                    reviewer_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    reviewer_origin_kind = table.Column<int>(type: "INTEGER", nullable: true),
                    reviewer_origin_trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    reviewer_origin_solution_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    reviewer_origin_instance_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    reviewer_origin_tenant_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    reviewer_origin_community_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    reviewer_origin_resource_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    capability_mask = table.Column<int>(type: "INTEGER", nullable: false),
                    grounds = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    review_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_protective_measures", x => x.id);
                    table.CheckConstraint("ck_moderation_protection_capability_mask", "capability_mask > 0 AND (capability_mask & 511) = capability_mask AND (capability_mask & 1) = 0");
                    table.CheckConstraint("ck_moderation_protection_interval", "ends_at > starts_at AND review_at > starts_at AND review_at <= ends_at AND ((reviewed_at IS NULL AND reviewer_id IS NULL) OR (reviewed_at IS NOT NULL AND reviewer_id IS NOT NULL AND reviewed_at >= starts_at AND reviewed_at < ends_at))");
                    table.CheckConstraint("ck_moderation_protective_measures_issuing_scope_scope", "issuing_scope_kind IS NOT NULL AND ((issuing_scope_kind = 1 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NULL AND issuing_scope_instance_id IS NULL AND issuing_scope_tenant_id IS NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL) OR (issuing_scope_kind = 2 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NULL AND issuing_scope_tenant_id IS NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL) OR (issuing_scope_kind = 3 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL) OR (issuing_scope_kind = 4 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NOT NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL) OR (issuing_scope_kind = 5 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NOT NULL AND issuing_scope_community_id IS NOT NULL AND issuing_scope_resource_id IS NULL) OR (issuing_scope_kind = 6 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NOT NULL AND issuing_scope_resource_id IS NOT NULL))");
                    table.CheckConstraint("ck_moderation_protective_measures_protector_origin_scope", "protector_origin_kind IS NOT NULL AND ((protector_origin_kind = 1 AND protector_origin_trust_domain_id IS NOT NULL AND protector_origin_solution_id IS NULL AND protector_origin_instance_id IS NULL AND protector_origin_tenant_id IS NULL AND protector_origin_community_id IS NULL AND protector_origin_resource_id IS NULL) OR (protector_origin_kind = 2 AND protector_origin_trust_domain_id IS NOT NULL AND protector_origin_solution_id IS NOT NULL AND protector_origin_instance_id IS NULL AND protector_origin_tenant_id IS NULL AND protector_origin_community_id IS NULL AND protector_origin_resource_id IS NULL) OR (protector_origin_kind = 3 AND protector_origin_trust_domain_id IS NOT NULL AND protector_origin_solution_id IS NOT NULL AND protector_origin_instance_id IS NOT NULL AND protector_origin_tenant_id IS NULL AND protector_origin_community_id IS NULL AND protector_origin_resource_id IS NULL) OR (protector_origin_kind = 4 AND protector_origin_trust_domain_id IS NOT NULL AND protector_origin_solution_id IS NOT NULL AND protector_origin_instance_id IS NOT NULL AND protector_origin_tenant_id IS NOT NULL AND protector_origin_community_id IS NULL AND protector_origin_resource_id IS NULL) OR (protector_origin_kind = 5 AND protector_origin_trust_domain_id IS NOT NULL AND protector_origin_solution_id IS NOT NULL AND protector_origin_instance_id IS NOT NULL AND protector_origin_tenant_id IS NOT NULL AND protector_origin_community_id IS NOT NULL AND protector_origin_resource_id IS NULL) OR (protector_origin_kind = 6 AND protector_origin_trust_domain_id IS NOT NULL AND protector_origin_solution_id IS NOT NULL AND protector_origin_instance_id IS NOT NULL AND protector_origin_tenant_id IS NOT NULL AND protector_origin_resource_id IS NOT NULL))");
                    table.CheckConstraint("ck_moderation_protective_measures_protector_subject", "protector_kind BETWEEN 1 AND 4");
                    table.CheckConstraint("ck_moderation_protective_measures_reviewer_origin_scope", "(reviewer_origin_kind IS NULL AND reviewer_origin_trust_domain_id IS NULL AND reviewer_origin_solution_id IS NULL AND reviewer_origin_instance_id IS NULL AND reviewer_origin_tenant_id IS NULL AND reviewer_origin_community_id IS NULL AND reviewer_origin_resource_id IS NULL) OR (reviewer_origin_kind IS NOT NULL AND ((reviewer_origin_kind = 1 AND reviewer_origin_trust_domain_id IS NOT NULL AND reviewer_origin_solution_id IS NULL AND reviewer_origin_instance_id IS NULL AND reviewer_origin_tenant_id IS NULL AND reviewer_origin_community_id IS NULL AND reviewer_origin_resource_id IS NULL) OR (reviewer_origin_kind = 2 AND reviewer_origin_trust_domain_id IS NOT NULL AND reviewer_origin_solution_id IS NOT NULL AND reviewer_origin_instance_id IS NULL AND reviewer_origin_tenant_id IS NULL AND reviewer_origin_community_id IS NULL AND reviewer_origin_resource_id IS NULL) OR (reviewer_origin_kind = 3 AND reviewer_origin_trust_domain_id IS NOT NULL AND reviewer_origin_solution_id IS NOT NULL AND reviewer_origin_instance_id IS NOT NULL AND reviewer_origin_tenant_id IS NULL AND reviewer_origin_community_id IS NULL AND reviewer_origin_resource_id IS NULL) OR (reviewer_origin_kind = 4 AND reviewer_origin_trust_domain_id IS NOT NULL AND reviewer_origin_solution_id IS NOT NULL AND reviewer_origin_instance_id IS NOT NULL AND reviewer_origin_tenant_id IS NOT NULL AND reviewer_origin_community_id IS NULL AND reviewer_origin_resource_id IS NULL) OR (reviewer_origin_kind = 5 AND reviewer_origin_trust_domain_id IS NOT NULL AND reviewer_origin_solution_id IS NOT NULL AND reviewer_origin_instance_id IS NOT NULL AND reviewer_origin_tenant_id IS NOT NULL AND reviewer_origin_community_id IS NOT NULL AND reviewer_origin_resource_id IS NULL) OR (reviewer_origin_kind = 6 AND reviewer_origin_trust_domain_id IS NOT NULL AND reviewer_origin_solution_id IS NOT NULL AND reviewer_origin_instance_id IS NOT NULL AND reviewer_origin_tenant_id IS NOT NULL AND reviewer_origin_resource_id IS NOT NULL)))");
                    table.CheckConstraint("ck_moderation_protective_measures_reviewer_subject", "(reviewer_kind IS NULL AND reviewer_id IS NULL AND reviewer_origin_kind IS NULL) OR (reviewer_kind IS NOT NULL AND reviewer_kind BETWEEN 1 AND 4 AND reviewer_id IS NOT NULL AND reviewer_origin_kind IS NOT NULL)");
                    table.CheckConstraint("ck_moderation_protective_measures_revision", "revision > 0");
                    table.CheckConstraint("ck_moderation_protective_measures_scope_scope", "scope_kind IS NOT NULL AND ((scope_kind = 1 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 2 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 3 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 4 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 5 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NOT NULL AND scope_resource_id IS NULL) OR (scope_kind = 6 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_resource_id IS NOT NULL))");
                    table.CheckConstraint("ck_moderation_protective_measures_subject_origin_scope", "subject_origin_kind IS NOT NULL AND ((subject_origin_kind = 1 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 2 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 3 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 4 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 5 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NOT NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 6 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_resource_id IS NOT NULL))");
                    table.CheckConstraint("ck_moderation_protective_measures_subject_subject", "subject_kind BETWEEN 1 AND 4");
                    table.CheckConstraint("ck_moderation_protective_measures_subject_trust", "subject_origin_trust_domain_id = scope_trust_domain_id");
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    case_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    subject_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    subject_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    subject_origin_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    subject_origin_trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    subject_origin_solution_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    subject_origin_instance_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    subject_origin_tenant_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    subject_origin_community_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    subject_origin_resource_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    issuer_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    issuer_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    issuer_origin_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    issuer_origin_trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    issuer_origin_solution_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    issuer_origin_instance_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    issuer_origin_tenant_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    issuer_origin_community_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    issuer_origin_resource_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    issuing_scope_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    issuing_scope_trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    issuing_scope_solution_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    issuing_scope_instance_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    issuing_scope_tenant_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    issuing_scope_community_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    issuing_scope_resource_id = table.Column<Guid>(type: "TEXT", nullable: true),
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
                    capability_mask = table.Column<int>(type: "INTEGER", nullable: false),
                    approval1_kind = table.Column<int>(type: "INTEGER", nullable: true),
                    approval1_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    approval1_origin_kind = table.Column<int>(type: "INTEGER", nullable: true),
                    approval1_origin_trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    approval1_origin_solution_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    approval1_origin_instance_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    approval1_origin_tenant_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    approval1_origin_community_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    approval1_origin_resource_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    approval2_kind = table.Column<int>(type: "INTEGER", nullable: true),
                    approval2_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    approval2_origin_kind = table.Column<int>(type: "INTEGER", nullable: true),
                    approval2_origin_trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    approval2_origin_solution_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    approval2_origin_instance_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    approval2_origin_tenant_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    approval2_origin_community_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    approval2_origin_resource_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    starts_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    review_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    required_approvals = table.Column<int>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    finding_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    state = table.Column<int>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_decisions", x => x.id);
                    table.CheckConstraint("ck_moderation_decision_approval_independence", "(approval1_id IS NULL OR (approval1_kind = 1 AND approval1_id <> issuer_id AND approval1_id <> subject_id)) AND (approval2_id IS NULL OR (approval2_kind = 1 AND approval2_id <> issuer_id AND approval2_id <> subject_id))");
                    table.CheckConstraint("ck_moderation_decision_approval_order", "approval2_id IS NULL OR (approval1_id IS NOT NULL AND approval2_id <> approval1_id)");
                    table.CheckConstraint("ck_moderation_decision_capability_mask", "capability_mask > 0 AND (capability_mask & 511) = capability_mask AND (capability_mask & 1) = 0");
                    table.CheckConstraint("ck_moderation_decision_interval", "(ends_at IS NULL OR ends_at > starts_at) AND (ends_at IS NOT NULL OR review_at IS NOT NULL) AND (review_at IS NULL OR (review_at > starts_at AND (ends_at IS NULL OR review_at <= ends_at)))");
                    table.CheckConstraint("ck_moderation_decision_quorum", "required_approvals IN (1, 2) AND (scope_kind NOT IN (1, 2) OR required_approvals = 2) AND (ends_at IS NOT NULL OR required_approvals = 2)");
                    table.CheckConstraint("ck_moderation_decision_rule_revision", "rule_revision > 0");
                    table.CheckConstraint("ck_moderation_decision_state", "state BETWEEN 1 AND 8");
                    table.CheckConstraint("ck_moderation_decisions_approval1_origin_scope", "(approval1_origin_kind IS NULL AND approval1_origin_trust_domain_id IS NULL AND approval1_origin_solution_id IS NULL AND approval1_origin_instance_id IS NULL AND approval1_origin_tenant_id IS NULL AND approval1_origin_community_id IS NULL AND approval1_origin_resource_id IS NULL) OR (approval1_origin_kind IS NOT NULL AND ((approval1_origin_kind = 1 AND approval1_origin_trust_domain_id IS NOT NULL AND approval1_origin_solution_id IS NULL AND approval1_origin_instance_id IS NULL AND approval1_origin_tenant_id IS NULL AND approval1_origin_community_id IS NULL AND approval1_origin_resource_id IS NULL) OR (approval1_origin_kind = 2 AND approval1_origin_trust_domain_id IS NOT NULL AND approval1_origin_solution_id IS NOT NULL AND approval1_origin_instance_id IS NULL AND approval1_origin_tenant_id IS NULL AND approval1_origin_community_id IS NULL AND approval1_origin_resource_id IS NULL) OR (approval1_origin_kind = 3 AND approval1_origin_trust_domain_id IS NOT NULL AND approval1_origin_solution_id IS NOT NULL AND approval1_origin_instance_id IS NOT NULL AND approval1_origin_tenant_id IS NULL AND approval1_origin_community_id IS NULL AND approval1_origin_resource_id IS NULL) OR (approval1_origin_kind = 4 AND approval1_origin_trust_domain_id IS NOT NULL AND approval1_origin_solution_id IS NOT NULL AND approval1_origin_instance_id IS NOT NULL AND approval1_origin_tenant_id IS NOT NULL AND approval1_origin_community_id IS NULL AND approval1_origin_resource_id IS NULL) OR (approval1_origin_kind = 5 AND approval1_origin_trust_domain_id IS NOT NULL AND approval1_origin_solution_id IS NOT NULL AND approval1_origin_instance_id IS NOT NULL AND approval1_origin_tenant_id IS NOT NULL AND approval1_origin_community_id IS NOT NULL AND approval1_origin_resource_id IS NULL) OR (approval1_origin_kind = 6 AND approval1_origin_trust_domain_id IS NOT NULL AND approval1_origin_solution_id IS NOT NULL AND approval1_origin_instance_id IS NOT NULL AND approval1_origin_tenant_id IS NOT NULL AND approval1_origin_resource_id IS NOT NULL)))");
                    table.CheckConstraint("ck_moderation_decisions_approval1_subject", "(approval1_kind IS NULL AND approval1_id IS NULL AND approval1_origin_kind IS NULL) OR (approval1_kind IS NOT NULL AND approval1_kind BETWEEN 1 AND 4 AND approval1_id IS NOT NULL AND approval1_origin_kind IS NOT NULL)");
                    table.CheckConstraint("ck_moderation_decisions_approval2_origin_scope", "(approval2_origin_kind IS NULL AND approval2_origin_trust_domain_id IS NULL AND approval2_origin_solution_id IS NULL AND approval2_origin_instance_id IS NULL AND approval2_origin_tenant_id IS NULL AND approval2_origin_community_id IS NULL AND approval2_origin_resource_id IS NULL) OR (approval2_origin_kind IS NOT NULL AND ((approval2_origin_kind = 1 AND approval2_origin_trust_domain_id IS NOT NULL AND approval2_origin_solution_id IS NULL AND approval2_origin_instance_id IS NULL AND approval2_origin_tenant_id IS NULL AND approval2_origin_community_id IS NULL AND approval2_origin_resource_id IS NULL) OR (approval2_origin_kind = 2 AND approval2_origin_trust_domain_id IS NOT NULL AND approval2_origin_solution_id IS NOT NULL AND approval2_origin_instance_id IS NULL AND approval2_origin_tenant_id IS NULL AND approval2_origin_community_id IS NULL AND approval2_origin_resource_id IS NULL) OR (approval2_origin_kind = 3 AND approval2_origin_trust_domain_id IS NOT NULL AND approval2_origin_solution_id IS NOT NULL AND approval2_origin_instance_id IS NOT NULL AND approval2_origin_tenant_id IS NULL AND approval2_origin_community_id IS NULL AND approval2_origin_resource_id IS NULL) OR (approval2_origin_kind = 4 AND approval2_origin_trust_domain_id IS NOT NULL AND approval2_origin_solution_id IS NOT NULL AND approval2_origin_instance_id IS NOT NULL AND approval2_origin_tenant_id IS NOT NULL AND approval2_origin_community_id IS NULL AND approval2_origin_resource_id IS NULL) OR (approval2_origin_kind = 5 AND approval2_origin_trust_domain_id IS NOT NULL AND approval2_origin_solution_id IS NOT NULL AND approval2_origin_instance_id IS NOT NULL AND approval2_origin_tenant_id IS NOT NULL AND approval2_origin_community_id IS NOT NULL AND approval2_origin_resource_id IS NULL) OR (approval2_origin_kind = 6 AND approval2_origin_trust_domain_id IS NOT NULL AND approval2_origin_solution_id IS NOT NULL AND approval2_origin_instance_id IS NOT NULL AND approval2_origin_tenant_id IS NOT NULL AND approval2_origin_resource_id IS NOT NULL)))");
                    table.CheckConstraint("ck_moderation_decisions_approval2_subject", "(approval2_kind IS NULL AND approval2_id IS NULL AND approval2_origin_kind IS NULL) OR (approval2_kind IS NOT NULL AND approval2_kind BETWEEN 1 AND 4 AND approval2_id IS NOT NULL AND approval2_origin_kind IS NOT NULL)");
                    table.CheckConstraint("ck_moderation_decisions_issuer_origin_scope", "issuer_origin_kind IS NOT NULL AND ((issuer_origin_kind = 1 AND issuer_origin_trust_domain_id IS NOT NULL AND issuer_origin_solution_id IS NULL AND issuer_origin_instance_id IS NULL AND issuer_origin_tenant_id IS NULL AND issuer_origin_community_id IS NULL AND issuer_origin_resource_id IS NULL) OR (issuer_origin_kind = 2 AND issuer_origin_trust_domain_id IS NOT NULL AND issuer_origin_solution_id IS NOT NULL AND issuer_origin_instance_id IS NULL AND issuer_origin_tenant_id IS NULL AND issuer_origin_community_id IS NULL AND issuer_origin_resource_id IS NULL) OR (issuer_origin_kind = 3 AND issuer_origin_trust_domain_id IS NOT NULL AND issuer_origin_solution_id IS NOT NULL AND issuer_origin_instance_id IS NOT NULL AND issuer_origin_tenant_id IS NULL AND issuer_origin_community_id IS NULL AND issuer_origin_resource_id IS NULL) OR (issuer_origin_kind = 4 AND issuer_origin_trust_domain_id IS NOT NULL AND issuer_origin_solution_id IS NOT NULL AND issuer_origin_instance_id IS NOT NULL AND issuer_origin_tenant_id IS NOT NULL AND issuer_origin_community_id IS NULL AND issuer_origin_resource_id IS NULL) OR (issuer_origin_kind = 5 AND issuer_origin_trust_domain_id IS NOT NULL AND issuer_origin_solution_id IS NOT NULL AND issuer_origin_instance_id IS NOT NULL AND issuer_origin_tenant_id IS NOT NULL AND issuer_origin_community_id IS NOT NULL AND issuer_origin_resource_id IS NULL) OR (issuer_origin_kind = 6 AND issuer_origin_trust_domain_id IS NOT NULL AND issuer_origin_solution_id IS NOT NULL AND issuer_origin_instance_id IS NOT NULL AND issuer_origin_tenant_id IS NOT NULL AND issuer_origin_resource_id IS NOT NULL))");
                    table.CheckConstraint("ck_moderation_decisions_issuer_subject", "issuer_kind BETWEEN 1 AND 4");
                    table.CheckConstraint("ck_moderation_decisions_issuing_scope_scope", "issuing_scope_kind IS NOT NULL AND ((issuing_scope_kind = 1 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NULL AND issuing_scope_instance_id IS NULL AND issuing_scope_tenant_id IS NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL) OR (issuing_scope_kind = 2 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NULL AND issuing_scope_tenant_id IS NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL) OR (issuing_scope_kind = 3 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL) OR (issuing_scope_kind = 4 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NOT NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL) OR (issuing_scope_kind = 5 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NOT NULL AND issuing_scope_community_id IS NOT NULL AND issuing_scope_resource_id IS NULL) OR (issuing_scope_kind = 6 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NOT NULL AND issuing_scope_resource_id IS NOT NULL))");
                    table.CheckConstraint("ck_moderation_decisions_revision", "revision > 0");
                    table.CheckConstraint("ck_moderation_decisions_scope_scope", "scope_kind IS NOT NULL AND ((scope_kind = 1 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 2 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 3 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 4 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 5 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NOT NULL AND scope_resource_id IS NULL) OR (scope_kind = 6 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_resource_id IS NOT NULL))");
                    table.CheckConstraint("ck_moderation_decisions_subject_origin_scope", "subject_origin_kind IS NOT NULL AND ((subject_origin_kind = 1 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 2 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 3 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 4 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 5 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NOT NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 6 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_resource_id IS NOT NULL))");
                    table.CheckConstraint("ck_moderation_decisions_subject_subject", "subject_kind BETWEEN 1 AND 4");
                    table.CheckConstraint("ck_moderation_decisions_subject_trust", "subject_origin_trust_domain_id = scope_trust_domain_id");
                    table.ForeignKey(
                        name: "fk_moderation_decisions_moderation_cases_case_id",
                        column: x => x.case_id,
                        principalTable: "ie_moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_operation_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    request_digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    scope_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    scope_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    subject_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    subject_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    principal_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    principal_digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    decision_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    sanction_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    result_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    policy_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    qualification_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_operation_receipts", x => x.id);
                    table.CheckConstraint("ck_moderation_receipt_revision", "result_revision > 0 AND policy_revision > 0 AND qualification_revision > 0");
                    table.CheckConstraint("ck_moderation_receipt_scope", "scope_kind BETWEEN 1 AND 6 AND subject_kind BETWEEN 1 AND 4");
                    table.ForeignKey(
                        name: "fk_moderation_operation_receipts_moderation_decisions_decision_id",
                        column: x => x.decision_id,
                        principalTable: "ie_moderation_decisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_sanctions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    decision_id = table.Column<Guid>(type: "TEXT", nullable: false),
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
                    capability_mask = table.Column<int>(type: "INTEGER", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    review_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    superseding_decision_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    state = table.Column<int>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_sanctions", x => x.id);
                    table.CheckConstraint("ck_moderation_sanction_capability_mask", "capability_mask > 0 AND (capability_mask & 511) = capability_mask AND (capability_mask & 1) = 0");
                    table.CheckConstraint("ck_moderation_sanction_interval", "(ends_at IS NULL OR ends_at > starts_at) AND (ends_at IS NOT NULL OR review_at IS NOT NULL) AND (review_at IS NULL OR (review_at > starts_at AND (ends_at IS NULL OR review_at <= ends_at)))");
                    table.CheckConstraint("ck_moderation_sanction_rule_revision", "rule_revision > 0");
                    table.CheckConstraint("ck_moderation_sanction_state", "state BETWEEN 1 AND 5");
                    table.CheckConstraint("ck_moderation_sanction_supersession", "(state = 5 AND superseding_decision_id IS NOT NULL AND superseding_decision_id <> decision_id) OR (state <> 5 AND superseding_decision_id IS NULL)");
                    table.CheckConstraint("ck_moderation_sanctions_revision", "revision > 0");
                    table.CheckConstraint("ck_moderation_sanctions_scope_scope", "scope_kind IS NOT NULL AND ((scope_kind = 1 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 2 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 3 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 4 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 5 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NOT NULL AND scope_resource_id IS NULL) OR (scope_kind = 6 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_resource_id IS NOT NULL))");
                    table.CheckConstraint("ck_moderation_sanctions_subject_origin_scope", "subject_origin_kind IS NOT NULL AND ((subject_origin_kind = 1 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 2 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 3 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 4 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 5 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NOT NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 6 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_resource_id IS NOT NULL))");
                    table.CheckConstraint("ck_moderation_sanctions_subject_subject", "subject_kind BETWEEN 1 AND 4");
                    table.CheckConstraint("ck_moderation_sanctions_subject_trust", "subject_origin_trust_domain_id = scope_trust_domain_id");
                    table.ForeignKey(
                        name: "fk_moderation_sanctions_moderation_decisions_decision_id",
                        column: x => x.decision_id,
                        principalTable: "ie_moderation_decisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_effect_intents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    operation_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    decision_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    decision_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    sanction_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    sanction_revision = table.Column<long>(type: "INTEGER", nullable: true),
                    superseded_sanction_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    superseded_sanction_revision = table.Column<long>(type: "INTEGER", nullable: true),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    scope_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    scope_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    subject_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    subject_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    operation_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    policy_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    qualification_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_effect_intents", x => x.id);
                    table.CheckConstraint("ck_moderation_effect_qualification", "policy_revision > 0 AND qualification_revision > 0");
                    table.CheckConstraint("ck_moderation_effect_revision", "revision > 0 AND decision_revision > 0");
                    table.CheckConstraint("ck_moderation_effect_sanction_pair", "(sanction_id IS NULL AND sanction_revision IS NULL) OR (sanction_id IS NOT NULL AND sanction_revision IS NOT NULL AND sanction_revision > 0)");
                    table.CheckConstraint("ck_moderation_effect_supersession", "(operation_kind <> 10 AND superseded_sanction_id IS NULL AND superseded_sanction_revision IS NULL) OR (operation_kind = 10 AND sanction_id IS NOT NULL AND superseded_sanction_id IS NOT NULL AND superseded_sanction_revision IS NOT NULL AND superseded_sanction_revision > 0 AND superseded_sanction_id <> sanction_id)");
                    table.ForeignKey(
                        name: "fk_moderation_effect_intents_moderation_decisions_decision_id",
                        column: x => x.decision_id,
                        principalTable: "ie_moderation_decisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_effect_intents_moderation_operation_receipts_operation_id",
                        column: x => x.operation_id,
                        principalTable: "ie_moderation_operation_receipts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_effect_intents_moderation_sanctions_superseded_sanction_id",
                        column: x => x.superseded_sanction_id,
                        principalTable: "ie_moderation_sanctions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_cases_scope_trust_domain_id_scope_tenant_id_occurrence_id",
                table: "ie_moderation_cases",
                columns: new[] { "scope_trust_domain_id", "scope_tenant_id", "occurrence_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_cases_scope_trust_domain_id_subject_kind_subject_id_scope_tenant_id",
                table: "ie_moderation_cases",
                columns: new[] { "scope_trust_domain_id", "subject_kind", "subject_id", "scope_tenant_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_decisions_case_id",
                table: "ie_moderation_decisions",
                column: "case_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_decisions_scope_trust_domain_id_subject_kind_subject_id_scope_tenant_id",
                table: "ie_moderation_decisions",
                columns: new[] { "scope_trust_domain_id", "subject_kind", "subject_id", "scope_tenant_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_effect_intents_completed_at_created_at",
                table: "ie_moderation_effect_intents",
                columns: new[] { "completed_at", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_effect_intents_decision_id",
                table: "ie_moderation_effect_intents",
                column: "decision_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_effect_intents_operation_id",
                table: "ie_moderation_effect_intents",
                column: "operation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_effect_intents_superseded_sanction_id",
                table: "ie_moderation_effect_intents",
                column: "superseded_sanction_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_operation_receipts_decision_id",
                table: "ie_moderation_operation_receipts",
                column: "decision_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_operation_receipts_trust_domain_id_scope_kind_scope_id_subject_kind_subject_id",
                table: "ie_moderation_operation_receipts",
                columns: new[] { "trust_domain_id", "scope_kind", "scope_id", "subject_kind", "subject_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_protective_measures_scope_trust_domain_id_subject_kind_subject_id_scope_tenant_id",
                table: "ie_moderation_protective_measures",
                columns: new[] { "scope_trust_domain_id", "subject_kind", "subject_id", "scope_tenant_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_sanctions_decision_id",
                table: "ie_moderation_sanctions",
                column: "decision_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_sanctions_scope_trust_domain_id_subject_kind_subject_id_scope_tenant_id",
                table: "ie_moderation_sanctions",
                columns: new[] { "scope_trust_domain_id", "subject_kind", "subject_id", "scope_tenant_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ie_moderation_effect_intents");

            migrationBuilder.DropTable(
                name: "ie_moderation_protective_measures");

            migrationBuilder.DropTable(
                name: "ie_moderation_operation_receipts");

            migrationBuilder.DropTable(
                name: "ie_moderation_sanctions");

            migrationBuilder.DropTable(
                name: "ie_moderation_decisions");

            migrationBuilder.DropTable(
                name: "ie_moderation_cases");
        }
    }
}

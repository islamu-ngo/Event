using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddNativeModerationSourceOwners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_sanctions_scope_scope",
                table: "ie_moderation_sanctions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_sanctions_subject_origin_scope",
                table: "ie_moderation_sanctions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_qualification_receipt_result",
                table: "ie_moderation_qualification_mutation_receipts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_protective_measures_issuing_scope_scope",
                table: "ie_moderation_protective_measures");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_protective_measures_protector_origin_scope",
                table: "ie_moderation_protective_measures");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_protective_measures_reviewer_origin_scope",
                table: "ie_moderation_protective_measures");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_protective_measures_scope_scope",
                table: "ie_moderation_protective_measures");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_protective_measures_subject_origin_scope",
                table: "ie_moderation_protective_measures");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_policies_jurisdiction_scope",
                table: "ie_moderation_policies");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_policy_jurisdiction",
                table: "ie_moderation_policies");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_receipt_scope",
                table: "ie_moderation_operation_receipts");

            migrationBuilder.DropIndex(
                name: "ix_moderation_findings_case_id_rule_owner_id_rule_code_rule_revision",
                table: "ie_moderation_findings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_finding_evidence",
                table: "ie_moderation_findings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_findings_adjudicator_origin_scope",
                table: "ie_moderation_findings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_findings_reversed_by_origin_scope",
                table: "ie_moderation_findings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_findings_scope_scope",
                table: "ie_moderation_findings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_findings_subject_origin_scope",
                table: "ie_moderation_findings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_decisions_approval1_origin_scope",
                table: "ie_moderation_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_decisions_approval2_origin_scope",
                table: "ie_moderation_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_decisions_issuer_origin_scope",
                table: "ie_moderation_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_decisions_issuing_scope_scope",
                table: "ie_moderation_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_decisions_scope_scope",
                table: "ie_moderation_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_decisions_subject_origin_scope",
                table: "ie_moderation_decisions");

            migrationBuilder.DropIndex(
                name: "ix_moderation_cases_scope_trust_domain_id_scope_tenant_id_occurrence_id",
                table: "ie_moderation_cases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_cases_scope_scope",
                table: "ie_moderation_cases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_cases_subject_origin_scope",
                table: "ie_moderation_cases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_cases_subject_subject",
                table: "ie_moderation_cases");

            migrationBuilder.DropIndex(
                name: "ix_event_report_signals_tenant_id_provider_provider_target_scope_provider_target_id_correlation_id",
                table: "ie_event_report_signals");

            migrationBuilder.DropIndex(
                name: "ix_event_report_signals_tenant_id_provider_provider_target_scope_provider_target_id_external_signal_id",
                table: "ie_event_report_signals");

            migrationBuilder.AddColumn<Guid>(
                name: "birth_date_collection_receipt_id",
                table: "ie_user_pii",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "birth_date_revision",
                table: "ie_user_pii",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateOnly>(
                name: "date_of_birth",
                table: "ie_user_pii",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "scope_event_id",
                table: "ie_moderation_sanctions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "subject_origin_event_id",
                table: "ie_moderation_sanctions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "issuing_scope_event_id",
                table: "ie_moderation_protective_measures",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "protector_origin_event_id",
                table: "ie_moderation_protective_measures",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reviewer_origin_event_id",
                table: "ie_moderation_protective_measures",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "scope_event_id",
                table: "ie_moderation_protective_measures",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "subject_origin_event_id",
                table: "ie_moderation_protective_measures",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "jurisdiction_event_id",
                table: "ie_moderation_policies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "adjudicator_origin_event_id",
                table: "ie_moderation_findings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "attribution_generation",
                table: "ie_moderation_findings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "attribution_source_id",
                table: "ie_moderation_findings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reason_code",
                table: "ie_moderation_findings",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "reason_owner_id",
                table: "ie_moderation_findings",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<long>(
                name: "reason_revision",
                table: "ie_moderation_findings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "reversed_by_origin_event_id",
                table: "ie_moderation_findings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "scope_event_id",
                table: "ie_moderation_findings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "scope_id",
                table: "ie_moderation_findings",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "subject_origin_event_id",
                table: "ie_moderation_findings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "lease_expires_at",
                table: "ie_moderation_effect_intents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "lease_id",
                table: "ie_moderation_effect_intents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "lease_started_at",
                table: "ie_moderation_effect_intents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "original_completion_host_binding_id",
                table: "ie_moderation_effect_intents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "original_completion_id",
                table: "ie_moderation_effect_intents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "original_completion_revision",
                table: "ie_moderation_effect_intents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "original_completion_writer_tenant_id",
                table: "ie_moderation_effect_intents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "original_notice_cleanup_id",
                table: "ie_moderation_effect_intents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "original_notice_host_binding_id",
                table: "ie_moderation_effect_intents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "original_notice_purged_at",
                table: "ie_moderation_effect_intents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "original_notice_retain_until",
                table: "ie_moderation_effect_intents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "original_notice_writer_tenant_id",
                table: "ie_moderation_effect_intents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "approval1_origin_event_id",
                table: "ie_moderation_decisions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "approval2_origin_event_id",
                table: "ie_moderation_decisions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "issuer_origin_event_id",
                table: "ie_moderation_decisions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "issuing_scope_event_id",
                table: "ie_moderation_decisions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "scope_event_id",
                table: "ie_moderation_decisions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "subject_origin_event_id",
                table: "ie_moderation_decisions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "subject_origin_trust_domain_id",
                table: "ie_moderation_cases",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<int>(
                name: "subject_origin_kind",
                table: "ie_moderation_cases",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<int>(
                name: "subject_kind",
                table: "ie_moderation_cases",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<Guid>(
                name: "subject_id",
                table: "ie_moderation_cases",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AddColumn<Guid>(
                name: "attribution_source_id",
                table: "ie_moderation_cases",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "occurrence_case_id",
                table: "ie_moderation_cases",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "occurrence_evidence_revision",
                table: "ie_moderation_cases",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "occurrence_owner_id",
                table: "ie_moderation_cases",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<int>(
                name: "occurrence_owner_kind",
                table: "ie_moderation_cases",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "scope_event_id",
                table: "ie_moderation_cases",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "subject_origin_event_id",
                table: "ie_moderation_cases",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "target_id",
                table: "ie_moderation_cases",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "target_kind",
                table: "ie_moderation_cases",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "target_origin_community_id",
                table: "ie_moderation_cases",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "target_origin_event_id",
                table: "ie_moderation_cases",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "target_origin_instance_id",
                table: "ie_moderation_cases",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "target_origin_kind",
                table: "ie_moderation_cases",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "target_origin_resource_id",
                table: "ie_moderation_cases",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "target_origin_solution_id",
                table: "ie_moderation_cases",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "target_origin_tenant_id",
                table: "ie_moderation_cases",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "target_origin_trust_domain_id",
                table: "ie_moderation_cases",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "target_version",
                table: "ie_moderation_cases",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "material_version_id",
                table: "ie_event_resources",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "moderation_quarantine_count",
                table: "ie_event_resources",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "advisory_identity",
                table: "ie_event_report_signals",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "advisory_material_sha256",
                table: "ie_event_report_signals",
                type: "TEXT",
                maxLength: 71,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "advisory_ordinal",
                table: "ie_event_report_signals",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "case_id",
                table: "ie_event_report_signals",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "incoming_webhook_message_id",
                table: "ie_event_report_signals",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "provider_link_id",
                table: "ie_event_report_signals",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "source_case_concurrency_stamp",
                table: "ie_event_report_signals",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "case_concurrency_stamp",
                table: "ie_event_report_external_links",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "moderation_reservation",
                table: "ie_configuration_import_sessions",
                type: "TEXT",
                maxLength: 1100000,
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_event_report_external_links_tenant_id_report_id_id",
                table: "ie_event_report_external_links",
                columns: new[] { "tenant_id", "report_id", "id" });

            migrationBuilder.CreateTable(
                name: "ie_key_obligation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    intent_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    key_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    key_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    verification_tag = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    capture_material_receipt = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    material_receipt = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    original_captured_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    original_retain_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    backup_horizon_ticks = table.Column<long>(type: "INTEGER", nullable: false),
                    backup_safety_margin_ticks = table.Column<long>(type: "INTEGER", nullable: false),
                    receipt_retention_ticks = table.Column<long>(type: "INTEGER", nullable: false),
                    readable_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    authority_sequence = table.Column<long>(type: "INTEGER", nullable: true),
                    material_pruned_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    receipt_retain_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_key_obligation", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ie_key_replica",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    runtime_replica_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    ring_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    ring_commitment = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    binding_revision = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    source = table.Column<int>(type: "INTEGER", nullable: false),
                    verified_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_key_replica", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_case_evidence",
                columns: table => new
                {
                    case_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    case_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_version = table.Column<Guid>(type: "TEXT", nullable: false),
                    material_fact_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_origin_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    source_origin_trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_origin_solution_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    source_origin_instance_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    source_origin_tenant_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    source_origin_community_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    source_origin_resource_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    source_origin_event_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_case_evidence", x => new { x.case_id, x.case_revision });
                    table.CheckConstraint("ck_moderation_case_evidence_origin", "source_origin_kind IN (3, 4) AND source_origin_solution_id IS NOT NULL AND source_origin_instance_id IS NOT NULL AND source_origin_community_id IS NULL AND source_origin_resource_id IS NULL AND source_origin_event_id IS NULL AND ((source_origin_kind = 3 AND source_origin_tenant_id IS NULL) OR (source_origin_kind = 4 AND source_origin_tenant_id IS NOT NULL))");
                    table.CheckConstraint("ck_moderation_case_evidence_revision", "case_revision > 0");
                    table.ForeignKey(
                        name: "fk_moderation_case_evidence_moderation_cases_case_id",
                        column: x => x.case_id,
                        principalTable: "ie_moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_case_narrative_policy_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    target_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    purpose = table.Column<int>(type: "INTEGER", nullable: false),
                    maximum_retention_ticks = table.Column<long>(type: "INTEGER", nullable: false),
                    maximum_material_bytes = table.Column<int>(type: "INTEGER", nullable: false),
                    privacy_owner_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    privacy_controller_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    governance_controller_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    identity_and_independence_basis = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    privacy_legal_mandate_basis = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    recorded_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_case_narrative_policy_sources", x => x.id);
                    table.UniqueConstraint("ak_moderation_case_narrative_policy_sources_id_host_binding_id_trust_domain_id_tenant_id", x => new { x.id, x.host_binding_id, x.trust_domain_id, x.tenant_id });
                    table.CheckConstraint("ck_case_narrative_policy_generation", "revision = 1 AND writer_generation > 0");
                    table.CheckConstraint("ck_case_narrative_policy_independence", "privacy_owner_human_id <> recorded_by_human_id AND privacy_controller_human_id <> governance_controller_human_id AND privacy_controller_human_id <> recorded_by_human_id AND privacy_owner_human_id <> governance_controller_human_id");
                    table.CheckConstraint("ck_case_narrative_policy_limits", "maximum_retention_ticks > 0 AND maximum_retention_ticks < 9223372036854775807 AND maximum_material_bytes > 0 AND maximum_material_bytes <= 65536 AND valid_until > recorded_at");
                    table.CheckConstraint("ck_case_narrative_policy_purpose", "purpose IN (1, 3) OR (purpose = 2 AND target_kind = 2)");
                    table.ForeignKey(
                        name: "fk_moderation_case_narrative_policy_sources_moderation_host_bindings_host_binding_id",
                        column: x => x.host_binding_id,
                        principalTable: "ie_moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_narrative_policy_sources_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_narrative_policy_sources_users_governance_controller_human_id",
                        column: x => x.governance_controller_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_narrative_policy_sources_users_privacy_controller_human_id",
                        column: x => x.privacy_controller_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_narrative_policy_sources_users_privacy_owner_human_id",
                        column: x => x.privacy_owner_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_narrative_policy_sources_users_recorded_by_human_id",
                        column: x => x.recorded_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_case_target_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    target_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    submission_lifetime_ticks = table.Column<long>(type: "INTEGER", nullable: false),
                    read_lifetime_ticks = table.Column<long>(type: "INTEGER", nullable: false),
                    enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    changed_by_user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_case_target_policies", x => x.id);
                    table.CheckConstraint("ck_moderation_target_policy_lifetime", "revision > 0 AND target_kind BETWEEN 1 AND 9 AND submission_lifetime_ticks > 0 AND submission_lifetime_ticks <= 864000000000 AND read_lifetime_ticks > 0 AND read_lifetime_ticks <= 864000000000");
                    table.ForeignKey(
                        name: "fk_moderation_case_target_policies_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_case_target_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    caller_user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    qualification_epoch_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    qualification_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    relationship_source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    relationship_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    relationship_revision = table.Column<long>(type: "INTEGER", nullable: true),
                    relationship_concurrency_stamp = table.Column<Guid>(type: "TEXT", nullable: true),
                    relationship_facts_digest = table.Column<string>(type: "TEXT", unicode: false, maxLength: 64, nullable: true),
                    lifetime_policy_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    lifetime_policy_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    token_digest = table.Column<string>(type: "TEXT", unicode: false, maxLength: 64, nullable: false),
                    target_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    target_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    target_version = table.Column<Guid>(type: "TEXT", nullable: false),
                    target_origin_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    target_origin_trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    target_origin_solution_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    target_origin_instance_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    target_origin_tenant_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    target_origin_community_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    target_origin_resource_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    target_origin_event_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    source_origin_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    source_origin_trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_origin_solution_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    source_origin_instance_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    source_origin_tenant_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    source_origin_community_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    source_origin_resource_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    source_origin_event_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    occurrence_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_version = table.Column<Guid>(type: "TEXT", nullable: false),
                    material_fact_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    consumed_case_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    consumed_operation_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    consumed_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_case_target_receipts", x => x.id);
                    table.CheckConstraint("ck_moderation_case_target_receipts_source_origin_scope", "source_origin_kind IS NOT NULL AND ((source_origin_kind = 1 AND source_origin_trust_domain_id IS NOT NULL AND source_origin_solution_id IS NULL AND source_origin_instance_id IS NULL AND source_origin_tenant_id IS NULL AND source_origin_community_id IS NULL AND source_origin_resource_id IS NULL AND source_origin_event_id IS NULL) OR (source_origin_kind = 2 AND source_origin_trust_domain_id IS NOT NULL AND source_origin_solution_id IS NOT NULL AND source_origin_instance_id IS NULL AND source_origin_tenant_id IS NULL AND source_origin_community_id IS NULL AND source_origin_resource_id IS NULL AND source_origin_event_id IS NULL) OR (source_origin_kind = 3 AND source_origin_trust_domain_id IS NOT NULL AND source_origin_solution_id IS NOT NULL AND source_origin_instance_id IS NOT NULL AND source_origin_tenant_id IS NULL AND source_origin_community_id IS NULL AND source_origin_resource_id IS NULL AND source_origin_event_id IS NULL) OR (source_origin_kind = 4 AND source_origin_trust_domain_id IS NOT NULL AND source_origin_solution_id IS NOT NULL AND source_origin_instance_id IS NOT NULL AND source_origin_tenant_id IS NOT NULL AND source_origin_community_id IS NULL AND source_origin_resource_id IS NULL AND source_origin_event_id IS NULL) OR (source_origin_kind = 5 AND source_origin_trust_domain_id IS NOT NULL AND source_origin_solution_id IS NOT NULL AND source_origin_instance_id IS NOT NULL AND source_origin_tenant_id IS NOT NULL AND source_origin_community_id IS NOT NULL AND source_origin_resource_id IS NULL AND source_origin_event_id IS NULL) OR (source_origin_kind = 6 AND source_origin_trust_domain_id IS NOT NULL AND source_origin_solution_id IS NOT NULL AND source_origin_instance_id IS NOT NULL AND source_origin_tenant_id IS NOT NULL AND source_origin_resource_id IS NOT NULL AND source_origin_event_id IS NULL) OR (source_origin_kind = 7 AND source_origin_trust_domain_id IS NOT NULL AND source_origin_solution_id IS NOT NULL AND source_origin_instance_id IS NOT NULL AND source_origin_tenant_id IS NOT NULL AND source_origin_community_id IS NULL AND source_origin_resource_id IS NULL AND source_origin_event_id IS NOT NULL AND source_origin_event_id <> '00000000-0000-0000-0000-000000000000'))");
                    table.CheckConstraint("ck_moderation_case_target_receipts_target_origin_scope", "target_origin_kind IS NOT NULL AND ((target_origin_kind = 1 AND target_origin_trust_domain_id IS NOT NULL AND target_origin_solution_id IS NULL AND target_origin_instance_id IS NULL AND target_origin_tenant_id IS NULL AND target_origin_community_id IS NULL AND target_origin_resource_id IS NULL AND target_origin_event_id IS NULL) OR (target_origin_kind = 2 AND target_origin_trust_domain_id IS NOT NULL AND target_origin_solution_id IS NOT NULL AND target_origin_instance_id IS NULL AND target_origin_tenant_id IS NULL AND target_origin_community_id IS NULL AND target_origin_resource_id IS NULL AND target_origin_event_id IS NULL) OR (target_origin_kind = 3 AND target_origin_trust_domain_id IS NOT NULL AND target_origin_solution_id IS NOT NULL AND target_origin_instance_id IS NOT NULL AND target_origin_tenant_id IS NULL AND target_origin_community_id IS NULL AND target_origin_resource_id IS NULL AND target_origin_event_id IS NULL) OR (target_origin_kind = 4 AND target_origin_trust_domain_id IS NOT NULL AND target_origin_solution_id IS NOT NULL AND target_origin_instance_id IS NOT NULL AND target_origin_tenant_id IS NOT NULL AND target_origin_community_id IS NULL AND target_origin_resource_id IS NULL AND target_origin_event_id IS NULL) OR (target_origin_kind = 5 AND target_origin_trust_domain_id IS NOT NULL AND target_origin_solution_id IS NOT NULL AND target_origin_instance_id IS NOT NULL AND target_origin_tenant_id IS NOT NULL AND target_origin_community_id IS NOT NULL AND target_origin_resource_id IS NULL AND target_origin_event_id IS NULL) OR (target_origin_kind = 6 AND target_origin_trust_domain_id IS NOT NULL AND target_origin_solution_id IS NOT NULL AND target_origin_instance_id IS NOT NULL AND target_origin_tenant_id IS NOT NULL AND target_origin_resource_id IS NOT NULL AND target_origin_event_id IS NULL) OR (target_origin_kind = 7 AND target_origin_trust_domain_id IS NOT NULL AND target_origin_solution_id IS NOT NULL AND target_origin_instance_id IS NOT NULL AND target_origin_tenant_id IS NOT NULL AND target_origin_community_id IS NULL AND target_origin_resource_id IS NULL AND target_origin_event_id IS NOT NULL AND target_origin_event_id <> '00000000-0000-0000-0000-000000000000'))");
                    table.CheckConstraint("ck_moderation_intake_receipt_consumption", "(revision = 1 AND consumed_case_id IS NULL AND consumed_operation_id IS NULL AND consumed_at IS NULL) OR (relationship_kind <> 5 AND revision = 2 AND consumed_case_id IS NOT NULL AND consumed_operation_id IS NOT NULL AND consumed_at IS NOT NULL AND consumed_at >= issued_at AND consumed_at < expires_at)");
                    table.CheckConstraint("ck_moderation_intake_receipt_generations", "qualification_revision > 0 AND lifetime_policy_revision > 0 AND ((relationship_kind IN (2, 5) AND relationship_revision IS NOT NULL AND relationship_revision > 0 AND relationship_concurrency_stamp IS NULL AND relationship_facts_digest IS NULL) OR (relationship_kind IN (1, 3, 4, 6) AND relationship_revision IS NULL AND relationship_concurrency_stamp IS NOT NULL AND relationship_facts_digest IS NULL) OR (relationship_kind = 7 AND target_kind IN (3, 4) AND relationship_revision IS NULL AND relationship_concurrency_stamp IS NULL AND relationship_facts_digest IS NOT NULL AND length(relationship_facts_digest) = 64))");
                    table.CheckConstraint("ck_moderation_intake_receipt_origin", "target_origin_trust_domain_id = source_origin_trust_domain_id AND target_origin_solution_id = source_origin_solution_id AND target_origin_instance_id = source_origin_instance_id AND source_origin_kind IN (3, 4) AND (source_origin_tenant_id IS NULL OR source_origin_tenant_id = tenant_id) AND ((target_kind IN (3, 4) AND target_origin_kind = 3) OR (target_kind IN (1, 2, 5, 6, 7, 8, 9) AND target_origin_kind = 4 AND target_origin_tenant_id = tenant_id))");
                    table.CheckConstraint("ck_moderation_intake_receipt_validity", "expires_at > issued_at");
                    table.ForeignKey(
                        name: "fk_moderation_case_target_receipts_moderation_cases_consumed_case_id",
                        column: x => x.consumed_case_id,
                        principalTable: "ie_moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_target_receipts_moderation_host_bindings_host_binding_id",
                        column: x => x.host_binding_id,
                        principalTable: "ie_moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_target_receipts_moderation_qualification_epochs_qualification_epoch_id",
                        column: x => x.qualification_epoch_id,
                        principalTable: "ie_moderation_qualification_epochs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_target_receipts_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_case_target_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    version = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    event_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    event_version = table.Column<Guid>(type: "TEXT", nullable: true),
                    kind = table.Column<int>(type: "INTEGER", nullable: false),
                    native_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    native_target_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    native_target_version = table.Column<Guid>(type: "TEXT", nullable: false),
                    native_facts_digest = table.Column<string>(type: "TEXT", unicode: false, maxLength: 64, nullable: false),
                    occurrence_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    occurrence_version = table.Column<Guid>(type: "TEXT", nullable: false),
                    material_fact_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    occurrence_digest = table.Column<string>(type: "TEXT", unicode: false, maxLength: 64, nullable: false),
                    material_digest = table.Column<string>(type: "TEXT", unicode: false, maxLength: 64, nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_case_target_sources", x => x.id);
                    table.CheckConstraint("ck_moderation_native_source_event", "(event_id IS NULL AND event_version IS NULL AND kind IN (3, 4)) OR (event_id IS NOT NULL AND event_version IS NOT NULL)");
                    table.CheckConstraint("ck_moderation_native_source_kind", "kind BETWEEN 1 AND 9 AND native_kind BETWEEN 1 AND 7 AND (kind IN (8, 9) OR kind = native_kind) AND (kind <> 9 OR native_kind = 1)");
                    table.ForeignKey(
                        name: "fk_moderation_case_target_sources_events_event_id",
                        column: x => x.event_id,
                        principalTable: "ie_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_target_sources_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_constitution_availability",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    reviewer_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    maximum_caseload = table.Column<int>(type: "INTEGER", nullable: false),
                    writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    withdrawn_by_human_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_constitution_availability", x => x.id);
                    table.CheckConstraint("ck_moderation_constitution_availability_capacity", "maximum_caseload > 0 AND writer_generation > 0");
                    table.CheckConstraint("ck_moderation_constitution_availability_validity", "valid_until > recorded_at");
                    table.CheckConstraint("ck_moderation_constitution_availability_withdrawal", "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR (revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at >= recorded_at)");
                    table.ForeignKey(
                        name: "fk_moderation_constitution_availability_users_reviewer_human_id",
                        column: x => x.reviewer_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_constitution_availability_users_withdrawn_by_human_id",
                        column: x => x.withdrawn_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_consumer_installations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    consumer_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    mode_code = table.Column<int>(type: "INTEGER", nullable: false),
                    runtime_generation = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    inventory_fingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    installed_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    installed_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    withdrawn_by_human_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_consumer_installations", x => x.id);
                    table.CheckConstraint("ck_moderation_consumer_installation_generation", "writer_generation > 0");
                    table.CheckConstraint("ck_moderation_consumer_installation_owner", "consumer_id <> '' AND mode_code = 1");
                    table.CheckConstraint("ck_moderation_consumer_installation_validity", "valid_until > installed_at");
                    table.CheckConstraint("ck_moderation_consumer_installation_withdrawal", "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR (revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at >= installed_at)");
                    table.ForeignKey(
                        name: "fk_moderation_consumer_installations_moderation_host_bindings_host_binding_id",
                        column: x => x.host_binding_id,
                        principalTable: "ie_moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_consumer_installations_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_consumer_installations_users_installed_by_human_id",
                        column: x => x.installed_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_consumer_installations_users_withdrawn_by_human_id",
                        column: x => x.withdrawn_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_event_generations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    event_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    observed_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    observed_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    event_stamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    actor_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    actor_stamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    organizer_actor_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    organizer_actor_stamp = table.Column<Guid>(type: "TEXT", nullable: true),
                    submitted_by_human_id = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_event_generations", x => x.id);
                    table.CheckConstraint("ck_moderation_event_generation_organizer", "(organizer_actor_id IS NULL AND organizer_actor_stamp IS NULL) OR (organizer_actor_id IS NOT NULL AND organizer_actor_stamp IS NOT NULL)");
                    table.CheckConstraint("ck_moderation_event_generation_revision", "revision > 0 AND writer_generation > 0");
                    table.CheckConstraint("ck_moderation_event_generation_validity", "valid_until > observed_at");
                    table.ForeignKey(
                        name: "fk_moderation_event_generations_moderation_host_bindings_host_binding_id",
                        column: x => x.host_binding_id,
                        principalTable: "ie_moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_event_generations_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_event_generations_users_observed_by_human_id",
                        column: x => x.observed_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_measurement_issuer_enrollments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    key_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    issuer = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    public_key_spki_base64 = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    runner_digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    catalogue_digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    test_bundle_digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    approved_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    revoked_by_human_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_measurement_issuer_enrollments", x => x.id);
                    table.CheckConstraint("ck_moderation_measurement_issuer_enrollment_generation", "writer_generation > 0 AND writer_generation < 9223372036854775807");
                    table.CheckConstraint("ck_moderation_measurement_issuer_enrollment_key", "key_id <> id");
                    table.CheckConstraint("ck_moderation_measurement_issuer_enrollment_revocation", "(revision = 1 AND revoked_at IS NULL AND revoked_by_human_id IS NULL) OR (revision = 2 AND revoked_at IS NOT NULL AND revoked_by_human_id IS NOT NULL AND revoked_at >= approved_at)");
                    table.CheckConstraint("ck_moderation_measurement_issuer_enrollment_validity", "valid_until > approved_at");
                    table.ForeignKey(
                        name: "fk_moderation_measurement_issuer_enrollments_moderation_host_bindings_host_binding_id",
                        column: x => x.host_binding_id,
                        principalTable: "ie_moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_measurement_issuer_enrollments_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_measurement_issuer_enrollments_users_approved_by_human_id",
                        column: x => x.approved_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_measurement_issuer_enrollments_users_revoked_by_human_id",
                        column: x => x.revoked_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_notice_sources",
                columns: table => new
                {
                    effect_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    operation_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    decision_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    decision_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    scope_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    scope_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    subject_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    subject_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    snapshot_json = table.Column<string>(type: "TEXT", nullable: false),
                    snapshot_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    effect_binding_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    retain_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    status_metadata_json = table.Column<string>(type: "TEXT", maxLength: 8192, nullable: true),
                    status_metadata_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_notice_sources", x => x.effect_id);
                    table.CheckConstraint("ck_moderation_notice_source_retention", "retain_until IS NULL OR retain_until > captured_at");
                    table.CheckConstraint("ck_moderation_notice_source_revision", "decision_revision > 0");
                    table.CheckConstraint("ck_moderation_notice_source_scope", "scope_kind BETWEEN 1 AND 7");
                    table.CheckConstraint("ck_moderation_notice_source_status", "(status_metadata_json IS NULL AND status_metadata_hash IS NULL) OR (status_metadata_json IS NOT NULL AND status_metadata_hash IS NOT NULL AND retain_until IS NOT NULL AND subject_kind IN (1, 4))");
                    table.CheckConstraint("ck_moderation_notice_source_subject", "subject_kind BETWEEN 1 AND 4");
                    table.ForeignKey(
                        name: "fk_moderation_notice_sources_moderation_effect_intents_effect_id",
                        column: x => x.effect_id,
                        principalTable: "ie_moderation_effect_intents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_operational_budget_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    subject_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    action = table.Column<int>(type: "INTEGER", nullable: false),
                    maximum_occurrences = table.Column<int>(type: "INTEGER", nullable: false),
                    window_ticks = table.Column<long>(type: "INTEGER", nullable: false),
                    operator_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    recorded_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    recorded_at = table.Column<long>(type: "INTEGER", nullable: false),
                    valid_until = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_operational_budget_sources", x => x.id);
                    table.UniqueConstraint("ak_moderation_operational_budget_sources_id_host_binding_id_trust_domain_id_tenant_id", x => new { x.id, x.host_binding_id, x.trust_domain_id, x.tenant_id });
                    table.CheckConstraint("ck_operational_budget_source_generation", "revision = 1 AND writer_generation > 0 AND writer_generation < 9223372036854775807");
                    table.CheckConstraint("ck_operational_budget_source_independence", "operator_human_id <> recorded_by_human_id");
                    table.CheckConstraint("ck_operational_budget_source_terms", "subject_kind = 1 AND action IN (1, 2, 3, 4, 5, 6) AND maximum_occurrences > 0 AND window_ticks > 0 AND window_ticks < 9223372036854775807");
                    table.CheckConstraint("ck_operational_budget_source_validity", "recorded_at > 0 AND valid_until > recorded_at AND valid_until < 3155378975999999999 AND window_ticks < recorded_at AND window_ticks < 3155378975999999999 - recorded_at");
                    table.ForeignKey(
                        name: "fk_moderation_operational_budget_sources_moderation_host_bindings_host_binding_id",
                        column: x => x.host_binding_id,
                        principalTable: "ie_moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_operational_budget_sources_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_operational_budget_sources_users_operator_human_id",
                        column: x => x.operator_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_operational_budget_sources_users_recorded_by_human_id",
                        column: x => x.recorded_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_policy_publications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    policy_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    adopted_revision = table.Column<long>(type: "INTEGER", nullable: true),
                    previous_publication_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    solution_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    instance_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    jurisdiction_tenant_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    principal_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    operation_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    request_digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    declared_state_digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    authority_source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    authority_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    qualification_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    writer_generation = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    catalogue_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    inventory_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    inventory_fingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    published_active = table.Column<bool>(type: "INTEGER", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    evidence_valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    maximum_finite_duration_ticks = table.Column<long>(type: "INTEGER", nullable: false),
                    maximum_review_delay_ticks = table.Column<long>(type: "INTEGER", nullable: false),
                    minimum_approvals = table.Column<int>(type: "INTEGER", nullable: false),
                    maximum_approvals = table.Column<int>(type: "INTEGER", nullable: false),
                    minimum_contestation_calendar_months = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_policy_publications", x => x.id);
                    table.UniqueConstraint("ak_moderation_policy_publications_policy_id_id", x => new { x.policy_id, x.id });
                    table.CheckConstraint("ck_moderation_publication_adoption", "(adopted_revision IS NOT NULL AND adopted_revision = revision) OR (adopted_revision IS NULL AND published_active = false AND evidence_valid_until IS NULL)");
                    table.CheckConstraint("ck_moderation_publication_duration", "maximum_finite_duration_ticks > 0 AND maximum_review_delay_ticks > 0");
                    table.CheckConstraint("ck_moderation_publication_history", "(revision = 1 AND previous_publication_id IS NULL) OR (revision > 1 AND previous_publication_id IS NOT NULL)");
                    table.CheckConstraint("ck_moderation_publication_revision", "revision > 0 AND authority_revision > 0 AND qualification_revision = authority_revision + 1");
                    table.ForeignKey(
                        name: "fk_moderation_policy_publications_moderation_host_bindings_host_binding_id",
                        column: x => x.host_binding_id,
                        principalTable: "ie_moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_policy_publications_moderation_policy_publications_policy_id_previous_publication_id",
                        columns: x => new { x.policy_id, x.previous_publication_id },
                        principalTable: "ie_moderation_policy_publications",
                        principalColumns: new[] { "policy_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_policy_publications_moderation_qualification_epochs_authority_source_id",
                        column: x => x.authority_source_id,
                        principalTable: "ie_moderation_qualification_epochs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_policy_publications_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_pre_finding_erasure_tombstones",
                columns: table => new
                {
                    case_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_pre_finding_erasure_tombstones", x => x.case_id);
                    table.ForeignKey(
                        name: "fk_moderation_pre_finding_erasure_tombstones_moderation_cases_case_id",
                        column: x => x.case_id,
                        principalTable: "ie_moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_pre_finding_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    case_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    actor_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    result_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    result_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    request_digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_pre_finding_receipts", x => x.id);
                    table.ForeignKey(
                        name: "fk_moderation_pre_finding_receipts_moderation_cases_case_id",
                        column: x => x.case_id,
                        principalTable: "ie_moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_pre_finding_receipts_users_actor_human_id",
                        column: x => x.actor_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_qualification_policy_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    case_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    case_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    maximum_reviewer_lifetime_ticks = table.Column<long>(type: "INTEGER", nullable: false),
                    maximum_urgency_lifetime_ticks = table.Column<long>(type: "INTEGER", nullable: false),
                    published_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_qualification_policy_sources", x => x.id);
                    table.UniqueConstraint("ak_moderation_qualification_policy_sources_id_host_binding_id_trust_domain_id_tenant_id_case_id", x => new { x.id, x.host_binding_id, x.trust_domain_id, x.tenant_id, x.case_id });
                    table.CheckConstraint("ck_moderation_qualification_policy_source_generation", "revision > 0 AND case_revision > 0 AND writer_generation > 0");
                    table.CheckConstraint("ck_moderation_qualification_policy_source_limits", "maximum_reviewer_lifetime_ticks > 0 AND maximum_reviewer_lifetime_ticks < 9223372036854775807 AND maximum_urgency_lifetime_ticks > 0 AND maximum_urgency_lifetime_ticks < 9223372036854775807");
                    table.CheckConstraint("ck_moderation_qualification_policy_source_validity", "valid_until > published_at");
                    table.ForeignKey(
                        name: "fk_moderation_qualification_policy_sources_moderation_cases_case_id",
                        column: x => x.case_id,
                        principalTable: "ie_moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_qualification_policy_sources_moderation_host_bindings_host_binding_id",
                        column: x => x.host_binding_id,
                        principalTable: "ie_moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_qualification_policy_sources_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_qualification_policy_sources_users_published_by_human_id",
                        column: x => x.published_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_recognition_disposition",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    intent_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    subject_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    policy_version = table.Column<int>(type: "INTEGER", nullable: false),
                    reason_code = table.Column<int>(type: "INTEGER", nullable: false),
                    pending_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    original_material_receipt = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    original_material_count = table.Column<int>(type: "INTEGER", nullable: false),
                    observed_material_commitment = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    coverage_receipt = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    coverage_count = table.Column<int>(type: "INTEGER", nullable: false),
                    observed_failure = table.Column<int>(type: "INTEGER", nullable: false),
                    grounds_receipt = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    legal_mandate_receipt = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    source_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    adoption_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    adoption_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    adoption_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    privacy_owner_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    recovery_owner_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    grounds = table.Column<int>(type: "INTEGER", nullable: false),
                    original_deadline = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    assessed_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    authorized_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    authority_high_water_before_authorization = table.Column<long>(type: "INTEGER", nullable: true),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    authority_sequence = table.Column<long>(type: "INTEGER", nullable: true),
                    authority_recorded_at_utc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    replay_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    retain_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_recognition_disposition", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_recognition_disposition_remedy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    intent_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    pending_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    original_material_receipt = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    observed_material_commitment = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    grounds_receipt = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    legal_mandate_receipt = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    coverage_receipt = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    privacy_owner_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    recovery_owner_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    grounds = table.Column<int>(type: "INTEGER", nullable: false),
                    receipt_hash = table.Column<byte[]>(type: "BLOB", maxLength: 32, nullable: false),
                    protected_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    remedy_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    retain_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_recognition_disposition_remedy", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_recognition_escalation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    intent_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    target_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    action_recovery_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    pending_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    recipient_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    other_owner_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    stage = table.Column<int>(type: "INTEGER", nullable: false),
                    required_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    original_deadline = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    retain_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    notification_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_recognition_escalation", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_recognition_key_lifecycle",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ring_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    acknowledgement_sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    initial_erasure_head = table.Column<long>(type: "INTEGER", nullable: false),
                    initial_erasure_retained_floor = table.Column<long>(type: "INTEGER", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_recognition_key_lifecycle", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_recognition_key_lifecycle_purge",
                columns: table => new
                {
                    intent_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    material_receipt = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    material_count = table.Column<int>(type: "INTEGER", nullable: false),
                    authority_sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    purged_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    backup_obligation_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    retain_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_recognition_key_lifecycle_purge", x => x.intent_id);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_recognition_pending",
                columns: table => new
                {
                    intent_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    subject_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    policy_version = table.Column<int>(type: "INTEGER", nullable: false),
                    reason_code = table.Column<int>(type: "INTEGER", nullable: false),
                    privacy_owner_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    recovery_owner_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    settle_by = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    escalate_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    receipt_hash = table.Column<byte[]>(type: "BLOB", maxLength: 32, nullable: false),
                    receipt_expires_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    state = table.Column<int>(type: "INTEGER", nullable: false),
                    failure = table.Column<int>(type: "INTEGER", nullable: false),
                    material_count = table.Column<int>(type: "INTEGER", nullable: false),
                    material_receipt = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    computed_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    authority_sequence = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_recognition_pending", x => x.intent_id);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_recovery_store_states",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    generation = table.Column<long>(type: "INTEGER", nullable: false),
                    position = table.Column<long>(type: "INTEGER", nullable: false),
                    installation_slot = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_recovery_store_states", x => x.id);
                    table.CheckConstraint("ck_moderation_recovery_store_coordinates", "revision > 0 AND generation > 0 AND generation <= revision - position AND position >= 0 AND position < revision");
                    table.CheckConstraint("ck_moderation_recovery_store_slot", "installation_slot = 1");
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_resource_generations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    resource_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    observed_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    observed_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    resource_stamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    material_version_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    event_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    event_stamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    actor_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    actor_stamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    organizer_actor_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    organizer_actor_stamp = table.Column<Guid>(type: "TEXT", nullable: true),
                    delivery_type = table.Column<int>(type: "INTEGER", nullable: false),
                    storage_object_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    storage_object_stamp = table.Column<Guid>(type: "TEXT", nullable: true),
                    storage_provider_binding_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    storage_provider = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_resource_generations", x => x.id);
                    table.CheckConstraint("ck_moderation_resource_generation_material", "material_version_id <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("ck_moderation_resource_generation_revision", "revision > 0 AND writer_generation > 0");
                    table.CheckConstraint("ck_moderation_resource_generation_storage", "(delivery_type = 1 AND storage_object_id IS NOT NULL AND storage_object_stamp IS NOT NULL AND storage_provider_binding_id IS NOT NULL AND storage_provider IS NOT NULL) OR (delivery_type = 2 AND storage_object_id IS NULL AND storage_object_stamp IS NULL AND storage_provider_binding_id IS NULL AND storage_provider IS NULL)");
                    table.CheckConstraint("ck_moderation_resource_generation_validity", "valid_until > observed_at");
                    table.ForeignKey(
                        name: "fk_moderation_resource_generations_moderation_host_bindings_host_binding_id",
                        column: x => x.host_binding_id,
                        principalTable: "ie_moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_resource_generations_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_resource_generations_users_observed_by_human_id",
                        column: x => x.observed_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_schedule_risk_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_sanction_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_sanction_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    source_decision_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    original_sanction_starts_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    original_sanction_ends_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    original_due_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    state = table.Column<int>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    accepted_intent_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    accepted_finding_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    accepted_finding_revision = table.Column<long>(type: "INTEGER", nullable: true),
                    accepted_reviewer_assignment_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    accepted_reviewer_assignment_revision = table.Column<long>(type: "INTEGER", nullable: true),
                    withdrawal_intent_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    subject_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    subject_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    subject_origin_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    subject_origin_trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    subject_origin_solution_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    subject_origin_instance_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    subject_origin_tenant_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    subject_origin_community_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    subject_origin_resource_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    subject_origin_event_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    source_scope_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    source_scope_trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_scope_solution_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    source_scope_instance_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    source_scope_tenant_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    source_scope_community_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    source_scope_resource_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    source_scope_event_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    source_rule_owner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_rule_code = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    source_rule_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    event_scope_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    event_scope_trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    event_scope_solution_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    event_scope_instance_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    event_scope_tenant_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    event_scope_community_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    event_scope_resource_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    event_scope_event_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    session_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    participant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    original_schedule_generation = table.Column<Guid>(type: "TEXT", nullable: false),
                    schedule_generation = table.Column<Guid>(type: "TEXT", nullable: false),
                    original_participant_generation = table.Column<Guid>(type: "TEXT", nullable: false),
                    participant_generation = table.Column<Guid>(type: "TEXT", nullable: false),
                    policy_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    original_policy_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    policy_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    original_publication_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    publication_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    policy_rule_owner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    policy_rule_code = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    original_policy_rule_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    policy_rule_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    owner_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    owner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    owner_origin_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    owner_origin_trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    owner_origin_solution_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    owner_origin_instance_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    owner_origin_tenant_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    owner_origin_community_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    owner_origin_resource_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    owner_origin_event_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    escalation_owner_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    escalation_owner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    escalation_owner_origin_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    escalation_owner_origin_trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    escalation_owner_origin_solution_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    escalation_owner_origin_instance_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    escalation_owner_origin_tenant_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    escalation_owner_origin_community_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    escalation_owner_origin_resource_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    escalation_owner_origin_event_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    withdrawn_by_human_id = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_schedule_risk_reviews", x => x.id);
                    table.CheckConstraint("ck_moderation_schedule_risk_ancestry", "subject_kind = 1 AND owner_kind = 1 AND escalation_owner_kind = 1 AND event_scope_kind = 7 AND event_scope_tenant_id IS NOT NULL AND source_sanction_revision > 0 AND revision > 0 AND source_rule_revision > 0 AND original_policy_revision > 0 AND policy_revision >= original_policy_revision AND original_policy_rule_revision > 0 AND policy_rule_revision >= original_policy_rule_revision AND subject_origin_trust_domain_id = source_scope_trust_domain_id AND owner_origin_trust_domain_id = source_scope_trust_domain_id AND escalation_owner_origin_trust_domain_id = source_scope_trust_domain_id AND event_scope_trust_domain_id = source_scope_trust_domain_id AND (source_scope_solution_id IS NULL OR source_scope_solution_id = event_scope_solution_id) AND (source_scope_instance_id IS NULL OR source_scope_instance_id = event_scope_instance_id) AND (source_scope_tenant_id IS NULL OR source_scope_tenant_id = event_scope_tenant_id) AND source_scope_community_id IS NULL AND source_scope_resource_id IS NULL AND (source_scope_event_id IS NULL OR source_scope_event_id = event_scope_event_id)");
                    table.CheckConstraint("ck_moderation_schedule_risk_identity", "source_sanction_id <> '00000000-0000-0000-0000-000000000000' AND source_decision_id <> '00000000-0000-0000-0000-000000000000' AND session_id <> '00000000-0000-0000-0000-000000000000' AND participant_id <> '00000000-0000-0000-0000-000000000000' AND policy_id <> '00000000-0000-0000-0000-000000000000' AND original_schedule_generation <> '00000000-0000-0000-0000-000000000000' AND schedule_generation <> '00000000-0000-0000-0000-000000000000' AND original_participant_generation <> '00000000-0000-0000-0000-000000000000' AND participant_generation <> '00000000-0000-0000-0000-000000000000' AND original_publication_id <> '00000000-0000-0000-0000-000000000000' AND publication_id <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("ck_moderation_schedule_risk_interval", "updated_at >= created_at AND original_due_at > created_at AND due_at > created_at AND (original_sanction_ends_at IS NULL OR original_sanction_ends_at > original_sanction_starts_at)");
                    table.CheckConstraint("ck_moderation_schedule_risk_reviews_escalation_owner_origin_scope", "escalation_owner_origin_kind IS NOT NULL AND ((escalation_owner_origin_kind = 1 AND escalation_owner_origin_trust_domain_id IS NOT NULL AND escalation_owner_origin_solution_id IS NULL AND escalation_owner_origin_instance_id IS NULL AND escalation_owner_origin_tenant_id IS NULL AND escalation_owner_origin_community_id IS NULL AND escalation_owner_origin_resource_id IS NULL AND escalation_owner_origin_event_id IS NULL) OR (escalation_owner_origin_kind = 2 AND escalation_owner_origin_trust_domain_id IS NOT NULL AND escalation_owner_origin_solution_id IS NOT NULL AND escalation_owner_origin_instance_id IS NULL AND escalation_owner_origin_tenant_id IS NULL AND escalation_owner_origin_community_id IS NULL AND escalation_owner_origin_resource_id IS NULL AND escalation_owner_origin_event_id IS NULL) OR (escalation_owner_origin_kind = 3 AND escalation_owner_origin_trust_domain_id IS NOT NULL AND escalation_owner_origin_solution_id IS NOT NULL AND escalation_owner_origin_instance_id IS NOT NULL AND escalation_owner_origin_tenant_id IS NULL AND escalation_owner_origin_community_id IS NULL AND escalation_owner_origin_resource_id IS NULL AND escalation_owner_origin_event_id IS NULL) OR (escalation_owner_origin_kind = 4 AND escalation_owner_origin_trust_domain_id IS NOT NULL AND escalation_owner_origin_solution_id IS NOT NULL AND escalation_owner_origin_instance_id IS NOT NULL AND escalation_owner_origin_tenant_id IS NOT NULL AND escalation_owner_origin_community_id IS NULL AND escalation_owner_origin_resource_id IS NULL AND escalation_owner_origin_event_id IS NULL) OR (escalation_owner_origin_kind = 5 AND escalation_owner_origin_trust_domain_id IS NOT NULL AND escalation_owner_origin_solution_id IS NOT NULL AND escalation_owner_origin_instance_id IS NOT NULL AND escalation_owner_origin_tenant_id IS NOT NULL AND escalation_owner_origin_community_id IS NOT NULL AND escalation_owner_origin_resource_id IS NULL AND escalation_owner_origin_event_id IS NULL) OR (escalation_owner_origin_kind = 6 AND escalation_owner_origin_trust_domain_id IS NOT NULL AND escalation_owner_origin_solution_id IS NOT NULL AND escalation_owner_origin_instance_id IS NOT NULL AND escalation_owner_origin_tenant_id IS NOT NULL AND escalation_owner_origin_resource_id IS NOT NULL AND escalation_owner_origin_event_id IS NULL) OR (escalation_owner_origin_kind = 7 AND escalation_owner_origin_trust_domain_id IS NOT NULL AND escalation_owner_origin_solution_id IS NOT NULL AND escalation_owner_origin_instance_id IS NOT NULL AND escalation_owner_origin_tenant_id IS NOT NULL AND escalation_owner_origin_community_id IS NULL AND escalation_owner_origin_resource_id IS NULL AND escalation_owner_origin_event_id IS NOT NULL AND escalation_owner_origin_event_id <> '00000000-0000-0000-0000-000000000000'))");
                    table.CheckConstraint("ck_moderation_schedule_risk_reviews_escalation_owner_subject", "escalation_owner_kind BETWEEN 1 AND 4");
                    table.CheckConstraint("ck_moderation_schedule_risk_reviews_event_scope_scope", "event_scope_kind IS NOT NULL AND ((event_scope_kind = 1 AND event_scope_trust_domain_id IS NOT NULL AND event_scope_solution_id IS NULL AND event_scope_instance_id IS NULL AND event_scope_tenant_id IS NULL AND event_scope_community_id IS NULL AND event_scope_resource_id IS NULL AND event_scope_event_id IS NULL) OR (event_scope_kind = 2 AND event_scope_trust_domain_id IS NOT NULL AND event_scope_solution_id IS NOT NULL AND event_scope_instance_id IS NULL AND event_scope_tenant_id IS NULL AND event_scope_community_id IS NULL AND event_scope_resource_id IS NULL AND event_scope_event_id IS NULL) OR (event_scope_kind = 3 AND event_scope_trust_domain_id IS NOT NULL AND event_scope_solution_id IS NOT NULL AND event_scope_instance_id IS NOT NULL AND event_scope_tenant_id IS NULL AND event_scope_community_id IS NULL AND event_scope_resource_id IS NULL AND event_scope_event_id IS NULL) OR (event_scope_kind = 4 AND event_scope_trust_domain_id IS NOT NULL AND event_scope_solution_id IS NOT NULL AND event_scope_instance_id IS NOT NULL AND event_scope_tenant_id IS NOT NULL AND event_scope_community_id IS NULL AND event_scope_resource_id IS NULL AND event_scope_event_id IS NULL) OR (event_scope_kind = 5 AND event_scope_trust_domain_id IS NOT NULL AND event_scope_solution_id IS NOT NULL AND event_scope_instance_id IS NOT NULL AND event_scope_tenant_id IS NOT NULL AND event_scope_community_id IS NOT NULL AND event_scope_resource_id IS NULL AND event_scope_event_id IS NULL) OR (event_scope_kind = 6 AND event_scope_trust_domain_id IS NOT NULL AND event_scope_solution_id IS NOT NULL AND event_scope_instance_id IS NOT NULL AND event_scope_tenant_id IS NOT NULL AND event_scope_resource_id IS NOT NULL AND event_scope_event_id IS NULL) OR (event_scope_kind = 7 AND event_scope_trust_domain_id IS NOT NULL AND event_scope_solution_id IS NOT NULL AND event_scope_instance_id IS NOT NULL AND event_scope_tenant_id IS NOT NULL AND event_scope_community_id IS NULL AND event_scope_resource_id IS NULL AND event_scope_event_id IS NOT NULL AND event_scope_event_id <> '00000000-0000-0000-0000-000000000000'))");
                    table.CheckConstraint("ck_moderation_schedule_risk_reviews_owner_origin_scope", "owner_origin_kind IS NOT NULL AND ((owner_origin_kind = 1 AND owner_origin_trust_domain_id IS NOT NULL AND owner_origin_solution_id IS NULL AND owner_origin_instance_id IS NULL AND owner_origin_tenant_id IS NULL AND owner_origin_community_id IS NULL AND owner_origin_resource_id IS NULL AND owner_origin_event_id IS NULL) OR (owner_origin_kind = 2 AND owner_origin_trust_domain_id IS NOT NULL AND owner_origin_solution_id IS NOT NULL AND owner_origin_instance_id IS NULL AND owner_origin_tenant_id IS NULL AND owner_origin_community_id IS NULL AND owner_origin_resource_id IS NULL AND owner_origin_event_id IS NULL) OR (owner_origin_kind = 3 AND owner_origin_trust_domain_id IS NOT NULL AND owner_origin_solution_id IS NOT NULL AND owner_origin_instance_id IS NOT NULL AND owner_origin_tenant_id IS NULL AND owner_origin_community_id IS NULL AND owner_origin_resource_id IS NULL AND owner_origin_event_id IS NULL) OR (owner_origin_kind = 4 AND owner_origin_trust_domain_id IS NOT NULL AND owner_origin_solution_id IS NOT NULL AND owner_origin_instance_id IS NOT NULL AND owner_origin_tenant_id IS NOT NULL AND owner_origin_community_id IS NULL AND owner_origin_resource_id IS NULL AND owner_origin_event_id IS NULL) OR (owner_origin_kind = 5 AND owner_origin_trust_domain_id IS NOT NULL AND owner_origin_solution_id IS NOT NULL AND owner_origin_instance_id IS NOT NULL AND owner_origin_tenant_id IS NOT NULL AND owner_origin_community_id IS NOT NULL AND owner_origin_resource_id IS NULL AND owner_origin_event_id IS NULL) OR (owner_origin_kind = 6 AND owner_origin_trust_domain_id IS NOT NULL AND owner_origin_solution_id IS NOT NULL AND owner_origin_instance_id IS NOT NULL AND owner_origin_tenant_id IS NOT NULL AND owner_origin_resource_id IS NOT NULL AND owner_origin_event_id IS NULL) OR (owner_origin_kind = 7 AND owner_origin_trust_domain_id IS NOT NULL AND owner_origin_solution_id IS NOT NULL AND owner_origin_instance_id IS NOT NULL AND owner_origin_tenant_id IS NOT NULL AND owner_origin_community_id IS NULL AND owner_origin_resource_id IS NULL AND owner_origin_event_id IS NOT NULL AND owner_origin_event_id <> '00000000-0000-0000-0000-000000000000'))");
                    table.CheckConstraint("ck_moderation_schedule_risk_reviews_owner_subject", "owner_kind BETWEEN 1 AND 4");
                    table.CheckConstraint("ck_moderation_schedule_risk_reviews_source_scope_scope", "source_scope_kind IS NOT NULL AND ((source_scope_kind = 1 AND source_scope_trust_domain_id IS NOT NULL AND source_scope_solution_id IS NULL AND source_scope_instance_id IS NULL AND source_scope_tenant_id IS NULL AND source_scope_community_id IS NULL AND source_scope_resource_id IS NULL AND source_scope_event_id IS NULL) OR (source_scope_kind = 2 AND source_scope_trust_domain_id IS NOT NULL AND source_scope_solution_id IS NOT NULL AND source_scope_instance_id IS NULL AND source_scope_tenant_id IS NULL AND source_scope_community_id IS NULL AND source_scope_resource_id IS NULL AND source_scope_event_id IS NULL) OR (source_scope_kind = 3 AND source_scope_trust_domain_id IS NOT NULL AND source_scope_solution_id IS NOT NULL AND source_scope_instance_id IS NOT NULL AND source_scope_tenant_id IS NULL AND source_scope_community_id IS NULL AND source_scope_resource_id IS NULL AND source_scope_event_id IS NULL) OR (source_scope_kind = 4 AND source_scope_trust_domain_id IS NOT NULL AND source_scope_solution_id IS NOT NULL AND source_scope_instance_id IS NOT NULL AND source_scope_tenant_id IS NOT NULL AND source_scope_community_id IS NULL AND source_scope_resource_id IS NULL AND source_scope_event_id IS NULL) OR (source_scope_kind = 5 AND source_scope_trust_domain_id IS NOT NULL AND source_scope_solution_id IS NOT NULL AND source_scope_instance_id IS NOT NULL AND source_scope_tenant_id IS NOT NULL AND source_scope_community_id IS NOT NULL AND source_scope_resource_id IS NULL AND source_scope_event_id IS NULL) OR (source_scope_kind = 6 AND source_scope_trust_domain_id IS NOT NULL AND source_scope_solution_id IS NOT NULL AND source_scope_instance_id IS NOT NULL AND source_scope_tenant_id IS NOT NULL AND source_scope_resource_id IS NOT NULL AND source_scope_event_id IS NULL) OR (source_scope_kind = 7 AND source_scope_trust_domain_id IS NOT NULL AND source_scope_solution_id IS NOT NULL AND source_scope_instance_id IS NOT NULL AND source_scope_tenant_id IS NOT NULL AND source_scope_community_id IS NULL AND source_scope_resource_id IS NULL AND source_scope_event_id IS NOT NULL AND source_scope_event_id <> '00000000-0000-0000-0000-000000000000'))");
                    table.CheckConstraint("ck_moderation_schedule_risk_reviews_subject_origin_scope", "subject_origin_kind IS NOT NULL AND ((subject_origin_kind = 1 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 2 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 3 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 4 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 5 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NOT NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 6 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_resource_id IS NOT NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 7 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NOT NULL AND subject_origin_event_id <> '00000000-0000-0000-0000-000000000000'))");
                    table.CheckConstraint("ck_moderation_schedule_risk_reviews_subject_subject", "subject_kind BETWEEN 1 AND 4");
                    table.CheckConstraint("ck_moderation_schedule_risk_state", "state BETWEEN 1 AND 4 AND ((state = 4 AND updated_at >= due_at) OR (state <> 4 AND updated_at < due_at)) AND (revision <> 1 OR (state = 1 AND updated_at = created_at AND due_at = original_due_at AND schedule_generation = original_schedule_generation AND participant_generation = original_participant_generation AND policy_revision = original_policy_revision AND publication_id = original_publication_id AND policy_rule_revision = original_policy_rule_revision))");
                    table.CheckConstraint("ck_moderation_schedule_risk_terminal", "((state = 2 AND accepted_intent_id IS NOT NULL AND accepted_finding_id IS NOT NULL AND accepted_finding_revision IS NOT NULL AND accepted_finding_revision > 0 AND accepted_reviewer_assignment_id IS NOT NULL AND accepted_reviewer_assignment_revision IS NOT NULL AND accepted_reviewer_assignment_revision > 0) OR (state <> 2 AND accepted_intent_id IS NULL AND accepted_finding_id IS NULL AND accepted_finding_revision IS NULL AND accepted_reviewer_assignment_id IS NULL AND accepted_reviewer_assignment_revision IS NULL)) AND ((state = 3 AND withdrawal_intent_id IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_by_human_id = owner_id) OR (state <> 3 AND withdrawal_intent_id IS NULL AND withdrawn_by_human_id IS NULL))");
                    table.ForeignKey(
                        name: "fk_moderation_schedule_risk_reviews_moderation_findings_accepted_finding_id",
                        column: x => x.accepted_finding_id,
                        principalTable: "ie_moderation_findings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_schedule_risk_reviews_moderation_reviewer_assignments_accepted_reviewer_assignment_id",
                        column: x => x.accepted_reviewer_assignment_id,
                        principalTable: "ie_moderation_reviewer_assignments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_schedule_risk_reviews_moderation_sanctions_source_sanction_id",
                        column: x => x.source_sanction_id,
                        principalTable: "ie_moderation_sanctions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_user_birth_date_collection_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    observation_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    assessment_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    assessment_adoption_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    consent_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    policy_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    policy_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    publication_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    profile_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    collected_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_user_birth_date_collection_receipts", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_birth_date_collection_receipts_user_pii_user_id",
                        column: x => x.user_id,
                        principalTable: "ie_user_pii",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_case_narrative_adoptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    source_writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    adopted_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    withdrawn_by_human_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_case_narrative_adoptions", x => x.id);
                    table.UniqueConstraint("ak_moderation_case_narrative_adoptions_id_host_binding_id_trust_domain_id_tenant_id_source_id", x => new { x.id, x.host_binding_id, x.trust_domain_id, x.tenant_id, x.source_id });
                    table.CheckConstraint("ck_case_narrative_adoption_generation", "source_revision = 1 AND source_writer_generation > 0 AND writer_generation > source_writer_generation");
                    table.CheckConstraint("ck_case_narrative_adoption_validity", "valid_until > adopted_at");
                    table.CheckConstraint("ck_case_narrative_adoption_withdrawal", "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR (revision = 2 AND withdrawn_at >= adopted_at AND withdrawn_by_human_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_moderation_case_narrative_adoptions_moderation_case_narrative_policy_sources_source_id_host_binding_id_trust_dom_cef36ebfe3ff",
                        columns: x => new { x.source_id, x.host_binding_id, x.trust_domain_id, x.tenant_id },
                        principalTable: "ie_moderation_case_narrative_policy_sources",
                        principalColumns: new[] { "id", "host_binding_id", "trust_domain_id", "tenant_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_narrative_adoptions_users_adopted_by_human_id",
                        column: x => x.adopted_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_narrative_adoptions_users_withdrawn_by_human_id",
                        column: x => x.withdrawn_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_case_read_grants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    case_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    case_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    caller_user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    issuer_user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    issuer_role_grant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    issuer_platform_grant = table.Column<bool>(type: "INTEGER", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_case_read_grants", x => x.id);
                    table.CheckConstraint("ck_moderation_case_read_grant_validity", "expires_at > issued_at AND revision > 0 AND case_revision > 0 AND (revoked_at IS NULL OR revoked_at >= issued_at)");
                    table.ForeignKey(
                        name: "fk_moderation_case_read_grants_moderation_case_target_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "ie_moderation_case_target_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_read_grants_moderation_cases_case_id",
                        column: x => x.case_id,
                        principalTable: "ie_moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_read_grants_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_notices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    effect_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    audience = table.Column<int>(type: "INTEGER", nullable: false),
                    recipient_subject_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    recipient_subject_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    native_intent_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    delivery_disposition = table.Column<int>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_notices", x => x.id);
                    table.CheckConstraint("ck_moderation_notice_native", "(native_intent_id IS NULL AND delivery_disposition BETWEEN 1 AND 4) OR (native_intent_id IS NOT NULL AND delivery_disposition IN (5, 6) AND tenant_id IS NOT NULL AND recipient_subject_kind = 1)");
                    table.CheckConstraint("ck_moderation_notice_recipient", "recipient_subject_kind BETWEEN 1 AND 4 AND audience BETWEEN 1 AND 3 AND (audience <> 3 OR recipient_subject_kind = 1)");
                    table.ForeignKey(
                        name: "fk_moderation_notices_moderation_notice_sources_effect_id",
                        column: x => x.effect_id,
                        principalTable: "ie_moderation_notice_sources",
                        principalColumn: "effect_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_notices_notification_intents_native_intent_id",
                        column: x => x.native_intent_id,
                        principalTable: "ie_notification_intents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_original_effect_completion_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    effect_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    lease_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    lease_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    lease_started_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    lease_expires_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    publication_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    profile_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    snapshot_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    effect_binding_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    selection_digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    proof_digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    original_cause_count = table.Column<int>(type: "INTEGER", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    qualified_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    retain_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_original_effect_completion_receipts", x => x.id);
                    table.CheckConstraint("ck_original_completion_count", "original_cause_count >= 1 AND original_cause_count <= 64");
                    table.CheckConstraint("ck_original_completion_revision", "lease_revision > 0");
                    table.CheckConstraint("ck_original_completion_window", "completed_at >= lease_started_at AND completed_at < qualified_until AND qualified_until <= lease_expires_at AND qualified_until <= retain_until");
                    table.ForeignKey(
                        name: "fk_moderation_original_effect_completion_receipts_moderation_notice_sources_effect_id",
                        column: x => x.effect_id,
                        principalTable: "ie_moderation_notice_sources",
                        principalColumn: "effect_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_operational_budget_adoptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    source_writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    adopted_at = table.Column<long>(type: "INTEGER", nullable: false),
                    valid_until = table.Column<long>(type: "INTEGER", nullable: false),
                    withdrawn_by_human_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    withdrawn_at = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_operational_budget_adoptions", x => x.id);
                    table.UniqueConstraint("ak_moderation_operational_budget_adoption_id_trust_domain_id_tenant_id_source_id", x => new { x.id, x.trust_domain_id, x.tenant_id, x.source_id });
                    table.UniqueConstraint("ak_moderation_operational_budget_adoptions_id_host_binding_id_trust_domain_id_tenant_id_source_id", x => new { x.id, x.host_binding_id, x.trust_domain_id, x.tenant_id, x.source_id });
                    table.CheckConstraint("ck_operational_budget_adoption_generation", "id <> source_id AND source_revision = 1 AND source_writer_generation > 0 AND source_writer_generation < 9223372036854775807 AND writer_generation > source_writer_generation AND writer_generation < 9223372036854775807");
                    table.CheckConstraint("ck_operational_budget_adoption_validity", "adopted_at > 0 AND valid_until > adopted_at AND valid_until < 3155378975999999999");
                    table.CheckConstraint("ck_operational_budget_adoption_withdrawal", "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR (revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_at >= adopted_at AND withdrawn_at < 3155378975999999999 AND withdrawn_by_human_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_moderation_operational_budget_adoptions_moderation_operational_budget_sources_source_id_host_binding_id_trust_do_ddb3af922f29",
                        columns: x => new { x.source_id, x.host_binding_id, x.trust_domain_id, x.tenant_id },
                        principalTable: "ie_moderation_operational_budget_sources",
                        principalColumns: new[] { "id", "host_binding_id", "trust_domain_id", "tenant_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_operational_budget_adoptions_users_adopted_by_human_id",
                        column: x => x.adopted_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_operational_budget_adoptions_users_withdrawn_by_human_id",
                        column: x => x.withdrawn_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_child_age_policy_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    policy_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    policy_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    publication_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    definition_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    profile_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    mode_code = table.Column<int>(type: "INTEGER", nullable: false),
                    proposed_state_digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    scope_ceiling = table.Column<int>(type: "INTEGER", nullable: false),
                    collection = table.Column<int>(type: "INTEGER", nullable: false),
                    consent = table.Column<int>(type: "INTEGER", nullable: false),
                    assurance = table.Column<int>(type: "INTEGER", nullable: false),
                    provider_assurance = table.Column<int>(type: "INTEGER", nullable: false),
                    evidence = table.Column<int>(type: "INTEGER", nullable: false),
                    classification = table.Column<int>(type: "INTEGER", nullable: false),
                    debate_minimum_age = table.Column<int>(type: "INTEGER", nullable: false),
                    audience = table.Column<int>(type: "INTEGER", nullable: false),
                    participant = table.Column<int>(type: "INTEGER", nullable: false),
                    correction = table.Column<int>(type: "INTEGER", nullable: false),
                    protected_access = table.Column<int>(type: "INTEGER", nullable: false),
                    prohibited_inference = table.Column<int>(type: "INTEGER", nullable: false),
                    calendar = table.Column<int>(type: "INTEGER", nullable: false),
                    time_zone = table.Column<int>(type: "INTEGER", nullable: false),
                    leap_day = table.Column<int>(type: "INTEGER", nullable: false),
                    adulthood = table.Column<int>(type: "INTEGER", nullable: false),
                    adulthood_age = table.Column<int>(type: "INTEGER", nullable: false),
                    child_risk_grounds = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    privacy_legal_grounds = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    lawful_collection_purpose = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    consent_guardian_applicability_grounds = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    manual_assurance_qualification_grounds = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    provider_assurance_review_grounds = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    classification_rationale = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    calendar_jurisdiction_grounds = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    reviewer_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    reviewer_controller_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    collection_owner_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    collection_controller_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    governance_controller_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    identity_and_independence_basis = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    privacy_legal_mandate_basis = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    child_safeguarding_mandate_basis = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    collection_mandate_basis = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    recorded_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_child_age_policy_sources", x => x.id);
                    table.CheckConstraint("ck_moderation_child_age_independence", "reviewer_human_id <> recorded_by_human_id AND collection_owner_human_id <> recorded_by_human_id AND reviewer_human_id <> collection_owner_human_id AND reviewer_controller_human_id <> collection_controller_human_id AND reviewer_controller_human_id <> governance_controller_human_id AND collection_controller_human_id <> governance_controller_human_id AND reviewer_human_id <> collection_controller_human_id AND reviewer_human_id <> governance_controller_human_id AND collection_owner_human_id <> reviewer_controller_human_id AND collection_owner_human_id <> governance_controller_human_id AND recorded_by_human_id <> reviewer_controller_human_id AND recorded_by_human_id <> collection_controller_human_id");
                    table.CheckConstraint("ck_moderation_child_age_provider_review", "(provider_assurance = 1 AND provider_assurance_review_grounds IS NULL) OR (provider_assurance = 2 AND provider_assurance_review_grounds IS NOT NULL)");
                    table.CheckConstraint("ck_moderation_child_age_source_generation", "revision = 1 AND policy_revision > 0 AND writer_generation > 0");
                    table.CheckConstraint("ck_moderation_child_age_source_mode", "mode_code BETWEEN 1 AND 2");
                    table.CheckConstraint("ck_moderation_child_age_source_validity", "valid_until > recorded_at");
                    table.CheckConstraint("ck_moderation_child_age_terms", "collection = 1 AND consent = 1 AND assurance = 1 AND evidence = 1 AND classification = 1 AND debate_minimum_age = 18 AND audience = 1 AND participant = 1 AND correction = 1 AND protected_access = 1 AND prohibited_inference = 1 AND calendar = 1 AND time_zone = 1 AND leap_day = 1 AND adulthood = 1 AND adulthood_age BETWEEN 1 AND 130");
                    table.ForeignKey(
                        name: "fk_moderation_child_age_policy_sources_moderation_policy_publications_definition_id",
                        column: x => x.definition_id,
                        principalTable: "ie_moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_child_age_policy_sources_users_collection_controller_human_id",
                        column: x => x.collection_controller_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_child_age_policy_sources_users_collection_owner_human_id",
                        column: x => x.collection_owner_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_child_age_policy_sources_users_governance_controller_human_id",
                        column: x => x.governance_controller_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_child_age_policy_sources_users_recorded_by_human_id",
                        column: x => x.recorded_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_child_age_policy_sources_users_reviewer_controller_human_id",
                        column: x => x.reviewer_controller_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_child_age_policy_sources_users_reviewer_human_id",
                        column: x => x.reviewer_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_constitution_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    policy_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    policy_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    publication_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    definition_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    profile_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    mode_code = table.Column<int>(type: "INTEGER", nullable: false),
                    proposed_state_digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    scope_ceiling = table.Column<int>(type: "INTEGER", nullable: false),
                    evidence = table.Column<int>(type: "INTEGER", nullable: false),
                    remedies = table.Column<int>(type: "INTEGER", nullable: false),
                    independence = table.Column<int>(type: "INTEGER", nullable: false),
                    exposure = table.Column<int>(type: "INTEGER", nullable: false),
                    overload = table.Column<int>(type: "INTEGER", nullable: false),
                    minimum_independent_reviewers = table.Column<int>(type: "INTEGER", nullable: false),
                    protected_complaint_calendar_months = table.Column<int>(type: "INTEGER", nullable: false),
                    maximum_review_interval_ticks = table.Column<long>(type: "INTEGER", nullable: false),
                    escalation_after_ticks = table.Column<long>(type: "INTEGER", nullable: false),
                    support_route = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    independent_redress_route = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    coordinator_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    fallback_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    recorded_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    original_effect_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    original_snapshot_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    original_effect_binding_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    original_custody_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_constitution_sources", x => x.id);
                    table.CheckConstraint("ck_moderation_constitution_original_purpose", "(original_effect_id IS NULL AND original_snapshot_hash IS NULL AND original_effect_binding_hash IS NULL AND original_custody_until IS NULL) OR (original_effect_id IS NOT NULL AND original_snapshot_hash IS NOT NULL AND original_effect_binding_hash IS NOT NULL AND original_custody_until IS NOT NULL AND valid_until <= original_custody_until)");
                    table.CheckConstraint("ck_moderation_constitution_source_mode", "mode_code BETWEEN 1 AND 2");
                    table.CheckConstraint("ck_moderation_constitution_source_revision", "revision = 1 AND policy_revision > 0 AND writer_generation > 0");
                    table.CheckConstraint("ck_moderation_constitution_source_validity", "valid_until > recorded_at");
                    table.CheckConstraint("ck_moderation_constitution_substance", "evidence = 1 AND remedies = 1 AND independence = 1 AND exposure = 1 AND overload = 1 AND minimum_independent_reviewers BETWEEN 1 AND 32 AND protected_complaint_calendar_months >= 6 AND maximum_review_interval_ticks > 0 AND escalation_after_ticks > 0 AND coordinator_human_id <> fallback_human_id");
                    table.ForeignKey(
                        name: "fk_moderation_constitution_sources_moderation_effect_intents_original_effect_id",
                        column: x => x.original_effect_id,
                        principalTable: "ie_moderation_effect_intents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_constitution_sources_moderation_policy_publications_definition_id",
                        column: x => x.definition_id,
                        principalTable: "ie_moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_constitution_sources_users_coordinator_human_id",
                        column: x => x.coordinator_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_constitution_sources_users_fallback_human_id",
                        column: x => x.fallback_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_constitution_sources_users_recorded_by_human_id",
                        column: x => x.recorded_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_consumer_measurement_challenges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    installation_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    installation_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    installation_writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    consumer_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    mode_code = table.Column<int>(type: "INTEGER", nullable: false),
                    runtime_generation = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    inventory_fingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    publication_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    definition_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    binding_digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    consumer_contract_digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    nonce = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    prepared_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    prepared_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    original_effect_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    original_snapshot_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    original_effect_binding_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_consumer_measurement_challenges", x => x.id);
                    table.CheckConstraint("ck_moderation_measurement_challenge_original", "(original_effect_id IS NULL AND original_snapshot_hash IS NULL AND original_effect_binding_hash IS NULL) OR (original_effect_id IS NOT NULL AND original_snapshot_hash IS NOT NULL AND original_effect_binding_hash IS NOT NULL)");
                    table.CheckConstraint("ck_moderation_measurement_challenge_owner", "consumer_id <> '' AND mode_code = 1");
                    table.CheckConstraint("ck_moderation_measurement_challenge_source", "installation_revision = 1 AND installation_writer_generation > 0 AND writer_generation > installation_writer_generation AND revision = 1");
                    table.CheckConstraint("ck_moderation_measurement_challenge_validity", "valid_until > prepared_at");
                    table.ForeignKey(
                        name: "fk_moderation_consumer_measurement_challenges_moderation_consumer_installations_installation_id",
                        column: x => x.installation_id,
                        principalTable: "ie_moderation_consumer_installations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_consumer_measurement_challenges_moderation_effect_intents_original_effect_id",
                        column: x => x.original_effect_id,
                        principalTable: "ie_moderation_effect_intents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_consumer_measurement_challenges_moderation_host_bindings_host_binding_id",
                        column: x => x.host_binding_id,
                        principalTable: "ie_moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_consumer_measurement_challenges_moderation_policy_publications_definition_id",
                        column: x => x.definition_id,
                        principalTable: "ie_moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_consumer_measurement_challenges_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_consumer_measurement_challenges_users_prepared_by_human_id",
                        column: x => x.prepared_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_contribution_selections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    effect_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    decision_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    decision_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    case_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    case_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    policy_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    policy_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    publication_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    profile_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    authored_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    selection_digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    event_window_end = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_contribution_selections", x => x.id);
                    table.CheckConstraint("ck_moderation_contribution_selection_generations", "revision = 1 AND decision_revision > 1 AND case_revision > 0 AND policy_revision > 0 AND writer_generation > 0");
                    table.CheckConstraint("ck_moderation_contribution_selection_validity", "valid_until > recorded_at");
                    table.ForeignKey(
                        name: "fk_moderation_contribution_selections_moderation_cases_case_id",
                        column: x => x.case_id,
                        principalTable: "ie_moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_contribution_selections_moderation_decisions_decision_id",
                        column: x => x.decision_id,
                        principalTable: "ie_moderation_decisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_contribution_selections_moderation_host_bindings_host_binding_id",
                        column: x => x.host_binding_id,
                        principalTable: "ie_moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_contribution_selections_moderation_policy_publications_publication_id",
                        column: x => x.publication_id,
                        principalTable: "ie_moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_contribution_selections_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_contribution_selections_users_authored_by_human_id",
                        column: x => x.authored_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_deployment_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    policy_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    policy_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    publication_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    definition_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    profile_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    mode_code = table.Column<int>(type: "INTEGER", nullable: false),
                    proposed_state_digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    inventory_fingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    database_configuration_digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    database_provider_code = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    database_provider_version = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    recorded_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_deployment_sources", x => x.id);
                    table.CheckConstraint("ck_moderation_deployment_source_mode", "mode_code BETWEEN 1 AND 2");
                    table.CheckConstraint("ck_moderation_deployment_source_revision", "revision = 1 AND policy_revision > 0 AND writer_generation > 0");
                    table.CheckConstraint("ck_moderation_deployment_source_validity", "valid_until > recorded_at");
                    table.ForeignKey(
                        name: "fk_moderation_deployment_sources_moderation_policy_publications_definition_id",
                        column: x => x.definition_id,
                        principalTable: "ie_moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_deployment_sources_users_recorded_by_human_id",
                        column: x => x.recorded_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_event_rights_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    policy_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    policy_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    publication_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    definition_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    profile_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    mode_code = table.Column<int>(type: "INTEGER", nullable: false),
                    proposed_state_digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    scope_ceiling = table.Column<int>(type: "INTEGER", nullable: false),
                    continuity = table.Column<int>(type: "INTEGER", nullable: false),
                    consent = table.Column<int>(type: "INTEGER", nullable: false),
                    missing_steward = table.Column<int>(type: "INTEGER", nullable: false),
                    physical_jurisdiction = table.Column<int>(type: "INTEGER", nullable: false),
                    settlement = table.Column<int>(type: "INTEGER", nullable: false),
                    missing_settlement = table.Column<int>(type: "INTEGER", nullable: false),
                    companions = table.Column<int>(type: "INTEGER", nullable: false),
                    historical_commerce = table.Column<int>(type: "INTEGER", nullable: false),
                    correction = table.Column<int>(type: "INTEGER", nullable: false),
                    event_rights_grounds = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    continuity_grounds = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    premises_jurisdiction_grounds = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    settlement_mapping_grounds = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    reviewer_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    reviewer_controller_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    continuity_mapping_owner_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    continuity_mapping_controller_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    settlement_mapping_owner_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    settlement_mapping_controller_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    governance_controller_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    controller_and_independence_grounds = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    rights_review_mandate_grounds = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    continuity_mapping_mandate_grounds = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    settlement_mapping_mandate_grounds = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    recorded_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_event_rights_sources", x => x.id);
                    table.CheckConstraint("ck_moderation_event_rights_independence", "reviewer_human_id <> recorded_by_human_id AND reviewer_human_id <> continuity_mapping_owner_human_id AND reviewer_human_id <> settlement_mapping_owner_human_id AND reviewer_controller_human_id <> governance_controller_human_id AND reviewer_controller_human_id <> continuity_mapping_controller_human_id AND reviewer_controller_human_id <> settlement_mapping_controller_human_id");
                    table.CheckConstraint("ck_moderation_event_rights_source_generation", "revision = 1 AND policy_revision > 0 AND writer_generation > 0");
                    table.CheckConstraint("ck_moderation_event_rights_source_mode", "mode_code BETWEEN 1 AND 2");
                    table.CheckConstraint("ck_moderation_event_rights_source_validity", "valid_until > recorded_at");
                    table.CheckConstraint("ck_moderation_event_rights_terms", "continuity = 1 AND consent = 1 AND missing_steward = 1 AND physical_jurisdiction = 1 AND settlement = 1 AND missing_settlement = 1 AND companions = 1 AND historical_commerce = 1 AND correction = 1");
                    table.ForeignKey(
                        name: "fk_moderation_event_rights_sources_moderation_policy_publications_definition_id",
                        column: x => x.definition_id,
                        principalTable: "ie_moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_event_rights_sources_users_continuity_mapping_controller_human_id",
                        column: x => x.continuity_mapping_controller_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_event_rights_sources_users_continuity_mapping_owner_human_id",
                        column: x => x.continuity_mapping_owner_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_event_rights_sources_users_governance_controller_human_id",
                        column: x => x.governance_controller_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_event_rights_sources_users_recorded_by_human_id",
                        column: x => x.recorded_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_event_rights_sources_users_reviewer_controller_human_id",
                        column: x => x.reviewer_controller_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_event_rights_sources_users_reviewer_human_id",
                        column: x => x.reviewer_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_event_rights_sources_users_settlement_mapping_controller_human_id",
                        column: x => x.settlement_mapping_controller_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_event_rights_sources_users_settlement_mapping_owner_human_id",
                        column: x => x.settlement_mapping_owner_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_measured_operations_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    policy_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    policy_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    publication_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    definition_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    profile_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    mode_code = table.Column<int>(type: "INTEGER", nullable: false),
                    binding_digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    proposed_state_digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    evidence_json = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: false),
                    reviewer_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    reviewer_controller_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    recorder_controller_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    recorded_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    original_effect_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    original_snapshot_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    original_effect_binding_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    original_custody_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_measured_operations_sources", x => x.id);
                    table.CheckConstraint("ck_moderation_measured_source_generation", "revision = 1 AND policy_revision > 0 AND writer_generation > 0");
                    table.CheckConstraint("ck_moderation_measured_source_independence", "reviewer_human_id <> recorded_by_human_id AND reviewer_controller_human_id <> recorder_controller_human_id AND reviewer_human_id <> recorder_controller_human_id AND recorded_by_human_id <> reviewer_controller_human_id");
                    table.CheckConstraint("ck_moderation_measured_source_mode", "mode_code BETWEEN 1 AND 2");
                    table.CheckConstraint("ck_moderation_measured_source_original", "(original_effect_id IS NULL AND original_snapshot_hash IS NULL AND original_effect_binding_hash IS NULL AND original_custody_until IS NULL) OR (original_effect_id IS NOT NULL AND original_snapshot_hash IS NOT NULL AND original_effect_binding_hash IS NOT NULL AND original_custody_until IS NOT NULL AND original_custody_until > recorded_at AND valid_until <= original_custody_until)");
                    table.CheckConstraint("ck_moderation_measured_source_validity", "valid_until > recorded_at");
                    table.ForeignKey(
                        name: "fk_moderation_measured_operations_sources_moderation_effect_intents_original_effect_id",
                        column: x => x.original_effect_id,
                        principalTable: "ie_moderation_effect_intents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_measured_operations_sources_moderation_policy_publications_definition_id",
                        column: x => x.definition_id,
                        principalTable: "ie_moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_measured_operations_sources_users_recorded_by_human_id",
                        column: x => x.recorded_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_measured_operations_sources_users_recorder_controller_human_id",
                        column: x => x.recorder_controller_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_measured_operations_sources_users_reviewer_controller_human_id",
                        column: x => x.reviewer_controller_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_measured_operations_sources_users_reviewer_human_id",
                        column: x => x.reviewer_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_notice_disclosure_approvals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    effect_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    decision_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    decision_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    case_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    case_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    policy_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    policy_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    publication_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    profile_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    contribution_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    selection_digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    rule_meaning = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    reason_meaning = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    facts_json = table.Column<string>(type: "TEXT", maxLength: 131072, nullable: false),
                    remaining_actions = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    recourse = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    automation = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    retention = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    content_representatives_json = table.Column<string>(type: "TEXT", maxLength: 8192, nullable: false),
                    approved_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    retain_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    revoked_by_human_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_notice_disclosure_approvals", x => x.id);
                    table.CheckConstraint("ck_moderation_notice_disclosure_generations", "decision_revision > 1 AND case_revision > 0 AND policy_revision > 0 AND writer_generation > 0");
                    table.CheckConstraint("ck_moderation_notice_disclosure_retention", "retain_until > approved_at");
                    table.CheckConstraint("ck_moderation_notice_disclosure_revocation", "(revision = 1 AND revoked_at IS NULL AND revoked_by_human_id IS NULL) OR (revision = 2 AND revoked_at IS NOT NULL AND revoked_by_human_id IS NOT NULL AND revoked_at >= approved_at)");
                    table.CheckConstraint("ck_moderation_notice_disclosure_validity", "valid_until > approved_at");
                    table.ForeignKey(
                        name: "fk_moderation_notice_disclosure_approvals_moderation_cases_case_id",
                        column: x => x.case_id,
                        principalTable: "ie_moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_notice_disclosure_approvals_moderation_decisions_decision_id",
                        column: x => x.decision_id,
                        principalTable: "ie_moderation_decisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_notice_disclosure_approvals_moderation_host_bindings_host_binding_id",
                        column: x => x.host_binding_id,
                        principalTable: "ie_moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_notice_disclosure_approvals_moderation_policy_publications_publication_id",
                        column: x => x.publication_id,
                        principalTable: "ie_moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_notice_disclosure_approvals_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_notice_disclosure_approvals_users_approved_by_human_id",
                        column: x => x.approved_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_notice_disclosure_approvals_users_revoked_by_human_id",
                        column: x => x.revoked_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_policy_publication_candidates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    definition_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    policy_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    expected_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    previous_publication_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    solution_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    instance_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    jurisdiction_tenant_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    prepared_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    operation_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    request_digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    proposed_state_digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    authority_source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    authority_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    prepared_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_policy_publication_candidates", x => x.id);
                    table.CheckConstraint("ck_moderation_publication_candidate_history", "(expected_revision = 0 AND previous_publication_id IS NULL) OR (expected_revision > 0 AND previous_publication_id IS NOT NULL)");
                    table.CheckConstraint("ck_moderation_publication_candidate_identity", "id <> definition_id");
                    table.CheckConstraint("ck_moderation_publication_candidate_revision", "expected_revision >= 0 AND expected_revision < 9223372036854775807 AND revision = 1");
                    table.CheckConstraint("ck_moderation_publication_candidate_source", "authority_revision > 0 AND authority_revision < 9223372036854775806 AND writer_generation = authority_revision + 1");
                    table.CheckConstraint("ck_moderation_publication_candidate_validity", "valid_until > prepared_at");
                    table.ForeignKey(
                        name: "fk_moderation_policy_publication_candidates_moderation_host_bindings_host_binding_id",
                        column: x => x.host_binding_id,
                        principalTable: "ie_moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_policy_publication_candidates_moderation_policy_publications_policy_id_definition_id",
                        columns: x => new { x.policy_id, x.definition_id },
                        principalTable: "ie_moderation_policy_publications",
                        principalColumns: new[] { "policy_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_policy_publication_candidates_moderation_policy_publications_policy_id_previous_publication_id",
                        columns: x => new { x.policy_id, x.previous_publication_id },
                        principalTable: "ie_moderation_policy_publications",
                        principalColumns: new[] { "policy_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_policy_publication_candidates_moderation_qualification_epochs_authority_source_id",
                        column: x => x.authority_source_id,
                        principalTable: "ie_moderation_qualification_epochs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_policy_publication_candidates_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_policy_publication_candidates_users_prepared_by_human_id",
                        column: x => x.prepared_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_policy_selections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    jurisdiction_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    jurisdiction_tenant_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    latest_publication_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    active_publication_id = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_policy_selections", x => x.id);
                    table.CheckConstraint("ck_moderation_selection_revision", "revision > 0");
                    table.ForeignKey(
                        name: "fk_moderation_policy_selections_moderation_policy_publications_id_active_publication_id",
                        columns: x => new { x.id, x.active_publication_id },
                        principalTable: "ie_moderation_policy_publications",
                        principalColumns: new[] { "policy_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_policy_selections_moderation_policy_publications_id_latest_publication_id",
                        columns: x => new { x.id, x.latest_publication_id },
                        principalTable: "ie_moderation_policy_publications",
                        principalColumns: new[] { "policy_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_publication_classes",
                columns: table => new
                {
                    publication_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    code = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    meaning = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    ground = table.Column<int>(type: "INTEGER", nullable: false),
                    basis = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_publication_classes", x => new { x.publication_id, x.code, x.revision });
                    table.ForeignKey(
                        name: "fk_moderation_publication_classes_moderation_policy_publications_publication_id",
                        column: x => x.publication_id,
                        principalTable: "ie_moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_publication_evidence",
                columns: table => new
                {
                    publication_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    requirement_digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    approval_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    approval_revision = table.Column<long>(type: "INTEGER", nullable: true),
                    source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    writer_generation = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    observed_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_publication_evidence", x => new { x.publication_id, x.requirement_digest });
                    table.CheckConstraint("ck_moderation_publication_evidence", "source_revision > 0 AND ((approval_id IS NULL AND approval_revision IS NULL) OR (approval_id IS NOT NULL AND approval_revision > 0))");
                    table.ForeignKey(
                        name: "fk_moderation_publication_evidence_moderation_policy_publications_publication_id",
                        column: x => x.publication_id,
                        principalTable: "ie_moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_publication_modes",
                columns: table => new
                {
                    publication_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    mode = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_publication_modes", x => new { x.publication_id, x.mode });
                    table.CheckConstraint("ck_moderation_publication_mode", "mode BETWEEN 1 AND 2");
                    table.ForeignKey(
                        name: "fk_moderation_publication_modes_moderation_policy_publications_publication_id",
                        column: x => x.publication_id,
                        principalTable: "ie_moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_recognition_privacy_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    policy_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    policy_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    publication_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    definition_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    profile_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    mode_code = table.Column<int>(type: "INTEGER", nullable: false),
                    proposed_state_digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    purposes = table.Column<int>(type: "INTEGER", nullable: false),
                    scope_ceiling = table.Column<int>(type: "INTEGER", nullable: false),
                    linkage = table.Column<int>(type: "INTEGER", nullable: false),
                    ordinary_erasure = table.Column<int>(type: "INTEGER", nullable: false),
                    correction = table.Column<int>(type: "INTEGER", nullable: false),
                    catastrophic_disposition = table.Column<int>(type: "INTEGER", nullable: false),
                    key_obligations = table.Column<int>(type: "INTEGER", nullable: false),
                    protected_remedy_calendar_months = table.Column<int>(type: "INTEGER", nullable: false),
                    escalation_after_ticks = table.Column<long>(type: "INTEGER", nullable: false),
                    lawful_request_deadline_ticks = table.Column<long>(type: "INTEGER", nullable: false),
                    maximum_backup_horizon_ticks = table.Column<long>(type: "INTEGER", nullable: false),
                    backup_safety_margin_ticks = table.Column<long>(type: "INTEGER", nullable: false),
                    privacy_owner_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    privacy_controller_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    recovery_owner_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    recovery_controller_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    governance_controller_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    identity_and_independence_basis = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    privacy_legal_mandate_basis = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    recovery_mandate_basis = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    recorded_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_recognition_privacy_sources", x => x.id);
                    table.CheckConstraint("ck_moderation_recognition_privacy_independence", "privacy_owner_human_id <> recorded_by_human_id AND recovery_owner_human_id <> recorded_by_human_id AND privacy_owner_human_id <> recovery_owner_human_id AND privacy_controller_human_id <> recovery_controller_human_id AND privacy_controller_human_id <> governance_controller_human_id AND recovery_controller_human_id <> governance_controller_human_id");
                    table.CheckConstraint("ck_moderation_recognition_privacy_source_generation", "revision = 1 AND policy_revision > 0 AND writer_generation > 0");
                    table.CheckConstraint("ck_moderation_recognition_privacy_source_mode", "mode_code BETWEEN 1 AND 2");
                    table.CheckConstraint("ck_moderation_recognition_privacy_source_validity", "valid_until > recorded_at");
                    table.CheckConstraint("ck_moderation_recognition_privacy_terms", "purposes BETWEEN 1 AND 3 AND linkage = 1 AND ordinary_erasure = 1 AND correction = 1 AND catastrophic_disposition = 1 AND key_obligations = 1 AND protected_remedy_calendar_months BETWEEN 1 AND 1200 AND escalation_after_ticks > 0 AND lawful_request_deadline_ticks > escalation_after_ticks AND maximum_backup_horizon_ticks > 0 AND backup_safety_margin_ticks > 0");
                    table.ForeignKey(
                        name: "fk_moderation_recognition_privacy_sources_moderation_policy_publications_definition_id",
                        column: x => x.definition_id,
                        principalTable: "ie_moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_recognition_privacy_sources_users_governance_controller_human_id",
                        column: x => x.governance_controller_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_recognition_privacy_sources_users_privacy_controller_human_id",
                        column: x => x.privacy_controller_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_recognition_privacy_sources_users_privacy_owner_human_id",
                        column: x => x.privacy_owner_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_recognition_privacy_sources_users_recorded_by_human_id",
                        column: x => x.recorded_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_recognition_privacy_sources_users_recovery_controller_human_id",
                        column: x => x.recovery_controller_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_recognition_privacy_sources_users_recovery_owner_human_id",
                        column: x => x.recovery_owner_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_qualification_policy_adoptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    case_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    case_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    maximum_reviewer_lifetime_ticks = table.Column<long>(type: "INTEGER", nullable: false),
                    maximum_urgency_lifetime_ticks = table.Column<long>(type: "INTEGER", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    adopted_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    revoked_by_human_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_qualification_policy_adoptions", x => x.id);
                    table.CheckConstraint("ck_moderation_qualification_policy_adoption_generation", "id <> source_id AND source_revision > 0 AND case_revision > 0 AND writer_generation > 0");
                    table.CheckConstraint("ck_moderation_qualification_policy_adoption_limits", "maximum_reviewer_lifetime_ticks > 0 AND maximum_reviewer_lifetime_ticks < 9223372036854775807 AND maximum_urgency_lifetime_ticks > 0 AND maximum_urgency_lifetime_ticks < 9223372036854775807");
                    table.CheckConstraint("ck_moderation_qualification_policy_adoption_revocation", "(revision = 1 AND revoked_at IS NULL AND revoked_by_human_id IS NULL) OR (revision = 2 AND revoked_at IS NOT NULL AND revoked_by_human_id IS NOT NULL AND revoked_at >= adopted_at)");
                    table.CheckConstraint("ck_moderation_qualification_policy_adoption_validity", "valid_until > adopted_at");
                    table.ForeignKey(
                        name: "fk_moderation_qualification_policy_adoptions_moderation_qualification_policy_sources_source_id_host_binding_id_trus_cf4e4a3d694c",
                        columns: x => new { x.source_id, x.host_binding_id, x.trust_domain_id, x.tenant_id, x.case_id },
                        principalTable: "ie_moderation_qualification_policy_sources",
                        principalColumns: new[] { "id", "host_binding_id", "trust_domain_id", "tenant_id", "case_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_qualification_policy_adoptions_users_adopted_by_human_id",
                        column: x => x.adopted_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_qualification_policy_adoptions_users_revoked_by_human_id",
                        column: x => x.revoked_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_recognition_disposition_acceptance",
                columns: table => new
                {
                    disposition_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    role = table.Column<int>(type: "INTEGER", nullable: false),
                    human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    accepted_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    request_digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_recognition_disposition_acceptance", x => new { x.disposition_id, x.role });
                    table.ForeignKey(
                        name: "fk_moderation_recognition_disposition_acceptance_moderation_recognition_disposition_disposition_id",
                        column: x => x.disposition_id,
                        principalTable: "ie_moderation_recognition_disposition",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_recognition_key_lifecycle_key",
                columns: table => new
                {
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    key_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    key_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    verification_tag = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    state = table.Column<int>(type: "INTEGER", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    original_obligations_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    required_erasure_head = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_recognition_key_lifecycle_key", x => new { x.trust_domain_id, x.key_id });
                    table.ForeignKey(
                        name: "fk_moderation_recognition_key_lifecycle_key_moderation_recognition_key_lifecycle_trust_domain_id",
                        column: x => x.trust_domain_id,
                        principalTable: "ie_moderation_recognition_key_lifecycle",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_case_narratives",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    case_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    reporter_user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    receipt_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_version = table.Column<Guid>(type: "TEXT", nullable: false),
                    material_fact_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    policy_source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    adoption_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    policy_writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    adoption_writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    qualification_epoch_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    qualification_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    purge_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    protected_material = table.Column<byte[]>(type: "BLOB", maxLength: 131072, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_case_narratives", x => x.id);
                    table.CheckConstraint("ck_case_narrative_finite_custody", "purge_at > captured_at");
                    table.CheckConstraint("ck_case_narrative_generations", "policy_writer_generation > 0 AND adoption_writer_generation > policy_writer_generation AND qualification_revision >= adoption_writer_generation");
                    table.ForeignKey(
                        name: "fk_moderation_case_narratives_moderation_case_narrative_adoptions_adoption_id_host_binding_id_trust_domain_id_tenan_514a35efa48d",
                        columns: x => new { x.adoption_id, x.host_binding_id, x.trust_domain_id, x.tenant_id, x.policy_source_id },
                        principalTable: "ie_moderation_case_narrative_adoptions",
                        principalColumns: new[] { "id", "host_binding_id", "trust_domain_id", "tenant_id", "source_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_narratives_moderation_case_target_receipts_receipt_id",
                        column: x => x.receipt_id,
                        principalTable: "ie_moderation_case_target_receipts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_narratives_moderation_case_target_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "ie_moderation_case_target_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_narratives_moderation_cases_case_id",
                        column: x => x.case_id,
                        principalTable: "ie_moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_narratives_moderation_qualification_epochs_qualification_epoch_id",
                        column: x => x.qualification_epoch_id,
                        principalTable: "ie_moderation_qualification_epochs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_narratives_users_reporter_user_id",
                        column: x => x.reporter_user_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_case_attribution_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    case_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    generation = table.Column<long>(type: "INTEGER", nullable: false),
                    occurrence_case_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    occurrence_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    occurrence_case_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    evidence_case_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    evidence_source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    evidence_source_version = table.Column<Guid>(type: "TEXT", nullable: false),
                    material_fact_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    investigation_source_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    investigation_source_revision = table.Column<long>(type: "INTEGER", nullable: true),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    qualification_epoch_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    qualification_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    basis = table.Column<int>(type: "INTEGER", nullable: false),
                    subject_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    subject_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    qualified_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    qualifier_role_grant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    qualifier_platform_grant = table.Column<bool>(type: "INTEGER", nullable: false),
                    read_grant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    read_grant_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    lifetime_policy_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    lifetime_policy_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    native_responsibility_digest = table.Column<string>(type: "TEXT", unicode: false, maxLength: 64, nullable: false),
                    qualification_facts_digest = table.Column<string>(type: "TEXT", unicode: false, maxLength: 64, nullable: false),
                    qualified_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    withdrawn_by_human_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    withdrawal_operation_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_case_attribution_sources", x => x.id);
                    table.CheckConstraint("ck_moderation_attribution_basis", "(investigation_source_id IS NULL AND investigation_source_revision IS NULL AND ((basis = 1 AND subject_kind = 1 AND subject_id <> qualified_by_human_id) OR (basis = 2 AND subject_kind = 4) OR (basis = 3 AND subject_kind = 3) OR (basis = 4 AND subject_kind = 2))) OR (basis = 5 AND subject_kind = 1 AND subject_id <> qualified_by_human_id AND investigation_source_id IS NOT NULL AND investigation_source_revision IS NOT NULL AND investigation_source_revision = 1)");
                    table.CheckConstraint("ck_moderation_attribution_evidence", "generation > 0 AND case_id <> occurrence_case_id AND evidence_case_revision > 0 AND occurrence_case_revision >= evidence_case_revision AND qualification_revision > 0 AND read_grant_revision > 0 AND lifetime_policy_revision > 0");
                    table.CheckConstraint("ck_moderation_attribution_validity", "valid_until > qualified_at AND ((revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL AND withdrawal_operation_id IS NULL) OR (revision = 2 AND withdrawn_at >= qualified_at AND withdrawn_by_human_id IS NOT NULL AND withdrawal_operation_id IS NOT NULL))");
                    table.ForeignKey(
                        name: "fk_moderation_case_attribution_sources_moderation_case_read_grants_read_grant_id",
                        column: x => x.read_grant_id,
                        principalTable: "ie_moderation_case_read_grants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_attribution_sources_moderation_case_target_sources_evidence_source_id",
                        column: x => x.evidence_source_id,
                        principalTable: "ie_moderation_case_target_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_attribution_sources_moderation_cases_case_id",
                        column: x => x.case_id,
                        principalTable: "ie_moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_attribution_sources_moderation_cases_occurrence_case_id",
                        column: x => x.occurrence_case_id,
                        principalTable: "ie_moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_attribution_sources_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_case_read_grant_revocation_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    grant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    expected_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    administrator_user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    administrator_role_grant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    administrator_platform_grant = table.Column<bool>(type: "INTEGER", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_case_read_grant_revocation_receipts", x => x.id);
                    table.CheckConstraint("ck_moderation_read_grant_revocation_revision", "expected_revision = 1");
                    table.ForeignKey(
                        name: "fk_moderation_case_read_grant_revocation_receipts_moderation_case_read_grants_grant_id",
                        column: x => x.grant_id,
                        principalTable: "ie_moderation_case_read_grants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_read_grant_revocation_receipts_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_read_grant_revocation_receipts_users_administrator_user_id",
                        column: x => x.administrator_user_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_content_responses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    notice_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    effect_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    case_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    content_subject_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    representative_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    assignment_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    assignment_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    snapshot_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    request_digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    retain_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    protected_statement = table.Column<byte[]>(type: "BLOB", maxLength: 16384, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_content_responses", x => x.id);
                    table.CheckConstraint("ck_content_response_assignment", "assignment_revision > 0");
                    table.CheckConstraint("ck_content_response_finite_purpose", "retain_until > submitted_at");
                    table.ForeignKey(
                        name: "fk_moderation_content_responses_moderation_cases_case_id",
                        column: x => x.case_id,
                        principalTable: "ie_moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_content_responses_moderation_notice_sources_effect_id",
                        column: x => x.effect_id,
                        principalTable: "ie_moderation_notice_sources",
                        principalColumn: "effect_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_content_responses_moderation_notices_notice_id",
                        column: x => x.notice_id,
                        principalTable: "ie_moderation_notices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_content_responses_users_representative_human_id",
                        column: x => x.representative_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_notice_notification_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    notice_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    effect_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    subject_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    subject_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    acknowledged_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    snapshot_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    contestation_calendar_months = table.Column<int>(type: "INTEGER", nullable: false),
                    notified_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    remedy_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    retain_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_notice_notification_receipts", x => x.id);
                    table.CheckConstraint("ck_moderation_notice_notification_calendar", "contestation_calendar_months >= 0 AND contestation_calendar_months <= 120");
                    table.CheckConstraint("ck_moderation_notice_notification_subject", "subject_kind IN (1, 4) AND (subject_kind <> 1 OR acknowledged_by_human_id = subject_id)");
                    table.CheckConstraint("ck_moderation_notice_notification_time", "remedy_until >= notified_at AND retain_until >= remedy_until AND retain_until > notified_at");
                    table.ForeignKey(
                        name: "fk_moderation_notice_notification_receipts_moderation_notice_sources_effect_id",
                        column: x => x.effect_id,
                        principalTable: "ie_moderation_notice_sources",
                        principalColumn: "effect_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_notice_notification_receipts_moderation_notices_notice_id",
                        column: x => x.notice_id,
                        principalTable: "ie_moderation_notices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_notice_notification_receipts_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_case_intake_acknowledgements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    caller_user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    case_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    case_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    receipt_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    receipt_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    operation_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    evidence_case_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    budget_source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    budget_adoption_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    budget_source_writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    budget_adoption_writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    request_digest = table.Column<string>(type: "TEXT", unicode: false, maxLength: 64, nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_case_intake_acknowledgements", x => x.id);
                    table.CheckConstraint("ck_moderation_case_acknowledgement_revisions", "case_revision > 0 AND receipt_revision = 2 AND evidence_case_revision > 0 AND evidence_case_revision <= case_revision AND budget_source_writer_generation > 0 AND budget_adoption_writer_generation > budget_source_writer_generation AND budget_adoption_writer_generation < 9223372036854775807 AND budget_source_id <> budget_adoption_id");
                    table.ForeignKey(
                        name: "fk_moderation_case_intake_acknowledgements_moderation_case_target_receipts_receipt_id",
                        column: x => x.receipt_id,
                        principalTable: "ie_moderation_case_target_receipts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_intake_acknowledgements_moderation_cases_case_id",
                        column: x => x.case_id,
                        principalTable: "ie_moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_intake_acknowledgements_moderation_operational_budget_adoptions_budget_adoption_id_trust_domain__61df15e26135",
                        columns: x => new { x.budget_adoption_id, x.trust_domain_id, x.tenant_id, x.budget_source_id },
                        principalTable: "ie_moderation_operational_budget_adoptions",
                        principalColumns: new[] { "id", "trust_domain_id", "tenant_id", "source_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_intake_acknowledgements_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_child_age_policy_adoptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    source_writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    adopted_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    withdrawn_by_human_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_child_age_policy_adoptions", x => x.id);
                    table.CheckConstraint("ck_moderation_child_age_adoption_generation", "source_revision = 1 AND source_writer_generation > 0 AND writer_generation > source_writer_generation");
                    table.CheckConstraint("ck_moderation_child_age_adoption_validity", "valid_until > adopted_at");
                    table.CheckConstraint("ck_moderation_child_age_adoption_withdrawal", "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR (revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at >= adopted_at)");
                    table.ForeignKey(
                        name: "fk_moderation_child_age_policy_adoptions_moderation_child_age_policy_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "ie_moderation_child_age_policy_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_child_age_policy_adoptions_users_adopted_by_human_id",
                        column: x => x.adopted_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_child_age_policy_adoptions_users_withdrawn_by_human_id",
                        column: x => x.withdrawn_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_constitution_adoptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    source_writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    adopted_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    withdrawn_by_human_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_constitution_adoptions", x => x.id);
                    table.CheckConstraint("ck_moderation_constitution_adoption_generation", "source_revision = 1 AND source_writer_generation > 0 AND writer_generation > source_writer_generation");
                    table.CheckConstraint("ck_moderation_constitution_adoption_validity", "valid_until > adopted_at");
                    table.CheckConstraint("ck_moderation_constitution_adoption_withdrawal", "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR (revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at >= adopted_at)");
                    table.ForeignKey(
                        name: "fk_moderation_constitution_adoptions_moderation_constitution_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "ie_moderation_constitution_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_constitution_adoptions_users_adopted_by_human_id",
                        column: x => x.adopted_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_constitution_adoptions_users_withdrawn_by_human_id",
                        column: x => x.withdrawn_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_constitution_reviewers",
                columns: table => new
                {
                    source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    availability_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    availability_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    availability_writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    reviewer_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    controller_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    identity_basis = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    qualification_basis = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    independence_basis = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_constitution_reviewers", x => new { x.source_id, x.ordinal });
                    table.CheckConstraint("ck_moderation_constitution_reviewer_generation", "ordinal >= 0 AND ordinal < 32 AND availability_revision = 1 AND availability_writer_generation > 0");
                    table.ForeignKey(
                        name: "fk_moderation_constitution_reviewers_moderation_constitution_availability_availability_id",
                        column: x => x.availability_id,
                        principalTable: "ie_moderation_constitution_availability",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_constitution_reviewers_moderation_constitution_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "ie_moderation_constitution_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_constitution_reviewers_users_controller_human_id",
                        column: x => x.controller_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_constitution_reviewers_users_reviewer_human_id",
                        column: x => x.reviewer_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_consumer_measurement_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    challenge_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    issuer_enrollment_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    issuer_key_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    run_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    signed_token = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: false),
                    token_digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    accepted_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_consumer_measurement_receipts", x => x.id);
                    table.CheckConstraint("ck_moderation_measurement_receipt_generation", "writer_generation > 0 AND writer_generation < 9223372036854775807 AND revision = 1");
                    table.CheckConstraint("ck_moderation_measurement_receipt_validity", "valid_until > accepted_at");
                    table.ForeignKey(
                        name: "fk_moderation_consumer_measurement_receipts_moderation_consumer_measurement_challenges_challenge_id",
                        column: x => x.challenge_id,
                        principalTable: "ie_moderation_consumer_measurement_challenges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_consumer_measurement_receipts_moderation_host_bindings_host_binding_id",
                        column: x => x.host_binding_id,
                        principalTable: "ie_moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_consumer_measurement_receipts_moderation_measurement_issuer_enrollments_issuer_enrollment_id",
                        column: x => x.issuer_enrollment_id,
                        principalTable: "ie_moderation_measurement_issuer_enrollments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_consumer_measurement_receipts_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_consumer_measurement_receipts_users_accepted_by_human_id",
                        column: x => x.accepted_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_contribution_bindings",
                columns: table => new
                {
                    selection_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    kind = table.Column<int>(type: "INTEGER", nullable: false),
                    effect_kind = table.Column<int>(type: "INTEGER", nullable: true),
                    effect_mode = table.Column<int>(type: "INTEGER", nullable: true),
                    effect_target = table.Column<int>(type: "INTEGER", nullable: true),
                    capability = table.Column<int>(type: "INTEGER", nullable: true),
                    execution = table.Column<int>(type: "INTEGER", nullable: true),
                    strategy = table.Column<int>(type: "INTEGER", nullable: true),
                    scope_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    scope_trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    scope_solution_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    scope_instance_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    scope_tenant_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    scope_community_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    scope_resource_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    scope_event_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    subject_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    subject_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    target_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    target_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    operation = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    window_end = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_contribution_bindings", x => new { x.selection_id, x.ordinal });
                    table.CheckConstraint("ck_moderation_contribution_binding_meaning", "(kind = 1 AND effect_kind IS NOT NULL AND effect_mode IS NOT NULL AND effect_target IS NOT NULL AND execution IS NOT NULL AND strategy IS NULL) OR (kind = 2 AND effect_kind IS NULL AND effect_mode IS NULL AND effect_target IS NULL AND capability IS NULL AND execution IS NULL)");
                    table.CheckConstraint("ck_moderation_contribution_binding_partition", "ordinal >= 0 AND ordinal < 64 AND target_revision > 0");
                    table.CheckConstraint("ck_moderation_contribution_bindings_scope_scope", "scope_kind IS NOT NULL AND ((scope_kind = 1 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 2 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 3 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 4 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 5 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NOT NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 6 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_resource_id IS NOT NULL AND scope_event_id IS NULL) OR (scope_kind = 7 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NOT NULL AND scope_event_id <> '00000000-0000-0000-0000-000000000000'))");
                    table.ForeignKey(
                        name: "fk_moderation_contribution_bindings_moderation_contribution_selections_selection_id",
                        column: x => x.selection_id,
                        principalTable: "ie_moderation_contribution_selections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_profile_adoptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    selection_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    selection_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    selection_writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    publication_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    profile_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    case_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    case_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    mode_code = table.Column<int>(type: "INTEGER", nullable: false),
                    prerequisite_mask = table.Column<int>(type: "INTEGER", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    adopted_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    revoked_by_human_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_profile_adoptions", x => x.id);
                    table.CheckConstraint("ck_moderation_profile_adoption_mode", "mode_code BETWEEN 1 AND 2");
                    table.CheckConstraint("ck_moderation_profile_adoption_revision", "selection_revision = 1 AND selection_writer_generation > 0 AND case_revision > 0 AND writer_generation > 0");
                    table.CheckConstraint("ck_moderation_profile_adoption_revocation", "(revision = 1 AND revoked_at IS NULL AND revoked_by_human_id IS NULL) OR (revision = 2 AND revoked_at IS NOT NULL AND revoked_by_human_id IS NOT NULL AND revoked_at >= adopted_at)");
                    table.CheckConstraint("ck_moderation_profile_adoption_validity", "valid_until > adopted_at");
                    table.ForeignKey(
                        name: "fk_moderation_profile_adoptions_moderation_contribution_selections_selection_id",
                        column: x => x.selection_id,
                        principalTable: "ie_moderation_contribution_selections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_profile_adoptions_moderation_policy_publications_publication_id",
                        column: x => x.publication_id,
                        principalTable: "ie_moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_profile_adoptions_users_adopted_by_human_id",
                        column: x => x.adopted_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_profile_adoptions_users_revoked_by_human_id",
                        column: x => x.revoked_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_deployment_adoptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    source_writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    adopted_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    withdrawn_by_human_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_deployment_adoptions", x => x.id);
                    table.CheckConstraint("ck_moderation_deployment_adoption_generation", "source_revision = 1 AND source_writer_generation > 0 AND writer_generation > source_writer_generation");
                    table.CheckConstraint("ck_moderation_deployment_adoption_validity", "valid_until > adopted_at");
                    table.CheckConstraint("ck_moderation_deployment_adoption_withdrawal", "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR (revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at >= adopted_at)");
                    table.ForeignKey(
                        name: "fk_moderation_deployment_adoptions_moderation_deployment_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "ie_moderation_deployment_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_deployment_adoptions_users_adopted_by_human_id",
                        column: x => x.adopted_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_deployment_adoptions_users_withdrawn_by_human_id",
                        column: x => x.withdrawn_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_deployment_consumers",
                columns: table => new
                {
                    source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    consumer_id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    binding_digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    contract_digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    installation_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    installation_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    installation_writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    runtime_generation = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    product_artifact_digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_deployment_consumers", x => new { x.source_id, x.ordinal });
                    table.CheckConstraint("ck_moderation_deployment_consumer_revision", "ordinal >= 0 AND ordinal < 256 AND installation_revision = 1 AND installation_writer_generation > 0");
                    table.ForeignKey(
                        name: "fk_moderation_deployment_consumers_moderation_consumer_installations_installation_id",
                        column: x => x.installation_id,
                        principalTable: "ie_moderation_consumer_installations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_deployment_consumers_moderation_deployment_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "ie_moderation_deployment_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_event_rights_adoptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    source_writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    adopted_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    withdrawn_by_human_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_event_rights_adoptions", x => x.id);
                    table.CheckConstraint("ck_moderation_event_rights_adoption_generation", "source_revision = 1 AND source_writer_generation > 0 AND writer_generation > source_writer_generation");
                    table.CheckConstraint("ck_moderation_event_rights_adoption_validity", "valid_until > adopted_at");
                    table.CheckConstraint("ck_moderation_event_rights_adoption_withdrawal", "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR (revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at >= adopted_at)");
                    table.ForeignKey(
                        name: "fk_moderation_event_rights_adoptions_moderation_event_rights_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "ie_moderation_event_rights_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_event_rights_adoptions_users_adopted_by_human_id",
                        column: x => x.adopted_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_event_rights_adoptions_users_withdrawn_by_human_id",
                        column: x => x.withdrawn_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_measured_operations_acceptances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    source_writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    accepted_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    withdrawn_by_human_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_measured_operations_acceptances", x => x.id);
                    table.CheckConstraint("ck_moderation_measured_acceptance_generation", "source_revision = 1 AND source_writer_generation > 0 AND writer_generation > source_writer_generation");
                    table.CheckConstraint("ck_moderation_measured_acceptance_validity", "valid_until > accepted_at");
                    table.CheckConstraint("ck_moderation_measured_acceptance_withdrawal", "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR (revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at >= accepted_at)");
                    table.ForeignKey(
                        name: "fk_moderation_measured_operations_acceptances_moderation_measured_operations_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "ie_moderation_measured_operations_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_measured_operations_acceptances_users_accepted_by_human_id",
                        column: x => x.accepted_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_measured_operations_acceptances_users_withdrawn_by_human_id",
                        column: x => x.withdrawn_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_publication_reasons",
                columns: table => new
                {
                    publication_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    code = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    meaning = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    class_code = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    class_revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_publication_reasons", x => new { x.publication_id, x.code, x.revision });
                    table.ForeignKey(
                        name: "fk_moderation_publication_reasons_moderation_policy_publications_publication_id",
                        column: x => x.publication_id,
                        principalTable: "ie_moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_publication_reasons_moderation_publication_classes_publication_id_class_code_class_revision",
                        columns: x => new { x.publication_id, x.class_code, x.class_revision },
                        principalTable: "ie_moderation_publication_classes",
                        principalColumns: new[] { "publication_id", "code", "revision" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_recognition_privacy_adoptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    source_writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    adopted_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    withdrawn_by_human_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_recognition_privacy_adoptions", x => x.id);
                    table.CheckConstraint("ck_moderation_recognition_privacy_adoption_generation", "source_revision = 1 AND source_writer_generation > 0 AND writer_generation > source_writer_generation");
                    table.CheckConstraint("ck_moderation_recognition_privacy_adoption_validity", "valid_until > adopted_at");
                    table.CheckConstraint("ck_moderation_recognition_privacy_adoption_withdrawal", "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR (revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at >= adopted_at)");
                    table.ForeignKey(
                        name: "fk_moderation_recognition_privacy_adoptions_moderation_recognition_privacy_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "ie_moderation_recognition_privacy_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_recognition_privacy_adoptions_users_adopted_by_human_id",
                        column: x => x.adopted_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_recognition_privacy_adoptions_users_withdrawn_by_human_id",
                        column: x => x.withdrawn_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_recognition_privacy_retention",
                columns: table => new
                {
                    source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    record_class = table.Column<int>(type: "INTEGER", nullable: false),
                    maximum_retention_ticks = table.Column<long>(type: "INTEGER", nullable: false),
                    hold = table.Column<int>(type: "INTEGER", nullable: false),
                    maximum_hold_ticks = table.Column<long>(type: "INTEGER", nullable: false),
                    hold_review_within_ticks = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_recognition_privacy_retention", x => new { x.source_id, x.record_class });
                    table.CheckConstraint("ck_moderation_recognition_privacy_hold", "(hold = 1 AND maximum_hold_ticks = 0 AND hold_review_within_ticks = 0) OR (hold = 2 AND record_class IN (3, 4) AND maximum_hold_ticks > 0 AND hold_review_within_ticks > 0 AND hold_review_within_ticks <= maximum_hold_ticks)");
                    table.CheckConstraint("ck_moderation_recognition_privacy_retention", "record_class BETWEEN 1 AND 4 AND maximum_retention_ticks > 0");
                    table.ForeignKey(
                        name: "fk_moderation_recognition_privacy_retention_moderation_recognition_privacy_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "ie_moderation_recognition_privacy_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_participant_incident_responsibility_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    occurrence_case_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    occurrence_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    occurrence_case_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    evidence_case_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    evidence_source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    evidence_source_version = table.Column<Guid>(type: "TEXT", nullable: false),
                    material_fact_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    original_narrative_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    original_narrative_purge_at = table.Column<long>(type: "INTEGER", nullable: false),
                    event_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    event_version = table.Column<Guid>(type: "TEXT", nullable: false),
                    session_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    session_generation = table.Column<Guid>(type: "TEXT", nullable: false),
                    incident_window_from = table.Column<long>(type: "INTEGER", nullable: false),
                    incident_window_until = table.Column<long>(type: "INTEGER", nullable: false),
                    order_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    order_generation = table.Column<Guid>(type: "TEXT", nullable: false),
                    participant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    participant_generation = table.Column<Guid>(type: "TEXT", nullable: false),
                    subject_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    subject_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    assessor_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    assessor_role_grant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    read_issuer_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    read_grant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    read_grant_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    read_grant_expires_at = table.Column<long>(type: "INTEGER", nullable: false),
                    qualification_policy_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    qualification_policy_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    maximum_qualification_lifetime_ticks = table.Column<long>(type: "INTEGER", nullable: false),
                    target_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    purpose = table.Column<int>(type: "INTEGER", nullable: false),
                    custody_source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    custody_source_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    custody_source_writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    custody_source_valid_until = table.Column<long>(type: "INTEGER", nullable: false),
                    maximum_retention_ticks = table.Column<long>(type: "INTEGER", nullable: false),
                    custody_adoption_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    custody_adoption_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    custody_adoption_writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    custody_adoption_valid_until = table.Column<long>(type: "INTEGER", nullable: false),
                    qualified_at = table.Column<long>(type: "INTEGER", nullable: false),
                    valid_until = table.Column<long>(type: "INTEGER", nullable: false),
                    purge_at = table.Column<long>(type: "INTEGER", nullable: false),
                    assessment_digest = table.Column<string>(type: "TEXT", unicode: false, maxLength: 64, nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    withdrawal_operation_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    withdrawn_by_human_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    withdrawn_at = table.Column<long>(type: "INTEGER", nullable: true),
                    protected_assessment = table.Column<byte[]>(type: "BLOB", maxLength: 131072, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_participant_incident_responsibility_sources", x => x.id);
                    table.CheckConstraint("ck_incident_assessment_digest", "length(assessment_digest) = 64");
                    table.CheckConstraint("ck_incident_assessment_finite_deadlines", "qualified_at > 0 AND valid_until > qualified_at AND valid_until <= purge_at AND purge_at <= original_narrative_purge_at AND original_narrative_purge_at < 3155378975999999999 AND purge_at <= read_grant_expires_at AND read_grant_expires_at < 3155378975999999999 AND purge_at <= custody_adoption_valid_until AND custody_adoption_valid_until <= custody_source_valid_until AND custody_source_valid_until < 3155378975999999999");
                    table.CheckConstraint("ck_incident_assessment_generations", "custody_source_writer_generation > 0 AND custody_source_writer_generation < 9223372036854775807 AND custody_adoption_writer_generation > custody_source_writer_generation AND custody_adoption_writer_generation < 9223372036854775807 AND custody_adoption_id <> custody_source_id AND evidence_source_version <> '00000000-0000-0000-0000-000000000000' AND event_version <> '00000000-0000-0000-0000-000000000000' AND session_generation <> '00000000-0000-0000-0000-000000000000' AND order_generation <> '00000000-0000-0000-0000-000000000000' AND participant_generation <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("ck_incident_assessment_lifetime_limits", "maximum_qualification_lifetime_ticks > 0 AND maximum_qualification_lifetime_ticks < 9223372036854775807 AND maximum_retention_ticks > 0 AND maximum_retention_ticks < 9223372036854775807 AND valid_until - qualified_at <= maximum_qualification_lifetime_ticks AND purge_at - qualified_at <= maximum_retention_ticks");
                    table.CheckConstraint("ck_incident_assessment_purpose", "target_kind = 2 AND purpose = 2 AND subject_kind = 1 AND subject_id <> assessor_human_id AND subject_id <> read_issuer_human_id");
                    table.CheckConstraint("ck_incident_assessment_revisions", "evidence_case_revision > 0 AND occurrence_case_revision >= evidence_case_revision AND read_grant_revision = 1 AND qualification_policy_revision > 0 AND custody_source_revision = 1 AND custody_adoption_revision = 1");
                    table.CheckConstraint("ck_incident_assessment_window", "incident_window_from > 0 AND incident_window_until > incident_window_from AND incident_window_until <= qualified_at");
                    table.CheckConstraint("ck_incident_assessment_withdrawal", "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL AND withdrawal_operation_id IS NULL) OR (revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_at >= qualified_at AND withdrawn_at < 3155378975999999999 AND withdrawn_by_human_id IS NOT NULL AND withdrawal_operation_id IS NOT NULL AND withdrawal_operation_id <> '00000000-0000-0000-0000-000000000000')");
                    table.ForeignKey(
                        name: "fk_moderation_participant_incident_responsibility_sources_event_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "ie_event_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_participant_incident_responsibility_sources_events_event_id",
                        column: x => x.event_id,
                        principalTable: "ie_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_participant_incident_responsibility_sources_moderation_case_narrative_adoptions_custody_adoption_id_h_be22aee0f8ae",
                        columns: x => new { x.custody_adoption_id, x.host_binding_id, x.trust_domain_id, x.tenant_id, x.custody_source_id },
                        principalTable: "ie_moderation_case_narrative_adoptions",
                        principalColumns: new[] { "id", "host_binding_id", "trust_domain_id", "tenant_id", "source_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_participant_incident_responsibility_sources_moderation_case_narrative_policy_sources_custody_source_i_60863fafd058",
                        columns: x => new { x.custody_source_id, x.host_binding_id, x.trust_domain_id, x.tenant_id },
                        principalTable: "ie_moderation_case_narrative_policy_sources",
                        principalColumns: new[] { "id", "host_binding_id", "trust_domain_id", "tenant_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_participant_incident_responsibility_sources_moderation_case_narratives_original_narrative_id",
                        column: x => x.original_narrative_id,
                        principalTable: "ie_moderation_case_narratives",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_participant_incident_responsibility_sources_moderation_case_read_grants_read_grant_id",
                        column: x => x.read_grant_id,
                        principalTable: "ie_moderation_case_read_grants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_participant_incident_responsibility_sources_moderation_case_target_policies_qualification_policy_id",
                        column: x => x.qualification_policy_id,
                        principalTable: "ie_moderation_case_target_policies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_participant_incident_responsibility_sources_moderation_case_target_sources_evidence_source_id",
                        column: x => x.evidence_source_id,
                        principalTable: "ie_moderation_case_target_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_participant_incident_responsibility_sources_moderation_cases_occurrence_case_id",
                        column: x => x.occurrence_case_id,
                        principalTable: "ie_moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_participant_incident_responsibility_sources_moderation_host_bindings_host_binding_id",
                        column: x => x.host_binding_id,
                        principalTable: "ie_moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_participant_incident_responsibility_sources_registration_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "ie_registration_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_participant_incident_responsibility_sources_registration_participants_participant_id",
                        column: x => x.participant_id,
                        principalTable: "ie_registration_participants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_participant_incident_responsibility_sources_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_participant_incident_responsibility_sources_users_assessor_human_id",
                        column: x => x.assessor_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_participant_incident_responsibility_sources_users_read_issuer_human_id",
                        column: x => x.read_issuer_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_participant_incident_responsibility_sources_users_subject_id",
                        column: x => x.subject_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_participant_incident_responsibility_sources_users_withdrawn_by_human_id",
                        column: x => x.withdrawn_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_case_attribution_heads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    case_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    solution_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    instance_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    occurrence_case_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    occurrence_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    occurrence_evidence_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    subject_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    subject_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    opening_source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    generation = table.Column<long>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_case_attribution_heads", x => x.id);
                    table.CheckConstraint("ck_moderation_attribution_head_generation", "generation > 0 AND revision = generation AND ((generation = 1 AND source_id = opening_source_id) OR (generation > 1 AND source_id <> opening_source_id))");
                    table.CheckConstraint("ck_moderation_attribution_head_scope", "case_id <> occurrence_case_id AND occurrence_evidence_revision > 0 AND subject_kind IN (1, 2, 3, 4)");
                    table.ForeignKey(
                        name: "fk_moderation_case_attribution_heads_moderation_case_attribution_sources_opening_source_id",
                        column: x => x.opening_source_id,
                        principalTable: "ie_moderation_case_attribution_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_attribution_heads_moderation_case_attribution_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "ie_moderation_case_attribution_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_attribution_heads_moderation_cases_case_id",
                        column: x => x.case_id,
                        principalTable: "ie_moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_attribution_heads_moderation_cases_occurrence_case_id",
                        column: x => x.occurrence_case_id,
                        principalTable: "ie_moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_attribution_heads_moderation_host_bindings_host_binding_id",
                        column: x => x.host_binding_id,
                        principalTable: "ie_moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_attribution_heads_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_pre_finding_custody",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    case_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    case_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    attribution_source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    attribution_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    evidence_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    proposed_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    privacy_owner_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    maximum_retention_ticks = table.Column<long>(type: "INTEGER", nullable: false),
                    maximum_text_bytes = table.Column<int>(type: "INTEGER", nullable: false),
                    purpose = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    privacy_legal_mandate = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    proposed_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_pre_finding_custody", x => x.id);
                    table.UniqueConstraint("ak_moderation_pre_finding_custody_id_host_binding_id_trust_domain_id_tenant_id_case_id", x => new { x.id, x.host_binding_id, x.trust_domain_id, x.tenant_id, x.case_id });
                    table.CheckConstraint("ck_pre_finding_custody_attribution", "attribution_generation > 0 AND evidence_revision > 0");
                    table.CheckConstraint("ck_pre_finding_custody_independence", "proposed_by_human_id <> privacy_owner_human_id");
                    table.CheckConstraint("ck_pre_finding_custody_limits", "maximum_retention_ticks > 0 AND maximum_retention_ticks < 9223372036854775807 AND maximum_text_bytes > 0 AND maximum_text_bytes <= 65536 AND valid_until > proposed_at");
                    table.ForeignKey(
                        name: "fk_moderation_pre_finding_custody_moderation_case_attribution_sources_attribution_source_id",
                        column: x => x.attribution_source_id,
                        principalTable: "ie_moderation_case_attribution_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_pre_finding_custody_moderation_cases_case_id",
                        column: x => x.case_id,
                        principalTable: "ie_moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_pre_finding_custody_users_privacy_owner_human_id",
                        column: x => x.privacy_owner_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_pre_finding_custody_users_proposed_by_human_id",
                        column: x => x.proposed_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_profile_collection_procedure_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    policy_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    policy_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    publication_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    definition_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    profile_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    mode_code = table.Column<int>(type: "INTEGER", nullable: false),
                    proposed_state_digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    child_age_source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    child_age_source_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    child_age_source_writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    child_age_recorded_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    child_age_recorded_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    child_age_valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    child_age_terms = table.Column<string>(type: "TEXT", nullable: false),
                    child_age_ownership = table.Column<string>(type: "TEXT", nullable: false),
                    child_age_adoption_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    child_age_adoption_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    child_age_adoption_writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    child_age_adopted_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    child_age_adoption_valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    procedure = table.Column<string>(type: "TEXT", nullable: false),
                    recorded_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_profile_collection_procedure_sources", x => x.id);
                    table.CheckConstraint("ck_profile_collection_procedure_generation", "revision = 1 AND policy_revision > 0 AND child_age_source_revision = 1 AND child_age_source_writer_generation > 0 AND child_age_adoption_revision = 1 AND child_age_adoption_writer_generation > child_age_source_writer_generation AND writer_generation > child_age_adoption_writer_generation");
                    table.CheckConstraint("ck_profile_collection_procedure_mode", "mode_code BETWEEN 1 AND 2");
                    table.CheckConstraint("ck_profile_collection_procedure_validity", "valid_until > recorded_at AND recorded_at >= child_age_recorded_at AND recorded_at >= child_age_adopted_at AND valid_until <= child_age_valid_until AND valid_until <= child_age_adoption_valid_until");
                    table.ForeignKey(
                        name: "fk_moderation_profile_collection_procedure_sources_moderation_child_age_policy_adoptions_child_age_adoption_id",
                        column: x => x.child_age_adoption_id,
                        principalTable: "ie_moderation_child_age_policy_adoptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_profile_collection_procedure_sources_moderation_child_age_policy_sources_child_age_source_id",
                        column: x => x.child_age_source_id,
                        principalTable: "ie_moderation_child_age_policy_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_profile_collection_procedure_sources_users_recorded_by_human_id",
                        column: x => x.recorded_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_classification_taxonomy_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    taxonomy_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    taxonomy_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    previous_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    policy_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    policy_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    publication_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    definition_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    profile_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    mode_code = table.Column<int>(type: "INTEGER", nullable: false),
                    proposed_state_digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    child_age_source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    child_age_source_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    child_age_source_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    child_age_adoption_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    child_age_adoption_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    child_age_adoption_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    adopted_debate_minimum_age = table.Column<int>(type: "INTEGER", nullable: false),
                    constitution_source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    constitution_source_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    constitution_source_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    constitution_adoption_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    constitution_adoption_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    constitution_adoption_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    owner_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    owner_controller_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    reviewer_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    reviewer_controller_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ownership_grounds = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    review_mandate_grounds = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_classification_taxonomy_sources", x => x.id);
                    table.CheckConstraint("ck_moderation_taxonomy_generation", "revision = 1 AND taxonomy_revision > 0 AND previous_generation >= 0 AND writer_generation > previous_generation AND child_age_source_revision = 1 AND child_age_adoption_revision = 1 AND constitution_source_revision = 1 AND constitution_adoption_revision = 1 AND child_age_source_generation > 0 AND child_age_adoption_generation > child_age_source_generation AND constitution_source_generation > 0 AND constitution_adoption_generation > constitution_source_generation AND writer_generation > child_age_adoption_generation AND writer_generation > constitution_adoption_generation");
                    table.CheckConstraint("ck_moderation_taxonomy_independence", "owner_human_id <> reviewer_human_id AND owner_controller_human_id <> reviewer_controller_human_id AND owner_human_id <> reviewer_controller_human_id AND reviewer_human_id <> owner_controller_human_id");
                    table.CheckConstraint("ck_moderation_taxonomy_mode", "mode_code BETWEEN 1 AND 2 AND policy_revision > 0");
                    table.CheckConstraint("ck_moderation_taxonomy_validity", "valid_until > recorded_at");
                    table.ForeignKey(
                        name: "fk_moderation_classification_taxonomy_sources_moderation_child_age_policy_adoptions_child_age_adoption_id",
                        column: x => x.child_age_adoption_id,
                        principalTable: "ie_moderation_child_age_policy_adoptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_taxonomy_sources_moderation_child_age_policy_sources_child_age_source_id",
                        column: x => x.child_age_source_id,
                        principalTable: "ie_moderation_child_age_policy_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_taxonomy_sources_moderation_constitution_adoptions_constitution_adoption_id",
                        column: x => x.constitution_adoption_id,
                        principalTable: "ie_moderation_constitution_adoptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_taxonomy_sources_moderation_constitution_sources_constitution_source_id",
                        column: x => x.constitution_source_id,
                        principalTable: "ie_moderation_constitution_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_taxonomy_sources_moderation_policy_publications_definition_id",
                        column: x => x.definition_id,
                        principalTable: "ie_moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_taxonomy_sources_users_owner_controller_human_id",
                        column: x => x.owner_controller_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_taxonomy_sources_users_owner_human_id",
                        column: x => x.owner_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_taxonomy_sources_users_reviewer_controller_human_id",
                        column: x => x.reviewer_controller_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_taxonomy_sources_users_reviewer_human_id",
                        column: x => x.reviewer_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_event_target_adoptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    selection_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    selection_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    binding_ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    generation_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    generation_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    generation_writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    case_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    case_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    adopted_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    revoked_by_human_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_event_target_adoptions", x => x.id);
                    table.CheckConstraint("ck_moderation_event_target_adoption_revision", "selection_revision = 1 AND generation_revision > 0 AND generation_writer_generation > 0 AND case_revision > 0 AND writer_generation > 0");
                    table.CheckConstraint("ck_moderation_event_target_adoption_revocation", "(revision = 1 AND revoked_at IS NULL AND revoked_by_human_id IS NULL) OR (revision = 2 AND revoked_at IS NOT NULL AND revoked_by_human_id IS NOT NULL AND revoked_at >= adopted_at)");
                    table.CheckConstraint("ck_moderation_event_target_adoption_validity", "valid_until > adopted_at");
                    table.ForeignKey(
                        name: "fk_moderation_event_target_adoptions_moderation_contribution_bindings_selection_id_binding_ordinal",
                        columns: x => new { x.selection_id, x.binding_ordinal },
                        principalTable: "ie_moderation_contribution_bindings",
                        principalColumns: new[] { "selection_id", "ordinal" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_event_target_adoptions_moderation_event_generations_generation_id",
                        column: x => x.generation_id,
                        principalTable: "ie_moderation_event_generations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_event_target_adoptions_users_adopted_by_human_id",
                        column: x => x.adopted_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_event_target_adoptions_users_revoked_by_human_id",
                        column: x => x.revoked_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_resource_target_adoptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    selection_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    selection_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    binding_ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    generation_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    generation_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    generation_writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    case_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    case_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    adopted_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    revoked_by_human_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_resource_target_adoptions", x => x.id);
                    table.CheckConstraint("ck_moderation_resource_target_adoption_revision", "selection_revision = 1 AND generation_revision > 0 AND generation_writer_generation > 0 AND case_revision > 0 AND writer_generation > 0");
                    table.CheckConstraint("ck_moderation_resource_target_adoption_revocation", "(revision = 1 AND revoked_at IS NULL AND revoked_by_human_id IS NULL) OR (revision = 2 AND revoked_at IS NOT NULL AND revoked_by_human_id IS NOT NULL AND revoked_at >= adopted_at)");
                    table.CheckConstraint("ck_moderation_resource_target_adoption_validity", "valid_until > adopted_at");
                    table.ForeignKey(
                        name: "fk_moderation_resource_target_adoptions_moderation_contribution_bindings_selection_id_binding_ordinal",
                        columns: x => new { x.selection_id, x.binding_ordinal },
                        principalTable: "ie_moderation_contribution_bindings",
                        principalColumns: new[] { "selection_id", "ordinal" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_resource_target_adoptions_moderation_resource_generations_generation_id",
                        column: x => x.generation_id,
                        principalTable: "ie_moderation_resource_generations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_resource_target_adoptions_users_adopted_by_human_id",
                        column: x => x.adopted_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_resource_target_adoptions_users_revoked_by_human_id",
                        column: x => x.revoked_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_publication_profiles",
                columns: table => new
                {
                    publication_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    profile_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    rule_code = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    rule_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    reason_code = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    reason_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    reference_profile = table.Column<int>(type: "INTEGER", nullable: false),
                    reference_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    grade = table.Column<int>(type: "INTEGER", nullable: false),
                    bundle = table.Column<int>(type: "INTEGER", nullable: false),
                    protection_purpose = table.Column<int>(type: "INTEGER", nullable: true),
                    timing_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    duration_ticks = table.Column<long>(type: "INTEGER", nullable: false),
                    required_approvals = table.Column<int>(type: "INTEGER", nullable: false),
                    contestation_calendar_months = table.Column<int>(type: "INTEGER", nullable: false),
                    recognition = table.Column<int>(type: "INTEGER", nullable: false),
                    supported = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_publication_profiles", x => new { x.publication_id, x.profile_id });
                    table.CheckConstraint("ck_moderation_profile_purpose", "(bundle = 10 AND protection_purpose BETWEEN 1 AND 2) OR (bundle BETWEEN 1 AND 9 AND protection_purpose IS NULL)");
                    table.CheckConstraint("ck_moderation_profile_quorum", "required_approvals BETWEEN 0 AND 2");
                    table.CheckConstraint("ck_moderation_profile_revision", "rule_revision > 0 AND reason_revision > 0 AND reference_revision > 0");
                    table.CheckConstraint("ck_moderation_profile_timing", "duration_ticks >= 0 AND timing_kind BETWEEN 1 AND 9");
                    table.ForeignKey(
                        name: "fk_moderation_publication_profiles_moderation_policy_publications_publication_id",
                        column: x => x.publication_id,
                        principalTable: "ie_moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_publication_profiles_moderation_publication_reasons_publication_id_reason_code_reason_revision",
                        columns: x => new { x.publication_id, x.reason_code, x.reason_revision },
                        principalTable: "ie_moderation_publication_reasons",
                        principalColumns: new[] { "publication_id", "code", "revision" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_case_attribution_renewal_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    case_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    head_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    expected_case_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    expected_source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    expected_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    expected_head_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    qualified_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_case_attribution_renewal_receipts", x => x.id);
                    table.CheckConstraint("ck_moderation_attribution_renewal_distinct_source", "id <> expected_source_id");
                    table.CheckConstraint("ck_moderation_attribution_renewal_expected", "expected_case_revision > 0 AND expected_generation > 0 AND expected_head_revision > 0 AND expected_head_revision = expected_generation");
                    table.ForeignKey(
                        name: "fk_moderation_case_attribution_renewal_receipts_moderation_case_attribution_heads_head_id",
                        column: x => x.head_id,
                        principalTable: "ie_moderation_case_attribution_heads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_attribution_renewal_receipts_moderation_case_attribution_sources_expected_source_id",
                        column: x => x.expected_source_id,
                        principalTable: "ie_moderation_case_attribution_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_attribution_renewal_receipts_moderation_case_attribution_sources_id",
                        column: x => x.id,
                        principalTable: "ie_moderation_case_attribution_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_attribution_renewal_receipts_moderation_cases_case_id",
                        column: x => x.case_id,
                        principalTable: "ie_moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_attribution_renewal_receipts_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_pre_finding_notices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    custody_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    case_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    case_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    occurrence_case_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    evidence_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    attribution_source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    attribution_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    subject_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    subject_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    recipient_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    recipient_assignment_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    recipient_assignment_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    issued_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    reviewer_assignment_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    reviewer_assignment_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    retain_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    responded_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    response_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    purged_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    withdrawn = table.Column<bool>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    protected_notice = table.Column<byte[]>(type: "BLOB", maxLength: 131072, nullable: false),
                    protected_response = table.Column<byte[]>(type: "BLOB", maxLength: 131072, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_pre_finding_notices", x => x.id);
                    table.CheckConstraint("ck_pre_finding_notice_finite", "retain_until > issued_at");
                    table.CheckConstraint("ck_pre_finding_notice_link", "case_id <> occurrence_case_id AND case_revision > 0 AND evidence_revision > 0 AND attribution_generation > 0 AND revision > 0");
                    table.CheckConstraint("ck_pre_finding_notice_response", "(response_id IS NULL AND responded_at IS NULL) OR (response_id IS NOT NULL AND responded_at >= acknowledged_at AND responded_at < retain_until)");
                    table.ForeignKey(
                        name: "fk_moderation_pre_finding_notices_moderation_cases_case_id",
                        column: x => x.case_id,
                        principalTable: "ie_moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_pre_finding_notices_moderation_cases_occurrence_case_id",
                        column: x => x.occurrence_case_id,
                        principalTable: "ie_moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_pre_finding_notices_moderation_pre_finding_custody_custody_id_host_binding_id_trust_domain_id_tenant_id_case_id",
                        columns: x => new { x.custody_id, x.host_binding_id, x.trust_domain_id, x.tenant_id, x.case_id },
                        principalTable: "ie_moderation_pre_finding_custody",
                        principalColumns: new[] { "id", "host_binding_id", "trust_domain_id", "tenant_id", "case_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_pre_finding_notices_moderation_reviewer_assignments_reviewer_assignment_id",
                        column: x => x.reviewer_assignment_id,
                        principalTable: "ie_moderation_reviewer_assignments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_pre_finding_notices_users_recipient_human_id",
                        column: x => x.recipient_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_profile_collection_procedure_adoptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    source_writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    adopted_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    withdrawn_by_human_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_profile_collection_procedure_adoptions", x => x.id);
                    table.CheckConstraint("ck_profile_collection_adoption_generation", "source_revision = 1 AND source_writer_generation > 0 AND writer_generation > source_writer_generation");
                    table.CheckConstraint("ck_profile_collection_adoption_validity", "valid_until > adopted_at");
                    table.CheckConstraint("ck_profile_collection_adoption_withdrawal", "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR (revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at >= adopted_at)");
                    table.ForeignKey(
                        name: "fk_moderation_profile_collection_procedure_adoptions_moderation_profile_collection_procedure_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "ie_moderation_profile_collection_procedure_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_profile_collection_procedure_adoptions_users_adopted_by_human_id",
                        column: x => x.adopted_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_profile_collection_procedure_adoptions_users_withdrawn_by_human_id",
                        column: x => x.withdrawn_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_profile_subject_consents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    procedure_adoption_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    procedure_adoption_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    procedure_adoption_writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    procedure_adopted_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    procedure_adopted_at = table.Column<long>(type: "INTEGER", nullable: false),
                    procedure_adoption_valid_until = table.Column<long>(type: "INTEGER", nullable: false),
                    binding = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    assessment = table.Column<string>(type: "TEXT", maxLength: 8192, nullable: false),
                    subject_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    generation = table.Column<long>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    granted_at = table.Column<long>(type: "INTEGER", nullable: false),
                    valid_until = table.Column<long>(type: "INTEGER", nullable: false),
                    withdrawn_at = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_profile_subject_consents", x => x.id);
                    table.CheckConstraint("ck_profile_subject_consent_generation", "generation > 0 AND generation < 9223372036854775807 AND procedure_adoption_revision = 1 AND procedure_adoption_writer_generation > 0 AND procedure_adoption_writer_generation < 9223372036854775807");
                    table.CheckConstraint("ck_profile_subject_consent_terminal", "(revision = 1 AND withdrawn_at IS NULL) OR (revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_at >= granted_at AND withdrawn_at < 3155378975999999999)");
                    table.CheckConstraint("ck_profile_subject_consent_validity", "granted_at > 0 AND valid_until < 3155378975999999999 AND valid_until > granted_at AND procedure_adopted_at > 0 AND procedure_adoption_valid_until < 3155378975999999999 AND procedure_adoption_valid_until > procedure_adopted_at AND granted_at >= procedure_adopted_at AND valid_until <= procedure_adoption_valid_until");
                    table.ForeignKey(
                        name: "fk_moderation_profile_subject_consents_moderation_profile_collection_procedure_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "ie_moderation_profile_collection_procedure_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_profile_subject_consents_user_pii_subject_human_id",
                        column: x => x.subject_human_id,
                        principalTable: "ie_user_pii",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_classification_taxonomy_adoptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    source_writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    adopted_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    withdrawn_by_human_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_classification_taxonomy_adoptions", x => x.id);
                    table.CheckConstraint("ck_moderation_taxonomy_adoption_generation", "source_revision = 1 AND source_writer_generation > 0 AND writer_generation > source_writer_generation");
                    table.CheckConstraint("ck_moderation_taxonomy_adoption_validity", "valid_until > adopted_at");
                    table.CheckConstraint("ck_moderation_taxonomy_adoption_withdrawal", "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR (revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at >= adopted_at)");
                    table.ForeignKey(
                        name: "fk_moderation_classification_taxonomy_adoptions_moderation_classification_taxonomy_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "ie_moderation_classification_taxonomy_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_taxonomy_adoptions_users_adopted_by_human_id",
                        column: x => x.adopted_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_taxonomy_adoptions_users_withdrawn_by_human_id",
                        column: x => x.withdrawn_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_classification_taxonomy_categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    category_id = table.Column<int>(type: "INTEGER", nullable: false),
                    code = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    kind = table.Column<int>(type: "INTEGER", nullable: false),
                    minimum_age = table.Column<int>(type: "INTEGER", nullable: false),
                    meaning = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    review_rationale = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_classification_taxonomy_categories", x => x.id);
                    table.CheckConstraint("ck_moderation_taxonomy_category", "category_id > 0 AND ordinal BETWEEN 0 AND 63 AND kind BETWEEN 1 AND 4 AND minimum_age BETWEEN 0 AND 130");
                    table.ForeignKey(
                        name: "fk_moderation_classification_taxonomy_categories_moderation_classification_taxonomy_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "ie_moderation_classification_taxonomy_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_publication_consumers",
                columns: table => new
                {
                    publication_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    profile_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    mode = table.Column<int>(type: "INTEGER", nullable: false),
                    capability = table.Column<int>(type: "INTEGER", nullable: false),
                    consumer_id = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    subject_resolver = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    target_resolver = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    authorization_owner = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    entry_checkpoint = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    terminal_checkpoint = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    writer_fence = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    freshness_class = table.Column<int>(type: "INTEGER", nullable: false),
                    owning_packet = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    conformance_test = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_publication_consumers", x => new { x.publication_id, x.profile_id, x.mode, x.capability, x.consumer_id });
                    table.ForeignKey(
                        name: "fk_moderation_publication_consumers_moderation_publication_profiles_publication_id_profile_id",
                        columns: x => new { x.publication_id, x.profile_id },
                        principalTable: "ie_moderation_publication_profiles",
                        principalColumns: new[] { "publication_id", "profile_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_publication_effects",
                columns: table => new
                {
                    publication_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    profile_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    kind = table.Column<int>(type: "INTEGER", nullable: false),
                    mode = table.Column<int>(type: "INTEGER", nullable: false),
                    target = table.Column<int>(type: "INTEGER", nullable: false),
                    capability = table.Column<int>(type: "INTEGER", nullable: true),
                    execution = table.Column<int>(type: "INTEGER", nullable: false),
                    semantic_owner = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_publication_effects", x => new { x.publication_id, x.profile_id, x.ordinal });
                    table.ForeignKey(
                        name: "fk_moderation_publication_effects_moderation_publication_profiles_publication_id_profile_id",
                        columns: x => new { x.publication_id, x.profile_id },
                        principalTable: "ie_moderation_publication_profiles",
                        principalColumns: new[] { "publication_id", "profile_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_publication_plans",
                columns: table => new
                {
                    publication_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    profile_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    strategy = table.Column<int>(type: "INTEGER", nullable: false),
                    content = table.Column<int>(type: "INTEGER", nullable: false),
                    management = table.Column<int>(type: "INTEGER", nullable: false),
                    resources = table.Column<int>(type: "INTEGER", nullable: false),
                    new_admission = table.Column<int>(type: "INTEGER", nullable: false),
                    existing_admission = table.Column<int>(type: "INTEGER", nullable: false),
                    private_disclosure = table.Column<int>(type: "INTEGER", nullable: false),
                    settlement = table.Column<int>(type: "INTEGER", nullable: false),
                    capacity = table.Column<int>(type: "INTEGER", nullable: false),
                    credentials = table.Column<int>(type: "INTEGER", nullable: false),
                    notifications = table.Column<int>(type: "INTEGER", nullable: false),
                    recognition = table.Column<int>(type: "INTEGER", nullable: false),
                    pending_work = table.Column<int>(type: "INTEGER", nullable: false),
                    restoration = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_publication_plans", x => new { x.publication_id, x.profile_id, x.strategy });
                    table.CheckConstraint("ck_moderation_publication_strategy", "strategy BETWEEN 0 AND 3");
                    table.ForeignKey(
                        name: "fk_moderation_publication_plans_moderation_publication_profiles_publication_id_profile_id",
                        columns: x => new { x.publication_id, x.profile_id },
                        principalTable: "ie_moderation_publication_profiles",
                        principalColumns: new[] { "publication_id", "profile_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_publication_prerequisites",
                columns: table => new
                {
                    publication_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    profile_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    mode = table.Column<int>(type: "INTEGER", nullable: false),
                    ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    activation_gate = table.Column<int>(type: "INTEGER", nullable: true),
                    native_prerequisite = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_publication_prerequisites", x => new { x.publication_id, x.profile_id, x.mode, x.ordinal });
                    table.CheckConstraint("ck_moderation_publication_prerequisite", "(activation_gate BETWEEN 1 AND 5 AND native_prerequisite IS NULL) OR (activation_gate IS NULL AND native_prerequisite BETWEEN 1 AND 30)");
                    table.ForeignKey(
                        name: "fk_moderation_publication_prerequisites_moderation_publication_profiles_publication_id_profile_id",
                        columns: x => new { x.publication_id, x.profile_id },
                        principalTable: "ie_moderation_publication_profiles",
                        principalColumns: new[] { "publication_id", "profile_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_publication_scopes",
                columns: table => new
                {
                    publication_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    profile_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    kind = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_publication_scopes", x => new { x.publication_id, x.profile_id, x.kind });
                    table.ForeignKey(
                        name: "fk_moderation_publication_scopes_moderation_publication_profiles_publication_id_profile_id",
                        columns: x => new { x.publication_id, x.profile_id },
                        principalTable: "ie_moderation_publication_profiles",
                        principalColumns: new[] { "publication_id", "profile_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_publication_subjects",
                columns: table => new
                {
                    publication_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    profile_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    kind = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_publication_subjects", x => new { x.publication_id, x.profile_id, x.kind });
                    table.ForeignKey(
                        name: "fk_moderation_publication_subjects_moderation_publication_profiles_publication_id_profile_id",
                        columns: x => new { x.publication_id, x.profile_id },
                        principalTable: "ie_moderation_publication_profiles",
                        principalColumns: new[] { "publication_id", "profile_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_profile_collection_assessment_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    procedure_source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    subject_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    owner_controller_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    reviewer_controller_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    procedure_adoption_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    procedure_adoption_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    procedure_adoption_writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    procedure_adopted_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    procedure_adopted_at = table.Column<long>(type: "INTEGER", nullable: false),
                    procedure_adoption_valid_until = table.Column<long>(type: "INTEGER", nullable: false),
                    binding = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    required = table.Column<int>(type: "INTEGER", nullable: false),
                    recorded_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    generation = table.Column<long>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    recorded_at = table.Column<long>(type: "INTEGER", nullable: false),
                    valid_until = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_profile_collection_assessment_sources", x => x.id);
                    table.CheckConstraint("ck_profile_assessment_source_generation", "revision = 1 AND generation > 0 AND generation < 9223372036854775807 AND procedure_adoption_revision = 1 AND procedure_adoption_writer_generation > 0 AND procedure_adoption_writer_generation < 9223372036854775807");
                    table.CheckConstraint("ck_profile_assessment_source_independence", "recorded_by_human_id <> procedure_adopted_by_human_id AND procedure_adopted_by_human_id <> owner_controller_human_id AND owner_controller_human_id <> reviewer_controller_human_id");
                    table.CheckConstraint("ck_profile_assessment_source_required", "required BETWEEN 0 AND 7");
                    table.CheckConstraint("ck_profile_assessment_source_window", "recorded_at > 0 AND valid_until < 3155378975999999999 AND valid_until > recorded_at AND procedure_adopted_at > 0 AND procedure_adoption_valid_until < 3155378975999999999 AND procedure_adoption_valid_until > procedure_adopted_at AND recorded_at >= procedure_adopted_at AND valid_until <= procedure_adoption_valid_until");
                    table.ForeignKey(
                        name: "fk_moderation_profile_collection_assessment_sources_moderation_profile_collection_procedure_adoptions_procedure_adoption_id",
                        column: x => x.procedure_adoption_id,
                        principalTable: "ie_moderation_profile_collection_procedure_adoptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_profile_collection_assessment_sources_moderation_profile_collection_procedure_sources_procedure_source_id",
                        column: x => x.procedure_source_id,
                        principalTable: "ie_moderation_profile_collection_procedure_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_profile_collection_assessment_sources_user_pii_subject_human_id",
                        column: x => x.subject_human_id,
                        principalTable: "ie_user_pii",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_moderation_profile_collection_assessment_sources_users_owner_controller_human_id",
                        column: x => x.owner_controller_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_profile_collection_assessment_sources_users_procedure_adopted_by_human_id",
                        column: x => x.procedure_adopted_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_profile_collection_assessment_sources_users_recorded_by_human_id",
                        column: x => x.recorded_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_profile_collection_assessment_sources_users_reviewer_controller_human_id",
                        column: x => x.reviewer_controller_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_profile_collection_subject_observations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    generation = table.Column<long>(type: "INTEGER", nullable: false),
                    observation_sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    observation_stamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    observed_at = table.Column<long>(type: "INTEGER", nullable: false),
                    valid_until = table.Column<long>(type: "INTEGER", nullable: false),
                    subject_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    subject_login_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    solution_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    instance_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    jurisdiction_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    epoch_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    epoch_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    policy_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    policy_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    publication_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    definition_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    profile_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    mode_code = table.Column<int>(type: "INTEGER", nullable: false),
                    procedure_source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    procedure_source_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    procedure_source_writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    procedure_adoption_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    procedure_adoption_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    procedure_adoption_writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    procedure_adopted_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    procedure_adopted_at = table.Column<long>(type: "INTEGER", nullable: false),
                    procedure_adoption_valid_until = table.Column<long>(type: "INTEGER", nullable: false),
                    original_procedure_digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    original_policy_digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    original_selection_digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_profile_collection_subject_observations", x => x.id);
                    table.CheckConstraint("ck_profile_subject_observation_generations", "revision = 1 AND generation > 0 AND generation < 9223372036854775807 AND observation_sequence = generation AND policy_revision > 0 AND policy_revision < 9223372036854775807 AND procedure_source_revision = 1 AND procedure_source_writer_generation > 0 AND procedure_adoption_revision = 1 AND procedure_adoption_writer_generation > procedure_source_writer_generation AND epoch_revision >= procedure_adoption_writer_generation AND epoch_revision < 9223372036854775807");
                    table.CheckConstraint("ck_profile_subject_observation_identity", "id <> subject_human_id AND id <> observation_stamp AND id <> procedure_source_id AND id <> procedure_adoption_id AND observation_stamp <> subject_human_id");
                    table.CheckConstraint("ck_profile_subject_observation_scope", "jurisdiction_kind IN (3, 4) AND mode_code BETWEEN 1 AND 2");
                    table.CheckConstraint("ck_profile_subject_observation_window", "observed_at > 0 AND valid_until < 3155378975999999999 AND valid_until > observed_at AND procedure_adopted_at > 0 AND procedure_adoption_valid_until < 3155378975999999999 AND procedure_adoption_valid_until > procedure_adopted_at AND observed_at >= procedure_adopted_at AND valid_until <= procedure_adoption_valid_until");
                    table.ForeignKey(
                        name: "fk_moderation_profile_collection_subject_observations_moderation_profile_collection_procedure_adoptions_procedure_adoption_id",
                        column: x => x.procedure_adoption_id,
                        principalTable: "ie_moderation_profile_collection_procedure_adoptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_profile_collection_subject_observations_moderation_profile_collection_procedure_sources_procedure_source_id",
                        column: x => x.procedure_source_id,
                        principalTable: "ie_moderation_profile_collection_procedure_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_profile_collection_subject_observations_moderation_qualification_epochs_epoch_id",
                        column: x => x.epoch_id,
                        principalTable: "ie_moderation_qualification_epochs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_profile_collection_subject_observations_user_pii_subject_human_id",
                        column: x => x.subject_human_id,
                        principalTable: "ie_user_pii",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_classification_material_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    previous_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    event_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    event_stamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    event_generation_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    event_generation_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    event_writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    policy_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    policy_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    publication_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    profile_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    mode_code = table.Column<int>(type: "INTEGER", nullable: false),
                    taxonomy_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    taxonomy_source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    taxonomy_source_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    taxonomy_source_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    taxonomy_adoption_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    taxonomy_adoption_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    taxonomy_adoption_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    owner_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    owner_controller_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    owner_assignment_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    owner_assignment_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    reviewer_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    reviewer_controller_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ownership_grounds = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    review_grounds = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_classification_material_sources", x => x.id);
                    table.CheckConstraint("ck_moderation_material_generation", "revision = 1 AND previous_generation >= 0 AND event_generation_revision > 0 AND event_writer_generation > 0 AND taxonomy_source_revision = 1 AND taxonomy_adoption_revision = 1 AND taxonomy_source_generation > 0 AND taxonomy_adoption_generation > taxonomy_source_generation AND writer_generation > taxonomy_adoption_generation AND writer_generation > event_writer_generation AND writer_generation > previous_generation AND owner_assignment_revision > 0 AND policy_revision > 0 AND mode_code BETWEEN 1 AND 2");
                    table.CheckConstraint("ck_moderation_material_independence", "owner_human_id <> reviewer_human_id AND owner_controller_human_id <> reviewer_controller_human_id AND owner_human_id <> reviewer_controller_human_id AND reviewer_human_id <> owner_controller_human_id");
                    table.CheckConstraint("ck_moderation_material_validity", "valid_until > recorded_at");
                    table.ForeignKey(
                        name: "fk_moderation_classification_material_sources_moderation_classification_taxonomy_adoptions_taxonomy_adoption_id",
                        column: x => x.taxonomy_adoption_id,
                        principalTable: "ie_moderation_classification_taxonomy_adoptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_material_sources_moderation_classification_taxonomy_sources_taxonomy_source_id",
                        column: x => x.taxonomy_source_id,
                        principalTable: "ie_moderation_classification_taxonomy_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_material_sources_moderation_event_generations_event_generation_id",
                        column: x => x.event_generation_id,
                        principalTable: "ie_moderation_event_generations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_profile_collection_assessment_adoptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    source_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    generation = table.Column<long>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    adopted_at = table.Column<long>(type: "INTEGER", nullable: false),
                    valid_until = table.Column<long>(type: "INTEGER", nullable: false),
                    withdrawn_by_human_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    withdrawn_at = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_profile_collection_assessment_adoptions", x => x.id);
                    table.CheckConstraint("ck_profile_assessment_adoption_generation", "source_revision = 1 AND source_generation > 0 AND source_generation < 9223372036854775807 AND generation > source_generation AND generation < 9223372036854775807");
                    table.CheckConstraint("ck_profile_assessment_adoption_terminal", "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR (revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at >= adopted_at AND withdrawn_at < 3155378975999999999)");
                    table.CheckConstraint("ck_profile_assessment_adoption_window", "adopted_at > 0 AND valid_until < 3155378975999999999 AND valid_until > adopted_at");
                    table.ForeignKey(
                        name: "fk_moderation_profile_collection_assessment_adoptions_moderation_profile_collection_assessment_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "ie_moderation_profile_collection_assessment_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_moderation_profile_collection_assessment_adoptions_users_adopted_by_human_id",
                        column: x => x.adopted_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_profile_collection_assessment_adoptions_users_withdrawn_by_human_id",
                        column: x => x.withdrawn_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_classification_material_components",
                columns: table => new
                {
                    source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    kind = table.Column<int>(type: "INTEGER", nullable: false),
                    native_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    native_stamp = table.Column<Guid>(type: "TEXT", nullable: true),
                    digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_classification_material_components", x => new { x.source_id, x.ordinal });
                    table.CheckConstraint("ck_moderation_material_component_kind", "kind BETWEEN 1 AND 41 AND ordinal BETWEEN 0 AND 4095");
                    table.ForeignKey(
                        name: "fk_moderation_classification_material_components_moderation_classification_material_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "ie_moderation_classification_material_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_classification_material_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    event_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    event_stamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    session_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    session_stamp = table.Column<Guid>(type: "TEXT", nullable: true),
                    resource_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    resource_stamp = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_classification_material_entries", x => x.id);
                    table.CheckConstraint("ck_moderation_material_entry_ancestry", "((session_id IS NULL AND session_stamp IS NULL) OR (session_id IS NOT NULL AND session_stamp IS NOT NULL)) AND ((resource_id IS NULL AND resource_stamp IS NULL) OR (resource_id IS NOT NULL AND resource_stamp IS NOT NULL))");
                    table.CheckConstraint("ck_moderation_material_entry_ordinal", "ordinal BETWEEN 0 AND 4095");
                    table.ForeignKey(
                        name: "fk_moderation_classification_material_entries_moderation_classification_material_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "ie_moderation_classification_material_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_classification_material_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    source_writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    reviewer_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    end_kind = table.Column<int>(type: "INTEGER", nullable: true),
                    ended_by_human_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    end_writer_generation = table.Column<long>(type: "INTEGER", nullable: true),
                    ended_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    end_grounds = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_classification_material_reviews", x => x.id);
                    table.CheckConstraint("ck_moderation_material_review_ending", "(revision = 1 AND end_kind IS NULL AND ended_at IS NULL AND ended_by_human_id IS NULL AND end_writer_generation IS NULL AND end_grounds IS NULL) OR (revision = 2 AND end_kind BETWEEN 1 AND 2 AND ended_at IS NOT NULL AND ended_by_human_id IS NOT NULL AND end_writer_generation > writer_generation AND end_grounds IS NOT NULL AND ended_at >= accepted_at)");
                    table.CheckConstraint("ck_moderation_material_review_generation", "source_revision = 1 AND source_writer_generation > 0 AND writer_generation > source_writer_generation");
                    table.CheckConstraint("ck_moderation_material_review_validity", "valid_until > accepted_at");
                    table.ForeignKey(
                        name: "fk_moderation_classification_material_reviews_moderation_classification_material_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "ie_moderation_classification_material_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_classification_material_categories",
                columns: table => new
                {
                    entry_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    version_id = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_classification_material_categories", x => new { x.entry_id, x.ordinal });
                    table.CheckConstraint("ck_moderation_material_category_ordinal", "ordinal BETWEEN 0 AND 63");
                    table.ForeignKey(
                        name: "fk_moderation_classification_material_categories_moderation_classification_material_entries_entry_id",
                        column: x => x.entry_id,
                        principalTable: "ie_moderation_classification_material_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_material_categories_moderation_classification_taxonomy_categories_version_id",
                        column: x => x.version_id,
                        principalTable: "ie_moderation_classification_taxonomy_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_classification_control_selections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    source_writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    source_recorded_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    source_valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    review_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    review_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    review_writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    review_valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    generation_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    generation_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    generation_writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    generation_observed_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    generation_valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    event_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    event_stamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    policy_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    policy_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    publication_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    profile_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    mode_code = table.Column<int>(type: "INTEGER", nullable: false),
                    owner_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    owner_controller_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    reviewer_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    reviewer_controller_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    authored_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    selection_digest = table.Column<string>(type: "TEXT", unicode: false, maxLength: 64, nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_classification_control_selections", x => x.id);
                    table.CheckConstraint("ck_classification_control_digest", "length(selection_digest) = 64");
                    table.CheckConstraint("ck_classification_control_generations", "revision = 1 AND source_revision = 1 AND review_revision = 1 AND generation_revision > 0 AND generation_revision < 9223372036854775807 AND generation_writer_generation > 0 AND source_writer_generation > generation_writer_generation AND review_writer_generation > source_writer_generation AND writer_generation > review_writer_generation AND writer_generation < 9223372036854775807 AND policy_revision > 0 AND policy_revision < 9223372036854775807 AND mode_code IN (1, 2)");
                    table.CheckConstraint("ck_classification_control_identity", "id <> source_id AND id <> review_id AND id <> generation_id AND source_id <> review_id AND source_id <> generation_id AND review_id <> generation_id");
                    table.CheckConstraint("ck_classification_control_independence", "reviewer_human_id <> owner_human_id AND reviewer_human_id <> owner_controller_human_id AND reviewer_controller_human_id <> owner_human_id AND reviewer_controller_human_id <> owner_controller_human_id AND authored_by_human_id <> owner_human_id AND authored_by_human_id <> owner_controller_human_id AND authored_by_human_id <> reviewer_human_id AND authored_by_human_id <> reviewer_controller_human_id");
                    table.CheckConstraint("ck_classification_control_validity", "source_valid_until > source_recorded_at AND review_valid_until > starts_at AND generation_valid_until > generation_observed_at AND valid_until > recorded_at AND starts_at >= source_recorded_at AND starts_at >= generation_observed_at AND recorded_at >= starts_at AND source_valid_until <= generation_valid_until AND review_valid_until <= source_valid_until AND valid_until <= review_valid_until");
                    table.ForeignKey(
                        name: "fk_moderation_classification_control_selections_moderation_classification_material_reviews_review_id",
                        column: x => x.review_id,
                        principalTable: "ie_moderation_classification_material_reviews",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_control_selections_moderation_classification_material_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "ie_moderation_classification_material_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_control_selections_moderation_event_generations_generation_id",
                        column: x => x.generation_id,
                        principalTable: "ie_moderation_event_generations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_control_selections_moderation_host_bindings_host_binding_id",
                        column: x => x.host_binding_id,
                        principalTable: "ie_moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_control_selections_moderation_policy_publications_publication_id",
                        column: x => x.publication_id,
                        principalTable: "ie_moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_control_selections_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_control_selections_users_authored_by_human_id",
                        column: x => x.authored_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_classification_control_bindings",
                columns: table => new
                {
                    selection_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    kind = table.Column<int>(type: "INTEGER", nullable: false),
                    effect_kind = table.Column<int>(type: "INTEGER", nullable: true),
                    effect_mode = table.Column<int>(type: "INTEGER", nullable: true),
                    effect_target = table.Column<int>(type: "INTEGER", nullable: true),
                    capability = table.Column<int>(type: "INTEGER", nullable: true),
                    execution = table.Column<int>(type: "INTEGER", nullable: true),
                    strategy = table.Column<int>(type: "INTEGER", nullable: true),
                    scope_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    scope_trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    scope_solution_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    scope_instance_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    scope_tenant_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    scope_community_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    scope_resource_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    scope_event_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    subject_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    subject_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    target_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    target_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    operation = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    window_end = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_classification_control_bindings", x => new { x.selection_id, x.ordinal });
                    table.CheckConstraint("ck_classification_control_binding_event", "scope_kind = 7");
                    table.CheckConstraint("ck_classification_control_binding_meaning", "(kind = 1 AND effect_kind BETWEEN 1 AND 26 AND effect_kind IS NOT NULL AND effect_mode BETWEEN 1 AND 8 AND effect_mode IS NOT NULL AND effect_target = 12 AND effect_target IS NOT NULL AND execution = 2 AND execution IS NOT NULL AND strategy IS NULL AND (capability IS NULL OR capability BETWEEN 1 AND 12)) OR (kind = 2 AND effect_kind IS NULL AND effect_mode IS NULL AND effect_target IS NULL AND capability IS NULL AND execution IS NULL AND (strategy IS NULL OR strategy IN (1, 2, 3)))");
                    table.CheckConstraint("ck_classification_control_binding_partition", "ordinal >= 0 AND ordinal < 64 AND target_revision > 0 AND target_revision < 9223372036854775807 AND subject_kind = 4 AND subject_id = target_id AND target_id = scope_event_id AND window_end IS NULL");
                    table.CheckConstraint("ck_moderation_classification_control_bindings_scope_scope", "scope_kind IS NOT NULL AND ((scope_kind = 1 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 2 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 3 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 4 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 5 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NOT NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 6 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_resource_id IS NOT NULL AND scope_event_id IS NULL) OR (scope_kind = 7 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NOT NULL AND scope_event_id <> '00000000-0000-0000-0000-000000000000'))");
                    table.ForeignKey(
                        name: "fk_moderation_classification_control_bindings_moderation_classification_control_selections_selection_id",
                        column: x => x.selection_id,
                        principalTable: "ie_moderation_classification_control_selections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_classification_event_target_adoptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    selection_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    selection_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    selection_writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    binding_ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    generation_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    generation_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    generation_writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    event_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    event_stamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    owner_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    owner_controller_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    reviewer_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    reviewer_controller_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    selection_authored_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    writer_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    selection_recorded_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    selection_valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    generation_observed_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    generation_valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    adopted_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    revoked_by_human_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    revocation_writer_generation = table.Column<long>(type: "INTEGER", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    revocation_grounds = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_classification_event_target_adoptions", x => x.id);
                    table.CheckConstraint("ck_classification_event_adoption_generations", "selection_revision = 1 AND selection_writer_generation > 0 AND generation_revision > 0 AND generation_revision < 9223372036854775807 AND generation_writer_generation > 0 AND writer_generation > selection_writer_generation AND writer_generation > generation_writer_generation AND writer_generation < 9223372036854775807 AND binding_ordinal >= 0 AND binding_ordinal < 64");
                    table.CheckConstraint("ck_classification_event_adoption_identity", "id <> selection_id AND id <> generation_id AND selection_id <> generation_id");
                    table.CheckConstraint("ck_classification_event_adoption_independence", "adopted_by_human_id <> owner_human_id AND adopted_by_human_id <> owner_controller_human_id AND adopted_by_human_id <> reviewer_human_id AND adopted_by_human_id <> reviewer_controller_human_id AND adopted_by_human_id <> selection_authored_by_human_id");
                    table.CheckConstraint("ck_classification_event_adoption_revocation", "(revision = 1 AND revoked_at IS NULL AND revoked_by_human_id IS NULL AND revocation_writer_generation IS NULL AND revocation_grounds IS NULL) OR (revision = 2 AND revoked_at IS NOT NULL AND revoked_by_human_id IS NOT NULL AND revocation_writer_generation IS NOT NULL AND revocation_writer_generation > writer_generation AND revocation_writer_generation < 9223372036854775807 AND revocation_grounds IS NOT NULL AND length(trim(revocation_grounds)) > 0 AND length(revocation_grounds) <= 2048 AND revoked_at >= adopted_at)");
                    table.CheckConstraint("ck_classification_event_adoption_validity", "selection_valid_until > selection_recorded_at AND generation_valid_until > generation_observed_at AND valid_until > adopted_at AND adopted_at >= selection_recorded_at AND adopted_at >= generation_observed_at AND valid_until <= selection_valid_until AND valid_until <= generation_valid_until");
                    table.ForeignKey(
                        name: "fk_moderation_classification_event_target_adoptions_moderation_classification_control_bindings_selection_id_binding_ordinal",
                        columns: x => new { x.selection_id, x.binding_ordinal },
                        principalTable: "ie_moderation_classification_control_bindings",
                        principalColumns: new[] { "selection_id", "ordinal" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_event_target_adoptions_moderation_event_generations_generation_id",
                        column: x => x.generation_id,
                        principalTable: "ie_moderation_event_generations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_event_target_adoptions_moderation_host_bindings_host_binding_id",
                        column: x => x.host_binding_id,
                        principalTable: "ie_moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_event_target_adoptions_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_event_target_adoptions_users_adopted_by_human_id",
                        column: x => x.adopted_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_event_target_adoptions_users_revoked_by_human_id",
                        column: x => x.revoked_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_event_resource_moderation_quarantines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    event_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    event_resource_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    material_version_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    effect_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    binding_ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    release_id = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_event_resource_moderation_quarantines", x => x.id);
                    table.UniqueConstraint("ak_event_resource_moderation_quarantines_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_event_resource_quarantines_cause", "effect_id <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("ck_event_resource_quarantines_material", "material_version_id <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("ck_event_resource_quarantines_ordinal", "binding_ordinal >= 0 AND binding_ordinal < 64");
                    table.ForeignKey(
                        name: "fk_event_resource_moderation_quarantines_event_resources_tenant_id_event_id_event_resource_id",
                        columns: x => new { x.tenant_id, x.event_id, x.event_resource_id },
                        principalTable: "ie_event_resources",
                        principalColumns: new[] { "tenant_id", "event_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_event_resource_moderation_review_duties",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    event_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    event_resource_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    material_version_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_event_resource_moderation_review_duties", x => x.id);
                    table.UniqueConstraint("ak_event_resource_moderation_review_duties_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_event_resource_moderation_review_duties_event_resource_moderation_quarantines_tenant_id_id",
                        columns: x => new { x.tenant_id, x.id },
                        principalTable: "ie_event_resource_moderation_quarantines",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_event_resource_quarantine_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    quarantine_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    event_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    event_resource_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    material_version_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    case_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    case_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    step = table.Column<int>(type: "INTEGER", nullable: false),
                    purpose_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    predecessor_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    reviewer_assignment_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    reviewer_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    controller_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    required_approvals = table.Column<int>(type: "INTEGER", nullable: false),
                    maximum_evidence_lifetime_ticks = table.Column<long>(type: "INTEGER", nullable: false),
                    attestation = table.Column<int>(type: "INTEGER", nullable: true),
                    resource_stamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    native_digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    request_digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    qualification_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_event_resource_quarantine_reviews", x => x.id);
                    table.UniqueConstraint("ak_event_resource_quarantine_reviews_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_resource_safety_lifetime", "maximum_evidence_lifetime_ticks > 0");
                    table.CheckConstraint("ck_resource_safety_quorum", "required_approvals BETWEEN 1 AND 2");
                    table.CheckConstraint("ck_resource_safety_step", "step BETWEEN 1 AND 6");
                    table.ForeignKey(
                        name: "fk_event_resource_quarantine_reviews_event_resource_moderation_quarantines_tenant_id_quarantine_id",
                        columns: x => new { x.tenant_id, x.quarantine_id },
                        principalTable: "ie_event_resource_moderation_quarantines",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_event_resource_moderation_review_escalations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    service_principal_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    qualification_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    escalated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    purpose_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    recipient_count = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_event_resource_moderation_review_escalations", x => x.id);
                    table.CheckConstraint("ck_resource_review_escalation_recipients", "recipient_count BETWEEN 1 AND 32");
                    table.ForeignKey(
                        name: "fk_event_resource_moderation_review_escalations_event_resource_moderation_review_duties_tenant_id_id",
                        columns: x => new { x.tenant_id, x.id },
                        principalTable: "ie_event_resource_moderation_review_duties",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_event_resource_moderation_review_completions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    quarantine_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    event_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    event_resource_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    material_version_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    purpose_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    attestation_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    approval_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    disposition = table.Column<int>(type: "INTEGER", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_event_resource_moderation_review_completions", x => x.id);
                    table.CheckConstraint("ck_resource_review_disposition", "disposition IN (1, 2)");
                    table.ForeignKey(
                        name: "fk_event_resource_moderation_review_completions_event_resource_moderation_quarantines_tenant_id_quarantine_id",
                        columns: x => new { x.tenant_id, x.quarantine_id },
                        principalTable: "ie_event_resource_moderation_quarantines",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_event_resource_moderation_review_completions_event_resource_quarantine_reviews_tenant_id_approval_id",
                        columns: x => new { x.tenant_id, x.approval_id },
                        principalTable: "ie_event_resource_quarantine_reviews",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_event_resource_moderation_review_completions_event_resource_quarantine_reviews_tenant_id_attestation_id",
                        columns: x => new { x.tenant_id, x.attestation_id },
                        principalTable: "ie_event_resource_quarantine_reviews",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_event_resource_quarantine_releases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    quarantine_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    event_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    event_resource_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    material_version_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    original_effect_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    original_binding_ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    originally_applied_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    approval_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    released_by_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    released_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    request_digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_event_resource_quarantine_releases", x => x.id);
                    table.UniqueConstraint("ak_event_resource_moderation_quarantine_release_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_event_resource_quarantine_releases_event_resource_moderation_quarantines_tenant_id_quarantine_id",
                        columns: x => new { x.tenant_id, x.quarantine_id },
                        principalTable: "ie_event_resource_moderation_quarantines",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_event_resource_quarantine_releases_event_resource_quarantine_reviews_tenant_id_approval_id",
                        columns: x => new { x.tenant_id, x.approval_id },
                        principalTable: "ie_event_resource_quarantine_reviews",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_recognition_action_recovery",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    fresh_subject_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    verified_binding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    capture_item_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    capture_material_receipt = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    original_intent_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    privacy_owner_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    recovery_owner_human_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    provider_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    disposition_by = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_recognition_action_recovery", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_recognition_capture_item",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    intent_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    erasure_subject_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    subject_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    subject_solution_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    subject_instance_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    source_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    adoption_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    adoption_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    adoption_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    ring_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    provider_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    purpose = table.Column<int>(type: "INTEGER", nullable: false),
                    key_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    key_generation = table.Column<long>(type: "INTEGER", nullable: false),
                    serialization_version = table.Column<int>(type: "INTEGER", nullable: false),
                    digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    key_verification_tag = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    sanction_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    sanction_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    decision_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    decision_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    finding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    parent_capture_item_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    continuity_binding_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    rule_owner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    rule_code = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    rule_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    original_capability_mask = table.Column<int>(type: "INTEGER", nullable: false),
                    purpose_capability_mask = table.Column<int>(type: "INTEGER", nullable: false),
                    scope_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    scope_solution_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    scope_instance_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    scope_tenant_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    scope_community_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    scope_resource_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    scope_event_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    starts_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    review_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    captured_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    original_captured_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    retain_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    dropped_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_recognition_capture_item", x => x.id);
                    table.CheckConstraint("ck_moderation_recognition_capture_item_scope_scope", "scope_kind IS NOT NULL AND ((scope_kind = 1 AND trust_domain_id IS NOT NULL AND scope_solution_id IS NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 2 AND trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 3 AND trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 4 AND trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 5 AND trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NOT NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 6 AND trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_resource_id IS NOT NULL AND scope_event_id IS NULL) OR (scope_kind = 7 AND trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NOT NULL AND scope_event_id <> '00000000-0000-0000-0000-000000000000'))");
                    table.ForeignKey(
                        name: "fk_moderation_recognition_capture_item_moderation_recognition_capture_item_parent_capture_item_id",
                        column: x => x.parent_capture_item_id,
                        principalTable: "ie_moderation_recognition_capture_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_recognition_capture_item_moderation_recognition_pending_intent_id",
                        column: x => x.intent_id,
                        principalTable: "ie_moderation_recognition_pending",
                        principalColumn: "intent_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ie_moderation_recognition_continuity_binding",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    fresh_subject_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    capture_item_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    capture_material_receipt = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    sanction_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    finding_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    bound_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    retain_until = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_recognition_continuity_binding", x => x.id);
                    table.ForeignKey(
                        name: "fk_moderation_recognition_continuity_binding_moderation_recognition_capture_item_capture_item_id",
                        column: x => x.capture_item_id,
                        principalTable: "ie_moderation_recognition_capture_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_sanctions_scope_scope",
                table: "ie_moderation_sanctions",
                sql: "scope_kind IS NOT NULL AND ((scope_kind = 1 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 2 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 3 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 4 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 5 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NOT NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 6 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_resource_id IS NOT NULL AND scope_event_id IS NULL) OR (scope_kind = 7 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NOT NULL AND scope_event_id <> '00000000-0000-0000-0000-000000000000'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_sanctions_subject_origin_scope",
                table: "ie_moderation_sanctions",
                sql: "subject_origin_kind IS NOT NULL AND ((subject_origin_kind = 1 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 2 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 3 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 4 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 5 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NOT NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 6 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_resource_id IS NOT NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 7 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NOT NULL AND subject_origin_event_id <> '00000000-0000-0000-0000-000000000000'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_qualification_receipt_result",
                table: "ie_moderation_qualification_mutation_receipts",
                sql: "source_kind BETWEEN 1 AND 44 AND result_revision > 0 AND qualification_revision > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_protective_measures_issuing_scope_scope",
                table: "ie_moderation_protective_measures",
                sql: "issuing_scope_kind IS NOT NULL AND ((issuing_scope_kind = 1 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NULL AND issuing_scope_instance_id IS NULL AND issuing_scope_tenant_id IS NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL AND issuing_scope_event_id IS NULL) OR (issuing_scope_kind = 2 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NULL AND issuing_scope_tenant_id IS NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL AND issuing_scope_event_id IS NULL) OR (issuing_scope_kind = 3 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL AND issuing_scope_event_id IS NULL) OR (issuing_scope_kind = 4 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NOT NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL AND issuing_scope_event_id IS NULL) OR (issuing_scope_kind = 5 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NOT NULL AND issuing_scope_community_id IS NOT NULL AND issuing_scope_resource_id IS NULL AND issuing_scope_event_id IS NULL) OR (issuing_scope_kind = 6 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NOT NULL AND issuing_scope_resource_id IS NOT NULL AND issuing_scope_event_id IS NULL) OR (issuing_scope_kind = 7 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NOT NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL AND issuing_scope_event_id IS NOT NULL AND issuing_scope_event_id <> '00000000-0000-0000-0000-000000000000'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_protective_measures_protector_origin_scope",
                table: "ie_moderation_protective_measures",
                sql: "protector_origin_kind IS NOT NULL AND ((protector_origin_kind = 1 AND protector_origin_trust_domain_id IS NOT NULL AND protector_origin_solution_id IS NULL AND protector_origin_instance_id IS NULL AND protector_origin_tenant_id IS NULL AND protector_origin_community_id IS NULL AND protector_origin_resource_id IS NULL AND protector_origin_event_id IS NULL) OR (protector_origin_kind = 2 AND protector_origin_trust_domain_id IS NOT NULL AND protector_origin_solution_id IS NOT NULL AND protector_origin_instance_id IS NULL AND protector_origin_tenant_id IS NULL AND protector_origin_community_id IS NULL AND protector_origin_resource_id IS NULL AND protector_origin_event_id IS NULL) OR (protector_origin_kind = 3 AND protector_origin_trust_domain_id IS NOT NULL AND protector_origin_solution_id IS NOT NULL AND protector_origin_instance_id IS NOT NULL AND protector_origin_tenant_id IS NULL AND protector_origin_community_id IS NULL AND protector_origin_resource_id IS NULL AND protector_origin_event_id IS NULL) OR (protector_origin_kind = 4 AND protector_origin_trust_domain_id IS NOT NULL AND protector_origin_solution_id IS NOT NULL AND protector_origin_instance_id IS NOT NULL AND protector_origin_tenant_id IS NOT NULL AND protector_origin_community_id IS NULL AND protector_origin_resource_id IS NULL AND protector_origin_event_id IS NULL) OR (protector_origin_kind = 5 AND protector_origin_trust_domain_id IS NOT NULL AND protector_origin_solution_id IS NOT NULL AND protector_origin_instance_id IS NOT NULL AND protector_origin_tenant_id IS NOT NULL AND protector_origin_community_id IS NOT NULL AND protector_origin_resource_id IS NULL AND protector_origin_event_id IS NULL) OR (protector_origin_kind = 6 AND protector_origin_trust_domain_id IS NOT NULL AND protector_origin_solution_id IS NOT NULL AND protector_origin_instance_id IS NOT NULL AND protector_origin_tenant_id IS NOT NULL AND protector_origin_resource_id IS NOT NULL AND protector_origin_event_id IS NULL) OR (protector_origin_kind = 7 AND protector_origin_trust_domain_id IS NOT NULL AND protector_origin_solution_id IS NOT NULL AND protector_origin_instance_id IS NOT NULL AND protector_origin_tenant_id IS NOT NULL AND protector_origin_community_id IS NULL AND protector_origin_resource_id IS NULL AND protector_origin_event_id IS NOT NULL AND protector_origin_event_id <> '00000000-0000-0000-0000-000000000000'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_protective_measures_reviewer_origin_scope",
                table: "ie_moderation_protective_measures",
                sql: "(reviewer_origin_kind IS NULL AND reviewer_origin_trust_domain_id IS NULL AND reviewer_origin_solution_id IS NULL AND reviewer_origin_instance_id IS NULL AND reviewer_origin_tenant_id IS NULL AND reviewer_origin_community_id IS NULL AND reviewer_origin_resource_id IS NULL AND reviewer_origin_event_id IS NULL) OR (reviewer_origin_kind IS NOT NULL AND ((reviewer_origin_kind = 1 AND reviewer_origin_trust_domain_id IS NOT NULL AND reviewer_origin_solution_id IS NULL AND reviewer_origin_instance_id IS NULL AND reviewer_origin_tenant_id IS NULL AND reviewer_origin_community_id IS NULL AND reviewer_origin_resource_id IS NULL AND reviewer_origin_event_id IS NULL) OR (reviewer_origin_kind = 2 AND reviewer_origin_trust_domain_id IS NOT NULL AND reviewer_origin_solution_id IS NOT NULL AND reviewer_origin_instance_id IS NULL AND reviewer_origin_tenant_id IS NULL AND reviewer_origin_community_id IS NULL AND reviewer_origin_resource_id IS NULL AND reviewer_origin_event_id IS NULL) OR (reviewer_origin_kind = 3 AND reviewer_origin_trust_domain_id IS NOT NULL AND reviewer_origin_solution_id IS NOT NULL AND reviewer_origin_instance_id IS NOT NULL AND reviewer_origin_tenant_id IS NULL AND reviewer_origin_community_id IS NULL AND reviewer_origin_resource_id IS NULL AND reviewer_origin_event_id IS NULL) OR (reviewer_origin_kind = 4 AND reviewer_origin_trust_domain_id IS NOT NULL AND reviewer_origin_solution_id IS NOT NULL AND reviewer_origin_instance_id IS NOT NULL AND reviewer_origin_tenant_id IS NOT NULL AND reviewer_origin_community_id IS NULL AND reviewer_origin_resource_id IS NULL AND reviewer_origin_event_id IS NULL) OR (reviewer_origin_kind = 5 AND reviewer_origin_trust_domain_id IS NOT NULL AND reviewer_origin_solution_id IS NOT NULL AND reviewer_origin_instance_id IS NOT NULL AND reviewer_origin_tenant_id IS NOT NULL AND reviewer_origin_community_id IS NOT NULL AND reviewer_origin_resource_id IS NULL AND reviewer_origin_event_id IS NULL) OR (reviewer_origin_kind = 6 AND reviewer_origin_trust_domain_id IS NOT NULL AND reviewer_origin_solution_id IS NOT NULL AND reviewer_origin_instance_id IS NOT NULL AND reviewer_origin_tenant_id IS NOT NULL AND reviewer_origin_resource_id IS NOT NULL AND reviewer_origin_event_id IS NULL) OR (reviewer_origin_kind = 7 AND reviewer_origin_trust_domain_id IS NOT NULL AND reviewer_origin_solution_id IS NOT NULL AND reviewer_origin_instance_id IS NOT NULL AND reviewer_origin_tenant_id IS NOT NULL AND reviewer_origin_community_id IS NULL AND reviewer_origin_resource_id IS NULL AND reviewer_origin_event_id IS NOT NULL AND reviewer_origin_event_id <> '00000000-0000-0000-0000-000000000000')))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_protective_measures_scope_scope",
                table: "ie_moderation_protective_measures",
                sql: "scope_kind IS NOT NULL AND ((scope_kind = 1 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 2 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 3 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 4 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 5 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NOT NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 6 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_resource_id IS NOT NULL AND scope_event_id IS NULL) OR (scope_kind = 7 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NOT NULL AND scope_event_id <> '00000000-0000-0000-0000-000000000000'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_protective_measures_subject_origin_scope",
                table: "ie_moderation_protective_measures",
                sql: "subject_origin_kind IS NOT NULL AND ((subject_origin_kind = 1 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 2 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 3 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 4 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 5 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NOT NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 6 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_resource_id IS NOT NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 7 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NOT NULL AND subject_origin_event_id <> '00000000-0000-0000-0000-000000000000'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_policies_jurisdiction_scope",
                table: "ie_moderation_policies",
                sql: "jurisdiction_kind IS NOT NULL AND ((jurisdiction_kind = 1 AND jurisdiction_trust_domain_id IS NOT NULL AND jurisdiction_solution_id IS NULL AND jurisdiction_instance_id IS NULL AND jurisdiction_tenant_id IS NULL AND jurisdiction_community_id IS NULL AND jurisdiction_resource_id IS NULL AND jurisdiction_event_id IS NULL) OR (jurisdiction_kind = 2 AND jurisdiction_trust_domain_id IS NOT NULL AND jurisdiction_solution_id IS NOT NULL AND jurisdiction_instance_id IS NULL AND jurisdiction_tenant_id IS NULL AND jurisdiction_community_id IS NULL AND jurisdiction_resource_id IS NULL AND jurisdiction_event_id IS NULL) OR (jurisdiction_kind = 3 AND jurisdiction_trust_domain_id IS NOT NULL AND jurisdiction_solution_id IS NOT NULL AND jurisdiction_instance_id IS NOT NULL AND jurisdiction_tenant_id IS NULL AND jurisdiction_community_id IS NULL AND jurisdiction_resource_id IS NULL AND jurisdiction_event_id IS NULL) OR (jurisdiction_kind = 4 AND jurisdiction_trust_domain_id IS NOT NULL AND jurisdiction_solution_id IS NOT NULL AND jurisdiction_instance_id IS NOT NULL AND jurisdiction_tenant_id IS NOT NULL AND jurisdiction_community_id IS NULL AND jurisdiction_resource_id IS NULL AND jurisdiction_event_id IS NULL) OR (jurisdiction_kind = 5 AND jurisdiction_trust_domain_id IS NOT NULL AND jurisdiction_solution_id IS NOT NULL AND jurisdiction_instance_id IS NOT NULL AND jurisdiction_tenant_id IS NOT NULL AND jurisdiction_community_id IS NOT NULL AND jurisdiction_resource_id IS NULL AND jurisdiction_event_id IS NULL) OR (jurisdiction_kind = 6 AND jurisdiction_trust_domain_id IS NOT NULL AND jurisdiction_solution_id IS NOT NULL AND jurisdiction_instance_id IS NOT NULL AND jurisdiction_tenant_id IS NOT NULL AND jurisdiction_resource_id IS NOT NULL AND jurisdiction_event_id IS NULL) OR (jurisdiction_kind = 7 AND jurisdiction_trust_domain_id IS NOT NULL AND jurisdiction_solution_id IS NOT NULL AND jurisdiction_instance_id IS NOT NULL AND jurisdiction_tenant_id IS NOT NULL AND jurisdiction_community_id IS NULL AND jurisdiction_resource_id IS NULL AND jurisdiction_event_id IS NOT NULL AND jurisdiction_event_id <> '00000000-0000-0000-0000-000000000000'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_policy_jurisdiction",
                table: "ie_moderation_policies",
                sql: "jurisdiction_id = CASE jurisdiction_kind WHEN 1 THEN jurisdiction_trust_domain_id WHEN 2 THEN jurisdiction_solution_id WHEN 3 THEN jurisdiction_instance_id WHEN 4 THEN jurisdiction_tenant_id WHEN 5 THEN jurisdiction_community_id WHEN 6 THEN jurisdiction_resource_id WHEN 7 THEN jurisdiction_event_id END");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_receipt_scope",
                table: "ie_moderation_operation_receipts",
                sql: "scope_kind BETWEEN 1 AND 7 AND subject_kind BETWEEN 1 AND 4");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_findings_case_id_rule_owner_id_rule_code_rule_revision_scope_kind_scope_id_attribution_generation",
                table: "ie_moderation_findings",
                columns: new[] { "case_id", "rule_owner_id", "rule_code", "rule_revision", "scope_kind", "scope_id", "attribution_generation" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_finding_attribution",
                table: "ie_moderation_findings",
                sql: "(attribution_source_id IS NULL AND attribution_generation = 0) OR (attribution_source_id IS NOT NULL AND attribution_generation > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_finding_evidence",
                table: "ie_moderation_findings",
                sql: "case_revision = 1 AND rule_revision > 0 AND reason_revision > 0 AND evidence_recorded_at >= case_created_at AND evidence_recorded_at <= finalized_at AND finalized_at >= case_created_at AND ((evidence_purpose = 1 AND urgency_qualification_id IS NULL) OR (evidence_purpose = 2 AND urgency_qualification_id IS NOT NULL AND urgency_qualification_id <> evidence_reference_id))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_findings_adjudicator_origin_scope",
                table: "ie_moderation_findings",
                sql: "adjudicator_origin_kind IS NOT NULL AND ((adjudicator_origin_kind = 1 AND adjudicator_origin_trust_domain_id IS NOT NULL AND adjudicator_origin_solution_id IS NULL AND adjudicator_origin_instance_id IS NULL AND adjudicator_origin_tenant_id IS NULL AND adjudicator_origin_community_id IS NULL AND adjudicator_origin_resource_id IS NULL AND adjudicator_origin_event_id IS NULL) OR (adjudicator_origin_kind = 2 AND adjudicator_origin_trust_domain_id IS NOT NULL AND adjudicator_origin_solution_id IS NOT NULL AND adjudicator_origin_instance_id IS NULL AND adjudicator_origin_tenant_id IS NULL AND adjudicator_origin_community_id IS NULL AND adjudicator_origin_resource_id IS NULL AND adjudicator_origin_event_id IS NULL) OR (adjudicator_origin_kind = 3 AND adjudicator_origin_trust_domain_id IS NOT NULL AND adjudicator_origin_solution_id IS NOT NULL AND adjudicator_origin_instance_id IS NOT NULL AND adjudicator_origin_tenant_id IS NULL AND adjudicator_origin_community_id IS NULL AND adjudicator_origin_resource_id IS NULL AND adjudicator_origin_event_id IS NULL) OR (adjudicator_origin_kind = 4 AND adjudicator_origin_trust_domain_id IS NOT NULL AND adjudicator_origin_solution_id IS NOT NULL AND adjudicator_origin_instance_id IS NOT NULL AND adjudicator_origin_tenant_id IS NOT NULL AND adjudicator_origin_community_id IS NULL AND adjudicator_origin_resource_id IS NULL AND adjudicator_origin_event_id IS NULL) OR (adjudicator_origin_kind = 5 AND adjudicator_origin_trust_domain_id IS NOT NULL AND adjudicator_origin_solution_id IS NOT NULL AND adjudicator_origin_instance_id IS NOT NULL AND adjudicator_origin_tenant_id IS NOT NULL AND adjudicator_origin_community_id IS NOT NULL AND adjudicator_origin_resource_id IS NULL AND adjudicator_origin_event_id IS NULL) OR (adjudicator_origin_kind = 6 AND adjudicator_origin_trust_domain_id IS NOT NULL AND adjudicator_origin_solution_id IS NOT NULL AND adjudicator_origin_instance_id IS NOT NULL AND adjudicator_origin_tenant_id IS NOT NULL AND adjudicator_origin_resource_id IS NOT NULL AND adjudicator_origin_event_id IS NULL) OR (adjudicator_origin_kind = 7 AND adjudicator_origin_trust_domain_id IS NOT NULL AND adjudicator_origin_solution_id IS NOT NULL AND adjudicator_origin_instance_id IS NOT NULL AND adjudicator_origin_tenant_id IS NOT NULL AND adjudicator_origin_community_id IS NULL AND adjudicator_origin_resource_id IS NULL AND adjudicator_origin_event_id IS NOT NULL AND adjudicator_origin_event_id <> '00000000-0000-0000-0000-000000000000'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_findings_reversed_by_origin_scope",
                table: "ie_moderation_findings",
                sql: "(reversed_by_origin_kind IS NULL AND reversed_by_origin_trust_domain_id IS NULL AND reversed_by_origin_solution_id IS NULL AND reversed_by_origin_instance_id IS NULL AND reversed_by_origin_tenant_id IS NULL AND reversed_by_origin_community_id IS NULL AND reversed_by_origin_resource_id IS NULL AND reversed_by_origin_event_id IS NULL) OR (reversed_by_origin_kind IS NOT NULL AND ((reversed_by_origin_kind = 1 AND reversed_by_origin_trust_domain_id IS NOT NULL AND reversed_by_origin_solution_id IS NULL AND reversed_by_origin_instance_id IS NULL AND reversed_by_origin_tenant_id IS NULL AND reversed_by_origin_community_id IS NULL AND reversed_by_origin_resource_id IS NULL AND reversed_by_origin_event_id IS NULL) OR (reversed_by_origin_kind = 2 AND reversed_by_origin_trust_domain_id IS NOT NULL AND reversed_by_origin_solution_id IS NOT NULL AND reversed_by_origin_instance_id IS NULL AND reversed_by_origin_tenant_id IS NULL AND reversed_by_origin_community_id IS NULL AND reversed_by_origin_resource_id IS NULL AND reversed_by_origin_event_id IS NULL) OR (reversed_by_origin_kind = 3 AND reversed_by_origin_trust_domain_id IS NOT NULL AND reversed_by_origin_solution_id IS NOT NULL AND reversed_by_origin_instance_id IS NOT NULL AND reversed_by_origin_tenant_id IS NULL AND reversed_by_origin_community_id IS NULL AND reversed_by_origin_resource_id IS NULL AND reversed_by_origin_event_id IS NULL) OR (reversed_by_origin_kind = 4 AND reversed_by_origin_trust_domain_id IS NOT NULL AND reversed_by_origin_solution_id IS NOT NULL AND reversed_by_origin_instance_id IS NOT NULL AND reversed_by_origin_tenant_id IS NOT NULL AND reversed_by_origin_community_id IS NULL AND reversed_by_origin_resource_id IS NULL AND reversed_by_origin_event_id IS NULL) OR (reversed_by_origin_kind = 5 AND reversed_by_origin_trust_domain_id IS NOT NULL AND reversed_by_origin_solution_id IS NOT NULL AND reversed_by_origin_instance_id IS NOT NULL AND reversed_by_origin_tenant_id IS NOT NULL AND reversed_by_origin_community_id IS NOT NULL AND reversed_by_origin_resource_id IS NULL AND reversed_by_origin_event_id IS NULL) OR (reversed_by_origin_kind = 6 AND reversed_by_origin_trust_domain_id IS NOT NULL AND reversed_by_origin_solution_id IS NOT NULL AND reversed_by_origin_instance_id IS NOT NULL AND reversed_by_origin_tenant_id IS NOT NULL AND reversed_by_origin_resource_id IS NOT NULL AND reversed_by_origin_event_id IS NULL) OR (reversed_by_origin_kind = 7 AND reversed_by_origin_trust_domain_id IS NOT NULL AND reversed_by_origin_solution_id IS NOT NULL AND reversed_by_origin_instance_id IS NOT NULL AND reversed_by_origin_tenant_id IS NOT NULL AND reversed_by_origin_community_id IS NULL AND reversed_by_origin_resource_id IS NULL AND reversed_by_origin_event_id IS NOT NULL AND reversed_by_origin_event_id <> '00000000-0000-0000-0000-000000000000')))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_findings_scope_scope",
                table: "ie_moderation_findings",
                sql: "scope_kind IS NOT NULL AND ((scope_kind = 1 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 2 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 3 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 4 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 5 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NOT NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 6 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_resource_id IS NOT NULL AND scope_event_id IS NULL) OR (scope_kind = 7 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NOT NULL AND scope_event_id <> '00000000-0000-0000-0000-000000000000'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_findings_subject_origin_scope",
                table: "ie_moderation_findings",
                sql: "subject_origin_kind IS NOT NULL AND ((subject_origin_kind = 1 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 2 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 3 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 4 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 5 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NOT NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 6 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_resource_id IS NOT NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 7 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NOT NULL AND subject_origin_event_id <> '00000000-0000-0000-0000-000000000000'))");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_effect_intents_completed_at_lease_expires_at_id",
                table: "ie_moderation_effect_intents",
                columns: new[] { "completed_at", "lease_expires_at", "id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_effect_completed_lease",
                table: "ie_moderation_effect_intents",
                sql: "completed_at IS NULL OR lease_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_effect_completion_window",
                table: "ie_moderation_effect_intents",
                sql: "completed_at IS NULL OR (completed_at >= lease_started_at AND completed_at < lease_expires_at)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_effect_lease",
                table: "ie_moderation_effect_intents",
                sql: "(lease_id IS NULL AND lease_started_at IS NULL AND lease_expires_at IS NULL) OR (lease_id IS NOT NULL AND lease_started_at IS NOT NULL AND lease_expires_at IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_effect_lease_window",
                table: "ie_moderation_effect_intents",
                sql: "lease_id IS NULL OR (lease_started_at >= created_at AND lease_expires_at > lease_started_at)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_effect_notice_cleanup",
                table: "ie_moderation_effect_intents",
                sql: "(original_notice_cleanup_id IS NULL AND original_notice_host_binding_id IS NULL AND original_notice_writer_tenant_id IS NULL AND original_notice_retain_until IS NULL AND original_notice_purged_at IS NULL) OR (original_notice_cleanup_id IS NOT NULL AND original_notice_host_binding_id IS NOT NULL AND original_notice_writer_tenant_id IS NOT NULL AND original_notice_retain_until IS NOT NULL AND original_notice_purged_at IS NOT NULL AND original_notice_retain_until > created_at AND original_notice_purged_at >= original_notice_retain_until AND revision > 1)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_effect_original_completion",
                table: "ie_moderation_effect_intents",
                sql: "(original_completion_id IS NULL AND original_completion_host_binding_id IS NULL AND original_completion_writer_tenant_id IS NULL AND original_completion_revision IS NULL) OR (original_completion_id IS NOT NULL AND original_completion_host_binding_id IS NOT NULL AND original_completion_writer_tenant_id IS NOT NULL AND original_completion_revision IS NOT NULL AND completed_at IS NOT NULL AND original_completion_revision >= 3 AND original_completion_revision <= revision)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_decisions_approval1_origin_scope",
                table: "ie_moderation_decisions",
                sql: "(approval1_origin_kind IS NULL AND approval1_origin_trust_domain_id IS NULL AND approval1_origin_solution_id IS NULL AND approval1_origin_instance_id IS NULL AND approval1_origin_tenant_id IS NULL AND approval1_origin_community_id IS NULL AND approval1_origin_resource_id IS NULL AND approval1_origin_event_id IS NULL) OR (approval1_origin_kind IS NOT NULL AND ((approval1_origin_kind = 1 AND approval1_origin_trust_domain_id IS NOT NULL AND approval1_origin_solution_id IS NULL AND approval1_origin_instance_id IS NULL AND approval1_origin_tenant_id IS NULL AND approval1_origin_community_id IS NULL AND approval1_origin_resource_id IS NULL AND approval1_origin_event_id IS NULL) OR (approval1_origin_kind = 2 AND approval1_origin_trust_domain_id IS NOT NULL AND approval1_origin_solution_id IS NOT NULL AND approval1_origin_instance_id IS NULL AND approval1_origin_tenant_id IS NULL AND approval1_origin_community_id IS NULL AND approval1_origin_resource_id IS NULL AND approval1_origin_event_id IS NULL) OR (approval1_origin_kind = 3 AND approval1_origin_trust_domain_id IS NOT NULL AND approval1_origin_solution_id IS NOT NULL AND approval1_origin_instance_id IS NOT NULL AND approval1_origin_tenant_id IS NULL AND approval1_origin_community_id IS NULL AND approval1_origin_resource_id IS NULL AND approval1_origin_event_id IS NULL) OR (approval1_origin_kind = 4 AND approval1_origin_trust_domain_id IS NOT NULL AND approval1_origin_solution_id IS NOT NULL AND approval1_origin_instance_id IS NOT NULL AND approval1_origin_tenant_id IS NOT NULL AND approval1_origin_community_id IS NULL AND approval1_origin_resource_id IS NULL AND approval1_origin_event_id IS NULL) OR (approval1_origin_kind = 5 AND approval1_origin_trust_domain_id IS NOT NULL AND approval1_origin_solution_id IS NOT NULL AND approval1_origin_instance_id IS NOT NULL AND approval1_origin_tenant_id IS NOT NULL AND approval1_origin_community_id IS NOT NULL AND approval1_origin_resource_id IS NULL AND approval1_origin_event_id IS NULL) OR (approval1_origin_kind = 6 AND approval1_origin_trust_domain_id IS NOT NULL AND approval1_origin_solution_id IS NOT NULL AND approval1_origin_instance_id IS NOT NULL AND approval1_origin_tenant_id IS NOT NULL AND approval1_origin_resource_id IS NOT NULL AND approval1_origin_event_id IS NULL) OR (approval1_origin_kind = 7 AND approval1_origin_trust_domain_id IS NOT NULL AND approval1_origin_solution_id IS NOT NULL AND approval1_origin_instance_id IS NOT NULL AND approval1_origin_tenant_id IS NOT NULL AND approval1_origin_community_id IS NULL AND approval1_origin_resource_id IS NULL AND approval1_origin_event_id IS NOT NULL AND approval1_origin_event_id <> '00000000-0000-0000-0000-000000000000')))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_decisions_approval2_origin_scope",
                table: "ie_moderation_decisions",
                sql: "(approval2_origin_kind IS NULL AND approval2_origin_trust_domain_id IS NULL AND approval2_origin_solution_id IS NULL AND approval2_origin_instance_id IS NULL AND approval2_origin_tenant_id IS NULL AND approval2_origin_community_id IS NULL AND approval2_origin_resource_id IS NULL AND approval2_origin_event_id IS NULL) OR (approval2_origin_kind IS NOT NULL AND ((approval2_origin_kind = 1 AND approval2_origin_trust_domain_id IS NOT NULL AND approval2_origin_solution_id IS NULL AND approval2_origin_instance_id IS NULL AND approval2_origin_tenant_id IS NULL AND approval2_origin_community_id IS NULL AND approval2_origin_resource_id IS NULL AND approval2_origin_event_id IS NULL) OR (approval2_origin_kind = 2 AND approval2_origin_trust_domain_id IS NOT NULL AND approval2_origin_solution_id IS NOT NULL AND approval2_origin_instance_id IS NULL AND approval2_origin_tenant_id IS NULL AND approval2_origin_community_id IS NULL AND approval2_origin_resource_id IS NULL AND approval2_origin_event_id IS NULL) OR (approval2_origin_kind = 3 AND approval2_origin_trust_domain_id IS NOT NULL AND approval2_origin_solution_id IS NOT NULL AND approval2_origin_instance_id IS NOT NULL AND approval2_origin_tenant_id IS NULL AND approval2_origin_community_id IS NULL AND approval2_origin_resource_id IS NULL AND approval2_origin_event_id IS NULL) OR (approval2_origin_kind = 4 AND approval2_origin_trust_domain_id IS NOT NULL AND approval2_origin_solution_id IS NOT NULL AND approval2_origin_instance_id IS NOT NULL AND approval2_origin_tenant_id IS NOT NULL AND approval2_origin_community_id IS NULL AND approval2_origin_resource_id IS NULL AND approval2_origin_event_id IS NULL) OR (approval2_origin_kind = 5 AND approval2_origin_trust_domain_id IS NOT NULL AND approval2_origin_solution_id IS NOT NULL AND approval2_origin_instance_id IS NOT NULL AND approval2_origin_tenant_id IS NOT NULL AND approval2_origin_community_id IS NOT NULL AND approval2_origin_resource_id IS NULL AND approval2_origin_event_id IS NULL) OR (approval2_origin_kind = 6 AND approval2_origin_trust_domain_id IS NOT NULL AND approval2_origin_solution_id IS NOT NULL AND approval2_origin_instance_id IS NOT NULL AND approval2_origin_tenant_id IS NOT NULL AND approval2_origin_resource_id IS NOT NULL AND approval2_origin_event_id IS NULL) OR (approval2_origin_kind = 7 AND approval2_origin_trust_domain_id IS NOT NULL AND approval2_origin_solution_id IS NOT NULL AND approval2_origin_instance_id IS NOT NULL AND approval2_origin_tenant_id IS NOT NULL AND approval2_origin_community_id IS NULL AND approval2_origin_resource_id IS NULL AND approval2_origin_event_id IS NOT NULL AND approval2_origin_event_id <> '00000000-0000-0000-0000-000000000000')))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_decisions_issuer_origin_scope",
                table: "ie_moderation_decisions",
                sql: "issuer_origin_kind IS NOT NULL AND ((issuer_origin_kind = 1 AND issuer_origin_trust_domain_id IS NOT NULL AND issuer_origin_solution_id IS NULL AND issuer_origin_instance_id IS NULL AND issuer_origin_tenant_id IS NULL AND issuer_origin_community_id IS NULL AND issuer_origin_resource_id IS NULL AND issuer_origin_event_id IS NULL) OR (issuer_origin_kind = 2 AND issuer_origin_trust_domain_id IS NOT NULL AND issuer_origin_solution_id IS NOT NULL AND issuer_origin_instance_id IS NULL AND issuer_origin_tenant_id IS NULL AND issuer_origin_community_id IS NULL AND issuer_origin_resource_id IS NULL AND issuer_origin_event_id IS NULL) OR (issuer_origin_kind = 3 AND issuer_origin_trust_domain_id IS NOT NULL AND issuer_origin_solution_id IS NOT NULL AND issuer_origin_instance_id IS NOT NULL AND issuer_origin_tenant_id IS NULL AND issuer_origin_community_id IS NULL AND issuer_origin_resource_id IS NULL AND issuer_origin_event_id IS NULL) OR (issuer_origin_kind = 4 AND issuer_origin_trust_domain_id IS NOT NULL AND issuer_origin_solution_id IS NOT NULL AND issuer_origin_instance_id IS NOT NULL AND issuer_origin_tenant_id IS NOT NULL AND issuer_origin_community_id IS NULL AND issuer_origin_resource_id IS NULL AND issuer_origin_event_id IS NULL) OR (issuer_origin_kind = 5 AND issuer_origin_trust_domain_id IS NOT NULL AND issuer_origin_solution_id IS NOT NULL AND issuer_origin_instance_id IS NOT NULL AND issuer_origin_tenant_id IS NOT NULL AND issuer_origin_community_id IS NOT NULL AND issuer_origin_resource_id IS NULL AND issuer_origin_event_id IS NULL) OR (issuer_origin_kind = 6 AND issuer_origin_trust_domain_id IS NOT NULL AND issuer_origin_solution_id IS NOT NULL AND issuer_origin_instance_id IS NOT NULL AND issuer_origin_tenant_id IS NOT NULL AND issuer_origin_resource_id IS NOT NULL AND issuer_origin_event_id IS NULL) OR (issuer_origin_kind = 7 AND issuer_origin_trust_domain_id IS NOT NULL AND issuer_origin_solution_id IS NOT NULL AND issuer_origin_instance_id IS NOT NULL AND issuer_origin_tenant_id IS NOT NULL AND issuer_origin_community_id IS NULL AND issuer_origin_resource_id IS NULL AND issuer_origin_event_id IS NOT NULL AND issuer_origin_event_id <> '00000000-0000-0000-0000-000000000000'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_decisions_issuing_scope_scope",
                table: "ie_moderation_decisions",
                sql: "issuing_scope_kind IS NOT NULL AND ((issuing_scope_kind = 1 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NULL AND issuing_scope_instance_id IS NULL AND issuing_scope_tenant_id IS NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL AND issuing_scope_event_id IS NULL) OR (issuing_scope_kind = 2 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NULL AND issuing_scope_tenant_id IS NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL AND issuing_scope_event_id IS NULL) OR (issuing_scope_kind = 3 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL AND issuing_scope_event_id IS NULL) OR (issuing_scope_kind = 4 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NOT NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL AND issuing_scope_event_id IS NULL) OR (issuing_scope_kind = 5 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NOT NULL AND issuing_scope_community_id IS NOT NULL AND issuing_scope_resource_id IS NULL AND issuing_scope_event_id IS NULL) OR (issuing_scope_kind = 6 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NOT NULL AND issuing_scope_resource_id IS NOT NULL AND issuing_scope_event_id IS NULL) OR (issuing_scope_kind = 7 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NOT NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL AND issuing_scope_event_id IS NOT NULL AND issuing_scope_event_id <> '00000000-0000-0000-0000-000000000000'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_decisions_scope_scope",
                table: "ie_moderation_decisions",
                sql: "scope_kind IS NOT NULL AND ((scope_kind = 1 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 2 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 3 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 4 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 5 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NOT NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 6 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_resource_id IS NOT NULL AND scope_event_id IS NULL) OR (scope_kind = 7 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NOT NULL AND scope_event_id <> '00000000-0000-0000-0000-000000000000'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_decisions_subject_origin_scope",
                table: "ie_moderation_decisions",
                sql: "subject_origin_kind IS NOT NULL AND ((subject_origin_kind = 1 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 2 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 3 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 4 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 5 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NOT NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 6 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_resource_id IS NOT NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 7 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NOT NULL AND subject_origin_event_id <> '00000000-0000-0000-0000-000000000000'))");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_cases_occurrence_case_id",
                table: "ie_moderation_cases",
                column: "occurrence_case_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_cases_scope_trust_domain_id_scope_tenant_id_occurrence_id_occurrence_owner_kind_occurrence_owner_id",
                table: "ie_moderation_cases",
                columns: new[] { "scope_trust_domain_id", "scope_tenant_id", "occurrence_id", "occurrence_owner_kind", "occurrence_owner_id" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_case_attribution",
                table: "ie_moderation_cases",
                sql: "(occurrence_case_id IS NULL AND occurrence_evidence_revision IS NULL AND attribution_source_id IS NULL) OR (occurrence_case_id IS NOT NULL AND occurrence_case_id <> id AND occurrence_evidence_revision > 0 AND attribution_source_id IS NOT NULL AND subject_id IS NOT NULL AND scope_kind = 4)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_case_occurrence_owner",
                table: "ie_moderation_cases",
                sql: "(subject_id IS NULL AND occurrence_owner_kind = 0 AND occurrence_owner_id = '00000000-0000-0000-0000-000000000000') OR (subject_id IS NOT NULL AND occurrence_owner_kind = subject_kind AND occurrence_owner_id = subject_id)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_case_ownership",
                table: "ie_moderation_cases",
                sql: "(subject_id IS NOT NULL AND target_kind IS NULL AND target_id IS NULL AND target_version IS NULL AND target_origin_kind IS NULL) OR (subject_id IS NULL AND target_kind IS NOT NULL AND target_kind BETWEEN 1 AND 9 AND target_id IS NOT NULL AND target_version IS NOT NULL AND target_origin_kind IS NOT NULL AND scope_kind = 4 AND scope_tenant_id IS NOT NULL AND target_origin_trust_domain_id = scope_trust_domain_id AND target_origin_solution_id = scope_solution_id AND target_origin_instance_id = scope_instance_id AND ((target_kind IN (3, 4) AND target_origin_kind = 3) OR (target_kind NOT IN (3, 4) AND target_origin_kind = 4 AND target_origin_tenant_id = scope_tenant_id)))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_cases_scope_scope",
                table: "ie_moderation_cases",
                sql: "scope_kind IS NOT NULL AND ((scope_kind = 1 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 2 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 3 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 4 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 5 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NOT NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 6 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_resource_id IS NOT NULL AND scope_event_id IS NULL) OR (scope_kind = 7 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NOT NULL AND scope_event_id <> '00000000-0000-0000-0000-000000000000'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_cases_subject_origin_scope",
                table: "ie_moderation_cases",
                sql: "(subject_origin_kind IS NULL AND subject_origin_trust_domain_id IS NULL AND subject_origin_solution_id IS NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind IS NOT NULL AND ((subject_origin_kind = 1 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 2 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 3 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 4 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 5 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NOT NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 6 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_resource_id IS NOT NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 7 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NOT NULL AND subject_origin_event_id <> '00000000-0000-0000-0000-000000000000')))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_cases_subject_subject",
                table: "ie_moderation_cases",
                sql: "(subject_kind IS NULL AND subject_id IS NULL AND subject_origin_kind IS NULL) OR (subject_kind IS NOT NULL AND subject_kind BETWEEN 1 AND 4 AND subject_id IS NOT NULL AND subject_origin_kind IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_cases_target_origin_scope",
                table: "ie_moderation_cases",
                sql: "(target_origin_kind IS NULL AND target_origin_trust_domain_id IS NULL AND target_origin_solution_id IS NULL AND target_origin_instance_id IS NULL AND target_origin_tenant_id IS NULL AND target_origin_community_id IS NULL AND target_origin_resource_id IS NULL AND target_origin_event_id IS NULL) OR (target_origin_kind IS NOT NULL AND ((target_origin_kind = 1 AND target_origin_trust_domain_id IS NOT NULL AND target_origin_solution_id IS NULL AND target_origin_instance_id IS NULL AND target_origin_tenant_id IS NULL AND target_origin_community_id IS NULL AND target_origin_resource_id IS NULL AND target_origin_event_id IS NULL) OR (target_origin_kind = 2 AND target_origin_trust_domain_id IS NOT NULL AND target_origin_solution_id IS NOT NULL AND target_origin_instance_id IS NULL AND target_origin_tenant_id IS NULL AND target_origin_community_id IS NULL AND target_origin_resource_id IS NULL AND target_origin_event_id IS NULL) OR (target_origin_kind = 3 AND target_origin_trust_domain_id IS NOT NULL AND target_origin_solution_id IS NOT NULL AND target_origin_instance_id IS NOT NULL AND target_origin_tenant_id IS NULL AND target_origin_community_id IS NULL AND target_origin_resource_id IS NULL AND target_origin_event_id IS NULL) OR (target_origin_kind = 4 AND target_origin_trust_domain_id IS NOT NULL AND target_origin_solution_id IS NOT NULL AND target_origin_instance_id IS NOT NULL AND target_origin_tenant_id IS NOT NULL AND target_origin_community_id IS NULL AND target_origin_resource_id IS NULL AND target_origin_event_id IS NULL) OR (target_origin_kind = 5 AND target_origin_trust_domain_id IS NOT NULL AND target_origin_solution_id IS NOT NULL AND target_origin_instance_id IS NOT NULL AND target_origin_tenant_id IS NOT NULL AND target_origin_community_id IS NOT NULL AND target_origin_resource_id IS NULL AND target_origin_event_id IS NULL) OR (target_origin_kind = 6 AND target_origin_trust_domain_id IS NOT NULL AND target_origin_solution_id IS NOT NULL AND target_origin_instance_id IS NOT NULL AND target_origin_tenant_id IS NOT NULL AND target_origin_resource_id IS NOT NULL AND target_origin_event_id IS NULL) OR (target_origin_kind = 7 AND target_origin_trust_domain_id IS NOT NULL AND target_origin_solution_id IS NOT NULL AND target_origin_instance_id IS NOT NULL AND target_origin_tenant_id IS NOT NULL AND target_origin_community_id IS NULL AND target_origin_resource_id IS NULL AND target_origin_event_id IS NOT NULL AND target_origin_event_id <> '00000000-0000-0000-0000-000000000000')))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_event_resources_material",
                table: "ie_event_resources",
                sql: "material_version_id IS NULL OR material_version_id <> '00000000-0000-0000-0000-000000000000'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_event_resources_quarantine",
                table: "ie_event_resources",
                sql: "moderation_quarantine_count >= 0 AND (moderation_quarantine_count = 0 OR material_version_id IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_event_report_signals_tenant_id_incoming_webhook_message_id",
                table: "ie_event_report_signals",
                columns: new[] { "tenant_id", "incoming_webhook_message_id" });

            migrationBuilder.CreateIndex(
                name: "ix_event_report_signals_tenant_id_provider_advisory_identity_advisory_ordinal",
                table: "ie_event_report_signals",
                columns: new[] { "tenant_id", "provider", "advisory_identity", "advisory_ordinal" },
                unique: true,
                filter: "advisory_identity IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_event_report_signals_tenant_id_provider_provider_target_scope_provider_target_id_correlation_id",
                table: "ie_event_report_signals",
                columns: new[] { "tenant_id", "provider", "provider_target_scope", "provider_target_id", "correlation_id" },
                unique: true,
                filter: "advisory_identity IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_event_report_signals_tenant_id_provider_provider_target_scope_provider_target_id_external_signal_id",
                table: "ie_event_report_signals",
                columns: new[] { "tenant_id", "provider", "provider_target_scope", "provider_target_id", "external_signal_id" },
                unique: true,
                filter: "external_signal_id IS NOT NULL AND advisory_identity IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_event_report_signals_tenant_id_report_id_case_id",
                table: "ie_event_report_signals",
                columns: new[] { "tenant_id", "report_id", "case_id" });

            migrationBuilder.CreateIndex(
                name: "ix_event_report_signals_tenant_id_report_id_provider_link_id",
                table: "ie_event_report_signals",
                columns: new[] { "tenant_id", "report_id", "provider_link_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_event_report_signals_advisory_source",
                table: "ie_event_report_signals",
                sql: "(advisory_identity IS NULL AND advisory_ordinal IS NULL AND advisory_material_sha256 IS NULL AND case_id IS NULL AND source_case_concurrency_stamp IS NULL AND provider_link_id IS NULL AND incoming_webhook_message_id IS NULL) OR (advisory_identity IS NOT NULL AND trim(advisory_identity) <> '' AND advisory_ordinal IS NOT NULL AND advisory_ordinal >= 0 AND advisory_material_sha256 IS NOT NULL AND length(advisory_material_sha256) = 71 AND report_id IS NOT NULL AND case_id IS NOT NULL AND source_case_concurrency_stamp IS NOT NULL AND source_case_concurrency_stamp <> '00000000-0000-0000-0000-000000000000' AND provider_link_id IS NOT NULL AND ((provider = 3 AND incoming_webhook_message_id IS NOT NULL) OR (provider = 2 AND incoming_webhook_message_id IS NULL)))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_event_report_external_links_case_generation",
                table: "ie_event_report_external_links",
                sql: "(case_id IS NULL AND case_concurrency_stamp IS NULL) OR (case_id IS NOT NULL AND case_concurrency_stamp IS NOT NULL AND case_concurrency_stamp <> '00000000-0000-0000-0000-000000000000')");

            migrationBuilder.CreateIndex(
                name: "ix_event_resource_moderation_quarantines_tenant_id_event_id_event_resource_id",
                table: "ie_event_resource_moderation_quarantines",
                columns: new[] { "tenant_id", "event_id", "event_resource_id" });

            migrationBuilder.CreateIndex(
                name: "ix_event_resource_moderation_quarantines_tenant_id_event_resource_id_effect_id_binding_ordinal",
                table: "ie_event_resource_moderation_quarantines",
                columns: new[] { "tenant_id", "event_resource_id", "effect_id", "binding_ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_event_resource_moderation_quarantines_tenant_id_release_id",
                table: "ie_event_resource_moderation_quarantines",
                columns: new[] { "tenant_id", "release_id" });

            migrationBuilder.CreateIndex(
                name: "ix_event_resource_moderation_review_completions_tenant_id_approval_id",
                table: "ie_event_resource_moderation_review_completions",
                columns: new[] { "tenant_id", "approval_id" });

            migrationBuilder.CreateIndex(
                name: "ix_event_resource_moderation_review_completions_tenant_id_attestation_id",
                table: "ie_event_resource_moderation_review_completions",
                columns: new[] { "tenant_id", "attestation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_event_resource_moderation_review_completions_tenant_id_purpose_id",
                table: "ie_event_resource_moderation_review_completions",
                columns: new[] { "tenant_id", "purpose_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_event_resource_moderation_review_completions_tenant_id_quarantine_id_completed_at",
                table: "ie_event_resource_moderation_review_completions",
                columns: new[] { "tenant_id", "quarantine_id", "completed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_event_resource_moderation_review_duties_tenant_id_due_at_id",
                table: "ie_event_resource_moderation_review_duties",
                columns: new[] { "tenant_id", "due_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_event_resource_moderation_review_escalations_tenant_id_id",
                table: "ie_event_resource_moderation_review_escalations",
                columns: new[] { "tenant_id", "id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_event_resource_quarantine_releases_quarantine_id",
                table: "ie_event_resource_quarantine_releases",
                column: "quarantine_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_event_resource_quarantine_releases_tenant_id_approval_id",
                table: "ie_event_resource_quarantine_releases",
                columns: new[] { "tenant_id", "approval_id" });

            migrationBuilder.CreateIndex(
                name: "ix_event_resource_quarantine_releases_tenant_id_quarantine_id",
                table: "ie_event_resource_quarantine_releases",
                columns: new[] { "tenant_id", "quarantine_id" });

            migrationBuilder.CreateIndex(
                name: "ix_event_resource_quarantine_reviews_tenant_id_quarantine_id_purpose_id",
                table: "ie_event_resource_quarantine_reviews",
                columns: new[] { "tenant_id", "quarantine_id", "purpose_id" });

            migrationBuilder.CreateIndex(
                name: "ix_key_obligation_intent_id",
                table: "ie_key_obligation",
                column: "intent_id");

            migrationBuilder.CreateIndex(
                name: "ix_key_obligation_trust_domain_id_key_id_key_generation",
                table: "ie_key_obligation",
                columns: new[] { "trust_domain_id", "key_id", "key_generation" });

            migrationBuilder.CreateIndex(
                name: "ix_key_replica_trust_domain_id_runtime_replica_id_ring_generation",
                table: "ie_key_replica",
                columns: new[] { "trust_domain_id", "runtime_replica_id", "ring_generation" });

            migrationBuilder.CreateIndex(
                name: "ix_key_replica_trust_domain_id_sequence",
                table: "ie_key_replica",
                columns: new[] { "trust_domain_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_attribution_heads_case_id",
                table: "ie_moderation_case_attribution_heads",
                column: "case_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_attribution_heads_host_binding_id",
                table: "ie_moderation_case_attribution_heads",
                column: "host_binding_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_attribution_heads_occurrence_case_id",
                table: "ie_moderation_case_attribution_heads",
                column: "occurrence_case_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_attribution_heads_opening_source_id",
                table: "ie_moderation_case_attribution_heads",
                column: "opening_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_attribution_heads_source_id",
                table: "ie_moderation_case_attribution_heads",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_attribution_heads_tenant_id_trust_domain_id_occurrence_case_id_subject_kind_subject_id",
                table: "ie_moderation_case_attribution_heads",
                columns: new[] { "tenant_id", "trust_domain_id", "occurrence_case_id", "subject_kind", "subject_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_attribution_renewal_receipts_case_id_expected_generation",
                table: "ie_moderation_case_attribution_renewal_receipts",
                columns: new[] { "case_id", "expected_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_attribution_renewal_receipts_expected_source_id",
                table: "ie_moderation_case_attribution_renewal_receipts",
                column: "expected_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_attribution_renewal_receipts_head_id_expected_generation",
                table: "ie_moderation_case_attribution_renewal_receipts",
                columns: new[] { "head_id", "expected_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_attribution_renewal_receipts_tenant_id",
                table: "ie_moderation_case_attribution_renewal_receipts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_attribution_sources_case_id_generation",
                table: "ie_moderation_case_attribution_sources",
                columns: new[] { "case_id", "generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_attribution_sources_evidence_source_id",
                table: "ie_moderation_case_attribution_sources",
                column: "evidence_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_attribution_sources_occurrence_case_id",
                table: "ie_moderation_case_attribution_sources",
                column: "occurrence_case_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_attribution_sources_read_grant_id",
                table: "ie_moderation_case_attribution_sources",
                column: "read_grant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_attribution_sources_tenant_id_trust_domain_id_occurrence_case_id_subject_kind_subject_id_generation",
                table: "ie_moderation_case_attribution_sources",
                columns: new[] { "tenant_id", "trust_domain_id", "occurrence_case_id", "subject_kind", "subject_id", "generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_intake_acknowledgements_budget_adoption_id_trust_domain_id_tenant_id_budget_source_id",
                table: "ie_moderation_case_intake_acknowledgements",
                columns: new[] { "budget_adoption_id", "trust_domain_id", "tenant_id", "budget_source_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_intake_acknowledgements_case_id",
                table: "ie_moderation_case_intake_acknowledgements",
                column: "case_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_intake_acknowledgements_receipt_id",
                table: "ie_moderation_case_intake_acknowledgements",
                column: "receipt_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_intake_acknowledgements_tenant_id_caller_user_id_operation_id",
                table: "ie_moderation_case_intake_acknowledgements",
                columns: new[] { "tenant_id", "caller_user_id", "operation_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_narrative_adoptions_adopted_by_human_id",
                table: "ie_moderation_case_narrative_adoptions",
                column: "adopted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_narrative_adoptions_source_id",
                table: "ie_moderation_case_narrative_adoptions",
                column: "source_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_narrative_adoptions_source_id_host_binding_id_trust_domain_id_tenant_id",
                table: "ie_moderation_case_narrative_adoptions",
                columns: new[] { "source_id", "host_binding_id", "trust_domain_id", "tenant_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_narrative_adoptions_withdrawn_by_human_id",
                table: "ie_moderation_case_narrative_adoptions",
                column: "withdrawn_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_narrative_policy_sources_governance_controller_human_id",
                table: "ie_moderation_case_narrative_policy_sources",
                column: "governance_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_narrative_policy_sources_host_binding_id_tenant_id_target_kind_purpose_writer_generation",
                table: "ie_moderation_case_narrative_policy_sources",
                columns: new[] { "host_binding_id", "tenant_id", "target_kind", "purpose", "writer_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_narrative_policy_sources_privacy_controller_human_id",
                table: "ie_moderation_case_narrative_policy_sources",
                column: "privacy_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_narrative_policy_sources_privacy_owner_human_id",
                table: "ie_moderation_case_narrative_policy_sources",
                column: "privacy_owner_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_narrative_policy_sources_recorded_by_human_id",
                table: "ie_moderation_case_narrative_policy_sources",
                column: "recorded_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_narrative_policy_sources_tenant_id",
                table: "ie_moderation_case_narrative_policy_sources",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_narratives_adoption_id_host_binding_id_trust_domain_id_tenant_id_policy_source_id",
                table: "ie_moderation_case_narratives",
                columns: new[] { "adoption_id", "host_binding_id", "trust_domain_id", "tenant_id", "policy_source_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_narratives_case_id",
                table: "ie_moderation_case_narratives",
                column: "case_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_narratives_qualification_epoch_id",
                table: "ie_moderation_case_narratives",
                column: "qualification_epoch_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_narratives_receipt_id",
                table: "ie_moderation_case_narratives",
                column: "receipt_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_narratives_reporter_user_id",
                table: "ie_moderation_case_narratives",
                column: "reporter_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_narratives_source_id",
                table: "ie_moderation_case_narratives",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_narratives_tenant_id_purge_at",
                table: "ie_moderation_case_narratives",
                columns: new[] { "tenant_id", "purge_at" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_narratives_tenant_id_trust_domain_id_case_id_source_id_material_fact_id",
                table: "ie_moderation_case_narratives",
                columns: new[] { "tenant_id", "trust_domain_id", "case_id", "source_id", "material_fact_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_read_grant_revocation_receipts_administrator_user_id",
                table: "ie_moderation_case_read_grant_revocation_receipts",
                column: "administrator_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_read_grant_revocation_receipts_grant_id",
                table: "ie_moderation_case_read_grant_revocation_receipts",
                column: "grant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_read_grant_revocation_receipts_tenant_id",
                table: "ie_moderation_case_read_grant_revocation_receipts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_read_grants_case_id",
                table: "ie_moderation_case_read_grants",
                column: "case_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_read_grants_source_id",
                table: "ie_moderation_case_read_grants",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_read_grants_tenant_id_case_id_caller_user_id",
                table: "ie_moderation_case_read_grants",
                columns: new[] { "tenant_id", "case_id", "caller_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_target_policies_tenant_id_trust_domain_id_target_kind",
                table: "ie_moderation_case_target_policies",
                columns: new[] { "tenant_id", "trust_domain_id", "target_kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_target_receipts_consumed_case_id",
                table: "ie_moderation_case_target_receipts",
                column: "consumed_case_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_target_receipts_host_binding_id",
                table: "ie_moderation_case_target_receipts",
                column: "host_binding_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_target_receipts_qualification_epoch_id",
                table: "ie_moderation_case_target_receipts",
                column: "qualification_epoch_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_target_receipts_tenant_id_caller_user_id_expires_at",
                table: "ie_moderation_case_target_receipts",
                columns: new[] { "tenant_id", "caller_user_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_target_receipts_token_digest",
                table: "ie_moderation_case_target_receipts",
                column: "token_digest",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_target_sources_event_id",
                table: "ie_moderation_case_target_sources",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_target_sources_tenant_id_trust_domain_id_kind_native_kind_native_target_id_native_target_version_1daa445d50ae",
                table: "ie_moderation_case_target_sources",
                columns: new[] { "tenant_id", "trust_domain_id", "kind", "native_kind", "native_target_id", "native_target_version", "native_facts_digest", "occurrence_digest", "material_digest" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_child_age_policy_adoptions_adopted_by_human_id",
                table: "ie_moderation_child_age_policy_adoptions",
                column: "adopted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_child_age_policy_adoptions_source_id",
                table: "ie_moderation_child_age_policy_adoptions",
                column: "source_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_child_age_policy_adoptions_withdrawn_by_human_id",
                table: "ie_moderation_child_age_policy_adoptions",
                column: "withdrawn_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_child_age_policy_sources_collection_controller_human_id",
                table: "ie_moderation_child_age_policy_sources",
                column: "collection_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_child_age_policy_sources_collection_owner_human_id",
                table: "ie_moderation_child_age_policy_sources",
                column: "collection_owner_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_child_age_policy_sources_definition_id",
                table: "ie_moderation_child_age_policy_sources",
                column: "definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_child_age_policy_sources_governance_controller_human_id",
                table: "ie_moderation_child_age_policy_sources",
                column: "governance_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_child_age_policy_sources_host_binding_id_tenant_id_publication_id_profile_id_mode_code_writer_generation",
                table: "ie_moderation_child_age_policy_sources",
                columns: new[] { "host_binding_id", "tenant_id", "publication_id", "profile_id", "mode_code", "writer_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_child_age_policy_sources_recorded_by_human_id",
                table: "ie_moderation_child_age_policy_sources",
                column: "recorded_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_child_age_policy_sources_reviewer_controller_human_id",
                table: "ie_moderation_child_age_policy_sources",
                column: "reviewer_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_child_age_policy_sources_reviewer_human_id",
                table: "ie_moderation_child_age_policy_sources",
                column: "reviewer_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_control_selections_authored_by_human_id",
                table: "ie_moderation_classification_control_selections",
                column: "authored_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_control_selections_generation_id",
                table: "ie_moderation_classification_control_selections",
                column: "generation_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_control_selections_host_binding_id_trust_domain_id_tenant_id_event_id_review_id",
                table: "ie_moderation_classification_control_selections",
                columns: new[] { "host_binding_id", "trust_domain_id", "tenant_id", "event_id", "review_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_control_selections_publication_id",
                table: "ie_moderation_classification_control_selections",
                column: "publication_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_control_selections_review_id",
                table: "ie_moderation_classification_control_selections",
                column: "review_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_control_selections_source_id",
                table: "ie_moderation_classification_control_selections",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_control_selections_tenant_id",
                table: "ie_moderation_classification_control_selections",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_event_target_adoptions_adopted_by_human_id",
                table: "ie_moderation_classification_event_target_adoptions",
                column: "adopted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_event_target_adoptions_generation_id",
                table: "ie_moderation_classification_event_target_adoptions",
                column: "generation_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_event_target_adoptions_host_binding_id",
                table: "ie_moderation_classification_event_target_adoptions",
                column: "host_binding_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_event_target_adoptions_revoked_by_human_id",
                table: "ie_moderation_classification_event_target_adoptions",
                column: "revoked_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_event_target_adoptions_selection_id_binding_ordinal",
                table: "ie_moderation_classification_event_target_adoptions",
                columns: new[] { "selection_id", "binding_ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_event_target_adoptions_tenant_id",
                table: "ie_moderation_classification_event_target_adoptions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_material_categories_entry_id_version_id",
                table: "ie_moderation_classification_material_categories",
                columns: new[] { "entry_id", "version_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_material_categories_version_id",
                table: "ie_moderation_classification_material_categories",
                column: "version_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_material_components_source_id_kind_native_id",
                table: "ie_moderation_classification_material_components",
                columns: new[] { "source_id", "kind", "native_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_material_entries_source_id_ordinal",
                table: "ie_moderation_classification_material_entries",
                columns: new[] { "source_id", "ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_material_reviews_source_id",
                table: "ie_moderation_classification_material_reviews",
                column: "source_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_material_sources_event_generation_id",
                table: "ie_moderation_classification_material_sources",
                column: "event_generation_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_material_sources_host_binding_id_tenant_id_event_id_writer_generation",
                table: "ie_moderation_classification_material_sources",
                columns: new[] { "host_binding_id", "tenant_id", "event_id", "writer_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_material_sources_taxonomy_adoption_id",
                table: "ie_moderation_classification_material_sources",
                column: "taxonomy_adoption_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_material_sources_taxonomy_source_id",
                table: "ie_moderation_classification_material_sources",
                column: "taxonomy_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_taxonomy_adoptions_adopted_by_human_id",
                table: "ie_moderation_classification_taxonomy_adoptions",
                column: "adopted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_taxonomy_adoptions_source_id",
                table: "ie_moderation_classification_taxonomy_adoptions",
                column: "source_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_taxonomy_adoptions_withdrawn_by_human_id",
                table: "ie_moderation_classification_taxonomy_adoptions",
                column: "withdrawn_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_taxonomy_categories_source_id_category_id",
                table: "ie_moderation_classification_taxonomy_categories",
                columns: new[] { "source_id", "category_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_taxonomy_categories_source_id_code",
                table: "ie_moderation_classification_taxonomy_categories",
                columns: new[] { "source_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_taxonomy_categories_source_id_ordinal",
                table: "ie_moderation_classification_taxonomy_categories",
                columns: new[] { "source_id", "ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_taxonomy_sources_child_age_adoption_id",
                table: "ie_moderation_classification_taxonomy_sources",
                column: "child_age_adoption_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_taxonomy_sources_child_age_source_id",
                table: "ie_moderation_classification_taxonomy_sources",
                column: "child_age_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_taxonomy_sources_constitution_adoption_id",
                table: "ie_moderation_classification_taxonomy_sources",
                column: "constitution_adoption_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_taxonomy_sources_constitution_source_id",
                table: "ie_moderation_classification_taxonomy_sources",
                column: "constitution_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_taxonomy_sources_definition_id",
                table: "ie_moderation_classification_taxonomy_sources",
                column: "definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_taxonomy_sources_host_binding_id_tenant_id_taxonomy_id_taxonomy_revision",
                table: "ie_moderation_classification_taxonomy_sources",
                columns: new[] { "host_binding_id", "tenant_id", "taxonomy_id", "taxonomy_revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_taxonomy_sources_host_binding_id_tenant_id_taxonomy_id_writer_generation",
                table: "ie_moderation_classification_taxonomy_sources",
                columns: new[] { "host_binding_id", "tenant_id", "taxonomy_id", "writer_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_taxonomy_sources_owner_controller_human_id",
                table: "ie_moderation_classification_taxonomy_sources",
                column: "owner_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_taxonomy_sources_owner_human_id",
                table: "ie_moderation_classification_taxonomy_sources",
                column: "owner_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_taxonomy_sources_reviewer_controller_human_id",
                table: "ie_moderation_classification_taxonomy_sources",
                column: "reviewer_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_taxonomy_sources_reviewer_human_id",
                table: "ie_moderation_classification_taxonomy_sources",
                column: "reviewer_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_constitution_adoptions_adopted_by_human_id",
                table: "ie_moderation_constitution_adoptions",
                column: "adopted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_constitution_adoptions_source_id",
                table: "ie_moderation_constitution_adoptions",
                column: "source_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_constitution_adoptions_withdrawn_by_human_id",
                table: "ie_moderation_constitution_adoptions",
                column: "withdrawn_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_constitution_availability_host_binding_id_tenant_id_reviewer_human_id_writer_generation",
                table: "ie_moderation_constitution_availability",
                columns: new[] { "host_binding_id", "tenant_id", "reviewer_human_id", "writer_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_constitution_availability_reviewer_human_id",
                table: "ie_moderation_constitution_availability",
                column: "reviewer_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_constitution_availability_withdrawn_by_human_id",
                table: "ie_moderation_constitution_availability",
                column: "withdrawn_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_constitution_reviewers_availability_id",
                table: "ie_moderation_constitution_reviewers",
                column: "availability_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_constitution_reviewers_controller_human_id",
                table: "ie_moderation_constitution_reviewers",
                column: "controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_constitution_reviewers_reviewer_human_id",
                table: "ie_moderation_constitution_reviewers",
                column: "reviewer_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_constitution_reviewers_source_id_availability_id",
                table: "ie_moderation_constitution_reviewers",
                columns: new[] { "source_id", "availability_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_constitution_reviewers_source_id_controller_human_id",
                table: "ie_moderation_constitution_reviewers",
                columns: new[] { "source_id", "controller_human_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_constitution_reviewers_source_id_reviewer_human_id",
                table: "ie_moderation_constitution_reviewers",
                columns: new[] { "source_id", "reviewer_human_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_constitution_sources_coordinator_human_id",
                table: "ie_moderation_constitution_sources",
                column: "coordinator_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_constitution_sources_definition_id",
                table: "ie_moderation_constitution_sources",
                column: "definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_constitution_sources_fallback_human_id",
                table: "ie_moderation_constitution_sources",
                column: "fallback_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_constitution_sources_host_binding_id_tenant_id_publication_id_profile_id_mode_code_writer_generation",
                table: "ie_moderation_constitution_sources",
                columns: new[] { "host_binding_id", "tenant_id", "publication_id", "profile_id", "mode_code", "writer_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_constitution_sources_original_effect_id",
                table: "ie_moderation_constitution_sources",
                column: "original_effect_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_constitution_sources_recorded_by_human_id",
                table: "ie_moderation_constitution_sources",
                column: "recorded_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_consumer_installations_host_binding_id_tenant_id_consumer_id_mode_code_writer_generation",
                table: "ie_moderation_consumer_installations",
                columns: new[] { "host_binding_id", "tenant_id", "consumer_id", "mode_code", "writer_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_consumer_installations_installed_by_human_id",
                table: "ie_moderation_consumer_installations",
                column: "installed_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_consumer_installations_tenant_id",
                table: "ie_moderation_consumer_installations",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_consumer_installations_withdrawn_by_human_id",
                table: "ie_moderation_consumer_installations",
                column: "withdrawn_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_consumer_measurement_challenges_definition_id",
                table: "ie_moderation_consumer_measurement_challenges",
                column: "definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_consumer_measurement_challenges_host_binding_id",
                table: "ie_moderation_consumer_measurement_challenges",
                column: "host_binding_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_consumer_measurement_challenges_installation_id",
                table: "ie_moderation_consumer_measurement_challenges",
                column: "installation_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_consumer_measurement_challenges_nonce",
                table: "ie_moderation_consumer_measurement_challenges",
                column: "nonce",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_consumer_measurement_challenges_original_effect_id",
                table: "ie_moderation_consumer_measurement_challenges",
                column: "original_effect_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_consumer_measurement_challenges_prepared_by_human_id",
                table: "ie_moderation_consumer_measurement_challenges",
                column: "prepared_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_consumer_measurement_challenges_tenant_id",
                table: "ie_moderation_consumer_measurement_challenges",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_consumer_measurement_receipts_accepted_by_human_id",
                table: "ie_moderation_consumer_measurement_receipts",
                column: "accepted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_consumer_measurement_receipts_challenge_id",
                table: "ie_moderation_consumer_measurement_receipts",
                column: "challenge_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_consumer_measurement_receipts_host_binding_id_tenant_id_challenge_id",
                table: "ie_moderation_consumer_measurement_receipts",
                columns: new[] { "host_binding_id", "tenant_id", "challenge_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_consumer_measurement_receipts_host_binding_id_tenant_id_run_id",
                table: "ie_moderation_consumer_measurement_receipts",
                columns: new[] { "host_binding_id", "tenant_id", "run_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_consumer_measurement_receipts_issuer_enrollment_id",
                table: "ie_moderation_consumer_measurement_receipts",
                column: "issuer_enrollment_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_consumer_measurement_receipts_tenant_id",
                table: "ie_moderation_consumer_measurement_receipts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_consumer_measurement_receipts_token_digest",
                table: "ie_moderation_consumer_measurement_receipts",
                column: "token_digest");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_content_responses_case_id",
                table: "ie_moderation_content_responses",
                column: "case_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_content_responses_effect_id",
                table: "ie_moderation_content_responses",
                column: "effect_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_content_responses_notice_id",
                table: "ie_moderation_content_responses",
                column: "notice_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_content_responses_representative_human_id",
                table: "ie_moderation_content_responses",
                column: "representative_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_content_responses_tenant_id_effect_id",
                table: "ie_moderation_content_responses",
                columns: new[] { "tenant_id", "effect_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_content_responses_tenant_id_representative_human_id",
                table: "ie_moderation_content_responses",
                columns: new[] { "tenant_id", "representative_human_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_contribution_selections_authored_by_human_id",
                table: "ie_moderation_contribution_selections",
                column: "authored_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_contribution_selections_case_id",
                table: "ie_moderation_contribution_selections",
                column: "case_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_contribution_selections_decision_id",
                table: "ie_moderation_contribution_selections",
                column: "decision_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_contribution_selections_host_binding_id_trust_domain_id_tenant_id_effect_id",
                table: "ie_moderation_contribution_selections",
                columns: new[] { "host_binding_id", "trust_domain_id", "tenant_id", "effect_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_contribution_selections_publication_id",
                table: "ie_moderation_contribution_selections",
                column: "publication_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_contribution_selections_tenant_id",
                table: "ie_moderation_contribution_selections",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_deployment_adoptions_adopted_by_human_id",
                table: "ie_moderation_deployment_adoptions",
                column: "adopted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_deployment_adoptions_source_id",
                table: "ie_moderation_deployment_adoptions",
                column: "source_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_deployment_adoptions_withdrawn_by_human_id",
                table: "ie_moderation_deployment_adoptions",
                column: "withdrawn_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_deployment_consumers_installation_id",
                table: "ie_moderation_deployment_consumers",
                column: "installation_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_deployment_consumers_source_id_binding_digest_consumer_id",
                table: "ie_moderation_deployment_consumers",
                columns: new[] { "source_id", "binding_digest", "consumer_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_deployment_sources_definition_id",
                table: "ie_moderation_deployment_sources",
                column: "definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_deployment_sources_host_binding_id_tenant_id_publication_id_profile_id_mode_code_writer_generation",
                table: "ie_moderation_deployment_sources",
                columns: new[] { "host_binding_id", "tenant_id", "publication_id", "profile_id", "mode_code", "writer_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_deployment_sources_recorded_by_human_id",
                table: "ie_moderation_deployment_sources",
                column: "recorded_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_event_generations_host_binding_id_tenant_id_event_id_revision",
                table: "ie_moderation_event_generations",
                columns: new[] { "host_binding_id", "tenant_id", "event_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_event_generations_observed_by_human_id",
                table: "ie_moderation_event_generations",
                column: "observed_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_event_generations_tenant_id",
                table: "ie_moderation_event_generations",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_event_rights_adoptions_adopted_by_human_id",
                table: "ie_moderation_event_rights_adoptions",
                column: "adopted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_event_rights_adoptions_source_id",
                table: "ie_moderation_event_rights_adoptions",
                column: "source_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_event_rights_adoptions_withdrawn_by_human_id",
                table: "ie_moderation_event_rights_adoptions",
                column: "withdrawn_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_event_rights_sources_continuity_mapping_controller_human_id",
                table: "ie_moderation_event_rights_sources",
                column: "continuity_mapping_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_event_rights_sources_continuity_mapping_owner_human_id",
                table: "ie_moderation_event_rights_sources",
                column: "continuity_mapping_owner_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_event_rights_sources_definition_id",
                table: "ie_moderation_event_rights_sources",
                column: "definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_event_rights_sources_governance_controller_human_id",
                table: "ie_moderation_event_rights_sources",
                column: "governance_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_event_rights_sources_host_binding_id_tenant_id_publication_id_profile_id_mode_code_writer_generation",
                table: "ie_moderation_event_rights_sources",
                columns: new[] { "host_binding_id", "tenant_id", "publication_id", "profile_id", "mode_code", "writer_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_event_rights_sources_recorded_by_human_id",
                table: "ie_moderation_event_rights_sources",
                column: "recorded_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_event_rights_sources_reviewer_controller_human_id",
                table: "ie_moderation_event_rights_sources",
                column: "reviewer_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_event_rights_sources_reviewer_human_id",
                table: "ie_moderation_event_rights_sources",
                column: "reviewer_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_event_rights_sources_settlement_mapping_controller_human_id",
                table: "ie_moderation_event_rights_sources",
                column: "settlement_mapping_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_event_rights_sources_settlement_mapping_owner_human_id",
                table: "ie_moderation_event_rights_sources",
                column: "settlement_mapping_owner_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_event_target_adoptions_adopted_by_human_id",
                table: "ie_moderation_event_target_adoptions",
                column: "adopted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_event_target_adoptions_generation_id",
                table: "ie_moderation_event_target_adoptions",
                column: "generation_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_event_target_adoptions_revoked_by_human_id",
                table: "ie_moderation_event_target_adoptions",
                column: "revoked_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_event_target_adoptions_selection_id_binding_ordinal",
                table: "ie_moderation_event_target_adoptions",
                columns: new[] { "selection_id", "binding_ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_measured_operations_acceptances_accepted_by_human_id",
                table: "ie_moderation_measured_operations_acceptances",
                column: "accepted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_measured_operations_acceptances_source_id",
                table: "ie_moderation_measured_operations_acceptances",
                column: "source_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_measured_operations_acceptances_withdrawn_by_human_id",
                table: "ie_moderation_measured_operations_acceptances",
                column: "withdrawn_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_measured_operations_sources_definition_id",
                table: "ie_moderation_measured_operations_sources",
                column: "definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_measured_operations_sources_host_binding_id_tenant_id_binding_digest_writer_generation",
                table: "ie_moderation_measured_operations_sources",
                columns: new[] { "host_binding_id", "tenant_id", "binding_digest", "writer_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_measured_operations_sources_original_effect_id",
                table: "ie_moderation_measured_operations_sources",
                column: "original_effect_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_measured_operations_sources_recorded_by_human_id",
                table: "ie_moderation_measured_operations_sources",
                column: "recorded_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_measured_operations_sources_recorder_controller_human_id",
                table: "ie_moderation_measured_operations_sources",
                column: "recorder_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_measured_operations_sources_reviewer_controller_human_id",
                table: "ie_moderation_measured_operations_sources",
                column: "reviewer_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_measured_operations_sources_reviewer_human_id",
                table: "ie_moderation_measured_operations_sources",
                column: "reviewer_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_measurement_issuer_enrollments_approved_by_human_id",
                table: "ie_moderation_measurement_issuer_enrollments",
                column: "approved_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_measurement_issuer_enrollments_host_binding_id_tenant_id_key_id",
                table: "ie_moderation_measurement_issuer_enrollments",
                columns: new[] { "host_binding_id", "tenant_id", "key_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_measurement_issuer_enrollments_revoked_by_human_id",
                table: "ie_moderation_measurement_issuer_enrollments",
                column: "revoked_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_measurement_issuer_enrollments_tenant_id",
                table: "ie_moderation_measurement_issuer_enrollments",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_notice_disclosure_approvals_approved_by_human_id",
                table: "ie_moderation_notice_disclosure_approvals",
                column: "approved_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_notice_disclosure_approvals_case_id",
                table: "ie_moderation_notice_disclosure_approvals",
                column: "case_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_notice_disclosure_approvals_decision_id",
                table: "ie_moderation_notice_disclosure_approvals",
                column: "decision_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_notice_disclosure_approvals_host_binding_id_trust_domain_id_tenant_id_effect_id",
                table: "ie_moderation_notice_disclosure_approvals",
                columns: new[] { "host_binding_id", "trust_domain_id", "tenant_id", "effect_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_notice_disclosure_approvals_publication_id",
                table: "ie_moderation_notice_disclosure_approvals",
                column: "publication_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_notice_disclosure_approvals_revoked_by_human_id",
                table: "ie_moderation_notice_disclosure_approvals",
                column: "revoked_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_notice_disclosure_approvals_tenant_id",
                table: "ie_moderation_notice_disclosure_approvals",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_notice_notification_receipts_effect_id",
                table: "ie_moderation_notice_notification_receipts",
                column: "effect_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_notice_notification_receipts_notice_id",
                table: "ie_moderation_notice_notification_receipts",
                column: "notice_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_notice_notification_receipts_tenant_id",
                table: "ie_moderation_notice_notification_receipts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_notice_sources_trust_domain_id_retain_until_effect_id",
                table: "ie_moderation_notice_sources",
                columns: new[] { "trust_domain_id", "retain_until", "effect_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_notice_sources_trust_domain_id_subject_kind_subject_id_captured_at",
                table: "ie_moderation_notice_sources",
                columns: new[] { "trust_domain_id", "subject_kind", "subject_id", "captured_at" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_notices_effect_id_audience_recipient_subject_kind_recipient_subject_id",
                table: "ie_moderation_notices",
                columns: new[] { "effect_id", "audience", "recipient_subject_kind", "recipient_subject_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_notices_native_intent_id",
                table: "ie_moderation_notices",
                column: "native_intent_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_notices_trust_domain_id_recipient_subject_kind_recipient_subject_id_created_at",
                table: "ie_moderation_notices",
                columns: new[] { "trust_domain_id", "recipient_subject_kind", "recipient_subject_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_operational_budget_adoptions_adopted_by_human_id",
                table: "ie_moderation_operational_budget_adoptions",
                column: "adopted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_operational_budget_adoptions_source_id",
                table: "ie_moderation_operational_budget_adoptions",
                column: "source_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_operational_budget_adoptions_source_id_host_binding_id_trust_domain_id_tenant_id",
                table: "ie_moderation_operational_budget_adoptions",
                columns: new[] { "source_id", "host_binding_id", "trust_domain_id", "tenant_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_operational_budget_adoptions_withdrawn_by_human_id",
                table: "ie_moderation_operational_budget_adoptions",
                column: "withdrawn_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_operational_budget_sources_host_binding_id_tenant_id_subject_kind_action_writer_generation",
                table: "ie_moderation_operational_budget_sources",
                columns: new[] { "host_binding_id", "tenant_id", "subject_kind", "action", "writer_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_operational_budget_sources_operator_human_id",
                table: "ie_moderation_operational_budget_sources",
                column: "operator_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_operational_budget_sources_recorded_by_human_id",
                table: "ie_moderation_operational_budget_sources",
                column: "recorded_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_operational_budget_sources_tenant_id",
                table: "ie_moderation_operational_budget_sources",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_original_effect_completion_receipts_effect_id",
                table: "ie_moderation_original_effect_completion_receipts",
                column: "effect_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_participant_incident_responsibility_sources_assessor_human_id",
                table: "ie_moderation_participant_incident_responsibility_sources",
                column: "assessor_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_participant_incident_responsibility_sources_custody_adoption_id_host_binding_id_trust_domain_id_tenan_10036188741d",
                table: "ie_moderation_participant_incident_responsibility_sources",
                columns: new[] { "custody_adoption_id", "host_binding_id", "trust_domain_id", "tenant_id", "custody_source_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_participant_incident_responsibility_sources_custody_source_id_host_binding_id_trust_domain_id_tenant_id",
                table: "ie_moderation_participant_incident_responsibility_sources",
                columns: new[] { "custody_source_id", "host_binding_id", "trust_domain_id", "tenant_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_participant_incident_responsibility_sources_event_id",
                table: "ie_moderation_participant_incident_responsibility_sources",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_participant_incident_responsibility_sources_evidence_source_id",
                table: "ie_moderation_participant_incident_responsibility_sources",
                column: "evidence_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_participant_incident_responsibility_sources_host_binding_id",
                table: "ie_moderation_participant_incident_responsibility_sources",
                column: "host_binding_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_participant_incident_responsibility_sources_occurrence_case_id",
                table: "ie_moderation_participant_incident_responsibility_sources",
                column: "occurrence_case_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_participant_incident_responsibility_sources_order_id",
                table: "ie_moderation_participant_incident_responsibility_sources",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_participant_incident_responsibility_sources_original_narrative_id",
                table: "ie_moderation_participant_incident_responsibility_sources",
                column: "original_narrative_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_participant_incident_responsibility_sources_participant_id",
                table: "ie_moderation_participant_incident_responsibility_sources",
                column: "participant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_participant_incident_responsibility_sources_qualification_policy_id",
                table: "ie_moderation_participant_incident_responsibility_sources",
                column: "qualification_policy_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_participant_incident_responsibility_sources_read_grant_id",
                table: "ie_moderation_participant_incident_responsibility_sources",
                column: "read_grant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_participant_incident_responsibility_sources_read_issuer_human_id",
                table: "ie_moderation_participant_incident_responsibility_sources",
                column: "read_issuer_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_participant_incident_responsibility_sources_session_id",
                table: "ie_moderation_participant_incident_responsibility_sources",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_participant_incident_responsibility_sources_subject_id",
                table: "ie_moderation_participant_incident_responsibility_sources",
                column: "subject_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_participant_incident_responsibility_sources_tenant_id_trust_domain_id_occurrence_case_id_subject_id",
                table: "ie_moderation_participant_incident_responsibility_sources",
                columns: new[] { "tenant_id", "trust_domain_id", "occurrence_case_id", "subject_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_participant_incident_responsibility_sources_tenant_id_trust_domain_id_purge_at",
                table: "ie_moderation_participant_incident_responsibility_sources",
                columns: new[] { "tenant_id", "trust_domain_id", "purge_at" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_participant_incident_responsibility_sources_withdrawn_by_human_id",
                table: "ie_moderation_participant_incident_responsibility_sources",
                column: "withdrawn_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_policy_publication_candidates_authority_source_id",
                table: "ie_moderation_policy_publication_candidates",
                column: "authority_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_policy_publication_candidates_definition_id",
                table: "ie_moderation_policy_publication_candidates",
                column: "definition_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_policy_publication_candidates_host_binding_id",
                table: "ie_moderation_policy_publication_candidates",
                column: "host_binding_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_policy_publication_candidates_operation_id",
                table: "ie_moderation_policy_publication_candidates",
                column: "operation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_policy_publication_candidates_policy_id_definition_id",
                table: "ie_moderation_policy_publication_candidates",
                columns: new[] { "policy_id", "definition_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_policy_publication_candidates_policy_id_previous_publication_id",
                table: "ie_moderation_policy_publication_candidates",
                columns: new[] { "policy_id", "previous_publication_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_policy_publication_candidates_prepared_by_human_id",
                table: "ie_moderation_policy_publication_candidates",
                column: "prepared_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_policy_publication_candidates_tenant_id",
                table: "ie_moderation_policy_publication_candidates",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_policy_publications_authority_source_id",
                table: "ie_moderation_policy_publications",
                column: "authority_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_policy_publications_host_binding_id",
                table: "ie_moderation_policy_publications",
                column: "host_binding_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_policy_publications_operation_id",
                table: "ie_moderation_policy_publications",
                column: "operation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_policy_publications_policy_id_adopted_revision",
                table: "ie_moderation_policy_publications",
                columns: new[] { "policy_id", "adopted_revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_policy_publications_policy_id_previous_publication_id",
                table: "ie_moderation_policy_publications",
                columns: new[] { "policy_id", "previous_publication_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_policy_publications_tenant_id",
                table: "ie_moderation_policy_publications",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_policy_selections_host_binding_id_jurisdiction_id",
                table: "ie_moderation_policy_selections",
                columns: new[] { "host_binding_id", "jurisdiction_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_policy_selections_id_active_publication_id",
                table: "ie_moderation_policy_selections",
                columns: new[] { "id", "active_publication_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_policy_selections_id_latest_publication_id",
                table: "ie_moderation_policy_selections",
                columns: new[] { "id", "latest_publication_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_pre_finding_custody_attribution_source_id",
                table: "ie_moderation_pre_finding_custody",
                column: "attribution_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_pre_finding_custody_case_id",
                table: "ie_moderation_pre_finding_custody",
                column: "case_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_pre_finding_custody_privacy_owner_human_id",
                table: "ie_moderation_pre_finding_custody",
                column: "privacy_owner_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_pre_finding_custody_proposed_by_human_id",
                table: "ie_moderation_pre_finding_custody",
                column: "proposed_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_pre_finding_notices_case_id",
                table: "ie_moderation_pre_finding_notices",
                column: "case_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_pre_finding_notices_custody_id_host_binding_id_trust_domain_id_tenant_id_case_id",
                table: "ie_moderation_pre_finding_notices",
                columns: new[] { "custody_id", "host_binding_id", "trust_domain_id", "tenant_id", "case_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_pre_finding_notices_occurrence_case_id",
                table: "ie_moderation_pre_finding_notices",
                column: "occurrence_case_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_pre_finding_notices_recipient_human_id",
                table: "ie_moderation_pre_finding_notices",
                column: "recipient_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_pre_finding_notices_reviewer_assignment_id",
                table: "ie_moderation_pre_finding_notices",
                column: "reviewer_assignment_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_pre_finding_notices_tenant_id_retain_until",
                table: "ie_moderation_pre_finding_notices",
                columns: new[] { "tenant_id", "retain_until" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_pre_finding_receipts_actor_human_id",
                table: "ie_moderation_pre_finding_receipts",
                column: "actor_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_pre_finding_receipts_case_id",
                table: "ie_moderation_pre_finding_receipts",
                column: "case_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_pre_finding_receipts_result_id_result_revision",
                table: "ie_moderation_pre_finding_receipts",
                columns: new[] { "result_id", "result_revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_adoptions_adopted_by_human_id",
                table: "ie_moderation_profile_adoptions",
                column: "adopted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_adoptions_publication_id",
                table: "ie_moderation_profile_adoptions",
                column: "publication_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_adoptions_revoked_by_human_id",
                table: "ie_moderation_profile_adoptions",
                column: "revoked_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_adoptions_selection_id",
                table: "ie_moderation_profile_adoptions",
                column: "selection_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_assessment_adoptions_adopted_by_human_id",
                table: "ie_moderation_profile_collection_assessment_adoptions",
                column: "adopted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_assessment_adoptions_source_id",
                table: "ie_moderation_profile_collection_assessment_adoptions",
                column: "source_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_assessment_adoptions_withdrawn_by_human_id",
                table: "ie_moderation_profile_collection_assessment_adoptions",
                column: "withdrawn_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_assessment_sources_owner_controller_human_id",
                table: "ie_moderation_profile_collection_assessment_sources",
                column: "owner_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_assessment_sources_procedure_adopted_by_human_id",
                table: "ie_moderation_profile_collection_assessment_sources",
                column: "procedure_adopted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_assessment_sources_procedure_adoption_id",
                table: "ie_moderation_profile_collection_assessment_sources",
                column: "procedure_adoption_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_assessment_sources_procedure_source_id",
                table: "ie_moderation_profile_collection_assessment_sources",
                column: "procedure_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_assessment_sources_recorded_by_human_id",
                table: "ie_moderation_profile_collection_assessment_sources",
                column: "recorded_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_assessment_sources_reviewer_controller_human_id",
                table: "ie_moderation_profile_collection_assessment_sources",
                column: "reviewer_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_assessment_sources_subject_human_id_procedure_source_id_generation",
                table: "ie_moderation_profile_collection_assessment_sources",
                columns: new[] { "subject_human_id", "procedure_source_id", "generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_procedure_adoptions_adopted_by_human_id",
                table: "ie_moderation_profile_collection_procedure_adoptions",
                column: "adopted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_procedure_adoptions_source_id",
                table: "ie_moderation_profile_collection_procedure_adoptions",
                column: "source_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_procedure_adoptions_withdrawn_by_human_id",
                table: "ie_moderation_profile_collection_procedure_adoptions",
                column: "withdrawn_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_procedure_sources_child_age_adoption_id",
                table: "ie_moderation_profile_collection_procedure_sources",
                column: "child_age_adoption_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_procedure_sources_child_age_source_id",
                table: "ie_moderation_profile_collection_procedure_sources",
                column: "child_age_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_procedure_sources_host_binding_id_tenant_id_publication_id_profile_id_mode_code_wr_4e53d6921a1b",
                table: "ie_moderation_profile_collection_procedure_sources",
                columns: new[] { "host_binding_id", "tenant_id", "publication_id", "profile_id", "mode_code", "writer_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_procedure_sources_recorded_by_human_id",
                table: "ie_moderation_profile_collection_procedure_sources",
                column: "recorded_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_subject_observations_epoch_id",
                table: "ie_moderation_profile_collection_subject_observations",
                column: "epoch_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_subject_observations_host_binding_id_tenant_id_subject_human_id_profile_id_mode_co_f5c61ce2f37a",
                table: "ie_moderation_profile_collection_subject_observations",
                columns: new[] { "host_binding_id", "tenant_id", "subject_human_id", "profile_id", "mode_code", "observation_sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_subject_observations_observation_stamp",
                table: "ie_moderation_profile_collection_subject_observations",
                column: "observation_stamp",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_subject_observations_procedure_adoption_id",
                table: "ie_moderation_profile_collection_subject_observations",
                column: "procedure_adoption_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_subject_observations_procedure_source_id",
                table: "ie_moderation_profile_collection_subject_observations",
                column: "procedure_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_subject_observations_subject_human_id",
                table: "ie_moderation_profile_collection_subject_observations",
                column: "subject_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_subject_consents_source_id",
                table: "ie_moderation_profile_subject_consents",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_subject_consents_subject_human_id_source_id_generation",
                table: "ie_moderation_profile_subject_consents",
                columns: new[] { "subject_human_id", "source_id", "generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_publication_profiles_publication_id_reason_code_reason_revision",
                table: "ie_moderation_publication_profiles",
                columns: new[] { "publication_id", "reason_code", "reason_revision" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_publication_profiles_publication_id_rule_code_rule_revision",
                table: "ie_moderation_publication_profiles",
                columns: new[] { "publication_id", "rule_code", "rule_revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_publication_reasons_publication_id_class_code_class_revision",
                table: "ie_moderation_publication_reasons",
                columns: new[] { "publication_id", "class_code", "class_revision" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_qualification_policy_adoptions_adopted_by_human_id",
                table: "ie_moderation_qualification_policy_adoptions",
                column: "adopted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_qualification_policy_adoptions_revoked_by_human_id",
                table: "ie_moderation_qualification_policy_adoptions",
                column: "revoked_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_qualification_policy_adoptions_source_id_host_binding_id_trust_domain_id_tenant_id_case_id",
                table: "ie_moderation_qualification_policy_adoptions",
                columns: new[] { "source_id", "host_binding_id", "trust_domain_id", "tenant_id", "case_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_qualification_policy_adoptions_source_id_source_revision",
                table: "ie_moderation_qualification_policy_adoptions",
                columns: new[] { "source_id", "source_revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_qualification_policy_sources_case_id",
                table: "ie_moderation_qualification_policy_sources",
                column: "case_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_qualification_policy_sources_host_binding_id_trust_domain_id_tenant_id_case_id",
                table: "ie_moderation_qualification_policy_sources",
                columns: new[] { "host_binding_id", "trust_domain_id", "tenant_id", "case_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_qualification_policy_sources_published_by_human_id",
                table: "ie_moderation_qualification_policy_sources",
                column: "published_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_qualification_policy_sources_tenant_id",
                table: "ie_moderation_qualification_policy_sources",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_action_recovery_capture_item_id",
                table: "ie_moderation_recognition_action_recovery",
                column: "capture_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_action_recovery_fresh_subject_id_verified_binding_id_capture_item_id",
                table: "ie_moderation_recognition_action_recovery",
                columns: new[] { "fresh_subject_id", "verified_binding_id", "capture_item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_action_recovery_resolved_at_disposition_by",
                table: "ie_moderation_recognition_action_recovery",
                columns: new[] { "resolved_at", "disposition_by" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_capture_item_continuity_binding_id",
                table: "ie_moderation_recognition_capture_item",
                column: "continuity_binding_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_capture_item_intent_id_provider_kind_purpose_digest_sanction_id",
                table: "ie_moderation_recognition_capture_item",
                columns: new[] { "intent_id", "provider_kind", "purpose", "digest", "sanction_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_capture_item_parent_capture_item_id",
                table: "ie_moderation_recognition_capture_item",
                column: "parent_capture_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_capture_item_trust_domain_id_provider_kind_purpose_key_id_digest",
                table: "ie_moderation_recognition_capture_item",
                columns: new[] { "trust_domain_id", "provider_kind", "purpose", "key_id", "digest" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_continuity_binding_capture_item_id",
                table: "ie_moderation_recognition_continuity_binding",
                column: "capture_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_continuity_binding_fresh_subject_id_sanction_id",
                table: "ie_moderation_recognition_continuity_binding",
                columns: new[] { "fresh_subject_id", "sanction_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_continuity_binding_retain_until",
                table: "ie_moderation_recognition_continuity_binding",
                column: "retain_until");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_disposition_intent_id_pending_revision_source_id",
                table: "ie_moderation_recognition_disposition",
                columns: new[] { "intent_id", "pending_revision", "source_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_disposition_retain_until",
                table: "ie_moderation_recognition_disposition",
                column: "retain_until");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_disposition_remedy_receipt_hash",
                table: "ie_moderation_recognition_disposition_remedy",
                column: "receipt_hash");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_disposition_remedy_retain_until",
                table: "ie_moderation_recognition_disposition_remedy",
                column: "retain_until");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_escalation_recipient_human_id_acknowledged_at",
                table: "ie_moderation_recognition_escalation",
                columns: new[] { "recipient_human_id", "acknowledged_at" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_escalation_retain_until",
                table: "ie_moderation_recognition_escalation",
                column: "retain_until");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_escalation_target_id_source_id_recipient_human_id_stage",
                table: "ie_moderation_recognition_escalation",
                columns: new[] { "target_id", "source_id", "recipient_human_id", "stage" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_key_lifecycle_key_trust_domain_id_key_generation",
                table: "ie_moderation_recognition_key_lifecycle_key",
                columns: new[] { "trust_domain_id", "key_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_key_lifecycle_purge_authority_sequence",
                table: "ie_moderation_recognition_key_lifecycle_purge",
                column: "authority_sequence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_pending_receipt_hash",
                table: "ie_moderation_recognition_pending",
                column: "receipt_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_pending_state_escalate_at",
                table: "ie_moderation_recognition_pending",
                columns: new[] { "state", "escalate_at" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_pending_subject_id",
                table: "ie_moderation_recognition_pending",
                column: "subject_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_privacy_adoptions_adopted_by_human_id",
                table: "ie_moderation_recognition_privacy_adoptions",
                column: "adopted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_privacy_adoptions_source_id",
                table: "ie_moderation_recognition_privacy_adoptions",
                column: "source_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_privacy_adoptions_withdrawn_by_human_id",
                table: "ie_moderation_recognition_privacy_adoptions",
                column: "withdrawn_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_privacy_sources_definition_id",
                table: "ie_moderation_recognition_privacy_sources",
                column: "definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_privacy_sources_governance_controller_human_id",
                table: "ie_moderation_recognition_privacy_sources",
                column: "governance_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_privacy_sources_host_binding_id_tenant_id_publication_id_profile_id_mode_code_writer_generation",
                table: "ie_moderation_recognition_privacy_sources",
                columns: new[] { "host_binding_id", "tenant_id", "publication_id", "profile_id", "mode_code", "writer_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_privacy_sources_privacy_controller_human_id",
                table: "ie_moderation_recognition_privacy_sources",
                column: "privacy_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_privacy_sources_privacy_owner_human_id",
                table: "ie_moderation_recognition_privacy_sources",
                column: "privacy_owner_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_privacy_sources_recorded_by_human_id",
                table: "ie_moderation_recognition_privacy_sources",
                column: "recorded_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_privacy_sources_recovery_controller_human_id",
                table: "ie_moderation_recognition_privacy_sources",
                column: "recovery_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_privacy_sources_recovery_owner_human_id",
                table: "ie_moderation_recognition_privacy_sources",
                column: "recovery_owner_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recovery_store_states_installation_slot",
                table: "ie_moderation_recovery_store_states",
                column: "installation_slot",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_resource_generations_host_binding_id_tenant_id_resource_id_revision",
                table: "ie_moderation_resource_generations",
                columns: new[] { "host_binding_id", "tenant_id", "resource_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_resource_generations_observed_by_human_id",
                table: "ie_moderation_resource_generations",
                column: "observed_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_resource_generations_tenant_id",
                table: "ie_moderation_resource_generations",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_resource_target_adoptions_adopted_by_human_id",
                table: "ie_moderation_resource_target_adoptions",
                column: "adopted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_resource_target_adoptions_generation_id",
                table: "ie_moderation_resource_target_adoptions",
                column: "generation_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_resource_target_adoptions_revoked_by_human_id",
                table: "ie_moderation_resource_target_adoptions",
                column: "revoked_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_resource_target_adoptions_selection_id_binding_ordinal",
                table: "ie_moderation_resource_target_adoptions",
                columns: new[] { "selection_id", "binding_ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_schedule_risk_reviews_accepted_finding_id",
                table: "ie_moderation_schedule_risk_reviews",
                column: "accepted_finding_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_schedule_risk_reviews_accepted_reviewer_assignment_id",
                table: "ie_moderation_schedule_risk_reviews",
                column: "accepted_reviewer_assignment_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_schedule_risk_reviews_event_scope_trust_domain_id_event_scope_tenant_id_state_due_at",
                table: "ie_moderation_schedule_risk_reviews",
                columns: new[] { "event_scope_trust_domain_id", "event_scope_tenant_id", "state", "due_at" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_schedule_risk_reviews_source_sanction_id_event_scope_event_id_session_id_participant_id_original_sche_c5b059e54123",
                table: "ie_moderation_schedule_risk_reviews",
                columns: new[] { "source_sanction_id", "event_scope_event_id", "session_id", "participant_id", "original_schedule_generation", "original_participant_generation", "original_publication_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_birth_date_collection_receipts_user_id",
                table: "ie_user_birth_date_collection_receipts",
                column: "user_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_event_report_signals_event_report_cases_tenant_id_report_id_case_id",
                table: "ie_event_report_signals",
                columns: new[] { "tenant_id", "report_id", "case_id" },
                principalTable: "ie_event_report_cases",
                principalColumns: new[] { "tenant_id", "report_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_event_report_signals_event_report_external_links_tenant_id_report_id_provider_link_id",
                table: "ie_event_report_signals",
                columns: new[] { "tenant_id", "report_id", "provider_link_id" },
                principalTable: "ie_event_report_external_links",
                principalColumns: new[] { "tenant_id", "report_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_event_report_signals_incoming_webhook_messages_tenant_id_incoming_webhook_message_id",
                table: "ie_event_report_signals",
                columns: new[] { "tenant_id", "incoming_webhook_message_id" },
                principalTable: "ie_incoming_webhook_messages",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_moderation_cases_moderation_cases_occurrence_case_id",
                table: "ie_moderation_cases",
                column: "occurrence_case_id",
                principalTable: "ie_moderation_cases",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_event_resource_moderation_quarantines_event_resource_quarantine_releases_tenant_id_release_id",
                table: "ie_event_resource_moderation_quarantines",
                columns: new[] { "tenant_id", "release_id" },
                principalTable: "ie_event_resource_quarantine_releases",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_moderation_recognition_action_recovery_moderation_recognition_capture_item_capture_item_id",
                table: "ie_moderation_recognition_action_recovery",
                column: "capture_item_id",
                principalTable: "ie_moderation_recognition_capture_item",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_moderation_recognition_capture_item_moderation_recognition_continuity_binding_continuity_binding_id",
                table: "ie_moderation_recognition_capture_item",
                column: "continuity_binding_id",
                principalTable: "ie_moderation_recognition_continuity_binding",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_event_report_signals_event_report_cases_tenant_id_report_id_case_id",
                table: "ie_event_report_signals");

            migrationBuilder.DropForeignKey(
                name: "fk_event_report_signals_event_report_external_links_tenant_id_report_id_provider_link_id",
                table: "ie_event_report_signals");

            migrationBuilder.DropForeignKey(
                name: "fk_event_report_signals_incoming_webhook_messages_tenant_id_incoming_webhook_message_id",
                table: "ie_event_report_signals");

            migrationBuilder.DropForeignKey(
                name: "fk_moderation_cases_moderation_cases_occurrence_case_id",
                table: "ie_moderation_cases");

            migrationBuilder.DropForeignKey(
                name: "fk_event_resource_moderation_quarantines_event_resource_quarantine_releases_tenant_id_release_id",
                table: "ie_event_resource_moderation_quarantines");

            migrationBuilder.DropForeignKey(
                name: "fk_moderation_recognition_continuity_binding_moderation_recognition_capture_item_capture_item_id",
                table: "ie_moderation_recognition_continuity_binding");

            migrationBuilder.DropTable(
                name: "ie_event_resource_moderation_review_completions");

            migrationBuilder.DropTable(
                name: "ie_event_resource_moderation_review_escalations");

            migrationBuilder.DropTable(
                name: "ie_key_obligation");

            migrationBuilder.DropTable(
                name: "ie_key_replica");

            migrationBuilder.DropTable(
                name: "ie_moderation_case_attribution_renewal_receipts");

            migrationBuilder.DropTable(
                name: "ie_moderation_case_evidence");

            migrationBuilder.DropTable(
                name: "ie_moderation_case_intake_acknowledgements");

            migrationBuilder.DropTable(
                name: "ie_moderation_case_read_grant_revocation_receipts");

            migrationBuilder.DropTable(
                name: "ie_moderation_classification_event_target_adoptions");

            migrationBuilder.DropTable(
                name: "ie_moderation_classification_material_categories");

            migrationBuilder.DropTable(
                name: "ie_moderation_classification_material_components");

            migrationBuilder.DropTable(
                name: "ie_moderation_constitution_reviewers");

            migrationBuilder.DropTable(
                name: "ie_moderation_consumer_measurement_receipts");

            migrationBuilder.DropTable(
                name: "ie_moderation_content_responses");

            migrationBuilder.DropTable(
                name: "ie_moderation_deployment_adoptions");

            migrationBuilder.DropTable(
                name: "ie_moderation_deployment_consumers");

            migrationBuilder.DropTable(
                name: "ie_moderation_event_rights_adoptions");

            migrationBuilder.DropTable(
                name: "ie_moderation_event_target_adoptions");

            migrationBuilder.DropTable(
                name: "ie_moderation_measured_operations_acceptances");

            migrationBuilder.DropTable(
                name: "ie_moderation_notice_disclosure_approvals");

            migrationBuilder.DropTable(
                name: "ie_moderation_notice_notification_receipts");

            migrationBuilder.DropTable(
                name: "ie_moderation_original_effect_completion_receipts");

            migrationBuilder.DropTable(
                name: "ie_moderation_participant_incident_responsibility_sources");

            migrationBuilder.DropTable(
                name: "ie_moderation_policy_publication_candidates");

            migrationBuilder.DropTable(
                name: "ie_moderation_policy_selections");

            migrationBuilder.DropTable(
                name: "ie_moderation_pre_finding_erasure_tombstones");

            migrationBuilder.DropTable(
                name: "ie_moderation_pre_finding_notices");

            migrationBuilder.DropTable(
                name: "ie_moderation_pre_finding_receipts");

            migrationBuilder.DropTable(
                name: "ie_moderation_profile_adoptions");

            migrationBuilder.DropTable(
                name: "ie_moderation_profile_collection_assessment_adoptions");

            migrationBuilder.DropTable(
                name: "ie_moderation_profile_collection_subject_observations");

            migrationBuilder.DropTable(
                name: "ie_moderation_profile_subject_consents");

            migrationBuilder.DropTable(
                name: "ie_moderation_publication_consumers");

            migrationBuilder.DropTable(
                name: "ie_moderation_publication_effects");

            migrationBuilder.DropTable(
                name: "ie_moderation_publication_evidence");

            migrationBuilder.DropTable(
                name: "ie_moderation_publication_modes");

            migrationBuilder.DropTable(
                name: "ie_moderation_publication_plans");

            migrationBuilder.DropTable(
                name: "ie_moderation_publication_prerequisites");

            migrationBuilder.DropTable(
                name: "ie_moderation_publication_scopes");

            migrationBuilder.DropTable(
                name: "ie_moderation_publication_subjects");

            migrationBuilder.DropTable(
                name: "ie_moderation_qualification_policy_adoptions");

            migrationBuilder.DropTable(
                name: "ie_moderation_recognition_action_recovery");

            migrationBuilder.DropTable(
                name: "ie_moderation_recognition_disposition_acceptance");

            migrationBuilder.DropTable(
                name: "ie_moderation_recognition_disposition_remedy");

            migrationBuilder.DropTable(
                name: "ie_moderation_recognition_escalation");

            migrationBuilder.DropTable(
                name: "ie_moderation_recognition_key_lifecycle_key");

            migrationBuilder.DropTable(
                name: "ie_moderation_recognition_key_lifecycle_purge");

            migrationBuilder.DropTable(
                name: "ie_moderation_recognition_privacy_adoptions");

            migrationBuilder.DropTable(
                name: "ie_moderation_recognition_privacy_retention");

            migrationBuilder.DropTable(
                name: "ie_moderation_recovery_store_states");

            migrationBuilder.DropTable(
                name: "ie_moderation_resource_target_adoptions");

            migrationBuilder.DropTable(
                name: "ie_moderation_schedule_risk_reviews");

            migrationBuilder.DropTable(
                name: "ie_user_birth_date_collection_receipts");

            migrationBuilder.DropTable(
                name: "ie_event_resource_moderation_review_duties");

            migrationBuilder.DropTable(
                name: "ie_moderation_case_attribution_heads");

            migrationBuilder.DropTable(
                name: "ie_moderation_operational_budget_adoptions");

            migrationBuilder.DropTable(
                name: "ie_moderation_classification_control_bindings");

            migrationBuilder.DropTable(
                name: "ie_moderation_classification_material_entries");

            migrationBuilder.DropTable(
                name: "ie_moderation_classification_taxonomy_categories");

            migrationBuilder.DropTable(
                name: "ie_moderation_constitution_availability");

            migrationBuilder.DropTable(
                name: "ie_moderation_consumer_measurement_challenges");

            migrationBuilder.DropTable(
                name: "ie_moderation_measurement_issuer_enrollments");

            migrationBuilder.DropTable(
                name: "ie_moderation_deployment_sources");

            migrationBuilder.DropTable(
                name: "ie_moderation_event_rights_sources");

            migrationBuilder.DropTable(
                name: "ie_moderation_measured_operations_sources");

            migrationBuilder.DropTable(
                name: "ie_moderation_notices");

            migrationBuilder.DropTable(
                name: "ie_moderation_case_narratives");

            migrationBuilder.DropTable(
                name: "ie_moderation_case_target_policies");

            migrationBuilder.DropTable(
                name: "ie_moderation_pre_finding_custody");

            migrationBuilder.DropTable(
                name: "ie_moderation_profile_collection_assessment_sources");

            migrationBuilder.DropTable(
                name: "ie_moderation_publication_profiles");

            migrationBuilder.DropTable(
                name: "ie_moderation_qualification_policy_sources");

            migrationBuilder.DropTable(
                name: "ie_moderation_recognition_disposition");

            migrationBuilder.DropTable(
                name: "ie_moderation_recognition_key_lifecycle");

            migrationBuilder.DropTable(
                name: "ie_moderation_recognition_privacy_sources");

            migrationBuilder.DropTable(
                name: "ie_moderation_contribution_bindings");

            migrationBuilder.DropTable(
                name: "ie_moderation_resource_generations");

            migrationBuilder.DropTable(
                name: "ie_moderation_operational_budget_sources");

            migrationBuilder.DropTable(
                name: "ie_moderation_classification_control_selections");

            migrationBuilder.DropTable(
                name: "ie_moderation_consumer_installations");

            migrationBuilder.DropTable(
                name: "ie_moderation_notice_sources");

            migrationBuilder.DropTable(
                name: "ie_moderation_case_narrative_adoptions");

            migrationBuilder.DropTable(
                name: "ie_moderation_case_target_receipts");

            migrationBuilder.DropTable(
                name: "ie_moderation_case_attribution_sources");

            migrationBuilder.DropTable(
                name: "ie_moderation_profile_collection_procedure_adoptions");

            migrationBuilder.DropTable(
                name: "ie_moderation_publication_reasons");

            migrationBuilder.DropTable(
                name: "ie_moderation_contribution_selections");

            migrationBuilder.DropTable(
                name: "ie_moderation_classification_material_reviews");

            migrationBuilder.DropTable(
                name: "ie_moderation_case_narrative_policy_sources");

            migrationBuilder.DropTable(
                name: "ie_moderation_case_read_grants");

            migrationBuilder.DropTable(
                name: "ie_moderation_profile_collection_procedure_sources");

            migrationBuilder.DropTable(
                name: "ie_moderation_publication_classes");

            migrationBuilder.DropTable(
                name: "ie_moderation_classification_material_sources");

            migrationBuilder.DropTable(
                name: "ie_moderation_case_target_sources");

            migrationBuilder.DropTable(
                name: "ie_moderation_classification_taxonomy_adoptions");

            migrationBuilder.DropTable(
                name: "ie_moderation_event_generations");

            migrationBuilder.DropTable(
                name: "ie_moderation_classification_taxonomy_sources");

            migrationBuilder.DropTable(
                name: "ie_moderation_child_age_policy_adoptions");

            migrationBuilder.DropTable(
                name: "ie_moderation_constitution_adoptions");

            migrationBuilder.DropTable(
                name: "ie_moderation_child_age_policy_sources");

            migrationBuilder.DropTable(
                name: "ie_moderation_constitution_sources");

            migrationBuilder.DropTable(
                name: "ie_moderation_policy_publications");

            migrationBuilder.DropTable(
                name: "ie_event_resource_quarantine_releases");

            migrationBuilder.DropTable(
                name: "ie_event_resource_quarantine_reviews");

            migrationBuilder.DropTable(
                name: "ie_event_resource_moderation_quarantines");

            migrationBuilder.DropTable(
                name: "ie_moderation_recognition_capture_item");

            migrationBuilder.DropTable(
                name: "ie_moderation_recognition_continuity_binding");

            migrationBuilder.DropTable(
                name: "ie_moderation_recognition_pending");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_sanctions_scope_scope",
                table: "ie_moderation_sanctions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_sanctions_subject_origin_scope",
                table: "ie_moderation_sanctions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_qualification_receipt_result",
                table: "ie_moderation_qualification_mutation_receipts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_protective_measures_issuing_scope_scope",
                table: "ie_moderation_protective_measures");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_protective_measures_protector_origin_scope",
                table: "ie_moderation_protective_measures");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_protective_measures_reviewer_origin_scope",
                table: "ie_moderation_protective_measures");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_protective_measures_scope_scope",
                table: "ie_moderation_protective_measures");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_protective_measures_subject_origin_scope",
                table: "ie_moderation_protective_measures");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_policies_jurisdiction_scope",
                table: "ie_moderation_policies");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_policy_jurisdiction",
                table: "ie_moderation_policies");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_receipt_scope",
                table: "ie_moderation_operation_receipts");

            migrationBuilder.DropIndex(
                name: "ix_moderation_findings_case_id_rule_owner_id_rule_code_rule_revision_scope_kind_scope_id_attribution_generation",
                table: "ie_moderation_findings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_finding_attribution",
                table: "ie_moderation_findings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_finding_evidence",
                table: "ie_moderation_findings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_findings_adjudicator_origin_scope",
                table: "ie_moderation_findings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_findings_reversed_by_origin_scope",
                table: "ie_moderation_findings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_findings_scope_scope",
                table: "ie_moderation_findings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_findings_subject_origin_scope",
                table: "ie_moderation_findings");

            migrationBuilder.DropIndex(
                name: "ix_moderation_effect_intents_completed_at_lease_expires_at_id",
                table: "ie_moderation_effect_intents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_effect_completed_lease",
                table: "ie_moderation_effect_intents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_effect_completion_window",
                table: "ie_moderation_effect_intents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_effect_lease",
                table: "ie_moderation_effect_intents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_effect_lease_window",
                table: "ie_moderation_effect_intents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_effect_notice_cleanup",
                table: "ie_moderation_effect_intents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_effect_original_completion",
                table: "ie_moderation_effect_intents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_decisions_approval1_origin_scope",
                table: "ie_moderation_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_decisions_approval2_origin_scope",
                table: "ie_moderation_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_decisions_issuer_origin_scope",
                table: "ie_moderation_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_decisions_issuing_scope_scope",
                table: "ie_moderation_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_decisions_scope_scope",
                table: "ie_moderation_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_decisions_subject_origin_scope",
                table: "ie_moderation_decisions");

            migrationBuilder.DropIndex(
                name: "ix_moderation_cases_occurrence_case_id",
                table: "ie_moderation_cases");

            migrationBuilder.DropIndex(
                name: "ix_moderation_cases_scope_trust_domain_id_scope_tenant_id_occurrence_id_occurrence_owner_kind_occurrence_owner_id",
                table: "ie_moderation_cases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_case_attribution",
                table: "ie_moderation_cases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_case_occurrence_owner",
                table: "ie_moderation_cases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_case_ownership",
                table: "ie_moderation_cases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_cases_scope_scope",
                table: "ie_moderation_cases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_cases_subject_origin_scope",
                table: "ie_moderation_cases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_cases_subject_subject",
                table: "ie_moderation_cases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_cases_target_origin_scope",
                table: "ie_moderation_cases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_event_resources_material",
                table: "ie_event_resources");

            migrationBuilder.DropCheckConstraint(
                name: "ck_event_resources_quarantine",
                table: "ie_event_resources");

            migrationBuilder.DropIndex(
                name: "ix_event_report_signals_tenant_id_incoming_webhook_message_id",
                table: "ie_event_report_signals");

            migrationBuilder.DropIndex(
                name: "ix_event_report_signals_tenant_id_provider_advisory_identity_advisory_ordinal",
                table: "ie_event_report_signals");

            migrationBuilder.DropIndex(
                name: "ix_event_report_signals_tenant_id_provider_provider_target_scope_provider_target_id_correlation_id",
                table: "ie_event_report_signals");

            migrationBuilder.DropIndex(
                name: "ix_event_report_signals_tenant_id_provider_provider_target_scope_provider_target_id_external_signal_id",
                table: "ie_event_report_signals");

            migrationBuilder.DropIndex(
                name: "ix_event_report_signals_tenant_id_report_id_case_id",
                table: "ie_event_report_signals");

            migrationBuilder.DropIndex(
                name: "ix_event_report_signals_tenant_id_report_id_provider_link_id",
                table: "ie_event_report_signals");

            migrationBuilder.DropCheckConstraint(
                name: "ck_event_report_signals_advisory_source",
                table: "ie_event_report_signals");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_event_report_external_links_tenant_id_report_id_id",
                table: "ie_event_report_external_links");

            migrationBuilder.DropCheckConstraint(
                name: "ck_event_report_external_links_case_generation",
                table: "ie_event_report_external_links");

            migrationBuilder.DropColumn(
                name: "birth_date_collection_receipt_id",
                table: "ie_user_pii");

            migrationBuilder.DropColumn(
                name: "birth_date_revision",
                table: "ie_user_pii");

            migrationBuilder.DropColumn(
                name: "date_of_birth",
                table: "ie_user_pii");

            migrationBuilder.DropColumn(
                name: "scope_event_id",
                table: "ie_moderation_sanctions");

            migrationBuilder.DropColumn(
                name: "subject_origin_event_id",
                table: "ie_moderation_sanctions");

            migrationBuilder.DropColumn(
                name: "issuing_scope_event_id",
                table: "ie_moderation_protective_measures");

            migrationBuilder.DropColumn(
                name: "protector_origin_event_id",
                table: "ie_moderation_protective_measures");

            migrationBuilder.DropColumn(
                name: "reviewer_origin_event_id",
                table: "ie_moderation_protective_measures");

            migrationBuilder.DropColumn(
                name: "scope_event_id",
                table: "ie_moderation_protective_measures");

            migrationBuilder.DropColumn(
                name: "subject_origin_event_id",
                table: "ie_moderation_protective_measures");

            migrationBuilder.DropColumn(
                name: "jurisdiction_event_id",
                table: "ie_moderation_policies");

            migrationBuilder.DropColumn(
                name: "adjudicator_origin_event_id",
                table: "ie_moderation_findings");

            migrationBuilder.DropColumn(
                name: "attribution_generation",
                table: "ie_moderation_findings");

            migrationBuilder.DropColumn(
                name: "attribution_source_id",
                table: "ie_moderation_findings");

            migrationBuilder.DropColumn(
                name: "reason_code",
                table: "ie_moderation_findings");

            migrationBuilder.DropColumn(
                name: "reason_owner_id",
                table: "ie_moderation_findings");

            migrationBuilder.DropColumn(
                name: "reason_revision",
                table: "ie_moderation_findings");

            migrationBuilder.DropColumn(
                name: "reversed_by_origin_event_id",
                table: "ie_moderation_findings");

            migrationBuilder.DropColumn(
                name: "scope_event_id",
                table: "ie_moderation_findings");

            migrationBuilder.DropColumn(
                name: "scope_id",
                table: "ie_moderation_findings");

            migrationBuilder.DropColumn(
                name: "subject_origin_event_id",
                table: "ie_moderation_findings");

            migrationBuilder.DropColumn(
                name: "lease_expires_at",
                table: "ie_moderation_effect_intents");

            migrationBuilder.DropColumn(
                name: "lease_id",
                table: "ie_moderation_effect_intents");

            migrationBuilder.DropColumn(
                name: "lease_started_at",
                table: "ie_moderation_effect_intents");

            migrationBuilder.DropColumn(
                name: "original_completion_host_binding_id",
                table: "ie_moderation_effect_intents");

            migrationBuilder.DropColumn(
                name: "original_completion_id",
                table: "ie_moderation_effect_intents");

            migrationBuilder.DropColumn(
                name: "original_completion_revision",
                table: "ie_moderation_effect_intents");

            migrationBuilder.DropColumn(
                name: "original_completion_writer_tenant_id",
                table: "ie_moderation_effect_intents");

            migrationBuilder.DropColumn(
                name: "original_notice_cleanup_id",
                table: "ie_moderation_effect_intents");

            migrationBuilder.DropColumn(
                name: "original_notice_host_binding_id",
                table: "ie_moderation_effect_intents");

            migrationBuilder.DropColumn(
                name: "original_notice_purged_at",
                table: "ie_moderation_effect_intents");

            migrationBuilder.DropColumn(
                name: "original_notice_retain_until",
                table: "ie_moderation_effect_intents");

            migrationBuilder.DropColumn(
                name: "original_notice_writer_tenant_id",
                table: "ie_moderation_effect_intents");

            migrationBuilder.DropColumn(
                name: "approval1_origin_event_id",
                table: "ie_moderation_decisions");

            migrationBuilder.DropColumn(
                name: "approval2_origin_event_id",
                table: "ie_moderation_decisions");

            migrationBuilder.DropColumn(
                name: "issuer_origin_event_id",
                table: "ie_moderation_decisions");

            migrationBuilder.DropColumn(
                name: "issuing_scope_event_id",
                table: "ie_moderation_decisions");

            migrationBuilder.DropColumn(
                name: "scope_event_id",
                table: "ie_moderation_decisions");

            migrationBuilder.DropColumn(
                name: "subject_origin_event_id",
                table: "ie_moderation_decisions");

            migrationBuilder.DropColumn(
                name: "attribution_source_id",
                table: "ie_moderation_cases");

            migrationBuilder.DropColumn(
                name: "occurrence_case_id",
                table: "ie_moderation_cases");

            migrationBuilder.DropColumn(
                name: "occurrence_evidence_revision",
                table: "ie_moderation_cases");

            migrationBuilder.DropColumn(
                name: "occurrence_owner_id",
                table: "ie_moderation_cases");

            migrationBuilder.DropColumn(
                name: "occurrence_owner_kind",
                table: "ie_moderation_cases");

            migrationBuilder.DropColumn(
                name: "scope_event_id",
                table: "ie_moderation_cases");

            migrationBuilder.DropColumn(
                name: "subject_origin_event_id",
                table: "ie_moderation_cases");

            migrationBuilder.DropColumn(
                name: "target_id",
                table: "ie_moderation_cases");

            migrationBuilder.DropColumn(
                name: "target_kind",
                table: "ie_moderation_cases");

            migrationBuilder.DropColumn(
                name: "target_origin_community_id",
                table: "ie_moderation_cases");

            migrationBuilder.DropColumn(
                name: "target_origin_event_id",
                table: "ie_moderation_cases");

            migrationBuilder.DropColumn(
                name: "target_origin_instance_id",
                table: "ie_moderation_cases");

            migrationBuilder.DropColumn(
                name: "target_origin_kind",
                table: "ie_moderation_cases");

            migrationBuilder.DropColumn(
                name: "target_origin_resource_id",
                table: "ie_moderation_cases");

            migrationBuilder.DropColumn(
                name: "target_origin_solution_id",
                table: "ie_moderation_cases");

            migrationBuilder.DropColumn(
                name: "target_origin_tenant_id",
                table: "ie_moderation_cases");

            migrationBuilder.DropColumn(
                name: "target_origin_trust_domain_id",
                table: "ie_moderation_cases");

            migrationBuilder.DropColumn(
                name: "target_version",
                table: "ie_moderation_cases");

            migrationBuilder.DropColumn(
                name: "material_version_id",
                table: "ie_event_resources");

            migrationBuilder.DropColumn(
                name: "moderation_quarantine_count",
                table: "ie_event_resources");

            migrationBuilder.DropColumn(
                name: "advisory_identity",
                table: "ie_event_report_signals");

            migrationBuilder.DropColumn(
                name: "advisory_material_sha256",
                table: "ie_event_report_signals");

            migrationBuilder.DropColumn(
                name: "advisory_ordinal",
                table: "ie_event_report_signals");

            migrationBuilder.DropColumn(
                name: "case_id",
                table: "ie_event_report_signals");

            migrationBuilder.DropColumn(
                name: "incoming_webhook_message_id",
                table: "ie_event_report_signals");

            migrationBuilder.DropColumn(
                name: "provider_link_id",
                table: "ie_event_report_signals");

            migrationBuilder.DropColumn(
                name: "source_case_concurrency_stamp",
                table: "ie_event_report_signals");

            migrationBuilder.DropColumn(
                name: "case_concurrency_stamp",
                table: "ie_event_report_external_links");

            migrationBuilder.DropColumn(
                name: "moderation_reservation",
                table: "ie_configuration_import_sessions");

            migrationBuilder.AlterColumn<Guid>(
                name: "subject_origin_trust_domain_id",
                table: "ie_moderation_cases",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "subject_origin_kind",
                table: "ie_moderation_cases",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "subject_kind",
                table: "ie_moderation_cases",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "subject_id",
                table: "ie_moderation_cases",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_sanctions_scope_scope",
                table: "ie_moderation_sanctions",
                sql: "scope_kind IS NOT NULL AND ((scope_kind = 1 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 2 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 3 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 4 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 5 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NOT NULL AND scope_resource_id IS NULL) OR (scope_kind = 6 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_resource_id IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_sanctions_subject_origin_scope",
                table: "ie_moderation_sanctions",
                sql: "subject_origin_kind IS NOT NULL AND ((subject_origin_kind = 1 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 2 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 3 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 4 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 5 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NOT NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 6 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_resource_id IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_qualification_receipt_result",
                table: "ie_moderation_qualification_mutation_receipts",
                sql: "source_kind BETWEEN 1 AND 5 AND result_revision > 0 AND qualification_revision > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_protective_measures_issuing_scope_scope",
                table: "ie_moderation_protective_measures",
                sql: "issuing_scope_kind IS NOT NULL AND ((issuing_scope_kind = 1 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NULL AND issuing_scope_instance_id IS NULL AND issuing_scope_tenant_id IS NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL) OR (issuing_scope_kind = 2 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NULL AND issuing_scope_tenant_id IS NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL) OR (issuing_scope_kind = 3 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL) OR (issuing_scope_kind = 4 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NOT NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL) OR (issuing_scope_kind = 5 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NOT NULL AND issuing_scope_community_id IS NOT NULL AND issuing_scope_resource_id IS NULL) OR (issuing_scope_kind = 6 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NOT NULL AND issuing_scope_resource_id IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_protective_measures_protector_origin_scope",
                table: "ie_moderation_protective_measures",
                sql: "protector_origin_kind IS NOT NULL AND ((protector_origin_kind = 1 AND protector_origin_trust_domain_id IS NOT NULL AND protector_origin_solution_id IS NULL AND protector_origin_instance_id IS NULL AND protector_origin_tenant_id IS NULL AND protector_origin_community_id IS NULL AND protector_origin_resource_id IS NULL) OR (protector_origin_kind = 2 AND protector_origin_trust_domain_id IS NOT NULL AND protector_origin_solution_id IS NOT NULL AND protector_origin_instance_id IS NULL AND protector_origin_tenant_id IS NULL AND protector_origin_community_id IS NULL AND protector_origin_resource_id IS NULL) OR (protector_origin_kind = 3 AND protector_origin_trust_domain_id IS NOT NULL AND protector_origin_solution_id IS NOT NULL AND protector_origin_instance_id IS NOT NULL AND protector_origin_tenant_id IS NULL AND protector_origin_community_id IS NULL AND protector_origin_resource_id IS NULL) OR (protector_origin_kind = 4 AND protector_origin_trust_domain_id IS NOT NULL AND protector_origin_solution_id IS NOT NULL AND protector_origin_instance_id IS NOT NULL AND protector_origin_tenant_id IS NOT NULL AND protector_origin_community_id IS NULL AND protector_origin_resource_id IS NULL) OR (protector_origin_kind = 5 AND protector_origin_trust_domain_id IS NOT NULL AND protector_origin_solution_id IS NOT NULL AND protector_origin_instance_id IS NOT NULL AND protector_origin_tenant_id IS NOT NULL AND protector_origin_community_id IS NOT NULL AND protector_origin_resource_id IS NULL) OR (protector_origin_kind = 6 AND protector_origin_trust_domain_id IS NOT NULL AND protector_origin_solution_id IS NOT NULL AND protector_origin_instance_id IS NOT NULL AND protector_origin_tenant_id IS NOT NULL AND protector_origin_resource_id IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_protective_measures_reviewer_origin_scope",
                table: "ie_moderation_protective_measures",
                sql: "(reviewer_origin_kind IS NULL AND reviewer_origin_trust_domain_id IS NULL AND reviewer_origin_solution_id IS NULL AND reviewer_origin_instance_id IS NULL AND reviewer_origin_tenant_id IS NULL AND reviewer_origin_community_id IS NULL AND reviewer_origin_resource_id IS NULL) OR (reviewer_origin_kind IS NOT NULL AND ((reviewer_origin_kind = 1 AND reviewer_origin_trust_domain_id IS NOT NULL AND reviewer_origin_solution_id IS NULL AND reviewer_origin_instance_id IS NULL AND reviewer_origin_tenant_id IS NULL AND reviewer_origin_community_id IS NULL AND reviewer_origin_resource_id IS NULL) OR (reviewer_origin_kind = 2 AND reviewer_origin_trust_domain_id IS NOT NULL AND reviewer_origin_solution_id IS NOT NULL AND reviewer_origin_instance_id IS NULL AND reviewer_origin_tenant_id IS NULL AND reviewer_origin_community_id IS NULL AND reviewer_origin_resource_id IS NULL) OR (reviewer_origin_kind = 3 AND reviewer_origin_trust_domain_id IS NOT NULL AND reviewer_origin_solution_id IS NOT NULL AND reviewer_origin_instance_id IS NOT NULL AND reviewer_origin_tenant_id IS NULL AND reviewer_origin_community_id IS NULL AND reviewer_origin_resource_id IS NULL) OR (reviewer_origin_kind = 4 AND reviewer_origin_trust_domain_id IS NOT NULL AND reviewer_origin_solution_id IS NOT NULL AND reviewer_origin_instance_id IS NOT NULL AND reviewer_origin_tenant_id IS NOT NULL AND reviewer_origin_community_id IS NULL AND reviewer_origin_resource_id IS NULL) OR (reviewer_origin_kind = 5 AND reviewer_origin_trust_domain_id IS NOT NULL AND reviewer_origin_solution_id IS NOT NULL AND reviewer_origin_instance_id IS NOT NULL AND reviewer_origin_tenant_id IS NOT NULL AND reviewer_origin_community_id IS NOT NULL AND reviewer_origin_resource_id IS NULL) OR (reviewer_origin_kind = 6 AND reviewer_origin_trust_domain_id IS NOT NULL AND reviewer_origin_solution_id IS NOT NULL AND reviewer_origin_instance_id IS NOT NULL AND reviewer_origin_tenant_id IS NOT NULL AND reviewer_origin_resource_id IS NOT NULL)))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_protective_measures_scope_scope",
                table: "ie_moderation_protective_measures",
                sql: "scope_kind IS NOT NULL AND ((scope_kind = 1 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 2 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 3 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 4 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 5 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NOT NULL AND scope_resource_id IS NULL) OR (scope_kind = 6 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_resource_id IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_protective_measures_subject_origin_scope",
                table: "ie_moderation_protective_measures",
                sql: "subject_origin_kind IS NOT NULL AND ((subject_origin_kind = 1 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 2 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 3 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 4 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 5 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NOT NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 6 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_resource_id IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_policies_jurisdiction_scope",
                table: "ie_moderation_policies",
                sql: "jurisdiction_kind IS NOT NULL AND ((jurisdiction_kind = 1 AND jurisdiction_trust_domain_id IS NOT NULL AND jurisdiction_solution_id IS NULL AND jurisdiction_instance_id IS NULL AND jurisdiction_tenant_id IS NULL AND jurisdiction_community_id IS NULL AND jurisdiction_resource_id IS NULL) OR (jurisdiction_kind = 2 AND jurisdiction_trust_domain_id IS NOT NULL AND jurisdiction_solution_id IS NOT NULL AND jurisdiction_instance_id IS NULL AND jurisdiction_tenant_id IS NULL AND jurisdiction_community_id IS NULL AND jurisdiction_resource_id IS NULL) OR (jurisdiction_kind = 3 AND jurisdiction_trust_domain_id IS NOT NULL AND jurisdiction_solution_id IS NOT NULL AND jurisdiction_instance_id IS NOT NULL AND jurisdiction_tenant_id IS NULL AND jurisdiction_community_id IS NULL AND jurisdiction_resource_id IS NULL) OR (jurisdiction_kind = 4 AND jurisdiction_trust_domain_id IS NOT NULL AND jurisdiction_solution_id IS NOT NULL AND jurisdiction_instance_id IS NOT NULL AND jurisdiction_tenant_id IS NOT NULL AND jurisdiction_community_id IS NULL AND jurisdiction_resource_id IS NULL) OR (jurisdiction_kind = 5 AND jurisdiction_trust_domain_id IS NOT NULL AND jurisdiction_solution_id IS NOT NULL AND jurisdiction_instance_id IS NOT NULL AND jurisdiction_tenant_id IS NOT NULL AND jurisdiction_community_id IS NOT NULL AND jurisdiction_resource_id IS NULL) OR (jurisdiction_kind = 6 AND jurisdiction_trust_domain_id IS NOT NULL AND jurisdiction_solution_id IS NOT NULL AND jurisdiction_instance_id IS NOT NULL AND jurisdiction_tenant_id IS NOT NULL AND jurisdiction_resource_id IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_policy_jurisdiction",
                table: "ie_moderation_policies",
                sql: "jurisdiction_id = CASE jurisdiction_kind WHEN 1 THEN jurisdiction_trust_domain_id WHEN 2 THEN jurisdiction_solution_id WHEN 3 THEN jurisdiction_instance_id WHEN 4 THEN jurisdiction_tenant_id WHEN 5 THEN jurisdiction_community_id WHEN 6 THEN jurisdiction_resource_id END");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_receipt_scope",
                table: "ie_moderation_operation_receipts",
                sql: "scope_kind BETWEEN 1 AND 6 AND subject_kind BETWEEN 1 AND 4");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_findings_case_id_rule_owner_id_rule_code_rule_revision",
                table: "ie_moderation_findings",
                columns: new[] { "case_id", "rule_owner_id", "rule_code", "rule_revision" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_finding_evidence",
                table: "ie_moderation_findings",
                sql: "case_revision = 1 AND rule_revision > 0 AND evidence_recorded_at >= case_created_at AND evidence_recorded_at <= finalized_at AND finalized_at >= case_created_at AND ((evidence_purpose = 1 AND urgency_qualification_id IS NULL) OR (evidence_purpose = 2 AND urgency_qualification_id IS NOT NULL AND urgency_qualification_id <> evidence_reference_id))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_findings_adjudicator_origin_scope",
                table: "ie_moderation_findings",
                sql: "adjudicator_origin_kind IS NOT NULL AND ((adjudicator_origin_kind = 1 AND adjudicator_origin_trust_domain_id IS NOT NULL AND adjudicator_origin_solution_id IS NULL AND adjudicator_origin_instance_id IS NULL AND adjudicator_origin_tenant_id IS NULL AND adjudicator_origin_community_id IS NULL AND adjudicator_origin_resource_id IS NULL) OR (adjudicator_origin_kind = 2 AND adjudicator_origin_trust_domain_id IS NOT NULL AND adjudicator_origin_solution_id IS NOT NULL AND adjudicator_origin_instance_id IS NULL AND adjudicator_origin_tenant_id IS NULL AND adjudicator_origin_community_id IS NULL AND adjudicator_origin_resource_id IS NULL) OR (adjudicator_origin_kind = 3 AND adjudicator_origin_trust_domain_id IS NOT NULL AND adjudicator_origin_solution_id IS NOT NULL AND adjudicator_origin_instance_id IS NOT NULL AND adjudicator_origin_tenant_id IS NULL AND adjudicator_origin_community_id IS NULL AND adjudicator_origin_resource_id IS NULL) OR (adjudicator_origin_kind = 4 AND adjudicator_origin_trust_domain_id IS NOT NULL AND adjudicator_origin_solution_id IS NOT NULL AND adjudicator_origin_instance_id IS NOT NULL AND adjudicator_origin_tenant_id IS NOT NULL AND adjudicator_origin_community_id IS NULL AND adjudicator_origin_resource_id IS NULL) OR (adjudicator_origin_kind = 5 AND adjudicator_origin_trust_domain_id IS NOT NULL AND adjudicator_origin_solution_id IS NOT NULL AND adjudicator_origin_instance_id IS NOT NULL AND adjudicator_origin_tenant_id IS NOT NULL AND adjudicator_origin_community_id IS NOT NULL AND adjudicator_origin_resource_id IS NULL) OR (adjudicator_origin_kind = 6 AND adjudicator_origin_trust_domain_id IS NOT NULL AND adjudicator_origin_solution_id IS NOT NULL AND adjudicator_origin_instance_id IS NOT NULL AND adjudicator_origin_tenant_id IS NOT NULL AND adjudicator_origin_resource_id IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_findings_reversed_by_origin_scope",
                table: "ie_moderation_findings",
                sql: "(reversed_by_origin_kind IS NULL AND reversed_by_origin_trust_domain_id IS NULL AND reversed_by_origin_solution_id IS NULL AND reversed_by_origin_instance_id IS NULL AND reversed_by_origin_tenant_id IS NULL AND reversed_by_origin_community_id IS NULL AND reversed_by_origin_resource_id IS NULL) OR (reversed_by_origin_kind IS NOT NULL AND ((reversed_by_origin_kind = 1 AND reversed_by_origin_trust_domain_id IS NOT NULL AND reversed_by_origin_solution_id IS NULL AND reversed_by_origin_instance_id IS NULL AND reversed_by_origin_tenant_id IS NULL AND reversed_by_origin_community_id IS NULL AND reversed_by_origin_resource_id IS NULL) OR (reversed_by_origin_kind = 2 AND reversed_by_origin_trust_domain_id IS NOT NULL AND reversed_by_origin_solution_id IS NOT NULL AND reversed_by_origin_instance_id IS NULL AND reversed_by_origin_tenant_id IS NULL AND reversed_by_origin_community_id IS NULL AND reversed_by_origin_resource_id IS NULL) OR (reversed_by_origin_kind = 3 AND reversed_by_origin_trust_domain_id IS NOT NULL AND reversed_by_origin_solution_id IS NOT NULL AND reversed_by_origin_instance_id IS NOT NULL AND reversed_by_origin_tenant_id IS NULL AND reversed_by_origin_community_id IS NULL AND reversed_by_origin_resource_id IS NULL) OR (reversed_by_origin_kind = 4 AND reversed_by_origin_trust_domain_id IS NOT NULL AND reversed_by_origin_solution_id IS NOT NULL AND reversed_by_origin_instance_id IS NOT NULL AND reversed_by_origin_tenant_id IS NOT NULL AND reversed_by_origin_community_id IS NULL AND reversed_by_origin_resource_id IS NULL) OR (reversed_by_origin_kind = 5 AND reversed_by_origin_trust_domain_id IS NOT NULL AND reversed_by_origin_solution_id IS NOT NULL AND reversed_by_origin_instance_id IS NOT NULL AND reversed_by_origin_tenant_id IS NOT NULL AND reversed_by_origin_community_id IS NOT NULL AND reversed_by_origin_resource_id IS NULL) OR (reversed_by_origin_kind = 6 AND reversed_by_origin_trust_domain_id IS NOT NULL AND reversed_by_origin_solution_id IS NOT NULL AND reversed_by_origin_instance_id IS NOT NULL AND reversed_by_origin_tenant_id IS NOT NULL AND reversed_by_origin_resource_id IS NOT NULL)))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_findings_scope_scope",
                table: "ie_moderation_findings",
                sql: "scope_kind IS NOT NULL AND ((scope_kind = 1 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 2 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 3 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 4 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 5 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NOT NULL AND scope_resource_id IS NULL) OR (scope_kind = 6 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_resource_id IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_findings_subject_origin_scope",
                table: "ie_moderation_findings",
                sql: "subject_origin_kind IS NOT NULL AND ((subject_origin_kind = 1 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 2 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 3 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 4 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 5 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NOT NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 6 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_resource_id IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_decisions_approval1_origin_scope",
                table: "ie_moderation_decisions",
                sql: "(approval1_origin_kind IS NULL AND approval1_origin_trust_domain_id IS NULL AND approval1_origin_solution_id IS NULL AND approval1_origin_instance_id IS NULL AND approval1_origin_tenant_id IS NULL AND approval1_origin_community_id IS NULL AND approval1_origin_resource_id IS NULL) OR (approval1_origin_kind IS NOT NULL AND ((approval1_origin_kind = 1 AND approval1_origin_trust_domain_id IS NOT NULL AND approval1_origin_solution_id IS NULL AND approval1_origin_instance_id IS NULL AND approval1_origin_tenant_id IS NULL AND approval1_origin_community_id IS NULL AND approval1_origin_resource_id IS NULL) OR (approval1_origin_kind = 2 AND approval1_origin_trust_domain_id IS NOT NULL AND approval1_origin_solution_id IS NOT NULL AND approval1_origin_instance_id IS NULL AND approval1_origin_tenant_id IS NULL AND approval1_origin_community_id IS NULL AND approval1_origin_resource_id IS NULL) OR (approval1_origin_kind = 3 AND approval1_origin_trust_domain_id IS NOT NULL AND approval1_origin_solution_id IS NOT NULL AND approval1_origin_instance_id IS NOT NULL AND approval1_origin_tenant_id IS NULL AND approval1_origin_community_id IS NULL AND approval1_origin_resource_id IS NULL) OR (approval1_origin_kind = 4 AND approval1_origin_trust_domain_id IS NOT NULL AND approval1_origin_solution_id IS NOT NULL AND approval1_origin_instance_id IS NOT NULL AND approval1_origin_tenant_id IS NOT NULL AND approval1_origin_community_id IS NULL AND approval1_origin_resource_id IS NULL) OR (approval1_origin_kind = 5 AND approval1_origin_trust_domain_id IS NOT NULL AND approval1_origin_solution_id IS NOT NULL AND approval1_origin_instance_id IS NOT NULL AND approval1_origin_tenant_id IS NOT NULL AND approval1_origin_community_id IS NOT NULL AND approval1_origin_resource_id IS NULL) OR (approval1_origin_kind = 6 AND approval1_origin_trust_domain_id IS NOT NULL AND approval1_origin_solution_id IS NOT NULL AND approval1_origin_instance_id IS NOT NULL AND approval1_origin_tenant_id IS NOT NULL AND approval1_origin_resource_id IS NOT NULL)))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_decisions_approval2_origin_scope",
                table: "ie_moderation_decisions",
                sql: "(approval2_origin_kind IS NULL AND approval2_origin_trust_domain_id IS NULL AND approval2_origin_solution_id IS NULL AND approval2_origin_instance_id IS NULL AND approval2_origin_tenant_id IS NULL AND approval2_origin_community_id IS NULL AND approval2_origin_resource_id IS NULL) OR (approval2_origin_kind IS NOT NULL AND ((approval2_origin_kind = 1 AND approval2_origin_trust_domain_id IS NOT NULL AND approval2_origin_solution_id IS NULL AND approval2_origin_instance_id IS NULL AND approval2_origin_tenant_id IS NULL AND approval2_origin_community_id IS NULL AND approval2_origin_resource_id IS NULL) OR (approval2_origin_kind = 2 AND approval2_origin_trust_domain_id IS NOT NULL AND approval2_origin_solution_id IS NOT NULL AND approval2_origin_instance_id IS NULL AND approval2_origin_tenant_id IS NULL AND approval2_origin_community_id IS NULL AND approval2_origin_resource_id IS NULL) OR (approval2_origin_kind = 3 AND approval2_origin_trust_domain_id IS NOT NULL AND approval2_origin_solution_id IS NOT NULL AND approval2_origin_instance_id IS NOT NULL AND approval2_origin_tenant_id IS NULL AND approval2_origin_community_id IS NULL AND approval2_origin_resource_id IS NULL) OR (approval2_origin_kind = 4 AND approval2_origin_trust_domain_id IS NOT NULL AND approval2_origin_solution_id IS NOT NULL AND approval2_origin_instance_id IS NOT NULL AND approval2_origin_tenant_id IS NOT NULL AND approval2_origin_community_id IS NULL AND approval2_origin_resource_id IS NULL) OR (approval2_origin_kind = 5 AND approval2_origin_trust_domain_id IS NOT NULL AND approval2_origin_solution_id IS NOT NULL AND approval2_origin_instance_id IS NOT NULL AND approval2_origin_tenant_id IS NOT NULL AND approval2_origin_community_id IS NOT NULL AND approval2_origin_resource_id IS NULL) OR (approval2_origin_kind = 6 AND approval2_origin_trust_domain_id IS NOT NULL AND approval2_origin_solution_id IS NOT NULL AND approval2_origin_instance_id IS NOT NULL AND approval2_origin_tenant_id IS NOT NULL AND approval2_origin_resource_id IS NOT NULL)))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_decisions_issuer_origin_scope",
                table: "ie_moderation_decisions",
                sql: "issuer_origin_kind IS NOT NULL AND ((issuer_origin_kind = 1 AND issuer_origin_trust_domain_id IS NOT NULL AND issuer_origin_solution_id IS NULL AND issuer_origin_instance_id IS NULL AND issuer_origin_tenant_id IS NULL AND issuer_origin_community_id IS NULL AND issuer_origin_resource_id IS NULL) OR (issuer_origin_kind = 2 AND issuer_origin_trust_domain_id IS NOT NULL AND issuer_origin_solution_id IS NOT NULL AND issuer_origin_instance_id IS NULL AND issuer_origin_tenant_id IS NULL AND issuer_origin_community_id IS NULL AND issuer_origin_resource_id IS NULL) OR (issuer_origin_kind = 3 AND issuer_origin_trust_domain_id IS NOT NULL AND issuer_origin_solution_id IS NOT NULL AND issuer_origin_instance_id IS NOT NULL AND issuer_origin_tenant_id IS NULL AND issuer_origin_community_id IS NULL AND issuer_origin_resource_id IS NULL) OR (issuer_origin_kind = 4 AND issuer_origin_trust_domain_id IS NOT NULL AND issuer_origin_solution_id IS NOT NULL AND issuer_origin_instance_id IS NOT NULL AND issuer_origin_tenant_id IS NOT NULL AND issuer_origin_community_id IS NULL AND issuer_origin_resource_id IS NULL) OR (issuer_origin_kind = 5 AND issuer_origin_trust_domain_id IS NOT NULL AND issuer_origin_solution_id IS NOT NULL AND issuer_origin_instance_id IS NOT NULL AND issuer_origin_tenant_id IS NOT NULL AND issuer_origin_community_id IS NOT NULL AND issuer_origin_resource_id IS NULL) OR (issuer_origin_kind = 6 AND issuer_origin_trust_domain_id IS NOT NULL AND issuer_origin_solution_id IS NOT NULL AND issuer_origin_instance_id IS NOT NULL AND issuer_origin_tenant_id IS NOT NULL AND issuer_origin_resource_id IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_decisions_issuing_scope_scope",
                table: "ie_moderation_decisions",
                sql: "issuing_scope_kind IS NOT NULL AND ((issuing_scope_kind = 1 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NULL AND issuing_scope_instance_id IS NULL AND issuing_scope_tenant_id IS NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL) OR (issuing_scope_kind = 2 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NULL AND issuing_scope_tenant_id IS NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL) OR (issuing_scope_kind = 3 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL) OR (issuing_scope_kind = 4 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NOT NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL) OR (issuing_scope_kind = 5 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NOT NULL AND issuing_scope_community_id IS NOT NULL AND issuing_scope_resource_id IS NULL) OR (issuing_scope_kind = 6 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NOT NULL AND issuing_scope_resource_id IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_decisions_scope_scope",
                table: "ie_moderation_decisions",
                sql: "scope_kind IS NOT NULL AND ((scope_kind = 1 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 2 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 3 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 4 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 5 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NOT NULL AND scope_resource_id IS NULL) OR (scope_kind = 6 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_resource_id IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_decisions_subject_origin_scope",
                table: "ie_moderation_decisions",
                sql: "subject_origin_kind IS NOT NULL AND ((subject_origin_kind = 1 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 2 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 3 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 4 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 5 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NOT NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 6 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_resource_id IS NOT NULL))");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_cases_scope_trust_domain_id_scope_tenant_id_occurrence_id",
                table: "ie_moderation_cases",
                columns: new[] { "scope_trust_domain_id", "scope_tenant_id", "occurrence_id" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_cases_scope_scope",
                table: "ie_moderation_cases",
                sql: "scope_kind IS NOT NULL AND ((scope_kind = 1 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 2 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 3 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 4 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 5 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NOT NULL AND scope_resource_id IS NULL) OR (scope_kind = 6 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_resource_id IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_cases_subject_origin_scope",
                table: "ie_moderation_cases",
                sql: "subject_origin_kind IS NOT NULL AND ((subject_origin_kind = 1 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 2 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 3 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 4 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 5 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NOT NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 6 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_resource_id IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_cases_subject_subject",
                table: "ie_moderation_cases",
                sql: "subject_kind BETWEEN 1 AND 4");

            migrationBuilder.CreateIndex(
                name: "ix_event_report_signals_tenant_id_provider_provider_target_scope_provider_target_id_correlation_id",
                table: "ie_event_report_signals",
                columns: new[] { "tenant_id", "provider", "provider_target_scope", "provider_target_id", "correlation_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_event_report_signals_tenant_id_provider_provider_target_scope_provider_target_id_external_signal_id",
                table: "ie_event_report_signals",
                columns: new[] { "tenant_id", "provider", "provider_target_scope", "provider_target_id", "external_signal_id" },
                unique: true,
                filter: "external_signal_id IS NOT NULL");
        }
    }
}

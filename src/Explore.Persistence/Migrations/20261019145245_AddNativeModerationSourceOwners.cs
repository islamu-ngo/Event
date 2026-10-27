using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNativeModerationSourceOwners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_sanctions_scope_scope",
                schema: "islamu_event",
                table: "moderation_sanctions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_sanctions_subject_origin_scope",
                schema: "islamu_event",
                table: "moderation_sanctions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_qualification_receipt_result",
                schema: "islamu_event",
                table: "moderation_qualification_mutation_receipts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_protective_measures_issuing_scope_scope",
                schema: "islamu_event",
                table: "moderation_protective_measures");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_protective_measures_protector_origin_scope",
                schema: "islamu_event",
                table: "moderation_protective_measures");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_protective_measures_reviewer_origin_scope",
                schema: "islamu_event",
                table: "moderation_protective_measures");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_protective_measures_scope_scope",
                schema: "islamu_event",
                table: "moderation_protective_measures");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_protective_measures_subject_origin_scope",
                schema: "islamu_event",
                table: "moderation_protective_measures");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_policies_jurisdiction_scope",
                schema: "islamu_event",
                table: "moderation_policies");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_policy_jurisdiction",
                schema: "islamu_event",
                table: "moderation_policies");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_receipt_scope",
                schema: "islamu_event",
                table: "moderation_operation_receipts");

            migrationBuilder.DropIndex(
                name: "ix_moderation_findings_case_id_rule_owner_id_rule__cc96a1f86c78",
                schema: "islamu_event",
                table: "moderation_findings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_finding_evidence",
                schema: "islamu_event",
                table: "moderation_findings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_findings_adjudicator_origin_scope",
                schema: "islamu_event",
                table: "moderation_findings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_findings_reversed_by_origin_scope",
                schema: "islamu_event",
                table: "moderation_findings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_findings_scope_scope",
                schema: "islamu_event",
                table: "moderation_findings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_findings_subject_origin_scope",
                schema: "islamu_event",
                table: "moderation_findings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_decisions_approval1_origin_scope",
                schema: "islamu_event",
                table: "moderation_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_decisions_approval2_origin_scope",
                schema: "islamu_event",
                table: "moderation_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_decisions_issuer_origin_scope",
                schema: "islamu_event",
                table: "moderation_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_decisions_issuing_scope_scope",
                schema: "islamu_event",
                table: "moderation_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_decisions_scope_scope",
                schema: "islamu_event",
                table: "moderation_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_decisions_subject_origin_scope",
                schema: "islamu_event",
                table: "moderation_decisions");

            migrationBuilder.DropIndex(
                name: "ix_moderation_cases_scope_trust_domain_id_scope_te_f8fb806fdc5e",
                schema: "islamu_event",
                table: "moderation_cases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_cases_scope_scope",
                schema: "islamu_event",
                table: "moderation_cases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_cases_subject_origin_scope",
                schema: "islamu_event",
                table: "moderation_cases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_cases_subject_subject",
                schema: "islamu_event",
                table: "moderation_cases");

            migrationBuilder.DropIndex(
                name: "ix_event_report_signals_tenant_id_provider_provide_a8cb58cd4436",
                schema: "islamu_event",
                table: "event_report_signals");

            migrationBuilder.DropIndex(
                name: "ix_event_report_signals_tenant_id_provider_provide_a8e5d26e7346",
                schema: "islamu_event",
                table: "event_report_signals");

            migrationBuilder.AddColumn<Guid>(
                name: "birth_date_collection_receipt_id",
                schema: "islamu_event",
                table: "user_pii",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "birth_date_revision",
                schema: "islamu_event",
                table: "user_pii",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateOnly>(
                name: "date_of_birth",
                schema: "islamu_event",
                table: "user_pii",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "scope_event_id",
                schema: "islamu_event",
                table: "moderation_sanctions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "subject_origin_event_id",
                schema: "islamu_event",
                table: "moderation_sanctions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "issuing_scope_event_id",
                schema: "islamu_event",
                table: "moderation_protective_measures",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "protector_origin_event_id",
                schema: "islamu_event",
                table: "moderation_protective_measures",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reviewer_origin_event_id",
                schema: "islamu_event",
                table: "moderation_protective_measures",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "scope_event_id",
                schema: "islamu_event",
                table: "moderation_protective_measures",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "subject_origin_event_id",
                schema: "islamu_event",
                table: "moderation_protective_measures",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "jurisdiction_event_id",
                schema: "islamu_event",
                table: "moderation_policies",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "adjudicator_origin_event_id",
                schema: "islamu_event",
                table: "moderation_findings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "attribution_generation",
                schema: "islamu_event",
                table: "moderation_findings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "attribution_source_id",
                schema: "islamu_event",
                table: "moderation_findings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reason_code",
                schema: "islamu_event",
                table: "moderation_findings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "reason_owner_id",
                schema: "islamu_event",
                table: "moderation_findings",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<long>(
                name: "reason_revision",
                schema: "islamu_event",
                table: "moderation_findings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "reversed_by_origin_event_id",
                schema: "islamu_event",
                table: "moderation_findings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "scope_event_id",
                schema: "islamu_event",
                table: "moderation_findings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "scope_id",
                schema: "islamu_event",
                table: "moderation_findings",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "subject_origin_event_id",
                schema: "islamu_event",
                table: "moderation_findings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "lease_expires_at",
                schema: "islamu_event",
                table: "moderation_effect_intents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "lease_id",
                schema: "islamu_event",
                table: "moderation_effect_intents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "lease_started_at",
                schema: "islamu_event",
                table: "moderation_effect_intents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "original_completion_host_binding_id",
                schema: "islamu_event",
                table: "moderation_effect_intents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "original_completion_id",
                schema: "islamu_event",
                table: "moderation_effect_intents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "original_completion_revision",
                schema: "islamu_event",
                table: "moderation_effect_intents",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "original_completion_writer_tenant_id",
                schema: "islamu_event",
                table: "moderation_effect_intents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "original_notice_cleanup_id",
                schema: "islamu_event",
                table: "moderation_effect_intents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "original_notice_host_binding_id",
                schema: "islamu_event",
                table: "moderation_effect_intents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "original_notice_purged_at",
                schema: "islamu_event",
                table: "moderation_effect_intents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "original_notice_retain_until",
                schema: "islamu_event",
                table: "moderation_effect_intents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "original_notice_writer_tenant_id",
                schema: "islamu_event",
                table: "moderation_effect_intents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "approval1_origin_event_id",
                schema: "islamu_event",
                table: "moderation_decisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "approval2_origin_event_id",
                schema: "islamu_event",
                table: "moderation_decisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "issuer_origin_event_id",
                schema: "islamu_event",
                table: "moderation_decisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "issuing_scope_event_id",
                schema: "islamu_event",
                table: "moderation_decisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "scope_event_id",
                schema: "islamu_event",
                table: "moderation_decisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "subject_origin_event_id",
                schema: "islamu_event",
                table: "moderation_decisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "subject_origin_trust_domain_id",
                schema: "islamu_event",
                table: "moderation_cases",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<int>(
                name: "subject_origin_kind",
                schema: "islamu_event",
                table: "moderation_cases",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "subject_kind",
                schema: "islamu_event",
                table: "moderation_cases",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<Guid>(
                name: "subject_id",
                schema: "islamu_event",
                table: "moderation_cases",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "attribution_source_id",
                schema: "islamu_event",
                table: "moderation_cases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "occurrence_case_id",
                schema: "islamu_event",
                table: "moderation_cases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "occurrence_evidence_revision",
                schema: "islamu_event",
                table: "moderation_cases",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "occurrence_owner_id",
                schema: "islamu_event",
                table: "moderation_cases",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<int>(
                name: "occurrence_owner_kind",
                schema: "islamu_event",
                table: "moderation_cases",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "scope_event_id",
                schema: "islamu_event",
                table: "moderation_cases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "subject_origin_event_id",
                schema: "islamu_event",
                table: "moderation_cases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "target_id",
                schema: "islamu_event",
                table: "moderation_cases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "target_kind",
                schema: "islamu_event",
                table: "moderation_cases",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "target_origin_community_id",
                schema: "islamu_event",
                table: "moderation_cases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "target_origin_event_id",
                schema: "islamu_event",
                table: "moderation_cases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "target_origin_instance_id",
                schema: "islamu_event",
                table: "moderation_cases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "target_origin_kind",
                schema: "islamu_event",
                table: "moderation_cases",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "target_origin_resource_id",
                schema: "islamu_event",
                table: "moderation_cases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "target_origin_solution_id",
                schema: "islamu_event",
                table: "moderation_cases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "target_origin_tenant_id",
                schema: "islamu_event",
                table: "moderation_cases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "target_origin_trust_domain_id",
                schema: "islamu_event",
                table: "moderation_cases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "target_version",
                schema: "islamu_event",
                table: "moderation_cases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "material_version_id",
                schema: "islamu_event",
                table: "event_resources",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "moderation_quarantine_count",
                schema: "islamu_event",
                table: "event_resources",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "advisory_identity",
                schema: "islamu_event",
                table: "event_report_signals",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "advisory_material_sha256",
                schema: "islamu_event",
                table: "event_report_signals",
                type: "character varying(71)",
                maxLength: 71,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "advisory_ordinal",
                schema: "islamu_event",
                table: "event_report_signals",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "case_id",
                schema: "islamu_event",
                table: "event_report_signals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "incoming_webhook_message_id",
                schema: "islamu_event",
                table: "event_report_signals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "provider_link_id",
                schema: "islamu_event",
                table: "event_report_signals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "source_case_concurrency_stamp",
                schema: "islamu_event",
                table: "event_report_signals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "case_concurrency_stamp",
                schema: "islamu_event",
                table: "event_report_external_links",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "moderation_reservation",
                schema: "islamu_event",
                table: "configuration_import_sessions",
                type: "character varying(1100000)",
                maxLength: 1100000,
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_event_report_external_links_tenant_id_report_id_id",
                schema: "islamu_event",
                table: "event_report_external_links",
                columns: new[] { "tenant_id", "report_id", "id" });

            migrationBuilder.CreateTable(
                name: "key_obligation",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    intent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    key_generation = table.Column<long>(type: "bigint", nullable: false),
                    verification_tag = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    capture_material_receipt = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    material_receipt = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    original_captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    original_retain_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    backup_horizon_ticks = table.Column<long>(type: "bigint", nullable: false),
                    backup_safety_margin_ticks = table.Column<long>(type: "bigint", nullable: false),
                    receipt_retention_ticks = table.Column<long>(type: "bigint", nullable: false),
                    readable_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    authority_sequence = table.Column<long>(type: "bigint", nullable: true),
                    material_pruned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    receipt_retain_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_key_obligation", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "key_replica",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    runtime_replica_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    ring_generation = table.Column<long>(type: "bigint", nullable: false),
                    ring_commitment = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    binding_revision = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    source = table.Column<int>(type: "integer", nullable: false),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_key_replica", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "moderation_case_evidence",
                schema: "islamu_event",
                columns: table => new
                {
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_revision = table.Column<long>(type: "bigint", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_version = table.Column<Guid>(type: "uuid", nullable: false),
                    material_fact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_origin_kind = table.Column<int>(type: "integer", nullable: false),
                    source_origin_trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_origin_solution_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_origin_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_origin_tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_origin_community_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_origin_resource_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_origin_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_case_evidence", x => new { x.case_id, x.case_revision });
                    table.CheckConstraint("ck_moderation_case_evidence_origin", "source_origin_kind IN (3, 4) AND source_origin_solution_id IS NOT NULL AND source_origin_instance_id IS NOT NULL AND source_origin_community_id IS NULL AND source_origin_resource_id IS NULL AND source_origin_event_id IS NULL AND ((source_origin_kind = 3 AND source_origin_tenant_id IS NULL) OR (source_origin_kind = 4 AND source_origin_tenant_id IS NOT NULL))");
                    table.CheckConstraint("ck_moderation_case_evidence_revision", "case_revision > 0");
                    table.ForeignKey(
                        name: "fk_moderation_case_evidence_moderation_cases_case_id",
                        column: x => x.case_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "moderation_case_narrative_policy_sources",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_kind = table.Column<int>(type: "integer", nullable: false),
                    purpose = table.Column<int>(type: "integer", nullable: false),
                    maximum_retention_ticks = table.Column<long>(type: "bigint", nullable: false),
                    maximum_material_bytes = table.Column<int>(type: "integer", nullable: false),
                    privacy_owner_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    privacy_controller_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    governance_controller_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    identity_and_independence_basis = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    privacy_legal_mandate_basis = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    recorded_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_case_narrative_policy_sources", x => x.id);
                    table.UniqueConstraint("ak_moderation_case_narrative_policy_sources_id_host_binding_id", x => new { x.id, x.host_binding_id, x.trust_domain_id, x.tenant_id });
                    table.CheckConstraint("ck_case_narrative_policy_generation", "revision = 1 AND writer_generation > 0");
                    table.CheckConstraint("ck_case_narrative_policy_independence", "privacy_owner_human_id <> recorded_by_human_id AND privacy_controller_human_id <> governance_controller_human_id AND privacy_controller_human_id <> recorded_by_human_id AND privacy_owner_human_id <> governance_controller_human_id");
                    table.CheckConstraint("ck_case_narrative_policy_limits", "maximum_retention_ticks > 0 AND maximum_retention_ticks < 9223372036854775807 AND maximum_material_bytes > 0 AND maximum_material_bytes <= 65536 AND valid_until > recorded_at");
                    table.CheckConstraint("ck_case_narrative_policy_purpose", "purpose IN (1, 3) OR (purpose = 2 AND target_kind = 2)");
                    table.ForeignKey(
                        name: "fk_moderation_case_narrative_policy_sources_modera_1c68bc79ba2a",
                        column: x => x.host_binding_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_narrative_policy_sources_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "islamu_event",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_narrative_policy_sources_users__9ee962ac13b9",
                        column: x => x.governance_controller_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_narrative_policy_sources_users__c18d4031bd4b",
                        column: x => x.privacy_controller_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_narrative_policy_sources_users__c82ce48d8787",
                        column: x => x.privacy_owner_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_narrative_policy_sources_users__fde2fedb1f49",
                        column: x => x.recorded_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_case_target_policies",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_kind = table.Column<int>(type: "integer", nullable: false),
                    submission_lifetime_ticks = table.Column<long>(type: "bigint", nullable: false),
                    read_lifetime_ticks = table.Column<long>(type: "bigint", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    changed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_case_target_policies", x => x.id);
                    table.CheckConstraint("ck_moderation_target_policy_lifetime", "revision > 0 AND target_kind BETWEEN 1 AND 9 AND submission_lifetime_ticks > 0 AND submission_lifetime_ticks <= 864000000000 AND read_lifetime_ticks > 0 AND read_lifetime_ticks <= 864000000000");
                    table.ForeignKey(
                        name: "fk_moderation_case_target_policies_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "islamu_event",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_case_target_receipts",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caller_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    qualification_epoch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    qualification_revision = table.Column<long>(type: "bigint", nullable: false),
                    relationship_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    relationship_kind = table.Column<int>(type: "integer", nullable: false),
                    relationship_revision = table.Column<long>(type: "bigint", nullable: true),
                    relationship_concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: true),
                    relationship_facts_digest = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: true),
                    lifetime_policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lifetime_policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    token_digest = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false),
                    target_kind = table.Column<int>(type: "integer", nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_version = table.Column<Guid>(type: "uuid", nullable: false),
                    target_origin_kind = table.Column<int>(type: "integer", nullable: false),
                    target_origin_trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_origin_solution_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_origin_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_origin_tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_origin_community_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_origin_resource_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_origin_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_origin_kind = table.Column<int>(type: "integer", nullable: false),
                    source_origin_trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_origin_solution_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_origin_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_origin_tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_origin_community_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_origin_resource_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_origin_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurrence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_version = table.Column<Guid>(type: "uuid", nullable: false),
                    material_fact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_case_id = table.Column<Guid>(type: "uuid", nullable: true),
                    consumed_operation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_case_target_receipts", x => x.id);
                    table.CheckConstraint("ck_moderation_case_target_receipts_source_origin_scope", "source_origin_kind IS NOT NULL AND ((source_origin_kind = 1 AND source_origin_trust_domain_id IS NOT NULL AND source_origin_solution_id IS NULL AND source_origin_instance_id IS NULL AND source_origin_tenant_id IS NULL AND source_origin_community_id IS NULL AND source_origin_resource_id IS NULL AND source_origin_event_id IS NULL) OR (source_origin_kind = 2 AND source_origin_trust_domain_id IS NOT NULL AND source_origin_solution_id IS NOT NULL AND source_origin_instance_id IS NULL AND source_origin_tenant_id IS NULL AND source_origin_community_id IS NULL AND source_origin_resource_id IS NULL AND source_origin_event_id IS NULL) OR (source_origin_kind = 3 AND source_origin_trust_domain_id IS NOT NULL AND source_origin_solution_id IS NOT NULL AND source_origin_instance_id IS NOT NULL AND source_origin_tenant_id IS NULL AND source_origin_community_id IS NULL AND source_origin_resource_id IS NULL AND source_origin_event_id IS NULL) OR (source_origin_kind = 4 AND source_origin_trust_domain_id IS NOT NULL AND source_origin_solution_id IS NOT NULL AND source_origin_instance_id IS NOT NULL AND source_origin_tenant_id IS NOT NULL AND source_origin_community_id IS NULL AND source_origin_resource_id IS NULL AND source_origin_event_id IS NULL) OR (source_origin_kind = 5 AND source_origin_trust_domain_id IS NOT NULL AND source_origin_solution_id IS NOT NULL AND source_origin_instance_id IS NOT NULL AND source_origin_tenant_id IS NOT NULL AND source_origin_community_id IS NOT NULL AND source_origin_resource_id IS NULL AND source_origin_event_id IS NULL) OR (source_origin_kind = 6 AND source_origin_trust_domain_id IS NOT NULL AND source_origin_solution_id IS NOT NULL AND source_origin_instance_id IS NOT NULL AND source_origin_tenant_id IS NOT NULL AND source_origin_resource_id IS NOT NULL AND source_origin_event_id IS NULL) OR (source_origin_kind = 7 AND source_origin_trust_domain_id IS NOT NULL AND source_origin_solution_id IS NOT NULL AND source_origin_instance_id IS NOT NULL AND source_origin_tenant_id IS NOT NULL AND source_origin_community_id IS NULL AND source_origin_resource_id IS NULL AND source_origin_event_id IS NOT NULL AND source_origin_event_id <> '00000000-0000-0000-0000-000000000000'))");
                    table.CheckConstraint("ck_moderation_case_target_receipts_target_origin_scope", "target_origin_kind IS NOT NULL AND ((target_origin_kind = 1 AND target_origin_trust_domain_id IS NOT NULL AND target_origin_solution_id IS NULL AND target_origin_instance_id IS NULL AND target_origin_tenant_id IS NULL AND target_origin_community_id IS NULL AND target_origin_resource_id IS NULL AND target_origin_event_id IS NULL) OR (target_origin_kind = 2 AND target_origin_trust_domain_id IS NOT NULL AND target_origin_solution_id IS NOT NULL AND target_origin_instance_id IS NULL AND target_origin_tenant_id IS NULL AND target_origin_community_id IS NULL AND target_origin_resource_id IS NULL AND target_origin_event_id IS NULL) OR (target_origin_kind = 3 AND target_origin_trust_domain_id IS NOT NULL AND target_origin_solution_id IS NOT NULL AND target_origin_instance_id IS NOT NULL AND target_origin_tenant_id IS NULL AND target_origin_community_id IS NULL AND target_origin_resource_id IS NULL AND target_origin_event_id IS NULL) OR (target_origin_kind = 4 AND target_origin_trust_domain_id IS NOT NULL AND target_origin_solution_id IS NOT NULL AND target_origin_instance_id IS NOT NULL AND target_origin_tenant_id IS NOT NULL AND target_origin_community_id IS NULL AND target_origin_resource_id IS NULL AND target_origin_event_id IS NULL) OR (target_origin_kind = 5 AND target_origin_trust_domain_id IS NOT NULL AND target_origin_solution_id IS NOT NULL AND target_origin_instance_id IS NOT NULL AND target_origin_tenant_id IS NOT NULL AND target_origin_community_id IS NOT NULL AND target_origin_resource_id IS NULL AND target_origin_event_id IS NULL) OR (target_origin_kind = 6 AND target_origin_trust_domain_id IS NOT NULL AND target_origin_solution_id IS NOT NULL AND target_origin_instance_id IS NOT NULL AND target_origin_tenant_id IS NOT NULL AND target_origin_resource_id IS NOT NULL AND target_origin_event_id IS NULL) OR (target_origin_kind = 7 AND target_origin_trust_domain_id IS NOT NULL AND target_origin_solution_id IS NOT NULL AND target_origin_instance_id IS NOT NULL AND target_origin_tenant_id IS NOT NULL AND target_origin_community_id IS NULL AND target_origin_resource_id IS NULL AND target_origin_event_id IS NOT NULL AND target_origin_event_id <> '00000000-0000-0000-0000-000000000000'))");
                    table.CheckConstraint("ck_moderation_intake_receipt_consumption", "(revision = 1 AND consumed_case_id IS NULL AND consumed_operation_id IS NULL AND consumed_at IS NULL) OR (relationship_kind <> 5 AND revision = 2 AND consumed_case_id IS NOT NULL AND consumed_operation_id IS NOT NULL AND consumed_at IS NOT NULL AND consumed_at >= issued_at AND consumed_at < expires_at)");
                    table.CheckConstraint("ck_moderation_intake_receipt_generations", "qualification_revision > 0 AND lifetime_policy_revision > 0 AND ((relationship_kind IN (2, 5) AND relationship_revision IS NOT NULL AND relationship_revision > 0 AND relationship_concurrency_stamp IS NULL AND relationship_facts_digest IS NULL) OR (relationship_kind IN (1, 3, 4, 6) AND relationship_revision IS NULL AND relationship_concurrency_stamp IS NOT NULL AND relationship_facts_digest IS NULL) OR (relationship_kind = 7 AND target_kind IN (3, 4) AND relationship_revision IS NULL AND relationship_concurrency_stamp IS NULL AND relationship_facts_digest IS NOT NULL AND length(relationship_facts_digest) = 64))");
                    table.CheckConstraint("ck_moderation_intake_receipt_origin", "target_origin_trust_domain_id = source_origin_trust_domain_id AND target_origin_solution_id = source_origin_solution_id AND target_origin_instance_id = source_origin_instance_id AND source_origin_kind IN (3, 4) AND (source_origin_tenant_id IS NULL OR source_origin_tenant_id = tenant_id) AND ((target_kind IN (3, 4) AND target_origin_kind = 3) OR (target_kind IN (1, 2, 5, 6, 7, 8, 9) AND target_origin_kind = 4 AND target_origin_tenant_id = tenant_id))");
                    table.CheckConstraint("ck_moderation_intake_receipt_validity", "expires_at > issued_at");
                    table.ForeignKey(
                        name: "fk_moderation_case_target_receipts_moderation_case_a5a3d6314967",
                        column: x => x.consumed_case_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_target_receipts_moderation_host_1286d490ca4f",
                        column: x => x.host_binding_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_target_receipts_moderation_qual_f617fd34a9e1",
                        column: x => x.qualification_epoch_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_qualification_epochs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_target_receipts_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "islamu_event",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_case_target_sources",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    event_version = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    native_kind = table.Column<int>(type: "integer", nullable: false),
                    native_target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    native_target_version = table.Column<Guid>(type: "uuid", nullable: false),
                    native_facts_digest = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false),
                    occurrence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurrence_version = table.Column<Guid>(type: "uuid", nullable: false),
                    material_fact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurrence_digest = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false),
                    material_digest = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_case_target_sources", x => x.id);
                    table.CheckConstraint("ck_moderation_native_source_event", "(event_id IS NULL AND event_version IS NULL AND kind IN (3, 4)) OR (event_id IS NOT NULL AND event_version IS NOT NULL)");
                    table.CheckConstraint("ck_moderation_native_source_kind", "kind BETWEEN 1 AND 9 AND native_kind BETWEEN 1 AND 7 AND (kind IN (8, 9) OR kind = native_kind) AND (kind <> 9 OR native_kind = 1)");
                    table.ForeignKey(
                        name: "fk_moderation_case_target_sources_events_event_id",
                        column: x => x.event_id,
                        principalSchema: "islamu_event",
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_target_sources_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "islamu_event",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_constitution_availability",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewer_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    maximum_caseload = table.Column<int>(type: "integer", nullable: false),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    withdrawn_by_human_id = table.Column<Guid>(type: "uuid", nullable: true),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_constitution_availability", x => x.id);
                    table.CheckConstraint("ck_moderation_constitution_availability_capacity", "maximum_caseload > 0 AND writer_generation > 0");
                    table.CheckConstraint("ck_moderation_constitution_availability_validity", "valid_until > recorded_at");
                    table.CheckConstraint("ck_moderation_constitution_availability_withdrawal", "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR (revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at >= recorded_at)");
                    table.ForeignKey(
                        name: "fk_moderation_constitution_availability_users_reviewer_human_id",
                        column: x => x.reviewer_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_constitution_availability_users_with_9f0f07c1fe61",
                        column: x => x.withdrawn_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_consumer_installations",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    consumer_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    mode_code = table.Column<int>(type: "integer", nullable: false),
                    runtime_generation = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    inventory_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    installed_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    installed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    withdrawn_by_human_id = table.Column<Guid>(type: "uuid", nullable: true),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_consumer_installations", x => x.id);
                    table.CheckConstraint("ck_moderation_consumer_installation_generation", "writer_generation > 0");
                    table.CheckConstraint("ck_moderation_consumer_installation_owner", "consumer_id <> '' AND mode_code = 1");
                    table.CheckConstraint("ck_moderation_consumer_installation_validity", "valid_until > installed_at");
                    table.CheckConstraint("ck_moderation_consumer_installation_withdrawal", "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR (revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at >= installed_at)");
                    table.ForeignKey(
                        name: "fk_moderation_consumer_installations_moderation_ho_f06cf1a91870",
                        column: x => x.host_binding_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_consumer_installations_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "islamu_event",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_consumer_installations_users_install_e910e2b0ce0d",
                        column: x => x.installed_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_consumer_installations_users_withdra_8564bd259043",
                        column: x => x.withdrawn_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_event_generations",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    observed_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    event_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    organizer_actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    organizer_actor_stamp = table.Column<Guid>(type: "uuid", nullable: true),
                    submitted_by_human_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_event_generations", x => x.id);
                    table.CheckConstraint("ck_moderation_event_generation_organizer", "(organizer_actor_id IS NULL AND organizer_actor_stamp IS NULL) OR (organizer_actor_id IS NOT NULL AND organizer_actor_stamp IS NOT NULL)");
                    table.CheckConstraint("ck_moderation_event_generation_revision", "revision > 0 AND writer_generation > 0");
                    table.CheckConstraint("ck_moderation_event_generation_validity", "valid_until > observed_at");
                    table.ForeignKey(
                        name: "fk_moderation_event_generations_moderation_host_bi_7fbc19c69a2e",
                        column: x => x.host_binding_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_event_generations_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "islamu_event",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_event_generations_users_observed_by_human_id",
                        column: x => x.observed_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_measurement_issuer_enrollments",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key_id = table.Column<Guid>(type: "uuid", nullable: false),
                    issuer = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    public_key_spki_base64 = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    runner_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    catalogue_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    test_bundle_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    approved_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_by_human_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_measurement_issuer_enrollments", x => x.id);
                    table.CheckConstraint("ck_moderation_measurement_issuer_enrollment_generation", "writer_generation > 0 AND writer_generation < 9223372036854775807");
                    table.CheckConstraint("ck_moderation_measurement_issuer_enrollment_key", "key_id <> id");
                    table.CheckConstraint("ck_moderation_measurement_issuer_enrollment_revocation", "(revision = 1 AND revoked_at IS NULL AND revoked_by_human_id IS NULL) OR (revision = 2 AND revoked_at IS NOT NULL AND revoked_by_human_id IS NOT NULL AND revoked_at >= approved_at)");
                    table.CheckConstraint("ck_moderation_measurement_issuer_enrollment_validity", "valid_until > approved_at");
                    table.ForeignKey(
                        name: "fk_moderation_measurement_issuer_enrollments_moder_6c4dedfbde7f",
                        column: x => x.host_binding_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_measurement_issuer_enrollments_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "islamu_event",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_measurement_issuer_enrollments_users_100bb1778069",
                        column: x => x.approved_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_measurement_issuer_enrollments_users_586fe5cda71c",
                        column: x => x.revoked_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_notice_sources",
                schema: "islamu_event",
                columns: table => new
                {
                    effect_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision_revision = table.Column<long>(type: "bigint", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scope_kind = table.Column<int>(type: "integer", nullable: false),
                    scope_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_kind = table.Column<int>(type: "integer", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    snapshot_json = table.Column<string>(type: "text", nullable: false),
                    snapshot_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    effect_binding_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    retain_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status_metadata_json = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                    status_metadata_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_notice_sources", x => x.effect_id);
                    table.CheckConstraint("ck_moderation_notice_source_retention", "retain_until IS NULL OR retain_until > captured_at");
                    table.CheckConstraint("ck_moderation_notice_source_revision", "decision_revision > 0");
                    table.CheckConstraint("ck_moderation_notice_source_scope", "scope_kind BETWEEN 1 AND 7");
                    table.CheckConstraint("ck_moderation_notice_source_status", "(status_metadata_json IS NULL AND status_metadata_hash IS NULL) OR (status_metadata_json IS NOT NULL AND status_metadata_hash IS NOT NULL AND retain_until IS NOT NULL AND subject_kind IN (1, 4))");
                    table.CheckConstraint("ck_moderation_notice_source_subject", "subject_kind BETWEEN 1 AND 4");
                    table.ForeignKey(
                        name: "fk_moderation_notice_sources_moderation_effect_int_c159766d643f",
                        column: x => x.effect_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_effect_intents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_operational_budget_sources",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_kind = table.Column<int>(type: "integer", nullable: false),
                    action = table.Column<int>(type: "integer", nullable: false),
                    maximum_occurrences = table.Column<int>(type: "integer", nullable: false),
                    window_ticks = table.Column<long>(type: "bigint", nullable: false),
                    operator_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    recorded_at = table.Column<long>(type: "bigint", nullable: false),
                    valid_until = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_operational_budget_sources", x => x.id);
                    table.UniqueConstraint("ak_moderation_operational_budget_sources_id_host_binding_id_tr", x => new { x.id, x.host_binding_id, x.trust_domain_id, x.tenant_id });
                    table.CheckConstraint("ck_operational_budget_source_generation", "revision = 1 AND writer_generation > 0 AND writer_generation < 9223372036854775807");
                    table.CheckConstraint("ck_operational_budget_source_independence", "operator_human_id <> recorded_by_human_id");
                    table.CheckConstraint("ck_operational_budget_source_terms", "subject_kind = 1 AND action IN (1, 2, 3, 4, 5, 6) AND maximum_occurrences > 0 AND window_ticks > 0 AND window_ticks < 9223372036854775807");
                    table.CheckConstraint("ck_operational_budget_source_validity", "recorded_at > 0 AND valid_until > recorded_at AND valid_until < 3155378975999999999 AND window_ticks < recorded_at AND window_ticks < 3155378975999999999 - recorded_at");
                    table.ForeignKey(
                        name: "fk_moderation_operational_budget_sources_moderatio_f3b091d1b93f",
                        column: x => x.host_binding_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_operational_budget_sources_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "islamu_event",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_operational_budget_sources_users_ope_d29eb4e10610",
                        column: x => x.operator_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_operational_budget_sources_users_rec_bcaa052806b5",
                        column: x => x.recorded_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_policy_publications",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    adopted_revision = table.Column<long>(type: "bigint", nullable: true),
                    previous_publication_id = table.Column<Guid>(type: "uuid", nullable: true),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    solution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    jurisdiction_tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    principal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    declared_state_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    authority_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    authority_revision = table.Column<long>(type: "bigint", nullable: false),
                    qualification_revision = table.Column<long>(type: "bigint", nullable: false),
                    writer_generation = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    catalogue_revision = table.Column<long>(type: "bigint", nullable: false),
                    inventory_revision = table.Column<long>(type: "bigint", nullable: false),
                    inventory_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    published_active = table.Column<bool>(type: "boolean", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    evidence_valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    maximum_finite_duration_ticks = table.Column<long>(type: "bigint", nullable: false),
                    maximum_review_delay_ticks = table.Column<long>(type: "bigint", nullable: false),
                    minimum_approvals = table.Column<int>(type: "integer", nullable: false),
                    maximum_approvals = table.Column<int>(type: "integer", nullable: false),
                    minimum_contestation_calendar_months = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_policy_publications", x => x.id);
                    table.UniqueConstraint("ak_moderation_policy_publications_policy_id_id", x => new { x.policy_id, x.id });
                    table.CheckConstraint("ck_moderation_publication_adoption", "(adopted_revision IS NOT NULL AND adopted_revision = revision) OR (adopted_revision IS NULL AND published_active = false AND evidence_valid_until IS NULL)");
                    table.CheckConstraint("ck_moderation_publication_duration", "maximum_finite_duration_ticks > 0 AND maximum_review_delay_ticks > 0");
                    table.CheckConstraint("ck_moderation_publication_history", "(revision = 1 AND previous_publication_id IS NULL) OR (revision > 1 AND previous_publication_id IS NOT NULL)");
                    table.CheckConstraint("ck_moderation_publication_revision", "revision > 0 AND authority_revision > 0 AND qualification_revision = authority_revision + 1");
                    table.ForeignKey(
                        name: "fk_moderation_policy_publications_moderation_host__2394e4faae30",
                        column: x => x.host_binding_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_policy_publications_moderation_polic_5dbf4585817a",
                        columns: x => new { x.policy_id, x.previous_publication_id },
                        principalSchema: "islamu_event",
                        principalTable: "moderation_policy_publications",
                        principalColumns: new[] { "policy_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_policy_publications_moderation_quali_c189b6df0fe7",
                        column: x => x.authority_source_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_qualification_epochs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_policy_publications_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "islamu_event",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_pre_finding_erasure_tombstones",
                schema: "islamu_event",
                columns: table => new
                {
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_pre_finding_erasure_tombstones", x => x.case_id);
                    table.ForeignKey(
                        name: "fk_moderation_pre_finding_erasure_tombstones_moder_9d65109b40d8",
                        column: x => x.case_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_pre_finding_receipts",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    result_id = table.Column<Guid>(type: "uuid", nullable: false),
                    result_revision = table.Column<long>(type: "bigint", nullable: false),
                    request_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_pre_finding_receipts", x => x.id);
                    table.ForeignKey(
                        name: "fk_moderation_pre_finding_receipts_moderation_cases_case_id",
                        column: x => x.case_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_pre_finding_receipts_users_actor_human_id",
                        column: x => x.actor_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_qualification_policy_sources",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_revision = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    maximum_reviewer_lifetime_ticks = table.Column<long>(type: "bigint", nullable: false),
                    maximum_urgency_lifetime_ticks = table.Column<long>(type: "bigint", nullable: false),
                    published_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_qualification_policy_sources", x => x.id);
                    table.UniqueConstraint("ak_moderation_qualification_policy_sources_id_host_binding_id_", x => new { x.id, x.host_binding_id, x.trust_domain_id, x.tenant_id, x.case_id });
                    table.CheckConstraint("ck_moderation_qualification_policy_source_generation", "revision > 0 AND case_revision > 0 AND writer_generation > 0");
                    table.CheckConstraint("ck_moderation_qualification_policy_source_limits", "maximum_reviewer_lifetime_ticks > 0 AND maximum_reviewer_lifetime_ticks < 9223372036854775807 AND maximum_urgency_lifetime_ticks > 0 AND maximum_urgency_lifetime_ticks < 9223372036854775807");
                    table.CheckConstraint("ck_moderation_qualification_policy_source_validity", "valid_until > published_at");
                    table.ForeignKey(
                        name: "fk_moderation_qualification_policy_sources_moderat_521e350afec3",
                        column: x => x.case_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_qualification_policy_sources_moderat_9b243fc678b5",
                        column: x => x.host_binding_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_qualification_policy_sources_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "islamu_event",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_qualification_policy_sources_users_p_c05b00cd0da2",
                        column: x => x.published_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_recognition_disposition",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    intent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_version = table.Column<int>(type: "integer", nullable: false),
                    reason_code = table.Column<int>(type: "integer", nullable: false),
                    pending_revision = table.Column<long>(type: "bigint", nullable: false),
                    original_material_receipt = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    original_material_count = table.Column<int>(type: "integer", nullable: false),
                    observed_material_commitment = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    coverage_receipt = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    coverage_count = table.Column<int>(type: "integer", nullable: false),
                    observed_failure = table.Column<int>(type: "integer", nullable: false),
                    grounds_receipt = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    legal_mandate_receipt = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_revision = table.Column<long>(type: "bigint", nullable: false),
                    source_generation = table.Column<long>(type: "bigint", nullable: false),
                    adoption_id = table.Column<Guid>(type: "uuid", nullable: false),
                    adoption_revision = table.Column<long>(type: "bigint", nullable: false),
                    adoption_generation = table.Column<long>(type: "bigint", nullable: false),
                    privacy_owner_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recovery_owner_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    grounds = table.Column<int>(type: "integer", nullable: false),
                    original_deadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    assessed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    authorized_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    authority_high_water_before_authorization = table.Column<long>(type: "bigint", nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    authority_sequence = table.Column<long>(type: "bigint", nullable: true),
                    authority_recorded_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    replay_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    retain_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_recognition_disposition", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "moderation_recognition_disposition_remedy",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    intent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pending_revision = table.Column<long>(type: "bigint", nullable: false),
                    original_material_receipt = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    observed_material_commitment = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    grounds_receipt = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    legal_mandate_receipt = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    coverage_receipt = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    privacy_owner_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recovery_owner_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    grounds = table.Column<int>(type: "integer", nullable: false),
                    receipt_hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    protected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    remedy_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    retain_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_recognition_disposition_remedy", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "moderation_recognition_escalation",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    intent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action_recovery_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pending_revision = table.Column<long>(type: "bigint", nullable: false),
                    recipient_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    other_owner_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stage = table.Column<int>(type: "integer", nullable: false),
                    required_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    original_deadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    retain_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    notification_id = table.Column<Guid>(type: "uuid", nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_recognition_escalation", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "moderation_recognition_key_lifecycle",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ring_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    acknowledgement_sequence = table.Column<long>(type: "bigint", nullable: false),
                    initial_erasure_head = table.Column<long>(type: "bigint", nullable: false),
                    initial_erasure_retained_floor = table.Column<long>(type: "bigint", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_recognition_key_lifecycle", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "moderation_recognition_key_lifecycle_purge",
                schema: "islamu_event",
                columns: table => new
                {
                    intent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_receipt = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    material_count = table.Column<int>(type: "integer", nullable: false),
                    authority_sequence = table.Column<long>(type: "bigint", nullable: false),
                    purged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    backup_obligation_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    retain_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_recognition_key_lifecycle_purge", x => x.intent_id);
                });

            migrationBuilder.CreateTable(
                name: "moderation_recognition_pending",
                schema: "islamu_event",
                columns: table => new
                {
                    intent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_version = table.Column<int>(type: "integer", nullable: false),
                    reason_code = table.Column<int>(type: "integer", nullable: false),
                    privacy_owner_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recovery_owner_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    settle_by = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    escalate_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    receipt_hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    receipt_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    failure = table.Column<int>(type: "integer", nullable: false),
                    material_count = table.Column<int>(type: "integer", nullable: false),
                    material_receipt = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    computed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    authority_sequence = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_recognition_pending", x => x.intent_id);
                });

            migrationBuilder.CreateTable(
                name: "moderation_recovery_store_states",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    generation = table.Column<long>(type: "bigint", nullable: false),
                    position = table.Column<long>(type: "bigint", nullable: false),
                    installation_slot = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_recovery_store_states", x => x.id);
                    table.CheckConstraint("ck_moderation_recovery_store_coordinates", "revision > 0 AND generation > 0 AND generation <= revision - position AND position >= 0 AND position < revision");
                    table.CheckConstraint("ck_moderation_recovery_store_slot", "installation_slot = 1");
                });

            migrationBuilder.CreateTable(
                name: "moderation_resource_generations",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    observed_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resource_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    material_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    organizer_actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    organizer_actor_stamp = table.Column<Guid>(type: "uuid", nullable: true),
                    delivery_type = table.Column<int>(type: "integer", nullable: false),
                    storage_object_id = table.Column<Guid>(type: "uuid", nullable: true),
                    storage_object_stamp = table.Column<Guid>(type: "uuid", nullable: true),
                    storage_provider_binding_id = table.Column<Guid>(type: "uuid", nullable: true),
                    storage_provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_resource_generations", x => x.id);
                    table.CheckConstraint("ck_moderation_resource_generation_material", "material_version_id <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("ck_moderation_resource_generation_revision", "revision > 0 AND writer_generation > 0");
                    table.CheckConstraint("ck_moderation_resource_generation_storage", "(delivery_type = 1 AND storage_object_id IS NOT NULL AND storage_object_stamp IS NOT NULL AND storage_provider_binding_id IS NOT NULL AND storage_provider IS NOT NULL) OR (delivery_type = 2 AND storage_object_id IS NULL AND storage_object_stamp IS NULL AND storage_provider_binding_id IS NULL AND storage_provider IS NULL)");
                    table.CheckConstraint("ck_moderation_resource_generation_validity", "valid_until > observed_at");
                    table.ForeignKey(
                        name: "fk_moderation_resource_generations_moderation_host_6a28cb064b8b",
                        column: x => x.host_binding_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_resource_generations_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "islamu_event",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_resource_generations_users_observed_by_human_id",
                        column: x => x.observed_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_schedule_risk_reviews",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_sanction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_sanction_revision = table.Column<long>(type: "bigint", nullable: false),
                    source_decision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_sanction_starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    original_sanction_ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    original_due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    accepted_intent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    accepted_finding_id = table.Column<Guid>(type: "uuid", nullable: true),
                    accepted_finding_revision = table.Column<long>(type: "bigint", nullable: true),
                    accepted_reviewer_assignment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    accepted_reviewer_assignment_revision = table.Column<long>(type: "bigint", nullable: true),
                    withdrawal_intent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subject_kind = table.Column<int>(type: "integer", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_origin_kind = table.Column<int>(type: "integer", nullable: false),
                    subject_origin_trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_origin_solution_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subject_origin_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subject_origin_tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subject_origin_community_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subject_origin_resource_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subject_origin_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_scope_kind = table.Column<int>(type: "integer", nullable: false),
                    source_scope_trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_scope_solution_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_scope_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_scope_tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_scope_community_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_scope_resource_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_scope_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_rule_owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_rule_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source_rule_revision = table.Column<long>(type: "bigint", nullable: false),
                    event_scope_kind = table.Column<int>(type: "integer", nullable: false),
                    event_scope_trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_scope_solution_id = table.Column<Guid>(type: "uuid", nullable: true),
                    event_scope_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    event_scope_tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    event_scope_community_id = table.Column<Guid>(type: "uuid", nullable: true),
                    event_scope_resource_id = table.Column<Guid>(type: "uuid", nullable: true),
                    event_scope_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    participant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_schedule_generation = table.Column<Guid>(type: "uuid", nullable: false),
                    schedule_generation = table.Column<Guid>(type: "uuid", nullable: false),
                    original_participant_generation = table.Column<Guid>(type: "uuid", nullable: false),
                    participant_generation = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    original_publication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    publication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_rule_owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_rule_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    original_policy_rule_revision = table.Column<long>(type: "bigint", nullable: false),
                    policy_rule_revision = table.Column<long>(type: "bigint", nullable: false),
                    owner_kind = table.Column<int>(type: "integer", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_origin_kind = table.Column<int>(type: "integer", nullable: false),
                    owner_origin_trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_origin_solution_id = table.Column<Guid>(type: "uuid", nullable: true),
                    owner_origin_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    owner_origin_tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    owner_origin_community_id = table.Column<Guid>(type: "uuid", nullable: true),
                    owner_origin_resource_id = table.Column<Guid>(type: "uuid", nullable: true),
                    owner_origin_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    escalation_owner_kind = table.Column<int>(type: "integer", nullable: false),
                    escalation_owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    escalation_owner_origin_kind = table.Column<int>(type: "integer", nullable: false),
                    escalation_owner_origin_trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    escalation_owner_origin_solution_id = table.Column<Guid>(type: "uuid", nullable: true),
                    escalation_owner_origin_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    escalation_owner_origin_tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    escalation_owner_origin_community_id = table.Column<Guid>(type: "uuid", nullable: true),
                    escalation_owner_origin_resource_id = table.Column<Guid>(type: "uuid", nullable: true),
                    escalation_owner_origin_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    withdrawn_by_human_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_schedule_risk_reviews", x => x.id);
                    table.CheckConstraint("ck_moderation_schedule_risk_ancestry", "subject_kind = 1 AND owner_kind = 1 AND escalation_owner_kind = 1 AND event_scope_kind = 7 AND event_scope_tenant_id IS NOT NULL AND source_sanction_revision > 0 AND revision > 0 AND source_rule_revision > 0 AND original_policy_revision > 0 AND policy_revision >= original_policy_revision AND original_policy_rule_revision > 0 AND policy_rule_revision >= original_policy_rule_revision AND subject_origin_trust_domain_id = source_scope_trust_domain_id AND owner_origin_trust_domain_id = source_scope_trust_domain_id AND escalation_owner_origin_trust_domain_id = source_scope_trust_domain_id AND event_scope_trust_domain_id = source_scope_trust_domain_id AND (source_scope_solution_id IS NULL OR source_scope_solution_id = event_scope_solution_id) AND (source_scope_instance_id IS NULL OR source_scope_instance_id = event_scope_instance_id) AND (source_scope_tenant_id IS NULL OR source_scope_tenant_id = event_scope_tenant_id) AND source_scope_community_id IS NULL AND source_scope_resource_id IS NULL AND (source_scope_event_id IS NULL OR source_scope_event_id = event_scope_event_id)");
                    table.CheckConstraint("ck_moderation_schedule_risk_identity", "source_sanction_id <> '00000000-0000-0000-0000-000000000000' AND source_decision_id <> '00000000-0000-0000-0000-000000000000' AND session_id <> '00000000-0000-0000-0000-000000000000' AND participant_id <> '00000000-0000-0000-0000-000000000000' AND policy_id <> '00000000-0000-0000-0000-000000000000' AND original_schedule_generation <> '00000000-0000-0000-0000-000000000000' AND schedule_generation <> '00000000-0000-0000-0000-000000000000' AND original_participant_generation <> '00000000-0000-0000-0000-000000000000' AND participant_generation <> '00000000-0000-0000-0000-000000000000' AND original_publication_id <> '00000000-0000-0000-0000-000000000000' AND publication_id <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("ck_moderation_schedule_risk_interval", "updated_at >= created_at AND original_due_at > created_at AND due_at > created_at AND (original_sanction_ends_at IS NULL OR original_sanction_ends_at > original_sanction_starts_at)");
                    table.CheckConstraint("ck_moderation_schedule_risk_reviews_escalation_own_93d68bdfa905", "escalation_owner_origin_kind IS NOT NULL AND ((escalation_owner_origin_kind = 1 AND escalation_owner_origin_trust_domain_id IS NOT NULL AND escalation_owner_origin_solution_id IS NULL AND escalation_owner_origin_instance_id IS NULL AND escalation_owner_origin_tenant_id IS NULL AND escalation_owner_origin_community_id IS NULL AND escalation_owner_origin_resource_id IS NULL AND escalation_owner_origin_event_id IS NULL) OR (escalation_owner_origin_kind = 2 AND escalation_owner_origin_trust_domain_id IS NOT NULL AND escalation_owner_origin_solution_id IS NOT NULL AND escalation_owner_origin_instance_id IS NULL AND escalation_owner_origin_tenant_id IS NULL AND escalation_owner_origin_community_id IS NULL AND escalation_owner_origin_resource_id IS NULL AND escalation_owner_origin_event_id IS NULL) OR (escalation_owner_origin_kind = 3 AND escalation_owner_origin_trust_domain_id IS NOT NULL AND escalation_owner_origin_solution_id IS NOT NULL AND escalation_owner_origin_instance_id IS NOT NULL AND escalation_owner_origin_tenant_id IS NULL AND escalation_owner_origin_community_id IS NULL AND escalation_owner_origin_resource_id IS NULL AND escalation_owner_origin_event_id IS NULL) OR (escalation_owner_origin_kind = 4 AND escalation_owner_origin_trust_domain_id IS NOT NULL AND escalation_owner_origin_solution_id IS NOT NULL AND escalation_owner_origin_instance_id IS NOT NULL AND escalation_owner_origin_tenant_id IS NOT NULL AND escalation_owner_origin_community_id IS NULL AND escalation_owner_origin_resource_id IS NULL AND escalation_owner_origin_event_id IS NULL) OR (escalation_owner_origin_kind = 5 AND escalation_owner_origin_trust_domain_id IS NOT NULL AND escalation_owner_origin_solution_id IS NOT NULL AND escalation_owner_origin_instance_id IS NOT NULL AND escalation_owner_origin_tenant_id IS NOT NULL AND escalation_owner_origin_community_id IS NOT NULL AND escalation_owner_origin_resource_id IS NULL AND escalation_owner_origin_event_id IS NULL) OR (escalation_owner_origin_kind = 6 AND escalation_owner_origin_trust_domain_id IS NOT NULL AND escalation_owner_origin_solution_id IS NOT NULL AND escalation_owner_origin_instance_id IS NOT NULL AND escalation_owner_origin_tenant_id IS NOT NULL AND escalation_owner_origin_resource_id IS NOT NULL AND escalation_owner_origin_event_id IS NULL) OR (escalation_owner_origin_kind = 7 AND escalation_owner_origin_trust_domain_id IS NOT NULL AND escalation_owner_origin_solution_id IS NOT NULL AND escalation_owner_origin_instance_id IS NOT NULL AND escalation_owner_origin_tenant_id IS NOT NULL AND escalation_owner_origin_community_id IS NULL AND escalation_owner_origin_resource_id IS NULL AND escalation_owner_origin_event_id IS NOT NULL AND escalation_owner_origin_event_id <> '00000000-0000-0000-0000-000000000000'))");
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
                        name: "fk_moderation_schedule_risk_reviews_moderation_fin_12ef67de6994",
                        column: x => x.accepted_finding_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_findings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_schedule_risk_reviews_moderation_rev_642d17cf71a4",
                        column: x => x.accepted_reviewer_assignment_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_reviewer_assignments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_schedule_risk_reviews_moderation_san_a6f58e54f09b",
                        column: x => x.source_sanction_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_sanctions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_birth_date_collection_receipts",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    observation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assessment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assessment_adoption_id = table.Column<Guid>(type: "uuid", nullable: false),
                    consent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    publication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    collected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_birth_date_collection_receipts", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_birth_date_collection_receipts_user_pii_user_id",
                        column: x => x.user_id,
                        principalSchema: "islamu_event",
                        principalTable: "user_pii",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "moderation_case_narrative_adoptions",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_revision = table.Column<long>(type: "bigint", nullable: false),
                    source_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    adopted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    withdrawn_by_human_id = table.Column<Guid>(type: "uuid", nullable: true),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_case_narrative_adoptions", x => x.id);
                    table.UniqueConstraint("ak_moderation_case_narrative_adoptions_id_host_binding_id_trus", x => new { x.id, x.host_binding_id, x.trust_domain_id, x.tenant_id, x.source_id });
                    table.CheckConstraint("ck_case_narrative_adoption_generation", "source_revision = 1 AND source_writer_generation > 0 AND writer_generation > source_writer_generation");
                    table.CheckConstraint("ck_case_narrative_adoption_validity", "valid_until > adopted_at");
                    table.CheckConstraint("ck_case_narrative_adoption_withdrawal", "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR (revision = 2 AND withdrawn_at >= adopted_at AND withdrawn_by_human_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_moderation_case_narrative_adoptions_moderation__cef36ebfe3ff",
                        columns: x => new { x.source_id, x.host_binding_id, x.trust_domain_id, x.tenant_id },
                        principalSchema: "islamu_event",
                        principalTable: "moderation_case_narrative_policy_sources",
                        principalColumns: new[] { "id", "host_binding_id", "trust_domain_id", "tenant_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_narrative_adoptions_users_adopt_ab8a0c4298c1",
                        column: x => x.adopted_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_narrative_adoptions_users_withd_64e9586d0e7e",
                        column: x => x.withdrawn_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_case_read_grants",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_revision = table.Column<long>(type: "bigint", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caller_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    issuer_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    issuer_role_grant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    issuer_platform_grant = table.Column<bool>(type: "boolean", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_case_read_grants", x => x.id);
                    table.CheckConstraint("ck_moderation_case_read_grant_validity", "expires_at > issued_at AND revision > 0 AND case_revision > 0 AND (revoked_at IS NULL OR revoked_at >= issued_at)");
                    table.ForeignKey(
                        name: "fk_moderation_case_read_grants_moderation_case_tar_deb49548753f",
                        column: x => x.source_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_case_target_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_read_grants_moderation_cases_case_id",
                        column: x => x.case_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_read_grants_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "islamu_event",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_notices",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    effect_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    audience = table.Column<int>(type: "integer", nullable: false),
                    recipient_subject_kind = table.Column<int>(type: "integer", nullable: false),
                    recipient_subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    native_intent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    delivery_disposition = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_notices", x => x.id);
                    table.CheckConstraint("ck_moderation_notice_native", "(native_intent_id IS NULL AND delivery_disposition BETWEEN 1 AND 4) OR (native_intent_id IS NOT NULL AND delivery_disposition IN (5, 6) AND tenant_id IS NOT NULL AND recipient_subject_kind = 1)");
                    table.CheckConstraint("ck_moderation_notice_recipient", "recipient_subject_kind BETWEEN 1 AND 4 AND audience BETWEEN 1 AND 3 AND (audience <> 3 OR recipient_subject_kind = 1)");
                    table.ForeignKey(
                        name: "fk_moderation_notices_moderation_notice_sources_effect_id",
                        column: x => x.effect_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_notice_sources",
                        principalColumn: "effect_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_notices_notification_intents_native_intent_id",
                        column: x => x.native_intent_id,
                        principalSchema: "islamu_event",
                        principalTable: "notification_intents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_original_effect_completion_receipts",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    effect_id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lease_revision = table.Column<long>(type: "bigint", nullable: false),
                    lease_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    lease_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    publication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    snapshot_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    effect_binding_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    selection_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    proof_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    original_cause_count = table.Column<int>(type: "integer", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    qualified_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    retain_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_original_effect_completion_receipts", x => x.id);
                    table.CheckConstraint("ck_original_completion_count", "original_cause_count >= 1 AND original_cause_count <= 64");
                    table.CheckConstraint("ck_original_completion_revision", "lease_revision > 0");
                    table.CheckConstraint("ck_original_completion_window", "completed_at >= lease_started_at AND completed_at < qualified_until AND qualified_until <= lease_expires_at AND qualified_until <= retain_until");
                    table.ForeignKey(
                        name: "fk_moderation_original_effect_completion_receipts__c2970c0916c2",
                        column: x => x.effect_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_notice_sources",
                        principalColumn: "effect_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "moderation_operational_budget_adoptions",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_revision = table.Column<long>(type: "bigint", nullable: false),
                    source_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    adopted_at = table.Column<long>(type: "bigint", nullable: false),
                    valid_until = table.Column<long>(type: "bigint", nullable: false),
                    withdrawn_by_human_id = table.Column<Guid>(type: "uuid", nullable: true),
                    withdrawn_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_operational_budget_adoptions", x => x.id);
                    table.UniqueConstraint("ak_moderation_operational_budget_adoption_id_trust_domain_id_t", x => new { x.id, x.trust_domain_id, x.tenant_id, x.source_id });
                    table.UniqueConstraint("ak_moderation_operational_budget_adoptions_id_host_binding_id_", x => new { x.id, x.host_binding_id, x.trust_domain_id, x.tenant_id, x.source_id });
                    table.CheckConstraint("ck_operational_budget_adoption_generation", "id <> source_id AND source_revision = 1 AND source_writer_generation > 0 AND source_writer_generation < 9223372036854775807 AND writer_generation > source_writer_generation AND writer_generation < 9223372036854775807");
                    table.CheckConstraint("ck_operational_budget_adoption_validity", "adopted_at > 0 AND valid_until > adopted_at AND valid_until < 3155378975999999999");
                    table.CheckConstraint("ck_operational_budget_adoption_withdrawal", "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR (revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_at >= adopted_at AND withdrawn_at < 3155378975999999999 AND withdrawn_by_human_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_moderation_operational_budget_adoptions_moderat_ddb3af922f29",
                        columns: x => new { x.source_id, x.host_binding_id, x.trust_domain_id, x.tenant_id },
                        principalSchema: "islamu_event",
                        principalTable: "moderation_operational_budget_sources",
                        principalColumns: new[] { "id", "host_binding_id", "trust_domain_id", "tenant_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_operational_budget_adoptions_users_a_f1b1e8534408",
                        column: x => x.adopted_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_operational_budget_adoptions_users_w_8c8c8e64eb1f",
                        column: x => x.withdrawn_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_child_age_policy_sources",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    publication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode_code = table.Column<int>(type: "integer", nullable: false),
                    proposed_state_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    scope_ceiling = table.Column<int>(type: "integer", nullable: false),
                    collection = table.Column<int>(type: "integer", nullable: false),
                    consent = table.Column<int>(type: "integer", nullable: false),
                    assurance = table.Column<int>(type: "integer", nullable: false),
                    provider_assurance = table.Column<int>(type: "integer", nullable: false),
                    evidence = table.Column<int>(type: "integer", nullable: false),
                    classification = table.Column<int>(type: "integer", nullable: false),
                    debate_minimum_age = table.Column<int>(type: "integer", nullable: false),
                    audience = table.Column<int>(type: "integer", nullable: false),
                    participant = table.Column<int>(type: "integer", nullable: false),
                    correction = table.Column<int>(type: "integer", nullable: false),
                    protected_access = table.Column<int>(type: "integer", nullable: false),
                    prohibited_inference = table.Column<int>(type: "integer", nullable: false),
                    calendar = table.Column<int>(type: "integer", nullable: false),
                    time_zone = table.Column<int>(type: "integer", nullable: false),
                    leap_day = table.Column<int>(type: "integer", nullable: false),
                    adulthood = table.Column<int>(type: "integer", nullable: false),
                    adulthood_age = table.Column<int>(type: "integer", nullable: false),
                    child_risk_grounds = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    privacy_legal_grounds = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    lawful_collection_purpose = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    consent_guardian_applicability_grounds = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    manual_assurance_qualification_grounds = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    provider_assurance_review_grounds = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    classification_rationale = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    calendar_jurisdiction_grounds = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    reviewer_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewer_controller_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    collection_owner_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    collection_controller_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    governance_controller_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    identity_and_independence_basis = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    privacy_legal_mandate_basis = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    child_safeguarding_mandate_basis = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    collection_mandate_basis = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    recorded_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_child_age_policy_sources", x => x.id);
                    table.CheckConstraint("ck_moderation_child_age_independence", "reviewer_human_id <> recorded_by_human_id AND collection_owner_human_id <> recorded_by_human_id AND reviewer_human_id <> collection_owner_human_id AND reviewer_controller_human_id <> collection_controller_human_id AND reviewer_controller_human_id <> governance_controller_human_id AND collection_controller_human_id <> governance_controller_human_id AND reviewer_human_id <> collection_controller_human_id AND reviewer_human_id <> governance_controller_human_id AND collection_owner_human_id <> reviewer_controller_human_id AND collection_owner_human_id <> governance_controller_human_id AND recorded_by_human_id <> reviewer_controller_human_id AND recorded_by_human_id <> collection_controller_human_id");
                    table.CheckConstraint("ck_moderation_child_age_provider_review", "(provider_assurance = 1 AND provider_assurance_review_grounds IS NULL) OR (provider_assurance = 2 AND provider_assurance_review_grounds IS NOT NULL)");
                    table.CheckConstraint("ck_moderation_child_age_source_generation", "revision = 1 AND policy_revision > 0 AND writer_generation > 0");
                    table.CheckConstraint("ck_moderation_child_age_source_mode", "mode_code BETWEEN 1 AND 2");
                    table.CheckConstraint("ck_moderation_child_age_source_validity", "valid_until > recorded_at");
                    table.CheckConstraint("ck_moderation_child_age_terms", "collection = 1 AND consent = 1 AND assurance = 1 AND evidence = 1 AND classification = 1 AND debate_minimum_age = 18 AND audience = 1 AND participant = 1 AND correction = 1 AND protected_access = 1 AND prohibited_inference = 1 AND calendar = 1 AND time_zone = 1 AND leap_day = 1 AND adulthood = 1 AND adulthood_age BETWEEN 1 AND 130");
                    table.ForeignKey(
                        name: "fk_moderation_child_age_policy_sources_moderation__1cc86abdeb14",
                        column: x => x.definition_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_child_age_policy_sources_users_colle_021bc2b4e020",
                        column: x => x.collection_owner_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_child_age_policy_sources_users_colle_40e802a92500",
                        column: x => x.collection_controller_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_child_age_policy_sources_users_gover_3a2c271e952e",
                        column: x => x.governance_controller_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_child_age_policy_sources_users_recor_5c76e5020fe4",
                        column: x => x.recorded_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_child_age_policy_sources_users_revie_c814cd3744a8",
                        column: x => x.reviewer_controller_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_child_age_policy_sources_users_reviewer_human_id",
                        column: x => x.reviewer_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_constitution_sources",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    publication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode_code = table.Column<int>(type: "integer", nullable: false),
                    proposed_state_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    scope_ceiling = table.Column<int>(type: "integer", nullable: false),
                    evidence = table.Column<int>(type: "integer", nullable: false),
                    remedies = table.Column<int>(type: "integer", nullable: false),
                    independence = table.Column<int>(type: "integer", nullable: false),
                    exposure = table.Column<int>(type: "integer", nullable: false),
                    overload = table.Column<int>(type: "integer", nullable: false),
                    minimum_independent_reviewers = table.Column<int>(type: "integer", nullable: false),
                    protected_complaint_calendar_months = table.Column<int>(type: "integer", nullable: false),
                    maximum_review_interval_ticks = table.Column<long>(type: "bigint", nullable: false),
                    escalation_after_ticks = table.Column<long>(type: "bigint", nullable: false),
                    support_route = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    independent_redress_route = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    coordinator_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fallback_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    original_effect_id = table.Column<Guid>(type: "uuid", nullable: true),
                    original_snapshot_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    original_effect_binding_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    original_custody_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_constitution_sources", x => x.id);
                    table.CheckConstraint("ck_moderation_constitution_original_purpose", "(original_effect_id IS NULL AND original_snapshot_hash IS NULL AND original_effect_binding_hash IS NULL AND original_custody_until IS NULL) OR (original_effect_id IS NOT NULL AND original_snapshot_hash IS NOT NULL AND original_effect_binding_hash IS NOT NULL AND original_custody_until IS NOT NULL AND valid_until <= original_custody_until)");
                    table.CheckConstraint("ck_moderation_constitution_source_mode", "mode_code BETWEEN 1 AND 2");
                    table.CheckConstraint("ck_moderation_constitution_source_revision", "revision = 1 AND policy_revision > 0 AND writer_generation > 0");
                    table.CheckConstraint("ck_moderation_constitution_source_validity", "valid_until > recorded_at");
                    table.CheckConstraint("ck_moderation_constitution_substance", "evidence = 1 AND remedies = 1 AND independence = 1 AND exposure = 1 AND overload = 1 AND minimum_independent_reviewers BETWEEN 1 AND 32 AND protected_complaint_calendar_months >= 6 AND maximum_review_interval_ticks > 0 AND escalation_after_ticks > 0 AND coordinator_human_id <> fallback_human_id");
                    table.ForeignKey(
                        name: "fk_moderation_constitution_sources_moderation_effe_840a1efa260b",
                        column: x => x.original_effect_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_effect_intents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_constitution_sources_moderation_poli_92612eeaabff",
                        column: x => x.definition_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_constitution_sources_users_coordinator_human_id",
                        column: x => x.coordinator_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_constitution_sources_users_fallback_human_id",
                        column: x => x.fallback_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_constitution_sources_users_recorded_by_human_id",
                        column: x => x.recorded_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_consumer_measurement_challenges",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    installation_revision = table.Column<long>(type: "bigint", nullable: false),
                    installation_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    consumer_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    mode_code = table.Column<int>(type: "integer", nullable: false),
                    runtime_generation = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    inventory_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    publication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    binding_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    consumer_contract_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    nonce = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    prepared_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    prepared_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    original_effect_id = table.Column<Guid>(type: "uuid", nullable: true),
                    original_snapshot_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    original_effect_binding_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_consumer_measurement_challenges", x => x.id);
                    table.CheckConstraint("ck_moderation_measurement_challenge_original", "(original_effect_id IS NULL AND original_snapshot_hash IS NULL AND original_effect_binding_hash IS NULL) OR (original_effect_id IS NOT NULL AND original_snapshot_hash IS NOT NULL AND original_effect_binding_hash IS NOT NULL)");
                    table.CheckConstraint("ck_moderation_measurement_challenge_owner", "consumer_id <> '' AND mode_code = 1");
                    table.CheckConstraint("ck_moderation_measurement_challenge_source", "installation_revision = 1 AND installation_writer_generation > 0 AND writer_generation > installation_writer_generation AND revision = 1");
                    table.CheckConstraint("ck_moderation_measurement_challenge_validity", "valid_until > prepared_at");
                    table.ForeignKey(
                        name: "fk_moderation_consumer_measurement_challenges_mode_5c2aa9260dcb",
                        column: x => x.installation_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_consumer_installations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_consumer_measurement_challenges_mode_870ea54938fb",
                        column: x => x.host_binding_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_consumer_measurement_challenges_mode_b810f1addf9c",
                        column: x => x.definition_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_consumer_measurement_challenges_mode_fd5c52104ec4",
                        column: x => x.original_effect_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_effect_intents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_consumer_measurement_challenges_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "islamu_event",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_consumer_measurement_challenges_user_5338a174def0",
                        column: x => x.prepared_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_contribution_selections",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    effect_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision_revision = table.Column<long>(type: "bigint", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_revision = table.Column<long>(type: "bigint", nullable: false),
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    publication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    authored_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    selection_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    event_window_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_contribution_selections", x => x.id);
                    table.CheckConstraint("ck_moderation_contribution_selection_generations", "revision = 1 AND decision_revision > 1 AND case_revision > 0 AND policy_revision > 0 AND writer_generation > 0");
                    table.CheckConstraint("ck_moderation_contribution_selection_validity", "valid_until > recorded_at");
                    table.ForeignKey(
                        name: "fk_moderation_contribution_selections_moderation_cases_case_id",
                        column: x => x.case_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_contribution_selections_moderation_d_4d3027ba226e",
                        column: x => x.decision_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_decisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_contribution_selections_moderation_h_b96a47d9e72f",
                        column: x => x.host_binding_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_contribution_selections_moderation_p_ce54fda8efc0",
                        column: x => x.publication_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_contribution_selections_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "islamu_event",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_contribution_selections_users_author_30c555e1da22",
                        column: x => x.authored_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_deployment_sources",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    publication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode_code = table.Column<int>(type: "integer", nullable: false),
                    proposed_state_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    inventory_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    database_configuration_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    database_provider_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    database_provider_version = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    recorded_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_deployment_sources", x => x.id);
                    table.CheckConstraint("ck_moderation_deployment_source_mode", "mode_code BETWEEN 1 AND 2");
                    table.CheckConstraint("ck_moderation_deployment_source_revision", "revision = 1 AND policy_revision > 0 AND writer_generation > 0");
                    table.CheckConstraint("ck_moderation_deployment_source_validity", "valid_until > recorded_at");
                    table.ForeignKey(
                        name: "fk_moderation_deployment_sources_moderation_policy_350068a7b81f",
                        column: x => x.definition_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_deployment_sources_users_recorded_by_human_id",
                        column: x => x.recorded_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_event_rights_sources",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    publication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode_code = table.Column<int>(type: "integer", nullable: false),
                    proposed_state_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    scope_ceiling = table.Column<int>(type: "integer", nullable: false),
                    continuity = table.Column<int>(type: "integer", nullable: false),
                    consent = table.Column<int>(type: "integer", nullable: false),
                    missing_steward = table.Column<int>(type: "integer", nullable: false),
                    physical_jurisdiction = table.Column<int>(type: "integer", nullable: false),
                    settlement = table.Column<int>(type: "integer", nullable: false),
                    missing_settlement = table.Column<int>(type: "integer", nullable: false),
                    companions = table.Column<int>(type: "integer", nullable: false),
                    historical_commerce = table.Column<int>(type: "integer", nullable: false),
                    correction = table.Column<int>(type: "integer", nullable: false),
                    event_rights_grounds = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    continuity_grounds = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    premises_jurisdiction_grounds = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    settlement_mapping_grounds = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    reviewer_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewer_controller_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    continuity_mapping_owner_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    continuity_mapping_controller_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    settlement_mapping_owner_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    settlement_mapping_controller_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    governance_controller_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    controller_and_independence_grounds = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    rights_review_mandate_grounds = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    continuity_mapping_mandate_grounds = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    settlement_mapping_mandate_grounds = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    recorded_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_event_rights_sources", x => x.id);
                    table.CheckConstraint("ck_moderation_event_rights_independence", "reviewer_human_id <> recorded_by_human_id AND reviewer_human_id <> continuity_mapping_owner_human_id AND reviewer_human_id <> settlement_mapping_owner_human_id AND reviewer_controller_human_id <> governance_controller_human_id AND reviewer_controller_human_id <> continuity_mapping_controller_human_id AND reviewer_controller_human_id <> settlement_mapping_controller_human_id");
                    table.CheckConstraint("ck_moderation_event_rights_source_generation", "revision = 1 AND policy_revision > 0 AND writer_generation > 0");
                    table.CheckConstraint("ck_moderation_event_rights_source_mode", "mode_code BETWEEN 1 AND 2");
                    table.CheckConstraint("ck_moderation_event_rights_source_validity", "valid_until > recorded_at");
                    table.CheckConstraint("ck_moderation_event_rights_terms", "continuity = 1 AND consent = 1 AND missing_steward = 1 AND physical_jurisdiction = 1 AND settlement = 1 AND missing_settlement = 1 AND companions = 1 AND historical_commerce = 1 AND correction = 1");
                    table.ForeignKey(
                        name: "fk_moderation_event_rights_sources_moderation_poli_0ae2b866edd8",
                        column: x => x.definition_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_event_rights_sources_users_continuit_87945ce958ec",
                        column: x => x.continuity_mapping_controller_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_event_rights_sources_users_continuit_96a7d10ae0f2",
                        column: x => x.continuity_mapping_owner_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_event_rights_sources_users_governanc_9dfd013a72ce",
                        column: x => x.governance_controller_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_event_rights_sources_users_recorded_by_human_id",
                        column: x => x.recorded_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_event_rights_sources_users_reviewer__f1eaef654250",
                        column: x => x.reviewer_controller_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_event_rights_sources_users_reviewer_human_id",
                        column: x => x.reviewer_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_event_rights_sources_users_settlemen_19cd83ceb710",
                        column: x => x.settlement_mapping_owner_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_event_rights_sources_users_settlemen_cb1ed42759c3",
                        column: x => x.settlement_mapping_controller_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_measured_operations_sources",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    publication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode_code = table.Column<int>(type: "integer", nullable: false),
                    binding_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    proposed_state_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    evidence_json = table.Column<string>(type: "character varying(65536)", maxLength: 65536, nullable: false),
                    reviewer_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewer_controller_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorder_controller_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    original_effect_id = table.Column<Guid>(type: "uuid", nullable: true),
                    original_snapshot_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    original_effect_binding_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    original_custody_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_measured_operations_sources", x => x.id);
                    table.CheckConstraint("ck_moderation_measured_source_generation", "revision = 1 AND policy_revision > 0 AND writer_generation > 0");
                    table.CheckConstraint("ck_moderation_measured_source_independence", "reviewer_human_id <> recorded_by_human_id AND reviewer_controller_human_id <> recorder_controller_human_id AND reviewer_human_id <> recorder_controller_human_id AND recorded_by_human_id <> reviewer_controller_human_id");
                    table.CheckConstraint("ck_moderation_measured_source_mode", "mode_code BETWEEN 1 AND 2");
                    table.CheckConstraint("ck_moderation_measured_source_original", "(original_effect_id IS NULL AND original_snapshot_hash IS NULL AND original_effect_binding_hash IS NULL AND original_custody_until IS NULL) OR (original_effect_id IS NOT NULL AND original_snapshot_hash IS NOT NULL AND original_effect_binding_hash IS NOT NULL AND original_custody_until IS NOT NULL AND original_custody_until > recorded_at AND valid_until <= original_custody_until)");
                    table.CheckConstraint("ck_moderation_measured_source_validity", "valid_until > recorded_at");
                    table.ForeignKey(
                        name: "fk_moderation_measured_operations_sources_moderati_157f010e9a16",
                        column: x => x.original_effect_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_effect_intents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_measured_operations_sources_moderati_7877e23c7b10",
                        column: x => x.definition_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_measured_operations_sources_users_re_3368d491b753",
                        column: x => x.recorder_controller_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_measured_operations_sources_users_re_5ad57df9b7a9",
                        column: x => x.reviewer_controller_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_measured_operations_sources_users_re_b983c85c27c5",
                        column: x => x.recorded_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_measured_operations_sources_users_re_c029fda4d9e4",
                        column: x => x.reviewer_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_notice_disclosure_approvals",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effect_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision_revision = table.Column<long>(type: "bigint", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_revision = table.Column<long>(type: "bigint", nullable: false),
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    publication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contribution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    selection_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    rule_meaning = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    reason_meaning = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    facts_json = table.Column<string>(type: "character varying(131072)", maxLength: 131072, nullable: false),
                    remaining_actions = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    recourse = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    automation = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    retention = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    content_representatives_json = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    approved_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    retain_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_by_human_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_notice_disclosure_approvals", x => x.id);
                    table.CheckConstraint("ck_moderation_notice_disclosure_generations", "decision_revision > 1 AND case_revision > 0 AND policy_revision > 0 AND writer_generation > 0");
                    table.CheckConstraint("ck_moderation_notice_disclosure_retention", "retain_until > approved_at");
                    table.CheckConstraint("ck_moderation_notice_disclosure_revocation", "(revision = 1 AND revoked_at IS NULL AND revoked_by_human_id IS NULL) OR (revision = 2 AND revoked_at IS NOT NULL AND revoked_by_human_id IS NOT NULL AND revoked_at >= approved_at)");
                    table.CheckConstraint("ck_moderation_notice_disclosure_validity", "valid_until > approved_at");
                    table.ForeignKey(
                        name: "fk_moderation_notice_disclosure_approvals_moderati_0105ac0a012c",
                        column: x => x.host_binding_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_notice_disclosure_approvals_moderati_28d13d6f025d",
                        column: x => x.case_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_notice_disclosure_approvals_moderati_569dca76ae1d",
                        column: x => x.decision_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_decisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_notice_disclosure_approvals_moderati_74fa5f68b6e3",
                        column: x => x.publication_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_notice_disclosure_approvals_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "islamu_event",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_notice_disclosure_approvals_users_ap_c4729e964995",
                        column: x => x.approved_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_notice_disclosure_approvals_users_re_f3744ce106b9",
                        column: x => x.revoked_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_policy_publication_candidates",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expected_revision = table.Column<long>(type: "bigint", nullable: false),
                    previous_publication_id = table.Column<Guid>(type: "uuid", nullable: true),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    solution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    jurisdiction_tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prepared_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    proposed_state_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    authority_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    authority_revision = table.Column<long>(type: "bigint", nullable: false),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    prepared_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_policy_publication_candidates", x => x.id);
                    table.CheckConstraint("ck_moderation_publication_candidate_history", "(expected_revision = 0 AND previous_publication_id IS NULL) OR (expected_revision > 0 AND previous_publication_id IS NOT NULL)");
                    table.CheckConstraint("ck_moderation_publication_candidate_identity", "id <> definition_id");
                    table.CheckConstraint("ck_moderation_publication_candidate_revision", "expected_revision >= 0 AND expected_revision < 9223372036854775807 AND revision = 1");
                    table.CheckConstraint("ck_moderation_publication_candidate_source", "authority_revision > 0 AND authority_revision < 9223372036854775806 AND writer_generation = authority_revision + 1");
                    table.CheckConstraint("ck_moderation_publication_candidate_validity", "valid_until > prepared_at");
                    table.ForeignKey(
                        name: "fk_moderation_policy_publication_candidates_modera_4e398a05e46c",
                        columns: x => new { x.policy_id, x.previous_publication_id },
                        principalSchema: "islamu_event",
                        principalTable: "moderation_policy_publications",
                        principalColumns: new[] { "policy_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_policy_publication_candidates_modera_959868388ea8",
                        column: x => x.authority_source_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_qualification_epochs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_policy_publication_candidates_modera_a339f7737403",
                        column: x => x.host_binding_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_policy_publication_candidates_modera_c49a175d3281",
                        columns: x => new { x.policy_id, x.definition_id },
                        principalSchema: "islamu_event",
                        principalTable: "moderation_policy_publications",
                        principalColumns: new[] { "policy_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_policy_publication_candidates_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "islamu_event",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_policy_publication_candidates_users__0667b1935780",
                        column: x => x.prepared_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_policy_selections",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    jurisdiction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    jurisdiction_tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    latest_publication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    active_publication_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_policy_selections", x => x.id);
                    table.CheckConstraint("ck_moderation_selection_revision", "revision > 0");
                    table.ForeignKey(
                        name: "fk_moderation_policy_selections_moderation_policy__74f9395322c2",
                        columns: x => new { x.id, x.active_publication_id },
                        principalSchema: "islamu_event",
                        principalTable: "moderation_policy_publications",
                        principalColumns: new[] { "policy_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_policy_selections_moderation_policy__ab9bc0b3b055",
                        columns: x => new { x.id, x.latest_publication_id },
                        principalSchema: "islamu_event",
                        principalTable: "moderation_policy_publications",
                        principalColumns: new[] { "policy_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_publication_classes",
                schema: "islamu_event",
                columns: table => new
                {
                    publication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    meaning = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    ground = table.Column<int>(type: "integer", nullable: false),
                    basis = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_publication_classes", x => new { x.publication_id, x.code, x.revision });
                    table.ForeignKey(
                        name: "fk_moderation_publication_classes_moderation_polic_238af6b1d602",
                        column: x => x.publication_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_publication_evidence",
                schema: "islamu_event",
                columns: table => new
                {
                    publication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requirement_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    approval_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approval_revision = table.Column<long>(type: "bigint", nullable: true),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_revision = table.Column<long>(type: "bigint", nullable: false),
                    writer_generation = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_publication_evidence", x => new { x.publication_id, x.requirement_digest });
                    table.CheckConstraint("ck_moderation_publication_evidence", "source_revision > 0 AND ((approval_id IS NULL AND approval_revision IS NULL) OR (approval_id IS NOT NULL AND approval_revision > 0))");
                    table.ForeignKey(
                        name: "fk_moderation_publication_evidence_moderation_poli_3850f739a93c",
                        column: x => x.publication_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_publication_modes",
                schema: "islamu_event",
                columns: table => new
                {
                    publication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_publication_modes", x => new { x.publication_id, x.mode });
                    table.CheckConstraint("ck_moderation_publication_mode", "mode BETWEEN 1 AND 2");
                    table.ForeignKey(
                        name: "fk_moderation_publication_modes_moderation_policy__79853e024b2f",
                        column: x => x.publication_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_recognition_privacy_sources",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    publication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode_code = table.Column<int>(type: "integer", nullable: false),
                    proposed_state_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    purposes = table.Column<int>(type: "integer", nullable: false),
                    scope_ceiling = table.Column<int>(type: "integer", nullable: false),
                    linkage = table.Column<int>(type: "integer", nullable: false),
                    ordinary_erasure = table.Column<int>(type: "integer", nullable: false),
                    correction = table.Column<int>(type: "integer", nullable: false),
                    catastrophic_disposition = table.Column<int>(type: "integer", nullable: false),
                    key_obligations = table.Column<int>(type: "integer", nullable: false),
                    protected_remedy_calendar_months = table.Column<int>(type: "integer", nullable: false),
                    escalation_after_ticks = table.Column<long>(type: "bigint", nullable: false),
                    lawful_request_deadline_ticks = table.Column<long>(type: "bigint", nullable: false),
                    maximum_backup_horizon_ticks = table.Column<long>(type: "bigint", nullable: false),
                    backup_safety_margin_ticks = table.Column<long>(type: "bigint", nullable: false),
                    privacy_owner_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    privacy_controller_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recovery_owner_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recovery_controller_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    governance_controller_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    identity_and_independence_basis = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    privacy_legal_mandate_basis = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    recovery_mandate_basis = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    recorded_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_recognition_privacy_sources", x => x.id);
                    table.CheckConstraint("ck_moderation_recognition_privacy_independence", "privacy_owner_human_id <> recorded_by_human_id AND recovery_owner_human_id <> recorded_by_human_id AND privacy_owner_human_id <> recovery_owner_human_id AND privacy_controller_human_id <> recovery_controller_human_id AND privacy_controller_human_id <> governance_controller_human_id AND recovery_controller_human_id <> governance_controller_human_id");
                    table.CheckConstraint("ck_moderation_recognition_privacy_source_generation", "revision = 1 AND policy_revision > 0 AND writer_generation > 0");
                    table.CheckConstraint("ck_moderation_recognition_privacy_source_mode", "mode_code BETWEEN 1 AND 2");
                    table.CheckConstraint("ck_moderation_recognition_privacy_source_validity", "valid_until > recorded_at");
                    table.CheckConstraint("ck_moderation_recognition_privacy_terms", "purposes BETWEEN 1 AND 3 AND linkage = 1 AND ordinary_erasure = 1 AND correction = 1 AND catastrophic_disposition = 1 AND key_obligations = 1 AND protected_remedy_calendar_months BETWEEN 1 AND 1200 AND escalation_after_ticks > 0 AND lawful_request_deadline_ticks > escalation_after_ticks AND maximum_backup_horizon_ticks > 0 AND backup_safety_margin_ticks > 0");
                    table.ForeignKey(
                        name: "fk_moderation_recognition_privacy_sources_moderati_abf1b1c5d4b1",
                        column: x => x.definition_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_recognition_privacy_sources_users_go_3244dcbad7aa",
                        column: x => x.governance_controller_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_recognition_privacy_sources_users_pr_1eb5d73039dd",
                        column: x => x.privacy_owner_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_recognition_privacy_sources_users_pr_56b01dee3380",
                        column: x => x.privacy_controller_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_recognition_privacy_sources_users_re_03f729807d51",
                        column: x => x.recorded_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_recognition_privacy_sources_users_re_1ac5576f419f",
                        column: x => x.recovery_owner_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_recognition_privacy_sources_users_re_5ca6b7709f1c",
                        column: x => x.recovery_controller_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_qualification_policy_adoptions",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_revision = table.Column<long>(type: "bigint", nullable: false),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_revision = table.Column<long>(type: "bigint", nullable: false),
                    maximum_reviewer_lifetime_ticks = table.Column<long>(type: "bigint", nullable: false),
                    maximum_urgency_lifetime_ticks = table.Column<long>(type: "bigint", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    adopted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    revoked_by_human_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_qualification_policy_adoptions", x => x.id);
                    table.CheckConstraint("ck_moderation_qualification_policy_adoption_generation", "id <> source_id AND source_revision > 0 AND case_revision > 0 AND writer_generation > 0");
                    table.CheckConstraint("ck_moderation_qualification_policy_adoption_limits", "maximum_reviewer_lifetime_ticks > 0 AND maximum_reviewer_lifetime_ticks < 9223372036854775807 AND maximum_urgency_lifetime_ticks > 0 AND maximum_urgency_lifetime_ticks < 9223372036854775807");
                    table.CheckConstraint("ck_moderation_qualification_policy_adoption_revocation", "(revision = 1 AND revoked_at IS NULL AND revoked_by_human_id IS NULL) OR (revision = 2 AND revoked_at IS NOT NULL AND revoked_by_human_id IS NOT NULL AND revoked_at >= adopted_at)");
                    table.CheckConstraint("ck_moderation_qualification_policy_adoption_validity", "valid_until > adopted_at");
                    table.ForeignKey(
                        name: "fk_moderation_qualification_policy_adoptions_moder_cf4e4a3d694c",
                        columns: x => new { x.source_id, x.host_binding_id, x.trust_domain_id, x.tenant_id, x.case_id },
                        principalSchema: "islamu_event",
                        principalTable: "moderation_qualification_policy_sources",
                        principalColumns: new[] { "id", "host_binding_id", "trust_domain_id", "tenant_id", "case_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_qualification_policy_adoptions_users_5b3a4a7e2848",
                        column: x => x.revoked_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_qualification_policy_adoptions_users_f2d1c4c9540f",
                        column: x => x.adopted_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_recognition_disposition_acceptance",
                schema: "islamu_event",
                columns: table => new
                {
                    disposition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<int>(type: "integer", nullable: false),
                    human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    accepted_revision = table.Column<long>(type: "bigint", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    request_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_recognition_disposition_acceptance", x => new { x.disposition_id, x.role });
                    table.ForeignKey(
                        name: "fk_moderation_recognition_disposition_acceptance_m_5fb00b1eae27",
                        column: x => x.disposition_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_recognition_disposition",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_recognition_key_lifecycle_key",
                schema: "islamu_event",
                columns: table => new
                {
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    key_generation = table.Column<long>(type: "bigint", nullable: false),
                    verification_tag = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    original_obligations_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    required_erasure_head = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_recognition_key_lifecycle_key", x => new { x.trust_domain_id, x.key_id });
                    table.ForeignKey(
                        name: "fk_moderation_recognition_key_lifecycle_key_modera_87ce8044ba70",
                        column: x => x.trust_domain_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_recognition_key_lifecycle",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_case_narratives",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reporter_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_version = table.Column<Guid>(type: "uuid", nullable: false),
                    material_fact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    adoption_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    adoption_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    qualification_epoch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    qualification_revision = table.Column<long>(type: "bigint", nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    purge_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    protected_material = table.Column<byte[]>(type: "bytea", maxLength: 131072, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_case_narratives", x => x.id);
                    table.CheckConstraint("ck_case_narrative_finite_custody", "purge_at > captured_at");
                    table.CheckConstraint("ck_case_narrative_generations", "policy_writer_generation > 0 AND adoption_writer_generation > policy_writer_generation AND qualification_revision >= adoption_writer_generation");
                    table.ForeignKey(
                        name: "fk_moderation_case_narratives_moderation_case_narr_514a35efa48d",
                        columns: x => new { x.adoption_id, x.host_binding_id, x.trust_domain_id, x.tenant_id, x.policy_source_id },
                        principalSchema: "islamu_event",
                        principalTable: "moderation_case_narrative_adoptions",
                        principalColumns: new[] { "id", "host_binding_id", "trust_domain_id", "tenant_id", "source_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_narratives_moderation_case_targ_5de7a99c5be8",
                        column: x => x.receipt_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_case_target_receipts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_narratives_moderation_case_targ_c89810a70b3c",
                        column: x => x.source_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_case_target_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_narratives_moderation_cases_case_id",
                        column: x => x.case_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_narratives_moderation_qualifica_265aab9028b5",
                        column: x => x.qualification_epoch_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_qualification_epochs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_narratives_users_reporter_user_id",
                        column: x => x.reporter_user_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_case_attribution_sources",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    generation = table.Column<long>(type: "bigint", nullable: false),
                    occurrence_case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurrence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurrence_case_revision = table.Column<long>(type: "bigint", nullable: false),
                    evidence_case_revision = table.Column<long>(type: "bigint", nullable: false),
                    evidence_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evidence_source_version = table.Column<Guid>(type: "uuid", nullable: false),
                    material_fact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    investigation_source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    investigation_source_revision = table.Column<long>(type: "bigint", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    qualification_epoch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    qualification_revision = table.Column<long>(type: "bigint", nullable: false),
                    basis = table.Column<int>(type: "integer", nullable: false),
                    subject_kind = table.Column<int>(type: "integer", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    qualified_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    qualifier_role_grant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    qualifier_platform_grant = table.Column<bool>(type: "boolean", nullable: false),
                    read_grant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    read_grant_revision = table.Column<long>(type: "bigint", nullable: false),
                    lifetime_policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lifetime_policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    native_responsibility_digest = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false),
                    qualification_facts_digest = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false),
                    qualified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    withdrawn_by_human_id = table.Column<Guid>(type: "uuid", nullable: true),
                    withdrawal_operation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_case_attribution_sources", x => x.id);
                    table.CheckConstraint("ck_moderation_attribution_basis", "(investigation_source_id IS NULL AND investigation_source_revision IS NULL AND ((basis = 1 AND subject_kind = 1 AND subject_id <> qualified_by_human_id) OR (basis = 2 AND subject_kind = 4) OR (basis = 3 AND subject_kind = 3) OR (basis = 4 AND subject_kind = 2))) OR (basis = 5 AND subject_kind = 1 AND subject_id <> qualified_by_human_id AND investigation_source_id IS NOT NULL AND investigation_source_revision IS NOT NULL AND investigation_source_revision = 1)");
                    table.CheckConstraint("ck_moderation_attribution_evidence", "generation > 0 AND case_id <> occurrence_case_id AND evidence_case_revision > 0 AND occurrence_case_revision >= evidence_case_revision AND qualification_revision > 0 AND read_grant_revision > 0 AND lifetime_policy_revision > 0");
                    table.CheckConstraint("ck_moderation_attribution_validity", "valid_until > qualified_at AND ((revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL AND withdrawal_operation_id IS NULL) OR (revision = 2 AND withdrawn_at >= qualified_at AND withdrawn_by_human_id IS NOT NULL AND withdrawal_operation_id IS NOT NULL))");
                    table.ForeignKey(
                        name: "fk_moderation_case_attribution_sources_moderation__3377e39652da",
                        column: x => x.read_grant_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_case_read_grants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_attribution_sources_moderation__714eb71a0627",
                        column: x => x.evidence_source_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_case_target_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_attribution_sources_moderation__fe8c022b3ca1",
                        column: x => x.occurrence_case_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_attribution_sources_moderation_cases_case_id",
                        column: x => x.case_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_attribution_sources_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "islamu_event",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_case_read_grant_revocation_receipts",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    grant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expected_revision = table.Column<long>(type: "bigint", nullable: false),
                    administrator_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    administrator_role_grant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    administrator_platform_grant = table.Column<bool>(type: "boolean", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_case_read_grant_revocation_receipts", x => x.id);
                    table.CheckConstraint("ck_moderation_read_grant_revocation_revision", "expected_revision = 1");
                    table.ForeignKey(
                        name: "fk_moderation_case_read_grant_revocation_receipts__922fd263640a",
                        column: x => x.grant_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_case_read_grants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_read_grant_revocation_receipts__94f9d8744b7e",
                        column: x => x.tenant_id,
                        principalSchema: "islamu_event",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_read_grant_revocation_receipts__984cef9404d9",
                        column: x => x.administrator_user_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_content_responses",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    notice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effect_id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    representative_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assignment_revision = table.Column<long>(type: "bigint", nullable: false),
                    snapshot_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    request_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    retain_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    protected_statement = table.Column<byte[]>(type: "bytea", maxLength: 16384, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_content_responses", x => x.id);
                    table.CheckConstraint("ck_content_response_assignment", "assignment_revision > 0");
                    table.CheckConstraint("ck_content_response_finite_purpose", "retain_until > submitted_at");
                    table.ForeignKey(
                        name: "fk_moderation_content_responses_moderation_cases_case_id",
                        column: x => x.case_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_content_responses_moderation_notice__27d2a3c3643e",
                        column: x => x.effect_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_notice_sources",
                        principalColumn: "effect_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_content_responses_moderation_notices_notice_id",
                        column: x => x.notice_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_notices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_content_responses_users_representative_human_id",
                        column: x => x.representative_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_notice_notification_receipts",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    notice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effect_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_kind = table.Column<int>(type: "integer", nullable: false),
                    acknowledged_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    snapshot_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    contestation_calendar_months = table.Column<int>(type: "integer", nullable: false),
                    notified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    remedy_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    retain_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_notice_notification_receipts", x => x.id);
                    table.CheckConstraint("ck_moderation_notice_notification_calendar", "contestation_calendar_months >= 0 AND contestation_calendar_months <= 120");
                    table.CheckConstraint("ck_moderation_notice_notification_subject", "subject_kind IN (1, 4) AND (subject_kind <> 1 OR acknowledged_by_human_id = subject_id)");
                    table.CheckConstraint("ck_moderation_notice_notification_time", "remedy_until >= notified_at AND retain_until >= remedy_until AND retain_until > notified_at");
                    table.ForeignKey(
                        name: "fk_moderation_notice_notification_receipts_moderat_4069fc0ec751",
                        column: x => x.effect_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_notice_sources",
                        principalColumn: "effect_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_notice_notification_receipts_moderat_fa01cbb89e02",
                        column: x => x.notice_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_notices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_notice_notification_receipts_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "islamu_event",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_case_intake_acknowledgements",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caller_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_revision = table.Column<long>(type: "bigint", nullable: false),
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_revision = table.Column<long>(type: "bigint", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evidence_case_revision = table.Column<long>(type: "bigint", nullable: false),
                    budget_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    budget_adoption_id = table.Column<Guid>(type: "uuid", nullable: false),
                    budget_source_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    budget_adoption_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    request_digest = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_case_intake_acknowledgements", x => x.id);
                    table.CheckConstraint("ck_moderation_case_acknowledgement_revisions", "case_revision > 0 AND receipt_revision = 2 AND evidence_case_revision > 0 AND evidence_case_revision <= case_revision AND budget_source_writer_generation > 0 AND budget_adoption_writer_generation > budget_source_writer_generation AND budget_adoption_writer_generation < 9223372036854775807 AND budget_source_id <> budget_adoption_id");
                    table.ForeignKey(
                        name: "fk_moderation_case_intake_acknowledgements_moderat_595a0ac43450",
                        column: x => x.case_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_intake_acknowledgements_moderat_61df15e26135",
                        columns: x => new { x.budget_adoption_id, x.trust_domain_id, x.tenant_id, x.budget_source_id },
                        principalSchema: "islamu_event",
                        principalTable: "moderation_operational_budget_adoptions",
                        principalColumns: new[] { "id", "trust_domain_id", "tenant_id", "source_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_intake_acknowledgements_moderat_95ee4aded5ae",
                        column: x => x.receipt_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_case_target_receipts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_intake_acknowledgements_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "islamu_event",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_child_age_policy_adoptions",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_revision = table.Column<long>(type: "bigint", nullable: false),
                    source_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    adopted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    withdrawn_by_human_id = table.Column<Guid>(type: "uuid", nullable: true),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_child_age_policy_adoptions", x => x.id);
                    table.CheckConstraint("ck_moderation_child_age_adoption_generation", "source_revision = 1 AND source_writer_generation > 0 AND writer_generation > source_writer_generation");
                    table.CheckConstraint("ck_moderation_child_age_adoption_validity", "valid_until > adopted_at");
                    table.CheckConstraint("ck_moderation_child_age_adoption_withdrawal", "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR (revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at >= adopted_at)");
                    table.ForeignKey(
                        name: "fk_moderation_child_age_policy_adoptions_moderatio_41f61d12508c",
                        column: x => x.source_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_child_age_policy_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_child_age_policy_adoptions_users_ado_6a3811a8f757",
                        column: x => x.adopted_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_child_age_policy_adoptions_users_wit_f448d83daf04",
                        column: x => x.withdrawn_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_constitution_adoptions",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_revision = table.Column<long>(type: "bigint", nullable: false),
                    source_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    adopted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    withdrawn_by_human_id = table.Column<Guid>(type: "uuid", nullable: true),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_constitution_adoptions", x => x.id);
                    table.CheckConstraint("ck_moderation_constitution_adoption_generation", "source_revision = 1 AND source_writer_generation > 0 AND writer_generation > source_writer_generation");
                    table.CheckConstraint("ck_moderation_constitution_adoption_validity", "valid_until > adopted_at");
                    table.CheckConstraint("ck_moderation_constitution_adoption_withdrawal", "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR (revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at >= adopted_at)");
                    table.ForeignKey(
                        name: "fk_moderation_constitution_adoptions_moderation_co_dee0ba9afecc",
                        column: x => x.source_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_constitution_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_constitution_adoptions_users_adopted_by_human_id",
                        column: x => x.adopted_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_constitution_adoptions_users_withdra_65860bd6adf2",
                        column: x => x.withdrawn_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_constitution_reviewers",
                schema: "islamu_event",
                columns: table => new
                {
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    availability_id = table.Column<Guid>(type: "uuid", nullable: false),
                    availability_revision = table.Column<long>(type: "bigint", nullable: false),
                    availability_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    reviewer_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    controller_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    identity_basis = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    qualification_basis = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    independence_basis = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_constitution_reviewers", x => new { x.source_id, x.ordinal });
                    table.CheckConstraint("ck_moderation_constitution_reviewer_generation", "ordinal >= 0 AND ordinal < 32 AND availability_revision = 1 AND availability_writer_generation > 0");
                    table.ForeignKey(
                        name: "fk_moderation_constitution_reviewers_moderation_co_24dbe1d16dc3",
                        column: x => x.availability_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_constitution_availability",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_constitution_reviewers_moderation_co_5e8fcec8746e",
                        column: x => x.source_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_constitution_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_constitution_reviewers_users_controller_human_id",
                        column: x => x.controller_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_constitution_reviewers_users_reviewer_human_id",
                        column: x => x.reviewer_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_consumer_measurement_receipts",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    issuer_enrollment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    issuer_key_id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    signed_token = table.Column<string>(type: "character varying(65536)", maxLength: 65536, nullable: false),
                    token_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    accepted_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_consumer_measurement_receipts", x => x.id);
                    table.CheckConstraint("ck_moderation_measurement_receipt_generation", "writer_generation > 0 AND writer_generation < 9223372036854775807 AND revision = 1");
                    table.CheckConstraint("ck_moderation_measurement_receipt_validity", "valid_until > accepted_at");
                    table.ForeignKey(
                        name: "fk_moderation_consumer_measurement_receipts_modera_31a4620a0772",
                        column: x => x.challenge_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_consumer_measurement_challenges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_consumer_measurement_receipts_modera_326d73031a00",
                        column: x => x.host_binding_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_consumer_measurement_receipts_modera_78c12111e0e2",
                        column: x => x.issuer_enrollment_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_measurement_issuer_enrollments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_consumer_measurement_receipts_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "islamu_event",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_consumer_measurement_receipts_users__797b90aba6da",
                        column: x => x.accepted_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_contribution_bindings",
                schema: "islamu_event",
                columns: table => new
                {
                    selection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    effect_kind = table.Column<int>(type: "integer", nullable: true),
                    effect_mode = table.Column<int>(type: "integer", nullable: true),
                    effect_target = table.Column<int>(type: "integer", nullable: true),
                    capability = table.Column<int>(type: "integer", nullable: true),
                    execution = table.Column<int>(type: "integer", nullable: true),
                    strategy = table.Column<int>(type: "integer", nullable: true),
                    scope_kind = table.Column<int>(type: "integer", nullable: false),
                    scope_trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_solution_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scope_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scope_tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scope_community_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scope_resource_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scope_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subject_kind = table.Column<int>(type: "integer", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_revision = table.Column<long>(type: "bigint", nullable: false),
                    operation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    window_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_contribution_bindings", x => new { x.selection_id, x.ordinal });
                    table.CheckConstraint("ck_moderation_contribution_binding_meaning", "(kind = 1 AND effect_kind IS NOT NULL AND effect_mode IS NOT NULL AND effect_target IS NOT NULL AND execution IS NOT NULL AND strategy IS NULL) OR (kind = 2 AND effect_kind IS NULL AND effect_mode IS NULL AND effect_target IS NULL AND capability IS NULL AND execution IS NULL)");
                    table.CheckConstraint("ck_moderation_contribution_binding_partition", "ordinal >= 0 AND ordinal < 64 AND target_revision > 0");
                    table.CheckConstraint("ck_moderation_contribution_bindings_scope_scope", "scope_kind IS NOT NULL AND ((scope_kind = 1 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 2 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 3 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 4 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 5 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NOT NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 6 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_resource_id IS NOT NULL AND scope_event_id IS NULL) OR (scope_kind = 7 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NOT NULL AND scope_event_id <> '00000000-0000-0000-0000-000000000000'))");
                    table.ForeignKey(
                        name: "fk_moderation_contribution_bindings_moderation_con_717de591fd68",
                        column: x => x.selection_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_contribution_selections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_profile_adoptions",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    selection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    selection_revision = table.Column<long>(type: "bigint", nullable: false),
                    selection_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    publication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_revision = table.Column<long>(type: "bigint", nullable: false),
                    mode_code = table.Column<int>(type: "integer", nullable: false),
                    prerequisite_mask = table.Column<int>(type: "integer", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    adopted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_by_human_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_profile_adoptions", x => x.id);
                    table.CheckConstraint("ck_moderation_profile_adoption_mode", "mode_code BETWEEN 1 AND 2");
                    table.CheckConstraint("ck_moderation_profile_adoption_revision", "selection_revision = 1 AND selection_writer_generation > 0 AND case_revision > 0 AND writer_generation > 0");
                    table.CheckConstraint("ck_moderation_profile_adoption_revocation", "(revision = 1 AND revoked_at IS NULL AND revoked_by_human_id IS NULL) OR (revision = 2 AND revoked_at IS NOT NULL AND revoked_by_human_id IS NOT NULL AND revoked_at >= adopted_at)");
                    table.CheckConstraint("ck_moderation_profile_adoption_validity", "valid_until > adopted_at");
                    table.ForeignKey(
                        name: "fk_moderation_profile_adoptions_moderation_contrib_d3e1b38f6fb3",
                        column: x => x.selection_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_contribution_selections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_profile_adoptions_moderation_policy__8ce795cb3b3f",
                        column: x => x.publication_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_profile_adoptions_users_adopted_by_human_id",
                        column: x => x.adopted_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_profile_adoptions_users_revoked_by_human_id",
                        column: x => x.revoked_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_deployment_adoptions",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_revision = table.Column<long>(type: "bigint", nullable: false),
                    source_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    adopted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    withdrawn_by_human_id = table.Column<Guid>(type: "uuid", nullable: true),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_deployment_adoptions", x => x.id);
                    table.CheckConstraint("ck_moderation_deployment_adoption_generation", "source_revision = 1 AND source_writer_generation > 0 AND writer_generation > source_writer_generation");
                    table.CheckConstraint("ck_moderation_deployment_adoption_validity", "valid_until > adopted_at");
                    table.CheckConstraint("ck_moderation_deployment_adoption_withdrawal", "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR (revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at >= adopted_at)");
                    table.ForeignKey(
                        name: "fk_moderation_deployment_adoptions_moderation_depl_b3ce3670392d",
                        column: x => x.source_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_deployment_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_deployment_adoptions_users_adopted_by_human_id",
                        column: x => x.adopted_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_deployment_adoptions_users_withdrawn_by_human_id",
                        column: x => x.withdrawn_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_deployment_consumers",
                schema: "islamu_event",
                columns: table => new
                {
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    consumer_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    binding_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    contract_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    installation_revision = table.Column<long>(type: "bigint", nullable: false),
                    installation_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    runtime_generation = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    product_artifact_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_deployment_consumers", x => new { x.source_id, x.ordinal });
                    table.CheckConstraint("ck_moderation_deployment_consumer_revision", "ordinal >= 0 AND ordinal < 256 AND installation_revision = 1 AND installation_writer_generation > 0");
                    table.ForeignKey(
                        name: "fk_moderation_deployment_consumers_moderation_cons_d8873eaec17f",
                        column: x => x.installation_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_consumer_installations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_deployment_consumers_moderation_depl_a71525ee4cf0",
                        column: x => x.source_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_deployment_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_event_rights_adoptions",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_revision = table.Column<long>(type: "bigint", nullable: false),
                    source_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    adopted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    withdrawn_by_human_id = table.Column<Guid>(type: "uuid", nullable: true),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_event_rights_adoptions", x => x.id);
                    table.CheckConstraint("ck_moderation_event_rights_adoption_generation", "source_revision = 1 AND source_writer_generation > 0 AND writer_generation > source_writer_generation");
                    table.CheckConstraint("ck_moderation_event_rights_adoption_validity", "valid_until > adopted_at");
                    table.CheckConstraint("ck_moderation_event_rights_adoption_withdrawal", "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR (revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at >= adopted_at)");
                    table.ForeignKey(
                        name: "fk_moderation_event_rights_adoptions_moderation_ev_d3c2d66b520f",
                        column: x => x.source_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_event_rights_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_event_rights_adoptions_users_adopted_by_human_id",
                        column: x => x.adopted_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_event_rights_adoptions_users_withdra_adb1ae441478",
                        column: x => x.withdrawn_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_measured_operations_acceptances",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_revision = table.Column<long>(type: "bigint", nullable: false),
                    source_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    accepted_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    withdrawn_by_human_id = table.Column<Guid>(type: "uuid", nullable: true),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_measured_operations_acceptances", x => x.id);
                    table.CheckConstraint("ck_moderation_measured_acceptance_generation", "source_revision = 1 AND source_writer_generation > 0 AND writer_generation > source_writer_generation");
                    table.CheckConstraint("ck_moderation_measured_acceptance_validity", "valid_until > accepted_at");
                    table.CheckConstraint("ck_moderation_measured_acceptance_withdrawal", "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR (revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at >= accepted_at)");
                    table.ForeignKey(
                        name: "fk_moderation_measured_operations_acceptances_mode_7f6ecb0e1982",
                        column: x => x.source_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_measured_operations_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_measured_operations_acceptances_user_33bf05bc272d",
                        column: x => x.accepted_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_measured_operations_acceptances_user_825f1a767e6f",
                        column: x => x.withdrawn_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_publication_reasons",
                schema: "islamu_event",
                columns: table => new
                {
                    publication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    meaning = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    class_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    class_revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_publication_reasons", x => new { x.publication_id, x.code, x.revision });
                    table.ForeignKey(
                        name: "fk_moderation_publication_reasons_moderation_polic_08a2a735c907",
                        column: x => x.publication_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_publication_reasons_moderation_publi_2a9c8bcdc7b3",
                        columns: x => new { x.publication_id, x.class_code, x.class_revision },
                        principalSchema: "islamu_event",
                        principalTable: "moderation_publication_classes",
                        principalColumns: new[] { "publication_id", "code", "revision" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_recognition_privacy_adoptions",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_revision = table.Column<long>(type: "bigint", nullable: false),
                    source_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    adopted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    withdrawn_by_human_id = table.Column<Guid>(type: "uuid", nullable: true),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_recognition_privacy_adoptions", x => x.id);
                    table.CheckConstraint("ck_moderation_recognition_privacy_adoption_generation", "source_revision = 1 AND source_writer_generation > 0 AND writer_generation > source_writer_generation");
                    table.CheckConstraint("ck_moderation_recognition_privacy_adoption_validity", "valid_until > adopted_at");
                    table.CheckConstraint("ck_moderation_recognition_privacy_adoption_withdrawal", "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR (revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at >= adopted_at)");
                    table.ForeignKey(
                        name: "fk_moderation_recognition_privacy_adoptions_modera_0f84dc31dceb",
                        column: x => x.source_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_recognition_privacy_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_recognition_privacy_adoptions_users__90050dae1410",
                        column: x => x.withdrawn_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_recognition_privacy_adoptions_users__a2a1d444e352",
                        column: x => x.adopted_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_recognition_privacy_retention",
                schema: "islamu_event",
                columns: table => new
                {
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    record_class = table.Column<int>(type: "integer", nullable: false),
                    maximum_retention_ticks = table.Column<long>(type: "bigint", nullable: false),
                    hold = table.Column<int>(type: "integer", nullable: false),
                    maximum_hold_ticks = table.Column<long>(type: "bigint", nullable: false),
                    hold_review_within_ticks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_recognition_privacy_retention", x => new { x.source_id, x.record_class });
                    table.CheckConstraint("ck_moderation_recognition_privacy_hold", "(hold = 1 AND maximum_hold_ticks = 0 AND hold_review_within_ticks = 0) OR (hold = 2 AND record_class IN (3, 4) AND maximum_hold_ticks > 0 AND hold_review_within_ticks > 0 AND hold_review_within_ticks <= maximum_hold_ticks)");
                    table.CheckConstraint("ck_moderation_recognition_privacy_retention", "record_class BETWEEN 1 AND 4 AND maximum_retention_ticks > 0");
                    table.ForeignKey(
                        name: "fk_moderation_recognition_privacy_retention_modera_cd24217c6c22",
                        column: x => x.source_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_recognition_privacy_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_participant_incident_responsibility_sources",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurrence_case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurrence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurrence_case_revision = table.Column<long>(type: "bigint", nullable: false),
                    evidence_case_revision = table.Column<long>(type: "bigint", nullable: false),
                    evidence_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evidence_source_version = table.Column<Guid>(type: "uuid", nullable: false),
                    material_fact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_narrative_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_narrative_purge_at = table.Column<long>(type: "bigint", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_version = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_generation = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_window_from = table.Column<long>(type: "bigint", nullable: false),
                    incident_window_until = table.Column<long>(type: "bigint", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_generation = table.Column<Guid>(type: "uuid", nullable: false),
                    participant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    participant_generation = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_kind = table.Column<int>(type: "integer", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assessor_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assessor_role_grant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    read_issuer_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    read_grant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    read_grant_revision = table.Column<long>(type: "bigint", nullable: false),
                    read_grant_expires_at = table.Column<long>(type: "bigint", nullable: false),
                    qualification_policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    qualification_policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    maximum_qualification_lifetime_ticks = table.Column<long>(type: "bigint", nullable: false),
                    target_kind = table.Column<int>(type: "integer", nullable: false),
                    purpose = table.Column<int>(type: "integer", nullable: false),
                    custody_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    custody_source_revision = table.Column<long>(type: "bigint", nullable: false),
                    custody_source_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    custody_source_valid_until = table.Column<long>(type: "bigint", nullable: false),
                    maximum_retention_ticks = table.Column<long>(type: "bigint", nullable: false),
                    custody_adoption_id = table.Column<Guid>(type: "uuid", nullable: false),
                    custody_adoption_revision = table.Column<long>(type: "bigint", nullable: false),
                    custody_adoption_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    custody_adoption_valid_until = table.Column<long>(type: "bigint", nullable: false),
                    qualified_at = table.Column<long>(type: "bigint", nullable: false),
                    valid_until = table.Column<long>(type: "bigint", nullable: false),
                    purge_at = table.Column<long>(type: "bigint", nullable: false),
                    assessment_digest = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    withdrawal_operation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    withdrawn_by_human_id = table.Column<Guid>(type: "uuid", nullable: true),
                    withdrawn_at = table.Column<long>(type: "bigint", nullable: true),
                    protected_assessment = table.Column<byte[]>(type: "bytea", maxLength: 131072, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_participant_incident_responsibility_sources", x => x.id);
                    table.CheckConstraint("ck_incident_assessment_digest", "length(assessment_digest) = 64");
                    table.CheckConstraint("ck_incident_assessment_finite_deadlines", "qualified_at > 0 AND valid_until > qualified_at AND valid_until <= purge_at AND purge_at <= original_narrative_purge_at AND original_narrative_purge_at < 3155378975999999999 AND purge_at <= read_grant_expires_at AND read_grant_expires_at < 3155378975999999999 AND purge_at <= custody_adoption_valid_until AND custody_adoption_valid_until <= custody_source_valid_until AND custody_source_valid_until < 3155378975999999999");
                    table.CheckConstraint("ck_incident_assessment_generations", "custody_source_writer_generation > 0 AND custody_source_writer_generation < 9223372036854775807 AND custody_adoption_writer_generation > custody_source_writer_generation AND custody_adoption_writer_generation < 9223372036854775807 AND custody_adoption_id <> custody_source_id AND evidence_source_version <> '00000000-0000-0000-0000-000000000000' AND event_version <> '00000000-0000-0000-0000-000000000000' AND session_generation <> '00000000-0000-0000-0000-000000000000' AND order_generation <> '00000000-0000-0000-0000-000000000000' AND participant_generation <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("ck_incident_assessment_lifetime_limits", "maximum_qualification_lifetime_ticks > 0 AND maximum_qualification_lifetime_ticks < 9223372036854775807 AND maximum_retention_ticks > 0 AND maximum_retention_ticks < 9223372036854775807 AND valid_until - qualified_at <= maximum_qualification_lifetime_ticks AND purge_at - qualified_at <= maximum_retention_ticks");
                    table.CheckConstraint("ck_incident_assessment_purpose", "target_kind = 2 AND purpose = 2 AND subject_kind = 1 AND subject_id <> assessor_human_id AND subject_id <> read_issuer_human_id");
                    table.CheckConstraint("ck_incident_assessment_revisions", "evidence_case_revision > 0 AND occurrence_case_revision >= evidence_case_revision AND read_grant_revision = 1 AND qualification_policy_revision > 0 AND custody_source_revision = 1 AND custody_adoption_revision = 1");
                    table.CheckConstraint("ck_incident_assessment_window", "incident_window_from > 0 AND incident_window_until > incident_window_from AND incident_window_until <= qualified_at");
                    table.CheckConstraint("ck_incident_assessment_withdrawal", "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL AND withdrawal_operation_id IS NULL) OR (revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_at >= qualified_at AND withdrawn_at < 3155378975999999999 AND withdrawn_by_human_id IS NOT NULL AND withdrawal_operation_id IS NOT NULL AND withdrawal_operation_id <> '00000000-0000-0000-0000-000000000000')");
                    table.ForeignKey(
                        name: "fk_moderation_participant_incident_responsibility__13ffc5ffed02",
                        column: x => x.subject_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_participant_incident_responsibility__1a5013ba1c65",
                        column: x => x.original_narrative_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_case_narratives",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_participant_incident_responsibility__1afdd5a36688",
                        column: x => x.event_id,
                        principalSchema: "islamu_event",
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_participant_incident_responsibility__3b0a644e650a",
                        column: x => x.read_issuer_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_participant_incident_responsibility__60863fafd058",
                        columns: x => new { x.custody_source_id, x.host_binding_id, x.trust_domain_id, x.tenant_id },
                        principalSchema: "islamu_event",
                        principalTable: "moderation_case_narrative_policy_sources",
                        principalColumns: new[] { "id", "host_binding_id", "trust_domain_id", "tenant_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_participant_incident_responsibility__6c660fa2071a",
                        column: x => x.order_id,
                        principalSchema: "islamu_event",
                        principalTable: "registration_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_participant_incident_responsibility__6eff8f90938b",
                        column: x => x.occurrence_case_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_participant_incident_responsibility__79aaa633353d",
                        column: x => x.evidence_source_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_case_target_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_participant_incident_responsibility__950c992424a2",
                        column: x => x.session_id,
                        principalSchema: "islamu_event",
                        principalTable: "event_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_participant_incident_responsibility__ae700a8e3c84",
                        column: x => x.qualification_policy_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_case_target_policies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_participant_incident_responsibility__b8c5a058367f",
                        column: x => x.host_binding_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_participant_incident_responsibility__b8f49afca2c1",
                        column: x => x.participant_id,
                        principalSchema: "islamu_event",
                        principalTable: "registration_participants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_participant_incident_responsibility__bdfa89dd8ea9",
                        column: x => x.assessor_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_participant_incident_responsibility__be22aee0f8ae",
                        columns: x => new { x.custody_adoption_id, x.host_binding_id, x.trust_domain_id, x.tenant_id, x.custody_source_id },
                        principalSchema: "islamu_event",
                        principalTable: "moderation_case_narrative_adoptions",
                        principalColumns: new[] { "id", "host_binding_id", "trust_domain_id", "tenant_id", "source_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_participant_incident_responsibility__c0b12d570943",
                        column: x => x.tenant_id,
                        principalSchema: "islamu_event",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_participant_incident_responsibility__cae64ad74311",
                        column: x => x.read_grant_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_case_read_grants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_participant_incident_responsibility__efd758a9ab73",
                        column: x => x.withdrawn_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_case_attribution_heads",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    solution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurrence_case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurrence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurrence_evidence_revision = table.Column<long>(type: "bigint", nullable: false),
                    subject_kind = table.Column<int>(type: "integer", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opening_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_case_attribution_heads", x => x.id);
                    table.CheckConstraint("ck_moderation_attribution_head_generation", "generation > 0 AND revision = generation AND ((generation = 1 AND source_id = opening_source_id) OR (generation > 1 AND source_id <> opening_source_id))");
                    table.CheckConstraint("ck_moderation_attribution_head_scope", "case_id <> occurrence_case_id AND occurrence_evidence_revision > 0 AND subject_kind IN (1, 2, 3, 4)");
                    table.ForeignKey(
                        name: "fk_moderation_case_attribution_heads_moderation_ca_2b1789c35497",
                        column: x => x.occurrence_case_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_attribution_heads_moderation_ca_a7afaabc7bcb",
                        column: x => x.source_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_case_attribution_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_attribution_heads_moderation_ca_d6661eb4ed4c",
                        column: x => x.opening_source_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_case_attribution_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_attribution_heads_moderation_cases_case_id",
                        column: x => x.case_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_attribution_heads_moderation_ho_8c49cc2f7cfa",
                        column: x => x.host_binding_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_attribution_heads_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "islamu_event",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_pre_finding_custody",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_revision = table.Column<long>(type: "bigint", nullable: false),
                    attribution_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attribution_generation = table.Column<long>(type: "bigint", nullable: false),
                    evidence_revision = table.Column<long>(type: "bigint", nullable: false),
                    proposed_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    privacy_owner_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    maximum_retention_ticks = table.Column<long>(type: "bigint", nullable: false),
                    maximum_text_bytes = table.Column<int>(type: "integer", nullable: false),
                    purpose = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    privacy_legal_mandate = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    proposed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_pre_finding_custody", x => x.id);
                    table.UniqueConstraint("ak_moderation_pre_finding_custody_id_host_binding_id_trust_dom", x => new { x.id, x.host_binding_id, x.trust_domain_id, x.tenant_id, x.case_id });
                    table.CheckConstraint("ck_pre_finding_custody_attribution", "attribution_generation > 0 AND evidence_revision > 0");
                    table.CheckConstraint("ck_pre_finding_custody_independence", "proposed_by_human_id <> privacy_owner_human_id");
                    table.CheckConstraint("ck_pre_finding_custody_limits", "maximum_retention_ticks > 0 AND maximum_retention_ticks < 9223372036854775807 AND maximum_text_bytes > 0 AND maximum_text_bytes <= 65536 AND valid_until > proposed_at");
                    table.ForeignKey(
                        name: "fk_moderation_pre_finding_custody_moderation_case__f419e9e612c5",
                        column: x => x.attribution_source_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_case_attribution_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_pre_finding_custody_moderation_cases_case_id",
                        column: x => x.case_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_pre_finding_custody_users_privacy_owner_human_id",
                        column: x => x.privacy_owner_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_pre_finding_custody_users_proposed_by_human_id",
                        column: x => x.proposed_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_profile_collection_procedure_sources",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    publication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode_code = table.Column<int>(type: "integer", nullable: false),
                    proposed_state_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    child_age_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    child_age_source_revision = table.Column<long>(type: "bigint", nullable: false),
                    child_age_source_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    child_age_recorded_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    child_age_recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    child_age_valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    child_age_terms = table.Column<string>(type: "text", nullable: false),
                    child_age_ownership = table.Column<string>(type: "text", nullable: false),
                    child_age_adoption_id = table.Column<Guid>(type: "uuid", nullable: false),
                    child_age_adoption_revision = table.Column<long>(type: "bigint", nullable: false),
                    child_age_adoption_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    child_age_adopted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    child_age_adoption_valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    procedure = table.Column<string>(type: "text", nullable: false),
                    recorded_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_profile_collection_procedure_sources", x => x.id);
                    table.CheckConstraint("ck_profile_collection_procedure_generation", "revision = 1 AND policy_revision > 0 AND child_age_source_revision = 1 AND child_age_source_writer_generation > 0 AND child_age_adoption_revision = 1 AND child_age_adoption_writer_generation > child_age_source_writer_generation AND writer_generation > child_age_adoption_writer_generation");
                    table.CheckConstraint("ck_profile_collection_procedure_mode", "mode_code BETWEEN 1 AND 2");
                    table.CheckConstraint("ck_profile_collection_procedure_validity", "valid_until > recorded_at AND recorded_at >= child_age_recorded_at AND recorded_at >= child_age_adopted_at AND valid_until <= child_age_valid_until AND valid_until <= child_age_adoption_valid_until");
                    table.ForeignKey(
                        name: "fk_moderation_profile_collection_procedure_sources_247ba14cc1a6",
                        column: x => x.recorded_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_profile_collection_procedure_sources_24cd1a4604ef",
                        column: x => x.child_age_adoption_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_child_age_policy_adoptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_profile_collection_procedure_sources_f79f65685a2c",
                        column: x => x.child_age_source_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_child_age_policy_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_classification_taxonomy_sources",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    taxonomy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    taxonomy_revision = table.Column<long>(type: "bigint", nullable: false),
                    previous_generation = table.Column<long>(type: "bigint", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    publication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode_code = table.Column<int>(type: "integer", nullable: false),
                    proposed_state_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    child_age_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    child_age_source_revision = table.Column<long>(type: "bigint", nullable: false),
                    child_age_source_generation = table.Column<long>(type: "bigint", nullable: false),
                    child_age_adoption_id = table.Column<Guid>(type: "uuid", nullable: false),
                    child_age_adoption_revision = table.Column<long>(type: "bigint", nullable: false),
                    child_age_adoption_generation = table.Column<long>(type: "bigint", nullable: false),
                    adopted_debate_minimum_age = table.Column<int>(type: "integer", nullable: false),
                    constitution_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    constitution_source_revision = table.Column<long>(type: "bigint", nullable: false),
                    constitution_source_generation = table.Column<long>(type: "bigint", nullable: false),
                    constitution_adoption_id = table.Column<Guid>(type: "uuid", nullable: false),
                    constitution_adoption_revision = table.Column<long>(type: "bigint", nullable: false),
                    constitution_adoption_generation = table.Column<long>(type: "bigint", nullable: false),
                    owner_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_controller_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewer_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewer_controller_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ownership_grounds = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    review_mandate_grounds = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_classification_taxonomy_sources", x => x.id);
                    table.CheckConstraint("ck_moderation_taxonomy_generation", "revision = 1 AND taxonomy_revision > 0 AND previous_generation >= 0 AND writer_generation > previous_generation AND child_age_source_revision = 1 AND child_age_adoption_revision = 1 AND constitution_source_revision = 1 AND constitution_adoption_revision = 1 AND child_age_source_generation > 0 AND child_age_adoption_generation > child_age_source_generation AND constitution_source_generation > 0 AND constitution_adoption_generation > constitution_source_generation AND writer_generation > child_age_adoption_generation AND writer_generation > constitution_adoption_generation");
                    table.CheckConstraint("ck_moderation_taxonomy_independence", "owner_human_id <> reviewer_human_id AND owner_controller_human_id <> reviewer_controller_human_id AND owner_human_id <> reviewer_controller_human_id AND reviewer_human_id <> owner_controller_human_id");
                    table.CheckConstraint("ck_moderation_taxonomy_mode", "mode_code BETWEEN 1 AND 2 AND policy_revision > 0");
                    table.CheckConstraint("ck_moderation_taxonomy_validity", "valid_until > recorded_at");
                    table.ForeignKey(
                        name: "fk_moderation_classification_taxonomy_sources_mode_0ebf4312bc3a",
                        column: x => x.definition_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_taxonomy_sources_mode_383abfe82bcf",
                        column: x => x.child_age_source_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_child_age_policy_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_taxonomy_sources_mode_67e539ba3ce0",
                        column: x => x.constitution_source_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_constitution_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_taxonomy_sources_mode_973e3f0400fd",
                        column: x => x.constitution_adoption_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_constitution_adoptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_taxonomy_sources_mode_99b99baf6958",
                        column: x => x.child_age_adoption_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_child_age_policy_adoptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_taxonomy_sources_user_0510a7fc41b9",
                        column: x => x.reviewer_controller_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_taxonomy_sources_user_92fcde7e0fc1",
                        column: x => x.reviewer_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_taxonomy_sources_user_d874318ae87c",
                        column: x => x.owner_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_taxonomy_sources_user_da78a4e088bc",
                        column: x => x.owner_controller_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_event_target_adoptions",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    selection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    selection_revision = table.Column<long>(type: "bigint", nullable: false),
                    binding_ordinal = table.Column<int>(type: "integer", nullable: false),
                    generation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    generation_revision = table.Column<long>(type: "bigint", nullable: false),
                    generation_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_revision = table.Column<long>(type: "bigint", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    adopted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_by_human_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_event_target_adoptions", x => x.id);
                    table.CheckConstraint("ck_moderation_event_target_adoption_revision", "selection_revision = 1 AND generation_revision > 0 AND generation_writer_generation > 0 AND case_revision > 0 AND writer_generation > 0");
                    table.CheckConstraint("ck_moderation_event_target_adoption_revocation", "(revision = 1 AND revoked_at IS NULL AND revoked_by_human_id IS NULL) OR (revision = 2 AND revoked_at IS NOT NULL AND revoked_by_human_id IS NOT NULL AND revoked_at >= adopted_at)");
                    table.CheckConstraint("ck_moderation_event_target_adoption_validity", "valid_until > adopted_at");
                    table.ForeignKey(
                        name: "fk_moderation_event_target_adoptions_moderation_co_df13b74d76ae",
                        columns: x => new { x.selection_id, x.binding_ordinal },
                        principalSchema: "islamu_event",
                        principalTable: "moderation_contribution_bindings",
                        principalColumns: new[] { "selection_id", "ordinal" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_event_target_adoptions_moderation_ev_c95120b5dc17",
                        column: x => x.generation_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_event_generations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_event_target_adoptions_users_adopted_by_human_id",
                        column: x => x.adopted_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_event_target_adoptions_users_revoked_by_human_id",
                        column: x => x.revoked_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_resource_target_adoptions",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    selection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    selection_revision = table.Column<long>(type: "bigint", nullable: false),
                    binding_ordinal = table.Column<int>(type: "integer", nullable: false),
                    generation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    generation_revision = table.Column<long>(type: "bigint", nullable: false),
                    generation_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_revision = table.Column<long>(type: "bigint", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    adopted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_by_human_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_resource_target_adoptions", x => x.id);
                    table.CheckConstraint("ck_moderation_resource_target_adoption_revision", "selection_revision = 1 AND generation_revision > 0 AND generation_writer_generation > 0 AND case_revision > 0 AND writer_generation > 0");
                    table.CheckConstraint("ck_moderation_resource_target_adoption_revocation", "(revision = 1 AND revoked_at IS NULL AND revoked_by_human_id IS NULL) OR (revision = 2 AND revoked_at IS NOT NULL AND revoked_by_human_id IS NOT NULL AND revoked_at >= adopted_at)");
                    table.CheckConstraint("ck_moderation_resource_target_adoption_validity", "valid_until > adopted_at");
                    table.ForeignKey(
                        name: "fk_moderation_resource_target_adoptions_moderation_54b35894dbfe",
                        columns: x => new { x.selection_id, x.binding_ordinal },
                        principalSchema: "islamu_event",
                        principalTable: "moderation_contribution_bindings",
                        principalColumns: new[] { "selection_id", "ordinal" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_resource_target_adoptions_moderation_d32890752058",
                        column: x => x.generation_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_resource_generations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_resource_target_adoptions_users_adop_09c1a18d1aef",
                        column: x => x.adopted_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_resource_target_adoptions_users_revo_af40e21f7f4e",
                        column: x => x.revoked_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_publication_profiles",
                schema: "islamu_event",
                columns: table => new
                {
                    publication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    rule_revision = table.Column<long>(type: "bigint", nullable: false),
                    reason_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reason_revision = table.Column<long>(type: "bigint", nullable: false),
                    reference_profile = table.Column<int>(type: "integer", nullable: false),
                    reference_revision = table.Column<long>(type: "bigint", nullable: false),
                    grade = table.Column<int>(type: "integer", nullable: false),
                    bundle = table.Column<int>(type: "integer", nullable: false),
                    protection_purpose = table.Column<int>(type: "integer", nullable: true),
                    timing_kind = table.Column<int>(type: "integer", nullable: false),
                    duration_ticks = table.Column<long>(type: "bigint", nullable: false),
                    required_approvals = table.Column<int>(type: "integer", nullable: false),
                    contestation_calendar_months = table.Column<int>(type: "integer", nullable: false),
                    recognition = table.Column<int>(type: "integer", nullable: false),
                    supported = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_publication_profiles", x => new { x.publication_id, x.profile_id });
                    table.CheckConstraint("ck_moderation_profile_purpose", "(bundle = 10 AND protection_purpose BETWEEN 1 AND 2) OR (bundle BETWEEN 1 AND 9 AND protection_purpose IS NULL)");
                    table.CheckConstraint("ck_moderation_profile_quorum", "required_approvals BETWEEN 0 AND 2");
                    table.CheckConstraint("ck_moderation_profile_revision", "rule_revision > 0 AND reason_revision > 0 AND reference_revision > 0");
                    table.CheckConstraint("ck_moderation_profile_timing", "duration_ticks >= 0 AND timing_kind BETWEEN 1 AND 9");
                    table.ForeignKey(
                        name: "fk_moderation_publication_profiles_moderation_poli_e7bbcd48f902",
                        column: x => x.publication_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_publication_profiles_moderation_publ_4850bfb34de9",
                        columns: x => new { x.publication_id, x.reason_code, x.reason_revision },
                        principalSchema: "islamu_event",
                        principalTable: "moderation_publication_reasons",
                        principalColumns: new[] { "publication_id", "code", "revision" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_case_attribution_renewal_receipts",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    head_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expected_case_revision = table.Column<long>(type: "bigint", nullable: false),
                    expected_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expected_generation = table.Column<long>(type: "bigint", nullable: false),
                    expected_head_revision = table.Column<long>(type: "bigint", nullable: false),
                    qualified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_case_attribution_renewal_receipts", x => x.id);
                    table.CheckConstraint("ck_moderation_attribution_renewal_distinct_source", "id <> expected_source_id");
                    table.CheckConstraint("ck_moderation_attribution_renewal_expected", "expected_case_revision > 0 AND expected_generation > 0 AND expected_head_revision > 0 AND expected_head_revision = expected_generation");
                    table.ForeignKey(
                        name: "fk_moderation_case_attribution_renewal_receipts_mo_42b3a251df93",
                        column: x => x.head_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_case_attribution_heads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_attribution_renewal_receipts_mo_885b5b68bff1",
                        column: x => x.expected_source_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_case_attribution_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_attribution_renewal_receipts_mo_a211282cfd57",
                        column: x => x.id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_case_attribution_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_attribution_renewal_receipts_mo_a9487fb070a1",
                        column: x => x.case_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_attribution_renewal_receipts_te_e5802d946d99",
                        column: x => x.tenant_id,
                        principalSchema: "islamu_event",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_pre_finding_notices",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    custody_id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_revision = table.Column<long>(type: "bigint", nullable: false),
                    occurrence_case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evidence_revision = table.Column<long>(type: "bigint", nullable: false),
                    attribution_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attribution_generation = table.Column<long>(type: "bigint", nullable: false),
                    subject_kind = table.Column<int>(type: "integer", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_assignment_revision = table.Column<long>(type: "bigint", nullable: false),
                    issued_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewer_assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewer_assignment_revision = table.Column<long>(type: "bigint", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    retain_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    responded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    response_id = table.Column<Guid>(type: "uuid", nullable: true),
                    purged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    withdrawn = table.Column<bool>(type: "boolean", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    protected_notice = table.Column<byte[]>(type: "bytea", maxLength: 131072, nullable: false),
                    protected_response = table.Column<byte[]>(type: "bytea", maxLength: 131072, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_pre_finding_notices", x => x.id);
                    table.CheckConstraint("ck_pre_finding_notice_finite", "retain_until > issued_at");
                    table.CheckConstraint("ck_pre_finding_notice_link", "case_id <> occurrence_case_id AND case_revision > 0 AND evidence_revision > 0 AND attribution_generation > 0 AND revision > 0");
                    table.CheckConstraint("ck_pre_finding_notice_response", "(response_id IS NULL AND responded_at IS NULL) OR (response_id IS NOT NULL AND responded_at >= acknowledged_at AND responded_at < retain_until)");
                    table.ForeignKey(
                        name: "fk_moderation_pre_finding_notices_moderation_cases_92474baa4248",
                        column: x => x.occurrence_case_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_pre_finding_notices_moderation_cases_case_id",
                        column: x => x.case_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_pre_finding_notices_moderation_pre_f_4a58a4c694c6",
                        columns: x => new { x.custody_id, x.host_binding_id, x.trust_domain_id, x.tenant_id, x.case_id },
                        principalSchema: "islamu_event",
                        principalTable: "moderation_pre_finding_custody",
                        principalColumns: new[] { "id", "host_binding_id", "trust_domain_id", "tenant_id", "case_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_pre_finding_notices_moderation_revie_e1308b5e800a",
                        column: x => x.reviewer_assignment_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_reviewer_assignments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_pre_finding_notices_users_recipient_human_id",
                        column: x => x.recipient_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_profile_collection_procedure_adoptions",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_revision = table.Column<long>(type: "bigint", nullable: false),
                    source_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    adopted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    withdrawn_by_human_id = table.Column<Guid>(type: "uuid", nullable: true),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_profile_collection_procedure_adoptions", x => x.id);
                    table.CheckConstraint("ck_profile_collection_adoption_generation", "source_revision = 1 AND source_writer_generation > 0 AND writer_generation > source_writer_generation");
                    table.CheckConstraint("ck_profile_collection_adoption_validity", "valid_until > adopted_at");
                    table.CheckConstraint("ck_profile_collection_adoption_withdrawal", "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR (revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at >= adopted_at)");
                    table.ForeignKey(
                        name: "fk_moderation_profile_collection_procedure_adoptio_5da1788e7d5b",
                        column: x => x.adopted_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_profile_collection_procedure_adoptio_85612c5a1a97",
                        column: x => x.source_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_profile_collection_procedure_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_profile_collection_procedure_adoptio_d716ad0b5fa5",
                        column: x => x.withdrawn_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_profile_subject_consents",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    procedure_adoption_id = table.Column<Guid>(type: "uuid", nullable: false),
                    procedure_adoption_revision = table.Column<long>(type: "bigint", nullable: false),
                    procedure_adoption_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    procedure_adopted_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    procedure_adopted_at = table.Column<long>(type: "bigint", nullable: false),
                    procedure_adoption_valid_until = table.Column<long>(type: "bigint", nullable: false),
                    binding = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    assessment = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    subject_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    granted_at = table.Column<long>(type: "bigint", nullable: false),
                    valid_until = table.Column<long>(type: "bigint", nullable: false),
                    withdrawn_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_profile_subject_consents", x => x.id);
                    table.CheckConstraint("ck_profile_subject_consent_generation", "generation > 0 AND generation < 9223372036854775807 AND procedure_adoption_revision = 1 AND procedure_adoption_writer_generation > 0 AND procedure_adoption_writer_generation < 9223372036854775807");
                    table.CheckConstraint("ck_profile_subject_consent_terminal", "(revision = 1 AND withdrawn_at IS NULL) OR (revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_at >= granted_at AND withdrawn_at < 3155378975999999999)");
                    table.CheckConstraint("ck_profile_subject_consent_validity", "granted_at > 0 AND valid_until < 3155378975999999999 AND valid_until > granted_at AND procedure_adopted_at > 0 AND procedure_adoption_valid_until < 3155378975999999999 AND procedure_adoption_valid_until > procedure_adopted_at AND granted_at >= procedure_adopted_at AND valid_until <= procedure_adoption_valid_until");
                    table.ForeignKey(
                        name: "fk_moderation_profile_subject_consents_moderation__5a4487d1e8bd",
                        column: x => x.source_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_profile_collection_procedure_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_profile_subject_consents_user_pii_su_a29fb24ea149",
                        column: x => x.subject_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "user_pii",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "moderation_classification_taxonomy_adoptions",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_revision = table.Column<long>(type: "bigint", nullable: false),
                    source_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    adopted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    withdrawn_by_human_id = table.Column<Guid>(type: "uuid", nullable: true),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_classification_taxonomy_adoptions", x => x.id);
                    table.CheckConstraint("ck_moderation_taxonomy_adoption_generation", "source_revision = 1 AND source_writer_generation > 0 AND writer_generation > source_writer_generation");
                    table.CheckConstraint("ck_moderation_taxonomy_adoption_validity", "valid_until > adopted_at");
                    table.CheckConstraint("ck_moderation_taxonomy_adoption_withdrawal", "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR (revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at >= adopted_at)");
                    table.ForeignKey(
                        name: "fk_moderation_classification_taxonomy_adoptions_mo_a7ecb6815bdc",
                        column: x => x.source_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_classification_taxonomy_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_taxonomy_adoptions_us_807db6f90967",
                        column: x => x.adopted_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_taxonomy_adoptions_us_8a3f04b0f600",
                        column: x => x.withdrawn_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_classification_taxonomy_categories",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    category_id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    minimum_age = table.Column<int>(type: "integer", nullable: false),
                    meaning = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    review_rationale = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_classification_taxonomy_categories", x => x.id);
                    table.CheckConstraint("ck_moderation_taxonomy_category", "category_id > 0 AND ordinal BETWEEN 0 AND 63 AND kind BETWEEN 1 AND 4 AND minimum_age BETWEEN 0 AND 130");
                    table.ForeignKey(
                        name: "fk_moderation_classification_taxonomy_categories_m_9b4e778d4709",
                        column: x => x.source_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_classification_taxonomy_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_publication_consumers",
                schema: "islamu_event",
                columns: table => new
                {
                    publication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode = table.Column<int>(type: "integer", nullable: false),
                    capability = table.Column<int>(type: "integer", nullable: false),
                    consumer_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    subject_resolver = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    target_resolver = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    authorization_owner = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    entry_checkpoint = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    terminal_checkpoint = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    writer_fence = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    freshness_class = table.Column<int>(type: "integer", nullable: false),
                    owning_packet = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    conformance_test = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_publication_consumers", x => new { x.publication_id, x.profile_id, x.mode, x.capability, x.consumer_id });
                    table.ForeignKey(
                        name: "fk_moderation_publication_consumers_moderation_pub_fc150e43217e",
                        columns: x => new { x.publication_id, x.profile_id },
                        principalSchema: "islamu_event",
                        principalTable: "moderation_publication_profiles",
                        principalColumns: new[] { "publication_id", "profile_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_publication_effects",
                schema: "islamu_event",
                columns: table => new
                {
                    publication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    mode = table.Column<int>(type: "integer", nullable: false),
                    target = table.Column<int>(type: "integer", nullable: false),
                    capability = table.Column<int>(type: "integer", nullable: true),
                    execution = table.Column<int>(type: "integer", nullable: false),
                    semantic_owner = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_publication_effects", x => new { x.publication_id, x.profile_id, x.ordinal });
                    table.ForeignKey(
                        name: "fk_moderation_publication_effects_moderation_publi_3a850c508bf1",
                        columns: x => new { x.publication_id, x.profile_id },
                        principalSchema: "islamu_event",
                        principalTable: "moderation_publication_profiles",
                        principalColumns: new[] { "publication_id", "profile_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_publication_plans",
                schema: "islamu_event",
                columns: table => new
                {
                    publication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    strategy = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<int>(type: "integer", nullable: false),
                    management = table.Column<int>(type: "integer", nullable: false),
                    resources = table.Column<int>(type: "integer", nullable: false),
                    new_admission = table.Column<int>(type: "integer", nullable: false),
                    existing_admission = table.Column<int>(type: "integer", nullable: false),
                    private_disclosure = table.Column<int>(type: "integer", nullable: false),
                    settlement = table.Column<int>(type: "integer", nullable: false),
                    capacity = table.Column<int>(type: "integer", nullable: false),
                    credentials = table.Column<int>(type: "integer", nullable: false),
                    notifications = table.Column<int>(type: "integer", nullable: false),
                    recognition = table.Column<int>(type: "integer", nullable: false),
                    pending_work = table.Column<int>(type: "integer", nullable: false),
                    restoration = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_publication_plans", x => new { x.publication_id, x.profile_id, x.strategy });
                    table.CheckConstraint("ck_moderation_publication_strategy", "strategy BETWEEN 0 AND 3");
                    table.ForeignKey(
                        name: "fk_moderation_publication_plans_moderation_publica_9b4d58386bd7",
                        columns: x => new { x.publication_id, x.profile_id },
                        principalSchema: "islamu_event",
                        principalTable: "moderation_publication_profiles",
                        principalColumns: new[] { "publication_id", "profile_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_publication_prerequisites",
                schema: "islamu_event",
                columns: table => new
                {
                    publication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode = table.Column<int>(type: "integer", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    activation_gate = table.Column<int>(type: "integer", nullable: true),
                    native_prerequisite = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_publication_prerequisites", x => new { x.publication_id, x.profile_id, x.mode, x.ordinal });
                    table.CheckConstraint("ck_moderation_publication_prerequisite", "(activation_gate BETWEEN 1 AND 5 AND native_prerequisite IS NULL) OR (activation_gate IS NULL AND native_prerequisite BETWEEN 1 AND 30)");
                    table.ForeignKey(
                        name: "fk_moderation_publication_prerequisites_moderation_1eaee71067ec",
                        columns: x => new { x.publication_id, x.profile_id },
                        principalSchema: "islamu_event",
                        principalTable: "moderation_publication_profiles",
                        principalColumns: new[] { "publication_id", "profile_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_publication_scopes",
                schema: "islamu_event",
                columns: table => new
                {
                    publication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_publication_scopes", x => new { x.publication_id, x.profile_id, x.kind });
                    table.ForeignKey(
                        name: "fk_moderation_publication_scopes_moderation_public_fa2d12c4e46f",
                        columns: x => new { x.publication_id, x.profile_id },
                        principalSchema: "islamu_event",
                        principalTable: "moderation_publication_profiles",
                        principalColumns: new[] { "publication_id", "profile_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_publication_subjects",
                schema: "islamu_event",
                columns: table => new
                {
                    publication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_publication_subjects", x => new { x.publication_id, x.profile_id, x.kind });
                    table.ForeignKey(
                        name: "fk_moderation_publication_subjects_moderation_publ_ee5dd2b334a4",
                        columns: x => new { x.publication_id, x.profile_id },
                        principalSchema: "islamu_event",
                        principalTable: "moderation_publication_profiles",
                        principalColumns: new[] { "publication_id", "profile_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_profile_collection_assessment_sources",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    procedure_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_controller_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewer_controller_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    procedure_adoption_id = table.Column<Guid>(type: "uuid", nullable: false),
                    procedure_adoption_revision = table.Column<long>(type: "bigint", nullable: false),
                    procedure_adoption_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    procedure_adopted_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    procedure_adopted_at = table.Column<long>(type: "bigint", nullable: false),
                    procedure_adoption_valid_until = table.Column<long>(type: "bigint", nullable: false),
                    binding = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    required = table.Column<int>(type: "integer", nullable: false),
                    recorded_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    recorded_at = table.Column<long>(type: "bigint", nullable: false),
                    valid_until = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_profile_collection_assessment_sources", x => x.id);
                    table.CheckConstraint("ck_profile_assessment_source_generation", "revision = 1 AND generation > 0 AND generation < 9223372036854775807 AND procedure_adoption_revision = 1 AND procedure_adoption_writer_generation > 0 AND procedure_adoption_writer_generation < 9223372036854775807");
                    table.CheckConstraint("ck_profile_assessment_source_independence", "recorded_by_human_id <> procedure_adopted_by_human_id AND procedure_adopted_by_human_id <> owner_controller_human_id AND owner_controller_human_id <> reviewer_controller_human_id");
                    table.CheckConstraint("ck_profile_assessment_source_required", "required BETWEEN 0 AND 7");
                    table.CheckConstraint("ck_profile_assessment_source_window", "recorded_at > 0 AND valid_until < 3155378975999999999 AND valid_until > recorded_at AND procedure_adopted_at > 0 AND procedure_adoption_valid_until < 3155378975999999999 AND procedure_adoption_valid_until > procedure_adopted_at AND recorded_at >= procedure_adopted_at AND valid_until <= procedure_adoption_valid_until");
                    table.ForeignKey(
                        name: "fk_moderation_profile_collection_assessment_source_1e2e31ab3043",
                        column: x => x.subject_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "user_pii",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_moderation_profile_collection_assessment_source_4648c4aaaa6b",
                        column: x => x.reviewer_controller_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_profile_collection_assessment_source_701dce7a5b91",
                        column: x => x.procedure_adoption_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_profile_collection_procedure_adoptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_profile_collection_assessment_source_ae8454ff4166",
                        column: x => x.recorded_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_profile_collection_assessment_source_baaeaa91aa94",
                        column: x => x.owner_controller_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_profile_collection_assessment_source_d9e075a74564",
                        column: x => x.procedure_source_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_profile_collection_procedure_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_profile_collection_assessment_source_ddfa3e025244",
                        column: x => x.procedure_adopted_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_profile_collection_subject_observations",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    generation = table.Column<long>(type: "bigint", nullable: false),
                    observation_sequence = table.Column<long>(type: "bigint", nullable: false),
                    observation_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    observed_at = table.Column<long>(type: "bigint", nullable: false),
                    valid_until = table.Column<long>(type: "bigint", nullable: false),
                    subject_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_login_id = table.Column<Guid>(type: "uuid", nullable: true),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    solution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    jurisdiction_kind = table.Column<int>(type: "integer", nullable: false),
                    epoch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    epoch_revision = table.Column<long>(type: "bigint", nullable: false),
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    publication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode_code = table.Column<int>(type: "integer", nullable: false),
                    procedure_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    procedure_source_revision = table.Column<long>(type: "bigint", nullable: false),
                    procedure_source_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    procedure_adoption_id = table.Column<Guid>(type: "uuid", nullable: false),
                    procedure_adoption_revision = table.Column<long>(type: "bigint", nullable: false),
                    procedure_adoption_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    procedure_adopted_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    procedure_adopted_at = table.Column<long>(type: "bigint", nullable: false),
                    procedure_adoption_valid_until = table.Column<long>(type: "bigint", nullable: false),
                    original_procedure_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    original_policy_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    original_selection_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_profile_collection_subject_observations", x => x.id);
                    table.CheckConstraint("ck_profile_subject_observation_generations", "revision = 1 AND generation > 0 AND generation < 9223372036854775807 AND observation_sequence = generation AND policy_revision > 0 AND policy_revision < 9223372036854775807 AND procedure_source_revision = 1 AND procedure_source_writer_generation > 0 AND procedure_adoption_revision = 1 AND procedure_adoption_writer_generation > procedure_source_writer_generation AND epoch_revision >= procedure_adoption_writer_generation AND epoch_revision < 9223372036854775807");
                    table.CheckConstraint("ck_profile_subject_observation_identity", "id <> subject_human_id AND id <> observation_stamp AND id <> procedure_source_id AND id <> procedure_adoption_id AND observation_stamp <> subject_human_id");
                    table.CheckConstraint("ck_profile_subject_observation_scope", "jurisdiction_kind IN (3, 4) AND mode_code BETWEEN 1 AND 2");
                    table.CheckConstraint("ck_profile_subject_observation_window", "observed_at > 0 AND valid_until < 3155378975999999999 AND valid_until > observed_at AND procedure_adopted_at > 0 AND procedure_adoption_valid_until < 3155378975999999999 AND procedure_adoption_valid_until > procedure_adopted_at AND observed_at >= procedure_adopted_at AND valid_until <= procedure_adoption_valid_until");
                    table.ForeignKey(
                        name: "fk_moderation_profile_collection_subject_observati_37444090ac7e",
                        column: x => x.subject_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "user_pii",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_moderation_profile_collection_subject_observati_64d2351fccb0",
                        column: x => x.procedure_source_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_profile_collection_procedure_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_profile_collection_subject_observati_81d460fadaae",
                        column: x => x.procedure_adoption_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_profile_collection_procedure_adoptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_profile_collection_subject_observati_f0c5b8158927",
                        column: x => x.epoch_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_qualification_epochs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_classification_material_sources",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    previous_generation = table.Column<long>(type: "bigint", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    event_generation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_generation_revision = table.Column<long>(type: "bigint", nullable: false),
                    event_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    publication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode_code = table.Column<int>(type: "integer", nullable: false),
                    taxonomy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    taxonomy_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    taxonomy_source_revision = table.Column<long>(type: "bigint", nullable: false),
                    taxonomy_source_generation = table.Column<long>(type: "bigint", nullable: false),
                    taxonomy_adoption_id = table.Column<Guid>(type: "uuid", nullable: false),
                    taxonomy_adoption_revision = table.Column<long>(type: "bigint", nullable: false),
                    taxonomy_adoption_generation = table.Column<long>(type: "bigint", nullable: false),
                    owner_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_controller_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_assignment_revision = table.Column<long>(type: "bigint", nullable: false),
                    reviewer_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewer_controller_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ownership_grounds = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    review_grounds = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_classification_material_sources", x => x.id);
                    table.CheckConstraint("ck_moderation_material_generation", "revision = 1 AND previous_generation >= 0 AND event_generation_revision > 0 AND event_writer_generation > 0 AND taxonomy_source_revision = 1 AND taxonomy_adoption_revision = 1 AND taxonomy_source_generation > 0 AND taxonomy_adoption_generation > taxonomy_source_generation AND writer_generation > taxonomy_adoption_generation AND writer_generation > event_writer_generation AND writer_generation > previous_generation AND owner_assignment_revision > 0 AND policy_revision > 0 AND mode_code BETWEEN 1 AND 2");
                    table.CheckConstraint("ck_moderation_material_independence", "owner_human_id <> reviewer_human_id AND owner_controller_human_id <> reviewer_controller_human_id AND owner_human_id <> reviewer_controller_human_id AND reviewer_human_id <> owner_controller_human_id");
                    table.CheckConstraint("ck_moderation_material_validity", "valid_until > recorded_at");
                    table.ForeignKey(
                        name: "fk_moderation_classification_material_sources_mode_19636c5e283b",
                        column: x => x.event_generation_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_event_generations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_material_sources_mode_453bf0c003ac",
                        column: x => x.taxonomy_adoption_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_classification_taxonomy_adoptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_material_sources_mode_ab63c33ba9e9",
                        column: x => x.taxonomy_source_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_classification_taxonomy_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_profile_collection_assessment_adoptions",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_revision = table.Column<long>(type: "bigint", nullable: false),
                    source_generation = table.Column<long>(type: "bigint", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    adopted_at = table.Column<long>(type: "bigint", nullable: false),
                    valid_until = table.Column<long>(type: "bigint", nullable: false),
                    withdrawn_by_human_id = table.Column<Guid>(type: "uuid", nullable: true),
                    withdrawn_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_profile_collection_assessment_adoptions", x => x.id);
                    table.CheckConstraint("ck_profile_assessment_adoption_generation", "source_revision = 1 AND source_generation > 0 AND source_generation < 9223372036854775807 AND generation > source_generation AND generation < 9223372036854775807");
                    table.CheckConstraint("ck_profile_assessment_adoption_terminal", "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR (revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at >= adopted_at AND withdrawn_at < 3155378975999999999)");
                    table.CheckConstraint("ck_profile_assessment_adoption_window", "adopted_at > 0 AND valid_until < 3155378975999999999 AND valid_until > adopted_at");
                    table.ForeignKey(
                        name: "fk_moderation_profile_collection_assessment_adopti_07da50730dfc",
                        column: x => x.withdrawn_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_profile_collection_assessment_adopti_4d99085f9401",
                        column: x => x.adopted_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_profile_collection_assessment_adopti_fb3d46d6ad68",
                        column: x => x.source_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_profile_collection_assessment_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "moderation_classification_material_components",
                schema: "islamu_event",
                columns: table => new
                {
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    native_id = table.Column<Guid>(type: "uuid", nullable: false),
                    native_stamp = table.Column<Guid>(type: "uuid", nullable: true),
                    digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_classification_material_components", x => new { x.source_id, x.ordinal });
                    table.CheckConstraint("ck_moderation_material_component_kind", "kind BETWEEN 1 AND 41 AND ordinal BETWEEN 0 AND 4095");
                    table.ForeignKey(
                        name: "fk_moderation_classification_material_components_m_815372cdd3d5",
                        column: x => x.source_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_classification_material_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_classification_material_entries",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    session_stamp = table.Column<Guid>(type: "uuid", nullable: true),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resource_stamp = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_classification_material_entries", x => x.id);
                    table.CheckConstraint("ck_moderation_material_entry_ancestry", "((session_id IS NULL AND session_stamp IS NULL) OR (session_id IS NOT NULL AND session_stamp IS NOT NULL)) AND ((resource_id IS NULL AND resource_stamp IS NULL) OR (resource_id IS NOT NULL AND resource_stamp IS NOT NULL))");
                    table.CheckConstraint("ck_moderation_material_entry_ordinal", "ordinal BETWEEN 0 AND 4095");
                    table.ForeignKey(
                        name: "fk_moderation_classification_material_entries_mode_795b5fbfe87f",
                        column: x => x.source_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_classification_material_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_classification_material_reviews",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_revision = table.Column<long>(type: "bigint", nullable: false),
                    source_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    reviewer_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_kind = table.Column<int>(type: "integer", nullable: true),
                    ended_by_human_id = table.Column<Guid>(type: "uuid", nullable: true),
                    end_writer_generation = table.Column<long>(type: "bigint", nullable: true),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    end_grounds = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_classification_material_reviews", x => x.id);
                    table.CheckConstraint("ck_moderation_material_review_ending", "(revision = 1 AND end_kind IS NULL AND ended_at IS NULL AND ended_by_human_id IS NULL AND end_writer_generation IS NULL AND end_grounds IS NULL) OR (revision = 2 AND end_kind BETWEEN 1 AND 2 AND ended_at IS NOT NULL AND ended_by_human_id IS NOT NULL AND end_writer_generation > writer_generation AND end_grounds IS NOT NULL AND ended_at >= accepted_at)");
                    table.CheckConstraint("ck_moderation_material_review_generation", "source_revision = 1 AND source_writer_generation > 0 AND writer_generation > source_writer_generation");
                    table.CheckConstraint("ck_moderation_material_review_validity", "valid_until > accepted_at");
                    table.ForeignKey(
                        name: "fk_moderation_classification_material_reviews_mode_174036e94143",
                        column: x => x.source_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_classification_material_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_classification_material_categories",
                schema: "islamu_event",
                columns: table => new
                {
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    version_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_classification_material_categories", x => new { x.entry_id, x.ordinal });
                    table.CheckConstraint("ck_moderation_material_category_ordinal", "ordinal BETWEEN 0 AND 63");
                    table.ForeignKey(
                        name: "fk_moderation_classification_material_categories_m_8a3e8cd82577",
                        column: x => x.entry_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_classification_material_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_material_categories_m_a4ef9c4b3910",
                        column: x => x.version_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_classification_taxonomy_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_classification_control_selections",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_revision = table.Column<long>(type: "bigint", nullable: false),
                    source_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    source_recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source_valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    review_id = table.Column<Guid>(type: "uuid", nullable: false),
                    review_revision = table.Column<long>(type: "bigint", nullable: false),
                    review_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    review_valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    generation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    generation_revision = table.Column<long>(type: "bigint", nullable: false),
                    generation_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    generation_observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    generation_valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    publication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode_code = table.Column<int>(type: "integer", nullable: false),
                    owner_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_controller_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewer_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewer_controller_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    authored_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    selection_digest = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_classification_control_selections", x => x.id);
                    table.CheckConstraint("ck_classification_control_digest", "length(selection_digest) = 64");
                    table.CheckConstraint("ck_classification_control_generations", "revision = 1 AND source_revision = 1 AND review_revision = 1 AND generation_revision > 0 AND generation_revision < 9223372036854775807 AND generation_writer_generation > 0 AND source_writer_generation > generation_writer_generation AND review_writer_generation > source_writer_generation AND writer_generation > review_writer_generation AND writer_generation < 9223372036854775807 AND policy_revision > 0 AND policy_revision < 9223372036854775807 AND mode_code IN (1, 2)");
                    table.CheckConstraint("ck_classification_control_identity", "id <> source_id AND id <> review_id AND id <> generation_id AND source_id <> review_id AND source_id <> generation_id AND review_id <> generation_id");
                    table.CheckConstraint("ck_classification_control_independence", "reviewer_human_id <> owner_human_id AND reviewer_human_id <> owner_controller_human_id AND reviewer_controller_human_id <> owner_human_id AND reviewer_controller_human_id <> owner_controller_human_id AND authored_by_human_id <> owner_human_id AND authored_by_human_id <> owner_controller_human_id AND authored_by_human_id <> reviewer_human_id AND authored_by_human_id <> reviewer_controller_human_id");
                    table.CheckConstraint("ck_classification_control_validity", "source_valid_until > source_recorded_at AND review_valid_until > starts_at AND generation_valid_until > generation_observed_at AND valid_until > recorded_at AND starts_at >= source_recorded_at AND starts_at >= generation_observed_at AND recorded_at >= starts_at AND source_valid_until <= generation_valid_until AND review_valid_until <= source_valid_until AND valid_until <= review_valid_until");
                    table.ForeignKey(
                        name: "fk_moderation_classification_control_selections_mo_019b947e9dd5",
                        column: x => x.review_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_classification_material_reviews",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_control_selections_mo_6e8328e3adf6",
                        column: x => x.host_binding_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_control_selections_mo_abec953cbbec",
                        column: x => x.publication_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_control_selections_mo_fbf6855975d7",
                        column: x => x.source_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_classification_material_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_control_selections_mo_fe56ce72b4d5",
                        column: x => x.generation_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_event_generations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_control_selections_te_49665ba46e04",
                        column: x => x.tenant_id,
                        principalSchema: "islamu_event",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_control_selections_us_208af11c18ab",
                        column: x => x.authored_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_classification_control_bindings",
                schema: "islamu_event",
                columns: table => new
                {
                    selection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    effect_kind = table.Column<int>(type: "integer", nullable: true),
                    effect_mode = table.Column<int>(type: "integer", nullable: true),
                    effect_target = table.Column<int>(type: "integer", nullable: true),
                    capability = table.Column<int>(type: "integer", nullable: true),
                    execution = table.Column<int>(type: "integer", nullable: true),
                    strategy = table.Column<int>(type: "integer", nullable: true),
                    scope_kind = table.Column<int>(type: "integer", nullable: false),
                    scope_trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_solution_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scope_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scope_tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scope_community_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scope_resource_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scope_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subject_kind = table.Column<int>(type: "integer", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_revision = table.Column<long>(type: "bigint", nullable: false),
                    operation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    window_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_classification_control_bindings", x => new { x.selection_id, x.ordinal });
                    table.CheckConstraint("ck_classification_control_binding_event", "scope_kind = 7");
                    table.CheckConstraint("ck_classification_control_binding_meaning", "(kind = 1 AND effect_kind BETWEEN 1 AND 26 AND effect_kind IS NOT NULL AND effect_mode BETWEEN 1 AND 8 AND effect_mode IS NOT NULL AND effect_target = 12 AND effect_target IS NOT NULL AND execution = 2 AND execution IS NOT NULL AND strategy IS NULL AND (capability IS NULL OR capability BETWEEN 1 AND 12)) OR (kind = 2 AND effect_kind IS NULL AND effect_mode IS NULL AND effect_target IS NULL AND capability IS NULL AND execution IS NULL AND (strategy IS NULL OR strategy IN (1, 2, 3)))");
                    table.CheckConstraint("ck_classification_control_binding_partition", "ordinal >= 0 AND ordinal < 64 AND target_revision > 0 AND target_revision < 9223372036854775807 AND subject_kind = 4 AND subject_id = target_id AND target_id = scope_event_id AND window_end IS NULL");
                    table.CheckConstraint("ck_moderation_classification_control_bindings_scope_scope", "scope_kind IS NOT NULL AND ((scope_kind = 1 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 2 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 3 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 4 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 5 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NOT NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 6 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_resource_id IS NOT NULL AND scope_event_id IS NULL) OR (scope_kind = 7 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NOT NULL AND scope_event_id <> '00000000-0000-0000-0000-000000000000'))");
                    table.ForeignKey(
                        name: "fk_moderation_classification_control_bindings_mode_c85f69c0c030",
                        column: x => x.selection_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_classification_control_selections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_classification_event_target_adoptions",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    selection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    selection_revision = table.Column<long>(type: "bigint", nullable: false),
                    selection_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    binding_ordinal = table.Column<int>(type: "integer", nullable: false),
                    generation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    generation_revision = table.Column<long>(type: "bigint", nullable: false),
                    generation_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_controller_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewer_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewer_controller_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    selection_authored_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    selection_recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    selection_valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    generation_observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    generation_valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    adopted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_by_human_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revocation_writer_generation = table.Column<long>(type: "bigint", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revocation_grounds = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_classification_event_target_adoptions", x => x.id);
                    table.CheckConstraint("ck_classification_event_adoption_generations", "selection_revision = 1 AND selection_writer_generation > 0 AND generation_revision > 0 AND generation_revision < 9223372036854775807 AND generation_writer_generation > 0 AND writer_generation > selection_writer_generation AND writer_generation > generation_writer_generation AND writer_generation < 9223372036854775807 AND binding_ordinal >= 0 AND binding_ordinal < 64");
                    table.CheckConstraint("ck_classification_event_adoption_identity", "id <> selection_id AND id <> generation_id AND selection_id <> generation_id");
                    table.CheckConstraint("ck_classification_event_adoption_independence", "adopted_by_human_id <> owner_human_id AND adopted_by_human_id <> owner_controller_human_id AND adopted_by_human_id <> reviewer_human_id AND adopted_by_human_id <> reviewer_controller_human_id AND adopted_by_human_id <> selection_authored_by_human_id");
                    table.CheckConstraint("ck_classification_event_adoption_revocation", "(revision = 1 AND revoked_at IS NULL AND revoked_by_human_id IS NULL AND revocation_writer_generation IS NULL AND revocation_grounds IS NULL) OR (revision = 2 AND revoked_at IS NOT NULL AND revoked_by_human_id IS NOT NULL AND revocation_writer_generation IS NOT NULL AND revocation_writer_generation > writer_generation AND revocation_writer_generation < 9223372036854775807 AND revocation_grounds IS NOT NULL AND length(trim(revocation_grounds)) > 0 AND length(revocation_grounds) <= 2048 AND revoked_at >= adopted_at)");
                    table.CheckConstraint("ck_classification_event_adoption_validity", "selection_valid_until > selection_recorded_at AND generation_valid_until > generation_observed_at AND valid_until > adopted_at AND adopted_at >= selection_recorded_at AND adopted_at >= generation_observed_at AND valid_until <= selection_valid_until AND valid_until <= generation_valid_until");
                    table.ForeignKey(
                        name: "fk_moderation_classification_event_target_adoption_0054faa4acba",
                        column: x => x.tenant_id,
                        principalSchema: "islamu_event",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_event_target_adoption_59684a0fd6f3",
                        column: x => x.host_binding_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_event_target_adoption_7960b4700edb",
                        column: x => x.generation_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_event_generations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_event_target_adoption_9bc0958a13ac",
                        column: x => x.adopted_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_event_target_adoption_b914137a25bc",
                        column: x => x.revoked_by_human_id,
                        principalSchema: "islamu_event",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_classification_event_target_adoption_f64dc11f727d",
                        columns: x => new { x.selection_id, x.binding_ordinal },
                        principalSchema: "islamu_event",
                        principalTable: "moderation_classification_control_bindings",
                        principalColumns: new[] { "selection_id", "ordinal" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "event_resource_moderation_quarantines",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effect_id = table.Column<Guid>(type: "uuid", nullable: false),
                    binding_ordinal = table.Column<int>(type: "integer", nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    release_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_resource_moderation_quarantines", x => x.id);
                    table.UniqueConstraint("ak_event_resource_moderation_quarantines_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_event_resource_quarantines_cause", "effect_id <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("ck_event_resource_quarantines_material", "material_version_id <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("ck_event_resource_quarantines_ordinal", "binding_ordinal >= 0 AND binding_ordinal < 64");
                    table.ForeignKey(
                        name: "fk_event_resource_moderation_quarantines_event_res_72b6c9b74250",
                        columns: x => new { x.tenant_id, x.event_id, x.event_resource_id },
                        principalSchema: "islamu_event",
                        principalTable: "event_resources",
                        principalColumns: new[] { "tenant_id", "event_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "event_resource_moderation_review_duties",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_resource_moderation_review_duties", x => x.id);
                    table.UniqueConstraint("ak_event_resource_moderation_review_duties_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_event_resource_moderation_review_duties_event_r_e102c9d9df2b",
                        columns: x => new { x.tenant_id, x.id },
                        principalSchema: "islamu_event",
                        principalTable: "event_resource_moderation_quarantines",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "event_resource_quarantine_reviews",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quarantine_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_revision = table.Column<long>(type: "bigint", nullable: false),
                    step = table.Column<int>(type: "integer", nullable: false),
                    purpose_id = table.Column<Guid>(type: "uuid", nullable: false),
                    predecessor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewer_assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewer_revision = table.Column<long>(type: "bigint", nullable: false),
                    controller_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    required_approvals = table.Column<int>(type: "integer", nullable: false),
                    maximum_evidence_lifetime_ticks = table.Column<long>(type: "bigint", nullable: false),
                    attestation = table.Column<int>(type: "integer", nullable: true),
                    resource_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    native_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    request_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    qualification_revision = table.Column<long>(type: "bigint", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_resource_quarantine_reviews", x => x.id);
                    table.UniqueConstraint("ak_event_resource_quarantine_reviews_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_resource_safety_lifetime", "maximum_evidence_lifetime_ticks > 0");
                    table.CheckConstraint("ck_resource_safety_quorum", "required_approvals BETWEEN 1 AND 2");
                    table.CheckConstraint("ck_resource_safety_step", "step BETWEEN 1 AND 6");
                    table.ForeignKey(
                        name: "fk_event_resource_quarantine_reviews_event_resourc_41efdbc33dbd",
                        columns: x => new { x.tenant_id, x.quarantine_id },
                        principalSchema: "islamu_event",
                        principalTable: "event_resource_moderation_quarantines",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "event_resource_moderation_review_escalations",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_principal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    qualification_revision = table.Column<long>(type: "bigint", nullable: false),
                    escalated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    purpose_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recipient_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_resource_moderation_review_escalations", x => x.id);
                    table.CheckConstraint("ck_resource_review_escalation_recipients", "recipient_count BETWEEN 1 AND 32");
                    table.ForeignKey(
                        name: "fk_event_resource_moderation_review_escalations_ev_70e856fd7f16",
                        columns: x => new { x.tenant_id, x.id },
                        principalSchema: "islamu_event",
                        principalTable: "event_resource_moderation_review_duties",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "event_resource_moderation_review_completions",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quarantine_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attestation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approval_id = table.Column<Guid>(type: "uuid", nullable: false),
                    disposition = table.Column<int>(type: "integer", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_resource_moderation_review_completions", x => x.id);
                    table.CheckConstraint("ck_resource_review_disposition", "disposition IN (1, 2)");
                    table.ForeignKey(
                        name: "fk_event_resource_moderation_review_completions_ev_2538526dcfe9",
                        columns: x => new { x.tenant_id, x.quarantine_id },
                        principalSchema: "islamu_event",
                        principalTable: "event_resource_moderation_quarantines",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_event_resource_moderation_review_completions_ev_47e6c22b7885",
                        columns: x => new { x.tenant_id, x.attestation_id },
                        principalSchema: "islamu_event",
                        principalTable: "event_resource_quarantine_reviews",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_event_resource_moderation_review_completions_ev_cbd930e88042",
                        columns: x => new { x.tenant_id, x.approval_id },
                        principalSchema: "islamu_event",
                        principalTable: "event_resource_quarantine_reviews",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "event_resource_quarantine_releases",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quarantine_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_effect_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_binding_ordinal = table.Column<int>(type: "integer", nullable: false),
                    originally_applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approval_id = table.Column<Guid>(type: "uuid", nullable: false),
                    released_by_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    request_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_resource_quarantine_releases", x => x.id);
                    table.UniqueConstraint("ak_event_resource_moderation_quarantine_release_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_event_resource_quarantine_releases_event_resour_85572c04b9bf",
                        columns: x => new { x.tenant_id, x.quarantine_id },
                        principalSchema: "islamu_event",
                        principalTable: "event_resource_moderation_quarantines",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_event_resource_quarantine_releases_event_resour_ce6667789579",
                        columns: x => new { x.tenant_id, x.approval_id },
                        principalSchema: "islamu_event",
                        principalTable: "event_resource_quarantine_reviews",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_recognition_action_recovery",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fresh_subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    verified_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    capture_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    capture_material_receipt = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    original_intent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    privacy_owner_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recovery_owner_human_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_kind = table.Column<int>(type: "integer", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    disposition_by = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_recognition_action_recovery", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "moderation_recognition_capture_item",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    intent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    erasure_subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_solution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_revision = table.Column<long>(type: "bigint", nullable: false),
                    source_generation = table.Column<long>(type: "bigint", nullable: false),
                    adoption_id = table.Column<Guid>(type: "uuid", nullable: false),
                    adoption_revision = table.Column<long>(type: "bigint", nullable: false),
                    adoption_generation = table.Column<long>(type: "bigint", nullable: false),
                    ring_generation = table.Column<long>(type: "bigint", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_kind = table.Column<int>(type: "integer", nullable: false),
                    purpose = table.Column<int>(type: "integer", nullable: false),
                    key_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    key_generation = table.Column<long>(type: "bigint", nullable: false),
                    serialization_version = table.Column<int>(type: "integer", nullable: false),
                    digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    key_verification_tag = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    sanction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sanction_revision = table.Column<long>(type: "bigint", nullable: false),
                    decision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision_revision = table.Column<long>(type: "bigint", nullable: false),
                    finding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_capture_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    continuity_binding_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rule_owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    rule_revision = table.Column<long>(type: "bigint", nullable: false),
                    original_capability_mask = table.Column<int>(type: "integer", nullable: false),
                    purpose_capability_mask = table.Column<int>(type: "integer", nullable: false),
                    scope_kind = table.Column<int>(type: "integer", nullable: false),
                    scope_solution_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scope_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scope_tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scope_community_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scope_resource_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scope_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    review_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    original_captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    retain_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    dropped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_recognition_capture_item", x => x.id);
                    table.CheckConstraint("ck_moderation_recognition_capture_item_scope_scope", "scope_kind IS NOT NULL AND ((scope_kind = 1 AND trust_domain_id IS NOT NULL AND scope_solution_id IS NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 2 AND trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 3 AND trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 4 AND trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 5 AND trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NOT NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 6 AND trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_resource_id IS NOT NULL AND scope_event_id IS NULL) OR (scope_kind = 7 AND trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NOT NULL AND scope_event_id <> '00000000-0000-0000-0000-000000000000'))");
                    table.ForeignKey(
                        name: "fk_moderation_recognition_capture_item_moderation__049d8fb46623",
                        column: x => x.intent_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_recognition_pending",
                        principalColumn: "intent_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_recognition_capture_item_moderation__ad9b4ec61f11",
                        column: x => x.parent_capture_item_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_recognition_capture_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_recognition_continuity_binding",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fresh_subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    capture_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    capture_material_receipt = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    sanction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    finding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bound_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    retain_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_recognition_continuity_binding", x => x.id);
                    table.ForeignKey(
                        name: "fk_moderation_recognition_continuity_binding_moder_07f5d4ef50db",
                        column: x => x.capture_item_id,
                        principalSchema: "islamu_event",
                        principalTable: "moderation_recognition_capture_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_sanctions_scope_scope",
                schema: "islamu_event",
                table: "moderation_sanctions",
                sql: "scope_kind IS NOT NULL AND ((scope_kind = 1 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 2 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 3 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 4 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 5 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NOT NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 6 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_resource_id IS NOT NULL AND scope_event_id IS NULL) OR (scope_kind = 7 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NOT NULL AND scope_event_id <> '00000000-0000-0000-0000-000000000000'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_sanctions_subject_origin_scope",
                schema: "islamu_event",
                table: "moderation_sanctions",
                sql: "subject_origin_kind IS NOT NULL AND ((subject_origin_kind = 1 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 2 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 3 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 4 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 5 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NOT NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 6 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_resource_id IS NOT NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 7 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NOT NULL AND subject_origin_event_id <> '00000000-0000-0000-0000-000000000000'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_qualification_receipt_result",
                schema: "islamu_event",
                table: "moderation_qualification_mutation_receipts",
                sql: "source_kind BETWEEN 1 AND 44 AND result_revision > 0 AND qualification_revision > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_protective_measures_issuing_scope_scope",
                schema: "islamu_event",
                table: "moderation_protective_measures",
                sql: "issuing_scope_kind IS NOT NULL AND ((issuing_scope_kind = 1 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NULL AND issuing_scope_instance_id IS NULL AND issuing_scope_tenant_id IS NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL AND issuing_scope_event_id IS NULL) OR (issuing_scope_kind = 2 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NULL AND issuing_scope_tenant_id IS NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL AND issuing_scope_event_id IS NULL) OR (issuing_scope_kind = 3 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL AND issuing_scope_event_id IS NULL) OR (issuing_scope_kind = 4 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NOT NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL AND issuing_scope_event_id IS NULL) OR (issuing_scope_kind = 5 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NOT NULL AND issuing_scope_community_id IS NOT NULL AND issuing_scope_resource_id IS NULL AND issuing_scope_event_id IS NULL) OR (issuing_scope_kind = 6 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NOT NULL AND issuing_scope_resource_id IS NOT NULL AND issuing_scope_event_id IS NULL) OR (issuing_scope_kind = 7 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NOT NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL AND issuing_scope_event_id IS NOT NULL AND issuing_scope_event_id <> '00000000-0000-0000-0000-000000000000'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_protective_measures_protector_origin_scope",
                schema: "islamu_event",
                table: "moderation_protective_measures",
                sql: "protector_origin_kind IS NOT NULL AND ((protector_origin_kind = 1 AND protector_origin_trust_domain_id IS NOT NULL AND protector_origin_solution_id IS NULL AND protector_origin_instance_id IS NULL AND protector_origin_tenant_id IS NULL AND protector_origin_community_id IS NULL AND protector_origin_resource_id IS NULL AND protector_origin_event_id IS NULL) OR (protector_origin_kind = 2 AND protector_origin_trust_domain_id IS NOT NULL AND protector_origin_solution_id IS NOT NULL AND protector_origin_instance_id IS NULL AND protector_origin_tenant_id IS NULL AND protector_origin_community_id IS NULL AND protector_origin_resource_id IS NULL AND protector_origin_event_id IS NULL) OR (protector_origin_kind = 3 AND protector_origin_trust_domain_id IS NOT NULL AND protector_origin_solution_id IS NOT NULL AND protector_origin_instance_id IS NOT NULL AND protector_origin_tenant_id IS NULL AND protector_origin_community_id IS NULL AND protector_origin_resource_id IS NULL AND protector_origin_event_id IS NULL) OR (protector_origin_kind = 4 AND protector_origin_trust_domain_id IS NOT NULL AND protector_origin_solution_id IS NOT NULL AND protector_origin_instance_id IS NOT NULL AND protector_origin_tenant_id IS NOT NULL AND protector_origin_community_id IS NULL AND protector_origin_resource_id IS NULL AND protector_origin_event_id IS NULL) OR (protector_origin_kind = 5 AND protector_origin_trust_domain_id IS NOT NULL AND protector_origin_solution_id IS NOT NULL AND protector_origin_instance_id IS NOT NULL AND protector_origin_tenant_id IS NOT NULL AND protector_origin_community_id IS NOT NULL AND protector_origin_resource_id IS NULL AND protector_origin_event_id IS NULL) OR (protector_origin_kind = 6 AND protector_origin_trust_domain_id IS NOT NULL AND protector_origin_solution_id IS NOT NULL AND protector_origin_instance_id IS NOT NULL AND protector_origin_tenant_id IS NOT NULL AND protector_origin_resource_id IS NOT NULL AND protector_origin_event_id IS NULL) OR (protector_origin_kind = 7 AND protector_origin_trust_domain_id IS NOT NULL AND protector_origin_solution_id IS NOT NULL AND protector_origin_instance_id IS NOT NULL AND protector_origin_tenant_id IS NOT NULL AND protector_origin_community_id IS NULL AND protector_origin_resource_id IS NULL AND protector_origin_event_id IS NOT NULL AND protector_origin_event_id <> '00000000-0000-0000-0000-000000000000'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_protective_measures_reviewer_origin_scope",
                schema: "islamu_event",
                table: "moderation_protective_measures",
                sql: "(reviewer_origin_kind IS NULL AND reviewer_origin_trust_domain_id IS NULL AND reviewer_origin_solution_id IS NULL AND reviewer_origin_instance_id IS NULL AND reviewer_origin_tenant_id IS NULL AND reviewer_origin_community_id IS NULL AND reviewer_origin_resource_id IS NULL AND reviewer_origin_event_id IS NULL) OR (reviewer_origin_kind IS NOT NULL AND ((reviewer_origin_kind = 1 AND reviewer_origin_trust_domain_id IS NOT NULL AND reviewer_origin_solution_id IS NULL AND reviewer_origin_instance_id IS NULL AND reviewer_origin_tenant_id IS NULL AND reviewer_origin_community_id IS NULL AND reviewer_origin_resource_id IS NULL AND reviewer_origin_event_id IS NULL) OR (reviewer_origin_kind = 2 AND reviewer_origin_trust_domain_id IS NOT NULL AND reviewer_origin_solution_id IS NOT NULL AND reviewer_origin_instance_id IS NULL AND reviewer_origin_tenant_id IS NULL AND reviewer_origin_community_id IS NULL AND reviewer_origin_resource_id IS NULL AND reviewer_origin_event_id IS NULL) OR (reviewer_origin_kind = 3 AND reviewer_origin_trust_domain_id IS NOT NULL AND reviewer_origin_solution_id IS NOT NULL AND reviewer_origin_instance_id IS NOT NULL AND reviewer_origin_tenant_id IS NULL AND reviewer_origin_community_id IS NULL AND reviewer_origin_resource_id IS NULL AND reviewer_origin_event_id IS NULL) OR (reviewer_origin_kind = 4 AND reviewer_origin_trust_domain_id IS NOT NULL AND reviewer_origin_solution_id IS NOT NULL AND reviewer_origin_instance_id IS NOT NULL AND reviewer_origin_tenant_id IS NOT NULL AND reviewer_origin_community_id IS NULL AND reviewer_origin_resource_id IS NULL AND reviewer_origin_event_id IS NULL) OR (reviewer_origin_kind = 5 AND reviewer_origin_trust_domain_id IS NOT NULL AND reviewer_origin_solution_id IS NOT NULL AND reviewer_origin_instance_id IS NOT NULL AND reviewer_origin_tenant_id IS NOT NULL AND reviewer_origin_community_id IS NOT NULL AND reviewer_origin_resource_id IS NULL AND reviewer_origin_event_id IS NULL) OR (reviewer_origin_kind = 6 AND reviewer_origin_trust_domain_id IS NOT NULL AND reviewer_origin_solution_id IS NOT NULL AND reviewer_origin_instance_id IS NOT NULL AND reviewer_origin_tenant_id IS NOT NULL AND reviewer_origin_resource_id IS NOT NULL AND reviewer_origin_event_id IS NULL) OR (reviewer_origin_kind = 7 AND reviewer_origin_trust_domain_id IS NOT NULL AND reviewer_origin_solution_id IS NOT NULL AND reviewer_origin_instance_id IS NOT NULL AND reviewer_origin_tenant_id IS NOT NULL AND reviewer_origin_community_id IS NULL AND reviewer_origin_resource_id IS NULL AND reviewer_origin_event_id IS NOT NULL AND reviewer_origin_event_id <> '00000000-0000-0000-0000-000000000000')))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_protective_measures_scope_scope",
                schema: "islamu_event",
                table: "moderation_protective_measures",
                sql: "scope_kind IS NOT NULL AND ((scope_kind = 1 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 2 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 3 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 4 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 5 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NOT NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 6 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_resource_id IS NOT NULL AND scope_event_id IS NULL) OR (scope_kind = 7 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NOT NULL AND scope_event_id <> '00000000-0000-0000-0000-000000000000'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_protective_measures_subject_origin_scope",
                schema: "islamu_event",
                table: "moderation_protective_measures",
                sql: "subject_origin_kind IS NOT NULL AND ((subject_origin_kind = 1 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 2 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 3 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 4 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 5 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NOT NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 6 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_resource_id IS NOT NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 7 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NOT NULL AND subject_origin_event_id <> '00000000-0000-0000-0000-000000000000'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_policies_jurisdiction_scope",
                schema: "islamu_event",
                table: "moderation_policies",
                sql: "jurisdiction_kind IS NOT NULL AND ((jurisdiction_kind = 1 AND jurisdiction_trust_domain_id IS NOT NULL AND jurisdiction_solution_id IS NULL AND jurisdiction_instance_id IS NULL AND jurisdiction_tenant_id IS NULL AND jurisdiction_community_id IS NULL AND jurisdiction_resource_id IS NULL AND jurisdiction_event_id IS NULL) OR (jurisdiction_kind = 2 AND jurisdiction_trust_domain_id IS NOT NULL AND jurisdiction_solution_id IS NOT NULL AND jurisdiction_instance_id IS NULL AND jurisdiction_tenant_id IS NULL AND jurisdiction_community_id IS NULL AND jurisdiction_resource_id IS NULL AND jurisdiction_event_id IS NULL) OR (jurisdiction_kind = 3 AND jurisdiction_trust_domain_id IS NOT NULL AND jurisdiction_solution_id IS NOT NULL AND jurisdiction_instance_id IS NOT NULL AND jurisdiction_tenant_id IS NULL AND jurisdiction_community_id IS NULL AND jurisdiction_resource_id IS NULL AND jurisdiction_event_id IS NULL) OR (jurisdiction_kind = 4 AND jurisdiction_trust_domain_id IS NOT NULL AND jurisdiction_solution_id IS NOT NULL AND jurisdiction_instance_id IS NOT NULL AND jurisdiction_tenant_id IS NOT NULL AND jurisdiction_community_id IS NULL AND jurisdiction_resource_id IS NULL AND jurisdiction_event_id IS NULL) OR (jurisdiction_kind = 5 AND jurisdiction_trust_domain_id IS NOT NULL AND jurisdiction_solution_id IS NOT NULL AND jurisdiction_instance_id IS NOT NULL AND jurisdiction_tenant_id IS NOT NULL AND jurisdiction_community_id IS NOT NULL AND jurisdiction_resource_id IS NULL AND jurisdiction_event_id IS NULL) OR (jurisdiction_kind = 6 AND jurisdiction_trust_domain_id IS NOT NULL AND jurisdiction_solution_id IS NOT NULL AND jurisdiction_instance_id IS NOT NULL AND jurisdiction_tenant_id IS NOT NULL AND jurisdiction_resource_id IS NOT NULL AND jurisdiction_event_id IS NULL) OR (jurisdiction_kind = 7 AND jurisdiction_trust_domain_id IS NOT NULL AND jurisdiction_solution_id IS NOT NULL AND jurisdiction_instance_id IS NOT NULL AND jurisdiction_tenant_id IS NOT NULL AND jurisdiction_community_id IS NULL AND jurisdiction_resource_id IS NULL AND jurisdiction_event_id IS NOT NULL AND jurisdiction_event_id <> '00000000-0000-0000-0000-000000000000'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_policy_jurisdiction",
                schema: "islamu_event",
                table: "moderation_policies",
                sql: "jurisdiction_id = CASE jurisdiction_kind WHEN 1 THEN jurisdiction_trust_domain_id WHEN 2 THEN jurisdiction_solution_id WHEN 3 THEN jurisdiction_instance_id WHEN 4 THEN jurisdiction_tenant_id WHEN 5 THEN jurisdiction_community_id WHEN 6 THEN jurisdiction_resource_id WHEN 7 THEN jurisdiction_event_id END");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_receipt_scope",
                schema: "islamu_event",
                table: "moderation_operation_receipts",
                sql: "scope_kind BETWEEN 1 AND 7 AND subject_kind BETWEEN 1 AND 4");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_findings_case_id_rule_owner_id_rule__c1f550c5320a",
                schema: "islamu_event",
                table: "moderation_findings",
                columns: new[] { "case_id", "rule_owner_id", "rule_code", "rule_revision", "scope_kind", "scope_id", "attribution_generation" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_finding_attribution",
                schema: "islamu_event",
                table: "moderation_findings",
                sql: "(attribution_source_id IS NULL AND attribution_generation = 0) OR (attribution_source_id IS NOT NULL AND attribution_generation > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_finding_evidence",
                schema: "islamu_event",
                table: "moderation_findings",
                sql: "case_revision = 1 AND rule_revision > 0 AND reason_revision > 0 AND evidence_recorded_at >= case_created_at AND evidence_recorded_at <= finalized_at AND finalized_at >= case_created_at AND ((evidence_purpose = 1 AND urgency_qualification_id IS NULL) OR (evidence_purpose = 2 AND urgency_qualification_id IS NOT NULL AND urgency_qualification_id <> evidence_reference_id))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_findings_adjudicator_origin_scope",
                schema: "islamu_event",
                table: "moderation_findings",
                sql: "adjudicator_origin_kind IS NOT NULL AND ((adjudicator_origin_kind = 1 AND adjudicator_origin_trust_domain_id IS NOT NULL AND adjudicator_origin_solution_id IS NULL AND adjudicator_origin_instance_id IS NULL AND adjudicator_origin_tenant_id IS NULL AND adjudicator_origin_community_id IS NULL AND adjudicator_origin_resource_id IS NULL AND adjudicator_origin_event_id IS NULL) OR (adjudicator_origin_kind = 2 AND adjudicator_origin_trust_domain_id IS NOT NULL AND adjudicator_origin_solution_id IS NOT NULL AND adjudicator_origin_instance_id IS NULL AND adjudicator_origin_tenant_id IS NULL AND adjudicator_origin_community_id IS NULL AND adjudicator_origin_resource_id IS NULL AND adjudicator_origin_event_id IS NULL) OR (adjudicator_origin_kind = 3 AND adjudicator_origin_trust_domain_id IS NOT NULL AND adjudicator_origin_solution_id IS NOT NULL AND adjudicator_origin_instance_id IS NOT NULL AND adjudicator_origin_tenant_id IS NULL AND adjudicator_origin_community_id IS NULL AND adjudicator_origin_resource_id IS NULL AND adjudicator_origin_event_id IS NULL) OR (adjudicator_origin_kind = 4 AND adjudicator_origin_trust_domain_id IS NOT NULL AND adjudicator_origin_solution_id IS NOT NULL AND adjudicator_origin_instance_id IS NOT NULL AND adjudicator_origin_tenant_id IS NOT NULL AND adjudicator_origin_community_id IS NULL AND adjudicator_origin_resource_id IS NULL AND adjudicator_origin_event_id IS NULL) OR (adjudicator_origin_kind = 5 AND adjudicator_origin_trust_domain_id IS NOT NULL AND adjudicator_origin_solution_id IS NOT NULL AND adjudicator_origin_instance_id IS NOT NULL AND adjudicator_origin_tenant_id IS NOT NULL AND adjudicator_origin_community_id IS NOT NULL AND adjudicator_origin_resource_id IS NULL AND adjudicator_origin_event_id IS NULL) OR (adjudicator_origin_kind = 6 AND adjudicator_origin_trust_domain_id IS NOT NULL AND adjudicator_origin_solution_id IS NOT NULL AND adjudicator_origin_instance_id IS NOT NULL AND adjudicator_origin_tenant_id IS NOT NULL AND adjudicator_origin_resource_id IS NOT NULL AND adjudicator_origin_event_id IS NULL) OR (adjudicator_origin_kind = 7 AND adjudicator_origin_trust_domain_id IS NOT NULL AND adjudicator_origin_solution_id IS NOT NULL AND adjudicator_origin_instance_id IS NOT NULL AND adjudicator_origin_tenant_id IS NOT NULL AND adjudicator_origin_community_id IS NULL AND adjudicator_origin_resource_id IS NULL AND adjudicator_origin_event_id IS NOT NULL AND adjudicator_origin_event_id <> '00000000-0000-0000-0000-000000000000'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_findings_reversed_by_origin_scope",
                schema: "islamu_event",
                table: "moderation_findings",
                sql: "(reversed_by_origin_kind IS NULL AND reversed_by_origin_trust_domain_id IS NULL AND reversed_by_origin_solution_id IS NULL AND reversed_by_origin_instance_id IS NULL AND reversed_by_origin_tenant_id IS NULL AND reversed_by_origin_community_id IS NULL AND reversed_by_origin_resource_id IS NULL AND reversed_by_origin_event_id IS NULL) OR (reversed_by_origin_kind IS NOT NULL AND ((reversed_by_origin_kind = 1 AND reversed_by_origin_trust_domain_id IS NOT NULL AND reversed_by_origin_solution_id IS NULL AND reversed_by_origin_instance_id IS NULL AND reversed_by_origin_tenant_id IS NULL AND reversed_by_origin_community_id IS NULL AND reversed_by_origin_resource_id IS NULL AND reversed_by_origin_event_id IS NULL) OR (reversed_by_origin_kind = 2 AND reversed_by_origin_trust_domain_id IS NOT NULL AND reversed_by_origin_solution_id IS NOT NULL AND reversed_by_origin_instance_id IS NULL AND reversed_by_origin_tenant_id IS NULL AND reversed_by_origin_community_id IS NULL AND reversed_by_origin_resource_id IS NULL AND reversed_by_origin_event_id IS NULL) OR (reversed_by_origin_kind = 3 AND reversed_by_origin_trust_domain_id IS NOT NULL AND reversed_by_origin_solution_id IS NOT NULL AND reversed_by_origin_instance_id IS NOT NULL AND reversed_by_origin_tenant_id IS NULL AND reversed_by_origin_community_id IS NULL AND reversed_by_origin_resource_id IS NULL AND reversed_by_origin_event_id IS NULL) OR (reversed_by_origin_kind = 4 AND reversed_by_origin_trust_domain_id IS NOT NULL AND reversed_by_origin_solution_id IS NOT NULL AND reversed_by_origin_instance_id IS NOT NULL AND reversed_by_origin_tenant_id IS NOT NULL AND reversed_by_origin_community_id IS NULL AND reversed_by_origin_resource_id IS NULL AND reversed_by_origin_event_id IS NULL) OR (reversed_by_origin_kind = 5 AND reversed_by_origin_trust_domain_id IS NOT NULL AND reversed_by_origin_solution_id IS NOT NULL AND reversed_by_origin_instance_id IS NOT NULL AND reversed_by_origin_tenant_id IS NOT NULL AND reversed_by_origin_community_id IS NOT NULL AND reversed_by_origin_resource_id IS NULL AND reversed_by_origin_event_id IS NULL) OR (reversed_by_origin_kind = 6 AND reversed_by_origin_trust_domain_id IS NOT NULL AND reversed_by_origin_solution_id IS NOT NULL AND reversed_by_origin_instance_id IS NOT NULL AND reversed_by_origin_tenant_id IS NOT NULL AND reversed_by_origin_resource_id IS NOT NULL AND reversed_by_origin_event_id IS NULL) OR (reversed_by_origin_kind = 7 AND reversed_by_origin_trust_domain_id IS NOT NULL AND reversed_by_origin_solution_id IS NOT NULL AND reversed_by_origin_instance_id IS NOT NULL AND reversed_by_origin_tenant_id IS NOT NULL AND reversed_by_origin_community_id IS NULL AND reversed_by_origin_resource_id IS NULL AND reversed_by_origin_event_id IS NOT NULL AND reversed_by_origin_event_id <> '00000000-0000-0000-0000-000000000000')))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_findings_scope_scope",
                schema: "islamu_event",
                table: "moderation_findings",
                sql: "scope_kind IS NOT NULL AND ((scope_kind = 1 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 2 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 3 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 4 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 5 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NOT NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 6 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_resource_id IS NOT NULL AND scope_event_id IS NULL) OR (scope_kind = 7 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NOT NULL AND scope_event_id <> '00000000-0000-0000-0000-000000000000'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_findings_subject_origin_scope",
                schema: "islamu_event",
                table: "moderation_findings",
                sql: "subject_origin_kind IS NOT NULL AND ((subject_origin_kind = 1 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 2 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 3 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 4 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 5 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NOT NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 6 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_resource_id IS NOT NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 7 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NOT NULL AND subject_origin_event_id <> '00000000-0000-0000-0000-000000000000'))");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_effect_intents_completed_at_lease_expires_at_id",
                schema: "islamu_event",
                table: "moderation_effect_intents",
                columns: new[] { "completed_at", "lease_expires_at", "id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_effect_completed_lease",
                schema: "islamu_event",
                table: "moderation_effect_intents",
                sql: "completed_at IS NULL OR lease_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_effect_completion_window",
                schema: "islamu_event",
                table: "moderation_effect_intents",
                sql: "completed_at IS NULL OR (completed_at >= lease_started_at AND completed_at < lease_expires_at)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_effect_lease",
                schema: "islamu_event",
                table: "moderation_effect_intents",
                sql: "(lease_id IS NULL AND lease_started_at IS NULL AND lease_expires_at IS NULL) OR (lease_id IS NOT NULL AND lease_started_at IS NOT NULL AND lease_expires_at IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_effect_lease_window",
                schema: "islamu_event",
                table: "moderation_effect_intents",
                sql: "lease_id IS NULL OR (lease_started_at >= created_at AND lease_expires_at > lease_started_at)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_effect_notice_cleanup",
                schema: "islamu_event",
                table: "moderation_effect_intents",
                sql: "(original_notice_cleanup_id IS NULL AND original_notice_host_binding_id IS NULL AND original_notice_writer_tenant_id IS NULL AND original_notice_retain_until IS NULL AND original_notice_purged_at IS NULL) OR (original_notice_cleanup_id IS NOT NULL AND original_notice_host_binding_id IS NOT NULL AND original_notice_writer_tenant_id IS NOT NULL AND original_notice_retain_until IS NOT NULL AND original_notice_purged_at IS NOT NULL AND original_notice_retain_until > created_at AND original_notice_purged_at >= original_notice_retain_until AND revision > 1)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_effect_original_completion",
                schema: "islamu_event",
                table: "moderation_effect_intents",
                sql: "(original_completion_id IS NULL AND original_completion_host_binding_id IS NULL AND original_completion_writer_tenant_id IS NULL AND original_completion_revision IS NULL) OR (original_completion_id IS NOT NULL AND original_completion_host_binding_id IS NOT NULL AND original_completion_writer_tenant_id IS NOT NULL AND original_completion_revision IS NOT NULL AND completed_at IS NOT NULL AND original_completion_revision >= 3 AND original_completion_revision <= revision)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_decisions_approval1_origin_scope",
                schema: "islamu_event",
                table: "moderation_decisions",
                sql: "(approval1_origin_kind IS NULL AND approval1_origin_trust_domain_id IS NULL AND approval1_origin_solution_id IS NULL AND approval1_origin_instance_id IS NULL AND approval1_origin_tenant_id IS NULL AND approval1_origin_community_id IS NULL AND approval1_origin_resource_id IS NULL AND approval1_origin_event_id IS NULL) OR (approval1_origin_kind IS NOT NULL AND ((approval1_origin_kind = 1 AND approval1_origin_trust_domain_id IS NOT NULL AND approval1_origin_solution_id IS NULL AND approval1_origin_instance_id IS NULL AND approval1_origin_tenant_id IS NULL AND approval1_origin_community_id IS NULL AND approval1_origin_resource_id IS NULL AND approval1_origin_event_id IS NULL) OR (approval1_origin_kind = 2 AND approval1_origin_trust_domain_id IS NOT NULL AND approval1_origin_solution_id IS NOT NULL AND approval1_origin_instance_id IS NULL AND approval1_origin_tenant_id IS NULL AND approval1_origin_community_id IS NULL AND approval1_origin_resource_id IS NULL AND approval1_origin_event_id IS NULL) OR (approval1_origin_kind = 3 AND approval1_origin_trust_domain_id IS NOT NULL AND approval1_origin_solution_id IS NOT NULL AND approval1_origin_instance_id IS NOT NULL AND approval1_origin_tenant_id IS NULL AND approval1_origin_community_id IS NULL AND approval1_origin_resource_id IS NULL AND approval1_origin_event_id IS NULL) OR (approval1_origin_kind = 4 AND approval1_origin_trust_domain_id IS NOT NULL AND approval1_origin_solution_id IS NOT NULL AND approval1_origin_instance_id IS NOT NULL AND approval1_origin_tenant_id IS NOT NULL AND approval1_origin_community_id IS NULL AND approval1_origin_resource_id IS NULL AND approval1_origin_event_id IS NULL) OR (approval1_origin_kind = 5 AND approval1_origin_trust_domain_id IS NOT NULL AND approval1_origin_solution_id IS NOT NULL AND approval1_origin_instance_id IS NOT NULL AND approval1_origin_tenant_id IS NOT NULL AND approval1_origin_community_id IS NOT NULL AND approval1_origin_resource_id IS NULL AND approval1_origin_event_id IS NULL) OR (approval1_origin_kind = 6 AND approval1_origin_trust_domain_id IS NOT NULL AND approval1_origin_solution_id IS NOT NULL AND approval1_origin_instance_id IS NOT NULL AND approval1_origin_tenant_id IS NOT NULL AND approval1_origin_resource_id IS NOT NULL AND approval1_origin_event_id IS NULL) OR (approval1_origin_kind = 7 AND approval1_origin_trust_domain_id IS NOT NULL AND approval1_origin_solution_id IS NOT NULL AND approval1_origin_instance_id IS NOT NULL AND approval1_origin_tenant_id IS NOT NULL AND approval1_origin_community_id IS NULL AND approval1_origin_resource_id IS NULL AND approval1_origin_event_id IS NOT NULL AND approval1_origin_event_id <> '00000000-0000-0000-0000-000000000000')))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_decisions_approval2_origin_scope",
                schema: "islamu_event",
                table: "moderation_decisions",
                sql: "(approval2_origin_kind IS NULL AND approval2_origin_trust_domain_id IS NULL AND approval2_origin_solution_id IS NULL AND approval2_origin_instance_id IS NULL AND approval2_origin_tenant_id IS NULL AND approval2_origin_community_id IS NULL AND approval2_origin_resource_id IS NULL AND approval2_origin_event_id IS NULL) OR (approval2_origin_kind IS NOT NULL AND ((approval2_origin_kind = 1 AND approval2_origin_trust_domain_id IS NOT NULL AND approval2_origin_solution_id IS NULL AND approval2_origin_instance_id IS NULL AND approval2_origin_tenant_id IS NULL AND approval2_origin_community_id IS NULL AND approval2_origin_resource_id IS NULL AND approval2_origin_event_id IS NULL) OR (approval2_origin_kind = 2 AND approval2_origin_trust_domain_id IS NOT NULL AND approval2_origin_solution_id IS NOT NULL AND approval2_origin_instance_id IS NULL AND approval2_origin_tenant_id IS NULL AND approval2_origin_community_id IS NULL AND approval2_origin_resource_id IS NULL AND approval2_origin_event_id IS NULL) OR (approval2_origin_kind = 3 AND approval2_origin_trust_domain_id IS NOT NULL AND approval2_origin_solution_id IS NOT NULL AND approval2_origin_instance_id IS NOT NULL AND approval2_origin_tenant_id IS NULL AND approval2_origin_community_id IS NULL AND approval2_origin_resource_id IS NULL AND approval2_origin_event_id IS NULL) OR (approval2_origin_kind = 4 AND approval2_origin_trust_domain_id IS NOT NULL AND approval2_origin_solution_id IS NOT NULL AND approval2_origin_instance_id IS NOT NULL AND approval2_origin_tenant_id IS NOT NULL AND approval2_origin_community_id IS NULL AND approval2_origin_resource_id IS NULL AND approval2_origin_event_id IS NULL) OR (approval2_origin_kind = 5 AND approval2_origin_trust_domain_id IS NOT NULL AND approval2_origin_solution_id IS NOT NULL AND approval2_origin_instance_id IS NOT NULL AND approval2_origin_tenant_id IS NOT NULL AND approval2_origin_community_id IS NOT NULL AND approval2_origin_resource_id IS NULL AND approval2_origin_event_id IS NULL) OR (approval2_origin_kind = 6 AND approval2_origin_trust_domain_id IS NOT NULL AND approval2_origin_solution_id IS NOT NULL AND approval2_origin_instance_id IS NOT NULL AND approval2_origin_tenant_id IS NOT NULL AND approval2_origin_resource_id IS NOT NULL AND approval2_origin_event_id IS NULL) OR (approval2_origin_kind = 7 AND approval2_origin_trust_domain_id IS NOT NULL AND approval2_origin_solution_id IS NOT NULL AND approval2_origin_instance_id IS NOT NULL AND approval2_origin_tenant_id IS NOT NULL AND approval2_origin_community_id IS NULL AND approval2_origin_resource_id IS NULL AND approval2_origin_event_id IS NOT NULL AND approval2_origin_event_id <> '00000000-0000-0000-0000-000000000000')))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_decisions_issuer_origin_scope",
                schema: "islamu_event",
                table: "moderation_decisions",
                sql: "issuer_origin_kind IS NOT NULL AND ((issuer_origin_kind = 1 AND issuer_origin_trust_domain_id IS NOT NULL AND issuer_origin_solution_id IS NULL AND issuer_origin_instance_id IS NULL AND issuer_origin_tenant_id IS NULL AND issuer_origin_community_id IS NULL AND issuer_origin_resource_id IS NULL AND issuer_origin_event_id IS NULL) OR (issuer_origin_kind = 2 AND issuer_origin_trust_domain_id IS NOT NULL AND issuer_origin_solution_id IS NOT NULL AND issuer_origin_instance_id IS NULL AND issuer_origin_tenant_id IS NULL AND issuer_origin_community_id IS NULL AND issuer_origin_resource_id IS NULL AND issuer_origin_event_id IS NULL) OR (issuer_origin_kind = 3 AND issuer_origin_trust_domain_id IS NOT NULL AND issuer_origin_solution_id IS NOT NULL AND issuer_origin_instance_id IS NOT NULL AND issuer_origin_tenant_id IS NULL AND issuer_origin_community_id IS NULL AND issuer_origin_resource_id IS NULL AND issuer_origin_event_id IS NULL) OR (issuer_origin_kind = 4 AND issuer_origin_trust_domain_id IS NOT NULL AND issuer_origin_solution_id IS NOT NULL AND issuer_origin_instance_id IS NOT NULL AND issuer_origin_tenant_id IS NOT NULL AND issuer_origin_community_id IS NULL AND issuer_origin_resource_id IS NULL AND issuer_origin_event_id IS NULL) OR (issuer_origin_kind = 5 AND issuer_origin_trust_domain_id IS NOT NULL AND issuer_origin_solution_id IS NOT NULL AND issuer_origin_instance_id IS NOT NULL AND issuer_origin_tenant_id IS NOT NULL AND issuer_origin_community_id IS NOT NULL AND issuer_origin_resource_id IS NULL AND issuer_origin_event_id IS NULL) OR (issuer_origin_kind = 6 AND issuer_origin_trust_domain_id IS NOT NULL AND issuer_origin_solution_id IS NOT NULL AND issuer_origin_instance_id IS NOT NULL AND issuer_origin_tenant_id IS NOT NULL AND issuer_origin_resource_id IS NOT NULL AND issuer_origin_event_id IS NULL) OR (issuer_origin_kind = 7 AND issuer_origin_trust_domain_id IS NOT NULL AND issuer_origin_solution_id IS NOT NULL AND issuer_origin_instance_id IS NOT NULL AND issuer_origin_tenant_id IS NOT NULL AND issuer_origin_community_id IS NULL AND issuer_origin_resource_id IS NULL AND issuer_origin_event_id IS NOT NULL AND issuer_origin_event_id <> '00000000-0000-0000-0000-000000000000'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_decisions_issuing_scope_scope",
                schema: "islamu_event",
                table: "moderation_decisions",
                sql: "issuing_scope_kind IS NOT NULL AND ((issuing_scope_kind = 1 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NULL AND issuing_scope_instance_id IS NULL AND issuing_scope_tenant_id IS NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL AND issuing_scope_event_id IS NULL) OR (issuing_scope_kind = 2 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NULL AND issuing_scope_tenant_id IS NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL AND issuing_scope_event_id IS NULL) OR (issuing_scope_kind = 3 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL AND issuing_scope_event_id IS NULL) OR (issuing_scope_kind = 4 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NOT NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL AND issuing_scope_event_id IS NULL) OR (issuing_scope_kind = 5 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NOT NULL AND issuing_scope_community_id IS NOT NULL AND issuing_scope_resource_id IS NULL AND issuing_scope_event_id IS NULL) OR (issuing_scope_kind = 6 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NOT NULL AND issuing_scope_resource_id IS NOT NULL AND issuing_scope_event_id IS NULL) OR (issuing_scope_kind = 7 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NOT NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL AND issuing_scope_event_id IS NOT NULL AND issuing_scope_event_id <> '00000000-0000-0000-0000-000000000000'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_decisions_scope_scope",
                schema: "islamu_event",
                table: "moderation_decisions",
                sql: "scope_kind IS NOT NULL AND ((scope_kind = 1 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 2 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 3 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 4 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 5 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NOT NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 6 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_resource_id IS NOT NULL AND scope_event_id IS NULL) OR (scope_kind = 7 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NOT NULL AND scope_event_id <> '00000000-0000-0000-0000-000000000000'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_decisions_subject_origin_scope",
                schema: "islamu_event",
                table: "moderation_decisions",
                sql: "subject_origin_kind IS NOT NULL AND ((subject_origin_kind = 1 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 2 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 3 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 4 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 5 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NOT NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 6 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_resource_id IS NOT NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 7 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NOT NULL AND subject_origin_event_id <> '00000000-0000-0000-0000-000000000000'))");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_cases_occurrence_case_id",
                schema: "islamu_event",
                table: "moderation_cases",
                column: "occurrence_case_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_cases_scope_trust_domain_id_scope_te_39d64d68b146",
                schema: "islamu_event",
                table: "moderation_cases",
                columns: new[] { "scope_trust_domain_id", "scope_tenant_id", "occurrence_id", "occurrence_owner_kind", "occurrence_owner_id" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_case_attribution",
                schema: "islamu_event",
                table: "moderation_cases",
                sql: "(occurrence_case_id IS NULL AND occurrence_evidence_revision IS NULL AND attribution_source_id IS NULL) OR (occurrence_case_id IS NOT NULL AND occurrence_case_id <> id AND occurrence_evidence_revision > 0 AND attribution_source_id IS NOT NULL AND subject_id IS NOT NULL AND scope_kind = 4)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_case_occurrence_owner",
                schema: "islamu_event",
                table: "moderation_cases",
                sql: "(subject_id IS NULL AND occurrence_owner_kind = 0 AND occurrence_owner_id = '00000000-0000-0000-0000-000000000000') OR (subject_id IS NOT NULL AND occurrence_owner_kind = subject_kind AND occurrence_owner_id = subject_id)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_case_ownership",
                schema: "islamu_event",
                table: "moderation_cases",
                sql: "(subject_id IS NOT NULL AND target_kind IS NULL AND target_id IS NULL AND target_version IS NULL AND target_origin_kind IS NULL) OR (subject_id IS NULL AND target_kind IS NOT NULL AND target_kind BETWEEN 1 AND 9 AND target_id IS NOT NULL AND target_version IS NOT NULL AND target_origin_kind IS NOT NULL AND scope_kind = 4 AND scope_tenant_id IS NOT NULL AND target_origin_trust_domain_id = scope_trust_domain_id AND target_origin_solution_id = scope_solution_id AND target_origin_instance_id = scope_instance_id AND ((target_kind IN (3, 4) AND target_origin_kind = 3) OR (target_kind NOT IN (3, 4) AND target_origin_kind = 4 AND target_origin_tenant_id = scope_tenant_id)))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_cases_scope_scope",
                schema: "islamu_event",
                table: "moderation_cases",
                sql: "scope_kind IS NOT NULL AND ((scope_kind = 1 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 2 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 3 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 4 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 5 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NOT NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 6 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_resource_id IS NOT NULL AND scope_event_id IS NULL) OR (scope_kind = 7 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NOT NULL AND scope_event_id <> '00000000-0000-0000-0000-000000000000'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_cases_subject_origin_scope",
                schema: "islamu_event",
                table: "moderation_cases",
                sql: "(subject_origin_kind IS NULL AND subject_origin_trust_domain_id IS NULL AND subject_origin_solution_id IS NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind IS NOT NULL AND ((subject_origin_kind = 1 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 2 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 3 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 4 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 5 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NOT NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 6 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_resource_id IS NOT NULL AND subject_origin_event_id IS NULL) OR (subject_origin_kind = 7 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL AND subject_origin_event_id IS NOT NULL AND subject_origin_event_id <> '00000000-0000-0000-0000-000000000000')))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_cases_subject_subject",
                schema: "islamu_event",
                table: "moderation_cases",
                sql: "(subject_kind IS NULL AND subject_id IS NULL AND subject_origin_kind IS NULL) OR (subject_kind IS NOT NULL AND subject_kind BETWEEN 1 AND 4 AND subject_id IS NOT NULL AND subject_origin_kind IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_cases_target_origin_scope",
                schema: "islamu_event",
                table: "moderation_cases",
                sql: "(target_origin_kind IS NULL AND target_origin_trust_domain_id IS NULL AND target_origin_solution_id IS NULL AND target_origin_instance_id IS NULL AND target_origin_tenant_id IS NULL AND target_origin_community_id IS NULL AND target_origin_resource_id IS NULL AND target_origin_event_id IS NULL) OR (target_origin_kind IS NOT NULL AND ((target_origin_kind = 1 AND target_origin_trust_domain_id IS NOT NULL AND target_origin_solution_id IS NULL AND target_origin_instance_id IS NULL AND target_origin_tenant_id IS NULL AND target_origin_community_id IS NULL AND target_origin_resource_id IS NULL AND target_origin_event_id IS NULL) OR (target_origin_kind = 2 AND target_origin_trust_domain_id IS NOT NULL AND target_origin_solution_id IS NOT NULL AND target_origin_instance_id IS NULL AND target_origin_tenant_id IS NULL AND target_origin_community_id IS NULL AND target_origin_resource_id IS NULL AND target_origin_event_id IS NULL) OR (target_origin_kind = 3 AND target_origin_trust_domain_id IS NOT NULL AND target_origin_solution_id IS NOT NULL AND target_origin_instance_id IS NOT NULL AND target_origin_tenant_id IS NULL AND target_origin_community_id IS NULL AND target_origin_resource_id IS NULL AND target_origin_event_id IS NULL) OR (target_origin_kind = 4 AND target_origin_trust_domain_id IS NOT NULL AND target_origin_solution_id IS NOT NULL AND target_origin_instance_id IS NOT NULL AND target_origin_tenant_id IS NOT NULL AND target_origin_community_id IS NULL AND target_origin_resource_id IS NULL AND target_origin_event_id IS NULL) OR (target_origin_kind = 5 AND target_origin_trust_domain_id IS NOT NULL AND target_origin_solution_id IS NOT NULL AND target_origin_instance_id IS NOT NULL AND target_origin_tenant_id IS NOT NULL AND target_origin_community_id IS NOT NULL AND target_origin_resource_id IS NULL AND target_origin_event_id IS NULL) OR (target_origin_kind = 6 AND target_origin_trust_domain_id IS NOT NULL AND target_origin_solution_id IS NOT NULL AND target_origin_instance_id IS NOT NULL AND target_origin_tenant_id IS NOT NULL AND target_origin_resource_id IS NOT NULL AND target_origin_event_id IS NULL) OR (target_origin_kind = 7 AND target_origin_trust_domain_id IS NOT NULL AND target_origin_solution_id IS NOT NULL AND target_origin_instance_id IS NOT NULL AND target_origin_tenant_id IS NOT NULL AND target_origin_community_id IS NULL AND target_origin_resource_id IS NULL AND target_origin_event_id IS NOT NULL AND target_origin_event_id <> '00000000-0000-0000-0000-000000000000')))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_event_resources_material",
                schema: "islamu_event",
                table: "event_resources",
                sql: "material_version_id IS NULL OR material_version_id <> '00000000-0000-0000-0000-000000000000'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_event_resources_quarantine",
                schema: "islamu_event",
                table: "event_resources",
                sql: "moderation_quarantine_count >= 0 AND (moderation_quarantine_count = 0 OR material_version_id IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_event_report_signals_tenant_id_incoming_webhook_message_id",
                schema: "islamu_event",
                table: "event_report_signals",
                columns: new[] { "tenant_id", "incoming_webhook_message_id" });

            migrationBuilder.CreateIndex(
                name: "ix_event_report_signals_tenant_id_provider_advisor_b572b1f71201",
                schema: "islamu_event",
                table: "event_report_signals",
                columns: new[] { "tenant_id", "provider", "advisory_identity", "advisory_ordinal" },
                unique: true,
                filter: "advisory_identity IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_event_report_signals_tenant_id_provider_provide_a8cb58cd4436",
                schema: "islamu_event",
                table: "event_report_signals",
                columns: new[] { "tenant_id", "provider", "provider_target_scope", "provider_target_id", "external_signal_id" },
                unique: true,
                filter: "external_signal_id IS NOT NULL AND advisory_identity IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_event_report_signals_tenant_id_provider_provide_a8e5d26e7346",
                schema: "islamu_event",
                table: "event_report_signals",
                columns: new[] { "tenant_id", "provider", "provider_target_scope", "provider_target_id", "correlation_id" },
                unique: true,
                filter: "advisory_identity IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_event_report_signals_tenant_id_report_id_case_id",
                schema: "islamu_event",
                table: "event_report_signals",
                columns: new[] { "tenant_id", "report_id", "case_id" });

            migrationBuilder.CreateIndex(
                name: "ix_event_report_signals_tenant_id_report_id_provider_link_id",
                schema: "islamu_event",
                table: "event_report_signals",
                columns: new[] { "tenant_id", "report_id", "provider_link_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_event_report_signals_advisory_source",
                schema: "islamu_event",
                table: "event_report_signals",
                sql: "(advisory_identity IS NULL AND advisory_ordinal IS NULL AND advisory_material_sha256 IS NULL AND case_id IS NULL AND source_case_concurrency_stamp IS NULL AND provider_link_id IS NULL AND incoming_webhook_message_id IS NULL) OR (advisory_identity IS NOT NULL AND length(btrim(advisory_identity)) > 0 AND advisory_ordinal IS NOT NULL AND advisory_ordinal >= 0 AND advisory_material_sha256 IS NOT NULL AND length(advisory_material_sha256) = 71 AND report_id IS NOT NULL AND case_id IS NOT NULL AND source_case_concurrency_stamp IS NOT NULL AND source_case_concurrency_stamp <> '00000000-0000-0000-0000-000000000000' AND provider_link_id IS NOT NULL AND ((provider = 3 AND incoming_webhook_message_id IS NOT NULL) OR (provider = 2 AND incoming_webhook_message_id IS NULL)))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_event_report_external_links_case_generation",
                schema: "islamu_event",
                table: "event_report_external_links",
                sql: "(case_id IS NULL AND case_concurrency_stamp IS NULL) OR (case_id IS NOT NULL AND case_concurrency_stamp IS NOT NULL AND case_concurrency_stamp <> '00000000-0000-0000-0000-000000000000')");

            migrationBuilder.CreateIndex(
                name: "ix_event_resource_moderation_quarantines_tenant_id_1189ff377ed1",
                schema: "islamu_event",
                table: "event_resource_moderation_quarantines",
                columns: new[] { "tenant_id", "event_resource_id", "effect_id", "binding_ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_event_resource_moderation_quarantines_tenant_id_5eb95d4722d4",
                schema: "islamu_event",
                table: "event_resource_moderation_quarantines",
                columns: new[] { "tenant_id", "event_id", "event_resource_id" });

            migrationBuilder.CreateIndex(
                name: "ix_event_resource_moderation_quarantines_tenant_id_release_id",
                schema: "islamu_event",
                table: "event_resource_moderation_quarantines",
                columns: new[] { "tenant_id", "release_id" });

            migrationBuilder.CreateIndex(
                name: "ix_event_resource_moderation_review_completions_te_373552325161",
                schema: "islamu_event",
                table: "event_resource_moderation_review_completions",
                columns: new[] { "tenant_id", "approval_id" });

            migrationBuilder.CreateIndex(
                name: "ix_event_resource_moderation_review_completions_te_5d0b743f19bd",
                schema: "islamu_event",
                table: "event_resource_moderation_review_completions",
                columns: new[] { "tenant_id", "attestation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_event_resource_moderation_review_completions_te_fc0346df8cb2",
                schema: "islamu_event",
                table: "event_resource_moderation_review_completions",
                columns: new[] { "tenant_id", "quarantine_id", "completed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_event_resource_moderation_review_completions_te_ff1d779b3bd8",
                schema: "islamu_event",
                table: "event_resource_moderation_review_completions",
                columns: new[] { "tenant_id", "purpose_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_event_resource_moderation_review_duties_tenant_id_due_at_id",
                schema: "islamu_event",
                table: "event_resource_moderation_review_duties",
                columns: new[] { "tenant_id", "due_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_event_resource_moderation_review_escalations_tenant_id_id",
                schema: "islamu_event",
                table: "event_resource_moderation_review_escalations",
                columns: new[] { "tenant_id", "id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_event_resource_quarantine_releases_quarantine_id",
                schema: "islamu_event",
                table: "event_resource_quarantine_releases",
                column: "quarantine_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_event_resource_quarantine_releases_tenant_id_approval_id",
                schema: "islamu_event",
                table: "event_resource_quarantine_releases",
                columns: new[] { "tenant_id", "approval_id" });

            migrationBuilder.CreateIndex(
                name: "ix_event_resource_quarantine_releases_tenant_id_quarantine_id",
                schema: "islamu_event",
                table: "event_resource_quarantine_releases",
                columns: new[] { "tenant_id", "quarantine_id" });

            migrationBuilder.CreateIndex(
                name: "ix_event_resource_quarantine_reviews_tenant_id_qua_9f7cc8c6de43",
                schema: "islamu_event",
                table: "event_resource_quarantine_reviews",
                columns: new[] { "tenant_id", "quarantine_id", "purpose_id" });

            migrationBuilder.CreateIndex(
                name: "ix_key_obligation_intent_id",
                schema: "islamu_event",
                table: "key_obligation",
                column: "intent_id");

            migrationBuilder.CreateIndex(
                name: "ix_key_obligation_trust_domain_id_key_id_key_generation",
                schema: "islamu_event",
                table: "key_obligation",
                columns: new[] { "trust_domain_id", "key_id", "key_generation" });

            migrationBuilder.CreateIndex(
                name: "ix_key_replica_trust_domain_id_runtime_replica_id__2695344e3a8d",
                schema: "islamu_event",
                table: "key_replica",
                columns: new[] { "trust_domain_id", "runtime_replica_id", "ring_generation" });

            migrationBuilder.CreateIndex(
                name: "ix_key_replica_trust_domain_id_sequence",
                schema: "islamu_event",
                table: "key_replica",
                columns: new[] { "trust_domain_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_attribution_heads_case_id",
                schema: "islamu_event",
                table: "moderation_case_attribution_heads",
                column: "case_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_attribution_heads_host_binding_id",
                schema: "islamu_event",
                table: "moderation_case_attribution_heads",
                column: "host_binding_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_attribution_heads_occurrence_case_id",
                schema: "islamu_event",
                table: "moderation_case_attribution_heads",
                column: "occurrence_case_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_attribution_heads_opening_source_id",
                schema: "islamu_event",
                table: "moderation_case_attribution_heads",
                column: "opening_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_attribution_heads_source_id",
                schema: "islamu_event",
                table: "moderation_case_attribution_heads",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_attribution_heads_tenant_id_tru_b5d0cb8838f9",
                schema: "islamu_event",
                table: "moderation_case_attribution_heads",
                columns: new[] { "tenant_id", "trust_domain_id", "occurrence_case_id", "subject_kind", "subject_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_attribution_renewal_receipts_ca_39ef0ecb6d6a",
                schema: "islamu_event",
                table: "moderation_case_attribution_renewal_receipts",
                columns: new[] { "case_id", "expected_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_attribution_renewal_receipts_ex_e69a0b552b39",
                schema: "islamu_event",
                table: "moderation_case_attribution_renewal_receipts",
                column: "expected_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_attribution_renewal_receipts_he_e57e6c502937",
                schema: "islamu_event",
                table: "moderation_case_attribution_renewal_receipts",
                columns: new[] { "head_id", "expected_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_attribution_renewal_receipts_tenant_id",
                schema: "islamu_event",
                table: "moderation_case_attribution_renewal_receipts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_attribution_sources_case_id_generation",
                schema: "islamu_event",
                table: "moderation_case_attribution_sources",
                columns: new[] { "case_id", "generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_attribution_sources_evidence_source_id",
                schema: "islamu_event",
                table: "moderation_case_attribution_sources",
                column: "evidence_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_attribution_sources_occurrence_case_id",
                schema: "islamu_event",
                table: "moderation_case_attribution_sources",
                column: "occurrence_case_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_attribution_sources_read_grant_id",
                schema: "islamu_event",
                table: "moderation_case_attribution_sources",
                column: "read_grant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_attribution_sources_tenant_id_t_ce8706454c21",
                schema: "islamu_event",
                table: "moderation_case_attribution_sources",
                columns: new[] { "tenant_id", "trust_domain_id", "occurrence_case_id", "subject_kind", "subject_id", "generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_intake_acknowledgements_budget__32655b21d04d",
                schema: "islamu_event",
                table: "moderation_case_intake_acknowledgements",
                columns: new[] { "budget_adoption_id", "trust_domain_id", "tenant_id", "budget_source_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_intake_acknowledgements_case_id",
                schema: "islamu_event",
                table: "moderation_case_intake_acknowledgements",
                column: "case_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_intake_acknowledgements_receipt_id",
                schema: "islamu_event",
                table: "moderation_case_intake_acknowledgements",
                column: "receipt_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_intake_acknowledgements_tenant__723dd2ac5ac1",
                schema: "islamu_event",
                table: "moderation_case_intake_acknowledgements",
                columns: new[] { "tenant_id", "caller_user_id", "operation_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_narrative_adoptions_adopted_by_human_id",
                schema: "islamu_event",
                table: "moderation_case_narrative_adoptions",
                column: "adopted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_narrative_adoptions_source_id",
                schema: "islamu_event",
                table: "moderation_case_narrative_adoptions",
                column: "source_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_narrative_adoptions_source_id_h_02030df21f4f",
                schema: "islamu_event",
                table: "moderation_case_narrative_adoptions",
                columns: new[] { "source_id", "host_binding_id", "trust_domain_id", "tenant_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_narrative_adoptions_withdrawn_by_human_id",
                schema: "islamu_event",
                table: "moderation_case_narrative_adoptions",
                column: "withdrawn_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_narrative_policy_sources_govern_1abf8bf5783c",
                schema: "islamu_event",
                table: "moderation_case_narrative_policy_sources",
                column: "governance_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_narrative_policy_sources_host_b_193859e53e8c",
                schema: "islamu_event",
                table: "moderation_case_narrative_policy_sources",
                columns: new[] { "host_binding_id", "tenant_id", "target_kind", "purpose", "writer_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_narrative_policy_sources_privac_236af18b4970",
                schema: "islamu_event",
                table: "moderation_case_narrative_policy_sources",
                column: "privacy_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_narrative_policy_sources_privac_5e36e3c845b7",
                schema: "islamu_event",
                table: "moderation_case_narrative_policy_sources",
                column: "privacy_owner_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_narrative_policy_sources_record_3ce6ca070c23",
                schema: "islamu_event",
                table: "moderation_case_narrative_policy_sources",
                column: "recorded_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_narrative_policy_sources_tenant_id",
                schema: "islamu_event",
                table: "moderation_case_narrative_policy_sources",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_narratives_adoption_id_host_bin_c45b6aa51f19",
                schema: "islamu_event",
                table: "moderation_case_narratives",
                columns: new[] { "adoption_id", "host_binding_id", "trust_domain_id", "tenant_id", "policy_source_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_narratives_case_id",
                schema: "islamu_event",
                table: "moderation_case_narratives",
                column: "case_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_narratives_qualification_epoch_id",
                schema: "islamu_event",
                table: "moderation_case_narratives",
                column: "qualification_epoch_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_narratives_receipt_id",
                schema: "islamu_event",
                table: "moderation_case_narratives",
                column: "receipt_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_narratives_reporter_user_id",
                schema: "islamu_event",
                table: "moderation_case_narratives",
                column: "reporter_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_narratives_source_id",
                schema: "islamu_event",
                table: "moderation_case_narratives",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_narratives_tenant_id_purge_at",
                schema: "islamu_event",
                table: "moderation_case_narratives",
                columns: new[] { "tenant_id", "purge_at" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_narratives_tenant_id_trust_doma_6978c01f40df",
                schema: "islamu_event",
                table: "moderation_case_narratives",
                columns: new[] { "tenant_id", "trust_domain_id", "case_id", "source_id", "material_fact_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_read_grant_revocation_receipts__d5b7b8817512",
                schema: "islamu_event",
                table: "moderation_case_read_grant_revocation_receipts",
                column: "administrator_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_read_grant_revocation_receipts_grant_id",
                schema: "islamu_event",
                table: "moderation_case_read_grant_revocation_receipts",
                column: "grant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_read_grant_revocation_receipts_tenant_id",
                schema: "islamu_event",
                table: "moderation_case_read_grant_revocation_receipts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_read_grants_case_id",
                schema: "islamu_event",
                table: "moderation_case_read_grants",
                column: "case_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_read_grants_source_id",
                schema: "islamu_event",
                table: "moderation_case_read_grants",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_read_grants_tenant_id_case_id_caller_user_id",
                schema: "islamu_event",
                table: "moderation_case_read_grants",
                columns: new[] { "tenant_id", "case_id", "caller_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_target_policies_tenant_id_trust_4b3a03173510",
                schema: "islamu_event",
                table: "moderation_case_target_policies",
                columns: new[] { "tenant_id", "trust_domain_id", "target_kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_target_receipts_consumed_case_id",
                schema: "islamu_event",
                table: "moderation_case_target_receipts",
                column: "consumed_case_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_target_receipts_host_binding_id",
                schema: "islamu_event",
                table: "moderation_case_target_receipts",
                column: "host_binding_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_target_receipts_qualification_epoch_id",
                schema: "islamu_event",
                table: "moderation_case_target_receipts",
                column: "qualification_epoch_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_target_receipts_tenant_id_calle_f7bd5f5b4681",
                schema: "islamu_event",
                table: "moderation_case_target_receipts",
                columns: new[] { "tenant_id", "caller_user_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_target_receipts_token_digest",
                schema: "islamu_event",
                table: "moderation_case_target_receipts",
                column: "token_digest",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_target_sources_event_id",
                schema: "islamu_event",
                table: "moderation_case_target_sources",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_target_sources_tenant_id_trust__1daa445d50ae",
                schema: "islamu_event",
                table: "moderation_case_target_sources",
                columns: new[] { "tenant_id", "trust_domain_id", "kind", "native_kind", "native_target_id", "native_target_version", "native_facts_digest", "occurrence_digest", "material_digest" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_child_age_policy_adoptions_adopted_by_human_id",
                schema: "islamu_event",
                table: "moderation_child_age_policy_adoptions",
                column: "adopted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_child_age_policy_adoptions_source_id",
                schema: "islamu_event",
                table: "moderation_child_age_policy_adoptions",
                column: "source_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_child_age_policy_adoptions_withdrawn_by_human_id",
                schema: "islamu_event",
                table: "moderation_child_age_policy_adoptions",
                column: "withdrawn_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_child_age_policy_sources_collection__a09cc860e8e3",
                schema: "islamu_event",
                table: "moderation_child_age_policy_sources",
                column: "collection_owner_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_child_age_policy_sources_collection__a8df7e96dd01",
                schema: "islamu_event",
                table: "moderation_child_age_policy_sources",
                column: "collection_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_child_age_policy_sources_definition_id",
                schema: "islamu_event",
                table: "moderation_child_age_policy_sources",
                column: "definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_child_age_policy_sources_governance__41179f7f0e66",
                schema: "islamu_event",
                table: "moderation_child_age_policy_sources",
                column: "governance_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_child_age_policy_sources_host_bindin_e4235c3c5f4d",
                schema: "islamu_event",
                table: "moderation_child_age_policy_sources",
                columns: new[] { "host_binding_id", "tenant_id", "publication_id", "profile_id", "mode_code", "writer_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_child_age_policy_sources_recorded_by_human_id",
                schema: "islamu_event",
                table: "moderation_child_age_policy_sources",
                column: "recorded_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_child_age_policy_sources_reviewer_co_e095df2f8d8c",
                schema: "islamu_event",
                table: "moderation_child_age_policy_sources",
                column: "reviewer_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_child_age_policy_sources_reviewer_human_id",
                schema: "islamu_event",
                table: "moderation_child_age_policy_sources",
                column: "reviewer_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_control_selections_au_67aecd1bc07b",
                schema: "islamu_event",
                table: "moderation_classification_control_selections",
                column: "authored_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_control_selections_generation_id",
                schema: "islamu_event",
                table: "moderation_classification_control_selections",
                column: "generation_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_control_selections_ho_654fabb7b05e",
                schema: "islamu_event",
                table: "moderation_classification_control_selections",
                columns: new[] { "host_binding_id", "trust_domain_id", "tenant_id", "event_id", "review_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_control_selections_publication_id",
                schema: "islamu_event",
                table: "moderation_classification_control_selections",
                column: "publication_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_control_selections_review_id",
                schema: "islamu_event",
                table: "moderation_classification_control_selections",
                column: "review_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_control_selections_source_id",
                schema: "islamu_event",
                table: "moderation_classification_control_selections",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_control_selections_tenant_id",
                schema: "islamu_event",
                table: "moderation_classification_control_selections",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_event_target_adoption_05ac0024c742",
                schema: "islamu_event",
                table: "moderation_classification_event_target_adoptions",
                column: "host_binding_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_event_target_adoption_2e72480222a6",
                schema: "islamu_event",
                table: "moderation_classification_event_target_adoptions",
                column: "revoked_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_event_target_adoption_b4a7d32ce969",
                schema: "islamu_event",
                table: "moderation_classification_event_target_adoptions",
                column: "generation_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_event_target_adoption_b6e3d363b645",
                schema: "islamu_event",
                table: "moderation_classification_event_target_adoptions",
                columns: new[] { "selection_id", "binding_ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_event_target_adoption_f27c5920bc91",
                schema: "islamu_event",
                table: "moderation_classification_event_target_adoptions",
                column: "adopted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_event_target_adoptions_tenant_id",
                schema: "islamu_event",
                table: "moderation_classification_event_target_adoptions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_material_categories_e_cf40ed6d5fa1",
                schema: "islamu_event",
                table: "moderation_classification_material_categories",
                columns: new[] { "entry_id", "version_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_material_categories_version_id",
                schema: "islamu_event",
                table: "moderation_classification_material_categories",
                column: "version_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_material_components_s_5959c2647708",
                schema: "islamu_event",
                table: "moderation_classification_material_components",
                columns: new[] { "source_id", "kind", "native_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_material_entries_source_id_ordinal",
                schema: "islamu_event",
                table: "moderation_classification_material_entries",
                columns: new[] { "source_id", "ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_material_reviews_source_id",
                schema: "islamu_event",
                table: "moderation_classification_material_reviews",
                column: "source_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_material_sources_even_c541088af442",
                schema: "islamu_event",
                table: "moderation_classification_material_sources",
                column: "event_generation_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_material_sources_host_97b16bfd4469",
                schema: "islamu_event",
                table: "moderation_classification_material_sources",
                columns: new[] { "host_binding_id", "tenant_id", "event_id", "writer_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_material_sources_taxo_6b6f7f3b909e",
                schema: "islamu_event",
                table: "moderation_classification_material_sources",
                column: "taxonomy_adoption_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_material_sources_taxo_ffd89e89ea56",
                schema: "islamu_event",
                table: "moderation_classification_material_sources",
                column: "taxonomy_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_taxonomy_adoptions_ad_9fe791952cc0",
                schema: "islamu_event",
                table: "moderation_classification_taxonomy_adoptions",
                column: "adopted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_taxonomy_adoptions_source_id",
                schema: "islamu_event",
                table: "moderation_classification_taxonomy_adoptions",
                column: "source_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_taxonomy_adoptions_wi_1a777391657c",
                schema: "islamu_event",
                table: "moderation_classification_taxonomy_adoptions",
                column: "withdrawn_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_taxonomy_categories_s_a6764f347f22",
                schema: "islamu_event",
                table: "moderation_classification_taxonomy_categories",
                columns: new[] { "source_id", "category_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_taxonomy_categories_s_be85db2cdffd",
                schema: "islamu_event",
                table: "moderation_classification_taxonomy_categories",
                columns: new[] { "source_id", "ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_taxonomy_categories_source_id_code",
                schema: "islamu_event",
                table: "moderation_classification_taxonomy_categories",
                columns: new[] { "source_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_taxonomy_sources_chil_7270b77cd96b",
                schema: "islamu_event",
                table: "moderation_classification_taxonomy_sources",
                column: "child_age_adoption_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_taxonomy_sources_chil_dde383b5b96a",
                schema: "islamu_event",
                table: "moderation_classification_taxonomy_sources",
                column: "child_age_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_taxonomy_sources_cons_18d08d0db82f",
                schema: "islamu_event",
                table: "moderation_classification_taxonomy_sources",
                column: "constitution_adoption_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_taxonomy_sources_cons_8d69dc0a5c4e",
                schema: "islamu_event",
                table: "moderation_classification_taxonomy_sources",
                column: "constitution_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_taxonomy_sources_definition_id",
                schema: "islamu_event",
                table: "moderation_classification_taxonomy_sources",
                column: "definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_taxonomy_sources_host_d2904f47fc84",
                schema: "islamu_event",
                table: "moderation_classification_taxonomy_sources",
                columns: new[] { "host_binding_id", "tenant_id", "taxonomy_id", "writer_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_taxonomy_sources_host_dc952b329206",
                schema: "islamu_event",
                table: "moderation_classification_taxonomy_sources",
                columns: new[] { "host_binding_id", "tenant_id", "taxonomy_id", "taxonomy_revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_taxonomy_sources_owne_a9ff5c9b9660",
                schema: "islamu_event",
                table: "moderation_classification_taxonomy_sources",
                column: "owner_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_taxonomy_sources_owner_human_id",
                schema: "islamu_event",
                table: "moderation_classification_taxonomy_sources",
                column: "owner_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_taxonomy_sources_revi_f10132dfdd40",
                schema: "islamu_event",
                table: "moderation_classification_taxonomy_sources",
                column: "reviewer_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_taxonomy_sources_reviewer_human_id",
                schema: "islamu_event",
                table: "moderation_classification_taxonomy_sources",
                column: "reviewer_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_constitution_adoptions_adopted_by_human_id",
                schema: "islamu_event",
                table: "moderation_constitution_adoptions",
                column: "adopted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_constitution_adoptions_source_id",
                schema: "islamu_event",
                table: "moderation_constitution_adoptions",
                column: "source_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_constitution_adoptions_withdrawn_by_human_id",
                schema: "islamu_event",
                table: "moderation_constitution_adoptions",
                column: "withdrawn_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_constitution_availability_host_bindi_115c45f2e099",
                schema: "islamu_event",
                table: "moderation_constitution_availability",
                columns: new[] { "host_binding_id", "tenant_id", "reviewer_human_id", "writer_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_constitution_availability_reviewer_human_id",
                schema: "islamu_event",
                table: "moderation_constitution_availability",
                column: "reviewer_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_constitution_availability_withdrawn_by_human_id",
                schema: "islamu_event",
                table: "moderation_constitution_availability",
                column: "withdrawn_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_constitution_reviewers_availability_id",
                schema: "islamu_event",
                table: "moderation_constitution_reviewers",
                column: "availability_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_constitution_reviewers_controller_human_id",
                schema: "islamu_event",
                table: "moderation_constitution_reviewers",
                column: "controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_constitution_reviewers_reviewer_human_id",
                schema: "islamu_event",
                table: "moderation_constitution_reviewers",
                column: "reviewer_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_constitution_reviewers_source_id_availability_id",
                schema: "islamu_event",
                table: "moderation_constitution_reviewers",
                columns: new[] { "source_id", "availability_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_constitution_reviewers_source_id_con_748f1624e92f",
                schema: "islamu_event",
                table: "moderation_constitution_reviewers",
                columns: new[] { "source_id", "controller_human_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_constitution_reviewers_source_id_rev_31db36e90af2",
                schema: "islamu_event",
                table: "moderation_constitution_reviewers",
                columns: new[] { "source_id", "reviewer_human_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_constitution_sources_coordinator_human_id",
                schema: "islamu_event",
                table: "moderation_constitution_sources",
                column: "coordinator_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_constitution_sources_definition_id",
                schema: "islamu_event",
                table: "moderation_constitution_sources",
                column: "definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_constitution_sources_fallback_human_id",
                schema: "islamu_event",
                table: "moderation_constitution_sources",
                column: "fallback_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_constitution_sources_host_binding_id_d876d245f969",
                schema: "islamu_event",
                table: "moderation_constitution_sources",
                columns: new[] { "host_binding_id", "tenant_id", "publication_id", "profile_id", "mode_code", "writer_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_constitution_sources_original_effect_id",
                schema: "islamu_event",
                table: "moderation_constitution_sources",
                column: "original_effect_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_constitution_sources_recorded_by_human_id",
                schema: "islamu_event",
                table: "moderation_constitution_sources",
                column: "recorded_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_consumer_installations_host_binding__ffe63e7204b3",
                schema: "islamu_event",
                table: "moderation_consumer_installations",
                columns: new[] { "host_binding_id", "tenant_id", "consumer_id", "mode_code", "writer_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_consumer_installations_installed_by_human_id",
                schema: "islamu_event",
                table: "moderation_consumer_installations",
                column: "installed_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_consumer_installations_tenant_id",
                schema: "islamu_event",
                table: "moderation_consumer_installations",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_consumer_installations_withdrawn_by_human_id",
                schema: "islamu_event",
                table: "moderation_consumer_installations",
                column: "withdrawn_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_consumer_measurement_challenges_definition_id",
                schema: "islamu_event",
                table: "moderation_consumer_measurement_challenges",
                column: "definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_consumer_measurement_challenges_host_binding_id",
                schema: "islamu_event",
                table: "moderation_consumer_measurement_challenges",
                column: "host_binding_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_consumer_measurement_challenges_installation_id",
                schema: "islamu_event",
                table: "moderation_consumer_measurement_challenges",
                column: "installation_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_consumer_measurement_challenges_nonce",
                schema: "islamu_event",
                table: "moderation_consumer_measurement_challenges",
                column: "nonce",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_consumer_measurement_challenges_orig_255b4d4779f2",
                schema: "islamu_event",
                table: "moderation_consumer_measurement_challenges",
                column: "original_effect_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_consumer_measurement_challenges_prep_9015c58d960c",
                schema: "islamu_event",
                table: "moderation_consumer_measurement_challenges",
                column: "prepared_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_consumer_measurement_challenges_tenant_id",
                schema: "islamu_event",
                table: "moderation_consumer_measurement_challenges",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_consumer_measurement_receipts_accept_12bdbb8d71fd",
                schema: "islamu_event",
                table: "moderation_consumer_measurement_receipts",
                column: "accepted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_consumer_measurement_receipts_challenge_id",
                schema: "islamu_event",
                table: "moderation_consumer_measurement_receipts",
                column: "challenge_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_consumer_measurement_receipts_host_b_a43ef27236e7",
                schema: "islamu_event",
                table: "moderation_consumer_measurement_receipts",
                columns: new[] { "host_binding_id", "tenant_id", "run_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_consumer_measurement_receipts_host_b_a6a4e94b180b",
                schema: "islamu_event",
                table: "moderation_consumer_measurement_receipts",
                columns: new[] { "host_binding_id", "tenant_id", "challenge_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_consumer_measurement_receipts_issuer_06dba0753fd0",
                schema: "islamu_event",
                table: "moderation_consumer_measurement_receipts",
                column: "issuer_enrollment_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_consumer_measurement_receipts_tenant_id",
                schema: "islamu_event",
                table: "moderation_consumer_measurement_receipts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_consumer_measurement_receipts_token_digest",
                schema: "islamu_event",
                table: "moderation_consumer_measurement_receipts",
                column: "token_digest");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_content_responses_case_id",
                schema: "islamu_event",
                table: "moderation_content_responses",
                column: "case_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_content_responses_effect_id",
                schema: "islamu_event",
                table: "moderation_content_responses",
                column: "effect_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_content_responses_notice_id",
                schema: "islamu_event",
                table: "moderation_content_responses",
                column: "notice_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_content_responses_representative_human_id",
                schema: "islamu_event",
                table: "moderation_content_responses",
                column: "representative_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_content_responses_tenant_id_effect_id",
                schema: "islamu_event",
                table: "moderation_content_responses",
                columns: new[] { "tenant_id", "effect_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_content_responses_tenant_id_represen_4f15b028c8a6",
                schema: "islamu_event",
                table: "moderation_content_responses",
                columns: new[] { "tenant_id", "representative_human_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_contribution_selections_authored_by_human_id",
                schema: "islamu_event",
                table: "moderation_contribution_selections",
                column: "authored_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_contribution_selections_case_id",
                schema: "islamu_event",
                table: "moderation_contribution_selections",
                column: "case_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_contribution_selections_decision_id",
                schema: "islamu_event",
                table: "moderation_contribution_selections",
                column: "decision_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_contribution_selections_host_binding_1d08c5d03ace",
                schema: "islamu_event",
                table: "moderation_contribution_selections",
                columns: new[] { "host_binding_id", "trust_domain_id", "tenant_id", "effect_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_contribution_selections_publication_id",
                schema: "islamu_event",
                table: "moderation_contribution_selections",
                column: "publication_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_contribution_selections_tenant_id",
                schema: "islamu_event",
                table: "moderation_contribution_selections",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_deployment_adoptions_adopted_by_human_id",
                schema: "islamu_event",
                table: "moderation_deployment_adoptions",
                column: "adopted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_deployment_adoptions_source_id",
                schema: "islamu_event",
                table: "moderation_deployment_adoptions",
                column: "source_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_deployment_adoptions_withdrawn_by_human_id",
                schema: "islamu_event",
                table: "moderation_deployment_adoptions",
                column: "withdrawn_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_deployment_consumers_installation_id",
                schema: "islamu_event",
                table: "moderation_deployment_consumers",
                column: "installation_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_deployment_consumers_source_id_bindi_0565351e6b3d",
                schema: "islamu_event",
                table: "moderation_deployment_consumers",
                columns: new[] { "source_id", "binding_digest", "consumer_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_deployment_sources_definition_id",
                schema: "islamu_event",
                table: "moderation_deployment_sources",
                column: "definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_deployment_sources_host_binding_id_t_7ea51afbd580",
                schema: "islamu_event",
                table: "moderation_deployment_sources",
                columns: new[] { "host_binding_id", "tenant_id", "publication_id", "profile_id", "mode_code", "writer_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_deployment_sources_recorded_by_human_id",
                schema: "islamu_event",
                table: "moderation_deployment_sources",
                column: "recorded_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_event_generations_host_binding_id_te_44fb1b573d0b",
                schema: "islamu_event",
                table: "moderation_event_generations",
                columns: new[] { "host_binding_id", "tenant_id", "event_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_event_generations_observed_by_human_id",
                schema: "islamu_event",
                table: "moderation_event_generations",
                column: "observed_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_event_generations_tenant_id",
                schema: "islamu_event",
                table: "moderation_event_generations",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_event_rights_adoptions_adopted_by_human_id",
                schema: "islamu_event",
                table: "moderation_event_rights_adoptions",
                column: "adopted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_event_rights_adoptions_source_id",
                schema: "islamu_event",
                table: "moderation_event_rights_adoptions",
                column: "source_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_event_rights_adoptions_withdrawn_by_human_id",
                schema: "islamu_event",
                table: "moderation_event_rights_adoptions",
                column: "withdrawn_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_event_rights_sources_continuity_mapp_1d13664b4e3b",
                schema: "islamu_event",
                table: "moderation_event_rights_sources",
                column: "continuity_mapping_owner_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_event_rights_sources_continuity_mapp_e2b0cdd63f1b",
                schema: "islamu_event",
                table: "moderation_event_rights_sources",
                column: "continuity_mapping_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_event_rights_sources_definition_id",
                schema: "islamu_event",
                table: "moderation_event_rights_sources",
                column: "definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_event_rights_sources_governance_cont_186fa67c6a61",
                schema: "islamu_event",
                table: "moderation_event_rights_sources",
                column: "governance_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_event_rights_sources_host_binding_id_0f5a636abd37",
                schema: "islamu_event",
                table: "moderation_event_rights_sources",
                columns: new[] { "host_binding_id", "tenant_id", "publication_id", "profile_id", "mode_code", "writer_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_event_rights_sources_recorded_by_human_id",
                schema: "islamu_event",
                table: "moderation_event_rights_sources",
                column: "recorded_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_event_rights_sources_reviewer_controller_human_id",
                schema: "islamu_event",
                table: "moderation_event_rights_sources",
                column: "reviewer_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_event_rights_sources_reviewer_human_id",
                schema: "islamu_event",
                table: "moderation_event_rights_sources",
                column: "reviewer_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_event_rights_sources_settlement_mapp_3c800422fcbd",
                schema: "islamu_event",
                table: "moderation_event_rights_sources",
                column: "settlement_mapping_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_event_rights_sources_settlement_mapp_47acba417996",
                schema: "islamu_event",
                table: "moderation_event_rights_sources",
                column: "settlement_mapping_owner_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_event_target_adoptions_adopted_by_human_id",
                schema: "islamu_event",
                table: "moderation_event_target_adoptions",
                column: "adopted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_event_target_adoptions_generation_id",
                schema: "islamu_event",
                table: "moderation_event_target_adoptions",
                column: "generation_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_event_target_adoptions_revoked_by_human_id",
                schema: "islamu_event",
                table: "moderation_event_target_adoptions",
                column: "revoked_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_event_target_adoptions_selection_id__489b2d436217",
                schema: "islamu_event",
                table: "moderation_event_target_adoptions",
                columns: new[] { "selection_id", "binding_ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_measured_operations_acceptances_acce_481c38564a28",
                schema: "islamu_event",
                table: "moderation_measured_operations_acceptances",
                column: "accepted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_measured_operations_acceptances_source_id",
                schema: "islamu_event",
                table: "moderation_measured_operations_acceptances",
                column: "source_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_measured_operations_acceptances_with_8db597c8af79",
                schema: "islamu_event",
                table: "moderation_measured_operations_acceptances",
                column: "withdrawn_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_measured_operations_sources_definition_id",
                schema: "islamu_event",
                table: "moderation_measured_operations_sources",
                column: "definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_measured_operations_sources_host_bin_f404833dd002",
                schema: "islamu_event",
                table: "moderation_measured_operations_sources",
                columns: new[] { "host_binding_id", "tenant_id", "binding_digest", "writer_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_measured_operations_sources_original_effect_id",
                schema: "islamu_event",
                table: "moderation_measured_operations_sources",
                column: "original_effect_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_measured_operations_sources_recorded_by_human_id",
                schema: "islamu_event",
                table: "moderation_measured_operations_sources",
                column: "recorded_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_measured_operations_sources_recorder_a22adea15fa5",
                schema: "islamu_event",
                table: "moderation_measured_operations_sources",
                column: "recorder_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_measured_operations_sources_reviewer_58d55278f851",
                schema: "islamu_event",
                table: "moderation_measured_operations_sources",
                column: "reviewer_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_measured_operations_sources_reviewer_human_id",
                schema: "islamu_event",
                table: "moderation_measured_operations_sources",
                column: "reviewer_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_measurement_issuer_enrollments_appro_90eb46cbd78e",
                schema: "islamu_event",
                table: "moderation_measurement_issuer_enrollments",
                column: "approved_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_measurement_issuer_enrollments_host__bc3dc01e6240",
                schema: "islamu_event",
                table: "moderation_measurement_issuer_enrollments",
                columns: new[] { "host_binding_id", "tenant_id", "key_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_measurement_issuer_enrollments_revok_0056b5f78bfd",
                schema: "islamu_event",
                table: "moderation_measurement_issuer_enrollments",
                column: "revoked_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_measurement_issuer_enrollments_tenant_id",
                schema: "islamu_event",
                table: "moderation_measurement_issuer_enrollments",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_notice_disclosure_approvals_approved_by_human_id",
                schema: "islamu_event",
                table: "moderation_notice_disclosure_approvals",
                column: "approved_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_notice_disclosure_approvals_case_id",
                schema: "islamu_event",
                table: "moderation_notice_disclosure_approvals",
                column: "case_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_notice_disclosure_approvals_decision_id",
                schema: "islamu_event",
                table: "moderation_notice_disclosure_approvals",
                column: "decision_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_notice_disclosure_approvals_host_bin_87a2a24806ab",
                schema: "islamu_event",
                table: "moderation_notice_disclosure_approvals",
                columns: new[] { "host_binding_id", "trust_domain_id", "tenant_id", "effect_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_notice_disclosure_approvals_publication_id",
                schema: "islamu_event",
                table: "moderation_notice_disclosure_approvals",
                column: "publication_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_notice_disclosure_approvals_revoked_by_human_id",
                schema: "islamu_event",
                table: "moderation_notice_disclosure_approvals",
                column: "revoked_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_notice_disclosure_approvals_tenant_id",
                schema: "islamu_event",
                table: "moderation_notice_disclosure_approvals",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_notice_notification_receipts_effect_id",
                schema: "islamu_event",
                table: "moderation_notice_notification_receipts",
                column: "effect_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_notice_notification_receipts_notice_id",
                schema: "islamu_event",
                table: "moderation_notice_notification_receipts",
                column: "notice_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_notice_notification_receipts_tenant_id",
                schema: "islamu_event",
                table: "moderation_notice_notification_receipts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_notice_sources_trust_domain_id_retai_10207bdfc4c8",
                schema: "islamu_event",
                table: "moderation_notice_sources",
                columns: new[] { "trust_domain_id", "retain_until", "effect_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_notice_sources_trust_domain_id_subje_55e4a5568bed",
                schema: "islamu_event",
                table: "moderation_notice_sources",
                columns: new[] { "trust_domain_id", "subject_kind", "subject_id", "captured_at" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_notices_effect_id_audience_recipient_b1e4678be011",
                schema: "islamu_event",
                table: "moderation_notices",
                columns: new[] { "effect_id", "audience", "recipient_subject_kind", "recipient_subject_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_notices_native_intent_id",
                schema: "islamu_event",
                table: "moderation_notices",
                column: "native_intent_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_notices_trust_domain_id_recipient_su_857d7f3ab563",
                schema: "islamu_event",
                table: "moderation_notices",
                columns: new[] { "trust_domain_id", "recipient_subject_kind", "recipient_subject_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_operational_budget_adoptions_adopted_by_human_id",
                schema: "islamu_event",
                table: "moderation_operational_budget_adoptions",
                column: "adopted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_operational_budget_adoptions_source__dfcfad624b0e",
                schema: "islamu_event",
                table: "moderation_operational_budget_adoptions",
                columns: new[] { "source_id", "host_binding_id", "trust_domain_id", "tenant_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_operational_budget_adoptions_source_id",
                schema: "islamu_event",
                table: "moderation_operational_budget_adoptions",
                column: "source_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_operational_budget_adoptions_withdra_e925840e6f6c",
                schema: "islamu_event",
                table: "moderation_operational_budget_adoptions",
                column: "withdrawn_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_operational_budget_sources_host_bind_4a7fe61efa98",
                schema: "islamu_event",
                table: "moderation_operational_budget_sources",
                columns: new[] { "host_binding_id", "tenant_id", "subject_kind", "action", "writer_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_operational_budget_sources_operator_human_id",
                schema: "islamu_event",
                table: "moderation_operational_budget_sources",
                column: "operator_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_operational_budget_sources_recorded_by_human_id",
                schema: "islamu_event",
                table: "moderation_operational_budget_sources",
                column: "recorded_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_operational_budget_sources_tenant_id",
                schema: "islamu_event",
                table: "moderation_operational_budget_sources",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_original_effect_completion_receipts_effect_id",
                schema: "islamu_event",
                table: "moderation_original_effect_completion_receipts",
                column: "effect_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_participant_incident_responsibility__0308fe96dd15",
                schema: "islamu_event",
                table: "moderation_participant_incident_responsibility_sources",
                column: "assessor_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_participant_incident_responsibility__10036188741d",
                schema: "islamu_event",
                table: "moderation_participant_incident_responsibility_sources",
                columns: new[] { "custody_adoption_id", "host_binding_id", "trust_domain_id", "tenant_id", "custody_source_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_participant_incident_responsibility__1112d46a4ea2",
                schema: "islamu_event",
                table: "moderation_participant_incident_responsibility_sources",
                column: "qualification_policy_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_participant_incident_responsibility__1aef2bf00b27",
                schema: "islamu_event",
                table: "moderation_participant_incident_responsibility_sources",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_participant_incident_responsibility__25876857dc56",
                schema: "islamu_event",
                table: "moderation_participant_incident_responsibility_sources",
                column: "evidence_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_participant_incident_responsibility__286e2d63cfb9",
                schema: "islamu_event",
                table: "moderation_participant_incident_responsibility_sources",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_participant_incident_responsibility__6f703c7fd46d",
                schema: "islamu_event",
                table: "moderation_participant_incident_responsibility_sources",
                column: "subject_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_participant_incident_responsibility__9628d806a6da",
                schema: "islamu_event",
                table: "moderation_participant_incident_responsibility_sources",
                column: "original_narrative_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_participant_incident_responsibility__9f7d48843b27",
                schema: "islamu_event",
                table: "moderation_participant_incident_responsibility_sources",
                column: "read_issuer_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_participant_incident_responsibility__a222ec764349",
                schema: "islamu_event",
                table: "moderation_participant_incident_responsibility_sources",
                columns: new[] { "tenant_id", "trust_domain_id", "occurrence_case_id", "subject_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_participant_incident_responsibility__b8e06aefdd3e",
                schema: "islamu_event",
                table: "moderation_participant_incident_responsibility_sources",
                columns: new[] { "custody_source_id", "host_binding_id", "trust_domain_id", "tenant_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_participant_incident_responsibility__bcc6822e390f",
                schema: "islamu_event",
                table: "moderation_participant_incident_responsibility_sources",
                column: "occurrence_case_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_participant_incident_responsibility__cf4b6c68b35a",
                schema: "islamu_event",
                table: "moderation_participant_incident_responsibility_sources",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_participant_incident_responsibility__d17e3531e066",
                schema: "islamu_event",
                table: "moderation_participant_incident_responsibility_sources",
                column: "withdrawn_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_participant_incident_responsibility__e00f4902366c",
                schema: "islamu_event",
                table: "moderation_participant_incident_responsibility_sources",
                columns: new[] { "tenant_id", "trust_domain_id", "purge_at" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_participant_incident_responsibility__e73cb57a4be7",
                schema: "islamu_event",
                table: "moderation_participant_incident_responsibility_sources",
                column: "host_binding_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_participant_incident_responsibility__f516112f62b3",
                schema: "islamu_event",
                table: "moderation_participant_incident_responsibility_sources",
                column: "participant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_participant_incident_responsibility__f77b900afaea",
                schema: "islamu_event",
                table: "moderation_participant_incident_responsibility_sources",
                column: "read_grant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_policy_publication_candidates_authority_source_id",
                schema: "islamu_event",
                table: "moderation_policy_publication_candidates",
                column: "authority_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_policy_publication_candidates_definition_id",
                schema: "islamu_event",
                table: "moderation_policy_publication_candidates",
                column: "definition_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_policy_publication_candidates_host_binding_id",
                schema: "islamu_event",
                table: "moderation_policy_publication_candidates",
                column: "host_binding_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_policy_publication_candidates_operation_id",
                schema: "islamu_event",
                table: "moderation_policy_publication_candidates",
                column: "operation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_policy_publication_candidates_policy_28f910061b56",
                schema: "islamu_event",
                table: "moderation_policy_publication_candidates",
                columns: new[] { "policy_id", "definition_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_policy_publication_candidates_policy_b4b0fa66dcc1",
                schema: "islamu_event",
                table: "moderation_policy_publication_candidates",
                columns: new[] { "policy_id", "previous_publication_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_policy_publication_candidates_prepar_70d941a540a8",
                schema: "islamu_event",
                table: "moderation_policy_publication_candidates",
                column: "prepared_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_policy_publication_candidates_tenant_id",
                schema: "islamu_event",
                table: "moderation_policy_publication_candidates",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_policy_publications_authority_source_id",
                schema: "islamu_event",
                table: "moderation_policy_publications",
                column: "authority_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_policy_publications_host_binding_id",
                schema: "islamu_event",
                table: "moderation_policy_publications",
                column: "host_binding_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_policy_publications_operation_id",
                schema: "islamu_event",
                table: "moderation_policy_publications",
                column: "operation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_policy_publications_policy_id_adopted_revision",
                schema: "islamu_event",
                table: "moderation_policy_publications",
                columns: new[] { "policy_id", "adopted_revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_policy_publications_policy_id_previo_fe5f94c2d141",
                schema: "islamu_event",
                table: "moderation_policy_publications",
                columns: new[] { "policy_id", "previous_publication_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_policy_publications_tenant_id",
                schema: "islamu_event",
                table: "moderation_policy_publications",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_policy_selections_host_binding_id_jurisdiction_id",
                schema: "islamu_event",
                table: "moderation_policy_selections",
                columns: new[] { "host_binding_id", "jurisdiction_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_policy_selections_id_active_publication_id",
                schema: "islamu_event",
                table: "moderation_policy_selections",
                columns: new[] { "id", "active_publication_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_policy_selections_id_latest_publication_id",
                schema: "islamu_event",
                table: "moderation_policy_selections",
                columns: new[] { "id", "latest_publication_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_pre_finding_custody_attribution_source_id",
                schema: "islamu_event",
                table: "moderation_pre_finding_custody",
                column: "attribution_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_pre_finding_custody_case_id",
                schema: "islamu_event",
                table: "moderation_pre_finding_custody",
                column: "case_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_pre_finding_custody_privacy_owner_human_id",
                schema: "islamu_event",
                table: "moderation_pre_finding_custody",
                column: "privacy_owner_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_pre_finding_custody_proposed_by_human_id",
                schema: "islamu_event",
                table: "moderation_pre_finding_custody",
                column: "proposed_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_pre_finding_notices_case_id",
                schema: "islamu_event",
                table: "moderation_pre_finding_notices",
                column: "case_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_pre_finding_notices_custody_id_host__16a82c8f1b14",
                schema: "islamu_event",
                table: "moderation_pre_finding_notices",
                columns: new[] { "custody_id", "host_binding_id", "trust_domain_id", "tenant_id", "case_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_pre_finding_notices_occurrence_case_id",
                schema: "islamu_event",
                table: "moderation_pre_finding_notices",
                column: "occurrence_case_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_pre_finding_notices_recipient_human_id",
                schema: "islamu_event",
                table: "moderation_pre_finding_notices",
                column: "recipient_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_pre_finding_notices_reviewer_assignment_id",
                schema: "islamu_event",
                table: "moderation_pre_finding_notices",
                column: "reviewer_assignment_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_pre_finding_notices_tenant_id_retain_until",
                schema: "islamu_event",
                table: "moderation_pre_finding_notices",
                columns: new[] { "tenant_id", "retain_until" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_pre_finding_receipts_actor_human_id",
                schema: "islamu_event",
                table: "moderation_pre_finding_receipts",
                column: "actor_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_pre_finding_receipts_case_id",
                schema: "islamu_event",
                table: "moderation_pre_finding_receipts",
                column: "case_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_pre_finding_receipts_result_id_result_revision",
                schema: "islamu_event",
                table: "moderation_pre_finding_receipts",
                columns: new[] { "result_id", "result_revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_adoptions_adopted_by_human_id",
                schema: "islamu_event",
                table: "moderation_profile_adoptions",
                column: "adopted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_adoptions_publication_id",
                schema: "islamu_event",
                table: "moderation_profile_adoptions",
                column: "publication_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_adoptions_revoked_by_human_id",
                schema: "islamu_event",
                table: "moderation_profile_adoptions",
                column: "revoked_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_adoptions_selection_id",
                schema: "islamu_event",
                table: "moderation_profile_adoptions",
                column: "selection_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_assessment_adopti_abaabffdde70",
                schema: "islamu_event",
                table: "moderation_profile_collection_assessment_adoptions",
                column: "withdrawn_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_assessment_adopti_b2ce19bc4d8f",
                schema: "islamu_event",
                table: "moderation_profile_collection_assessment_adoptions",
                column: "adopted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_assessment_adoptions_source_id",
                schema: "islamu_event",
                table: "moderation_profile_collection_assessment_adoptions",
                column: "source_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_assessment_source_0ce1dadc7038",
                schema: "islamu_event",
                table: "moderation_profile_collection_assessment_sources",
                column: "recorded_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_assessment_source_2c36504a89b8",
                schema: "islamu_event",
                table: "moderation_profile_collection_assessment_sources",
                column: "procedure_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_assessment_source_613099f6e364",
                schema: "islamu_event",
                table: "moderation_profile_collection_assessment_sources",
                columns: new[] { "subject_human_id", "procedure_source_id", "generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_assessment_source_d14f9642604a",
                schema: "islamu_event",
                table: "moderation_profile_collection_assessment_sources",
                column: "reviewer_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_assessment_source_d314b81908a1",
                schema: "islamu_event",
                table: "moderation_profile_collection_assessment_sources",
                column: "owner_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_assessment_source_eb8d4b312436",
                schema: "islamu_event",
                table: "moderation_profile_collection_assessment_sources",
                column: "procedure_adopted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_assessment_source_fb9b2d8be14a",
                schema: "islamu_event",
                table: "moderation_profile_collection_assessment_sources",
                column: "procedure_adoption_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_procedure_adoptio_3b5310044da2",
                schema: "islamu_event",
                table: "moderation_profile_collection_procedure_adoptions",
                column: "adopted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_procedure_adoptio_fb0760a8f111",
                schema: "islamu_event",
                table: "moderation_profile_collection_procedure_adoptions",
                column: "withdrawn_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_procedure_adoptions_source_id",
                schema: "islamu_event",
                table: "moderation_profile_collection_procedure_adoptions",
                column: "source_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_procedure_sources_017738275028",
                schema: "islamu_event",
                table: "moderation_profile_collection_procedure_sources",
                column: "child_age_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_procedure_sources_145cca9d52a4",
                schema: "islamu_event",
                table: "moderation_profile_collection_procedure_sources",
                column: "recorded_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_procedure_sources_4e53d6921a1b",
                schema: "islamu_event",
                table: "moderation_profile_collection_procedure_sources",
                columns: new[] { "host_binding_id", "tenant_id", "publication_id", "profile_id", "mode_code", "writer_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_procedure_sources_793c83cf5703",
                schema: "islamu_event",
                table: "moderation_profile_collection_procedure_sources",
                column: "child_age_adoption_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_subject_observati_1729cda19c9e",
                schema: "islamu_event",
                table: "moderation_profile_collection_subject_observations",
                column: "observation_stamp",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_subject_observati_5a975c363f80",
                schema: "islamu_event",
                table: "moderation_profile_collection_subject_observations",
                column: "subject_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_subject_observati_b3871e81c0f7",
                schema: "islamu_event",
                table: "moderation_profile_collection_subject_observations",
                column: "procedure_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_subject_observati_c34f4d6458a8",
                schema: "islamu_event",
                table: "moderation_profile_collection_subject_observations",
                column: "procedure_adoption_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_subject_observati_f5c61ce2f37a",
                schema: "islamu_event",
                table: "moderation_profile_collection_subject_observations",
                columns: new[] { "host_binding_id", "tenant_id", "subject_human_id", "profile_id", "mode_code", "observation_sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_collection_subject_observations_epoch_id",
                schema: "islamu_event",
                table: "moderation_profile_collection_subject_observations",
                column: "epoch_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_subject_consents_source_id",
                schema: "islamu_event",
                table: "moderation_profile_subject_consents",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_subject_consents_subject_hum_60faf4de8309",
                schema: "islamu_event",
                table: "moderation_profile_subject_consents",
                columns: new[] { "subject_human_id", "source_id", "generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_publication_profiles_publication_id__5da4903ad853",
                schema: "islamu_event",
                table: "moderation_publication_profiles",
                columns: new[] { "publication_id", "reason_code", "reason_revision" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_publication_profiles_publication_id__c75fa7aed9d5",
                schema: "islamu_event",
                table: "moderation_publication_profiles",
                columns: new[] { "publication_id", "rule_code", "rule_revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_publication_reasons_publication_id_c_407610e5d9f9",
                schema: "islamu_event",
                table: "moderation_publication_reasons",
                columns: new[] { "publication_id", "class_code", "class_revision" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_qualification_policy_adoptions_adopt_218d7a47fdf4",
                schema: "islamu_event",
                table: "moderation_qualification_policy_adoptions",
                column: "adopted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_qualification_policy_adoptions_revok_f4d093607932",
                schema: "islamu_event",
                table: "moderation_qualification_policy_adoptions",
                column: "revoked_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_qualification_policy_adoptions_sourc_6941f528d7d0",
                schema: "islamu_event",
                table: "moderation_qualification_policy_adoptions",
                columns: new[] { "source_id", "host_binding_id", "trust_domain_id", "tenant_id", "case_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_qualification_policy_adoptions_sourc_a84826658a6f",
                schema: "islamu_event",
                table: "moderation_qualification_policy_adoptions",
                columns: new[] { "source_id", "source_revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_qualification_policy_sources_case_id",
                schema: "islamu_event",
                table: "moderation_qualification_policy_sources",
                column: "case_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_qualification_policy_sources_host_bi_303316e2a773",
                schema: "islamu_event",
                table: "moderation_qualification_policy_sources",
                columns: new[] { "host_binding_id", "trust_domain_id", "tenant_id", "case_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_qualification_policy_sources_publish_2a8032ad9ef2",
                schema: "islamu_event",
                table: "moderation_qualification_policy_sources",
                column: "published_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_qualification_policy_sources_tenant_id",
                schema: "islamu_event",
                table: "moderation_qualification_policy_sources",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_action_recovery_capture_item_id",
                schema: "islamu_event",
                table: "moderation_recognition_action_recovery",
                column: "capture_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_action_recovery_fresh_su_f583c15119fc",
                schema: "islamu_event",
                table: "moderation_recognition_action_recovery",
                columns: new[] { "fresh_subject_id", "verified_binding_id", "capture_item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_action_recovery_resolved_e5d468b7847a",
                schema: "islamu_event",
                table: "moderation_recognition_action_recovery",
                columns: new[] { "resolved_at", "disposition_by" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_capture_item_continuity_binding_id",
                schema: "islamu_event",
                table: "moderation_recognition_capture_item",
                column: "continuity_binding_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_capture_item_intent_id_p_1879ad431f52",
                schema: "islamu_event",
                table: "moderation_recognition_capture_item",
                columns: new[] { "intent_id", "provider_kind", "purpose", "digest", "sanction_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_capture_item_parent_capture_item_id",
                schema: "islamu_event",
                table: "moderation_recognition_capture_item",
                column: "parent_capture_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_capture_item_trust_domai_8bd2458164f9",
                schema: "islamu_event",
                table: "moderation_recognition_capture_item",
                columns: new[] { "trust_domain_id", "provider_kind", "purpose", "key_id", "digest" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_continuity_binding_capture_item_id",
                schema: "islamu_event",
                table: "moderation_recognition_continuity_binding",
                column: "capture_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_continuity_binding_fresh_59ef40ce8d93",
                schema: "islamu_event",
                table: "moderation_recognition_continuity_binding",
                columns: new[] { "fresh_subject_id", "sanction_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_continuity_binding_retain_until",
                schema: "islamu_event",
                table: "moderation_recognition_continuity_binding",
                column: "retain_until");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_disposition_intent_id_pe_efa509a5fe1b",
                schema: "islamu_event",
                table: "moderation_recognition_disposition",
                columns: new[] { "intent_id", "pending_revision", "source_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_disposition_retain_until",
                schema: "islamu_event",
                table: "moderation_recognition_disposition",
                column: "retain_until");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_disposition_remedy_receipt_hash",
                schema: "islamu_event",
                table: "moderation_recognition_disposition_remedy",
                column: "receipt_hash");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_disposition_remedy_retain_until",
                schema: "islamu_event",
                table: "moderation_recognition_disposition_remedy",
                column: "retain_until");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_escalation_recipient_hum_2a4638c73020",
                schema: "islamu_event",
                table: "moderation_recognition_escalation",
                columns: new[] { "recipient_human_id", "acknowledged_at" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_escalation_retain_until",
                schema: "islamu_event",
                table: "moderation_recognition_escalation",
                column: "retain_until");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_escalation_target_id_sou_610c24c4b772",
                schema: "islamu_event",
                table: "moderation_recognition_escalation",
                columns: new[] { "target_id", "source_id", "recipient_human_id", "stage" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_key_lifecycle_key_trust__7e61fd0dc619",
                schema: "islamu_event",
                table: "moderation_recognition_key_lifecycle_key",
                columns: new[] { "trust_domain_id", "key_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_key_lifecycle_purge_auth_15b3f77ff198",
                schema: "islamu_event",
                table: "moderation_recognition_key_lifecycle_purge",
                column: "authority_sequence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_pending_receipt_hash",
                schema: "islamu_event",
                table: "moderation_recognition_pending",
                column: "receipt_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_pending_state_escalate_at",
                schema: "islamu_event",
                table: "moderation_recognition_pending",
                columns: new[] { "state", "escalate_at" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_pending_subject_id",
                schema: "islamu_event",
                table: "moderation_recognition_pending",
                column: "subject_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_privacy_adoptions_adopted_by_human_id",
                schema: "islamu_event",
                table: "moderation_recognition_privacy_adoptions",
                column: "adopted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_privacy_adoptions_source_id",
                schema: "islamu_event",
                table: "moderation_recognition_privacy_adoptions",
                column: "source_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_privacy_adoptions_withdr_2543032b874a",
                schema: "islamu_event",
                table: "moderation_recognition_privacy_adoptions",
                column: "withdrawn_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_privacy_sources_definition_id",
                schema: "islamu_event",
                table: "moderation_recognition_privacy_sources",
                column: "definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_privacy_sources_governan_1b567110ac06",
                schema: "islamu_event",
                table: "moderation_recognition_privacy_sources",
                column: "governance_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_privacy_sources_host_bin_5d187f0026f7",
                schema: "islamu_event",
                table: "moderation_recognition_privacy_sources",
                columns: new[] { "host_binding_id", "tenant_id", "publication_id", "profile_id", "mode_code", "writer_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_privacy_sources_privacy__2853844e0ea5",
                schema: "islamu_event",
                table: "moderation_recognition_privacy_sources",
                column: "privacy_owner_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_privacy_sources_privacy__61dea675164a",
                schema: "islamu_event",
                table: "moderation_recognition_privacy_sources",
                column: "privacy_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_privacy_sources_recorded_by_human_id",
                schema: "islamu_event",
                table: "moderation_recognition_privacy_sources",
                column: "recorded_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_privacy_sources_recovery_24350a6105c6",
                schema: "islamu_event",
                table: "moderation_recognition_privacy_sources",
                column: "recovery_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_privacy_sources_recovery_d42a4a68523f",
                schema: "islamu_event",
                table: "moderation_recognition_privacy_sources",
                column: "recovery_owner_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recovery_store_states_installation_slot",
                schema: "islamu_event",
                table: "moderation_recovery_store_states",
                column: "installation_slot",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_resource_generations_host_binding_id_019ac585848d",
                schema: "islamu_event",
                table: "moderation_resource_generations",
                columns: new[] { "host_binding_id", "tenant_id", "resource_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_resource_generations_observed_by_human_id",
                schema: "islamu_event",
                table: "moderation_resource_generations",
                column: "observed_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_resource_generations_tenant_id",
                schema: "islamu_event",
                table: "moderation_resource_generations",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_resource_target_adoptions_adopted_by_human_id",
                schema: "islamu_event",
                table: "moderation_resource_target_adoptions",
                column: "adopted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_resource_target_adoptions_generation_id",
                schema: "islamu_event",
                table: "moderation_resource_target_adoptions",
                column: "generation_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_resource_target_adoptions_revoked_by_human_id",
                schema: "islamu_event",
                table: "moderation_resource_target_adoptions",
                column: "revoked_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_resource_target_adoptions_selection__09687ff2d2ed",
                schema: "islamu_event",
                table: "moderation_resource_target_adoptions",
                columns: new[] { "selection_id", "binding_ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_schedule_risk_reviews_accepted_finding_id",
                schema: "islamu_event",
                table: "moderation_schedule_risk_reviews",
                column: "accepted_finding_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_schedule_risk_reviews_accepted_revie_2eae63c8ae6f",
                schema: "islamu_event",
                table: "moderation_schedule_risk_reviews",
                column: "accepted_reviewer_assignment_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_schedule_risk_reviews_event_scope_tr_3c628f86c17e",
                schema: "islamu_event",
                table: "moderation_schedule_risk_reviews",
                columns: new[] { "event_scope_trust_domain_id", "event_scope_tenant_id", "state", "due_at" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_schedule_risk_reviews_source_sanctio_c5b059e54123",
                schema: "islamu_event",
                table: "moderation_schedule_risk_reviews",
                columns: new[] { "source_sanction_id", "event_scope_event_id", "session_id", "participant_id", "original_schedule_generation", "original_participant_generation", "original_publication_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_birth_date_collection_receipts_user_id",
                schema: "islamu_event",
                table: "user_birth_date_collection_receipts",
                column: "user_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_event_report_signals_event_report_cases_tenant__fd513cb2f7a8",
                schema: "islamu_event",
                table: "event_report_signals",
                columns: new[] { "tenant_id", "report_id", "case_id" },
                principalSchema: "islamu_event",
                principalTable: "event_report_cases",
                principalColumns: new[] { "tenant_id", "report_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_event_report_signals_event_report_external_link_c0cf91adb760",
                schema: "islamu_event",
                table: "event_report_signals",
                columns: new[] { "tenant_id", "report_id", "provider_link_id" },
                principalSchema: "islamu_event",
                principalTable: "event_report_external_links",
                principalColumns: new[] { "tenant_id", "report_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_event_report_signals_incoming_webhook_messages__6a26a7335cd6",
                schema: "islamu_event",
                table: "event_report_signals",
                columns: new[] { "tenant_id", "incoming_webhook_message_id" },
                principalSchema: "islamu_event",
                principalTable: "incoming_webhook_messages",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_moderation_cases_moderation_cases_occurrence_case_id",
                schema: "islamu_event",
                table: "moderation_cases",
                column: "occurrence_case_id",
                principalSchema: "islamu_event",
                principalTable: "moderation_cases",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_event_resource_moderation_quarantines_event_res_1a2cc455b743",
                schema: "islamu_event",
                table: "event_resource_moderation_quarantines",
                columns: new[] { "tenant_id", "release_id" },
                principalSchema: "islamu_event",
                principalTable: "event_resource_quarantine_releases",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_moderation_recognition_action_recovery_moderati_7b6c3042dab6",
                schema: "islamu_event",
                table: "moderation_recognition_action_recovery",
                column: "capture_item_id",
                principalSchema: "islamu_event",
                principalTable: "moderation_recognition_capture_item",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_moderation_recognition_capture_item_moderation__101918a71ac8",
                schema: "islamu_event",
                table: "moderation_recognition_capture_item",
                column: "continuity_binding_id",
                principalSchema: "islamu_event",
                principalTable: "moderation_recognition_continuity_binding",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_event_report_signals_event_report_cases_tenant__fd513cb2f7a8",
                schema: "islamu_event",
                table: "event_report_signals");

            migrationBuilder.DropForeignKey(
                name: "fk_event_report_signals_event_report_external_link_c0cf91adb760",
                schema: "islamu_event",
                table: "event_report_signals");

            migrationBuilder.DropForeignKey(
                name: "fk_event_report_signals_incoming_webhook_messages__6a26a7335cd6",
                schema: "islamu_event",
                table: "event_report_signals");

            migrationBuilder.DropForeignKey(
                name: "fk_moderation_cases_moderation_cases_occurrence_case_id",
                schema: "islamu_event",
                table: "moderation_cases");

            migrationBuilder.DropForeignKey(
                name: "fk_event_resource_moderation_quarantines_event_res_1a2cc455b743",
                schema: "islamu_event",
                table: "event_resource_moderation_quarantines");

            migrationBuilder.DropForeignKey(
                name: "fk_moderation_recognition_continuity_binding_moder_07f5d4ef50db",
                schema: "islamu_event",
                table: "moderation_recognition_continuity_binding");

            migrationBuilder.DropTable(
                name: "event_resource_moderation_review_completions",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "event_resource_moderation_review_escalations",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "key_obligation",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "key_replica",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_case_attribution_renewal_receipts",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_case_evidence",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_case_intake_acknowledgements",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_case_read_grant_revocation_receipts",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_classification_event_target_adoptions",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_classification_material_categories",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_classification_material_components",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_constitution_reviewers",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_consumer_measurement_receipts",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_content_responses",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_deployment_adoptions",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_deployment_consumers",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_event_rights_adoptions",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_event_target_adoptions",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_measured_operations_acceptances",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_notice_disclosure_approvals",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_notice_notification_receipts",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_original_effect_completion_receipts",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_participant_incident_responsibility_sources",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_policy_publication_candidates",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_policy_selections",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_pre_finding_erasure_tombstones",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_pre_finding_notices",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_pre_finding_receipts",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_profile_adoptions",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_profile_collection_assessment_adoptions",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_profile_collection_subject_observations",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_profile_subject_consents",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_publication_consumers",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_publication_effects",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_publication_evidence",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_publication_modes",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_publication_plans",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_publication_prerequisites",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_publication_scopes",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_publication_subjects",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_qualification_policy_adoptions",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_recognition_action_recovery",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_recognition_disposition_acceptance",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_recognition_disposition_remedy",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_recognition_escalation",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_recognition_key_lifecycle_key",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_recognition_key_lifecycle_purge",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_recognition_privacy_adoptions",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_recognition_privacy_retention",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_recovery_store_states",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_resource_target_adoptions",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_schedule_risk_reviews",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "user_birth_date_collection_receipts",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "event_resource_moderation_review_duties",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_case_attribution_heads",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_operational_budget_adoptions",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_classification_control_bindings",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_classification_material_entries",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_classification_taxonomy_categories",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_constitution_availability",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_consumer_measurement_challenges",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_measurement_issuer_enrollments",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_deployment_sources",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_event_rights_sources",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_measured_operations_sources",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_notices",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_case_narratives",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_case_target_policies",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_pre_finding_custody",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_profile_collection_assessment_sources",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_publication_profiles",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_qualification_policy_sources",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_recognition_disposition",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_recognition_key_lifecycle",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_recognition_privacy_sources",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_contribution_bindings",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_resource_generations",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_operational_budget_sources",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_classification_control_selections",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_consumer_installations",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_notice_sources",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_case_narrative_adoptions",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_case_target_receipts",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_case_attribution_sources",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_profile_collection_procedure_adoptions",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_publication_reasons",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_contribution_selections",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_classification_material_reviews",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_case_narrative_policy_sources",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_case_read_grants",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_profile_collection_procedure_sources",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_publication_classes",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_classification_material_sources",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_case_target_sources",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_event_generations",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_classification_taxonomy_adoptions",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_classification_taxonomy_sources",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_constitution_adoptions",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_child_age_policy_adoptions",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_constitution_sources",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_child_age_policy_sources",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_policy_publications",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "event_resource_quarantine_releases",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "event_resource_quarantine_reviews",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "event_resource_moderation_quarantines",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_recognition_capture_item",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_recognition_pending",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "moderation_recognition_continuity_binding",
                schema: "islamu_event");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_sanctions_scope_scope",
                schema: "islamu_event",
                table: "moderation_sanctions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_sanctions_subject_origin_scope",
                schema: "islamu_event",
                table: "moderation_sanctions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_qualification_receipt_result",
                schema: "islamu_event",
                table: "moderation_qualification_mutation_receipts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_protective_measures_issuing_scope_scope",
                schema: "islamu_event",
                table: "moderation_protective_measures");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_protective_measures_protector_origin_scope",
                schema: "islamu_event",
                table: "moderation_protective_measures");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_protective_measures_reviewer_origin_scope",
                schema: "islamu_event",
                table: "moderation_protective_measures");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_protective_measures_scope_scope",
                schema: "islamu_event",
                table: "moderation_protective_measures");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_protective_measures_subject_origin_scope",
                schema: "islamu_event",
                table: "moderation_protective_measures");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_policies_jurisdiction_scope",
                schema: "islamu_event",
                table: "moderation_policies");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_policy_jurisdiction",
                schema: "islamu_event",
                table: "moderation_policies");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_receipt_scope",
                schema: "islamu_event",
                table: "moderation_operation_receipts");

            migrationBuilder.DropIndex(
                name: "ix_moderation_findings_case_id_rule_owner_id_rule__c1f550c5320a",
                schema: "islamu_event",
                table: "moderation_findings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_finding_attribution",
                schema: "islamu_event",
                table: "moderation_findings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_finding_evidence",
                schema: "islamu_event",
                table: "moderation_findings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_findings_adjudicator_origin_scope",
                schema: "islamu_event",
                table: "moderation_findings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_findings_reversed_by_origin_scope",
                schema: "islamu_event",
                table: "moderation_findings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_findings_scope_scope",
                schema: "islamu_event",
                table: "moderation_findings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_findings_subject_origin_scope",
                schema: "islamu_event",
                table: "moderation_findings");

            migrationBuilder.DropIndex(
                name: "ix_moderation_effect_intents_completed_at_lease_expires_at_id",
                schema: "islamu_event",
                table: "moderation_effect_intents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_effect_completed_lease",
                schema: "islamu_event",
                table: "moderation_effect_intents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_effect_completion_window",
                schema: "islamu_event",
                table: "moderation_effect_intents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_effect_lease",
                schema: "islamu_event",
                table: "moderation_effect_intents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_effect_lease_window",
                schema: "islamu_event",
                table: "moderation_effect_intents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_effect_notice_cleanup",
                schema: "islamu_event",
                table: "moderation_effect_intents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_effect_original_completion",
                schema: "islamu_event",
                table: "moderation_effect_intents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_decisions_approval1_origin_scope",
                schema: "islamu_event",
                table: "moderation_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_decisions_approval2_origin_scope",
                schema: "islamu_event",
                table: "moderation_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_decisions_issuer_origin_scope",
                schema: "islamu_event",
                table: "moderation_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_decisions_issuing_scope_scope",
                schema: "islamu_event",
                table: "moderation_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_decisions_scope_scope",
                schema: "islamu_event",
                table: "moderation_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_decisions_subject_origin_scope",
                schema: "islamu_event",
                table: "moderation_decisions");

            migrationBuilder.DropIndex(
                name: "ix_moderation_cases_occurrence_case_id",
                schema: "islamu_event",
                table: "moderation_cases");

            migrationBuilder.DropIndex(
                name: "ix_moderation_cases_scope_trust_domain_id_scope_te_39d64d68b146",
                schema: "islamu_event",
                table: "moderation_cases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_case_attribution",
                schema: "islamu_event",
                table: "moderation_cases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_case_occurrence_owner",
                schema: "islamu_event",
                table: "moderation_cases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_case_ownership",
                schema: "islamu_event",
                table: "moderation_cases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_cases_scope_scope",
                schema: "islamu_event",
                table: "moderation_cases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_cases_subject_origin_scope",
                schema: "islamu_event",
                table: "moderation_cases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_cases_subject_subject",
                schema: "islamu_event",
                table: "moderation_cases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_moderation_cases_target_origin_scope",
                schema: "islamu_event",
                table: "moderation_cases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_event_resources_material",
                schema: "islamu_event",
                table: "event_resources");

            migrationBuilder.DropCheckConstraint(
                name: "ck_event_resources_quarantine",
                schema: "islamu_event",
                table: "event_resources");

            migrationBuilder.DropIndex(
                name: "ix_event_report_signals_tenant_id_incoming_webhook_message_id",
                schema: "islamu_event",
                table: "event_report_signals");

            migrationBuilder.DropIndex(
                name: "ix_event_report_signals_tenant_id_provider_advisor_b572b1f71201",
                schema: "islamu_event",
                table: "event_report_signals");

            migrationBuilder.DropIndex(
                name: "ix_event_report_signals_tenant_id_provider_provide_a8cb58cd4436",
                schema: "islamu_event",
                table: "event_report_signals");

            migrationBuilder.DropIndex(
                name: "ix_event_report_signals_tenant_id_provider_provide_a8e5d26e7346",
                schema: "islamu_event",
                table: "event_report_signals");

            migrationBuilder.DropIndex(
                name: "ix_event_report_signals_tenant_id_report_id_case_id",
                schema: "islamu_event",
                table: "event_report_signals");

            migrationBuilder.DropIndex(
                name: "ix_event_report_signals_tenant_id_report_id_provider_link_id",
                schema: "islamu_event",
                table: "event_report_signals");

            migrationBuilder.DropCheckConstraint(
                name: "ck_event_report_signals_advisory_source",
                schema: "islamu_event",
                table: "event_report_signals");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_event_report_external_links_tenant_id_report_id_id",
                schema: "islamu_event",
                table: "event_report_external_links");

            migrationBuilder.DropCheckConstraint(
                name: "ck_event_report_external_links_case_generation",
                schema: "islamu_event",
                table: "event_report_external_links");

            migrationBuilder.DropColumn(
                name: "birth_date_collection_receipt_id",
                schema: "islamu_event",
                table: "user_pii");

            migrationBuilder.DropColumn(
                name: "birth_date_revision",
                schema: "islamu_event",
                table: "user_pii");

            migrationBuilder.DropColumn(
                name: "date_of_birth",
                schema: "islamu_event",
                table: "user_pii");

            migrationBuilder.DropColumn(
                name: "scope_event_id",
                schema: "islamu_event",
                table: "moderation_sanctions");

            migrationBuilder.DropColumn(
                name: "subject_origin_event_id",
                schema: "islamu_event",
                table: "moderation_sanctions");

            migrationBuilder.DropColumn(
                name: "issuing_scope_event_id",
                schema: "islamu_event",
                table: "moderation_protective_measures");

            migrationBuilder.DropColumn(
                name: "protector_origin_event_id",
                schema: "islamu_event",
                table: "moderation_protective_measures");

            migrationBuilder.DropColumn(
                name: "reviewer_origin_event_id",
                schema: "islamu_event",
                table: "moderation_protective_measures");

            migrationBuilder.DropColumn(
                name: "scope_event_id",
                schema: "islamu_event",
                table: "moderation_protective_measures");

            migrationBuilder.DropColumn(
                name: "subject_origin_event_id",
                schema: "islamu_event",
                table: "moderation_protective_measures");

            migrationBuilder.DropColumn(
                name: "jurisdiction_event_id",
                schema: "islamu_event",
                table: "moderation_policies");

            migrationBuilder.DropColumn(
                name: "adjudicator_origin_event_id",
                schema: "islamu_event",
                table: "moderation_findings");

            migrationBuilder.DropColumn(
                name: "attribution_generation",
                schema: "islamu_event",
                table: "moderation_findings");

            migrationBuilder.DropColumn(
                name: "attribution_source_id",
                schema: "islamu_event",
                table: "moderation_findings");

            migrationBuilder.DropColumn(
                name: "reason_code",
                schema: "islamu_event",
                table: "moderation_findings");

            migrationBuilder.DropColumn(
                name: "reason_owner_id",
                schema: "islamu_event",
                table: "moderation_findings");

            migrationBuilder.DropColumn(
                name: "reason_revision",
                schema: "islamu_event",
                table: "moderation_findings");

            migrationBuilder.DropColumn(
                name: "reversed_by_origin_event_id",
                schema: "islamu_event",
                table: "moderation_findings");

            migrationBuilder.DropColumn(
                name: "scope_event_id",
                schema: "islamu_event",
                table: "moderation_findings");

            migrationBuilder.DropColumn(
                name: "scope_id",
                schema: "islamu_event",
                table: "moderation_findings");

            migrationBuilder.DropColumn(
                name: "subject_origin_event_id",
                schema: "islamu_event",
                table: "moderation_findings");

            migrationBuilder.DropColumn(
                name: "lease_expires_at",
                schema: "islamu_event",
                table: "moderation_effect_intents");

            migrationBuilder.DropColumn(
                name: "lease_id",
                schema: "islamu_event",
                table: "moderation_effect_intents");

            migrationBuilder.DropColumn(
                name: "lease_started_at",
                schema: "islamu_event",
                table: "moderation_effect_intents");

            migrationBuilder.DropColumn(
                name: "original_completion_host_binding_id",
                schema: "islamu_event",
                table: "moderation_effect_intents");

            migrationBuilder.DropColumn(
                name: "original_completion_id",
                schema: "islamu_event",
                table: "moderation_effect_intents");

            migrationBuilder.DropColumn(
                name: "original_completion_revision",
                schema: "islamu_event",
                table: "moderation_effect_intents");

            migrationBuilder.DropColumn(
                name: "original_completion_writer_tenant_id",
                schema: "islamu_event",
                table: "moderation_effect_intents");

            migrationBuilder.DropColumn(
                name: "original_notice_cleanup_id",
                schema: "islamu_event",
                table: "moderation_effect_intents");

            migrationBuilder.DropColumn(
                name: "original_notice_host_binding_id",
                schema: "islamu_event",
                table: "moderation_effect_intents");

            migrationBuilder.DropColumn(
                name: "original_notice_purged_at",
                schema: "islamu_event",
                table: "moderation_effect_intents");

            migrationBuilder.DropColumn(
                name: "original_notice_retain_until",
                schema: "islamu_event",
                table: "moderation_effect_intents");

            migrationBuilder.DropColumn(
                name: "original_notice_writer_tenant_id",
                schema: "islamu_event",
                table: "moderation_effect_intents");

            migrationBuilder.DropColumn(
                name: "approval1_origin_event_id",
                schema: "islamu_event",
                table: "moderation_decisions");

            migrationBuilder.DropColumn(
                name: "approval2_origin_event_id",
                schema: "islamu_event",
                table: "moderation_decisions");

            migrationBuilder.DropColumn(
                name: "issuer_origin_event_id",
                schema: "islamu_event",
                table: "moderation_decisions");

            migrationBuilder.DropColumn(
                name: "issuing_scope_event_id",
                schema: "islamu_event",
                table: "moderation_decisions");

            migrationBuilder.DropColumn(
                name: "scope_event_id",
                schema: "islamu_event",
                table: "moderation_decisions");

            migrationBuilder.DropColumn(
                name: "subject_origin_event_id",
                schema: "islamu_event",
                table: "moderation_decisions");

            migrationBuilder.DropColumn(
                name: "attribution_source_id",
                schema: "islamu_event",
                table: "moderation_cases");

            migrationBuilder.DropColumn(
                name: "occurrence_case_id",
                schema: "islamu_event",
                table: "moderation_cases");

            migrationBuilder.DropColumn(
                name: "occurrence_evidence_revision",
                schema: "islamu_event",
                table: "moderation_cases");

            migrationBuilder.DropColumn(
                name: "occurrence_owner_id",
                schema: "islamu_event",
                table: "moderation_cases");

            migrationBuilder.DropColumn(
                name: "occurrence_owner_kind",
                schema: "islamu_event",
                table: "moderation_cases");

            migrationBuilder.DropColumn(
                name: "scope_event_id",
                schema: "islamu_event",
                table: "moderation_cases");

            migrationBuilder.DropColumn(
                name: "subject_origin_event_id",
                schema: "islamu_event",
                table: "moderation_cases");

            migrationBuilder.DropColumn(
                name: "target_id",
                schema: "islamu_event",
                table: "moderation_cases");

            migrationBuilder.DropColumn(
                name: "target_kind",
                schema: "islamu_event",
                table: "moderation_cases");

            migrationBuilder.DropColumn(
                name: "target_origin_community_id",
                schema: "islamu_event",
                table: "moderation_cases");

            migrationBuilder.DropColumn(
                name: "target_origin_event_id",
                schema: "islamu_event",
                table: "moderation_cases");

            migrationBuilder.DropColumn(
                name: "target_origin_instance_id",
                schema: "islamu_event",
                table: "moderation_cases");

            migrationBuilder.DropColumn(
                name: "target_origin_kind",
                schema: "islamu_event",
                table: "moderation_cases");

            migrationBuilder.DropColumn(
                name: "target_origin_resource_id",
                schema: "islamu_event",
                table: "moderation_cases");

            migrationBuilder.DropColumn(
                name: "target_origin_solution_id",
                schema: "islamu_event",
                table: "moderation_cases");

            migrationBuilder.DropColumn(
                name: "target_origin_tenant_id",
                schema: "islamu_event",
                table: "moderation_cases");

            migrationBuilder.DropColumn(
                name: "target_origin_trust_domain_id",
                schema: "islamu_event",
                table: "moderation_cases");

            migrationBuilder.DropColumn(
                name: "target_version",
                schema: "islamu_event",
                table: "moderation_cases");

            migrationBuilder.DropColumn(
                name: "material_version_id",
                schema: "islamu_event",
                table: "event_resources");

            migrationBuilder.DropColumn(
                name: "moderation_quarantine_count",
                schema: "islamu_event",
                table: "event_resources");

            migrationBuilder.DropColumn(
                name: "advisory_identity",
                schema: "islamu_event",
                table: "event_report_signals");

            migrationBuilder.DropColumn(
                name: "advisory_material_sha256",
                schema: "islamu_event",
                table: "event_report_signals");

            migrationBuilder.DropColumn(
                name: "advisory_ordinal",
                schema: "islamu_event",
                table: "event_report_signals");

            migrationBuilder.DropColumn(
                name: "case_id",
                schema: "islamu_event",
                table: "event_report_signals");

            migrationBuilder.DropColumn(
                name: "incoming_webhook_message_id",
                schema: "islamu_event",
                table: "event_report_signals");

            migrationBuilder.DropColumn(
                name: "provider_link_id",
                schema: "islamu_event",
                table: "event_report_signals");

            migrationBuilder.DropColumn(
                name: "source_case_concurrency_stamp",
                schema: "islamu_event",
                table: "event_report_signals");

            migrationBuilder.DropColumn(
                name: "case_concurrency_stamp",
                schema: "islamu_event",
                table: "event_report_external_links");

            migrationBuilder.DropColumn(
                name: "moderation_reservation",
                schema: "islamu_event",
                table: "configuration_import_sessions");

            migrationBuilder.AlterColumn<Guid>(
                name: "subject_origin_trust_domain_id",
                schema: "islamu_event",
                table: "moderation_cases",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "subject_origin_kind",
                schema: "islamu_event",
                table: "moderation_cases",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "subject_kind",
                schema: "islamu_event",
                table: "moderation_cases",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "subject_id",
                schema: "islamu_event",
                table: "moderation_cases",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_sanctions_scope_scope",
                schema: "islamu_event",
                table: "moderation_sanctions",
                sql: "scope_kind IS NOT NULL AND ((scope_kind = 1 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 2 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 3 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 4 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 5 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NOT NULL AND scope_resource_id IS NULL) OR (scope_kind = 6 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_resource_id IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_sanctions_subject_origin_scope",
                schema: "islamu_event",
                table: "moderation_sanctions",
                sql: "subject_origin_kind IS NOT NULL AND ((subject_origin_kind = 1 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 2 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 3 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 4 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 5 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NOT NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 6 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_resource_id IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_qualification_receipt_result",
                schema: "islamu_event",
                table: "moderation_qualification_mutation_receipts",
                sql: "source_kind BETWEEN 1 AND 5 AND result_revision > 0 AND qualification_revision > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_protective_measures_issuing_scope_scope",
                schema: "islamu_event",
                table: "moderation_protective_measures",
                sql: "issuing_scope_kind IS NOT NULL AND ((issuing_scope_kind = 1 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NULL AND issuing_scope_instance_id IS NULL AND issuing_scope_tenant_id IS NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL) OR (issuing_scope_kind = 2 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NULL AND issuing_scope_tenant_id IS NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL) OR (issuing_scope_kind = 3 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL) OR (issuing_scope_kind = 4 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NOT NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL) OR (issuing_scope_kind = 5 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NOT NULL AND issuing_scope_community_id IS NOT NULL AND issuing_scope_resource_id IS NULL) OR (issuing_scope_kind = 6 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NOT NULL AND issuing_scope_resource_id IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_protective_measures_protector_origin_scope",
                schema: "islamu_event",
                table: "moderation_protective_measures",
                sql: "protector_origin_kind IS NOT NULL AND ((protector_origin_kind = 1 AND protector_origin_trust_domain_id IS NOT NULL AND protector_origin_solution_id IS NULL AND protector_origin_instance_id IS NULL AND protector_origin_tenant_id IS NULL AND protector_origin_community_id IS NULL AND protector_origin_resource_id IS NULL) OR (protector_origin_kind = 2 AND protector_origin_trust_domain_id IS NOT NULL AND protector_origin_solution_id IS NOT NULL AND protector_origin_instance_id IS NULL AND protector_origin_tenant_id IS NULL AND protector_origin_community_id IS NULL AND protector_origin_resource_id IS NULL) OR (protector_origin_kind = 3 AND protector_origin_trust_domain_id IS NOT NULL AND protector_origin_solution_id IS NOT NULL AND protector_origin_instance_id IS NOT NULL AND protector_origin_tenant_id IS NULL AND protector_origin_community_id IS NULL AND protector_origin_resource_id IS NULL) OR (protector_origin_kind = 4 AND protector_origin_trust_domain_id IS NOT NULL AND protector_origin_solution_id IS NOT NULL AND protector_origin_instance_id IS NOT NULL AND protector_origin_tenant_id IS NOT NULL AND protector_origin_community_id IS NULL AND protector_origin_resource_id IS NULL) OR (protector_origin_kind = 5 AND protector_origin_trust_domain_id IS NOT NULL AND protector_origin_solution_id IS NOT NULL AND protector_origin_instance_id IS NOT NULL AND protector_origin_tenant_id IS NOT NULL AND protector_origin_community_id IS NOT NULL AND protector_origin_resource_id IS NULL) OR (protector_origin_kind = 6 AND protector_origin_trust_domain_id IS NOT NULL AND protector_origin_solution_id IS NOT NULL AND protector_origin_instance_id IS NOT NULL AND protector_origin_tenant_id IS NOT NULL AND protector_origin_resource_id IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_protective_measures_reviewer_origin_scope",
                schema: "islamu_event",
                table: "moderation_protective_measures",
                sql: "(reviewer_origin_kind IS NULL AND reviewer_origin_trust_domain_id IS NULL AND reviewer_origin_solution_id IS NULL AND reviewer_origin_instance_id IS NULL AND reviewer_origin_tenant_id IS NULL AND reviewer_origin_community_id IS NULL AND reviewer_origin_resource_id IS NULL) OR (reviewer_origin_kind IS NOT NULL AND ((reviewer_origin_kind = 1 AND reviewer_origin_trust_domain_id IS NOT NULL AND reviewer_origin_solution_id IS NULL AND reviewer_origin_instance_id IS NULL AND reviewer_origin_tenant_id IS NULL AND reviewer_origin_community_id IS NULL AND reviewer_origin_resource_id IS NULL) OR (reviewer_origin_kind = 2 AND reviewer_origin_trust_domain_id IS NOT NULL AND reviewer_origin_solution_id IS NOT NULL AND reviewer_origin_instance_id IS NULL AND reviewer_origin_tenant_id IS NULL AND reviewer_origin_community_id IS NULL AND reviewer_origin_resource_id IS NULL) OR (reviewer_origin_kind = 3 AND reviewer_origin_trust_domain_id IS NOT NULL AND reviewer_origin_solution_id IS NOT NULL AND reviewer_origin_instance_id IS NOT NULL AND reviewer_origin_tenant_id IS NULL AND reviewer_origin_community_id IS NULL AND reviewer_origin_resource_id IS NULL) OR (reviewer_origin_kind = 4 AND reviewer_origin_trust_domain_id IS NOT NULL AND reviewer_origin_solution_id IS NOT NULL AND reviewer_origin_instance_id IS NOT NULL AND reviewer_origin_tenant_id IS NOT NULL AND reviewer_origin_community_id IS NULL AND reviewer_origin_resource_id IS NULL) OR (reviewer_origin_kind = 5 AND reviewer_origin_trust_domain_id IS NOT NULL AND reviewer_origin_solution_id IS NOT NULL AND reviewer_origin_instance_id IS NOT NULL AND reviewer_origin_tenant_id IS NOT NULL AND reviewer_origin_community_id IS NOT NULL AND reviewer_origin_resource_id IS NULL) OR (reviewer_origin_kind = 6 AND reviewer_origin_trust_domain_id IS NOT NULL AND reviewer_origin_solution_id IS NOT NULL AND reviewer_origin_instance_id IS NOT NULL AND reviewer_origin_tenant_id IS NOT NULL AND reviewer_origin_resource_id IS NOT NULL)))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_protective_measures_scope_scope",
                schema: "islamu_event",
                table: "moderation_protective_measures",
                sql: "scope_kind IS NOT NULL AND ((scope_kind = 1 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 2 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 3 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 4 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 5 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NOT NULL AND scope_resource_id IS NULL) OR (scope_kind = 6 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_resource_id IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_protective_measures_subject_origin_scope",
                schema: "islamu_event",
                table: "moderation_protective_measures",
                sql: "subject_origin_kind IS NOT NULL AND ((subject_origin_kind = 1 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 2 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 3 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 4 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 5 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NOT NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 6 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_resource_id IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_policies_jurisdiction_scope",
                schema: "islamu_event",
                table: "moderation_policies",
                sql: "jurisdiction_kind IS NOT NULL AND ((jurisdiction_kind = 1 AND jurisdiction_trust_domain_id IS NOT NULL AND jurisdiction_solution_id IS NULL AND jurisdiction_instance_id IS NULL AND jurisdiction_tenant_id IS NULL AND jurisdiction_community_id IS NULL AND jurisdiction_resource_id IS NULL) OR (jurisdiction_kind = 2 AND jurisdiction_trust_domain_id IS NOT NULL AND jurisdiction_solution_id IS NOT NULL AND jurisdiction_instance_id IS NULL AND jurisdiction_tenant_id IS NULL AND jurisdiction_community_id IS NULL AND jurisdiction_resource_id IS NULL) OR (jurisdiction_kind = 3 AND jurisdiction_trust_domain_id IS NOT NULL AND jurisdiction_solution_id IS NOT NULL AND jurisdiction_instance_id IS NOT NULL AND jurisdiction_tenant_id IS NULL AND jurisdiction_community_id IS NULL AND jurisdiction_resource_id IS NULL) OR (jurisdiction_kind = 4 AND jurisdiction_trust_domain_id IS NOT NULL AND jurisdiction_solution_id IS NOT NULL AND jurisdiction_instance_id IS NOT NULL AND jurisdiction_tenant_id IS NOT NULL AND jurisdiction_community_id IS NULL AND jurisdiction_resource_id IS NULL) OR (jurisdiction_kind = 5 AND jurisdiction_trust_domain_id IS NOT NULL AND jurisdiction_solution_id IS NOT NULL AND jurisdiction_instance_id IS NOT NULL AND jurisdiction_tenant_id IS NOT NULL AND jurisdiction_community_id IS NOT NULL AND jurisdiction_resource_id IS NULL) OR (jurisdiction_kind = 6 AND jurisdiction_trust_domain_id IS NOT NULL AND jurisdiction_solution_id IS NOT NULL AND jurisdiction_instance_id IS NOT NULL AND jurisdiction_tenant_id IS NOT NULL AND jurisdiction_resource_id IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_policy_jurisdiction",
                schema: "islamu_event",
                table: "moderation_policies",
                sql: "jurisdiction_id = CASE jurisdiction_kind WHEN 1 THEN jurisdiction_trust_domain_id WHEN 2 THEN jurisdiction_solution_id WHEN 3 THEN jurisdiction_instance_id WHEN 4 THEN jurisdiction_tenant_id WHEN 5 THEN jurisdiction_community_id WHEN 6 THEN jurisdiction_resource_id END");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_receipt_scope",
                schema: "islamu_event",
                table: "moderation_operation_receipts",
                sql: "scope_kind BETWEEN 1 AND 6 AND subject_kind BETWEEN 1 AND 4");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_findings_case_id_rule_owner_id_rule__cc96a1f86c78",
                schema: "islamu_event",
                table: "moderation_findings",
                columns: new[] { "case_id", "rule_owner_id", "rule_code", "rule_revision" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_finding_evidence",
                schema: "islamu_event",
                table: "moderation_findings",
                sql: "case_revision = 1 AND rule_revision > 0 AND evidence_recorded_at >= case_created_at AND evidence_recorded_at <= finalized_at AND finalized_at >= case_created_at AND ((evidence_purpose = 1 AND urgency_qualification_id IS NULL) OR (evidence_purpose = 2 AND urgency_qualification_id IS NOT NULL AND urgency_qualification_id <> evidence_reference_id))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_findings_adjudicator_origin_scope",
                schema: "islamu_event",
                table: "moderation_findings",
                sql: "adjudicator_origin_kind IS NOT NULL AND ((adjudicator_origin_kind = 1 AND adjudicator_origin_trust_domain_id IS NOT NULL AND adjudicator_origin_solution_id IS NULL AND adjudicator_origin_instance_id IS NULL AND adjudicator_origin_tenant_id IS NULL AND adjudicator_origin_community_id IS NULL AND adjudicator_origin_resource_id IS NULL) OR (adjudicator_origin_kind = 2 AND adjudicator_origin_trust_domain_id IS NOT NULL AND adjudicator_origin_solution_id IS NOT NULL AND adjudicator_origin_instance_id IS NULL AND adjudicator_origin_tenant_id IS NULL AND adjudicator_origin_community_id IS NULL AND adjudicator_origin_resource_id IS NULL) OR (adjudicator_origin_kind = 3 AND adjudicator_origin_trust_domain_id IS NOT NULL AND adjudicator_origin_solution_id IS NOT NULL AND adjudicator_origin_instance_id IS NOT NULL AND adjudicator_origin_tenant_id IS NULL AND adjudicator_origin_community_id IS NULL AND adjudicator_origin_resource_id IS NULL) OR (adjudicator_origin_kind = 4 AND adjudicator_origin_trust_domain_id IS NOT NULL AND adjudicator_origin_solution_id IS NOT NULL AND adjudicator_origin_instance_id IS NOT NULL AND adjudicator_origin_tenant_id IS NOT NULL AND adjudicator_origin_community_id IS NULL AND adjudicator_origin_resource_id IS NULL) OR (adjudicator_origin_kind = 5 AND adjudicator_origin_trust_domain_id IS NOT NULL AND adjudicator_origin_solution_id IS NOT NULL AND adjudicator_origin_instance_id IS NOT NULL AND adjudicator_origin_tenant_id IS NOT NULL AND adjudicator_origin_community_id IS NOT NULL AND adjudicator_origin_resource_id IS NULL) OR (adjudicator_origin_kind = 6 AND adjudicator_origin_trust_domain_id IS NOT NULL AND adjudicator_origin_solution_id IS NOT NULL AND adjudicator_origin_instance_id IS NOT NULL AND adjudicator_origin_tenant_id IS NOT NULL AND adjudicator_origin_resource_id IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_findings_reversed_by_origin_scope",
                schema: "islamu_event",
                table: "moderation_findings",
                sql: "(reversed_by_origin_kind IS NULL AND reversed_by_origin_trust_domain_id IS NULL AND reversed_by_origin_solution_id IS NULL AND reversed_by_origin_instance_id IS NULL AND reversed_by_origin_tenant_id IS NULL AND reversed_by_origin_community_id IS NULL AND reversed_by_origin_resource_id IS NULL) OR (reversed_by_origin_kind IS NOT NULL AND ((reversed_by_origin_kind = 1 AND reversed_by_origin_trust_domain_id IS NOT NULL AND reversed_by_origin_solution_id IS NULL AND reversed_by_origin_instance_id IS NULL AND reversed_by_origin_tenant_id IS NULL AND reversed_by_origin_community_id IS NULL AND reversed_by_origin_resource_id IS NULL) OR (reversed_by_origin_kind = 2 AND reversed_by_origin_trust_domain_id IS NOT NULL AND reversed_by_origin_solution_id IS NOT NULL AND reversed_by_origin_instance_id IS NULL AND reversed_by_origin_tenant_id IS NULL AND reversed_by_origin_community_id IS NULL AND reversed_by_origin_resource_id IS NULL) OR (reversed_by_origin_kind = 3 AND reversed_by_origin_trust_domain_id IS NOT NULL AND reversed_by_origin_solution_id IS NOT NULL AND reversed_by_origin_instance_id IS NOT NULL AND reversed_by_origin_tenant_id IS NULL AND reversed_by_origin_community_id IS NULL AND reversed_by_origin_resource_id IS NULL) OR (reversed_by_origin_kind = 4 AND reversed_by_origin_trust_domain_id IS NOT NULL AND reversed_by_origin_solution_id IS NOT NULL AND reversed_by_origin_instance_id IS NOT NULL AND reversed_by_origin_tenant_id IS NOT NULL AND reversed_by_origin_community_id IS NULL AND reversed_by_origin_resource_id IS NULL) OR (reversed_by_origin_kind = 5 AND reversed_by_origin_trust_domain_id IS NOT NULL AND reversed_by_origin_solution_id IS NOT NULL AND reversed_by_origin_instance_id IS NOT NULL AND reversed_by_origin_tenant_id IS NOT NULL AND reversed_by_origin_community_id IS NOT NULL AND reversed_by_origin_resource_id IS NULL) OR (reversed_by_origin_kind = 6 AND reversed_by_origin_trust_domain_id IS NOT NULL AND reversed_by_origin_solution_id IS NOT NULL AND reversed_by_origin_instance_id IS NOT NULL AND reversed_by_origin_tenant_id IS NOT NULL AND reversed_by_origin_resource_id IS NOT NULL)))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_findings_scope_scope",
                schema: "islamu_event",
                table: "moderation_findings",
                sql: "scope_kind IS NOT NULL AND ((scope_kind = 1 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 2 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 3 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 4 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 5 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NOT NULL AND scope_resource_id IS NULL) OR (scope_kind = 6 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_resource_id IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_findings_subject_origin_scope",
                schema: "islamu_event",
                table: "moderation_findings",
                sql: "subject_origin_kind IS NOT NULL AND ((subject_origin_kind = 1 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 2 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 3 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 4 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 5 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NOT NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 6 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_resource_id IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_decisions_approval1_origin_scope",
                schema: "islamu_event",
                table: "moderation_decisions",
                sql: "(approval1_origin_kind IS NULL AND approval1_origin_trust_domain_id IS NULL AND approval1_origin_solution_id IS NULL AND approval1_origin_instance_id IS NULL AND approval1_origin_tenant_id IS NULL AND approval1_origin_community_id IS NULL AND approval1_origin_resource_id IS NULL) OR (approval1_origin_kind IS NOT NULL AND ((approval1_origin_kind = 1 AND approval1_origin_trust_domain_id IS NOT NULL AND approval1_origin_solution_id IS NULL AND approval1_origin_instance_id IS NULL AND approval1_origin_tenant_id IS NULL AND approval1_origin_community_id IS NULL AND approval1_origin_resource_id IS NULL) OR (approval1_origin_kind = 2 AND approval1_origin_trust_domain_id IS NOT NULL AND approval1_origin_solution_id IS NOT NULL AND approval1_origin_instance_id IS NULL AND approval1_origin_tenant_id IS NULL AND approval1_origin_community_id IS NULL AND approval1_origin_resource_id IS NULL) OR (approval1_origin_kind = 3 AND approval1_origin_trust_domain_id IS NOT NULL AND approval1_origin_solution_id IS NOT NULL AND approval1_origin_instance_id IS NOT NULL AND approval1_origin_tenant_id IS NULL AND approval1_origin_community_id IS NULL AND approval1_origin_resource_id IS NULL) OR (approval1_origin_kind = 4 AND approval1_origin_trust_domain_id IS NOT NULL AND approval1_origin_solution_id IS NOT NULL AND approval1_origin_instance_id IS NOT NULL AND approval1_origin_tenant_id IS NOT NULL AND approval1_origin_community_id IS NULL AND approval1_origin_resource_id IS NULL) OR (approval1_origin_kind = 5 AND approval1_origin_trust_domain_id IS NOT NULL AND approval1_origin_solution_id IS NOT NULL AND approval1_origin_instance_id IS NOT NULL AND approval1_origin_tenant_id IS NOT NULL AND approval1_origin_community_id IS NOT NULL AND approval1_origin_resource_id IS NULL) OR (approval1_origin_kind = 6 AND approval1_origin_trust_domain_id IS NOT NULL AND approval1_origin_solution_id IS NOT NULL AND approval1_origin_instance_id IS NOT NULL AND approval1_origin_tenant_id IS NOT NULL AND approval1_origin_resource_id IS NOT NULL)))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_decisions_approval2_origin_scope",
                schema: "islamu_event",
                table: "moderation_decisions",
                sql: "(approval2_origin_kind IS NULL AND approval2_origin_trust_domain_id IS NULL AND approval2_origin_solution_id IS NULL AND approval2_origin_instance_id IS NULL AND approval2_origin_tenant_id IS NULL AND approval2_origin_community_id IS NULL AND approval2_origin_resource_id IS NULL) OR (approval2_origin_kind IS NOT NULL AND ((approval2_origin_kind = 1 AND approval2_origin_trust_domain_id IS NOT NULL AND approval2_origin_solution_id IS NULL AND approval2_origin_instance_id IS NULL AND approval2_origin_tenant_id IS NULL AND approval2_origin_community_id IS NULL AND approval2_origin_resource_id IS NULL) OR (approval2_origin_kind = 2 AND approval2_origin_trust_domain_id IS NOT NULL AND approval2_origin_solution_id IS NOT NULL AND approval2_origin_instance_id IS NULL AND approval2_origin_tenant_id IS NULL AND approval2_origin_community_id IS NULL AND approval2_origin_resource_id IS NULL) OR (approval2_origin_kind = 3 AND approval2_origin_trust_domain_id IS NOT NULL AND approval2_origin_solution_id IS NOT NULL AND approval2_origin_instance_id IS NOT NULL AND approval2_origin_tenant_id IS NULL AND approval2_origin_community_id IS NULL AND approval2_origin_resource_id IS NULL) OR (approval2_origin_kind = 4 AND approval2_origin_trust_domain_id IS NOT NULL AND approval2_origin_solution_id IS NOT NULL AND approval2_origin_instance_id IS NOT NULL AND approval2_origin_tenant_id IS NOT NULL AND approval2_origin_community_id IS NULL AND approval2_origin_resource_id IS NULL) OR (approval2_origin_kind = 5 AND approval2_origin_trust_domain_id IS NOT NULL AND approval2_origin_solution_id IS NOT NULL AND approval2_origin_instance_id IS NOT NULL AND approval2_origin_tenant_id IS NOT NULL AND approval2_origin_community_id IS NOT NULL AND approval2_origin_resource_id IS NULL) OR (approval2_origin_kind = 6 AND approval2_origin_trust_domain_id IS NOT NULL AND approval2_origin_solution_id IS NOT NULL AND approval2_origin_instance_id IS NOT NULL AND approval2_origin_tenant_id IS NOT NULL AND approval2_origin_resource_id IS NOT NULL)))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_decisions_issuer_origin_scope",
                schema: "islamu_event",
                table: "moderation_decisions",
                sql: "issuer_origin_kind IS NOT NULL AND ((issuer_origin_kind = 1 AND issuer_origin_trust_domain_id IS NOT NULL AND issuer_origin_solution_id IS NULL AND issuer_origin_instance_id IS NULL AND issuer_origin_tenant_id IS NULL AND issuer_origin_community_id IS NULL AND issuer_origin_resource_id IS NULL) OR (issuer_origin_kind = 2 AND issuer_origin_trust_domain_id IS NOT NULL AND issuer_origin_solution_id IS NOT NULL AND issuer_origin_instance_id IS NULL AND issuer_origin_tenant_id IS NULL AND issuer_origin_community_id IS NULL AND issuer_origin_resource_id IS NULL) OR (issuer_origin_kind = 3 AND issuer_origin_trust_domain_id IS NOT NULL AND issuer_origin_solution_id IS NOT NULL AND issuer_origin_instance_id IS NOT NULL AND issuer_origin_tenant_id IS NULL AND issuer_origin_community_id IS NULL AND issuer_origin_resource_id IS NULL) OR (issuer_origin_kind = 4 AND issuer_origin_trust_domain_id IS NOT NULL AND issuer_origin_solution_id IS NOT NULL AND issuer_origin_instance_id IS NOT NULL AND issuer_origin_tenant_id IS NOT NULL AND issuer_origin_community_id IS NULL AND issuer_origin_resource_id IS NULL) OR (issuer_origin_kind = 5 AND issuer_origin_trust_domain_id IS NOT NULL AND issuer_origin_solution_id IS NOT NULL AND issuer_origin_instance_id IS NOT NULL AND issuer_origin_tenant_id IS NOT NULL AND issuer_origin_community_id IS NOT NULL AND issuer_origin_resource_id IS NULL) OR (issuer_origin_kind = 6 AND issuer_origin_trust_domain_id IS NOT NULL AND issuer_origin_solution_id IS NOT NULL AND issuer_origin_instance_id IS NOT NULL AND issuer_origin_tenant_id IS NOT NULL AND issuer_origin_resource_id IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_decisions_issuing_scope_scope",
                schema: "islamu_event",
                table: "moderation_decisions",
                sql: "issuing_scope_kind IS NOT NULL AND ((issuing_scope_kind = 1 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NULL AND issuing_scope_instance_id IS NULL AND issuing_scope_tenant_id IS NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL) OR (issuing_scope_kind = 2 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NULL AND issuing_scope_tenant_id IS NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL) OR (issuing_scope_kind = 3 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL) OR (issuing_scope_kind = 4 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NOT NULL AND issuing_scope_community_id IS NULL AND issuing_scope_resource_id IS NULL) OR (issuing_scope_kind = 5 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NOT NULL AND issuing_scope_community_id IS NOT NULL AND issuing_scope_resource_id IS NULL) OR (issuing_scope_kind = 6 AND issuing_scope_trust_domain_id IS NOT NULL AND issuing_scope_solution_id IS NOT NULL AND issuing_scope_instance_id IS NOT NULL AND issuing_scope_tenant_id IS NOT NULL AND issuing_scope_resource_id IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_decisions_scope_scope",
                schema: "islamu_event",
                table: "moderation_decisions",
                sql: "scope_kind IS NOT NULL AND ((scope_kind = 1 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 2 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 3 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 4 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 5 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NOT NULL AND scope_resource_id IS NULL) OR (scope_kind = 6 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_resource_id IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_decisions_subject_origin_scope",
                schema: "islamu_event",
                table: "moderation_decisions",
                sql: "subject_origin_kind IS NOT NULL AND ((subject_origin_kind = 1 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 2 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 3 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 4 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 5 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NOT NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 6 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_resource_id IS NOT NULL))");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_cases_scope_trust_domain_id_scope_te_f8fb806fdc5e",
                schema: "islamu_event",
                table: "moderation_cases",
                columns: new[] { "scope_trust_domain_id", "scope_tenant_id", "occurrence_id" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_cases_scope_scope",
                schema: "islamu_event",
                table: "moderation_cases",
                sql: "scope_kind IS NOT NULL AND ((scope_kind = 1 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 2 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 3 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 4 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL) OR (scope_kind = 5 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NOT NULL AND scope_resource_id IS NULL) OR (scope_kind = 6 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_resource_id IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_cases_subject_origin_scope",
                schema: "islamu_event",
                table: "moderation_cases",
                sql: "subject_origin_kind IS NOT NULL AND ((subject_origin_kind = 1 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 2 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 3 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 4 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 5 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_community_id IS NOT NULL AND subject_origin_resource_id IS NULL) OR (subject_origin_kind = 6 AND subject_origin_trust_domain_id IS NOT NULL AND subject_origin_solution_id IS NOT NULL AND subject_origin_instance_id IS NOT NULL AND subject_origin_tenant_id IS NOT NULL AND subject_origin_resource_id IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_moderation_cases_subject_subject",
                schema: "islamu_event",
                table: "moderation_cases",
                sql: "subject_kind BETWEEN 1 AND 4");

            migrationBuilder.CreateIndex(
                name: "ix_event_report_signals_tenant_id_provider_provide_a8cb58cd4436",
                schema: "islamu_event",
                table: "event_report_signals",
                columns: new[] { "tenant_id", "provider", "provider_target_scope", "provider_target_id", "external_signal_id" },
                unique: true,
                filter: "external_signal_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_event_report_signals_tenant_id_provider_provide_a8e5d26e7346",
                schema: "islamu_event",
                table: "event_report_signals",
                columns: new[] { "tenant_id", "provider", "provider_target_scope", "provider_target_id", "correlation_id" },
                unique: true);
        }
    }
}

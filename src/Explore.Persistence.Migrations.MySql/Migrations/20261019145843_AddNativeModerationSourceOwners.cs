using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.MySql.Migrations
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
                name: "ix_ie_moderation_findings_case_id_rule_owner_id_rule_co_a3f515ca",
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
                name: "ix_ie_moderation_cases_scope_trust_domain_id_scope_tena_03fcca41",
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
                name: "ix_ie_event_report_signals_tenant_id_provider_provider__8d2fa8b8",
                table: "ie_event_report_signals");

            migrationBuilder.DropIndex(
                name: "ix_ie_event_report_signals_tenant_id_provider_provider__9034510f",
                table: "ie_event_report_signals");

            migrationBuilder.AddColumn<Guid>(
                name: "birth_date_collection_receipt_id",
                table: "ie_user_pii",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<long>(
                name: "birth_date_revision",
                table: "ie_user_pii",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateOnly>(
                name: "date_of_birth",
                table: "ie_user_pii",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "scope_event_id",
                table: "ie_moderation_sanctions",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "subject_origin_event_id",
                table: "ie_moderation_sanctions",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "issuing_scope_event_id",
                table: "ie_moderation_protective_measures",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "protector_origin_event_id",
                table: "ie_moderation_protective_measures",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "reviewer_origin_event_id",
                table: "ie_moderation_protective_measures",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "scope_event_id",
                table: "ie_moderation_protective_measures",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "subject_origin_event_id",
                table: "ie_moderation_protective_measures",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "jurisdiction_event_id",
                table: "ie_moderation_policies",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "adjudicator_origin_event_id",
                table: "ie_moderation_findings",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<long>(
                name: "attribution_generation",
                table: "ie_moderation_findings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "attribution_source_id",
                table: "ie_moderation_findings",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<string>(
                name: "reason_code",
                table: "ie_moderation_findings",
                type: "varchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<Guid>(
                name: "reason_owner_id",
                table: "ie_moderation_findings",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<long>(
                name: "reason_revision",
                table: "ie_moderation_findings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "reversed_by_origin_event_id",
                table: "ie_moderation_findings",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "scope_event_id",
                table: "ie_moderation_findings",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "scope_id",
                table: "ie_moderation_findings",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "subject_origin_event_id",
                table: "ie_moderation_findings",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "lease_expires_at",
                table: "ie_moderation_effect_intents",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "lease_id",
                table: "ie_moderation_effect_intents",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "lease_started_at",
                table: "ie_moderation_effect_intents",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "original_completion_host_binding_id",
                table: "ie_moderation_effect_intents",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "original_completion_id",
                table: "ie_moderation_effect_intents",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<long>(
                name: "original_completion_revision",
                table: "ie_moderation_effect_intents",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "original_completion_writer_tenant_id",
                table: "ie_moderation_effect_intents",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "original_notice_cleanup_id",
                table: "ie_moderation_effect_intents",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "original_notice_host_binding_id",
                table: "ie_moderation_effect_intents",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "original_notice_purged_at",
                table: "ie_moderation_effect_intents",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "original_notice_retain_until",
                table: "ie_moderation_effect_intents",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "original_notice_writer_tenant_id",
                table: "ie_moderation_effect_intents",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "approval1_origin_event_id",
                table: "ie_moderation_decisions",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "approval2_origin_event_id",
                table: "ie_moderation_decisions",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "issuer_origin_event_id",
                table: "ie_moderation_decisions",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "issuing_scope_event_id",
                table: "ie_moderation_decisions",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "scope_event_id",
                table: "ie_moderation_decisions",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "subject_origin_event_id",
                table: "ie_moderation_decisions",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AlterColumn<Guid>(
                name: "subject_origin_trust_domain_id",
                table: "ie_moderation_cases",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci",
                oldClrType: typeof(Guid),
                oldType: "char(36)")
                .OldAnnotation("Relational:Collation", "ascii_general_ci");

            migrationBuilder.AlterColumn<int>(
                name: "subject_origin_kind",
                table: "ie_moderation_cases",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "subject_kind",
                table: "ie_moderation_cases",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<Guid>(
                name: "subject_id",
                table: "ie_moderation_cases",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci",
                oldClrType: typeof(Guid),
                oldType: "char(36)")
                .OldAnnotation("Relational:Collation", "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "attribution_source_id",
                table: "ie_moderation_cases",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "occurrence_case_id",
                table: "ie_moderation_cases",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<long>(
                name: "occurrence_evidence_revision",
                table: "ie_moderation_cases",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "occurrence_owner_id",
                table: "ie_moderation_cases",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<int>(
                name: "occurrence_owner_kind",
                table: "ie_moderation_cases",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "scope_event_id",
                table: "ie_moderation_cases",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "subject_origin_event_id",
                table: "ie_moderation_cases",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "target_id",
                table: "ie_moderation_cases",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<int>(
                name: "target_kind",
                table: "ie_moderation_cases",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "target_origin_community_id",
                table: "ie_moderation_cases",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "target_origin_event_id",
                table: "ie_moderation_cases",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "target_origin_instance_id",
                table: "ie_moderation_cases",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<int>(
                name: "target_origin_kind",
                table: "ie_moderation_cases",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "target_origin_resource_id",
                table: "ie_moderation_cases",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "target_origin_solution_id",
                table: "ie_moderation_cases",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "target_origin_tenant_id",
                table: "ie_moderation_cases",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "target_origin_trust_domain_id",
                table: "ie_moderation_cases",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "target_version",
                table: "ie_moderation_cases",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "material_version_id",
                table: "ie_event_resources",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<int>(
                name: "moderation_quarantine_count",
                table: "ie_event_resources",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "advisory_identity",
                table: "ie_event_report_signals",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "advisory_material_sha256",
                table: "ie_event_report_signals",
                type: "varchar(71)",
                maxLength: 71,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "advisory_ordinal",
                table: "ie_event_report_signals",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "case_id",
                table: "ie_event_report_signals",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "incoming_webhook_message_id",
                table: "ie_event_report_signals",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "provider_link_id",
                table: "ie_event_report_signals",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "source_case_concurrency_stamp",
                table: "ie_event_report_signals",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "case_concurrency_stamp",
                table: "ie_event_report_external_links",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<string>(
                name: "moderation_reservation",
                table: "ie_configuration_import_sessions",
                type: "longtext",
                maxLength: 1100000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "non_advisory_correlation_id",
                table: "ie_event_report_signals",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true,
                computedColumnSql: "CASE WHEN advisory_identity IS NULL THEN correlation_id ELSE NULL END",
                stored: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "non_advisory_external_signal_id",
                table: "ie_event_report_signals",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true,
                computedColumnSql: "CASE WHEN advisory_identity IS NULL THEN external_signal_id ELSE NULL END",
                stored: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_event_report_external_links_tenant_id_report_id_id",
                table: "ie_event_report_external_links",
                columns: new[] { "tenant_id", "report_id", "id" });

            migrationBuilder.CreateTable(
                name: "ie_key_obligation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    intent_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    key_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    key_generation = table.Column<long>(type: "bigint", nullable: false),
                    verification_tag = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    capture_material_receipt = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    material_receipt = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    original_captured_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    original_retain_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    backup_horizon_ticks = table.Column<long>(type: "bigint", nullable: false),
                    backup_safety_margin_ticks = table.Column<long>(type: "bigint", nullable: false),
                    receipt_retention_ticks = table.Column<long>(type: "bigint", nullable: false),
                    readable_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    authority_sequence = table.Column<long>(type: "bigint", nullable: true),
                    material_pruned_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    receipt_retain_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_key_obligation", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_key_replica",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    runtime_replica_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    ring_generation = table.Column<long>(type: "bigint", nullable: false),
                    ring_commitment = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    binding_revision = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    source = table.Column<int>(type: "int", nullable: false),
                    verified_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_key_replica", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_case_evidence",
                columns: table => new
                {
                    case_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    case_revision = table.Column<long>(type: "bigint", nullable: false),
                    source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_version = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    material_fact_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_origin_kind = table.Column<int>(type: "int", nullable: false),
                    source_origin_trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_origin_solution_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    source_origin_instance_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    source_origin_tenant_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    source_origin_community_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    source_origin_resource_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    source_origin_event_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    recorded_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_case_narrative_policy_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    target_kind = table.Column<int>(type: "int", nullable: false),
                    purpose = table.Column<int>(type: "int", nullable: false),
                    maximum_retention_ticks = table.Column<long>(type: "bigint", nullable: false),
                    maximum_material_bytes = table.Column<int>(type: "int", nullable: false),
                    privacy_owner_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    privacy_controller_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    governance_controller_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    identity_and_independence_basis = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    privacy_legal_mandate_basis = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    recorded_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_case_narrative_policy_sources", x => x.id);
                    table.UniqueConstraint("ak_ie_moderation_case_narrative_policy_sources_id_host__bfc46542", x => new { x.id, x.host_binding_id, x.trust_domain_id, x.tenant_id });
                    table.CheckConstraint("ck_case_narrative_policy_generation", "revision = 1 AND writer_generation > 0");
                    table.CheckConstraint("ck_case_narrative_policy_independence", "privacy_owner_human_id <> recorded_by_human_id AND privacy_controller_human_id <> governance_controller_human_id AND privacy_controller_human_id <> recorded_by_human_id AND privacy_owner_human_id <> governance_controller_human_id");
                    table.CheckConstraint("ck_case_narrative_policy_limits", "maximum_retention_ticks > 0 AND maximum_retention_ticks < 9223372036854775807 AND maximum_material_bytes > 0 AND maximum_material_bytes <= 65536 AND valid_until > recorded_at");
                    table.CheckConstraint("ck_case_narrative_policy_purpose", "purpose IN (1, 3) OR (purpose = 2 AND target_kind = 2)");
                    table.ForeignKey(
                        name: "fk_ie_moderation_case_narrative_policy_sources_ie_moder_e9b9d4c3",
                        column: x => x.host_binding_id,
                        principalTable: "ie_moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_case_narrative_policy_sources_ie_tenan_de83c2d4",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_case_narrative_policy_sources_ie_users_435f903e",
                        column: x => x.recorded_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_case_narrative_policy_sources_ie_users_8ccca020",
                        column: x => x.governance_controller_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_case_narrative_policy_sources_ie_users_b5f797fa",
                        column: x => x.privacy_owner_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_case_narrative_policy_sources_ie_users_bcc077fc",
                        column: x => x.privacy_controller_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_case_target_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    target_kind = table.Column<int>(type: "int", nullable: false),
                    submission_lifetime_ticks = table.Column<long>(type: "bigint", nullable: false),
                    read_lifetime_ticks = table.Column<long>(type: "bigint", nullable: false),
                    enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    changed_by_user_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    revision = table.Column<long>(type: "bigint", nullable: false)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_case_target_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    caller_user_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    qualification_epoch_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    qualification_revision = table.Column<long>(type: "bigint", nullable: false),
                    relationship_source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    relationship_kind = table.Column<int>(type: "int", nullable: false),
                    relationship_revision = table.Column<long>(type: "bigint", nullable: true),
                    relationship_concurrency_stamp = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    relationship_facts_digest = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    lifetime_policy_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    lifetime_policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    token_digest = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    target_kind = table.Column<int>(type: "int", nullable: false),
                    target_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    target_version = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    target_origin_kind = table.Column<int>(type: "int", nullable: false),
                    target_origin_trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    target_origin_solution_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    target_origin_instance_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    target_origin_tenant_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    target_origin_community_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    target_origin_resource_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    target_origin_event_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    source_origin_kind = table.Column<int>(type: "int", nullable: false),
                    source_origin_trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_origin_solution_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    source_origin_instance_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    source_origin_tenant_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    source_origin_community_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    source_origin_resource_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    source_origin_event_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    occurrence_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_version = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    material_fact_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    issued_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    consumed_case_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    consumed_operation_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    consumed_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false)
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
                        name: "fk_ie_moderation_case_target_receipts_ie_moderation_cas_0f5f69fa",
                        column: x => x.consumed_case_id,
                        principalTable: "ie_moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_case_target_receipts_ie_moderation_hos_c63c38ec",
                        column: x => x.host_binding_id,
                        principalTable: "ie_moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_case_target_receipts_ie_moderation_qua_a38ec186",
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_case_target_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    version = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    event_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    event_version = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    kind = table.Column<int>(type: "int", nullable: false),
                    native_kind = table.Column<int>(type: "int", nullable: false),
                    native_target_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    native_target_version = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    native_facts_digest = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    occurrence_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    occurrence_version = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    material_fact_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    occurrence_digest = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    material_digest = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    recorded_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_constitution_availability",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    reviewer_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    maximum_caseload = table.Column<int>(type: "int", nullable: false),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    withdrawn_by_human_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_constitution_availability", x => x.id);
                    table.CheckConstraint("ck_moderation_constitution_availability_capacity", "maximum_caseload > 0 AND writer_generation > 0");
                    table.CheckConstraint("ck_moderation_constitution_availability_validity", "valid_until > recorded_at");
                    table.CheckConstraint("ck_moderation_constitution_availability_withdrawal", "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR (revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at >= recorded_at)");
                    table.ForeignKey(
                        name: "fk_ie_moderation_constitution_availability_ie_users_rev_a89433f4",
                        column: x => x.reviewer_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_constitution_availability_ie_users_wit_4aff2aeb",
                        column: x => x.withdrawn_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_consumer_installations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    consumer_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    mode_code = table.Column<int>(type: "int", nullable: false),
                    runtime_generation = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    inventory_fingerprint = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    installed_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    installed_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    withdrawn_by_human_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_consumer_installations", x => x.id);
                    table.CheckConstraint("ck_moderation_consumer_installation_generation", "writer_generation > 0");
                    table.CheckConstraint("ck_moderation_consumer_installation_owner", "consumer_id <> '' AND mode_code = 1");
                    table.CheckConstraint("ck_moderation_consumer_installation_validity", "valid_until > installed_at");
                    table.CheckConstraint("ck_moderation_consumer_installation_withdrawal", "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR (revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at >= installed_at)");
                    table.ForeignKey(
                        name: "fk_ie_moderation_consumer_installations_ie_moderation_h_edfe0998",
                        column: x => x.host_binding_id,
                        principalTable: "ie_moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_consumer_installations_ie_users_instal_89ada84f",
                        column: x => x.installed_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_consumer_installations_ie_users_withdr_90bb9d8b",
                        column: x => x.withdrawn_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_consumer_installations_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_event_generations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    event_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    observed_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    observed_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    event_stamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    actor_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    actor_stamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    organizer_actor_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    organizer_actor_stamp = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    submitted_by_human_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_event_generations", x => x.id);
                    table.CheckConstraint("ck_moderation_event_generation_organizer", "(organizer_actor_id IS NULL AND organizer_actor_stamp IS NULL) OR (organizer_actor_id IS NOT NULL AND organizer_actor_stamp IS NOT NULL)");
                    table.CheckConstraint("ck_moderation_event_generation_revision", "revision > 0 AND writer_generation > 0");
                    table.CheckConstraint("ck_moderation_event_generation_validity", "valid_until > observed_at");
                    table.ForeignKey(
                        name: "fk_ie_moderation_event_generations_ie_moderation_host_b_77df30c0",
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_measurement_issuer_enrollments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    key_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    issuer = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    public_key_spki_base64 = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    runner_digest = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    catalogue_digest = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    test_bundle_digest = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    approved_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    revoked_by_human_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    revoked_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_measurement_issuer_enrollments", x => x.id);
                    table.CheckConstraint("ck_moderation_measurement_issuer_enrollment_generation", "writer_generation > 0 AND writer_generation < 9223372036854775807");
                    table.CheckConstraint("ck_moderation_measurement_issuer_enrollment_key", "key_id <> id");
                    table.CheckConstraint("ck_moderation_measurement_issuer_enrollment_revocation", "(revision = 1 AND revoked_at IS NULL AND revoked_by_human_id IS NULL) OR (revision = 2 AND revoked_at IS NOT NULL AND revoked_by_human_id IS NOT NULL AND revoked_at >= approved_at)");
                    table.CheckConstraint("ck_moderation_measurement_issuer_enrollment_validity", "valid_until > approved_at");
                    table.ForeignKey(
                        name: "fk_ie_moderation_measurement_issuer_enrollments_ie_mode_f2a469ab",
                        column: x => x.host_binding_id,
                        principalTable: "ie_moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_measurement_issuer_enrollments_ie_tena_accc8ed8",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_measurement_issuer_enrollments_ie_user_03570481",
                        column: x => x.approved_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_measurement_issuer_enrollments_ie_user_52fc068c",
                        column: x => x.revoked_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_notice_sources",
                columns: table => new
                {
                    effect_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    operation_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    decision_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    decision_revision = table.Column<long>(type: "bigint", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    scope_kind = table.Column<int>(type: "int", nullable: false),
                    scope_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    subject_kind = table.Column<int>(type: "int", nullable: false),
                    subject_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    snapshot_json = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    snapshot_hash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    effect_binding_hash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    captured_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    retain_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    status_metadata_json = table.Column<string>(type: "varchar(8192)", maxLength: 8192, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status_metadata_hash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
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
                        name: "fk_ie_moderation_notice_sources_ie_moderation_effect_in_7fa74342",
                        column: x => x.effect_id,
                        principalTable: "ie_moderation_effect_intents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_operational_budget_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    subject_kind = table.Column<int>(type: "int", nullable: false),
                    action = table.Column<int>(type: "int", nullable: false),
                    maximum_occurrences = table.Column<int>(type: "int", nullable: false),
                    window_ticks = table.Column<long>(type: "bigint", nullable: false),
                    operator_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    recorded_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    recorded_at = table.Column<long>(type: "bigint", nullable: false),
                    valid_until = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_operational_budget_sources", x => x.id);
                    table.UniqueConstraint("ak_ie_moderation_operational_budget_sources_id_host_bin_5d891af2", x => new { x.id, x.host_binding_id, x.trust_domain_id, x.tenant_id });
                    table.CheckConstraint("ck_operational_budget_source_generation", "revision = 1 AND writer_generation > 0 AND writer_generation < 9223372036854775807");
                    table.CheckConstraint("ck_operational_budget_source_independence", "operator_human_id <> recorded_by_human_id");
                    table.CheckConstraint("ck_operational_budget_source_terms", "subject_kind = 1 AND action IN (1, 2, 3, 4, 5, 6) AND maximum_occurrences > 0 AND window_ticks > 0 AND window_ticks < 9223372036854775807");
                    table.CheckConstraint("ck_operational_budget_source_validity", "recorded_at > 0 AND valid_until > recorded_at AND valid_until < 3155378975999999999 AND window_ticks < recorded_at AND window_ticks < 3155378975999999999 - recorded_at");
                    table.ForeignKey(
                        name: "fk_ie_moderation_operational_budget_sources_ie_moderati_0c589afc",
                        column: x => x.host_binding_id,
                        principalTable: "ie_moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_operational_budget_sources_ie_users_op_e4cb64d2",
                        column: x => x.operator_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_operational_budget_sources_ie_users_re_79a7f0a4",
                        column: x => x.recorded_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_operational_budget_sources_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_policy_publications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    policy_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    adopted_revision = table.Column<long>(type: "bigint", nullable: true),
                    previous_publication_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    solution_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    instance_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    jurisdiction_tenant_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    principal_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    operation_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    request_digest = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    declared_state_digest = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    authority_source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    authority_revision = table.Column<long>(type: "bigint", nullable: false),
                    qualification_revision = table.Column<long>(type: "bigint", nullable: false),
                    writer_generation = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    catalogue_revision = table.Column<long>(type: "bigint", nullable: false),
                    inventory_revision = table.Column<long>(type: "bigint", nullable: false),
                    inventory_fingerprint = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    published_active = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    evidence_valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    maximum_finite_duration_ticks = table.Column<long>(type: "bigint", nullable: false),
                    maximum_review_delay_ticks = table.Column<long>(type: "bigint", nullable: false),
                    minimum_approvals = table.Column<int>(type: "int", nullable: false),
                    maximum_approvals = table.Column<int>(type: "int", nullable: false),
                    minimum_contestation_calendar_months = table.Column<int>(type: "int", nullable: false)
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
                        name: "fk_ie_moderation_policy_publications_ie_moderation_host_36312838",
                        column: x => x.host_binding_id,
                        principalTable: "ie_moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_policy_publications_ie_moderation_poli_408b26b7",
                        columns: x => new { x.policy_id, x.previous_publication_id },
                        principalTable: "ie_moderation_policy_publications",
                        principalColumns: new[] { "policy_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_policy_publications_ie_moderation_qual_06bf6744",
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_pre_finding_erasure_tombstones",
                columns: table => new
                {
                    case_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_pre_finding_erasure_tombstones", x => x.case_id);
                    table.ForeignKey(
                        name: "fk_ie_moderation_pre_finding_erasure_tombstones_ie_mode_aec82a4e",
                        column: x => x.case_id,
                        principalTable: "ie_moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_pre_finding_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    case_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    actor_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    result_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    result_revision = table.Column<long>(type: "bigint", nullable: false),
                    request_digest = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    completed_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_pre_finding_receipts", x => x.id);
                    table.ForeignKey(
                        name: "fk_ie_moderation_pre_finding_receipts_ie_moderation_cas_292a20e4",
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_qualification_policy_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    case_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    case_revision = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    maximum_reviewer_lifetime_ticks = table.Column<long>(type: "bigint", nullable: false),
                    maximum_urgency_lifetime_ticks = table.Column<long>(type: "bigint", nullable: false),
                    published_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    published_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_qualification_policy_sources", x => x.id);
                    table.UniqueConstraint("ak_ie_moderation_qualification_policy_sources_id_host_b_38e8afea", x => new { x.id, x.host_binding_id, x.trust_domain_id, x.tenant_id, x.case_id });
                    table.CheckConstraint("ck_moderation_qualification_policy_source_generation", "revision > 0 AND case_revision > 0 AND writer_generation > 0");
                    table.CheckConstraint("ck_moderation_qualification_policy_source_limits", "maximum_reviewer_lifetime_ticks > 0 AND maximum_reviewer_lifetime_ticks < 9223372036854775807 AND maximum_urgency_lifetime_ticks > 0 AND maximum_urgency_lifetime_ticks < 9223372036854775807");
                    table.CheckConstraint("ck_moderation_qualification_policy_source_validity", "valid_until > published_at");
                    table.ForeignKey(
                        name: "fk_ie_moderation_qualification_policy_sources_ie_modera_1c93f4ac",
                        column: x => x.case_id,
                        principalTable: "ie_moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_qualification_policy_sources_ie_modera_f209398f",
                        column: x => x.host_binding_id,
                        principalTable: "ie_moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_qualification_policy_sources_ie_tenant_634803ea",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_qualification_policy_sources_ie_users__9f1104e1",
                        column: x => x.published_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_recognition_disposition",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    intent_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    subject_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    policy_version = table.Column<int>(type: "int", nullable: false),
                    reason_code = table.Column<int>(type: "int", nullable: false),
                    pending_revision = table.Column<long>(type: "bigint", nullable: false),
                    original_material_receipt = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    original_material_count = table.Column<int>(type: "int", nullable: false),
                    observed_material_commitment = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    coverage_receipt = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    coverage_count = table.Column<int>(type: "int", nullable: false),
                    observed_failure = table.Column<int>(type: "int", nullable: false),
                    grounds_receipt = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    legal_mandate_receipt = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_revision = table.Column<long>(type: "bigint", nullable: false),
                    source_generation = table.Column<long>(type: "bigint", nullable: false),
                    adoption_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    adoption_revision = table.Column<long>(type: "bigint", nullable: false),
                    adoption_generation = table.Column<long>(type: "bigint", nullable: false),
                    privacy_owner_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    recovery_owner_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    grounds = table.Column<int>(type: "int", nullable: false),
                    original_deadline = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    assessed_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    authorized_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    authority_high_water_before_authorization = table.Column<long>(type: "bigint", nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    authority_sequence = table.Column<long>(type: "bigint", nullable: true),
                    authority_recorded_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    replay_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    retain_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_recognition_disposition", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_recognition_disposition_remedy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    intent_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    pending_revision = table.Column<long>(type: "bigint", nullable: false),
                    original_material_receipt = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    observed_material_commitment = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    grounds_receipt = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    legal_mandate_receipt = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    coverage_receipt = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    privacy_owner_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    recovery_owner_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    grounds = table.Column<int>(type: "int", nullable: false),
                    receipt_hash = table.Column<byte[]>(type: "varbinary(32)", maxLength: 32, nullable: false),
                    protected_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    remedy_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    retain_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_recognition_disposition_remedy", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_recognition_escalation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    intent_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    target_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    action_recovery_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    pending_revision = table.Column<long>(type: "bigint", nullable: false),
                    recipient_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    other_owner_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    stage = table.Column<int>(type: "int", nullable: false),
                    required_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    original_deadline = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    retain_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    notification_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    delivered_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_recognition_escalation", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_recognition_key_lifecycle",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ring_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    acknowledgement_sequence = table.Column<long>(type: "bigint", nullable: false),
                    initial_erasure_head = table.Column<long>(type: "bigint", nullable: false),
                    initial_erasure_retained_floor = table.Column<long>(type: "bigint", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_recognition_key_lifecycle", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_recognition_key_lifecycle_purge",
                columns: table => new
                {
                    intent_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    material_receipt = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    material_count = table.Column<int>(type: "int", nullable: false),
                    authority_sequence = table.Column<long>(type: "bigint", nullable: false),
                    purged_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    backup_obligation_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    retain_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_recognition_key_lifecycle_purge", x => x.intent_id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_recognition_pending",
                columns: table => new
                {
                    intent_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    subject_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    policy_version = table.Column<int>(type: "int", nullable: false),
                    reason_code = table.Column<int>(type: "int", nullable: false),
                    privacy_owner_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    recovery_owner_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    received_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    settle_by = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    escalate_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    receipt_hash = table.Column<byte[]>(type: "varbinary(32)", maxLength: 32, nullable: false),
                    receipt_expires_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    state = table.Column<int>(type: "int", nullable: false),
                    failure = table.Column<int>(type: "int", nullable: false),
                    material_count = table.Column<int>(type: "int", nullable: false),
                    material_receipt = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    computed_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    authority_sequence = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_recognition_pending", x => x.intent_id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_recovery_store_states",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    generation = table.Column<long>(type: "bigint", nullable: false),
                    position = table.Column<long>(type: "bigint", nullable: false),
                    installation_slot = table.Column<int>(type: "int", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_recovery_store_states", x => x.id);
                    table.CheckConstraint("ck_moderation_recovery_store_coordinates", "revision > 0 AND generation > 0 AND generation <= revision - position AND position >= 0 AND position < revision");
                    table.CheckConstraint("ck_moderation_recovery_store_slot", "installation_slot = 1");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_resource_generations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    resource_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    observed_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    observed_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    resource_stamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    material_version_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    event_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    event_stamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    actor_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    actor_stamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    organizer_actor_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    organizer_actor_stamp = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    delivery_type = table.Column<int>(type: "int", nullable: false),
                    storage_object_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    storage_object_stamp = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    storage_provider_binding_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    storage_provider = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_resource_generations", x => x.id);
                    table.CheckConstraint("ck_moderation_resource_generation_material", "material_version_id <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("ck_moderation_resource_generation_revision", "revision > 0 AND writer_generation > 0");
                    table.CheckConstraint("ck_moderation_resource_generation_storage", "(delivery_type = 1 AND storage_object_id IS NOT NULL AND storage_object_stamp IS NOT NULL AND storage_provider_binding_id IS NOT NULL AND storage_provider IS NOT NULL) OR (delivery_type = 2 AND storage_object_id IS NULL AND storage_object_stamp IS NULL AND storage_provider_binding_id IS NULL AND storage_provider IS NULL)");
                    table.CheckConstraint("ck_moderation_resource_generation_validity", "valid_until > observed_at");
                    table.ForeignKey(
                        name: "fk_ie_moderation_resource_generations_ie_moderation_hos_11f89026",
                        column: x => x.host_binding_id,
                        principalTable: "ie_moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_resource_generations_ie_users_observed_b17c983b",
                        column: x => x.observed_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_resource_generations_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_schedule_risk_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_sanction_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_sanction_revision = table.Column<long>(type: "bigint", nullable: false),
                    source_decision_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    original_sanction_starts_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    original_sanction_ends_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    original_due_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    state = table.Column<int>(type: "int", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    accepted_intent_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    accepted_finding_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    accepted_finding_revision = table.Column<long>(type: "bigint", nullable: true),
                    accepted_reviewer_assignment_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    accepted_reviewer_assignment_revision = table.Column<long>(type: "bigint", nullable: true),
                    withdrawal_intent_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    subject_kind = table.Column<int>(type: "int", nullable: false),
                    subject_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    subject_origin_kind = table.Column<int>(type: "int", nullable: false),
                    subject_origin_trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    subject_origin_solution_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    subject_origin_instance_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    subject_origin_tenant_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    subject_origin_community_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    subject_origin_resource_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    subject_origin_event_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    source_scope_kind = table.Column<int>(type: "int", nullable: false),
                    source_scope_trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_scope_solution_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    source_scope_instance_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    source_scope_tenant_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    source_scope_community_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    source_scope_resource_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    source_scope_event_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    source_rule_owner_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_rule_code = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    source_rule_revision = table.Column<long>(type: "bigint", nullable: false),
                    event_scope_kind = table.Column<int>(type: "int", nullable: false),
                    event_scope_trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    event_scope_solution_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    event_scope_instance_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    event_scope_tenant_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    event_scope_community_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    event_scope_resource_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    event_scope_event_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    session_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    participant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    original_schedule_generation = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    schedule_generation = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    original_participant_generation = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    participant_generation = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    policy_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    original_policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    original_publication_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    publication_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    policy_rule_owner_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    policy_rule_code = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    original_policy_rule_revision = table.Column<long>(type: "bigint", nullable: false),
                    policy_rule_revision = table.Column<long>(type: "bigint", nullable: false),
                    owner_kind = table.Column<int>(type: "int", nullable: false),
                    owner_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    owner_origin_kind = table.Column<int>(type: "int", nullable: false),
                    owner_origin_trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    owner_origin_solution_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    owner_origin_instance_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    owner_origin_tenant_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    owner_origin_community_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    owner_origin_resource_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    owner_origin_event_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    escalation_owner_kind = table.Column<int>(type: "int", nullable: false),
                    escalation_owner_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    escalation_owner_origin_kind = table.Column<int>(type: "int", nullable: false),
                    escalation_owner_origin_trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    escalation_owner_origin_solution_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    escalation_owner_origin_instance_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    escalation_owner_origin_tenant_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    escalation_owner_origin_community_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    escalation_owner_origin_resource_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    escalation_owner_origin_event_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    withdrawn_by_human_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_schedule_risk_reviews", x => x.id);
                    table.CheckConstraint("ck_moderation_schedule_risk_ancestry", "subject_kind = 1 AND owner_kind = 1 AND escalation_owner_kind = 1 AND event_scope_kind = 7 AND event_scope_tenant_id IS NOT NULL AND source_sanction_revision > 0 AND revision > 0 AND source_rule_revision > 0 AND original_policy_revision > 0 AND policy_revision >= original_policy_revision AND original_policy_rule_revision > 0 AND policy_rule_revision >= original_policy_rule_revision AND subject_origin_trust_domain_id = source_scope_trust_domain_id AND owner_origin_trust_domain_id = source_scope_trust_domain_id AND escalation_owner_origin_trust_domain_id = source_scope_trust_domain_id AND event_scope_trust_domain_id = source_scope_trust_domain_id AND (source_scope_solution_id IS NULL OR source_scope_solution_id = event_scope_solution_id) AND (source_scope_instance_id IS NULL OR source_scope_instance_id = event_scope_instance_id) AND (source_scope_tenant_id IS NULL OR source_scope_tenant_id = event_scope_tenant_id) AND source_scope_community_id IS NULL AND source_scope_resource_id IS NULL AND (source_scope_event_id IS NULL OR source_scope_event_id = event_scope_event_id)");
                    table.CheckConstraint("ck_moderation_schedule_risk_identity", "source_sanction_id <> '00000000-0000-0000-0000-000000000000' AND source_decision_id <> '00000000-0000-0000-0000-000000000000' AND session_id <> '00000000-0000-0000-0000-000000000000' AND participant_id <> '00000000-0000-0000-0000-000000000000' AND policy_id <> '00000000-0000-0000-0000-000000000000' AND original_schedule_generation <> '00000000-0000-0000-0000-000000000000' AND schedule_generation <> '00000000-0000-0000-0000-000000000000' AND original_participant_generation <> '00000000-0000-0000-0000-000000000000' AND participant_generation <> '00000000-0000-0000-0000-000000000000' AND original_publication_id <> '00000000-0000-0000-0000-000000000000' AND publication_id <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("ck_moderation_schedule_risk_interval", "updated_at >= created_at AND original_due_at > created_at AND due_at > created_at AND (original_sanction_ends_at IS NULL OR original_sanction_ends_at > original_sanction_starts_at)");
                    table.CheckConstraint("ck_moderation_schedule_risk_reviews_escalation_owne_c54de0ed43b4", "escalation_owner_origin_kind IS NOT NULL AND ((escalation_owner_origin_kind = 1 AND escalation_owner_origin_trust_domain_id IS NOT NULL AND escalation_owner_origin_solution_id IS NULL AND escalation_owner_origin_instance_id IS NULL AND escalation_owner_origin_tenant_id IS NULL AND escalation_owner_origin_community_id IS NULL AND escalation_owner_origin_resource_id IS NULL AND escalation_owner_origin_event_id IS NULL) OR (escalation_owner_origin_kind = 2 AND escalation_owner_origin_trust_domain_id IS NOT NULL AND escalation_owner_origin_solution_id IS NOT NULL AND escalation_owner_origin_instance_id IS NULL AND escalation_owner_origin_tenant_id IS NULL AND escalation_owner_origin_community_id IS NULL AND escalation_owner_origin_resource_id IS NULL AND escalation_owner_origin_event_id IS NULL) OR (escalation_owner_origin_kind = 3 AND escalation_owner_origin_trust_domain_id IS NOT NULL AND escalation_owner_origin_solution_id IS NOT NULL AND escalation_owner_origin_instance_id IS NOT NULL AND escalation_owner_origin_tenant_id IS NULL AND escalation_owner_origin_community_id IS NULL AND escalation_owner_origin_resource_id IS NULL AND escalation_owner_origin_event_id IS NULL) OR (escalation_owner_origin_kind = 4 AND escalation_owner_origin_trust_domain_id IS NOT NULL AND escalation_owner_origin_solution_id IS NOT NULL AND escalation_owner_origin_instance_id IS NOT NULL AND escalation_owner_origin_tenant_id IS NOT NULL AND escalation_owner_origin_community_id IS NULL AND escalation_owner_origin_resource_id IS NULL AND escalation_owner_origin_event_id IS NULL) OR (escalation_owner_origin_kind = 5 AND escalation_owner_origin_trust_domain_id IS NOT NULL AND escalation_owner_origin_solution_id IS NOT NULL AND escalation_owner_origin_instance_id IS NOT NULL AND escalation_owner_origin_tenant_id IS NOT NULL AND escalation_owner_origin_community_id IS NOT NULL AND escalation_owner_origin_resource_id IS NULL AND escalation_owner_origin_event_id IS NULL) OR (escalation_owner_origin_kind = 6 AND escalation_owner_origin_trust_domain_id IS NOT NULL AND escalation_owner_origin_solution_id IS NOT NULL AND escalation_owner_origin_instance_id IS NOT NULL AND escalation_owner_origin_tenant_id IS NOT NULL AND escalation_owner_origin_resource_id IS NOT NULL AND escalation_owner_origin_event_id IS NULL) OR (escalation_owner_origin_kind = 7 AND escalation_owner_origin_trust_domain_id IS NOT NULL AND escalation_owner_origin_solution_id IS NOT NULL AND escalation_owner_origin_instance_id IS NOT NULL AND escalation_owner_origin_tenant_id IS NOT NULL AND escalation_owner_origin_community_id IS NULL AND escalation_owner_origin_resource_id IS NULL AND escalation_owner_origin_event_id IS NOT NULL AND escalation_owner_origin_event_id <> '00000000-0000-0000-0000-000000000000'))");
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
                        name: "fk_ie_moderation_schedule_risk_reviews_ie_moderation_fi_abc6dd4d",
                        column: x => x.accepted_finding_id,
                        principalTable: "ie_moderation_findings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_schedule_risk_reviews_ie_moderation_re_43f88e3c",
                        column: x => x.accepted_reviewer_assignment_id,
                        principalTable: "ie_moderation_reviewer_assignments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_schedule_risk_reviews_ie_moderation_sa_ceac69d4",
                        column: x => x.source_sanction_id,
                        principalTable: "ie_moderation_sanctions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_user_birth_date_collection_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    user_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    observation_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    assessment_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    assessment_adoption_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    consent_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    policy_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    publication_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    profile_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    collected_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_case_narrative_adoptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_revision = table.Column<long>(type: "bigint", nullable: false),
                    source_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    adopted_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    withdrawn_by_human_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_case_narrative_adoptions", x => x.id);
                    table.UniqueConstraint("ak_ie_moderation_case_narrative_adoptions_id_host_bindi_c0f25f58", x => new { x.id, x.host_binding_id, x.trust_domain_id, x.tenant_id, x.source_id });
                    table.CheckConstraint("ck_case_narrative_adoption_generation", "source_revision = 1 AND source_writer_generation > 0 AND writer_generation > source_writer_generation");
                    table.CheckConstraint("ck_case_narrative_adoption_validity", "valid_until > adopted_at");
                    table.CheckConstraint("ck_case_narrative_adoption_withdrawal", "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR (revision = 2 AND withdrawn_at >= adopted_at AND withdrawn_by_human_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_ie_moderation_case_narrative_adoptions_ie_moderation_0d89204d",
                        columns: x => new { x.source_id, x.host_binding_id, x.trust_domain_id, x.tenant_id },
                        principalTable: "ie_moderation_case_narrative_policy_sources",
                        principalColumns: new[] { "id", "host_binding_id", "trust_domain_id", "tenant_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_case_narrative_adoptions_ie_users_adop_0f57f5ae",
                        column: x => x.adopted_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_case_narrative_adoptions_ie_users_with_422c3208",
                        column: x => x.withdrawn_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_case_read_grants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    case_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    case_revision = table.Column<long>(type: "bigint", nullable: false),
                    source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    caller_user_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    issuer_user_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    issuer_role_grant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    issuer_platform_grant = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_case_read_grants", x => x.id);
                    table.CheckConstraint("ck_moderation_case_read_grant_validity", "expires_at > issued_at AND revision > 0 AND case_revision > 0 AND (revoked_at IS NULL OR revoked_at >= issued_at)");
                    table.ForeignKey(
                        name: "fk_ie_moderation_case_read_grants_ie_moderation_case_ta_2296be95",
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_notices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    effect_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    audience = table.Column<int>(type: "int", nullable: false),
                    recipient_subject_kind = table.Column<int>(type: "int", nullable: false),
                    recipient_subject_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    native_intent_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    delivery_disposition = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_notices", x => x.id);
                    table.CheckConstraint("ck_moderation_notice_native", "(native_intent_id IS NULL AND delivery_disposition BETWEEN 1 AND 4) OR (native_intent_id IS NOT NULL AND delivery_disposition IN (5, 6) AND tenant_id IS NOT NULL AND recipient_subject_kind = 1)");
                    table.CheckConstraint("ck_moderation_notice_recipient", "recipient_subject_kind BETWEEN 1 AND 4 AND audience BETWEEN 1 AND 3 AND (audience <> 3 OR recipient_subject_kind = 1)");
                    table.ForeignKey(
                        name: "fk_ie_moderation_notices_ie_notification_intents_native_c3f754fc",
                        column: x => x.native_intent_id,
                        principalTable: "ie_notification_intents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_notices_moderation_notice_sources_effect_id",
                        column: x => x.effect_id,
                        principalTable: "ie_moderation_notice_sources",
                        principalColumn: "effect_id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_original_effect_completion_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    effect_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    lease_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    lease_revision = table.Column<long>(type: "bigint", nullable: false),
                    lease_started_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    lease_expires_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    publication_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    profile_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    snapshot_hash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    effect_binding_hash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    selection_digest = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    proof_digest = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    original_cause_count = table.Column<int>(type: "int", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    qualified_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    retain_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_original_effect_completion_receipts", x => x.id);
                    table.CheckConstraint("ck_original_completion_count", "original_cause_count >= 1 AND original_cause_count <= 64");
                    table.CheckConstraint("ck_original_completion_revision", "lease_revision > 0");
                    table.CheckConstraint("ck_original_completion_window", "completed_at >= lease_started_at AND completed_at < qualified_until AND qualified_until <= lease_expires_at AND qualified_until <= retain_until");
                    table.ForeignKey(
                        name: "fk_ie_moderation_original_effect_completion_receipts_ie_acc4585b",
                        column: x => x.effect_id,
                        principalTable: "ie_moderation_notice_sources",
                        principalColumn: "effect_id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_operational_budget_adoptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_revision = table.Column<long>(type: "bigint", nullable: false),
                    source_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    adopted_at = table.Column<long>(type: "bigint", nullable: false),
                    valid_until = table.Column<long>(type: "bigint", nullable: false),
                    withdrawn_by_human_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    withdrawn_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_operational_budget_adoptions", x => x.id);
                    table.UniqueConstraint("ak_ie_moderation_operational_budget_adoptions_id_host_b_007da5b9", x => new { x.id, x.host_binding_id, x.trust_domain_id, x.tenant_id, x.source_id });
                    table.UniqueConstraint("ak_ie_moderation_operational_budget_adoptions_id_trust__a25b1ba8", x => new { x.id, x.trust_domain_id, x.tenant_id, x.source_id });
                    table.CheckConstraint("ck_operational_budget_adoption_generation", "id <> source_id AND source_revision = 1 AND source_writer_generation > 0 AND source_writer_generation < 9223372036854775807 AND writer_generation > source_writer_generation AND writer_generation < 9223372036854775807");
                    table.CheckConstraint("ck_operational_budget_adoption_validity", "adopted_at > 0 AND valid_until > adopted_at AND valid_until < 3155378975999999999");
                    table.CheckConstraint("ck_operational_budget_adoption_withdrawal", "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR (revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_at >= adopted_at AND withdrawn_at < 3155378975999999999 AND withdrawn_by_human_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_ie_moderation_operational_budget_adoptions_ie_modera_67bec5b8",
                        columns: x => new { x.source_id, x.host_binding_id, x.trust_domain_id, x.tenant_id },
                        principalTable: "ie_moderation_operational_budget_sources",
                        principalColumns: new[] { "id", "host_binding_id", "trust_domain_id", "tenant_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_operational_budget_adoptions_ie_users__172ab459",
                        column: x => x.adopted_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_operational_budget_adoptions_ie_users__94da71d8",
                        column: x => x.withdrawn_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_child_age_policy_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    policy_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    publication_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    definition_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    profile_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    mode_code = table.Column<int>(type: "int", nullable: false),
                    proposed_state_digest = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    scope_ceiling = table.Column<int>(type: "int", nullable: false),
                    collection = table.Column<int>(type: "int", nullable: false),
                    consent = table.Column<int>(type: "int", nullable: false),
                    assurance = table.Column<int>(type: "int", nullable: false),
                    provider_assurance = table.Column<int>(type: "int", nullable: false),
                    evidence = table.Column<int>(type: "int", nullable: false),
                    classification = table.Column<int>(type: "int", nullable: false),
                    debate_minimum_age = table.Column<int>(type: "int", nullable: false),
                    audience = table.Column<int>(type: "int", nullable: false),
                    participant = table.Column<int>(type: "int", nullable: false),
                    correction = table.Column<int>(type: "int", nullable: false),
                    protected_access = table.Column<int>(type: "int", nullable: false),
                    prohibited_inference = table.Column<int>(type: "int", nullable: false),
                    calendar = table.Column<int>(type: "int", nullable: false),
                    time_zone = table.Column<int>(type: "int", nullable: false),
                    leap_day = table.Column<int>(type: "int", nullable: false),
                    adulthood = table.Column<int>(type: "int", nullable: false),
                    adulthood_age = table.Column<int>(type: "int", nullable: false),
                    child_risk_grounds = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    privacy_legal_grounds = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    lawful_collection_purpose = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    consent_guardian_applicability_grounds = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    manual_assurance_qualification_grounds = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    provider_assurance_review_grounds = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    classification_rationale = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    calendar_jurisdiction_grounds = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reviewer_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    reviewer_controller_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    collection_owner_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    collection_controller_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    governance_controller_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    identity_and_independence_basis = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    privacy_legal_mandate_basis = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    child_safeguarding_mandate_basis = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    collection_mandate_basis = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    recorded_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
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
                        name: "fk_ie_moderation_child_age_policy_sources_ie_moderation_376aeb2f",
                        column: x => x.definition_id,
                        principalTable: "ie_moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_child_age_policy_sources_ie_users_coll_01ac8e26",
                        column: x => x.collection_owner_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_child_age_policy_sources_ie_users_coll_1a95ac03",
                        column: x => x.collection_controller_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_child_age_policy_sources_ie_users_gove_68d5fc6a",
                        column: x => x.governance_controller_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_child_age_policy_sources_ie_users_reco_e8adf8f1",
                        column: x => x.recorded_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_child_age_policy_sources_ie_users_revi_149711a1",
                        column: x => x.reviewer_controller_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_child_age_policy_sources_ie_users_revi_aba3dd3e",
                        column: x => x.reviewer_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_constitution_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    policy_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    publication_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    definition_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    profile_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    mode_code = table.Column<int>(type: "int", nullable: false),
                    proposed_state_digest = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    scope_ceiling = table.Column<int>(type: "int", nullable: false),
                    evidence = table.Column<int>(type: "int", nullable: false),
                    remedies = table.Column<int>(type: "int", nullable: false),
                    independence = table.Column<int>(type: "int", nullable: false),
                    exposure = table.Column<int>(type: "int", nullable: false),
                    overload = table.Column<int>(type: "int", nullable: false),
                    minimum_independent_reviewers = table.Column<int>(type: "int", nullable: false),
                    protected_complaint_calendar_months = table.Column<int>(type: "int", nullable: false),
                    maximum_review_interval_ticks = table.Column<long>(type: "bigint", nullable: false),
                    escalation_after_ticks = table.Column<long>(type: "bigint", nullable: false),
                    support_route = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    independent_redress_route = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    coordinator_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    fallback_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    recorded_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    original_effect_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    original_snapshot_hash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    original_effect_binding_hash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    original_custody_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true)
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
                        name: "fk_ie_moderation_constitution_sources_ie_moderation_eff_ab636a54",
                        column: x => x.original_effect_id,
                        principalTable: "ie_moderation_effect_intents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_constitution_sources_ie_moderation_pol_14718bc9",
                        column: x => x.definition_id,
                        principalTable: "ie_moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_constitution_sources_ie_users_coordina_2534bd26",
                        column: x => x.coordinator_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_constitution_sources_ie_users_recorded_d67a23d3",
                        column: x => x.recorded_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_constitution_sources_users_fallback_human_id",
                        column: x => x.fallback_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_consumer_measurement_challenges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    installation_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    installation_revision = table.Column<long>(type: "bigint", nullable: false),
                    installation_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    consumer_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    mode_code = table.Column<int>(type: "int", nullable: false),
                    runtime_generation = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    inventory_fingerprint = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    publication_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    definition_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    binding_digest = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    consumer_contract_digest = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    nonce = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    prepared_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    prepared_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    original_effect_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    original_snapshot_hash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    original_effect_binding_hash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_consumer_measurement_challenges", x => x.id);
                    table.CheckConstraint("ck_moderation_measurement_challenge_original", "(original_effect_id IS NULL AND original_snapshot_hash IS NULL AND original_effect_binding_hash IS NULL) OR (original_effect_id IS NOT NULL AND original_snapshot_hash IS NOT NULL AND original_effect_binding_hash IS NOT NULL)");
                    table.CheckConstraint("ck_moderation_measurement_challenge_owner", "consumer_id <> '' AND mode_code = 1");
                    table.CheckConstraint("ck_moderation_measurement_challenge_source", "installation_revision = 1 AND installation_writer_generation > 0 AND writer_generation > installation_writer_generation AND revision = 1");
                    table.CheckConstraint("ck_moderation_measurement_challenge_validity", "valid_until > prepared_at");
                    table.ForeignKey(
                        name: "fk_ie_moderation_consumer_measurement_challenges_ie_mod_09069a2e",
                        column: x => x.definition_id,
                        principalTable: "ie_moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_consumer_measurement_challenges_ie_mod_2518cbe2",
                        column: x => x.original_effect_id,
                        principalTable: "ie_moderation_effect_intents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_consumer_measurement_challenges_ie_mod_4368d066",
                        column: x => x.installation_id,
                        principalTable: "ie_moderation_consumer_installations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_consumer_measurement_challenges_ie_mod_4be88db3",
                        column: x => x.host_binding_id,
                        principalTable: "ie_moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_consumer_measurement_challenges_ie_ten_0fe7e1ab",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_consumer_measurement_challenges_ie_use_701c3bcd",
                        column: x => x.prepared_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_contribution_selections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    effect_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    decision_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    decision_revision = table.Column<long>(type: "bigint", nullable: false),
                    case_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    case_revision = table.Column<long>(type: "bigint", nullable: false),
                    policy_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    publication_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    profile_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    authored_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    selection_digest = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    starts_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    event_window_end = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_contribution_selections", x => x.id);
                    table.CheckConstraint("ck_moderation_contribution_selection_generations", "revision = 1 AND decision_revision > 1 AND case_revision > 0 AND policy_revision > 0 AND writer_generation > 0");
                    table.CheckConstraint("ck_moderation_contribution_selection_validity", "valid_until > recorded_at");
                    table.ForeignKey(
                        name: "fk_ie_moderation_contribution_selections_ie_moderation__5e361c9c",
                        column: x => x.case_id,
                        principalTable: "ie_moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_contribution_selections_ie_moderation__7afc8509",
                        column: x => x.publication_id,
                        principalTable: "ie_moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_contribution_selections_ie_moderation__92d2731d",
                        column: x => x.decision_id,
                        principalTable: "ie_moderation_decisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_contribution_selections_ie_moderation__f54a280f",
                        column: x => x.host_binding_id,
                        principalTable: "ie_moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_contribution_selections_ie_users_autho_6e65334c",
                        column: x => x.authored_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_contribution_selections_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_deployment_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    policy_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    publication_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    definition_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    profile_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    mode_code = table.Column<int>(type: "int", nullable: false),
                    proposed_state_digest = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    inventory_fingerprint = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    database_configuration_digest = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    database_provider_code = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    database_provider_version = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    recorded_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_deployment_sources", x => x.id);
                    table.CheckConstraint("ck_moderation_deployment_source_mode", "mode_code BETWEEN 1 AND 2");
                    table.CheckConstraint("ck_moderation_deployment_source_revision", "revision = 1 AND policy_revision > 0 AND writer_generation > 0");
                    table.CheckConstraint("ck_moderation_deployment_source_validity", "valid_until > recorded_at");
                    table.ForeignKey(
                        name: "fk_ie_moderation_deployment_sources_ie_moderation_polic_588b412e",
                        column: x => x.definition_id,
                        principalTable: "ie_moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_deployment_sources_ie_users_recorded_b_cdbb9fd2",
                        column: x => x.recorded_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_event_rights_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    policy_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    publication_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    definition_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    profile_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    mode_code = table.Column<int>(type: "int", nullable: false),
                    proposed_state_digest = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    scope_ceiling = table.Column<int>(type: "int", nullable: false),
                    continuity = table.Column<int>(type: "int", nullable: false),
                    consent = table.Column<int>(type: "int", nullable: false),
                    missing_steward = table.Column<int>(type: "int", nullable: false),
                    physical_jurisdiction = table.Column<int>(type: "int", nullable: false),
                    settlement = table.Column<int>(type: "int", nullable: false),
                    missing_settlement = table.Column<int>(type: "int", nullable: false),
                    companions = table.Column<int>(type: "int", nullable: false),
                    historical_commerce = table.Column<int>(type: "int", nullable: false),
                    correction = table.Column<int>(type: "int", nullable: false),
                    event_rights_grounds = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    continuity_grounds = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    premises_jurisdiction_grounds = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    settlement_mapping_grounds = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reviewer_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    reviewer_controller_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    continuity_mapping_owner_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    continuity_mapping_controller_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    settlement_mapping_owner_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    settlement_mapping_controller_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    governance_controller_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    controller_and_independence_grounds = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    rights_review_mandate_grounds = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    continuity_mapping_mandate_grounds = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    settlement_mapping_mandate_grounds = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    recorded_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
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
                        name: "fk_ie_moderation_event_rights_sources_ie_moderation_pol_6cc452a7",
                        column: x => x.definition_id,
                        principalTable: "ie_moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_event_rights_sources_ie_users_continui_5db254bf",
                        column: x => x.continuity_mapping_owner_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_event_rights_sources_ie_users_continui_d8840c6c",
                        column: x => x.continuity_mapping_controller_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_event_rights_sources_ie_users_governan_fbbe58ce",
                        column: x => x.governance_controller_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_event_rights_sources_ie_users_recorded_a74fa0c6",
                        column: x => x.recorded_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_event_rights_sources_ie_users_reviewer_e447690b",
                        column: x => x.reviewer_controller_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_event_rights_sources_ie_users_settleme_9fbcac59",
                        column: x => x.settlement_mapping_owner_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_event_rights_sources_ie_users_settleme_d567e3e8",
                        column: x => x.settlement_mapping_controller_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_event_rights_sources_users_reviewer_human_id",
                        column: x => x.reviewer_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_measured_operations_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    policy_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    publication_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    definition_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    profile_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    mode_code = table.Column<int>(type: "int", nullable: false),
                    binding_digest = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    proposed_state_digest = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    evidence_json = table.Column<string>(type: "longtext", maxLength: 65536, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reviewer_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    reviewer_controller_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    recorder_controller_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    recorded_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    original_effect_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    original_snapshot_hash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    original_effect_binding_hash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    original_custody_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true)
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
                        name: "fk_ie_moderation_measured_operations_sources_ie_moderat_1d686f13",
                        column: x => x.definition_id,
                        principalTable: "ie_moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_measured_operations_sources_ie_moderat_61666a35",
                        column: x => x.original_effect_id,
                        principalTable: "ie_moderation_effect_intents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_measured_operations_sources_ie_users_r_3e7e25ed",
                        column: x => x.recorder_controller_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_measured_operations_sources_ie_users_r_57378647",
                        column: x => x.reviewer_controller_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_measured_operations_sources_ie_users_r_6a5231b3",
                        column: x => x.recorded_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_measured_operations_sources_ie_users_r_f5104dcf",
                        column: x => x.reviewer_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_notice_disclosure_approvals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    effect_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    decision_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    decision_revision = table.Column<long>(type: "bigint", nullable: false),
                    case_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    case_revision = table.Column<long>(type: "bigint", nullable: false),
                    policy_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    publication_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    profile_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    contribution_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    selection_digest = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    rule_meaning = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reason_meaning = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    facts_json = table.Column<string>(type: "longtext", maxLength: 131072, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    remaining_actions = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    recourse = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    automation = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    retention = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    content_representatives_json = table.Column<string>(type: "varchar(8192)", maxLength: 8192, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    approved_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    retain_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    revoked_by_human_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    revoked_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_notice_disclosure_approvals", x => x.id);
                    table.CheckConstraint("ck_moderation_notice_disclosure_generations", "decision_revision > 1 AND case_revision > 0 AND policy_revision > 0 AND writer_generation > 0");
                    table.CheckConstraint("ck_moderation_notice_disclosure_retention", "retain_until > approved_at");
                    table.CheckConstraint("ck_moderation_notice_disclosure_revocation", "(revision = 1 AND revoked_at IS NULL AND revoked_by_human_id IS NULL) OR (revision = 2 AND revoked_at IS NOT NULL AND revoked_by_human_id IS NOT NULL AND revoked_at >= approved_at)");
                    table.CheckConstraint("ck_moderation_notice_disclosure_validity", "valid_until > approved_at");
                    table.ForeignKey(
                        name: "fk_ie_moderation_notice_disclosure_approvals_ie_moderat_47a2f22d",
                        column: x => x.case_id,
                        principalTable: "ie_moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_notice_disclosure_approvals_ie_moderat_5b318917",
                        column: x => x.host_binding_id,
                        principalTable: "ie_moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_notice_disclosure_approvals_ie_moderat_7af96056",
                        column: x => x.publication_id,
                        principalTable: "ie_moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_notice_disclosure_approvals_ie_moderat_916bdf52",
                        column: x => x.decision_id,
                        principalTable: "ie_moderation_decisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_notice_disclosure_approvals_ie_tenants_1208343d",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_notice_disclosure_approvals_ie_users_a_194b5fbf",
                        column: x => x.approved_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_notice_disclosure_approvals_ie_users_r_4773e847",
                        column: x => x.revoked_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_policy_publication_candidates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    definition_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    policy_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    expected_revision = table.Column<long>(type: "bigint", nullable: false),
                    previous_publication_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    solution_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    instance_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    jurisdiction_tenant_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    prepared_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    operation_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    request_digest = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    proposed_state_digest = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    authority_source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    authority_revision = table.Column<long>(type: "bigint", nullable: false),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    prepared_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false)
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
                        name: "fk_ie_moderation_policy_publication_candidates_ie_moder_87aaca66",
                        columns: x => new { x.policy_id, x.definition_id },
                        principalTable: "ie_moderation_policy_publications",
                        principalColumns: new[] { "policy_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_policy_publication_candidates_ie_moder_8dd5d370",
                        column: x => x.authority_source_id,
                        principalTable: "ie_moderation_qualification_epochs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_policy_publication_candidates_ie_moder_eda025c9",
                        columns: x => new { x.policy_id, x.previous_publication_id },
                        principalTable: "ie_moderation_policy_publications",
                        principalColumns: new[] { "policy_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_policy_publication_candidates_ie_moder_f0c8de88",
                        column: x => x.host_binding_id,
                        principalTable: "ie_moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_policy_publication_candidates_ie_tenan_e9ab78f3",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_policy_publication_candidates_ie_users_a16533b4",
                        column: x => x.prepared_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_policy_selections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    jurisdiction_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    jurisdiction_tenant_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    latest_publication_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    active_publication_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_policy_selections", x => x.id);
                    table.CheckConstraint("ck_moderation_selection_revision", "revision > 0");
                    table.ForeignKey(
                        name: "fk_ie_moderation_policy_selections_ie_moderation_policy_15fff1dd",
                        columns: x => new { x.id, x.latest_publication_id },
                        principalTable: "ie_moderation_policy_publications",
                        principalColumns: new[] { "policy_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_policy_selections_ie_moderation_policy_503ab5cb",
                        columns: x => new { x.id, x.active_publication_id },
                        principalTable: "ie_moderation_policy_publications",
                        principalColumns: new[] { "policy_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_publication_classes",
                columns: table => new
                {
                    publication_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    code = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    meaning = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ground = table.Column<int>(type: "int", nullable: false),
                    basis = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_publication_classes_publication_id_cod_7deec25c", x => new { x.publication_id, x.code, x.revision });
                    table.ForeignKey(
                        name: "fk_ie_moderation_publication_classes_ie_moderation_poli_18bdbd40",
                        column: x => x.publication_id,
                        principalTable: "ie_moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_publication_evidence",
                columns: table => new
                {
                    publication_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    requirement_digest = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    approval_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    approval_revision = table.Column<long>(type: "bigint", nullable: true),
                    source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_revision = table.Column<long>(type: "bigint", nullable: false),
                    writer_generation = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    observed_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_publication_evidence_publication_id_re_05c4d4dc", x => new { x.publication_id, x.requirement_digest });
                    table.CheckConstraint("ck_moderation_publication_evidence", "source_revision > 0 AND ((approval_id IS NULL AND approval_revision IS NULL) OR (approval_id IS NOT NULL AND approval_revision > 0))");
                    table.ForeignKey(
                        name: "fk_ie_moderation_publication_evidence_ie_moderation_pol_1098527c",
                        column: x => x.publication_id,
                        principalTable: "ie_moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_publication_modes",
                columns: table => new
                {
                    publication_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    mode = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_publication_modes", x => new { x.publication_id, x.mode });
                    table.CheckConstraint("ck_moderation_publication_mode", "mode BETWEEN 1 AND 2");
                    table.ForeignKey(
                        name: "fk_ie_moderation_publication_modes_ie_moderation_policy_0a69f7f3",
                        column: x => x.publication_id,
                        principalTable: "ie_moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_recognition_privacy_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    policy_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    publication_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    definition_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    profile_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    mode_code = table.Column<int>(type: "int", nullable: false),
                    proposed_state_digest = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    purposes = table.Column<int>(type: "int", nullable: false),
                    scope_ceiling = table.Column<int>(type: "int", nullable: false),
                    linkage = table.Column<int>(type: "int", nullable: false),
                    ordinary_erasure = table.Column<int>(type: "int", nullable: false),
                    correction = table.Column<int>(type: "int", nullable: false),
                    catastrophic_disposition = table.Column<int>(type: "int", nullable: false),
                    key_obligations = table.Column<int>(type: "int", nullable: false),
                    protected_remedy_calendar_months = table.Column<int>(type: "int", nullable: false),
                    escalation_after_ticks = table.Column<long>(type: "bigint", nullable: false),
                    lawful_request_deadline_ticks = table.Column<long>(type: "bigint", nullable: false),
                    maximum_backup_horizon_ticks = table.Column<long>(type: "bigint", nullable: false),
                    backup_safety_margin_ticks = table.Column<long>(type: "bigint", nullable: false),
                    privacy_owner_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    privacy_controller_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    recovery_owner_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    recovery_controller_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    governance_controller_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    identity_and_independence_basis = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    privacy_legal_mandate_basis = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    recovery_mandate_basis = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    recorded_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
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
                        name: "fk_ie_moderation_recognition_privacy_sources_ie_moderat_ca2942c8",
                        column: x => x.definition_id,
                        principalTable: "ie_moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_recognition_privacy_sources_ie_users_g_20cae8b0",
                        column: x => x.governance_controller_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_recognition_privacy_sources_ie_users_p_196f7fdf",
                        column: x => x.privacy_owner_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_recognition_privacy_sources_ie_users_p_357148db",
                        column: x => x.privacy_controller_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_recognition_privacy_sources_ie_users_r_76d0fadb",
                        column: x => x.recovery_controller_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_recognition_privacy_sources_ie_users_r_db9135d7",
                        column: x => x.recorded_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_recognition_privacy_sources_ie_users_r_ee88c3ab",
                        column: x => x.recovery_owner_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_qualification_policy_adoptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_revision = table.Column<long>(type: "bigint", nullable: false),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    case_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    case_revision = table.Column<long>(type: "bigint", nullable: false),
                    maximum_reviewer_lifetime_ticks = table.Column<long>(type: "bigint", nullable: false),
                    maximum_urgency_lifetime_ticks = table.Column<long>(type: "bigint", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    adopted_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    revoked_by_human_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    revoked_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_qualification_policy_adoptions", x => x.id);
                    table.CheckConstraint("ck_moderation_qualification_policy_adoption_generation", "id <> source_id AND source_revision > 0 AND case_revision > 0 AND writer_generation > 0");
                    table.CheckConstraint("ck_moderation_qualification_policy_adoption_limits", "maximum_reviewer_lifetime_ticks > 0 AND maximum_reviewer_lifetime_ticks < 9223372036854775807 AND maximum_urgency_lifetime_ticks > 0 AND maximum_urgency_lifetime_ticks < 9223372036854775807");
                    table.CheckConstraint("ck_moderation_qualification_policy_adoption_revocation", "(revision = 1 AND revoked_at IS NULL AND revoked_by_human_id IS NULL) OR (revision = 2 AND revoked_at IS NOT NULL AND revoked_by_human_id IS NOT NULL AND revoked_at >= adopted_at)");
                    table.CheckConstraint("ck_moderation_qualification_policy_adoption_validity", "valid_until > adopted_at");
                    table.ForeignKey(
                        name: "fk_ie_moderation_qualification_policy_adoptions_ie_mode_bad204cb",
                        columns: x => new { x.source_id, x.host_binding_id, x.trust_domain_id, x.tenant_id, x.case_id },
                        principalTable: "ie_moderation_qualification_policy_sources",
                        principalColumns: new[] { "id", "host_binding_id", "trust_domain_id", "tenant_id", "case_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_qualification_policy_adoptions_ie_user_368fdbd3",
                        column: x => x.adopted_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_qualification_policy_adoptions_ie_user_f110e2f1",
                        column: x => x.revoked_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_recognition_disposition_acceptance",
                columns: table => new
                {
                    disposition_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    role = table.Column<int>(type: "int", nullable: false),
                    human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    accepted_revision = table.Column<long>(type: "bigint", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    request_digest = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_recognition_disposition_acceptance_dis_edf0b4e5", x => new { x.disposition_id, x.role });
                    table.ForeignKey(
                        name: "fk_ie_moderation_recognition_disposition_acceptance_ie__0e018797",
                        column: x => x.disposition_id,
                        principalTable: "ie_moderation_recognition_disposition",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_recognition_key_lifecycle_key",
                columns: table => new
                {
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    key_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    key_generation = table.Column<long>(type: "bigint", nullable: false),
                    verification_tag = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    state = table.Column<int>(type: "int", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    original_obligations_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    required_erasure_head = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_recognition_key_lifecycle_key_trust_do_bfe6658a", x => new { x.trust_domain_id, x.key_id });
                    table.ForeignKey(
                        name: "fk_ie_moderation_recognition_key_lifecycle_key_ie_moder_24066efa",
                        column: x => x.trust_domain_id,
                        principalTable: "ie_moderation_recognition_key_lifecycle",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_case_narratives",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    case_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    reporter_user_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    receipt_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_version = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    material_fact_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    policy_source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    adoption_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    policy_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    adoption_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    qualification_epoch_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    qualification_revision = table.Column<long>(type: "bigint", nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    purge_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    protected_material = table.Column<byte[]>(type: "longblob", maxLength: 131072, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_case_narratives", x => x.id);
                    table.CheckConstraint("ck_case_narrative_finite_custody", "purge_at > captured_at");
                    table.CheckConstraint("ck_case_narrative_generations", "policy_writer_generation > 0 AND adoption_writer_generation > policy_writer_generation AND qualification_revision >= adoption_writer_generation");
                    table.ForeignKey(
                        name: "fk_ie_moderation_case_narratives_ie_moderation_case_nar_7c061ffc",
                        columns: x => new { x.adoption_id, x.host_binding_id, x.trust_domain_id, x.tenant_id, x.policy_source_id },
                        principalTable: "ie_moderation_case_narrative_adoptions",
                        principalColumns: new[] { "id", "host_binding_id", "trust_domain_id", "tenant_id", "source_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_case_narratives_ie_moderation_case_tar_594372aa",
                        column: x => x.source_id,
                        principalTable: "ie_moderation_case_target_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_case_narratives_ie_moderation_case_tar_d3f75d78",
                        column: x => x.receipt_id,
                        principalTable: "ie_moderation_case_target_receipts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_case_narratives_ie_moderation_qualific_6d997f3f",
                        column: x => x.qualification_epoch_id,
                        principalTable: "ie_moderation_qualification_epochs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_narratives_moderation_cases_case_id",
                        column: x => x.case_id,
                        principalTable: "ie_moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_narratives_users_reporter_user_id",
                        column: x => x.reporter_user_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_case_attribution_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    case_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    generation = table.Column<long>(type: "bigint", nullable: false),
                    occurrence_case_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    occurrence_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    occurrence_case_revision = table.Column<long>(type: "bigint", nullable: false),
                    evidence_case_revision = table.Column<long>(type: "bigint", nullable: false),
                    evidence_source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    evidence_source_version = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    material_fact_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    investigation_source_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    investigation_source_revision = table.Column<long>(type: "bigint", nullable: true),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    qualification_epoch_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    qualification_revision = table.Column<long>(type: "bigint", nullable: false),
                    basis = table.Column<int>(type: "int", nullable: false),
                    subject_kind = table.Column<int>(type: "int", nullable: false),
                    subject_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    qualified_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    qualifier_role_grant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    qualifier_platform_grant = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    read_grant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    read_grant_revision = table.Column<long>(type: "bigint", nullable: false),
                    lifetime_policy_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    lifetime_policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    native_responsibility_digest = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    qualification_facts_digest = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    qualified_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    withdrawn_by_human_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    withdrawal_operation_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_case_attribution_sources", x => x.id);
                    table.CheckConstraint("ck_moderation_attribution_basis", "(investigation_source_id IS NULL AND investigation_source_revision IS NULL AND ((basis = 1 AND subject_kind = 1 AND subject_id <> qualified_by_human_id) OR (basis = 2 AND subject_kind = 4) OR (basis = 3 AND subject_kind = 3) OR (basis = 4 AND subject_kind = 2))) OR (basis = 5 AND subject_kind = 1 AND subject_id <> qualified_by_human_id AND investigation_source_id IS NOT NULL AND investigation_source_revision IS NOT NULL AND investigation_source_revision = 1)");
                    table.CheckConstraint("ck_moderation_attribution_evidence", "generation > 0 AND case_id <> occurrence_case_id AND evidence_case_revision > 0 AND occurrence_case_revision >= evidence_case_revision AND qualification_revision > 0 AND read_grant_revision > 0 AND lifetime_policy_revision > 0");
                    table.CheckConstraint("ck_moderation_attribution_validity", "valid_until > qualified_at AND ((revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL AND withdrawal_operation_id IS NULL) OR (revision = 2 AND withdrawn_at >= qualified_at AND withdrawn_by_human_id IS NOT NULL AND withdrawal_operation_id IS NOT NULL))");
                    table.ForeignKey(
                        name: "fk_ie_moderation_case_attribution_sources_ie_moderation_6c73b7c1",
                        column: x => x.read_grant_id,
                        principalTable: "ie_moderation_case_read_grants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_case_attribution_sources_ie_moderation_8863cc7a",
                        column: x => x.case_id,
                        principalTable: "ie_moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_case_attribution_sources_ie_moderation_a3053fc7",
                        column: x => x.occurrence_case_id,
                        principalTable: "ie_moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_case_attribution_sources_ie_moderation_e87a7074",
                        column: x => x.evidence_source_id,
                        principalTable: "ie_moderation_case_target_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_case_attribution_sources_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_case_read_grant_revocation_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    grant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    expected_revision = table.Column<long>(type: "bigint", nullable: false),
                    administrator_user_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    administrator_role_grant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    administrator_platform_grant = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_case_read_grant_revocation_receipts", x => x.id);
                    table.CheckConstraint("ck_moderation_read_grant_revocation_revision", "expected_revision = 1");
                    table.ForeignKey(
                        name: "fk_ie_moderation_case_read_grant_revocation_receipts_ie_578d160f",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_case_read_grant_revocation_receipts_ie_abd1a88e",
                        column: x => x.administrator_user_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_case_read_grant_revocation_receipts_ie_d05e70f2",
                        column: x => x.grant_id,
                        principalTable: "ie_moderation_case_read_grants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_content_responses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    notice_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    effect_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    case_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    content_subject_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    representative_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    assignment_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    assignment_revision = table.Column<long>(type: "bigint", nullable: false),
                    snapshot_hash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    request_digest = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    submitted_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    retain_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    protected_statement = table.Column<byte[]>(type: "longblob", maxLength: 16384, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_content_responses", x => x.id);
                    table.CheckConstraint("ck_content_response_assignment", "assignment_revision > 0");
                    table.CheckConstraint("ck_content_response_finite_purpose", "retain_until > submitted_at");
                    table.ForeignKey(
                        name: "fk_ie_moderation_content_responses_ie_moderation_notice_1341aeeb",
                        column: x => x.notice_id,
                        principalTable: "ie_moderation_notices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_content_responses_ie_moderation_notice_a9d00c52",
                        column: x => x.effect_id,
                        principalTable: "ie_moderation_notice_sources",
                        principalColumn: "effect_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_content_responses_ie_users_representat_d138b28e",
                        column: x => x.representative_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_content_responses_moderation_cases_case_id",
                        column: x => x.case_id,
                        principalTable: "ie_moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_notice_notification_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    notice_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    effect_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    subject_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    subject_kind = table.Column<int>(type: "int", nullable: false),
                    acknowledged_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    snapshot_hash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    contestation_calendar_months = table.Column<int>(type: "int", nullable: false),
                    notified_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    remedy_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    retain_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_notice_notification_receipts", x => x.id);
                    table.CheckConstraint("ck_moderation_notice_notification_calendar", "contestation_calendar_months >= 0 AND contestation_calendar_months <= 120");
                    table.CheckConstraint("ck_moderation_notice_notification_subject", "subject_kind IN (1, 4) AND (subject_kind <> 1 OR acknowledged_by_human_id = subject_id)");
                    table.CheckConstraint("ck_moderation_notice_notification_time", "remedy_until >= notified_at AND retain_until >= remedy_until AND retain_until > notified_at");
                    table.ForeignKey(
                        name: "fk_ie_moderation_notice_notification_receipts_ie_modera_558fe553",
                        column: x => x.effect_id,
                        principalTable: "ie_moderation_notice_sources",
                        principalColumn: "effect_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_notice_notification_receipts_ie_modera_5740c51c",
                        column: x => x.notice_id,
                        principalTable: "ie_moderation_notices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_notice_notification_receipts_ie_tenant_157a8cfe",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_case_intake_acknowledgements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    caller_user_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    case_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    case_revision = table.Column<long>(type: "bigint", nullable: false),
                    receipt_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    receipt_revision = table.Column<long>(type: "bigint", nullable: false),
                    operation_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    evidence_case_revision = table.Column<long>(type: "bigint", nullable: false),
                    budget_source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    budget_adoption_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    budget_source_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    budget_adoption_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    request_digest = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    accepted_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_case_intake_acknowledgements", x => x.id);
                    table.CheckConstraint("ck_moderation_case_acknowledgement_revisions", "case_revision > 0 AND receipt_revision = 2 AND evidence_case_revision > 0 AND evidence_case_revision <= case_revision AND budget_source_writer_generation > 0 AND budget_adoption_writer_generation > budget_source_writer_generation AND budget_adoption_writer_generation < 9223372036854775807 AND budget_source_id <> budget_adoption_id");
                    table.ForeignKey(
                        name: "fk_ie_moderation_case_intake_acknowledgements_ie_modera_10c2d3e8",
                        columns: x => new { x.budget_adoption_id, x.trust_domain_id, x.tenant_id, x.budget_source_id },
                        principalTable: "ie_moderation_operational_budget_adoptions",
                        principalColumns: new[] { "id", "trust_domain_id", "tenant_id", "source_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_case_intake_acknowledgements_ie_modera_1a54c752",
                        column: x => x.case_id,
                        principalTable: "ie_moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_case_intake_acknowledgements_ie_modera_380c2e38",
                        column: x => x.receipt_id,
                        principalTable: "ie_moderation_case_target_receipts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_case_intake_acknowledgements_ie_tenant_c155ef8e",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_child_age_policy_adoptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_revision = table.Column<long>(type: "bigint", nullable: false),
                    source_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    adopted_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    withdrawn_by_human_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_child_age_policy_adoptions", x => x.id);
                    table.CheckConstraint("ck_moderation_child_age_adoption_generation", "source_revision = 1 AND source_writer_generation > 0 AND writer_generation > source_writer_generation");
                    table.CheckConstraint("ck_moderation_child_age_adoption_validity", "valid_until > adopted_at");
                    table.CheckConstraint("ck_moderation_child_age_adoption_withdrawal", "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR (revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at >= adopted_at)");
                    table.ForeignKey(
                        name: "fk_ie_moderation_child_age_policy_adoptions_ie_moderati_2fc280da",
                        column: x => x.source_id,
                        principalTable: "ie_moderation_child_age_policy_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_child_age_policy_adoptions_ie_users_ad_9a15ccdd",
                        column: x => x.adopted_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_child_age_policy_adoptions_ie_users_wi_64d31351",
                        column: x => x.withdrawn_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_constitution_adoptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_revision = table.Column<long>(type: "bigint", nullable: false),
                    source_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    adopted_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    withdrawn_by_human_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_constitution_adoptions", x => x.id);
                    table.CheckConstraint("ck_moderation_constitution_adoption_generation", "source_revision = 1 AND source_writer_generation > 0 AND writer_generation > source_writer_generation");
                    table.CheckConstraint("ck_moderation_constitution_adoption_validity", "valid_until > adopted_at");
                    table.CheckConstraint("ck_moderation_constitution_adoption_withdrawal", "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR (revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at >= adopted_at)");
                    table.ForeignKey(
                        name: "fk_ie_moderation_constitution_adoptions_ie_moderation_c_35a0baaf",
                        column: x => x.source_id,
                        principalTable: "ie_moderation_constitution_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_constitution_adoptions_ie_users_adopte_06db63c2",
                        column: x => x.adopted_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_constitution_adoptions_ie_users_withdr_a008090e",
                        column: x => x.withdrawn_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_constitution_reviewers",
                columns: table => new
                {
                    source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ordinal = table.Column<int>(type: "int", nullable: false),
                    availability_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    availability_revision = table.Column<long>(type: "bigint", nullable: false),
                    availability_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    reviewer_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    controller_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    identity_basis = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    qualification_basis = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    independence_basis = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_constitution_reviewers", x => new { x.source_id, x.ordinal });
                    table.CheckConstraint("ck_moderation_constitution_reviewer_generation", "ordinal >= 0 AND ordinal < 32 AND availability_revision = 1 AND availability_writer_generation > 0");
                    table.ForeignKey(
                        name: "fk_ie_moderation_constitution_reviewers_ie_moderation_c_1f0cd284",
                        column: x => x.source_id,
                        principalTable: "ie_moderation_constitution_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_constitution_reviewers_ie_moderation_c_9f9ad5fe",
                        column: x => x.availability_id,
                        principalTable: "ie_moderation_constitution_availability",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_constitution_reviewers_ie_users_contro_01d55db4",
                        column: x => x.controller_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_constitution_reviewers_ie_users_review_30d8f83c",
                        column: x => x.reviewer_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_consumer_measurement_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    challenge_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    issuer_enrollment_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    issuer_key_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    run_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    signed_token = table.Column<string>(type: "longtext", maxLength: 65536, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    token_digest = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    accepted_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_consumer_measurement_receipts", x => x.id);
                    table.CheckConstraint("ck_moderation_measurement_receipt_generation", "writer_generation > 0 AND writer_generation < 9223372036854775807 AND revision = 1");
                    table.CheckConstraint("ck_moderation_measurement_receipt_validity", "valid_until > accepted_at");
                    table.ForeignKey(
                        name: "fk_ie_moderation_consumer_measurement_receipts_ie_moder_66e16fc8",
                        column: x => x.issuer_enrollment_id,
                        principalTable: "ie_moderation_measurement_issuer_enrollments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_consumer_measurement_receipts_ie_moder_83bcc1d8",
                        column: x => x.host_binding_id,
                        principalTable: "ie_moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_consumer_measurement_receipts_ie_moder_88f5ecc0",
                        column: x => x.challenge_id,
                        principalTable: "ie_moderation_consumer_measurement_challenges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_consumer_measurement_receipts_ie_tenan_5ed06922",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_consumer_measurement_receipts_ie_users_67cda3e0",
                        column: x => x.accepted_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_contribution_bindings",
                columns: table => new
                {
                    selection_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ordinal = table.Column<int>(type: "int", nullable: false),
                    kind = table.Column<int>(type: "int", nullable: false),
                    effect_kind = table.Column<int>(type: "int", nullable: true),
                    effect_mode = table.Column<int>(type: "int", nullable: true),
                    effect_target = table.Column<int>(type: "int", nullable: true),
                    capability = table.Column<int>(type: "int", nullable: true),
                    execution = table.Column<int>(type: "int", nullable: true),
                    strategy = table.Column<int>(type: "int", nullable: true),
                    scope_kind = table.Column<int>(type: "int", nullable: false),
                    scope_trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    scope_solution_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    scope_instance_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    scope_tenant_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    scope_community_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    scope_resource_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    scope_event_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    subject_kind = table.Column<int>(type: "int", nullable: false),
                    subject_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    target_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    target_revision = table.Column<long>(type: "bigint", nullable: false),
                    operation = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    window_end = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_contribution_bindings", x => new { x.selection_id, x.ordinal });
                    table.CheckConstraint("ck_moderation_contribution_binding_meaning", "(kind = 1 AND effect_kind IS NOT NULL AND effect_mode IS NOT NULL AND effect_target IS NOT NULL AND execution IS NOT NULL AND strategy IS NULL) OR (kind = 2 AND effect_kind IS NULL AND effect_mode IS NULL AND effect_target IS NULL AND capability IS NULL AND execution IS NULL)");
                    table.CheckConstraint("ck_moderation_contribution_binding_partition", "ordinal >= 0 AND ordinal < 64 AND target_revision > 0");
                    table.CheckConstraint("ck_moderation_contribution_bindings_scope_scope", "scope_kind IS NOT NULL AND ((scope_kind = 1 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 2 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 3 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 4 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 5 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NOT NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 6 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_resource_id IS NOT NULL AND scope_event_id IS NULL) OR (scope_kind = 7 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NOT NULL AND scope_event_id <> '00000000-0000-0000-0000-000000000000'))");
                    table.ForeignKey(
                        name: "fk_ie_moderation_contribution_bindings_ie_moderation_co_86e04a60",
                        column: x => x.selection_id,
                        principalTable: "ie_moderation_contribution_selections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_profile_adoptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    selection_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    selection_revision = table.Column<long>(type: "bigint", nullable: false),
                    selection_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    publication_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    profile_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    case_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    case_revision = table.Column<long>(type: "bigint", nullable: false),
                    mode_code = table.Column<int>(type: "int", nullable: false),
                    prerequisite_mask = table.Column<int>(type: "int", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    adopted_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    revoked_by_human_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    revoked_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_profile_adoptions", x => x.id);
                    table.CheckConstraint("ck_moderation_profile_adoption_mode", "mode_code BETWEEN 1 AND 2");
                    table.CheckConstraint("ck_moderation_profile_adoption_revision", "selection_revision = 1 AND selection_writer_generation > 0 AND case_revision > 0 AND writer_generation > 0");
                    table.CheckConstraint("ck_moderation_profile_adoption_revocation", "(revision = 1 AND revoked_at IS NULL AND revoked_by_human_id IS NULL) OR (revision = 2 AND revoked_at IS NOT NULL AND revoked_by_human_id IS NOT NULL AND revoked_at >= adopted_at)");
                    table.CheckConstraint("ck_moderation_profile_adoption_validity", "valid_until > adopted_at");
                    table.ForeignKey(
                        name: "fk_ie_moderation_profile_adoptions_ie_moderation_contri_0e7e9052",
                        column: x => x.selection_id,
                        principalTable: "ie_moderation_contribution_selections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_profile_adoptions_ie_moderation_policy_91354b79",
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_deployment_adoptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_revision = table.Column<long>(type: "bigint", nullable: false),
                    source_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    adopted_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    withdrawn_by_human_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_deployment_adoptions", x => x.id);
                    table.CheckConstraint("ck_moderation_deployment_adoption_generation", "source_revision = 1 AND source_writer_generation > 0 AND writer_generation > source_writer_generation");
                    table.CheckConstraint("ck_moderation_deployment_adoption_validity", "valid_until > adopted_at");
                    table.CheckConstraint("ck_moderation_deployment_adoption_withdrawal", "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR (revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at >= adopted_at)");
                    table.ForeignKey(
                        name: "fk_ie_moderation_deployment_adoptions_ie_moderation_dep_f58f2fe4",
                        column: x => x.source_id,
                        principalTable: "ie_moderation_deployment_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_deployment_adoptions_ie_users_adopted__b54f63ff",
                        column: x => x.adopted_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_deployment_adoptions_ie_users_withdraw_5d7463f9",
                        column: x => x.withdrawn_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_deployment_consumers",
                columns: table => new
                {
                    source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ordinal = table.Column<int>(type: "int", nullable: false),
                    consumer_id = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    binding_digest = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    contract_digest = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    installation_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    installation_revision = table.Column<long>(type: "bigint", nullable: false),
                    installation_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    runtime_generation = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    product_artifact_digest = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_deployment_consumers", x => new { x.source_id, x.ordinal });
                    table.CheckConstraint("ck_moderation_deployment_consumer_revision", "ordinal >= 0 AND ordinal < 256 AND installation_revision = 1 AND installation_writer_generation > 0");
                    table.ForeignKey(
                        name: "fk_ie_moderation_deployment_consumers_ie_moderation_con_a821e6e5",
                        column: x => x.installation_id,
                        principalTable: "ie_moderation_consumer_installations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_deployment_consumers_ie_moderation_dep_c8e14e4e",
                        column: x => x.source_id,
                        principalTable: "ie_moderation_deployment_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_event_rights_adoptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_revision = table.Column<long>(type: "bigint", nullable: false),
                    source_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    adopted_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    withdrawn_by_human_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_event_rights_adoptions", x => x.id);
                    table.CheckConstraint("ck_moderation_event_rights_adoption_generation", "source_revision = 1 AND source_writer_generation > 0 AND writer_generation > source_writer_generation");
                    table.CheckConstraint("ck_moderation_event_rights_adoption_validity", "valid_until > adopted_at");
                    table.CheckConstraint("ck_moderation_event_rights_adoption_withdrawal", "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR (revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at >= adopted_at)");
                    table.ForeignKey(
                        name: "fk_ie_moderation_event_rights_adoptions_ie_moderation_e_21c12616",
                        column: x => x.source_id,
                        principalTable: "ie_moderation_event_rights_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_event_rights_adoptions_ie_users_adopte_56beba15",
                        column: x => x.adopted_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_event_rights_adoptions_ie_users_withdr_bb8ae361",
                        column: x => x.withdrawn_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_measured_operations_acceptances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_revision = table.Column<long>(type: "bigint", nullable: false),
                    source_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    accepted_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    withdrawn_by_human_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_measured_operations_acceptances", x => x.id);
                    table.CheckConstraint("ck_moderation_measured_acceptance_generation", "source_revision = 1 AND source_writer_generation > 0 AND writer_generation > source_writer_generation");
                    table.CheckConstraint("ck_moderation_measured_acceptance_validity", "valid_until > accepted_at");
                    table.CheckConstraint("ck_moderation_measured_acceptance_withdrawal", "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR (revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at >= accepted_at)");
                    table.ForeignKey(
                        name: "fk_ie_moderation_measured_operations_acceptances_ie_mod_74bf76f6",
                        column: x => x.source_id,
                        principalTable: "ie_moderation_measured_operations_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_measured_operations_acceptances_ie_use_c4560b17",
                        column: x => x.accepted_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_measured_operations_acceptances_ie_use_c68e2a08",
                        column: x => x.withdrawn_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_publication_reasons",
                columns: table => new
                {
                    publication_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    code = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    meaning = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    class_code = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    class_revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_publication_reasons_publication_id_cod_c814f269", x => new { x.publication_id, x.code, x.revision });
                    table.ForeignKey(
                        name: "fk_ie_moderation_publication_reasons_ie_moderation_poli_f7d3d31f",
                        column: x => x.publication_id,
                        principalTable: "ie_moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_publication_reasons_ie_moderation_publ_dbe7ce6c",
                        columns: x => new { x.publication_id, x.class_code, x.class_revision },
                        principalTable: "ie_moderation_publication_classes",
                        principalColumns: new[] { "publication_id", "code", "revision" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_recognition_privacy_adoptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_revision = table.Column<long>(type: "bigint", nullable: false),
                    source_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    adopted_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    withdrawn_by_human_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_recognition_privacy_adoptions", x => x.id);
                    table.CheckConstraint("ck_moderation_recognition_privacy_adoption_generation", "source_revision = 1 AND source_writer_generation > 0 AND writer_generation > source_writer_generation");
                    table.CheckConstraint("ck_moderation_recognition_privacy_adoption_validity", "valid_until > adopted_at");
                    table.CheckConstraint("ck_moderation_recognition_privacy_adoption_withdrawal", "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR (revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at >= adopted_at)");
                    table.ForeignKey(
                        name: "fk_ie_moderation_recognition_privacy_adoptions_ie_moder_639da685",
                        column: x => x.source_id,
                        principalTable: "ie_moderation_recognition_privacy_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_recognition_privacy_adoptions_ie_users_1131ddd7",
                        column: x => x.withdrawn_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_recognition_privacy_adoptions_ie_users_cdb09a51",
                        column: x => x.adopted_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_recognition_privacy_retention",
                columns: table => new
                {
                    source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    record_class = table.Column<int>(type: "int", nullable: false),
                    maximum_retention_ticks = table.Column<long>(type: "bigint", nullable: false),
                    hold = table.Column<int>(type: "int", nullable: false),
                    maximum_hold_ticks = table.Column<long>(type: "bigint", nullable: false),
                    hold_review_within_ticks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_recognition_privacy_retention_source_i_eca85db6", x => new { x.source_id, x.record_class });
                    table.CheckConstraint("ck_moderation_recognition_privacy_hold", "(hold = 1 AND maximum_hold_ticks = 0 AND hold_review_within_ticks = 0) OR (hold = 2 AND record_class IN (3, 4) AND maximum_hold_ticks > 0 AND hold_review_within_ticks > 0 AND hold_review_within_ticks <= maximum_hold_ticks)");
                    table.CheckConstraint("ck_moderation_recognition_privacy_retention", "record_class BETWEEN 1 AND 4 AND maximum_retention_ticks > 0");
                    table.ForeignKey(
                        name: "fk_ie_moderation_recognition_privacy_retention_ie_moder_a9382295",
                        column: x => x.source_id,
                        principalTable: "ie_moderation_recognition_privacy_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_participant_incident_responsibility_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    occurrence_case_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    occurrence_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    occurrence_case_revision = table.Column<long>(type: "bigint", nullable: false),
                    evidence_case_revision = table.Column<long>(type: "bigint", nullable: false),
                    evidence_source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    evidence_source_version = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    material_fact_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    original_narrative_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    original_narrative_purge_at = table.Column<long>(type: "bigint", nullable: false),
                    event_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    event_version = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    session_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    session_generation = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    incident_window_from = table.Column<long>(type: "bigint", nullable: false),
                    incident_window_until = table.Column<long>(type: "bigint", nullable: false),
                    order_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    order_generation = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    participant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    participant_generation = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    subject_kind = table.Column<int>(type: "int", nullable: false),
                    subject_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    assessor_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    assessor_role_grant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    read_issuer_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    read_grant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    read_grant_revision = table.Column<long>(type: "bigint", nullable: false),
                    read_grant_expires_at = table.Column<long>(type: "bigint", nullable: false),
                    qualification_policy_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    qualification_policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    maximum_qualification_lifetime_ticks = table.Column<long>(type: "bigint", nullable: false),
                    target_kind = table.Column<int>(type: "int", nullable: false),
                    purpose = table.Column<int>(type: "int", nullable: false),
                    custody_source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    custody_source_revision = table.Column<long>(type: "bigint", nullable: false),
                    custody_source_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    custody_source_valid_until = table.Column<long>(type: "bigint", nullable: false),
                    maximum_retention_ticks = table.Column<long>(type: "bigint", nullable: false),
                    custody_adoption_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    custody_adoption_revision = table.Column<long>(type: "bigint", nullable: false),
                    custody_adoption_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    custody_adoption_valid_until = table.Column<long>(type: "bigint", nullable: false),
                    qualified_at = table.Column<long>(type: "bigint", nullable: false),
                    valid_until = table.Column<long>(type: "bigint", nullable: false),
                    purge_at = table.Column<long>(type: "bigint", nullable: false),
                    assessment_digest = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    withdrawal_operation_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    withdrawn_by_human_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    withdrawn_at = table.Column<long>(type: "bigint", nullable: true),
                    protected_assessment = table.Column<byte[]>(type: "longblob", maxLength: 131072, nullable: false)
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
                        name: "fk_ie_moderation_participant_incident_responsibility_so_01ba2bae",
                        column: x => x.occurrence_case_id,
                        principalTable: "ie_moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_participant_incident_responsibility_so_0fab19f6",
                        column: x => x.assessor_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_participant_incident_responsibility_so_2573d321",
                        column: x => x.original_narrative_id,
                        principalTable: "ie_moderation_case_narratives",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_participant_incident_responsibility_so_26e4255e",
                        column: x => x.evidence_source_id,
                        principalTable: "ie_moderation_case_target_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_participant_incident_responsibility_so_2a811c95",
                        column: x => x.read_issuer_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_participant_incident_responsibility_so_4c38b5b3",
                        column: x => x.participant_id,
                        principalTable: "ie_registration_participants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_participant_incident_responsibility_so_4f4c977d",
                        columns: x => new { x.custody_source_id, x.host_binding_id, x.trust_domain_id, x.tenant_id },
                        principalTable: "ie_moderation_case_narrative_policy_sources",
                        principalColumns: new[] { "id", "host_binding_id", "trust_domain_id", "tenant_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_participant_incident_responsibility_so_64079739",
                        column: x => x.qualification_policy_id,
                        principalTable: "ie_moderation_case_target_policies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_participant_incident_responsibility_so_6b4f9039",
                        column: x => x.withdrawn_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_participant_incident_responsibility_so_6d20e326",
                        column: x => x.host_binding_id,
                        principalTable: "ie_moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_participant_incident_responsibility_so_71bcafa1",
                        column: x => x.event_id,
                        principalTable: "ie_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_participant_incident_responsibility_so_89da40bc",
                        column: x => x.subject_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_participant_incident_responsibility_so_b3cff0e7",
                        columns: x => new { x.custody_adoption_id, x.host_binding_id, x.trust_domain_id, x.tenant_id, x.custody_source_id },
                        principalTable: "ie_moderation_case_narrative_adoptions",
                        principalColumns: new[] { "id", "host_binding_id", "trust_domain_id", "tenant_id", "source_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_participant_incident_responsibility_so_d183e1fe",
                        column: x => x.read_grant_id,
                        principalTable: "ie_moderation_case_read_grants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_participant_incident_responsibility_so_e6a99959",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_participant_incident_responsibility_so_e971d488",
                        column: x => x.order_id,
                        principalTable: "ie_registration_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_participant_incident_responsibility_so_e98ae65b",
                        column: x => x.session_id,
                        principalTable: "ie_event_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_case_attribution_heads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    case_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    solution_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    instance_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    occurrence_case_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    occurrence_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    occurrence_evidence_revision = table.Column<long>(type: "bigint", nullable: false),
                    subject_kind = table.Column<int>(type: "int", nullable: false),
                    subject_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    opening_source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_case_attribution_heads", x => x.id);
                    table.CheckConstraint("ck_moderation_attribution_head_generation", "generation > 0 AND revision = generation AND ((generation = 1 AND source_id = opening_source_id) OR (generation > 1 AND source_id <> opening_source_id))");
                    table.CheckConstraint("ck_moderation_attribution_head_scope", "case_id <> occurrence_case_id AND occurrence_evidence_revision > 0 AND subject_kind IN (1, 2, 3, 4)");
                    table.ForeignKey(
                        name: "fk_ie_moderation_case_attribution_heads_ie_moderation_c_4bda3cc5",
                        column: x => x.source_id,
                        principalTable: "ie_moderation_case_attribution_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_case_attribution_heads_ie_moderation_c_8fab92de",
                        column: x => x.occurrence_case_id,
                        principalTable: "ie_moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_case_attribution_heads_ie_moderation_c_c94cde3c",
                        column: x => x.case_id,
                        principalTable: "ie_moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_case_attribution_heads_ie_moderation_c_ebc2d918",
                        column: x => x.opening_source_id,
                        principalTable: "ie_moderation_case_attribution_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_case_attribution_heads_ie_moderation_h_efea2fbe",
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_pre_finding_custody",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    case_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    case_revision = table.Column<long>(type: "bigint", nullable: false),
                    attribution_source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    attribution_generation = table.Column<long>(type: "bigint", nullable: false),
                    evidence_revision = table.Column<long>(type: "bigint", nullable: false),
                    proposed_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    privacy_owner_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    maximum_retention_ticks = table.Column<long>(type: "bigint", nullable: false),
                    maximum_text_bytes = table.Column<int>(type: "int", nullable: false),
                    purpose = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    privacy_legal_mandate = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    proposed_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_pre_finding_custody", x => x.id);
                    table.UniqueConstraint("ak_ie_moderation_pre_finding_custody_id_host_binding_id_2ea07f11", x => new { x.id, x.host_binding_id, x.trust_domain_id, x.tenant_id, x.case_id });
                    table.CheckConstraint("ck_pre_finding_custody_attribution", "attribution_generation > 0 AND evidence_revision > 0");
                    table.CheckConstraint("ck_pre_finding_custody_independence", "proposed_by_human_id <> privacy_owner_human_id");
                    table.CheckConstraint("ck_pre_finding_custody_limits", "maximum_retention_ticks > 0 AND maximum_retention_ticks < 9223372036854775807 AND maximum_text_bytes > 0 AND maximum_text_bytes <= 65536 AND valid_until > proposed_at");
                    table.ForeignKey(
                        name: "fk_ie_moderation_pre_finding_custody_ie_moderation_case_5257d760",
                        column: x => x.attribution_source_id,
                        principalTable: "ie_moderation_case_attribution_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_pre_finding_custody_ie_users_privacy_o_7e63561c",
                        column: x => x.privacy_owner_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_pre_finding_custody_ie_users_proposed__4c5135c5",
                        column: x => x.proposed_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_pre_finding_custody_moderation_cases_case_id",
                        column: x => x.case_id,
                        principalTable: "ie_moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_profile_collection_procedure_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    policy_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    publication_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    definition_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    profile_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    mode_code = table.Column<int>(type: "int", nullable: false),
                    proposed_state_digest = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    child_age_source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    child_age_source_revision = table.Column<long>(type: "bigint", nullable: false),
                    child_age_source_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    child_age_recorded_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    child_age_recorded_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    child_age_valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    child_age_terms = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    child_age_ownership = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    child_age_adoption_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    child_age_adoption_revision = table.Column<long>(type: "bigint", nullable: false),
                    child_age_adoption_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    child_age_adopted_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    child_age_adoption_valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    procedure = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    recorded_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_profile_collection_procedure_sources", x => x.id);
                    table.CheckConstraint("ck_profile_collection_procedure_generation", "revision = 1 AND policy_revision > 0 AND child_age_source_revision = 1 AND child_age_source_writer_generation > 0 AND child_age_adoption_revision = 1 AND child_age_adoption_writer_generation > child_age_source_writer_generation AND writer_generation > child_age_adoption_writer_generation");
                    table.CheckConstraint("ck_profile_collection_procedure_mode", "mode_code BETWEEN 1 AND 2");
                    table.CheckConstraint("ck_profile_collection_procedure_validity", "valid_until > recorded_at AND recorded_at >= child_age_recorded_at AND recorded_at >= child_age_adopted_at AND valid_until <= child_age_valid_until AND valid_until <= child_age_adoption_valid_until");
                    table.ForeignKey(
                        name: "fk_ie_moderation_profile_collection_procedure_sources_i_55327c7f",
                        column: x => x.recorded_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_profile_collection_procedure_sources_i_69654e78",
                        column: x => x.child_age_source_id,
                        principalTable: "ie_moderation_child_age_policy_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_profile_collection_procedure_sources_i_8b632324",
                        column: x => x.child_age_adoption_id,
                        principalTable: "ie_moderation_child_age_policy_adoptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_classification_taxonomy_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    taxonomy_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    taxonomy_revision = table.Column<long>(type: "bigint", nullable: false),
                    previous_generation = table.Column<long>(type: "bigint", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    policy_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    publication_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    definition_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    profile_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    mode_code = table.Column<int>(type: "int", nullable: false),
                    proposed_state_digest = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    child_age_source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    child_age_source_revision = table.Column<long>(type: "bigint", nullable: false),
                    child_age_source_generation = table.Column<long>(type: "bigint", nullable: false),
                    child_age_adoption_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    child_age_adoption_revision = table.Column<long>(type: "bigint", nullable: false),
                    child_age_adoption_generation = table.Column<long>(type: "bigint", nullable: false),
                    adopted_debate_minimum_age = table.Column<int>(type: "int", nullable: false),
                    constitution_source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    constitution_source_revision = table.Column<long>(type: "bigint", nullable: false),
                    constitution_source_generation = table.Column<long>(type: "bigint", nullable: false),
                    constitution_adoption_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    constitution_adoption_revision = table.Column<long>(type: "bigint", nullable: false),
                    constitution_adoption_generation = table.Column<long>(type: "bigint", nullable: false),
                    owner_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    owner_controller_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    reviewer_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    reviewer_controller_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ownership_grounds = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    review_mandate_grounds = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_classification_taxonomy_sources", x => x.id);
                    table.CheckConstraint("ck_moderation_taxonomy_generation", "revision = 1 AND taxonomy_revision > 0 AND previous_generation >= 0 AND writer_generation > previous_generation AND child_age_source_revision = 1 AND child_age_adoption_revision = 1 AND constitution_source_revision = 1 AND constitution_adoption_revision = 1 AND child_age_source_generation > 0 AND child_age_adoption_generation > child_age_source_generation AND constitution_source_generation > 0 AND constitution_adoption_generation > constitution_source_generation AND writer_generation > child_age_adoption_generation AND writer_generation > constitution_adoption_generation");
                    table.CheckConstraint("ck_moderation_taxonomy_independence", "owner_human_id <> reviewer_human_id AND owner_controller_human_id <> reviewer_controller_human_id AND owner_human_id <> reviewer_controller_human_id AND reviewer_human_id <> owner_controller_human_id");
                    table.CheckConstraint("ck_moderation_taxonomy_mode", "mode_code BETWEEN 1 AND 2 AND policy_revision > 0");
                    table.CheckConstraint("ck_moderation_taxonomy_validity", "valid_until > recorded_at");
                    table.ForeignKey(
                        name: "fk_ie_moderation_classification_taxonomy_sources_ie_mod_177b171c",
                        column: x => x.definition_id,
                        principalTable: "ie_moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_classification_taxonomy_sources_ie_mod_4321fe1c",
                        column: x => x.child_age_adoption_id,
                        principalTable: "ie_moderation_child_age_policy_adoptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_classification_taxonomy_sources_ie_mod_48bd16a3",
                        column: x => x.constitution_source_id,
                        principalTable: "ie_moderation_constitution_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_classification_taxonomy_sources_ie_mod_e817f3ea",
                        column: x => x.child_age_source_id,
                        principalTable: "ie_moderation_child_age_policy_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_classification_taxonomy_sources_ie_mod_ebbe81e9",
                        column: x => x.constitution_adoption_id,
                        principalTable: "ie_moderation_constitution_adoptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_classification_taxonomy_sources_ie_use_83e47dcc",
                        column: x => x.reviewer_controller_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_classification_taxonomy_sources_ie_use_a2c85555",
                        column: x => x.owner_controller_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_classification_taxonomy_sources_ie_use_bfa2b8d8",
                        column: x => x.reviewer_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_classification_taxonomy_sources_ie_use_ce293574",
                        column: x => x.owner_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_event_target_adoptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    selection_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    selection_revision = table.Column<long>(type: "bigint", nullable: false),
                    binding_ordinal = table.Column<int>(type: "int", nullable: false),
                    generation_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    generation_revision = table.Column<long>(type: "bigint", nullable: false),
                    generation_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    case_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    case_revision = table.Column<long>(type: "bigint", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    adopted_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    revoked_by_human_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    revoked_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_event_target_adoptions", x => x.id);
                    table.CheckConstraint("ck_moderation_event_target_adoption_revision", "selection_revision = 1 AND generation_revision > 0 AND generation_writer_generation > 0 AND case_revision > 0 AND writer_generation > 0");
                    table.CheckConstraint("ck_moderation_event_target_adoption_revocation", "(revision = 1 AND revoked_at IS NULL AND revoked_by_human_id IS NULL) OR (revision = 2 AND revoked_at IS NOT NULL AND revoked_by_human_id IS NOT NULL AND revoked_at >= adopted_at)");
                    table.CheckConstraint("ck_moderation_event_target_adoption_validity", "valid_until > adopted_at");
                    table.ForeignKey(
                        name: "fk_ie_moderation_event_target_adoptions_ie_moderation_c_8a5d6efb",
                        columns: x => new { x.selection_id, x.binding_ordinal },
                        principalTable: "ie_moderation_contribution_bindings",
                        principalColumns: new[] { "selection_id", "ordinal" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_event_target_adoptions_ie_moderation_e_7cf02887",
                        column: x => x.generation_id,
                        principalTable: "ie_moderation_event_generations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_event_target_adoptions_ie_users_adopte_02db6947",
                        column: x => x.adopted_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_event_target_adoptions_ie_users_revoke_b3125587",
                        column: x => x.revoked_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_resource_target_adoptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    selection_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    selection_revision = table.Column<long>(type: "bigint", nullable: false),
                    binding_ordinal = table.Column<int>(type: "int", nullable: false),
                    generation_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    generation_revision = table.Column<long>(type: "bigint", nullable: false),
                    generation_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    case_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    case_revision = table.Column<long>(type: "bigint", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    adopted_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    revoked_by_human_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    revoked_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_resource_target_adoptions", x => x.id);
                    table.CheckConstraint("ck_moderation_resource_target_adoption_revision", "selection_revision = 1 AND generation_revision > 0 AND generation_writer_generation > 0 AND case_revision > 0 AND writer_generation > 0");
                    table.CheckConstraint("ck_moderation_resource_target_adoption_revocation", "(revision = 1 AND revoked_at IS NULL AND revoked_by_human_id IS NULL) OR (revision = 2 AND revoked_at IS NOT NULL AND revoked_by_human_id IS NOT NULL AND revoked_at >= adopted_at)");
                    table.CheckConstraint("ck_moderation_resource_target_adoption_validity", "valid_until > adopted_at");
                    table.ForeignKey(
                        name: "fk_ie_moderation_resource_target_adoptions_ie_moderatio_360d45ea",
                        column: x => x.generation_id,
                        principalTable: "ie_moderation_resource_generations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_resource_target_adoptions_ie_moderatio_36e7639b",
                        columns: x => new { x.selection_id, x.binding_ordinal },
                        principalTable: "ie_moderation_contribution_bindings",
                        principalColumns: new[] { "selection_id", "ordinal" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_resource_target_adoptions_ie_users_ado_389c2904",
                        column: x => x.adopted_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_resource_target_adoptions_ie_users_rev_50206cab",
                        column: x => x.revoked_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_publication_profiles",
                columns: table => new
                {
                    publication_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    profile_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    rule_code = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    rule_revision = table.Column<long>(type: "bigint", nullable: false),
                    reason_code = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reason_revision = table.Column<long>(type: "bigint", nullable: false),
                    reference_profile = table.Column<int>(type: "int", nullable: false),
                    reference_revision = table.Column<long>(type: "bigint", nullable: false),
                    grade = table.Column<int>(type: "int", nullable: false),
                    bundle = table.Column<int>(type: "int", nullable: false),
                    protection_purpose = table.Column<int>(type: "int", nullable: true),
                    timing_kind = table.Column<int>(type: "int", nullable: false),
                    duration_ticks = table.Column<long>(type: "bigint", nullable: false),
                    required_approvals = table.Column<int>(type: "int", nullable: false),
                    contestation_calendar_months = table.Column<int>(type: "int", nullable: false),
                    recognition = table.Column<int>(type: "int", nullable: false),
                    supported = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_publication_profiles", x => new { x.publication_id, x.profile_id });
                    table.CheckConstraint("ck_moderation_profile_purpose", "(bundle = 10 AND protection_purpose BETWEEN 1 AND 2) OR (bundle BETWEEN 1 AND 9 AND protection_purpose IS NULL)");
                    table.CheckConstraint("ck_moderation_profile_quorum", "required_approvals BETWEEN 0 AND 2");
                    table.CheckConstraint("ck_moderation_profile_revision", "rule_revision > 0 AND reason_revision > 0 AND reference_revision > 0");
                    table.CheckConstraint("ck_moderation_profile_timing", "duration_ticks >= 0 AND timing_kind BETWEEN 1 AND 9");
                    table.ForeignKey(
                        name: "fk_ie_moderation_publication_profiles_ie_moderation_pol_2ebd9e60",
                        column: x => x.publication_id,
                        principalTable: "ie_moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_publication_profiles_ie_moderation_pub_3e05cbb9",
                        columns: x => new { x.publication_id, x.reason_code, x.reason_revision },
                        principalTable: "ie_moderation_publication_reasons",
                        principalColumns: new[] { "publication_id", "code", "revision" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_case_attribution_renewal_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    case_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    head_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    expected_case_revision = table.Column<long>(type: "bigint", nullable: false),
                    expected_source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    expected_generation = table.Column<long>(type: "bigint", nullable: false),
                    expected_head_revision = table.Column<long>(type: "bigint", nullable: false),
                    qualified_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_case_attribution_renewal_receipts", x => x.id);
                    table.CheckConstraint("ck_moderation_attribution_renewal_distinct_source", "id <> expected_source_id");
                    table.CheckConstraint("ck_moderation_attribution_renewal_expected", "expected_case_revision > 0 AND expected_generation > 0 AND expected_head_revision > 0 AND expected_head_revision = expected_generation");
                    table.ForeignKey(
                        name: "fk_ie_moderation_case_attribution_renewal_receipts_ie_m_15c92eb0",
                        column: x => x.id,
                        principalTable: "ie_moderation_case_attribution_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_case_attribution_renewal_receipts_ie_m_53565fe6",
                        column: x => x.case_id,
                        principalTable: "ie_moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_case_attribution_renewal_receipts_ie_m_6d1ee754",
                        column: x => x.head_id,
                        principalTable: "ie_moderation_case_attribution_heads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_case_attribution_renewal_receipts_ie_m_b06d2cf2",
                        column: x => x.expected_source_id,
                        principalTable: "ie_moderation_case_attribution_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_case_attribution_renewal_receipts_ie_t_826677d8",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_pre_finding_notices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    custody_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    case_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    case_revision = table.Column<long>(type: "bigint", nullable: false),
                    occurrence_case_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    evidence_revision = table.Column<long>(type: "bigint", nullable: false),
                    attribution_source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    attribution_generation = table.Column<long>(type: "bigint", nullable: false),
                    subject_kind = table.Column<int>(type: "int", nullable: false),
                    subject_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    recipient_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    recipient_assignment_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    recipient_assignment_revision = table.Column<long>(type: "bigint", nullable: false),
                    issued_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    reviewer_assignment_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    reviewer_assignment_revision = table.Column<long>(type: "bigint", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    retain_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    responded_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    response_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    purged_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    withdrawn = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    protected_notice = table.Column<byte[]>(type: "longblob", maxLength: 131072, nullable: false),
                    protected_response = table.Column<byte[]>(type: "longblob", maxLength: 131072, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_pre_finding_notices", x => x.id);
                    table.CheckConstraint("ck_pre_finding_notice_finite", "retain_until > issued_at");
                    table.CheckConstraint("ck_pre_finding_notice_link", "case_id <> occurrence_case_id AND case_revision > 0 AND evidence_revision > 0 AND attribution_generation > 0 AND revision > 0");
                    table.CheckConstraint("ck_pre_finding_notice_response", "(response_id IS NULL AND responded_at IS NULL) OR (response_id IS NOT NULL AND responded_at >= acknowledged_at AND responded_at < retain_until)");
                    table.ForeignKey(
                        name: "fk_ie_moderation_pre_finding_notices_ie_moderation_case_b87d90b4",
                        column: x => x.occurrence_case_id,
                        principalTable: "ie_moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_pre_finding_notices_ie_moderation_pre__d8db4b7d",
                        columns: x => new { x.custody_id, x.host_binding_id, x.trust_domain_id, x.tenant_id, x.case_id },
                        principalTable: "ie_moderation_pre_finding_custody",
                        principalColumns: new[] { "id", "host_binding_id", "trust_domain_id", "tenant_id", "case_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_pre_finding_notices_ie_moderation_revi_08b36d0d",
                        column: x => x.reviewer_assignment_id,
                        principalTable: "ie_moderation_reviewer_assignments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_pre_finding_notices_moderation_cases_case_id",
                        column: x => x.case_id,
                        principalTable: "ie_moderation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_moderation_pre_finding_notices_users_recipient_human_id",
                        column: x => x.recipient_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_profile_collection_procedure_adoptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_revision = table.Column<long>(type: "bigint", nullable: false),
                    source_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    adopted_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    withdrawn_by_human_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_profile_collection_procedure_adoptions", x => x.id);
                    table.CheckConstraint("ck_profile_collection_adoption_generation", "source_revision = 1 AND source_writer_generation > 0 AND writer_generation > source_writer_generation");
                    table.CheckConstraint("ck_profile_collection_adoption_validity", "valid_until > adopted_at");
                    table.CheckConstraint("ck_profile_collection_adoption_withdrawal", "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR (revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at >= adopted_at)");
                    table.ForeignKey(
                        name: "fk_ie_moderation_profile_collection_procedure_adoptions_15f6b96a",
                        column: x => x.adopted_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_profile_collection_procedure_adoptions_53a20347",
                        column: x => x.source_id,
                        principalTable: "ie_moderation_profile_collection_procedure_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_profile_collection_procedure_adoptions_62cba100",
                        column: x => x.withdrawn_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_profile_subject_consents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    procedure_adoption_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    procedure_adoption_revision = table.Column<long>(type: "bigint", nullable: false),
                    procedure_adoption_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    procedure_adopted_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    procedure_adopted_at = table.Column<long>(type: "bigint", nullable: false),
                    procedure_adoption_valid_until = table.Column<long>(type: "bigint", nullable: false),
                    binding = table.Column<string>(type: "varchar(4096)", maxLength: 4096, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    assessment = table.Column<string>(type: "varchar(8192)", maxLength: 8192, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    subject_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    granted_at = table.Column<long>(type: "bigint", nullable: false),
                    valid_until = table.Column<long>(type: "bigint", nullable: false),
                    withdrawn_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_profile_subject_consents", x => x.id);
                    table.CheckConstraint("ck_profile_subject_consent_generation", "generation > 0 AND generation < 9223372036854775807 AND procedure_adoption_revision = 1 AND procedure_adoption_writer_generation > 0 AND procedure_adoption_writer_generation < 9223372036854775807");
                    table.CheckConstraint("ck_profile_subject_consent_terminal", "(revision = 1 AND withdrawn_at IS NULL) OR (revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_at >= granted_at AND withdrawn_at < 3155378975999999999)");
                    table.CheckConstraint("ck_profile_subject_consent_validity", "granted_at > 0 AND valid_until < 3155378975999999999 AND valid_until > granted_at AND procedure_adopted_at > 0 AND procedure_adoption_valid_until < 3155378975999999999 AND procedure_adoption_valid_until > procedure_adopted_at AND granted_at >= procedure_adopted_at AND valid_until <= procedure_adoption_valid_until");
                    table.ForeignKey(
                        name: "fk_ie_moderation_profile_subject_consents_ie_moderation_4454e307",
                        column: x => x.source_id,
                        principalTable: "ie_moderation_profile_collection_procedure_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_profile_subject_consents_ie_user_pii_s_a26c8558",
                        column: x => x.subject_human_id,
                        principalTable: "ie_user_pii",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_classification_taxonomy_adoptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_revision = table.Column<long>(type: "bigint", nullable: false),
                    source_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    adopted_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    withdrawn_by_human_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_classification_taxonomy_adoptions", x => x.id);
                    table.CheckConstraint("ck_moderation_taxonomy_adoption_generation", "source_revision = 1 AND source_writer_generation > 0 AND writer_generation > source_writer_generation");
                    table.CheckConstraint("ck_moderation_taxonomy_adoption_validity", "valid_until > adopted_at");
                    table.CheckConstraint("ck_moderation_taxonomy_adoption_withdrawal", "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR (revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at >= adopted_at)");
                    table.ForeignKey(
                        name: "fk_ie_moderation_classification_taxonomy_adoptions_ie_m_eee6af44",
                        column: x => x.source_id,
                        principalTable: "ie_moderation_classification_taxonomy_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_classification_taxonomy_adoptions_ie_u_259a9607",
                        column: x => x.adopted_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_classification_taxonomy_adoptions_ie_u_81eaf10c",
                        column: x => x.withdrawn_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_classification_taxonomy_categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ordinal = table.Column<int>(type: "int", nullable: false),
                    category_id = table.Column<int>(type: "int", nullable: false),
                    code = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    kind = table.Column<int>(type: "int", nullable: false),
                    minimum_age = table.Column<int>(type: "int", nullable: false),
                    meaning = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    review_rationale = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_classification_taxonomy_categories", x => x.id);
                    table.CheckConstraint("ck_moderation_taxonomy_category", "category_id > 0 AND ordinal BETWEEN 0 AND 63 AND kind BETWEEN 1 AND 4 AND minimum_age BETWEEN 0 AND 130");
                    table.ForeignKey(
                        name: "fk_ie_moderation_classification_taxonomy_categories_ie__569a9e2a",
                        column: x => x.source_id,
                        principalTable: "ie_moderation_classification_taxonomy_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_publication_consumers",
                columns: table => new
                {
                    publication_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    profile_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    mode = table.Column<int>(type: "int", nullable: false),
                    capability = table.Column<int>(type: "int", nullable: false),
                    consumer_id = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    subject_resolver = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    target_resolver = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    authorization_owner = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    entry_checkpoint = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    terminal_checkpoint = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    writer_fence = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    freshness_class = table.Column<int>(type: "int", nullable: false),
                    owning_packet = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    conformance_test = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_publication_consumers_publication_id_p_4c42530a", x => new { x.publication_id, x.profile_id, x.mode, x.capability, x.consumer_id });
                    table.ForeignKey(
                        name: "fk_ie_moderation_publication_consumers_ie_moderation_pu_286a6a00",
                        columns: x => new { x.publication_id, x.profile_id },
                        principalTable: "ie_moderation_publication_profiles",
                        principalColumns: new[] { "publication_id", "profile_id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_publication_effects",
                columns: table => new
                {
                    publication_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    profile_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ordinal = table.Column<int>(type: "int", nullable: false),
                    kind = table.Column<int>(type: "int", nullable: false),
                    mode = table.Column<int>(type: "int", nullable: false),
                    target = table.Column<int>(type: "int", nullable: false),
                    capability = table.Column<int>(type: "int", nullable: true),
                    execution = table.Column<int>(type: "int", nullable: false),
                    semantic_owner = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_publication_effects_publication_id_pro_8cff8251", x => new { x.publication_id, x.profile_id, x.ordinal });
                    table.ForeignKey(
                        name: "fk_ie_moderation_publication_effects_ie_moderation_publ_9c7dd478",
                        columns: x => new { x.publication_id, x.profile_id },
                        principalTable: "ie_moderation_publication_profiles",
                        principalColumns: new[] { "publication_id", "profile_id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_publication_plans",
                columns: table => new
                {
                    publication_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    profile_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    strategy = table.Column<int>(type: "int", nullable: false),
                    content = table.Column<int>(type: "int", nullable: false),
                    management = table.Column<int>(type: "int", nullable: false),
                    resources = table.Column<int>(type: "int", nullable: false),
                    new_admission = table.Column<int>(type: "int", nullable: false),
                    existing_admission = table.Column<int>(type: "int", nullable: false),
                    private_disclosure = table.Column<int>(type: "int", nullable: false),
                    settlement = table.Column<int>(type: "int", nullable: false),
                    capacity = table.Column<int>(type: "int", nullable: false),
                    credentials = table.Column<int>(type: "int", nullable: false),
                    notifications = table.Column<int>(type: "int", nullable: false),
                    recognition = table.Column<int>(type: "int", nullable: false),
                    pending_work = table.Column<int>(type: "int", nullable: false),
                    restoration = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_publication_plans_publication_id_profi_d1e13ff2", x => new { x.publication_id, x.profile_id, x.strategy });
                    table.CheckConstraint("ck_moderation_publication_strategy", "strategy BETWEEN 0 AND 3");
                    table.ForeignKey(
                        name: "fk_ie_moderation_publication_plans_ie_moderation_public_8918db67",
                        columns: x => new { x.publication_id, x.profile_id },
                        principalTable: "ie_moderation_publication_profiles",
                        principalColumns: new[] { "publication_id", "profile_id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_publication_prerequisites",
                columns: table => new
                {
                    publication_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    profile_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    mode = table.Column<int>(type: "int", nullable: false),
                    ordinal = table.Column<int>(type: "int", nullable: false),
                    activation_gate = table.Column<int>(type: "int", nullable: true),
                    native_prerequisite = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_publication_prerequisites_publication__d3eff580", x => new { x.publication_id, x.profile_id, x.mode, x.ordinal });
                    table.CheckConstraint("ck_moderation_publication_prerequisite", "(activation_gate BETWEEN 1 AND 5 AND native_prerequisite IS NULL) OR (activation_gate IS NULL AND native_prerequisite BETWEEN 1 AND 30)");
                    table.ForeignKey(
                        name: "fk_ie_moderation_publication_prerequisites_ie_moderatio_584894fe",
                        columns: x => new { x.publication_id, x.profile_id },
                        principalTable: "ie_moderation_publication_profiles",
                        principalColumns: new[] { "publication_id", "profile_id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_publication_scopes",
                columns: table => new
                {
                    publication_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    profile_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    kind = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_publication_scopes_publication_id_prof_0f800c9f", x => new { x.publication_id, x.profile_id, x.kind });
                    table.ForeignKey(
                        name: "fk_ie_moderation_publication_scopes_ie_moderation_publi_949b3a20",
                        columns: x => new { x.publication_id, x.profile_id },
                        principalTable: "ie_moderation_publication_profiles",
                        principalColumns: new[] { "publication_id", "profile_id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_publication_subjects",
                columns: table => new
                {
                    publication_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    profile_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    kind = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_publication_subjects_publication_id_pr_8b42665c", x => new { x.publication_id, x.profile_id, x.kind });
                    table.ForeignKey(
                        name: "fk_ie_moderation_publication_subjects_ie_moderation_pub_e822c2c3",
                        columns: x => new { x.publication_id, x.profile_id },
                        principalTable: "ie_moderation_publication_profiles",
                        principalColumns: new[] { "publication_id", "profile_id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_profile_collection_assessment_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    procedure_source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    subject_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    owner_controller_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    reviewer_controller_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    procedure_adoption_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    procedure_adoption_revision = table.Column<long>(type: "bigint", nullable: false),
                    procedure_adoption_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    procedure_adopted_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    procedure_adopted_at = table.Column<long>(type: "bigint", nullable: false),
                    procedure_adoption_valid_until = table.Column<long>(type: "bigint", nullable: false),
                    binding = table.Column<string>(type: "varchar(4096)", maxLength: 4096, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    required = table.Column<int>(type: "int", nullable: false),
                    recorded_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    recorded_at = table.Column<long>(type: "bigint", nullable: false),
                    valid_until = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_profile_collection_assessment_sources", x => x.id);
                    table.CheckConstraint("ck_profile_assessment_source_generation", "revision = 1 AND generation > 0 AND generation < 9223372036854775807 AND procedure_adoption_revision = 1 AND procedure_adoption_writer_generation > 0 AND procedure_adoption_writer_generation < 9223372036854775807");
                    table.CheckConstraint("ck_profile_assessment_source_independence", "recorded_by_human_id <> procedure_adopted_by_human_id AND procedure_adopted_by_human_id <> owner_controller_human_id AND owner_controller_human_id <> reviewer_controller_human_id");
                    table.CheckConstraint("ck_profile_assessment_source_required", "required BETWEEN 0 AND 7");
                    table.CheckConstraint("ck_profile_assessment_source_window", "recorded_at > 0 AND valid_until < 3155378975999999999 AND valid_until > recorded_at AND procedure_adopted_at > 0 AND procedure_adoption_valid_until < 3155378975999999999 AND procedure_adoption_valid_until > procedure_adopted_at AND recorded_at >= procedure_adopted_at AND valid_until <= procedure_adoption_valid_until");
                    table.ForeignKey(
                        name: "fk_ie_moderation_profile_collection_assessment_sources__0acef0c8",
                        column: x => x.reviewer_controller_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_profile_collection_assessment_sources__342c9674",
                        column: x => x.procedure_adoption_id,
                        principalTable: "ie_moderation_profile_collection_procedure_adoptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_profile_collection_assessment_sources__8b988bd8",
                        column: x => x.recorded_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_profile_collection_assessment_sources__954825b0",
                        column: x => x.procedure_source_id,
                        principalTable: "ie_moderation_profile_collection_procedure_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_profile_collection_assessment_sources__9ef1e02e",
                        column: x => x.owner_controller_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_profile_collection_assessment_sources__a7104b68",
                        column: x => x.procedure_adopted_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_profile_collection_assessment_sources__b34c9d88",
                        column: x => x.subject_human_id,
                        principalTable: "ie_user_pii",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_profile_collection_subject_observations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    generation = table.Column<long>(type: "bigint", nullable: false),
                    observation_sequence = table.Column<long>(type: "bigint", nullable: false),
                    observation_stamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    observed_at = table.Column<long>(type: "bigint", nullable: false),
                    valid_until = table.Column<long>(type: "bigint", nullable: false),
                    subject_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    subject_login_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    solution_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    instance_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    jurisdiction_kind = table.Column<int>(type: "int", nullable: false),
                    epoch_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    epoch_revision = table.Column<long>(type: "bigint", nullable: false),
                    policy_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    publication_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    definition_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    profile_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    mode_code = table.Column<int>(type: "int", nullable: false),
                    procedure_source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    procedure_source_revision = table.Column<long>(type: "bigint", nullable: false),
                    procedure_source_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    procedure_adoption_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    procedure_adoption_revision = table.Column<long>(type: "bigint", nullable: false),
                    procedure_adoption_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    procedure_adopted_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    procedure_adopted_at = table.Column<long>(type: "bigint", nullable: false),
                    procedure_adoption_valid_until = table.Column<long>(type: "bigint", nullable: false),
                    original_procedure_digest = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    original_policy_digest = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    original_selection_digest = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_profile_collection_subject_observations", x => x.id);
                    table.CheckConstraint("ck_profile_subject_observation_generations", "revision = 1 AND generation > 0 AND generation < 9223372036854775807 AND observation_sequence = generation AND policy_revision > 0 AND policy_revision < 9223372036854775807 AND procedure_source_revision = 1 AND procedure_source_writer_generation > 0 AND procedure_adoption_revision = 1 AND procedure_adoption_writer_generation > procedure_source_writer_generation AND epoch_revision >= procedure_adoption_writer_generation AND epoch_revision < 9223372036854775807");
                    table.CheckConstraint("ck_profile_subject_observation_identity", "id <> subject_human_id AND id <> observation_stamp AND id <> procedure_source_id AND id <> procedure_adoption_id AND observation_stamp <> subject_human_id");
                    table.CheckConstraint("ck_profile_subject_observation_scope", "jurisdiction_kind IN (3, 4) AND mode_code BETWEEN 1 AND 2");
                    table.CheckConstraint("ck_profile_subject_observation_window", "observed_at > 0 AND valid_until < 3155378975999999999 AND valid_until > observed_at AND procedure_adopted_at > 0 AND procedure_adoption_valid_until < 3155378975999999999 AND procedure_adoption_valid_until > procedure_adopted_at AND observed_at >= procedure_adopted_at AND valid_until <= procedure_adoption_valid_until");
                    table.ForeignKey(
                        name: "fk_ie_moderation_profile_collection_subject_observation_16e94b76",
                        column: x => x.epoch_id,
                        principalTable: "ie_moderation_qualification_epochs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_profile_collection_subject_observation_1f0eaabe",
                        column: x => x.subject_human_id,
                        principalTable: "ie_user_pii",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_ie_moderation_profile_collection_subject_observation_2bb30bdc",
                        column: x => x.procedure_adoption_id,
                        principalTable: "ie_moderation_profile_collection_procedure_adoptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_profile_collection_subject_observation_f9875e8b",
                        column: x => x.procedure_source_id,
                        principalTable: "ie_moderation_profile_collection_procedure_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_classification_material_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    previous_generation = table.Column<long>(type: "bigint", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    event_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    event_stamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    event_generation_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    event_generation_revision = table.Column<long>(type: "bigint", nullable: false),
                    event_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    policy_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    publication_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    profile_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    mode_code = table.Column<int>(type: "int", nullable: false),
                    taxonomy_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    taxonomy_source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    taxonomy_source_revision = table.Column<long>(type: "bigint", nullable: false),
                    taxonomy_source_generation = table.Column<long>(type: "bigint", nullable: false),
                    taxonomy_adoption_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    taxonomy_adoption_revision = table.Column<long>(type: "bigint", nullable: false),
                    taxonomy_adoption_generation = table.Column<long>(type: "bigint", nullable: false),
                    owner_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    owner_controller_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    owner_assignment_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    owner_assignment_revision = table.Column<long>(type: "bigint", nullable: false),
                    reviewer_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    reviewer_controller_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ownership_grounds = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    review_grounds = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_classification_material_sources", x => x.id);
                    table.CheckConstraint("ck_moderation_material_generation", "revision = 1 AND previous_generation >= 0 AND event_generation_revision > 0 AND event_writer_generation > 0 AND taxonomy_source_revision = 1 AND taxonomy_adoption_revision = 1 AND taxonomy_source_generation > 0 AND taxonomy_adoption_generation > taxonomy_source_generation AND writer_generation > taxonomy_adoption_generation AND writer_generation > event_writer_generation AND writer_generation > previous_generation AND owner_assignment_revision > 0 AND policy_revision > 0 AND mode_code BETWEEN 1 AND 2");
                    table.CheckConstraint("ck_moderation_material_independence", "owner_human_id <> reviewer_human_id AND owner_controller_human_id <> reviewer_controller_human_id AND owner_human_id <> reviewer_controller_human_id AND reviewer_human_id <> owner_controller_human_id");
                    table.CheckConstraint("ck_moderation_material_validity", "valid_until > recorded_at");
                    table.ForeignKey(
                        name: "fk_ie_moderation_classification_material_sources_ie_mod_0cdcd7ff",
                        column: x => x.event_generation_id,
                        principalTable: "ie_moderation_event_generations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_classification_material_sources_ie_mod_2edc8b7f",
                        column: x => x.taxonomy_adoption_id,
                        principalTable: "ie_moderation_classification_taxonomy_adoptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_classification_material_sources_ie_mod_b02ecac4",
                        column: x => x.taxonomy_source_id,
                        principalTable: "ie_moderation_classification_taxonomy_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_profile_collection_assessment_adoptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_revision = table.Column<long>(type: "bigint", nullable: false),
                    source_generation = table.Column<long>(type: "bigint", nullable: false),
                    adopted_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    adopted_at = table.Column<long>(type: "bigint", nullable: false),
                    valid_until = table.Column<long>(type: "bigint", nullable: false),
                    withdrawn_by_human_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    withdrawn_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_profile_collection_assessment_adoptions", x => x.id);
                    table.CheckConstraint("ck_profile_assessment_adoption_generation", "source_revision = 1 AND source_generation > 0 AND source_generation < 9223372036854775807 AND generation > source_generation AND generation < 9223372036854775807");
                    table.CheckConstraint("ck_profile_assessment_adoption_terminal", "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR (revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at >= adopted_at AND withdrawn_at < 3155378975999999999)");
                    table.CheckConstraint("ck_profile_assessment_adoption_window", "adopted_at > 0 AND valid_until < 3155378975999999999 AND valid_until > adopted_at");
                    table.ForeignKey(
                        name: "fk_ie_moderation_profile_collection_assessment_adoption_4c3a1cd7",
                        column: x => x.source_id,
                        principalTable: "ie_moderation_profile_collection_assessment_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_ie_moderation_profile_collection_assessment_adoption_8554088f",
                        column: x => x.withdrawn_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_profile_collection_assessment_adoption_97ee5271",
                        column: x => x.adopted_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_classification_material_components",
                columns: table => new
                {
                    source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ordinal = table.Column<int>(type: "int", nullable: false),
                    kind = table.Column<int>(type: "int", nullable: false),
                    native_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    native_stamp = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    digest = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_classification_material_components_sou_51ae3123", x => new { x.source_id, x.ordinal });
                    table.CheckConstraint("ck_moderation_material_component_kind", "kind BETWEEN 1 AND 41 AND ordinal BETWEEN 0 AND 4095");
                    table.ForeignKey(
                        name: "fk_ie_moderation_classification_material_components_ie__a2a7ce43",
                        column: x => x.source_id,
                        principalTable: "ie_moderation_classification_material_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_classification_material_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ordinal = table.Column<int>(type: "int", nullable: false),
                    event_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    event_stamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    session_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    session_stamp = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    resource_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    resource_stamp = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_classification_material_entries", x => x.id);
                    table.CheckConstraint("ck_moderation_material_entry_ancestry", "((session_id IS NULL AND session_stamp IS NULL) OR (session_id IS NOT NULL AND session_stamp IS NOT NULL)) AND ((resource_id IS NULL AND resource_stamp IS NULL) OR (resource_id IS NOT NULL AND resource_stamp IS NOT NULL))");
                    table.CheckConstraint("ck_moderation_material_entry_ordinal", "ordinal BETWEEN 0 AND 4095");
                    table.ForeignKey(
                        name: "fk_ie_moderation_classification_material_entries_ie_mod_7f5fd8b3",
                        column: x => x.source_id,
                        principalTable: "ie_moderation_classification_material_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_classification_material_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_revision = table.Column<long>(type: "bigint", nullable: false),
                    source_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    reviewer_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    end_kind = table.Column<int>(type: "int", nullable: true),
                    ended_by_human_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    end_writer_generation = table.Column<long>(type: "bigint", nullable: true),
                    ended_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    end_grounds = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_classification_material_reviews", x => x.id);
                    table.CheckConstraint("ck_moderation_material_review_ending", "(revision = 1 AND end_kind IS NULL AND ended_at IS NULL AND ended_by_human_id IS NULL AND end_writer_generation IS NULL AND end_grounds IS NULL) OR (revision = 2 AND end_kind BETWEEN 1 AND 2 AND ended_at IS NOT NULL AND ended_by_human_id IS NOT NULL AND end_writer_generation > writer_generation AND end_grounds IS NOT NULL AND ended_at >= accepted_at)");
                    table.CheckConstraint("ck_moderation_material_review_generation", "source_revision = 1 AND source_writer_generation > 0 AND writer_generation > source_writer_generation");
                    table.CheckConstraint("ck_moderation_material_review_validity", "valid_until > accepted_at");
                    table.ForeignKey(
                        name: "fk_ie_moderation_classification_material_reviews_ie_mod_7e2865b2",
                        column: x => x.source_id,
                        principalTable: "ie_moderation_classification_material_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_classification_material_categories",
                columns: table => new
                {
                    entry_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ordinal = table.Column<int>(type: "int", nullable: false),
                    version_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_classification_material_categories_ent_a446ee48", x => new { x.entry_id, x.ordinal });
                    table.CheckConstraint("ck_moderation_material_category_ordinal", "ordinal BETWEEN 0 AND 63");
                    table.ForeignKey(
                        name: "fk_ie_moderation_classification_material_categories_ie__134bbe8b",
                        column: x => x.version_id,
                        principalTable: "ie_moderation_classification_taxonomy_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_classification_material_categories_ie__c2735dc0",
                        column: x => x.entry_id,
                        principalTable: "ie_moderation_classification_material_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_classification_control_selections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_revision = table.Column<long>(type: "bigint", nullable: false),
                    source_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    source_recorded_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    source_valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    review_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    review_revision = table.Column<long>(type: "bigint", nullable: false),
                    review_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    review_valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    generation_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    generation_revision = table.Column<long>(type: "bigint", nullable: false),
                    generation_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    generation_observed_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    generation_valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    event_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    event_stamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    policy_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    publication_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    profile_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    mode_code = table.Column<int>(type: "int", nullable: false),
                    owner_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    owner_controller_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    reviewer_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    reviewer_controller_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    authored_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    selection_digest = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    starts_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
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
                        name: "fk_ie_moderation_classification_control_selections_ie_m_0bf33e66",
                        column: x => x.source_id,
                        principalTable: "ie_moderation_classification_material_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_classification_control_selections_ie_m_174702f1",
                        column: x => x.review_id,
                        principalTable: "ie_moderation_classification_material_reviews",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_classification_control_selections_ie_m_182e34a4",
                        column: x => x.publication_id,
                        principalTable: "ie_moderation_policy_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_classification_control_selections_ie_m_a94dc87e",
                        column: x => x.host_binding_id,
                        principalTable: "ie_moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_classification_control_selections_ie_m_e63939a6",
                        column: x => x.generation_id,
                        principalTable: "ie_moderation_event_generations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_classification_control_selections_ie_t_f6d14588",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_classification_control_selections_ie_u_3689d05c",
                        column: x => x.authored_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_classification_control_bindings",
                columns: table => new
                {
                    selection_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ordinal = table.Column<int>(type: "int", nullable: false),
                    kind = table.Column<int>(type: "int", nullable: false),
                    effect_kind = table.Column<int>(type: "int", nullable: true),
                    effect_mode = table.Column<int>(type: "int", nullable: true),
                    effect_target = table.Column<int>(type: "int", nullable: true),
                    capability = table.Column<int>(type: "int", nullable: true),
                    execution = table.Column<int>(type: "int", nullable: true),
                    strategy = table.Column<int>(type: "int", nullable: true),
                    scope_kind = table.Column<int>(type: "int", nullable: false),
                    scope_trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    scope_solution_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    scope_instance_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    scope_tenant_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    scope_community_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    scope_resource_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    scope_event_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    subject_kind = table.Column<int>(type: "int", nullable: false),
                    subject_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    target_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    target_revision = table.Column<long>(type: "bigint", nullable: false),
                    operation = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    window_end = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_classification_control_bindings_select_88c26235", x => new { x.selection_id, x.ordinal });
                    table.CheckConstraint("ck_classification_control_binding_event", "scope_kind = 7");
                    table.CheckConstraint("ck_classification_control_binding_meaning", "(kind = 1 AND effect_kind BETWEEN 1 AND 26 AND effect_kind IS NOT NULL AND effect_mode BETWEEN 1 AND 8 AND effect_mode IS NOT NULL AND effect_target = 12 AND effect_target IS NOT NULL AND execution = 2 AND execution IS NOT NULL AND strategy IS NULL AND (capability IS NULL OR capability BETWEEN 1 AND 12)) OR (kind = 2 AND effect_kind IS NULL AND effect_mode IS NULL AND effect_target IS NULL AND capability IS NULL AND execution IS NULL AND (strategy IS NULL OR strategy IN (1, 2, 3)))");
                    table.CheckConstraint("ck_classification_control_binding_partition", "ordinal >= 0 AND ordinal < 64 AND target_revision > 0 AND target_revision < 9223372036854775807 AND subject_kind = 4 AND subject_id = target_id AND target_id = scope_event_id AND window_end IS NULL");
                    table.CheckConstraint("ck_moderation_classification_control_bindings_scope_scope", "scope_kind IS NOT NULL AND ((scope_kind = 1 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 2 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 3 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 4 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 5 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NOT NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 6 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_resource_id IS NOT NULL AND scope_event_id IS NULL) OR (scope_kind = 7 AND scope_trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NOT NULL AND scope_event_id <> '00000000-0000-0000-0000-000000000000'))");
                    table.ForeignKey(
                        name: "fk_ie_moderation_classification_control_bindings_ie_mod_6ff5d95d",
                        column: x => x.selection_id,
                        principalTable: "ie_moderation_classification_control_selections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_classification_event_target_adoptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    selection_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    selection_revision = table.Column<long>(type: "bigint", nullable: false),
                    selection_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    binding_ordinal = table.Column<int>(type: "int", nullable: false),
                    generation_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    generation_revision = table.Column<long>(type: "bigint", nullable: false),
                    generation_writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    event_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    event_stamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    owner_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    owner_controller_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    reviewer_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    reviewer_controller_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    selection_authored_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    adopted_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    writer_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    selection_recorded_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    selection_valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    generation_observed_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    generation_valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    adopted_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    revoked_by_human_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    revocation_writer_generation = table.Column<long>(type: "bigint", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    revocation_grounds = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
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
                        name: "fk_ie_moderation_classification_event_target_adoptions__57952e39",
                        column: x => x.generation_id,
                        principalTable: "ie_moderation_event_generations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_classification_event_target_adoptions__64659f60",
                        column: x => x.host_binding_id,
                        principalTable: "ie_moderation_host_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_classification_event_target_adoptions__921114e0",
                        column: x => x.revoked_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_classification_event_target_adoptions__92dded0b",
                        columns: x => new { x.selection_id, x.binding_ordinal },
                        principalTable: "ie_moderation_classification_control_bindings",
                        principalColumns: new[] { "selection_id", "ordinal" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_classification_event_target_adoptions__cb6e3a56",
                        column: x => x.adopted_by_human_id,
                        principalTable: "ie_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_classification_event_target_adoptions__cd1792d8",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_event_resource_moderation_quarantines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    event_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    event_resource_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    material_version_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    effect_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    binding_ordinal = table.Column<int>(type: "int", nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    release_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_event_resource_moderation_quarantines", x => x.id);
                    table.UniqueConstraint("ak_event_resource_moderation_quarantines_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_event_resource_quarantines_cause", "effect_id <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("ck_event_resource_quarantines_material", "material_version_id <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("ck_event_resource_quarantines_ordinal", "binding_ordinal >= 0 AND binding_ordinal < 64");
                    table.ForeignKey(
                        name: "fk_ie_event_resource_moderation_quarantines_ie_event_re_496bf7e9",
                        columns: x => new { x.tenant_id, x.event_id, x.event_resource_id },
                        principalTable: "ie_event_resources",
                        principalColumns: new[] { "tenant_id", "event_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_event_resource_moderation_review_duties",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    event_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    event_resource_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    material_version_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    applied_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_event_resource_moderation_review_duties", x => x.id);
                    table.UniqueConstraint("ak_event_resource_moderation_review_duties_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_ie_event_resource_moderation_review_duties_ie_event__2cbd2a50",
                        columns: x => new { x.tenant_id, x.id },
                        principalTable: "ie_event_resource_moderation_quarantines",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_event_resource_quarantine_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    quarantine_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    event_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    event_resource_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    material_version_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    case_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    case_revision = table.Column<long>(type: "bigint", nullable: false),
                    step = table.Column<int>(type: "int", nullable: false),
                    purpose_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    predecessor_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    reviewer_assignment_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    reviewer_revision = table.Column<long>(type: "bigint", nullable: false),
                    controller_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    required_approvals = table.Column<int>(type: "int", nullable: false),
                    maximum_evidence_lifetime_ticks = table.Column<long>(type: "bigint", nullable: false),
                    attestation = table.Column<int>(type: "int", nullable: true),
                    resource_stamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    native_digest = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    request_digest = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    qualification_revision = table.Column<long>(type: "bigint", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_event_resource_quarantine_reviews", x => x.id);
                    table.UniqueConstraint("ak_event_resource_quarantine_reviews_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_resource_safety_lifetime", "maximum_evidence_lifetime_ticks > 0");
                    table.CheckConstraint("ck_resource_safety_quorum", "required_approvals BETWEEN 1 AND 2");
                    table.CheckConstraint("ck_resource_safety_step", "step BETWEEN 1 AND 6");
                    table.ForeignKey(
                        name: "fk_ie_event_resource_quarantine_reviews_ie_event_resour_f4b96bc9",
                        columns: x => new { x.tenant_id, x.quarantine_id },
                        principalTable: "ie_event_resource_moderation_quarantines",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_event_resource_moderation_review_escalations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    host_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    service_principal_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    qualification_revision = table.Column<long>(type: "bigint", nullable: false),
                    escalated_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    purpose_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    recipient_count = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_event_resource_moderation_review_escalations", x => x.id);
                    table.CheckConstraint("ck_resource_review_escalation_recipients", "recipient_count BETWEEN 1 AND 32");
                    table.ForeignKey(
                        name: "fk_ie_event_resource_moderation_review_escalations_ie_e_4aa04010",
                        columns: x => new { x.tenant_id, x.id },
                        principalTable: "ie_event_resource_moderation_review_duties",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_event_resource_moderation_review_completions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    quarantine_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    event_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    event_resource_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    material_version_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    purpose_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    attestation_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    approval_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    disposition = table.Column<int>(type: "int", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_event_resource_moderation_review_completions", x => x.id);
                    table.CheckConstraint("ck_resource_review_disposition", "disposition IN (1, 2)");
                    table.ForeignKey(
                        name: "fk_ie_event_resource_moderation_review_completions_ie_e_02f73703",
                        columns: x => new { x.tenant_id, x.quarantine_id },
                        principalTable: "ie_event_resource_moderation_quarantines",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_event_resource_moderation_review_completions_ie_e_4823f1fb",
                        columns: x => new { x.tenant_id, x.approval_id },
                        principalTable: "ie_event_resource_quarantine_reviews",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_event_resource_moderation_review_completions_ie_e_7edd4e82",
                        columns: x => new { x.tenant_id, x.attestation_id },
                        principalTable: "ie_event_resource_quarantine_reviews",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_event_resource_quarantine_releases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    quarantine_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    event_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    event_resource_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    material_version_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    original_effect_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    original_binding_ordinal = table.Column<int>(type: "int", nullable: false),
                    originally_applied_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    approval_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    released_by_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    released_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    request_digest = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_event_resource_quarantine_releases", x => x.id);
                    table.UniqueConstraint("ak_event_resource_moderation_quarantine_release_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_ie_event_resource_quarantine_releases_ie_event_resou_18544deb",
                        columns: x => new { x.tenant_id, x.approval_id },
                        principalTable: "ie_event_resource_quarantine_reviews",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_event_resource_quarantine_releases_ie_event_resou_b77dafbd",
                        columns: x => new { x.tenant_id, x.quarantine_id },
                        principalTable: "ie_event_resource_moderation_quarantines",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_recognition_action_recovery",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    fresh_subject_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    verified_binding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    capture_item_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    capture_material_receipt = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    original_intent_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    privacy_owner_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    recovery_owner_human_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    provider_kind = table.Column<int>(type: "int", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    disposition_by = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_recognition_action_recovery", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_recognition_capture_item",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    intent_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    erasure_subject_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    subject_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    subject_solution_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    subject_instance_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_revision = table.Column<long>(type: "bigint", nullable: false),
                    source_generation = table.Column<long>(type: "bigint", nullable: false),
                    adoption_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    adoption_revision = table.Column<long>(type: "bigint", nullable: false),
                    adoption_generation = table.Column<long>(type: "bigint", nullable: false),
                    ring_generation = table.Column<long>(type: "bigint", nullable: false),
                    trust_domain_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    provider_kind = table.Column<int>(type: "int", nullable: false),
                    purpose = table.Column<int>(type: "int", nullable: false),
                    key_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    key_generation = table.Column<long>(type: "bigint", nullable: false),
                    serialization_version = table.Column<int>(type: "int", nullable: false),
                    digest = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    key_verification_tag = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    sanction_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    sanction_revision = table.Column<long>(type: "bigint", nullable: false),
                    decision_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    decision_revision = table.Column<long>(type: "bigint", nullable: false),
                    finding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    parent_capture_item_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    continuity_binding_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    rule_owner_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    rule_code = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    rule_revision = table.Column<long>(type: "bigint", nullable: false),
                    original_capability_mask = table.Column<int>(type: "int", nullable: false),
                    purpose_capability_mask = table.Column<int>(type: "int", nullable: false),
                    scope_kind = table.Column<int>(type: "int", nullable: false),
                    scope_solution_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    scope_instance_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    scope_tenant_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    scope_community_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    scope_resource_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    scope_event_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    starts_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    review_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    captured_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    original_captured_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    retain_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    dropped_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_recognition_capture_item", x => x.id);
                    table.CheckConstraint("ck_moderation_recognition_capture_item_scope_scope", "scope_kind IS NOT NULL AND ((scope_kind = 1 AND trust_domain_id IS NOT NULL AND scope_solution_id IS NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 2 AND trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 3 AND trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 4 AND trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 5 AND trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NOT NULL AND scope_resource_id IS NULL AND scope_event_id IS NULL) OR (scope_kind = 6 AND trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_resource_id IS NOT NULL AND scope_event_id IS NULL) OR (scope_kind = 7 AND trust_domain_id IS NOT NULL AND scope_solution_id IS NOT NULL AND scope_instance_id IS NOT NULL AND scope_tenant_id IS NOT NULL AND scope_community_id IS NULL AND scope_resource_id IS NULL AND scope_event_id IS NOT NULL AND scope_event_id <> '00000000-0000-0000-0000-000000000000'))");
                    table.ForeignKey(
                        name: "fk_ie_moderation_recognition_capture_item_ie_moderation_0c974dc9",
                        column: x => x.intent_id,
                        principalTable: "ie_moderation_recognition_pending",
                        principalColumn: "intent_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_moderation_recognition_capture_item_ie_moderation_df916637",
                        column: x => x.parent_capture_item_id,
                        principalTable: "ie_moderation_recognition_capture_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_moderation_recognition_continuity_binding",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    fresh_subject_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    capture_item_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    capture_material_receipt = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    sanction_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    finding_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    bound_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    retain_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_moderation_recognition_continuity_binding", x => x.id);
                    table.ForeignKey(
                        name: "fk_ie_moderation_recognition_continuity_binding_ie_mode_4d5bf504",
                        column: x => x.capture_item_id,
                        principalTable: "ie_moderation_recognition_capture_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

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
                name: "ix_ie_moderation_findings_case_id_rule_owner_id_rule_co_46ad65fd",
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
                name: "ix_ie_moderation_cases_scope_trust_domain_id_scope_tena_4b09d2a5",
                table: "ie_moderation_cases",
                columns: new[] { "scope_trust_domain_id", "scope_tenant_id", "occurrence_id", "occurrence_owner_kind", "occurrence_owner_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_cases_occurrence_case_id",
                table: "ie_moderation_cases",
                column: "occurrence_case_id");

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
                name: "ix_event_report_signals_tenant_id_report_id_case_id",
                table: "ie_event_report_signals",
                columns: new[] { "tenant_id", "report_id", "case_id" });

            migrationBuilder.CreateIndex(
                name: "ix_event_report_signals_tenant_id_report_id_provider_link_id",
                table: "ie_event_report_signals",
                columns: new[] { "tenant_id", "report_id", "provider_link_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ie_event_report_signals_tenant_id_provider_advisory__ab44f81a",
                table: "ie_event_report_signals",
                columns: new[] { "tenant_id", "provider", "advisory_identity", "advisory_ordinal" },
                unique: true,
                filter: "advisory_identity IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_ie_event_report_signals_tenant_id_provider_provider__21af8934",
                table: "ie_event_report_signals",
                columns: new[] { "tenant_id", "provider", "provider_target_scope", "provider_target_id", "non_advisory_external_signal_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_event_report_signals_tenant_id_provider_provider__2f63e1e5",
                table: "ie_event_report_signals",
                columns: new[] { "tenant_id", "provider", "provider_target_scope", "provider_target_id", "non_advisory_correlation_id" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_event_report_signals_advisory_source",
                table: "ie_event_report_signals",
                sql: "(advisory_identity IS NULL AND advisory_ordinal IS NULL AND advisory_material_sha256 IS NULL AND case_id IS NULL AND source_case_concurrency_stamp IS NULL AND provider_link_id IS NULL AND incoming_webhook_message_id IS NULL) OR (advisory_identity IS NOT NULL AND trim(advisory_identity) <> '' AND advisory_ordinal IS NOT NULL AND advisory_ordinal >= 0 AND advisory_material_sha256 IS NOT NULL AND length(advisory_material_sha256) = 71 AND report_id IS NOT NULL AND case_id IS NOT NULL AND source_case_concurrency_stamp IS NOT NULL AND source_case_concurrency_stamp <> '00000000-0000-0000-0000-000000000000' AND provider_link_id IS NOT NULL AND ((provider = 3 AND incoming_webhook_message_id IS NOT NULL) OR (provider = 2 AND incoming_webhook_message_id IS NULL)))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_event_report_external_links_case_generation",
                table: "ie_event_report_external_links",
                sql: "(case_id IS NULL AND case_concurrency_stamp IS NULL) OR (case_id IS NOT NULL AND case_concurrency_stamp IS NOT NULL AND case_concurrency_stamp <> '00000000-0000-0000-0000-000000000000')");

            migrationBuilder.CreateIndex(
                name: "ix_event_resource_moderation_quarantines_tenant_id_release_id",
                table: "ie_event_resource_moderation_quarantines",
                columns: new[] { "tenant_id", "release_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ie_event_resource_moderation_quarantines_tenant_id_e_cc325103",
                table: "ie_event_resource_moderation_quarantines",
                columns: new[] { "tenant_id", "event_id", "event_resource_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ie_event_resource_moderation_quarantines_tenant_id_e_d00ef7d4",
                table: "ie_event_resource_moderation_quarantines",
                columns: new[] { "tenant_id", "event_resource_id", "effect_id", "binding_ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_event_resource_moderation_review_completions_tena_16b672c9",
                table: "ie_event_resource_moderation_review_completions",
                columns: new[] { "tenant_id", "purpose_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_event_resource_moderation_review_completions_tena_254b93c1",
                table: "ie_event_resource_moderation_review_completions",
                columns: new[] { "tenant_id", "attestation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ie_event_resource_moderation_review_completions_tena_51bd6f92",
                table: "ie_event_resource_moderation_review_completions",
                columns: new[] { "tenant_id", "quarantine_id", "completed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_ie_event_resource_moderation_review_completions_tena_a9d47df3",
                table: "ie_event_resource_moderation_review_completions",
                columns: new[] { "tenant_id", "approval_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ie_event_resource_moderation_review_duties_tenant_id_ccf0ef48",
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
                name: "ix_ie_event_resource_quarantine_reviews_tenant_id_quara_b7e7832c",
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
                name: "ix_ie_key_replica_trust_domain_id_runtime_replica_id_ri_f97ebfaa",
                table: "ie_key_replica",
                columns: new[] { "trust_domain_id", "runtime_replica_id", "ring_generation" });

            migrationBuilder.CreateIndex(
                name: "ix_key_replica_trust_domain_id_sequence",
                table: "ie_key_replica",
                columns: new[] { "trust_domain_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_case_attribution_heads_tenant_id_trust_ee3924f0",
                table: "ie_moderation_case_attribution_heads",
                columns: new[] { "tenant_id", "trust_domain_id", "occurrence_case_id", "subject_kind", "subject_id" },
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
                name: "ix_ie_moderation_case_attribution_renewal_receipts_case_99f00b73",
                table: "ie_moderation_case_attribution_renewal_receipts",
                columns: new[] { "case_id", "expected_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_case_attribution_renewal_receipts_expe_730e0f94",
                table: "ie_moderation_case_attribution_renewal_receipts",
                column: "expected_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_case_attribution_renewal_receipts_head_17cae751",
                table: "ie_moderation_case_attribution_renewal_receipts",
                columns: new[] { "head_id", "expected_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_attribution_renewal_receipts_tenant_id",
                table: "ie_moderation_case_attribution_renewal_receipts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_case_attribution_sources_tenant_id_tru_27fab4f2",
                table: "ie_moderation_case_attribution_sources",
                columns: new[] { "tenant_id", "trust_domain_id", "occurrence_case_id", "subject_kind", "subject_id", "generation" },
                unique: true);

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
                name: "ix_ie_moderation_case_intake_acknowledgements_budget_ad_9fa53005",
                table: "ie_moderation_case_intake_acknowledgements",
                columns: new[] { "budget_adoption_id", "trust_domain_id", "tenant_id", "budget_source_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_case_intake_acknowledgements_tenant_id_48b20da4",
                table: "ie_moderation_case_intake_acknowledgements",
                columns: new[] { "tenant_id", "caller_user_id", "operation_id" },
                unique: true);

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
                name: "ix_ie_moderation_case_narrative_adoptions_source_id_hos_04c85347",
                table: "ie_moderation_case_narrative_adoptions",
                columns: new[] { "source_id", "host_binding_id", "trust_domain_id", "tenant_id" });

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
                name: "ix_moderation_case_narrative_adoptions_withdrawn_by_human_id",
                table: "ie_moderation_case_narrative_adoptions",
                column: "withdrawn_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_case_narrative_policy_sources_governan_2e2c4bcc",
                table: "ie_moderation_case_narrative_policy_sources",
                column: "governance_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_case_narrative_policy_sources_host_bin_ad718924",
                table: "ie_moderation_case_narrative_policy_sources",
                columns: new[] { "host_binding_id", "tenant_id", "target_kind", "purpose", "writer_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_case_narrative_policy_sources_privacy__0c77773b",
                table: "ie_moderation_case_narrative_policy_sources",
                column: "privacy_owner_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_case_narrative_policy_sources_privacy__d251e037",
                table: "ie_moderation_case_narrative_policy_sources",
                column: "privacy_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_case_narrative_policy_sources_recorded_ab4a47ff",
                table: "ie_moderation_case_narrative_policy_sources",
                column: "recorded_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_narrative_policy_sources_tenant_id",
                table: "ie_moderation_case_narrative_policy_sources",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_case_narratives_adoption_id_host_bindi_f9235ef6",
                table: "ie_moderation_case_narratives",
                columns: new[] { "adoption_id", "host_binding_id", "trust_domain_id", "tenant_id", "policy_source_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_case_narratives_tenant_id_trust_domain_2f745e1a",
                table: "ie_moderation_case_narratives",
                columns: new[] { "tenant_id", "trust_domain_id", "case_id", "source_id", "material_fact_id" },
                unique: true);

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
                name: "ix_ie_moderation_case_read_grant_revocation_receipts_ad_00e9d21d",
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
                name: "ix_ie_moderation_case_read_grants_tenant_id_case_id_cal_60801750",
                table: "ie_moderation_case_read_grants",
                columns: new[] { "tenant_id", "case_id", "caller_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_read_grants_case_id",
                table: "ie_moderation_case_read_grants",
                column: "case_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_read_grants_source_id",
                table: "ie_moderation_case_read_grants",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_case_target_policies_tenant_id_trust_d_d5f10021",
                table: "ie_moderation_case_target_policies",
                columns: new[] { "tenant_id", "trust_domain_id", "target_kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_case_target_receipts_tenant_id_caller__99782812",
                table: "ie_moderation_case_target_receipts",
                columns: new[] { "tenant_id", "caller_user_id", "expires_at" });

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
                name: "ix_moderation_case_target_receipts_token_digest",
                table: "ie_moderation_case_target_receipts",
                column: "token_digest",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_case_target_sources_tenant_id_trust_do_7c73fe8a",
                table: "ie_moderation_case_target_sources",
                columns: new[] { "tenant_id", "trust_domain_id", "kind", "native_kind", "native_target_id", "native_target_version", "native_facts_digest", "occurrence_digest", "material_digest" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_case_target_sources_event_id",
                table: "ie_moderation_case_target_sources",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_child_age_policy_adoptions_withdrawn_b_81fc657a",
                table: "ie_moderation_child_age_policy_adoptions",
                column: "withdrawn_by_human_id");

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
                name: "ix_ie_moderation_child_age_policy_sources_collection_co_1b0b0fe3",
                table: "ie_moderation_child_age_policy_sources",
                column: "collection_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_child_age_policy_sources_collection_ow_a1c71d90",
                table: "ie_moderation_child_age_policy_sources",
                column: "collection_owner_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_child_age_policy_sources_governance_co_0163b64c",
                table: "ie_moderation_child_age_policy_sources",
                column: "governance_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_child_age_policy_sources_host_binding__c58dda8a",
                table: "ie_moderation_child_age_policy_sources",
                columns: new[] { "host_binding_id", "tenant_id", "publication_id", "profile_id", "mode_code", "writer_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_child_age_policy_sources_reviewer_cont_9094f762",
                table: "ie_moderation_child_age_policy_sources",
                column: "reviewer_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_child_age_policy_sources_definition_id",
                table: "ie_moderation_child_age_policy_sources",
                column: "definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_child_age_policy_sources_recorded_by_human_id",
                table: "ie_moderation_child_age_policy_sources",
                column: "recorded_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_child_age_policy_sources_reviewer_human_id",
                table: "ie_moderation_child_age_policy_sources",
                column: "reviewer_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_classification_control_selections_auth_fb67b0e1",
                table: "ie_moderation_classification_control_selections",
                column: "authored_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_classification_control_selections_host_f28eab43",
                table: "ie_moderation_classification_control_selections",
                columns: new[] { "host_binding_id", "trust_domain_id", "tenant_id", "event_id", "review_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_classification_control_selections_publ_3f96ff2d",
                table: "ie_moderation_classification_control_selections",
                column: "publication_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_control_selections_generation_id",
                table: "ie_moderation_classification_control_selections",
                column: "generation_id");

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
                name: "ix_ie_moderation_classification_event_target_adoptions__01ea27c2",
                table: "ie_moderation_classification_event_target_adoptions",
                column: "adopted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_classification_event_target_adoptions__05f108b4",
                table: "ie_moderation_classification_event_target_adoptions",
                column: "host_binding_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_classification_event_target_adoptions__2e25e60f",
                table: "ie_moderation_classification_event_target_adoptions",
                column: "revoked_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_classification_event_target_adoptions__341cf84a",
                table: "ie_moderation_classification_event_target_adoptions",
                columns: new[] { "selection_id", "binding_ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_classification_event_target_adoptions__919198cc",
                table: "ie_moderation_classification_event_target_adoptions",
                column: "generation_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_event_target_adoptions_tenant_id",
                table: "ie_moderation_classification_event_target_adoptions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_classification_material_categories_ent_5e9bb4b9",
                table: "ie_moderation_classification_material_categories",
                columns: new[] { "entry_id", "version_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_material_categories_version_id",
                table: "ie_moderation_classification_material_categories",
                column: "version_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_classification_material_components_sou_622edc7b",
                table: "ie_moderation_classification_material_components",
                columns: new[] { "source_id", "kind", "native_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_classification_material_entries_source_287c97e4",
                table: "ie_moderation_classification_material_entries",
                columns: new[] { "source_id", "ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_material_reviews_source_id",
                table: "ie_moderation_classification_material_reviews",
                column: "source_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_classification_material_sources_event__da7f7684",
                table: "ie_moderation_classification_material_sources",
                column: "event_generation_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_classification_material_sources_host_b_1dbf8c09",
                table: "ie_moderation_classification_material_sources",
                columns: new[] { "host_binding_id", "tenant_id", "event_id", "writer_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_classification_material_sources_taxono_94b3aaae",
                table: "ie_moderation_classification_material_sources",
                column: "taxonomy_adoption_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_classification_material_sources_taxono_aa701a7c",
                table: "ie_moderation_classification_material_sources",
                column: "taxonomy_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_classification_taxonomy_adoptions_adop_139a7841",
                table: "ie_moderation_classification_taxonomy_adoptions",
                column: "adopted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_classification_taxonomy_adoptions_with_8bd04c6d",
                table: "ie_moderation_classification_taxonomy_adoptions",
                column: "withdrawn_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_taxonomy_adoptions_source_id",
                table: "ie_moderation_classification_taxonomy_adoptions",
                column: "source_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_classification_taxonomy_categories_sou_3d8ca835",
                table: "ie_moderation_classification_taxonomy_categories",
                columns: new[] { "source_id", "category_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_classification_taxonomy_categories_sou_5f954d79",
                table: "ie_moderation_classification_taxonomy_categories",
                columns: new[] { "source_id", "ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_classification_taxonomy_categories_sou_aad8630d",
                table: "ie_moderation_classification_taxonomy_categories",
                columns: new[] { "source_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_classification_taxonomy_sources_child__0425b377",
                table: "ie_moderation_classification_taxonomy_sources",
                column: "child_age_adoption_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_classification_taxonomy_sources_child__5941d030",
                table: "ie_moderation_classification_taxonomy_sources",
                column: "child_age_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_classification_taxonomy_sources_consti_77a59e5d",
                table: "ie_moderation_classification_taxonomy_sources",
                column: "constitution_adoption_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_classification_taxonomy_sources_consti_954e717b",
                table: "ie_moderation_classification_taxonomy_sources",
                column: "constitution_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_classification_taxonomy_sources_host_b_2eb36f44",
                table: "ie_moderation_classification_taxonomy_sources",
                columns: new[] { "host_binding_id", "tenant_id", "taxonomy_id", "taxonomy_revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_classification_taxonomy_sources_host_b_455bc83b",
                table: "ie_moderation_classification_taxonomy_sources",
                columns: new[] { "host_binding_id", "tenant_id", "taxonomy_id", "writer_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_classification_taxonomy_sources_owner__85f4f14f",
                table: "ie_moderation_classification_taxonomy_sources",
                column: "owner_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_classification_taxonomy_sources_review_33bae537",
                table: "ie_moderation_classification_taxonomy_sources",
                column: "reviewer_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_classification_taxonomy_sources_review_810b071d",
                table: "ie_moderation_classification_taxonomy_sources",
                column: "reviewer_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_taxonomy_sources_definition_id",
                table: "ie_moderation_classification_taxonomy_sources",
                column: "definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_classification_taxonomy_sources_owner_human_id",
                table: "ie_moderation_classification_taxonomy_sources",
                column: "owner_human_id");

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
                name: "ix_ie_moderation_constitution_availability_host_binding_76346def",
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
                name: "ix_ie_moderation_constitution_reviewers_source_id_avail_6176a346",
                table: "ie_moderation_constitution_reviewers",
                columns: new[] { "source_id", "availability_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_constitution_reviewers_source_id_contr_0b69c872",
                table: "ie_moderation_constitution_reviewers",
                columns: new[] { "source_id", "controller_human_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_constitution_reviewers_source_id_revie_789f91d8",
                table: "ie_moderation_constitution_reviewers",
                columns: new[] { "source_id", "reviewer_human_id" },
                unique: true);

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
                name: "ix_ie_moderation_constitution_sources_host_binding_id_t_1bccb4eb",
                table: "ie_moderation_constitution_sources",
                columns: new[] { "host_binding_id", "tenant_id", "publication_id", "profile_id", "mode_code", "writer_generation" },
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
                name: "ix_moderation_constitution_sources_original_effect_id",
                table: "ie_moderation_constitution_sources",
                column: "original_effect_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_constitution_sources_recorded_by_human_id",
                table: "ie_moderation_constitution_sources",
                column: "recorded_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_consumer_installations_host_binding_id_e7285e49",
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
                name: "ix_ie_moderation_consumer_measurement_challenges_origin_a13cf19f",
                table: "ie_moderation_consumer_measurement_challenges",
                column: "original_effect_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_consumer_measurement_challenges_prepar_ab13a022",
                table: "ie_moderation_consumer_measurement_challenges",
                column: "prepared_by_human_id");

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
                name: "ix_moderation_consumer_measurement_challenges_tenant_id",
                table: "ie_moderation_consumer_measurement_challenges",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_consumer_measurement_receipts_accepted_527781dc",
                table: "ie_moderation_consumer_measurement_receipts",
                column: "accepted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_consumer_measurement_receipts_host_bin_216d6307",
                table: "ie_moderation_consumer_measurement_receipts",
                columns: new[] { "host_binding_id", "tenant_id", "run_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_consumer_measurement_receipts_host_bin_f165b2eb",
                table: "ie_moderation_consumer_measurement_receipts",
                columns: new[] { "host_binding_id", "tenant_id", "challenge_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_consumer_measurement_receipts_issuer_e_31758a21",
                table: "ie_moderation_consumer_measurement_receipts",
                column: "issuer_enrollment_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_consumer_measurement_receipts_challenge_id",
                table: "ie_moderation_consumer_measurement_receipts",
                column: "challenge_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_consumer_measurement_receipts_tenant_id",
                table: "ie_moderation_consumer_measurement_receipts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_consumer_measurement_receipts_token_digest",
                table: "ie_moderation_consumer_measurement_receipts",
                column: "token_digest");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_content_responses_tenant_id_representa_8b2d02b7",
                table: "ie_moderation_content_responses",
                columns: new[] { "tenant_id", "representative_human_id" });

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
                name: "ix_ie_moderation_contribution_selections_host_binding_i_23934c17",
                table: "ie_moderation_contribution_selections",
                columns: new[] { "host_binding_id", "trust_domain_id", "tenant_id", "effect_id" },
                unique: true);

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
                name: "ix_ie_moderation_deployment_consumers_source_id_binding_002ccb34",
                table: "ie_moderation_deployment_consumers",
                columns: new[] { "source_id", "binding_digest", "consumer_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_deployment_consumers_installation_id",
                table: "ie_moderation_deployment_consumers",
                column: "installation_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_deployment_sources_host_binding_id_ten_69ef899b",
                table: "ie_moderation_deployment_sources",
                columns: new[] { "host_binding_id", "tenant_id", "publication_id", "profile_id", "mode_code", "writer_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_deployment_sources_definition_id",
                table: "ie_moderation_deployment_sources",
                column: "definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_deployment_sources_recorded_by_human_id",
                table: "ie_moderation_deployment_sources",
                column: "recorded_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_event_generations_host_binding_id_tena_f3b95494",
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
                name: "ix_ie_moderation_event_rights_sources_continuity_mappin_af42f9df",
                table: "ie_moderation_event_rights_sources",
                column: "continuity_mapping_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_event_rights_sources_continuity_mappin_f932072d",
                table: "ie_moderation_event_rights_sources",
                column: "continuity_mapping_owner_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_event_rights_sources_governance_contro_294a8ac5",
                table: "ie_moderation_event_rights_sources",
                column: "governance_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_event_rights_sources_host_binding_id_t_6d552a5d",
                table: "ie_moderation_event_rights_sources",
                columns: new[] { "host_binding_id", "tenant_id", "publication_id", "profile_id", "mode_code", "writer_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_event_rights_sources_reviewer_controll_f6cb9c8b",
                table: "ie_moderation_event_rights_sources",
                column: "reviewer_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_event_rights_sources_settlement_mappin_bafef216",
                table: "ie_moderation_event_rights_sources",
                column: "settlement_mapping_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_event_rights_sources_settlement_mappin_cd31be89",
                table: "ie_moderation_event_rights_sources",
                column: "settlement_mapping_owner_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_event_rights_sources_definition_id",
                table: "ie_moderation_event_rights_sources",
                column: "definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_event_rights_sources_recorded_by_human_id",
                table: "ie_moderation_event_rights_sources",
                column: "recorded_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_event_rights_sources_reviewer_human_id",
                table: "ie_moderation_event_rights_sources",
                column: "reviewer_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_event_target_adoptions_selection_id_bi_0c4047c7",
                table: "ie_moderation_event_target_adoptions",
                columns: new[] { "selection_id", "binding_ordinal" },
                unique: true);

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
                name: "ix_ie_moderation_measured_operations_acceptances_accept_9329db41",
                table: "ie_moderation_measured_operations_acceptances",
                column: "accepted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_measured_operations_acceptances_withdr_6ed2d7a5",
                table: "ie_moderation_measured_operations_acceptances",
                column: "withdrawn_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_measured_operations_acceptances_source_id",
                table: "ie_moderation_measured_operations_acceptances",
                column: "source_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_measured_operations_sources_host_bindi_af7b2344",
                table: "ie_moderation_measured_operations_sources",
                columns: new[] { "host_binding_id", "tenant_id", "binding_digest", "writer_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_measured_operations_sources_recorded_b_a8de0098",
                table: "ie_moderation_measured_operations_sources",
                column: "recorded_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_measured_operations_sources_recorder_c_5e4226f5",
                table: "ie_moderation_measured_operations_sources",
                column: "recorder_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_measured_operations_sources_reviewer_c_4ad77ccf",
                table: "ie_moderation_measured_operations_sources",
                column: "reviewer_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_measured_operations_sources_definition_id",
                table: "ie_moderation_measured_operations_sources",
                column: "definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_measured_operations_sources_original_effect_id",
                table: "ie_moderation_measured_operations_sources",
                column: "original_effect_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_measured_operations_sources_reviewer_human_id",
                table: "ie_moderation_measured_operations_sources",
                column: "reviewer_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_measurement_issuer_enrollments_approve_2c21dd8b",
                table: "ie_moderation_measurement_issuer_enrollments",
                column: "approved_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_measurement_issuer_enrollments_host_bi_38799fea",
                table: "ie_moderation_measurement_issuer_enrollments",
                columns: new[] { "host_binding_id", "tenant_id", "key_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_measurement_issuer_enrollments_revoked_e8b39200",
                table: "ie_moderation_measurement_issuer_enrollments",
                column: "revoked_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_measurement_issuer_enrollments_tenant_id",
                table: "ie_moderation_measurement_issuer_enrollments",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_notice_disclosure_approvals_approved_b_3af5a0c5",
                table: "ie_moderation_notice_disclosure_approvals",
                column: "approved_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_notice_disclosure_approvals_host_bindi_9adc9076",
                table: "ie_moderation_notice_disclosure_approvals",
                columns: new[] { "host_binding_id", "trust_domain_id", "tenant_id", "effect_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_notice_disclosure_approvals_case_id",
                table: "ie_moderation_notice_disclosure_approvals",
                column: "case_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_notice_disclosure_approvals_decision_id",
                table: "ie_moderation_notice_disclosure_approvals",
                column: "decision_id");

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
                name: "ix_ie_moderation_notice_sources_trust_domain_id_retain__b6bf8f1d",
                table: "ie_moderation_notice_sources",
                columns: new[] { "trust_domain_id", "retain_until", "effect_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_notice_sources_trust_domain_id_subject_37bb4752",
                table: "ie_moderation_notice_sources",
                columns: new[] { "trust_domain_id", "subject_kind", "subject_id", "captured_at" });

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_notices_effect_id_audience_recipient_s_515b1de7",
                table: "ie_moderation_notices",
                columns: new[] { "effect_id", "audience", "recipient_subject_kind", "recipient_subject_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_notices_trust_domain_id_recipient_subj_aec421e0",
                table: "ie_moderation_notices",
                columns: new[] { "trust_domain_id", "recipient_subject_kind", "recipient_subject_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_notices_native_intent_id",
                table: "ie_moderation_notices",
                column: "native_intent_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_operational_budget_adoptions_adopted_b_3f56cbdd",
                table: "ie_moderation_operational_budget_adoptions",
                column: "adopted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_operational_budget_adoptions_source_id_d9d173c6",
                table: "ie_moderation_operational_budget_adoptions",
                columns: new[] { "source_id", "host_binding_id", "trust_domain_id", "tenant_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_operational_budget_adoptions_withdrawn_d20811d1",
                table: "ie_moderation_operational_budget_adoptions",
                column: "withdrawn_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_operational_budget_adoptions_source_id",
                table: "ie_moderation_operational_budget_adoptions",
                column: "source_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_operational_budget_sources_host_bindin_138192d7",
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
                name: "ix_ie_moderation_participant_incident_responsibility_so_09aaaaad",
                table: "ie_moderation_participant_incident_responsibility_sources",
                column: "read_grant_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_participant_incident_responsibility_so_0d4bcfdf",
                table: "ie_moderation_participant_incident_responsibility_sources",
                column: "participant_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_participant_incident_responsibility_so_13965b20",
                table: "ie_moderation_participant_incident_responsibility_sources",
                column: "original_narrative_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_participant_incident_responsibility_so_18ebfc99",
                table: "ie_moderation_participant_incident_responsibility_sources",
                column: "occurrence_case_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_participant_incident_responsibility_so_1fbe6d03",
                table: "ie_moderation_participant_incident_responsibility_sources",
                column: "evidence_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_participant_incident_responsibility_so_2b735dd3",
                table: "ie_moderation_participant_incident_responsibility_sources",
                column: "host_binding_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_participant_incident_responsibility_so_545ecc6e",
                table: "ie_moderation_participant_incident_responsibility_sources",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_participant_incident_responsibility_so_5f01d778",
                table: "ie_moderation_participant_incident_responsibility_sources",
                columns: new[] { "custody_adoption_id", "host_binding_id", "trust_domain_id", "tenant_id", "custody_source_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_participant_incident_responsibility_so_818362c9",
                table: "ie_moderation_participant_incident_responsibility_sources",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_participant_incident_responsibility_so_9e46c89a",
                table: "ie_moderation_participant_incident_responsibility_sources",
                column: "withdrawn_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_participant_incident_responsibility_so_a060b24f",
                table: "ie_moderation_participant_incident_responsibility_sources",
                columns: new[] { "tenant_id", "trust_domain_id", "purge_at" });

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_participant_incident_responsibility_so_a881a213",
                table: "ie_moderation_participant_incident_responsibility_sources",
                column: "read_issuer_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_participant_incident_responsibility_so_b22ad62e",
                table: "ie_moderation_participant_incident_responsibility_sources",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_participant_incident_responsibility_so_b2951e7d",
                table: "ie_moderation_participant_incident_responsibility_sources",
                column: "assessor_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_participant_incident_responsibility_so_b9936351",
                table: "ie_moderation_participant_incident_responsibility_sources",
                column: "qualification_policy_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_participant_incident_responsibility_so_c469e10a",
                table: "ie_moderation_participant_incident_responsibility_sources",
                column: "subject_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_participant_incident_responsibility_so_c9a97f73",
                table: "ie_moderation_participant_incident_responsibility_sources",
                columns: new[] { "tenant_id", "trust_domain_id", "occurrence_case_id", "subject_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_participant_incident_responsibility_so_eb144970",
                table: "ie_moderation_participant_incident_responsibility_sources",
                columns: new[] { "custody_source_id", "host_binding_id", "trust_domain_id", "tenant_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_policy_publication_candidates_authorit_edb1e1d5",
                table: "ie_moderation_policy_publication_candidates",
                column: "authority_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_policy_publication_candidates_policy_i_8389f7eb",
                table: "ie_moderation_policy_publication_candidates",
                columns: new[] { "policy_id", "previous_publication_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_policy_publication_candidates_policy_i_cdc6962e",
                table: "ie_moderation_policy_publication_candidates",
                columns: new[] { "policy_id", "definition_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_policy_publication_candidates_prepared_1f377461",
                table: "ie_moderation_policy_publication_candidates",
                column: "prepared_by_human_id");

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
                name: "ix_moderation_policy_publication_candidates_tenant_id",
                table: "ie_moderation_policy_publication_candidates",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_policy_publications_policy_id_previous_8e86ddc0",
                table: "ie_moderation_policy_publications",
                columns: new[] { "policy_id", "previous_publication_id" });

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
                name: "ix_moderation_policy_publications_tenant_id",
                table: "ie_moderation_policy_publications",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_policy_selections_host_binding_id_juri_57d320ba",
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
                name: "ix_ie_moderation_pre_finding_notices_custody_id_host_bi_b678ac47",
                table: "ie_moderation_pre_finding_notices",
                columns: new[] { "custody_id", "host_binding_id", "trust_domain_id", "tenant_id", "case_id" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_pre_finding_notices_case_id",
                table: "ie_moderation_pre_finding_notices",
                column: "case_id",
                unique: true);

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
                name: "ix_ie_moderation_profile_collection_assessment_adoption_3b29c4ba",
                table: "ie_moderation_profile_collection_assessment_adoptions",
                column: "source_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_profile_collection_assessment_adoption_613262a1",
                table: "ie_moderation_profile_collection_assessment_adoptions",
                column: "withdrawn_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_profile_collection_assessment_adoption_e4376bb1",
                table: "ie_moderation_profile_collection_assessment_adoptions",
                column: "adopted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_profile_collection_assessment_sources__25a631bf",
                table: "ie_moderation_profile_collection_assessment_sources",
                column: "procedure_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_profile_collection_assessment_sources__66db4225",
                table: "ie_moderation_profile_collection_assessment_sources",
                column: "reviewer_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_profile_collection_assessment_sources__7dd9368a",
                table: "ie_moderation_profile_collection_assessment_sources",
                column: "recorded_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_profile_collection_assessment_sources__bd50051e",
                table: "ie_moderation_profile_collection_assessment_sources",
                columns: new[] { "subject_human_id", "procedure_source_id", "generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_profile_collection_assessment_sources__c55b5093",
                table: "ie_moderation_profile_collection_assessment_sources",
                column: "procedure_adoption_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_profile_collection_assessment_sources__c91b2572",
                table: "ie_moderation_profile_collection_assessment_sources",
                column: "procedure_adopted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_profile_collection_assessment_sources__d04703e0",
                table: "ie_moderation_profile_collection_assessment_sources",
                column: "owner_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_profile_collection_procedure_adoptions_07074174",
                table: "ie_moderation_profile_collection_procedure_adoptions",
                column: "source_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_profile_collection_procedure_adoptions_5a082008",
                table: "ie_moderation_profile_collection_procedure_adoptions",
                column: "adopted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_profile_collection_procedure_adoptions_9ea4aa53",
                table: "ie_moderation_profile_collection_procedure_adoptions",
                column: "withdrawn_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_profile_collection_procedure_sources_c_41203319",
                table: "ie_moderation_profile_collection_procedure_sources",
                column: "child_age_adoption_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_profile_collection_procedure_sources_c_617f5a30",
                table: "ie_moderation_profile_collection_procedure_sources",
                column: "child_age_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_profile_collection_procedure_sources_h_44e5408b",
                table: "ie_moderation_profile_collection_procedure_sources",
                columns: new[] { "host_binding_id", "tenant_id", "publication_id", "profile_id", "mode_code", "writer_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_profile_collection_procedure_sources_r_1894381d",
                table: "ie_moderation_profile_collection_procedure_sources",
                column: "recorded_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_profile_collection_subject_observation_276ff9a6",
                table: "ie_moderation_profile_collection_subject_observations",
                columns: new[] { "host_binding_id", "tenant_id", "subject_human_id", "profile_id", "mode_code", "observation_sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_profile_collection_subject_observation_48772c5a",
                table: "ie_moderation_profile_collection_subject_observations",
                column: "observation_stamp",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_profile_collection_subject_observation_8d5b49b6",
                table: "ie_moderation_profile_collection_subject_observations",
                column: "procedure_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_profile_collection_subject_observation_a9af7a2c",
                table: "ie_moderation_profile_collection_subject_observations",
                column: "procedure_adoption_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_profile_collection_subject_observation_ea09d997",
                table: "ie_moderation_profile_collection_subject_observations",
                column: "subject_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_profile_collection_subject_observation_f76e5b50",
                table: "ie_moderation_profile_collection_subject_observations",
                column: "epoch_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_profile_subject_consents_subject_human_7c92d99a",
                table: "ie_moderation_profile_subject_consents",
                columns: new[] { "subject_human_id", "source_id", "generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_profile_subject_consents_source_id",
                table: "ie_moderation_profile_subject_consents",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_publication_profiles_publication_id_re_7bd399dd",
                table: "ie_moderation_publication_profiles",
                columns: new[] { "publication_id", "reason_code", "reason_revision" });

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_publication_profiles_publication_id_ru_1b5cf994",
                table: "ie_moderation_publication_profiles",
                columns: new[] { "publication_id", "rule_code", "rule_revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_publication_reasons_publication_id_cla_b975b732",
                table: "ie_moderation_publication_reasons",
                columns: new[] { "publication_id", "class_code", "class_revision" });

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_qualification_policy_adoptions_adopted_1a6f5b2f",
                table: "ie_moderation_qualification_policy_adoptions",
                column: "adopted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_qualification_policy_adoptions_revoked_63930a62",
                table: "ie_moderation_qualification_policy_adoptions",
                column: "revoked_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_qualification_policy_adoptions_source__0169acd5",
                table: "ie_moderation_qualification_policy_adoptions",
                columns: new[] { "source_id", "host_binding_id", "trust_domain_id", "tenant_id", "case_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_qualification_policy_adoptions_source__a6e99522",
                table: "ie_moderation_qualification_policy_adoptions",
                columns: new[] { "source_id", "source_revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_qualification_policy_sources_host_bind_e869c4f8",
                table: "ie_moderation_qualification_policy_sources",
                columns: new[] { "host_binding_id", "trust_domain_id", "tenant_id", "case_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_qualification_policy_sources_published_b16bac61",
                table: "ie_moderation_qualification_policy_sources",
                column: "published_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_qualification_policy_sources_case_id",
                table: "ie_moderation_qualification_policy_sources",
                column: "case_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_qualification_policy_sources_tenant_id",
                table: "ie_moderation_qualification_policy_sources",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_recognition_action_recovery_fresh_subj_efcfc75d",
                table: "ie_moderation_recognition_action_recovery",
                columns: new[] { "fresh_subject_id", "verified_binding_id", "capture_item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_recognition_action_recovery_resolved_a_337f3cd6",
                table: "ie_moderation_recognition_action_recovery",
                columns: new[] { "resolved_at", "disposition_by" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_action_recovery_capture_item_id",
                table: "ie_moderation_recognition_action_recovery",
                column: "capture_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_recognition_capture_item_intent_id_pro_37fa3122",
                table: "ie_moderation_recognition_capture_item",
                columns: new[] { "intent_id", "provider_kind", "purpose", "digest", "sanction_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_recognition_capture_item_trust_domain__4474d291",
                table: "ie_moderation_recognition_capture_item",
                columns: new[] { "trust_domain_id", "provider_kind", "purpose", "key_id", "digest" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_capture_item_continuity_binding_id",
                table: "ie_moderation_recognition_capture_item",
                column: "continuity_binding_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_capture_item_parent_capture_item_id",
                table: "ie_moderation_recognition_capture_item",
                column: "parent_capture_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_recognition_continuity_binding_fresh_s_0543a76e",
                table: "ie_moderation_recognition_continuity_binding",
                columns: new[] { "fresh_subject_id", "sanction_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_continuity_binding_capture_item_id",
                table: "ie_moderation_recognition_continuity_binding",
                column: "capture_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_continuity_binding_retain_until",
                table: "ie_moderation_recognition_continuity_binding",
                column: "retain_until");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_recognition_disposition_intent_id_pend_0e1dda16",
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
                name: "ix_ie_moderation_recognition_escalation_recipient_human_deeb8761",
                table: "ie_moderation_recognition_escalation",
                columns: new[] { "recipient_human_id", "acknowledged_at" });

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_recognition_escalation_target_id_sourc_7a53c911",
                table: "ie_moderation_recognition_escalation",
                columns: new[] { "target_id", "source_id", "recipient_human_id", "stage" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_escalation_retain_until",
                table: "ie_moderation_recognition_escalation",
                column: "retain_until");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_recognition_key_lifecycle_key_trust_do_72deb5da",
                table: "ie_moderation_recognition_key_lifecycle_key",
                columns: new[] { "trust_domain_id", "key_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_recognition_key_lifecycle_purge_author_1bbe5dfd",
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
                name: "ix_ie_moderation_recognition_privacy_adoptions_adopted__142b2377",
                table: "ie_moderation_recognition_privacy_adoptions",
                column: "adopted_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_recognition_privacy_adoptions_withdraw_a7a30332",
                table: "ie_moderation_recognition_privacy_adoptions",
                column: "withdrawn_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_privacy_adoptions_source_id",
                table: "ie_moderation_recognition_privacy_adoptions",
                column: "source_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_recognition_privacy_sources_governance_861fe672",
                table: "ie_moderation_recognition_privacy_sources",
                column: "governance_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_recognition_privacy_sources_host_bindi_416244eb",
                table: "ie_moderation_recognition_privacy_sources",
                columns: new[] { "host_binding_id", "tenant_id", "publication_id", "profile_id", "mode_code", "writer_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_recognition_privacy_sources_privacy_co_946d4f44",
                table: "ie_moderation_recognition_privacy_sources",
                column: "privacy_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_recognition_privacy_sources_privacy_ow_a26a6340",
                table: "ie_moderation_recognition_privacy_sources",
                column: "privacy_owner_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_recognition_privacy_sources_recorded_b_580d8af5",
                table: "ie_moderation_recognition_privacy_sources",
                column: "recorded_by_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_recognition_privacy_sources_recovery_c_b7aa7c42",
                table: "ie_moderation_recognition_privacy_sources",
                column: "recovery_controller_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_recognition_privacy_sources_recovery_o_c9a796ff",
                table: "ie_moderation_recognition_privacy_sources",
                column: "recovery_owner_human_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recognition_privacy_sources_definition_id",
                table: "ie_moderation_recognition_privacy_sources",
                column: "definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_recovery_store_states_installation_slot",
                table: "ie_moderation_recovery_store_states",
                column: "installation_slot",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_resource_generations_host_binding_id_t_800b796c",
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
                name: "ix_ie_moderation_resource_target_adoptions_selection_id_8463a26c",
                table: "ie_moderation_resource_target_adoptions",
                columns: new[] { "selection_id", "binding_ordinal" },
                unique: true);

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
                name: "ix_ie_moderation_schedule_risk_reviews_accepted_reviewe_dcceff9a",
                table: "ie_moderation_schedule_risk_reviews",
                column: "accepted_reviewer_assignment_id");

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_schedule_risk_reviews_event_scope_trus_08f8f6a2",
                table: "ie_moderation_schedule_risk_reviews",
                columns: new[] { "event_scope_trust_domain_id", "event_scope_tenant_id", "state", "due_at" });

            migrationBuilder.CreateIndex(
                name: "ix_ie_moderation_schedule_risk_reviews_source_sanction__92cc59bd",
                table: "ie_moderation_schedule_risk_reviews",
                columns: new[] { "source_sanction_id", "event_scope_event_id", "session_id", "participant_id", "original_schedule_generation", "original_participant_generation", "original_publication_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_schedule_risk_reviews_accepted_finding_id",
                table: "ie_moderation_schedule_risk_reviews",
                column: "accepted_finding_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_birth_date_collection_receipts_user_id",
                table: "ie_user_birth_date_collection_receipts",
                column: "user_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_ie_event_report_signals_ie_event_report_cases_tenant_07a31b70",
                table: "ie_event_report_signals",
                columns: new[] { "tenant_id", "report_id", "case_id" },
                principalTable: "ie_event_report_cases",
                principalColumns: new[] { "tenant_id", "report_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_ie_event_report_signals_ie_event_report_external_lin_f4e63079",
                table: "ie_event_report_signals",
                columns: new[] { "tenant_id", "report_id", "provider_link_id" },
                principalTable: "ie_event_report_external_links",
                principalColumns: new[] { "tenant_id", "report_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_ie_event_report_signals_ie_incoming_webhook_messages_2830e115",
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
                name: "fk_ie_event_resource_moderation_quarantines_ie_event_re_43a47b2d",
                table: "ie_event_resource_moderation_quarantines",
                columns: new[] { "tenant_id", "release_id" },
                principalTable: "ie_event_resource_quarantine_releases",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_ie_moderation_recognition_action_recovery_ie_moderat_0f572818",
                table: "ie_moderation_recognition_action_recovery",
                column: "capture_item_id",
                principalTable: "ie_moderation_recognition_capture_item",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_ie_moderation_recognition_capture_item_ie_moderation_d4d527c9",
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
                name: "fk_ie_event_report_signals_ie_event_report_cases_tenant_07a31b70",
                table: "ie_event_report_signals");

            migrationBuilder.DropForeignKey(
                name: "fk_ie_event_report_signals_ie_event_report_external_lin_f4e63079",
                table: "ie_event_report_signals");

            migrationBuilder.DropForeignKey(
                name: "fk_ie_event_report_signals_ie_incoming_webhook_messages_2830e115",
                table: "ie_event_report_signals");

            migrationBuilder.DropForeignKey(
                name: "fk_moderation_cases_moderation_cases_occurrence_case_id",
                table: "ie_moderation_cases");

            migrationBuilder.DropForeignKey(
                name: "fk_ie_event_resource_moderation_quarantines_ie_event_re_43a47b2d",
                table: "ie_event_resource_moderation_quarantines");

            migrationBuilder.DropForeignKey(
                name: "fk_ie_moderation_recognition_continuity_binding_ie_mode_4d5bf504",
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
                name: "ie_moderation_classification_taxonomy_categories");

            migrationBuilder.DropTable(
                name: "ie_moderation_classification_material_entries");

            migrationBuilder.DropTable(
                name: "ie_moderation_constitution_availability");

            migrationBuilder.DropTable(
                name: "ie_moderation_measurement_issuer_enrollments");

            migrationBuilder.DropTable(
                name: "ie_moderation_consumer_measurement_challenges");

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
                name: "ie_moderation_resource_generations");

            migrationBuilder.DropTable(
                name: "ie_moderation_contribution_bindings");

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
                name: "ie_moderation_event_generations");

            migrationBuilder.DropTable(
                name: "ie_moderation_classification_taxonomy_adoptions");

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
                name: "ie_moderation_recognition_pending");

            migrationBuilder.DropTable(
                name: "ie_moderation_recognition_continuity_binding");

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
                name: "ix_ie_moderation_findings_case_id_rule_owner_id_rule_co_46ad65fd",
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
                name: "ix_ie_moderation_cases_scope_trust_domain_id_scope_tena_4b09d2a5",
                table: "ie_moderation_cases");

            migrationBuilder.DropIndex(
                name: "ix_moderation_cases_occurrence_case_id",
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
                name: "ix_event_report_signals_tenant_id_report_id_case_id",
                table: "ie_event_report_signals");

            migrationBuilder.DropIndex(
                name: "ix_event_report_signals_tenant_id_report_id_provider_link_id",
                table: "ie_event_report_signals");

            migrationBuilder.DropIndex(
                name: "ix_ie_event_report_signals_tenant_id_provider_advisory__ab44f81a",
                table: "ie_event_report_signals");

            migrationBuilder.DropIndex(
                name: "ix_ie_event_report_signals_tenant_id_provider_provider__21af8934",
                table: "ie_event_report_signals");

            migrationBuilder.DropIndex(
                name: "ix_ie_event_report_signals_tenant_id_provider_provider__2f63e1e5",
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
                name: "non_advisory_correlation_id",
                table: "ie_event_report_signals");

            migrationBuilder.DropColumn(
                name: "non_advisory_external_signal_id",
                table: "ie_event_report_signals");

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
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci",
                oldClrType: typeof(Guid),
                oldType: "char(36)",
                oldNullable: true)
                .OldAnnotation("Relational:Collation", "ascii_general_ci");

            migrationBuilder.AlterColumn<int>(
                name: "subject_origin_kind",
                table: "ie_moderation_cases",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "subject_kind",
                table: "ie_moderation_cases",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "subject_id",
                table: "ie_moderation_cases",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci",
                oldClrType: typeof(Guid),
                oldType: "char(36)",
                oldNullable: true)
                .OldAnnotation("Relational:Collation", "ascii_general_ci");

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
                name: "ix_ie_moderation_findings_case_id_rule_owner_id_rule_co_a3f515ca",
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
                name: "ix_ie_moderation_cases_scope_trust_domain_id_scope_tena_03fcca41",
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
                name: "ix_ie_event_report_signals_tenant_id_provider_provider__8d2fa8b8",
                table: "ie_event_report_signals",
                columns: new[] { "tenant_id", "provider", "provider_target_scope", "provider_target_id", "correlation_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_event_report_signals_tenant_id_provider_provider__9034510f",
                table: "ie_event_report_signals",
                columns: new[] { "tenant_id", "provider", "provider_target_scope", "provider_target_id", "external_signal_id" },
                unique: true,
                filter: "external_signal_id IS NOT NULL");
        }
    }
}

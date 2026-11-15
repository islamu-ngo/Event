using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddVisitorEnrollment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_moderation_case_evidence_moderation_cases_case_id",
                schema: "islamu_event",
                table: "moderation_case_evidence");

            migrationBuilder.DropCheckConstraint(
                name: "ck_local_credential_operation_kind",
                schema: "islamu_event",
                table: "local_identity_credential_operation");

            migrationBuilder.DropCheckConstraint(
                name: "ck_local_credential_operation_reset_metadata",
                schema: "islamu_event",
                table: "local_identity_credential_operation");

            migrationBuilder.DropCheckConstraint(
                name: "ck_local_credential_operation_timestamps",
                schema: "islamu_event",
                table: "local_identity_credential_operation");

            migrationBuilder.AlterColumn<Guid>(
                name: "verified_by_application_user_id",
                schema: "islamu_event",
                table: "local_identity_credential_operation",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<DateTime>(
                name: "verified_at",
                schema: "islamu_event",
                table: "local_identity_credential_operation",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AlterColumn<Guid>(
                name: "initiating_application_user_id",
                schema: "islamu_event",
                table: "local_identity_credential_operation",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "visitor_enrollment_operation_id",
                schema: "islamu_event",
                table: "local_identity_credential_operation",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "visitor_enrollment_operations",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    local_subject_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    personal_actor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    external_login_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    credential_operation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    credential_generation = table.Column<long>(type: "bigint", nullable: true),
                    tenant_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    intent_digest = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    stage = table.Column<int>(type: "int", nullable: false),
                    generation = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    expires_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    cleanup_completed_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_visitor_enrollment_operations", x => x.id);
                    table.CheckConstraint("ck_visitor_enrollment_cleanup", "cleanup_completed_at IS NULL OR (stage = 5 AND updated_at IS NOT NULL AND cleanup_completed_at >= created_at AND cleanup_completed_at <= updated_at)");
                    table.CheckConstraint("ck_visitor_enrollment_evidence", "(stage = 1 AND credential_generation IS NULL AND tenant_user_id IS NULL) OR (stage = 2 AND credential_generation IS NOT NULL AND credential_generation > 0 AND tenant_user_id IS NULL) OR (stage IN (3, 4) AND credential_generation IS NOT NULL AND credential_generation > 0 AND tenant_user_id IS NOT NULL) OR (stage = 5 AND tenant_user_id IS NULL AND (credential_generation IS NULL OR credential_generation > 0))");
                    table.CheckConstraint("ck_visitor_enrollment_generation", "generation > 0");
                    table.CheckConstraint("ck_visitor_enrollment_stage", "stage BETWEEN 1 AND 5");
                    table.CheckConstraint("ck_visitor_enrollment_timeline", "expires_at > created_at AND (updated_at IS NULL OR updated_at >= created_at)");
                    table.ForeignKey(
                        name: "fk_visitor_enrollment_operations_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "islamu_event",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_local_credential_operation_kind",
                schema: "islamu_event",
                table: "local_identity_credential_operation",
                sql: "kind BETWEEN 1 AND 3");

            migrationBuilder.AddCheckConstraint(
                name: "ck_local_credential_operation_origin",
                schema: "islamu_event",
                table: "local_identity_credential_operation",
                sql: "((kind IN (1, 2) AND initiating_application_user_id IS NOT NULL) OR (kind = 3 AND initiating_application_user_id IS NULL)) AND ((kind IN (1, 2) AND visitor_enrollment_operation_id IS NULL AND verified_by_application_user_id IS NOT NULL AND verified_at IS NOT NULL) OR (kind IN (2, 3) AND visitor_enrollment_operation_id IS NOT NULL AND verified_by_application_user_id IS NULL AND verified_at IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_local_credential_operation_reset_metadata",
                schema: "islamu_event",
                table: "local_identity_credential_operation",
                sql: "(kind IN (1, 3) AND previous_operation_id IS NULL AND previous_operation_concurrency_stamp IS NULL AND reset_reason IS NULL AND (kind <> 3 OR stage <> 2)) OR (kind = 2 AND previous_operation_id IS NOT NULL AND previous_operation_id <> id AND previous_operation_concurrency_stamp IS NOT NULL AND reset_reason IS NOT NULL AND TRIM(reset_reason) <> '' AND stage <> 1)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_local_credential_operation_timestamps",
                schema: "islamu_event",
                table: "local_identity_credential_operation",
                sql: "((kind = 1 AND verified_at IS NOT NULL AND verified_at >= created_at) OR (kind = 2 AND (verified_at IS NULL OR verified_at <= created_at)) OR (kind = 3 AND verified_at IS NULL)) AND (updated_at IS NULL OR updated_at >= created_at)");

            migrationBuilder.CreateIndex(
                name: "ix_visitor_enrollment_operations_credential_operation_id",
                schema: "islamu_event",
                table: "visitor_enrollment_operations",
                column: "credential_operation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_visitor_enrollment_operations_local_subject_id",
                schema: "islamu_event",
                table: "visitor_enrollment_operations",
                column: "local_subject_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_visitor_enrollment_operations_stage_expires_at_id",
                schema: "islamu_event",
                table: "visitor_enrollment_operations",
                columns: new[] { "stage", "expires_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_visitor_enrollment_operations_tenant_id",
                schema: "islamu_event",
                table: "visitor_enrollment_operations",
                column: "tenant_id");

            migrationBuilder.AddForeignKey(
                name: "fk_moderation_case_evidence_moderation_cases_case_id",
                schema: "islamu_event",
                table: "moderation_case_evidence",
                column: "case_id",
                principalSchema: "islamu_event",
                principalTable: "moderation_cases",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_moderation_case_evidence_moderation_cases_case_id",
                schema: "islamu_event",
                table: "moderation_case_evidence");

            migrationBuilder.DropTable(
                name: "visitor_enrollment_operations",
                schema: "islamu_event");

            migrationBuilder.DropCheckConstraint(
                name: "ck_local_credential_operation_kind",
                schema: "islamu_event",
                table: "local_identity_credential_operation");

            migrationBuilder.DropCheckConstraint(
                name: "ck_local_credential_operation_origin",
                schema: "islamu_event",
                table: "local_identity_credential_operation");

            migrationBuilder.DropCheckConstraint(
                name: "ck_local_credential_operation_reset_metadata",
                schema: "islamu_event",
                table: "local_identity_credential_operation");

            migrationBuilder.DropCheckConstraint(
                name: "ck_local_credential_operation_timestamps",
                schema: "islamu_event",
                table: "local_identity_credential_operation");

            migrationBuilder.DropColumn(
                name: "visitor_enrollment_operation_id",
                schema: "islamu_event",
                table: "local_identity_credential_operation");

            migrationBuilder.AlterColumn<Guid>(
                name: "verified_by_application_user_id",
                schema: "islamu_event",
                table: "local_identity_credential_operation",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "verified_at",
                schema: "islamu_event",
                table: "local_identity_credential_operation",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "initiating_application_user_id",
                schema: "islamu_event",
                table: "local_identity_credential_operation",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_local_credential_operation_kind",
                schema: "islamu_event",
                table: "local_identity_credential_operation",
                sql: "kind BETWEEN 1 AND 2");

            migrationBuilder.AddCheckConstraint(
                name: "ck_local_credential_operation_reset_metadata",
                schema: "islamu_event",
                table: "local_identity_credential_operation",
                sql: "(kind = 1 AND previous_operation_id IS NULL AND previous_operation_concurrency_stamp IS NULL AND reset_reason IS NULL) OR (kind = 2 AND previous_operation_id IS NOT NULL AND previous_operation_id <> id AND previous_operation_concurrency_stamp IS NOT NULL AND reset_reason IS NOT NULL AND TRIM(reset_reason) <> '' AND stage <> 1)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_local_credential_operation_timestamps",
                schema: "islamu_event",
                table: "local_identity_credential_operation",
                sql: "((kind = 1 AND verified_at >= created_at) OR (kind = 2 AND verified_at <= created_at)) AND (updated_at IS NULL OR updated_at >= created_at)");

            migrationBuilder.AddForeignKey(
                name: "fk_moderation_case_evidence_moderation_cases_case_id",
                schema: "islamu_event",
                table: "moderation_case_evidence",
                column: "case_id",
                principalSchema: "islamu_event",
                principalTable: "moderation_cases",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}

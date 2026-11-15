using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.MySql.Migrations.Identity
{
    /// <inheritdoc />
    public partial class AddLocalVisitorEnrollment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_local_credential_operation_kind",
                table: "ie_local_identity_credential_operation");

            migrationBuilder.DropCheckConstraint(
                name: "ck_local_credential_operation_reset_metadata",
                table: "ie_local_identity_credential_operation");

            migrationBuilder.DropCheckConstraint(
                name: "ck_local_credential_operation_timestamps",
                table: "ie_local_identity_credential_operation");

            migrationBuilder.AlterColumn<Guid>(
                name: "verified_by_application_user_id",
                table: "ie_local_identity_credential_operation",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci",
                oldClrType: typeof(Guid),
                oldType: "char(36)")
                .OldAnnotation("Relational:Collation", "ascii_general_ci");

            migrationBuilder.AlterColumn<DateTime>(
                name: "verified_at",
                table: "ie_local_identity_credential_operation",
                type: "datetime(6)",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime(6)");

            migrationBuilder.AlterColumn<Guid>(
                name: "initiating_application_user_id",
                table: "ie_local_identity_credential_operation",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci",
                oldClrType: typeof(Guid),
                oldType: "char(36)")
                .OldAnnotation("Relational:Collation", "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "visitor_enrollment_operation_id",
                table: "ie_local_identity_credential_operation",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddCheckConstraint(
                name: "ck_local_credential_operation_kind",
                table: "ie_local_identity_credential_operation",
                sql: "kind BETWEEN 1 AND 3");

            migrationBuilder.AddCheckConstraint(
                name: "ck_local_credential_operation_origin",
                table: "ie_local_identity_credential_operation",
                sql: "((kind IN (1, 2) AND initiating_application_user_id IS NOT NULL) OR (kind = 3 AND initiating_application_user_id IS NULL)) AND ((kind IN (1, 2) AND visitor_enrollment_operation_id IS NULL AND verified_by_application_user_id IS NOT NULL AND verified_at IS NOT NULL) OR (kind IN (2, 3) AND visitor_enrollment_operation_id IS NOT NULL AND verified_by_application_user_id IS NULL AND verified_at IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_local_credential_operation_reset_metadata",
                table: "ie_local_identity_credential_operation",
                sql: "(kind IN (1, 3) AND previous_operation_id IS NULL AND previous_operation_concurrency_stamp IS NULL AND reset_reason IS NULL AND (kind <> 3 OR stage <> 2)) OR (kind = 2 AND previous_operation_id IS NOT NULL AND previous_operation_id <> id AND previous_operation_concurrency_stamp IS NOT NULL AND reset_reason IS NOT NULL AND TRIM(reset_reason) <> '' AND stage <> 1)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_local_credential_operation_timestamps",
                table: "ie_local_identity_credential_operation",
                sql: "((kind = 1 AND verified_at IS NOT NULL AND verified_at >= created_at) OR (kind = 2 AND (verified_at IS NULL OR verified_at <= created_at)) OR (kind = 3 AND verified_at IS NULL)) AND (updated_at IS NULL OR updated_at >= created_at)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_local_credential_operation_kind",
                table: "ie_local_identity_credential_operation");

            migrationBuilder.DropCheckConstraint(
                name: "ck_local_credential_operation_origin",
                table: "ie_local_identity_credential_operation");

            migrationBuilder.DropCheckConstraint(
                name: "ck_local_credential_operation_reset_metadata",
                table: "ie_local_identity_credential_operation");

            migrationBuilder.DropCheckConstraint(
                name: "ck_local_credential_operation_timestamps",
                table: "ie_local_identity_credential_operation");

            migrationBuilder.DropColumn(
                name: "visitor_enrollment_operation_id",
                table: "ie_local_identity_credential_operation");

            migrationBuilder.AlterColumn<Guid>(
                name: "verified_by_application_user_id",
                table: "ie_local_identity_credential_operation",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci",
                oldClrType: typeof(Guid),
                oldType: "char(36)",
                oldNullable: true)
                .OldAnnotation("Relational:Collation", "ascii_general_ci");

            migrationBuilder.AlterColumn<DateTime>(
                name: "verified_at",
                table: "ie_local_identity_credential_operation",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "datetime(6)",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "initiating_application_user_id",
                table: "ie_local_identity_credential_operation",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci",
                oldClrType: typeof(Guid),
                oldType: "char(36)",
                oldNullable: true)
                .OldAnnotation("Relational:Collation", "ascii_general_ci");

            migrationBuilder.AddCheckConstraint(
                name: "ck_local_credential_operation_kind",
                table: "ie_local_identity_credential_operation",
                sql: "kind BETWEEN 1 AND 2");

            migrationBuilder.AddCheckConstraint(
                name: "ck_local_credential_operation_reset_metadata",
                table: "ie_local_identity_credential_operation",
                sql: "(kind = 1 AND previous_operation_id IS NULL AND previous_operation_concurrency_stamp IS NULL AND reset_reason IS NULL) OR (kind = 2 AND previous_operation_id IS NOT NULL AND previous_operation_id <> id AND previous_operation_concurrency_stamp IS NOT NULL AND reset_reason IS NOT NULL AND TRIM(reset_reason) <> '' AND stage <> 1)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_local_credential_operation_timestamps",
                table: "ie_local_identity_credential_operation",
                sql: "((kind = 1 AND verified_at >= created_at) OR (kind = 2 AND verified_at <= created_at)) AND (updated_at IS NULL OR updated_at >= created_at)");
        }
    }
}

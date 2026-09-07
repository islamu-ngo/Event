using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.MySql.Migrations.Identity
{
    /// <inheritdoc />
    public partial class LocalCredentialOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ie_local_identity_credential_operation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    kind = table.Column<int>(type: "int", nullable: false),
                    stage = table.Column<int>(type: "int", nullable: false),
                    initiating_application_user_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    local_subject_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    personal_actor_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    external_login_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    previous_operation_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    previous_operation_concurrency_stamp = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    reset_reason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    verified_by_application_user_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    verified_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    concurrency_stamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_local_identity_credential_operation", x => x.id);
                    table.CheckConstraint("ck_local_credential_operation_kind", "kind BETWEEN 1 AND 2");
                    table.CheckConstraint("ck_local_credential_operation_reset_metadata", "(kind = 1 AND previous_operation_id IS NULL AND previous_operation_concurrency_stamp IS NULL AND reset_reason IS NULL) OR (kind = 2 AND previous_operation_id IS NOT NULL AND previous_operation_id <> id AND previous_operation_concurrency_stamp IS NOT NULL AND reset_reason IS NOT NULL AND TRIM(reset_reason) <> '' AND stage <> 1)");
                    table.CheckConstraint("ck_local_credential_operation_stage", "stage BETWEEN 1 AND 5");
                    table.CheckConstraint("ck_local_credential_operation_timestamps", "((kind = 1 AND verified_at >= created_at) OR (kind = 2 AND verified_at <= created_at)) AND (updated_at IS NULL OR updated_at >= created_at)");
                    table.ForeignKey(
                        name: "fk_ie_local_identity_credential_operation_ie_local_iden_1288010b",
                        column: x => x.local_subject_id,
                        principalTable: "ie_local_identity_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_local_identity_credential_operation_local_subject_id",
                table: "ie_local_identity_credential_operation",
                column: "local_subject_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ie_local_identity_credential_operation");
        }
    }
}

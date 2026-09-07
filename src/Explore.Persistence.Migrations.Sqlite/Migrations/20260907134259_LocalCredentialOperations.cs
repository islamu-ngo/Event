using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.Sqlite.Migrations
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
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    kind = table.Column<int>(type: "INTEGER", nullable: false),
                    stage = table.Column<int>(type: "INTEGER", nullable: false),
                    initiating_application_user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    local_subject_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    personal_actor_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    external_login_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    previous_operation_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    previous_operation_concurrency_stamp = table.Column<Guid>(type: "TEXT", nullable: true),
                    reset_reason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    verified_by_application_user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    verified_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    concurrency_stamp = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_local_identity_credential_operation", x => x.id);
                    table.CheckConstraint("ck_local_credential_operation_kind", "kind BETWEEN 1 AND 2");
                    table.CheckConstraint("ck_local_credential_operation_reset_metadata", "(kind = 1 AND previous_operation_id IS NULL AND previous_operation_concurrency_stamp IS NULL AND reset_reason IS NULL) OR (kind = 2 AND previous_operation_id IS NOT NULL AND previous_operation_id <> id AND previous_operation_concurrency_stamp IS NOT NULL AND reset_reason IS NOT NULL AND TRIM(reset_reason) <> '' AND stage <> 1)");
                    table.CheckConstraint("ck_local_credential_operation_stage", "stage BETWEEN 1 AND 5");
                    table.CheckConstraint("ck_local_credential_operation_timestamps", "((kind = 1 AND verified_at >= created_at) OR (kind = 2 AND verified_at <= created_at)) AND (updated_at IS NULL OR updated_at >= created_at)");
                    table.ForeignKey(
                        name: "fk_local_identity_credential_operation_local_identity_users_local_subject_id",
                        column: x => x.local_subject_id,
                        principalTable: "ie_local_identity_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

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

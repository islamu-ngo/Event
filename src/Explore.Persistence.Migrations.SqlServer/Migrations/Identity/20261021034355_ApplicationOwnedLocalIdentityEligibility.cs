using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.SqlServer.Migrations.Identity
{
    /// <inheritdoc />
    public partial class ApplicationOwnedLocalIdentityEligibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "local_identity_eligibilities",
                schema: "islamu_identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    local_subject_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    current_operation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    personal_actor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    external_login_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    state = table.Column<int>(type: "int", nullable: false),
                    generation = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    lockout_until_utc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_local_identity_eligibilities", x => x.id);
                    table.CheckConstraint("ck_local_identity_eligibility_audit", "updated_at IS NULL OR updated_at >= created_at");
                    table.CheckConstraint("ck_local_identity_eligibility_generation", "generation > 0");
                    table.CheckConstraint("ck_local_identity_eligibility_state", "state BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "fk_local_identity_eligibilities_local_identity_credential_operation_current_operation_id",
                        column: x => x.current_operation_id,
                        principalSchema: "islamu_identity",
                        principalTable: "local_identity_credential_operation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_local_identity_eligibilities_local_identity_users_local_subject_id",
                        column: x => x.local_subject_id,
                        principalSchema: "islamu_identity",
                        principalTable: "local_identity_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_local_identity_eligibilities_current_operation_id",
                schema: "islamu_identity",
                table: "local_identity_eligibilities",
                column: "current_operation_id");

            migrationBuilder.CreateIndex(
                name: "ix_local_identity_eligibilities_local_subject_id",
                schema: "islamu_identity",
                table: "local_identity_eligibilities",
                column: "local_subject_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "local_identity_eligibilities",
                schema: "islamu_identity");
        }
    }
}

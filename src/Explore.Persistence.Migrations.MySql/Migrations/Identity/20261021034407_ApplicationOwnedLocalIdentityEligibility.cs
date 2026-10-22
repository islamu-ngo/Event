using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.MySql.Migrations.Identity
{
    /// <inheritdoc />
    public partial class ApplicationOwnedLocalIdentityEligibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ie_local_identity_eligibilities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    local_subject_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    current_operation_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    personal_actor_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    external_login_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    state = table.Column<int>(type: "int", nullable: false),
                    generation = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    lockout_until_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_local_identity_eligibilities", x => x.id);
                    table.CheckConstraint("ck_local_identity_eligibility_audit", "updated_at IS NULL OR updated_at >= created_at");
                    table.CheckConstraint("ck_local_identity_eligibility_generation", "generation > 0");
                    table.CheckConstraint("ck_local_identity_eligibility_state", "state BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "fk_ie_local_identity_eligibilities_ie_local_identity_cr_1f0b67f3",
                        column: x => x.current_operation_id,
                        principalTable: "ie_local_identity_credential_operation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ie_local_identity_eligibilities_ie_local_identity_us_20823c25",
                        column: x => x.local_subject_id,
                        principalTable: "ie_local_identity_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_local_identity_eligibilities_current_operation_id",
                table: "ie_local_identity_eligibilities",
                column: "current_operation_id");

            migrationBuilder.CreateIndex(
                name: "ix_local_identity_eligibilities_local_subject_id",
                table: "ie_local_identity_eligibilities",
                column: "local_subject_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ie_local_identity_eligibilities");
        }
    }
}

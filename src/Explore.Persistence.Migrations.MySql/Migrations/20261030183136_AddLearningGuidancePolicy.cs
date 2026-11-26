using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddLearningGuidancePolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ie_learning_requirement_acknowledgements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    user_learning_state_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    requirement_key = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    material_version = table.Column<int>(type: "int", nullable: false),
                    acknowledged_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    policy_source_stamp = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    last_intent_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    last_intent_fingerprint = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_learning_requirement_acknowledgements", x => x.id);
                    table.ForeignKey(
                        name: "fk_ie_learning_requirement_acknowledgements_ie_user_lea_43186a25",
                        columns: x => new { x.tenant_id, x.user_learning_state_id },
                        principalTable: "ie_user_learning_states",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_ie_learning_requirement_acknowledgements_tenant_id_u_56d61a79",
                table: "ie_learning_requirement_acknowledgements",
                columns: new[] { "tenant_id", "user_learning_state_id", "requirement_key", "material_version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ie_learning_requirement_acknowledgements");
        }
    }
}

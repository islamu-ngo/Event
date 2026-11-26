using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.Sqlite.Migrations
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
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    user_learning_state_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    requirement_key = table.Column<string>(type: "TEXT", unicode: false, maxLength: 32, nullable: false),
                    material_version = table.Column<int>(type: "INTEGER", nullable: false),
                    acknowledged_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    policy_source_stamp = table.Column<string>(type: "TEXT", unicode: false, maxLength: 64, nullable: false),
                    last_intent_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    last_intent_fingerprint = table.Column<string>(type: "TEXT", unicode: false, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_learning_requirement_acknowledgements", x => x.id);
                    table.ForeignKey(
                        name: "fk_learning_requirement_acknowledgements_user_learning_states_tenant_id_user_learning_state_id",
                        columns: x => new { x.tenant_id, x.user_learning_state_id },
                        principalTable: "ie_user_learning_states",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_learning_requirement_acknowledgements_tenant_id_user_learning_state_id_requirement_key_material_version",
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

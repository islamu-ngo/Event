using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddLearningGuidancePolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "learning_requirement_acknowledgements",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_learning_state_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    requirement_key = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    material_version = table.Column<int>(type: "int", nullable: false),
                    acknowledged_at_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    policy_source_stamp = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    last_intent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    last_intent_fingerprint = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_learning_requirement_acknowledgements", x => x.id);
                    table.ForeignKey(
                        name: "fk_learning_requirement_acknowledgements_user_learning_states_tenant_id_user_learning_state_id",
                        columns: x => new { x.tenant_id, x.user_learning_state_id },
                        principalSchema: "islamu_event",
                        principalTable: "user_learning_states",
                        principalColumns: new[] { "tenant_id", "id" });
                });

            migrationBuilder.CreateIndex(
                name: "ix_learning_requirement_acknowledgements_tenant_id_user_learning_state_id_requirement_key_material_version",
                schema: "islamu_event",
                table: "learning_requirement_acknowledgements",
                columns: new[] { "tenant_id", "user_learning_state_id", "requirement_key", "material_version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "learning_requirement_acknowledgements",
                schema: "islamu_event");
        }
    }
}

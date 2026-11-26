using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations
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
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_learning_state_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requirement_key = table.Column<string>(type: "character varying(32)", unicode: false, maxLength: 32, nullable: false),
                    material_version = table.Column<int>(type: "integer", nullable: false),
                    acknowledged_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    policy_source_stamp = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false),
                    last_intent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_intent_fingerprint = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_learning_requirement_acknowledgements", x => x.id);
                    table.ForeignKey(
                        name: "fk_learning_requirement_acknowledgements_user_lear_b86e31bfef9c",
                        columns: x => new { x.tenant_id, x.user_learning_state_id },
                        principalSchema: "islamu_event",
                        principalTable: "user_learning_states",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_learning_requirement_acknowledgements_tenant_id_27ecf8dd23ac",
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

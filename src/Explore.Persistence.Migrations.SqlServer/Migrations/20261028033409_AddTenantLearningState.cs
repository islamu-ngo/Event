using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantLearningState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_moderation_case_evidence_moderation_cases_case_id",
                schema: "islamu_event",
                table: "moderation_case_evidence");

            migrationBuilder.CreateTable(
                name: "tenant_learning_spaces",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    pack_key = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    pack_version = table.Column<int>(type: "int", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    is_removed = table.Column<bool>(type: "bit", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    removed_at_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_learning_spaces", x => x.id);
                    table.ForeignKey(
                        name: "fk_tenant_learning_spaces_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "islamu_event",
                        principalTable: "tenants",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "user_learning_states",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    suppress_automatic_tours = table.Column<bool>(type: "bit", nullable: false),
                    is_hidden = table.Column<bool>(type: "bit", nullable: false),
                    is_pinned = table.Column<bool>(type: "bit", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    active_claim_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    active_tour_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    active_context_key = table.Column<string>(type: "varchar(42)", unicode: false, maxLength: 42, nullable: true),
                    active_event_context_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    claim_expires_at_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    last_intent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    last_intent_fingerprint = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_learning_states", x => x.id);
                    table.UniqueConstraint("ak_user_learning_states_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_user_learning_states_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "islamu_event",
                        principalTable: "tenants",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "learning_practice_states",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_learning_state_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    context_key = table.Column<string>(type: "varchar(42)", unicode: false, maxLength: 42, nullable: false),
                    recipe_key = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    recipe_version = table.Column<int>(type: "int", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    last_action = table.Column<int>(type: "int", nullable: false),
                    choice = table.Column<int>(type: "int", nullable: false),
                    step = table.Column<int>(type: "int", nullable: false),
                    value = table.Column<int>(type: "int", nullable: false),
                    is_confirmed = table.Column<bool>(type: "bit", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    last_intent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    last_intent_fingerprint = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_learning_practice_states", x => x.id);
                    table.ForeignKey(
                        name: "fk_learning_practice_states_user_learning_states_tenant_id_user_learning_state_id",
                        columns: x => new { x.tenant_id, x.user_learning_state_id },
                        principalSchema: "islamu_event",
                        principalTable: "user_learning_states",
                        principalColumns: new[] { "tenant_id", "id" });
                });

            migrationBuilder.CreateTable(
                name: "learning_tour_progress",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_learning_state_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    family = table.Column<int>(type: "int", nullable: false),
                    material_version = table.Column<int>(type: "int", nullable: false),
                    outcome = table.Column<int>(type: "int", nullable: false),
                    step = table.Column<int>(type: "int", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    last_intent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    last_intent_fingerprint = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_learning_tour_progress", x => x.id);
                    table.ForeignKey(
                        name: "fk_learning_tour_progress_user_learning_states_tenant_id_user_learning_state_id",
                        columns: x => new { x.tenant_id, x.user_learning_state_id },
                        principalSchema: "islamu_event",
                        principalTable: "user_learning_states",
                        principalColumns: new[] { "tenant_id", "id" });
                });

            migrationBuilder.CreateIndex(
                name: "ix_learning_practice_states_tenant_id_user_learning_state_id_context_key_recipe_key",
                schema: "islamu_event",
                table: "learning_practice_states",
                columns: new[] { "tenant_id", "user_learning_state_id", "context_key", "recipe_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_learning_tour_progress_tenant_id_user_learning_state_id_family_material_version",
                schema: "islamu_event",
                table: "learning_tour_progress",
                columns: new[] { "tenant_id", "user_learning_state_id", "family", "material_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_learning_spaces_tenant_id",
                schema: "islamu_event",
                table: "tenant_learning_spaces",
                column: "tenant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_learning_states_tenant_id_user_id",
                schema: "islamu_event",
                table: "user_learning_states",
                columns: new[] { "tenant_id", "user_id" },
                unique: true);

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
                name: "learning_practice_states",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "learning_tour_progress",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "tenant_learning_spaces",
                schema: "islamu_event");

            migrationBuilder.DropTable(
                name: "user_learning_states",
                schema: "islamu_event");

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

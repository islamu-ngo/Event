using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantLearningState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ie_tenant_learning_spaces",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    pack_key = table.Column<string>(type: "TEXT", unicode: false, maxLength: 32, nullable: false),
                    pack_version = table.Column<int>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    is_removed = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    removed_at_utc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_tenant_learning_spaces", x => x.id);
                    table.ForeignKey(
                        name: "fk_tenant_learning_spaces_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ie_user_learning_states",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    suppress_automatic_tours = table.Column<bool>(type: "INTEGER", nullable: false),
                    is_hidden = table.Column<bool>(type: "INTEGER", nullable: false),
                    is_pinned = table.Column<bool>(type: "INTEGER", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    active_claim_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    active_tour_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    active_context_key = table.Column<string>(type: "TEXT", unicode: false, maxLength: 42, nullable: true),
                    active_event_context_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    claim_expires_at_utc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    last_intent_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    last_intent_fingerprint = table.Column<string>(type: "TEXT", unicode: false, maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_user_learning_states", x => x.id);
                    table.UniqueConstraint("ak_user_learning_states_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_user_learning_states_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "ie_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ie_learning_practice_states",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    user_learning_state_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    context_key = table.Column<string>(type: "TEXT", unicode: false, maxLength: 42, nullable: false),
                    recipe_key = table.Column<string>(type: "TEXT", unicode: false, maxLength: 32, nullable: false),
                    recipe_version = table.Column<int>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    last_action = table.Column<int>(type: "INTEGER", nullable: false),
                    choice = table.Column<int>(type: "INTEGER", nullable: false),
                    step = table.Column<int>(type: "INTEGER", nullable: false),
                    value = table.Column<int>(type: "INTEGER", nullable: false),
                    is_confirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    last_intent_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    last_intent_fingerprint = table.Column<string>(type: "TEXT", unicode: false, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_learning_practice_states", x => x.id);
                    table.ForeignKey(
                        name: "fk_learning_practice_states_user_learning_states_tenant_id_user_learning_state_id",
                        columns: x => new { x.tenant_id, x.user_learning_state_id },
                        principalTable: "ie_user_learning_states",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ie_learning_tour_progress",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    user_learning_state_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    family = table.Column<int>(type: "INTEGER", nullable: false),
                    material_version = table.Column<int>(type: "INTEGER", nullable: false),
                    outcome = table.Column<int>(type: "INTEGER", nullable: false),
                    step = table.Column<int>(type: "INTEGER", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    last_intent_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    last_intent_fingerprint = table.Column<string>(type: "TEXT", unicode: false, maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_learning_tour_progress", x => x.id);
                    table.ForeignKey(
                        name: "fk_learning_tour_progress_user_learning_states_tenant_id_user_learning_state_id",
                        columns: x => new { x.tenant_id, x.user_learning_state_id },
                        principalTable: "ie_user_learning_states",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_learning_practice_states_tenant_id_user_learning_state_id_context_key_recipe_key",
                table: "ie_learning_practice_states",
                columns: new[] { "tenant_id", "user_learning_state_id", "context_key", "recipe_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_learning_tour_progress_tenant_id_user_learning_state_id_family_material_version",
                table: "ie_learning_tour_progress",
                columns: new[] { "tenant_id", "user_learning_state_id", "family", "material_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_learning_spaces_tenant_id",
                table: "ie_tenant_learning_spaces",
                column: "tenant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_learning_states_tenant_id_user_id",
                table: "ie_user_learning_states",
                columns: new[] { "tenant_id", "user_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ie_learning_practice_states");

            migrationBuilder.DropTable(
                name: "ie_learning_tour_progress");

            migrationBuilder.DropTable(
                name: "ie_tenant_learning_spaces");

            migrationBuilder.DropTable(
                name: "ie_user_learning_states");
        }
    }
}

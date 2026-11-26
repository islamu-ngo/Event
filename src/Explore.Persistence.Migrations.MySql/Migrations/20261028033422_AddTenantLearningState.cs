using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.MySql.Migrations
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
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    pack_key = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    pack_version = table.Column<int>(type: "int", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    is_removed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    removed_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_user_learning_states",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    user_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    suppress_automatic_tours = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    is_hidden = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    is_pinned = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    active_claim_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    active_tour_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    active_context_key = table.Column<string>(type: "varchar(42)", unicode: false, maxLength: 42, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    active_event_context_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    claim_expires_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    last_intent_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    last_intent_fingerprint = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_learning_practice_states",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    user_learning_state_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    context_key = table.Column<string>(type: "varchar(42)", unicode: false, maxLength: 42, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    recipe_key = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    recipe_version = table.Column<int>(type: "int", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    last_action = table.Column<int>(type: "int", nullable: false),
                    choice = table.Column<int>(type: "int", nullable: false),
                    step = table.Column<int>(type: "int", nullable: false),
                    value = table.Column<int>(type: "int", nullable: false),
                    is_confirmed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    last_intent_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    last_intent_fingerprint = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_learning_practice_states", x => x.id);
                    table.ForeignKey(
                        name: "fk_ie_learning_practice_states_ie_user_learning_states__4c499dd6",
                        columns: x => new { x.tenant_id, x.user_learning_state_id },
                        principalTable: "ie_user_learning_states",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ie_learning_tour_progress",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    user_learning_state_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    family = table.Column<int>(type: "int", nullable: false),
                    material_version = table.Column<int>(type: "int", nullable: false),
                    outcome = table.Column<int>(type: "int", nullable: false),
                    step = table.Column<int>(type: "int", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    last_intent_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    last_intent_fingerprint = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_learning_tour_progress", x => x.id);
                    table.ForeignKey(
                        name: "fk_ie_learning_tour_progress_ie_user_learning_states_te_4a50d991",
                        columns: x => new { x.tenant_id, x.user_learning_state_id },
                        principalTable: "ie_user_learning_states",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_ie_learning_practice_states_tenant_id_user_learning__1d26c971",
                table: "ie_learning_practice_states",
                columns: new[] { "tenant_id", "user_learning_state_id", "context_key", "recipe_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ie_learning_tour_progress_tenant_id_user_learning_st_93207e6a",
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

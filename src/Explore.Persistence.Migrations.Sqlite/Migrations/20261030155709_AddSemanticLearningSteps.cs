using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddSemanticLearningSteps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "step",
                table: "ie_learning_tour_progress");

            migrationBuilder.AddColumn<string>(
                name: "active_plan_fingerprint",
                table: "ie_user_learning_states",
                type: "TEXT",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "active_policy_source_stamp",
                table: "ie_user_learning_states",
                type: "TEXT",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_step_key",
                table: "ie_learning_tour_progress",
                type: "TEXT",
                unicode: false,
                maxLength: 96,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "snoozed_until_utc",
                table: "ie_learning_tour_progress",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_learning_tour_progress_tenant_id_id",
                table: "ie_learning_tour_progress",
                columns: new[] { "tenant_id", "id" });

            migrationBuilder.CreateTable(
                name: "ie_learning_tour_step_progress",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tenant_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tour_progress_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    step_key = table.Column<string>(type: "TEXT", unicode: false, maxLength: 96, nullable: false),
                    step_version = table.Column<int>(type: "INTEGER", nullable: false),
                    completed_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_learning_tour_step_progress", x => x.id);
                    table.ForeignKey(
                        name: "fk_learning_tour_step_progress_learning_tour_progress_tenant_id_tour_progress_id",
                        columns: x => new { x.tenant_id, x.tour_progress_id },
                        principalTable: "ie_learning_tour_progress",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_learning_tour_step_progress_tenant_id_tour_progress_id_step_key_step_version",
                table: "ie_learning_tour_step_progress",
                columns: new[] { "tenant_id", "tour_progress_id", "step_key", "step_version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ie_learning_tour_step_progress");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_learning_tour_progress_tenant_id_id",
                table: "ie_learning_tour_progress");

            migrationBuilder.DropColumn(
                name: "active_plan_fingerprint",
                table: "ie_user_learning_states");

            migrationBuilder.DropColumn(
                name: "active_policy_source_stamp",
                table: "ie_user_learning_states");

            migrationBuilder.DropColumn(
                name: "last_step_key",
                table: "ie_learning_tour_progress");

            migrationBuilder.DropColumn(
                name: "snoozed_until_utc",
                table: "ie_learning_tour_progress");

            migrationBuilder.AddColumn<int>(
                name: "step",
                table: "ie_learning_tour_progress",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }
    }
}

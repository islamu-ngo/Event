using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.MySql.Migrations
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
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "active_policy_source_stamp",
                table: "ie_user_learning_states",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "last_step_key",
                table: "ie_learning_tour_progress",
                type: "varchar(96)",
                unicode: false,
                maxLength: 96,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "snoozed_until_utc",
                table: "ie_learning_tour_progress",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_learning_tour_progress_tenant_id_id",
                table: "ie_learning_tour_progress",
                columns: new[] { "tenant_id", "id" });

            migrationBuilder.CreateTable(
                name: "ie_learning_tour_step_progress",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tour_progress_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    step_key = table.Column<string>(type: "varchar(96)", unicode: false, maxLength: 96, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    step_version = table.Column<int>(type: "int", nullable: false),
                    completed_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_learning_tour_step_progress", x => x.id);
                    table.ForeignKey(
                        name: "fk_ie_learning_tour_step_progress_ie_learning_tour_prog_a4dfcac4",
                        columns: x => new { x.tenant_id, x.tour_progress_id },
                        principalTable: "ie_learning_tour_progress",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_ie_learning_tour_step_progress_tenant_id_tour_progre_060d60a2",
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
                type: "int",
                nullable: false,
                defaultValue: 0);
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddSemanticLearningSteps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "step",
                schema: "islamu_event",
                table: "learning_tour_progress");

            migrationBuilder.AddColumn<string>(
                name: "active_plan_fingerprint",
                schema: "islamu_event",
                table: "user_learning_states",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "active_policy_source_stamp",
                schema: "islamu_event",
                table: "user_learning_states",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_step_key",
                schema: "islamu_event",
                table: "learning_tour_progress",
                type: "varchar(96)",
                unicode: false,
                maxLength: 96,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "snoozed_until_utc",
                schema: "islamu_event",
                table: "learning_tour_progress",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_learning_tour_progress_tenant_id_id",
                schema: "islamu_event",
                table: "learning_tour_progress",
                columns: new[] { "tenant_id", "id" });

            migrationBuilder.CreateTable(
                name: "learning_tour_step_progress",
                schema: "islamu_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tour_progress_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    step_key = table.Column<string>(type: "varchar(96)", unicode: false, maxLength: 96, nullable: false),
                    step_version = table.Column<int>(type: "int", nullable: false),
                    completed_at_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_learning_tour_step_progress", x => x.id);
                    table.ForeignKey(
                        name: "fk_learning_tour_step_progress_learning_tour_progress_tenant_id_tour_progress_id",
                        columns: x => new { x.tenant_id, x.tour_progress_id },
                        principalSchema: "islamu_event",
                        principalTable: "learning_tour_progress",
                        principalColumns: new[] { "tenant_id", "id" });
                });

            migrationBuilder.CreateIndex(
                name: "ix_learning_tour_step_progress_tenant_id_tour_progress_id_step_key_step_version",
                schema: "islamu_event",
                table: "learning_tour_step_progress",
                columns: new[] { "tenant_id", "tour_progress_id", "step_key", "step_version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "learning_tour_step_progress",
                schema: "islamu_event");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_learning_tour_progress_tenant_id_id",
                schema: "islamu_event",
                table: "learning_tour_progress");

            migrationBuilder.DropColumn(
                name: "active_plan_fingerprint",
                schema: "islamu_event",
                table: "user_learning_states");

            migrationBuilder.DropColumn(
                name: "active_policy_source_stamp",
                schema: "islamu_event",
                table: "user_learning_states");

            migrationBuilder.DropColumn(
                name: "last_step_key",
                schema: "islamu_event",
                table: "learning_tour_progress");

            migrationBuilder.DropColumn(
                name: "snoozed_until_utc",
                schema: "islamu_event",
                table: "learning_tour_progress");

            migrationBuilder.AddColumn<int>(
                name: "step",
                schema: "islamu_event",
                table: "learning_tour_progress",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }
    }
}

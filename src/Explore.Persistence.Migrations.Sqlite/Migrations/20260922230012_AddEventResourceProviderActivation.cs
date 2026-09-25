using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddEventResourceProviderActivation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ie_event_resource_provider_activations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    epoch = table.Column<long>(type: "INTEGER", nullable: false),
                    current_operation_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    state = table.Column<int>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: true),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    updated_by = table.Column<Guid>(type: "TEXT", nullable: true),
                    concurrency_stamp = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_event_resource_provider_activations", x => x.id);
                    table.CheckConstraint("ck_event_resource_provider_activation_epoch", "epoch >= 0");
                    table.CheckConstraint("ck_event_resource_provider_activation_owner", "(epoch = 0 AND state = 3 AND current_operation_id = '00000000-0000-0000-0000-000000000000') OR (epoch > 0 AND current_operation_id <> '00000000-0000-0000-0000-000000000000')");
                    table.CheckConstraint("ck_event_resource_provider_activation_state", "state BETWEEN 1 AND 3");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ie_event_resource_provider_activations");
        }
    }
}

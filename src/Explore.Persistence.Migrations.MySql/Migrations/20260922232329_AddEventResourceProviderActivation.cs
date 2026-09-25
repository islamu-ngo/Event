using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.MySql.Migrations
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
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    epoch = table.Column<long>(type: "bigint", nullable: false),
                    current_operation_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    state = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    updated_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    concurrency_stamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ie_event_resource_provider_activations", x => x.id);
                    table.CheckConstraint("ck_event_resource_provider_activation_epoch", "epoch >= 0");
                    table.CheckConstraint("ck_event_resource_provider_activation_owner", "(epoch = 0 AND state = 3 AND current_operation_id = '00000000-0000-0000-0000-000000000000') OR (epoch > 0 AND current_operation_id <> '00000000-0000-0000-0000-000000000000')");
                    table.CheckConstraint("ck_event_resource_provider_activation_state", "state BETWEEN 1 AND 3");
                })
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ie_event_resource_provider_activations");
        }
    }
}

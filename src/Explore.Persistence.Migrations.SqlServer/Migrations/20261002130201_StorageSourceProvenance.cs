using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class StorageSourceProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "uri",
                schema: "islamu_event",
                table: "storage_objects");

            migrationBuilder.AddColumn<string>(
                name: "source_uri",
                schema: "islamu_event",
                table: "storage_objects",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "source_uri",
                schema: "islamu_event",
                table: "storage_objects");

            migrationBuilder.AddColumn<string>(
                name: "uri",
                schema: "islamu_event",
                table: "storage_objects",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");
        }
    }
}

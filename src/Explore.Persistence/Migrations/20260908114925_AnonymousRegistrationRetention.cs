using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AnonymousRegistrationRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "registration_content_retention_until_utc",
                schema: "islamu_event",
                table: "storage_objects",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "anonymous_pii_retention_until_utc",
                schema: "islamu_event",
                table: "registration_orders",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "registration_content_retention_until_utc",
                schema: "islamu_event",
                table: "storage_objects");

            migrationBuilder.DropColumn(
                name: "anonymous_pii_retention_until_utc",
                schema: "islamu_event",
                table: "registration_orders");
        }
    }
}

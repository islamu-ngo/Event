using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AnonymousRegistrationRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "registration_content_retention_until_utc",
                table: "ie_storage_objects",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "anonymous_pii_retention_until_utc",
                table: "ie_registration_orders",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "registration_content_retention_until_utc",
                table: "ie_storage_objects");

            migrationBuilder.DropColumn(
                name: "anonymous_pii_retention_until_utc",
                table: "ie_registration_orders");
        }
    }
}

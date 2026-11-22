using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.PrivacyErasureAuthority.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class PrepareEmbeddedAuthorityRecoveryCoordinates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "recovery_generation",
                table: "ie_authority_counter",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<long>(
                name: "revision",
                table: "ie_authority_counter",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<Guid>(
                name: "store_id",
                table: "ie_authority_counter",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "recovery_generation",
                table: "ie_authority_counter");

            migrationBuilder.DropColumn(
                name: "revision",
                table: "ie_authority_counter");

            migrationBuilder.DropColumn(
                name: "store_id",
                table: "ie_authority_counter");
        }
    }
}

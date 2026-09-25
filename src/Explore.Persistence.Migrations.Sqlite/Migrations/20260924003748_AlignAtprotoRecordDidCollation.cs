using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Explore.Persistence.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AlignAtprotoRecordDidCollation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "did",
                table: "ie_atproto_records",
                type: "TEXT",
                maxLength: 255,
                nullable: false,
                collation: "BINARY",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 255);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "did",
                table: "ie_atproto_records",
                type: "TEXT",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 255,
                oldCollation: "BINARY");
        }
    }
}
